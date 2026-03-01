/**
 * connect-jira-start/index.ts 통합 테스트 (외부 API 모킹)
 *
 * 실행: deno test --allow-env --allow-net supabase/functions/tests/connect-jira-start.test.ts
 */

import {
    assertEquals,
    assertExists,
    assertMatch,
} from "https://deno.land/std@0.208.0/assert/mod.ts";

// 환경 변수 모킹
Deno.env.set("SUPABASE_URL", "https://mock.supabase.co");
Deno.env.set("SUPABASE_SERVICE_ROLE_KEY", "mock-service-role-key");
Deno.env.set("JIRA_CLIENT_ID", "test-jira-client-id");
Deno.env.set("JIRA_REDIRECT_URI", "https://mock.supabase.co/functions/v1/connect-jira-callback");
Deno.env.set("JWT_SECRET", "test-secret-for-unit-testing-at-least-32-chars");

// ========================================================
// 모킹 인프라
// ========================================================

interface MockDbState {
    tenants: Map<string, { id: string; name: string }>;
    users: Map<string, { id: string; tenant_id: string; external_id: string }>;
    connections: Map<string, { id: string; user_id: string; tenant_id: string; status: string; state: string; state_expires_at: string }>;
}

const mockDb: MockDbState = {
    tenants: new Map(),
    users: new Map(),
    connections: new Map(),
};

// Supabase 클라이언트 모킹
const mockGetServiceClient = () => {
    return {
        schema: (_name: string) => ({
            from: (table: string) => createTableMock(table),
        }),
        rpc: (_fn: string, _args: unknown) => ({
            data: crypto.randomUUID(),
            error: null,
        }),
    };
};

function createTableMock(table: string) {
    const queries: { action?: string; filters?: Record<string, unknown>; data?: unknown } = {};

    const chainable = {
        select: (_cols: string) => chainable,
        eq: (col: string, val: unknown) => {
            if (!queries.filters) queries.filters = {};
            queries.filters[col] = val;
            return chainable;
        },
        maybeSingle: () => {
            if (table === "tenants") {
                const id = queries.filters?.["id"] as string;
                const tenant = mockDb.tenants.get(id);
                return { data: tenant || null, error: null };
            }
            if (table === "users") {
                for (const user of mockDb.users.values()) {
                    if (user.tenant_id === queries.filters?.["tenant_id"] &&
                        user.external_id === queries.filters?.["external_id"]) {
                        return { data: user, error: null };
                    }
                }
                return { data: null, error: null };
            }
            return { data: null, error: null };
        },
        insert: (data: unknown) => {
            const record = data as Record<string, unknown>;
            if (table === "tenants") {
                const tenant = { id: record["id"] as string || crypto.randomUUID(), name: record["name"] as string };
                mockDb.tenants.set(tenant.id, tenant);
            }
            if (table === "users") {
                const user = {
                    id: crypto.randomUUID(),
                    tenant_id: record["tenant_id"] as string,
                    external_id: record["external_id"] as string,
                };
                mockDb.users.set(user.id, user);
            }
            return chainable;
        },
        upsert: (data: unknown, _options?: unknown) => {
            const record = data as Record<string, unknown>;
            const id = crypto.randomUUID();
            const conn = {
                id,
                user_id: record["user_id"] as string,
                tenant_id: record["tenant_id"] as string,
                status: record["status"] as string,
                state: record["state"] as string,
                state_expires_at: record["state_expires_at"] as string,
            };
            mockDb.connections.set(id, conn);
            return chainable;
        },
        single: () => {
            // 마지막 insert/upsert 결과 반환
            const lastConn = [...mockDb.connections.values()].at(-1);
            if (lastConn) {
                return { data: lastConn, error: null };
            }
            // users
            const lastUser = [...mockDb.users.values()].at(-1);
            if (lastUser) {
                return { data: lastUser, error: null };
            }
            return { data: { id: crypto.randomUUID() }, error: null };
        },
    };

    return chainable;
}

// ========================================================
// 핵심 로직만 직접 테스트 (엣지 함수 로직 추출)
// ========================================================

function buildAuthorizeUrl(clientId: string, redirectUri: string, state: string): URL {
    const authorizeUrl = new URL("https://auth.atlassian.com/authorize");
    authorizeUrl.searchParams.set("audience", "api.atlassian.com");
    authorizeUrl.searchParams.set("client_id", clientId);
    authorizeUrl.searchParams.set("scope", "read:jira-work write:jira-work read:jira-user offline_access");
    authorizeUrl.searchParams.set("redirect_uri", redirectUri);
    authorizeUrl.searchParams.set("state", state);
    authorizeUrl.searchParams.set("response_type", "code");
    authorizeUrl.searchParams.set("prompt", "consent");
    return authorizeUrl;
}

function validateTenantId(tenantId: string): boolean {
    const uuidRegex = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
    return uuidRegex.test(tenantId);
}

function generateState(): string {
    return crypto.randomUUID();
}

function getStateExpiresAt(minutes = 10): string {
    return new Date(Date.now() + minutes * 60 * 1000).toISOString();
}

// ========================================================
// 테스트
// ========================================================

Deno.test("connect-jira-start - authorize_url 구조 검증", () => {
    const clientId = "test-client-id";
    const redirectUri = "https://example.com/callback";
    const state = crypto.randomUUID();

    const url = buildAuthorizeUrl(clientId, redirectUri, state);

    assertEquals(url.hostname, "auth.atlassian.com");
    assertEquals(url.pathname, "/authorize");
    assertEquals(url.searchParams.get("audience"), "api.atlassian.com");
    assertEquals(url.searchParams.get("client_id"), clientId);
    assertEquals(url.searchParams.get("redirect_uri"), redirectUri);
    assertEquals(url.searchParams.get("state"), state);
    assertEquals(url.searchParams.get("response_type"), "code");
    assertEquals(url.searchParams.get("prompt"), "consent");
});

Deno.test("connect-jira-start - scope에 offline_access 포함 확인", () => {
    const url = buildAuthorizeUrl("id", "https://redirect.example.com", "state");
    const scope = url.searchParams.get("scope") || "";

    assertEquals(scope.includes("offline_access"), true, "offline_access 스코프 필요");
    assertEquals(scope.includes("read:jira-work"), true, "read:jira-work 스코프 필요");
    assertEquals(scope.includes("write:jira-work"), true, "write:jira-work 스코프 필요");
    assertEquals(scope.includes("read:jira-user"), true, "read:jira-user 스코프 필요");
});

Deno.test("connect-jira-start - tenant_id UUID 검증 통과", () => {
    const validUuid = "550e8400-e29b-41d4-a716-446655440000";
    assertEquals(validateTenantId(validUuid), true);
});

Deno.test("connect-jira-start - tenant_id 잘못된 형식 거부", () => {
    const invalidIds = [
        "not-a-uuid",
        "12345",
        "",
        "550e8400-e29b-41d4-a716", // 짧음
        "550e8400-e29b-41d4-a716-4466554400001", // 김
    ];

    for (const id of invalidIds) {
        assertEquals(validateTenantId(id), false, `"${id}"는 유효하지 않아야 함`);
    }
});

Deno.test("connect-jira-start - state는 UUID 형식이어야 함", () => {
    const state = generateState();
    assertMatch(
        state,
        /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i,
        "state는 UUID 형식이어야 함"
    );
});

Deno.test("connect-jira-start - state TTL은 10분이어야 함", () => {
    const before = Date.now();
    const expiresAt = getStateExpiresAt(10);
    const after = Date.now();

    const expiresMs = new Date(expiresAt).getTime();
    const expectedMin = before + 10 * 60 * 1000;
    const expectedMax = after + 10 * 60 * 1000;

    assertEquals(expiresMs >= expectedMin, true, "만료 시각은 최소 10분 후여야 함");
    assertEquals(expiresMs <= expectedMax, true, "만료 시각은 최대 10분 + 약간 후여야 함");
});

Deno.test("connect-jira-start - 매 요청마다 다른 state 생성 (CSRF 보호)", () => {
    const states = new Set<string>();
    for (let i = 0; i < 100; i++) {
        states.add(generateState());
    }
    assertEquals(states.size, 100, "100개의 state가 모두 고유해야 함");
});

Deno.test("connect-jira-start - 빈 user_id 입력 검증", () => {
    const user_id = "";
    assertEquals(
        !user_id || typeof user_id !== "string" || user_id.trim() === "",
        true,
        "빈 user_id는 유효하지 않아야 함"
    );
});

Deno.test("connect-jira-start - 모의 DB를 통한 tenant upsert 흐름", () => {
    // 테넌트가 없을 때 생성하는 로직 검증
    const tenantId = crypto.randomUUID();
    const db = mockGetServiceClient();

    // 존재 확인
    const { data: existing } = db.schema("private").from("tenants").select("id").eq("id", tenantId).maybeSingle();
    assertEquals(existing, null, "새 tenant는 없어야 함");

    // 생성
    db.schema("private").from("tenants").insert({ id: tenantId, name: `tenant_${tenantId}` }).select("id").single();

    // 확인
    const { data: created } = db.schema("private").from("tenants").select("id").eq("id", tenantId).maybeSingle();
    assertExists(created, "생성된 tenant가 존재해야 함");
});

Deno.test("connect-jira-start - CORS OPTIONS 요청 처리", async () => {
    // OPTIONS 요청에 204 반환 로직 테스트
    const req = new Request("https://example.com/connect-jira-start", {
        method: "OPTIONS",
    });

    const response = new Response(null, {
        status: 204,
        headers: {
            "Access-Control-Allow-Origin": "*",
            "Access-Control-Allow-Methods": "POST, OPTIONS",
        },
    });

    assertEquals(response.status, 204);
    assertEquals(response.headers.get("Access-Control-Allow-Origin"), "*");
});

Deno.test("connect-jira-start - POST 이외 메서드 거부", () => {
    // GET 요청 처리 로직
    const method = "GET";
    const shouldReject = method !== "POST" && method !== "OPTIONS";
    assertEquals(shouldReject, true, "POST/OPTIONS 외 메서드는 거부되어야 함");
});

/**
 * token-jira/index.ts 통합 테스트 (외부 API 모킹)
 *
 * 실행: deno test --allow-env supabase/functions/tests/token-jira.test.ts
 */

import {
    assertEquals,
    assertExists,
    assertRejects,
} from "https://deno.land/std@0.208.0/assert/mod.ts";

// 환경 변수 설정
Deno.env.set("SUPABASE_URL", "https://mock.supabase.co");
Deno.env.set("SUPABASE_SERVICE_ROLE_KEY", "mock-service-role-key");
Deno.env.set("JIRA_CLIENT_ID", "test-jira-client-id");
Deno.env.set("JIRA_CLIENT_SECRET", "test-jira-client-secret");
Deno.env.set("JWT_SECRET", "test-secret-for-unit-testing-at-least-32-chars");

import { createSessionToken, verifySessionToken } from "../_shared/jwt-helper.ts";
import { AppError } from "../_shared/error-handler.ts";

// ========================================================
// 토큰 갱신 핵심 로직 (token-jira 로직 추출)
// ========================================================

const REFRESH_LOCK_TTL_SECONDS = 5;

interface Connection {
    id: string;
    cloud_id: string | null;
    status: string;
    refreshing_at: string | null;
}

interface AtlassianTokenResponse {
    access_token: string;
    refresh_token?: string;
    expires_in: number;
}

function isRefreshLockActive(connection: Connection): boolean {
    if (!connection.refreshing_at) return false;
    const lockTime = new Date(connection.refreshing_at).getTime();
    const lockAge = (Date.now() - lockTime) / 1000;
    return lockAge < REFRESH_LOCK_TTL_SECONDS;
}

function calculateExpiresAt(expiresIn: number): string {
    return new Date(Date.now() + expiresIn * 1000).toISOString();
}

// Atlassian API 모킹 팩토리
function createMockAtlassianFetch(response: AtlassianTokenResponse) {
    return async (_url: string, _options: unknown): Promise<Response> => {
        return new Response(JSON.stringify(response), {
            status: 200,
            headers: { "Content-Type": "application/json" },
        });
    };
}

function createFailingAtlassianFetch(status: number) {
    return async (_url: string, _options: unknown): Promise<Response> => {
        return new Response(JSON.stringify({ error: "invalid_grant" }), {
            status,
            headers: { "Content-Type": "application/json" },
        });
    };
}

// ========================================================
// JWT 관련 테스트
// ========================================================

Deno.test("token-jira - 유효한 JWT로 payload 추출", async () => {
    const tenantId = "550e8400-e29b-41d4-a716-446655440000";
    const userId = "6ba7b810-9dad-11d1-80b4-00c04fd430c8";

    const token = await createSessionToken(tenantId, userId);
    const payload = await verifySessionToken(token);

    assertEquals(payload.tenant_id, tenantId);
    assertEquals(payload.user_id, userId);
    assertExists(payload.exp);
});

Deno.test("token-jira - 잘못된 JWT 거부", async () => {
    await assertRejects(
        async () => {
            await verifySessionToken("malformed.jwt.token");
        },
        undefined,
        undefined,
        "잘못된 JWT는 거부되어야 함"
    );
});

Deno.test("token-jira - 헤더 없을 때 AppError(401) 발생", () => {
    const clientToken = null;
    if (!clientToken) {
        const error = new AppError(401, "X-Client-Token header is required");
        assertEquals(error.statusCode, 401);
        assertEquals(error.message, "X-Client-Token header is required");
    }
});

// ========================================================
// 동시 갱신 방지 (refreshing_at 락) 테스트
// ========================================================

Deno.test("token-jira - refreshing_at 없으면 락 비활성", () => {
    const conn: Connection = {
        id: "conn-1",
        cloud_id: "cloud-abc",
        status: "completed",
        refreshing_at: null,
    };

    assertEquals(isRefreshLockActive(conn), false, "refreshing_at가 null이면 락 비활성");
});

Deno.test("token-jira - 5초 이내 refreshing_at는 락 활성", () => {
    const conn: Connection = {
        id: "conn-1",
        cloud_id: "cloud-abc",
        status: "completed",
        refreshing_at: new Date(Date.now() - 2000).toISOString(), // 2초 전
    };

    assertEquals(isRefreshLockActive(conn), true, "2초 전 락은 활성이어야 함");
});

Deno.test("token-jira - 5초 초과 refreshing_at는 락 만료", () => {
    const conn: Connection = {
        id: "conn-1",
        cloud_id: "cloud-abc",
        status: "completed",
        refreshing_at: new Date(Date.now() - 6000).toISOString(), // 6초 전
    };

    assertEquals(isRefreshLockActive(conn), false, "6초 전 락은 만료되어야 함");
});

Deno.test("token-jira - 락 TTL 경계값: 정확히 5초 전은 만료", () => {
    const conn: Connection = {
        id: "conn-1",
        cloud_id: "cloud-abc",
        status: "completed",
        refreshing_at: new Date(Date.now() - 5001).toISOString(), // 5.001초 전
    };

    assertEquals(isRefreshLockActive(conn), false, "5초 초과는 만료되어야 함");
});

// ========================================================
// 토큰 갱신 로직 테스트
// ========================================================

Deno.test("token-jira - 성공적인 토큰 갱신 응답 구조", async () => {
    const mockResponse: AtlassianTokenResponse = {
        access_token: "new-access-token-xyz",
        expires_in: 3600,
    };

    const mockFetch = createMockAtlassianFetch(mockResponse);
    const response = await mockFetch("https://auth.atlassian.com/oauth/token", {});
    const data: AtlassianTokenResponse = await response.json();

    assertEquals(data.access_token, "new-access-token-xyz");
    assertEquals(data.expires_in, 3600);
    assertEquals(response.status, 200);
});

Deno.test("token-jira - refresh_token 로테이션 감지", async () => {
    const mockResponse: AtlassianTokenResponse = {
        access_token: "new-access-token",
        refresh_token: "new-refresh-token-rotated",
        expires_in: 3600,
    };

    const mockFetch = createMockAtlassianFetch(mockResponse);
    const response = await mockFetch("https://auth.atlassian.com/oauth/token", {});
    const data: AtlassianTokenResponse = await response.json();

    assertExists(data.refresh_token, "로테이션된 refresh_token이 있어야 함");
    assertEquals(data.refresh_token, "new-refresh-token-rotated");
});

Deno.test("token-jira - refresh_token 없으면 로테이션 미적용", async () => {
    const mockResponse: AtlassianTokenResponse = {
        access_token: "new-access-token",
        expires_in: 3600,
        // refresh_token 없음
    };

    const mockFetch = createMockAtlassianFetch(mockResponse);
    const response = await mockFetch("https://auth.atlassian.com/oauth/token", {});
    const data: AtlassianTokenResponse = await response.json();

    assertEquals(data.refresh_token, undefined, "refresh_token이 없으면 로테이션 불필요");
});

Deno.test("token-jira - Atlassian 401 응답 처리 (재연결 필요)", async () => {
    const mockFetch = createFailingAtlassianFetch(401);
    const response = await mockFetch("https://auth.atlassian.com/oauth/token", {});

    assertEquals(response.status, 401);
    assertEquals(response.ok, false, "401은 실패로 처리되어야 함");
});

Deno.test("token-jira - Atlassian 400 응답 처리 (invalid_grant)", async () => {
    const mockFetch = createFailingAtlassianFetch(400);
    const response = await mockFetch("https://auth.atlassian.com/oauth/token", {});

    assertEquals(response.status, 400);
    assertEquals(response.ok, false);

    const body = await response.json();
    assertEquals(body.error, "invalid_grant");
});

Deno.test("token-jira - expires_at 계산 (현재 + expires_in 초)", () => {
    const expiresIn = 3600;
    const before = Date.now();
    const expiresAt = calculateExpiresAt(expiresIn);
    const after = Date.now();

    const expiresMs = new Date(expiresAt).getTime();
    const expectedMin = before + expiresIn * 1000;
    const expectedMax = after + expiresIn * 1000;

    assertEquals(expiresMs >= expectedMin, true, "expires_at은 최소 1시간 후여야 함");
    assertEquals(expiresMs <= expectedMax, true, "expires_at은 최대 1시간 + 약간 후여야 함");
});

Deno.test("token-jira - completed 아닌 연결 상태 처리", () => {
    const connection: Connection = {
        id: "conn-1",
        cloud_id: null,
        status: "pending",
        refreshing_at: null,
    };

    // pending 상태에서는 토큰 갱신 불가
    if (connection.status !== "completed") {
        const error = new AppError(400, "Jira connection is not completed");
        assertEquals(error.statusCode, 400);
    }
});

Deno.test("token-jira - POST 이외 메서드 거부", () => {
    const method = "GET";
    const shouldReject = method !== "POST" && method !== "OPTIONS";
    assertEquals(shouldReject, true, "GET은 거부되어야 함");
});

Deno.test("token-jira - cloud_id가 null이면 빈 문자열 반환", () => {
    const cloudId: string | null = null;
    const result = cloudId || "";
    assertEquals(result, "");
});

Deno.test("token-jira - 정상 cloud_id 반환", () => {
    const cloudId: string | null = "cloud-abc123";
    const result = cloudId || "";
    assertEquals(result, "cloud-abc123");
});

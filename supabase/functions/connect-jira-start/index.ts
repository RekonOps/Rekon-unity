/**
 * POST /functions/v1/connect-jira-start
 *
 * Jira OAuth 흐름을 시작합니다.
 * tenant/user를 DB에 upsert하고, CSRF state를 생성하여
 * Jira authorize_url을 반환합니다.
 */

import { getServiceClient } from "../_shared/supabase-client.ts";
import { rateLimitMiddleware } from "../_shared/rate-limiter.ts";
import { AppError, errorResponse, jsonResponse } from "../_shared/error-handler.ts";
import { safeLog } from "../_shared/log-redactor.ts";

const JIRA_CLIENT_ID = Deno.env.get("JIRA_CLIENT_ID")!;
const JIRA_REDIRECT_URI = Deno.env.get("JIRA_REDIRECT_URI")!;
const JIRA_SCOPES = "read:jira-work write:jira-work read:jira-user offline_access";
const STATE_TTL_MINUTES = 10;

interface StartRequest {
    tenant_id: string;
    user_id: string;
}

Deno.serve(async (req: Request): Promise<Response> => {
    // CORS 처리
    if (req.method === "OPTIONS") {
        return new Response(null, {
            status: 204,
            headers: {
                "Access-Control-Allow-Origin": "*",
                "Access-Control-Allow-Methods": "POST, OPTIONS",
                "Access-Control-Allow-Headers": "Content-Type, Authorization",
            },
        });
    }

    if (req.method !== "POST") {
        return errorResponse(new AppError(405, "Method Not Allowed"));
    }

    try {
        // IP Rate Limit 체크 (tenant_id 없이)
        const rateLimitError = rateLimitMiddleware(req);
        if (rateLimitError) return rateLimitError;

        // 요청 파싱
        let body: StartRequest;
        try {
            body = await req.json();
        } catch {
            throw new AppError(400, "Invalid JSON body");
        }

        const { tenant_id, user_id } = body;

        if (!tenant_id || typeof tenant_id !== "string") {
            throw new AppError(400, "tenant_id is required");
        }
        if (!user_id || typeof user_id !== "string") {
            throw new AppError(400, "user_id is required");
        }

        // UUID 형식 검증
        const uuidRegex = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
        if (!uuidRegex.test(tenant_id)) {
            throw new AppError(400, "tenant_id must be a valid UUID");
        }

        // Tenant Rate Limit 체크
        const tenantRateLimitError = rateLimitMiddleware(req, tenant_id);
        if (tenantRateLimitError) return tenantRateLimitError;

        const db = getServiceClient();

        // Tenant 존재 확인 (없으면 생성)
        let { data: tenant, error: tenantError } = await db
            .schema("private")
            .from("tenants")
            .select("id")
            .eq("id", tenant_id)
            .maybeSingle();

        if (tenantError) {
            safeLog("error", "테넌트 조회 실패", { tenant_id, error: tenantError.message });
            throw new AppError(500, "Database error");
        }

        if (!tenant) {
            const { data: newTenant, error: createTenantError } = await db
                .schema("private")
                .from("tenants")
                .insert({ id: tenant_id, name: `tenant_${tenant_id}` })
                .select("id")
                .single();

            if (createTenantError) {
                safeLog("error", "테넌트 생성 실패", { tenant_id, error: createTenantError.message });
                throw new AppError(500, "Failed to create tenant");
            }
            tenant = newTenant;
            safeLog("info", "새 테넌트 생성", { tenant_id });
        }

        // User 존재 확인 (없으면 생성)
        let { data: user, error: userError } = await db
            .schema("private")
            .from("users")
            .select("id")
            .eq("tenant_id", tenant_id)
            .eq("external_id", user_id)
            .maybeSingle();

        if (userError) {
            safeLog("error", "사용자 조회 실패", { user_id, error: userError.message });
            throw new AppError(500, "Database error");
        }

        if (!user) {
            const { data: newUser, error: createUserError } = await db
                .schema("private")
                .from("users")
                .insert({ tenant_id, external_id: user_id })
                .select("id")
                .single();

            if (createUserError) {
                safeLog("error", "사용자 생성 실패", { user_id, error: createUserError.message });
                throw new AppError(500, "Failed to create user");
            }
            user = newUser;
            safeLog("info", "새 사용자 생성", { user_id });
        }

        // CSRF state 생성
        const state = crypto.randomUUID();
        const stateExpiresAt = new Date(Date.now() + STATE_TTL_MINUTES * 60 * 1000).toISOString();

        // oauth_connections 레코드 생성 (기존 pending 있으면 갱신)
        const { data: connection, error: connectionError } = await db
            .schema("private")
            .from("oauth_connections")
            .upsert(
                {
                    user_id: user.id,
                    tenant_id,
                    provider: "jira",
                    status: "pending",
                    state,
                    state_expires_at: stateExpiresAt,
                },
                { onConflict: "user_id,provider" }
            )
            .select("id")
            .single();

        if (connectionError) {
            safeLog("error", "연결 레코드 생성 실패", { error: connectionError.message });
            throw new AppError(500, "Failed to create connection");
        }

        // Jira authorize_url 생성
        const authorizeUrl = new URL("https://auth.atlassian.com/authorize");
        authorizeUrl.searchParams.set("audience", "api.atlassian.com");
        authorizeUrl.searchParams.set("client_id", JIRA_CLIENT_ID);
        authorizeUrl.searchParams.set("scope", JIRA_SCOPES);
        authorizeUrl.searchParams.set("redirect_uri", JIRA_REDIRECT_URI);
        authorizeUrl.searchParams.set("state", state);
        authorizeUrl.searchParams.set("response_type", "code");
        authorizeUrl.searchParams.set("prompt", "consent");

        safeLog("info", "Jira OAuth 흐름 시작", { connect_id: connection.id, tenant_id });

        return jsonResponse({
            connect_id: connection.id,
            authorize_url: authorizeUrl.toString(),
        });
    } catch (error) {
        return errorResponse(error);
    }
});

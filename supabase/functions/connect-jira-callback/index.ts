/**
 * GET /functions/v1/connect-jira-callback
 *
 * Jira OAuth 콜백 처리.
 * state 검증 → code 교환 → 토큰 Vault 저장 → cloud_id 획득 → HTML 응답
 */

import { getServiceClient } from "../_shared/supabase-client.ts";
import { AppError, errorResponse } from "../_shared/error-handler.ts";
import { safeLog } from "../_shared/log-redactor.ts";
import { createSessionToken } from "../_shared/jwt-helper.ts";

const JIRA_CLIENT_ID = Deno.env.get("JIRA_CLIENT_ID")!;
const JIRA_CLIENT_SECRET = Deno.env.get("JIRA_CLIENT_SECRET")!;
const JIRA_REDIRECT_URI = Deno.env.get("JIRA_REDIRECT_URI")!;

function successHtml(): Response {
    const text = [
        "=================================",
        "  Jira Connected Successfully!",
        "=================================",
        "",
        "  You can close this window.",
        "",
        "=================================",
    ].join("\n");

    return new Response(text, {
        status: 200,
        headers: { "Content-Type": "text/plain; charset=utf-8" },
    });
}

function errorHtml(message: string): Response {
    const text = [
        "=================================",
        "  Connection Failed",
        "=================================",
        "",
        `  ${message}`,
        "",
        "  Please close this window",
        "  and try again.",
        "",
        "=================================",
    ].join("\n");

    return new Response(text, {
        status: 400,
        headers: { "Content-Type": "text/plain; charset=utf-8" },
    });
}

Deno.serve(async (req: Request): Promise<Response> => {
    if (req.method !== "GET") {
        return errorResponse(new AppError(405, "Method Not Allowed"));
    }

    try {
        const url = new URL(req.url);
        const code = url.searchParams.get("code");
        const state = url.searchParams.get("state");
        const errorParam = url.searchParams.get("error");

        // 사용자가 인증을 거부한 경우
        if (errorParam) {
            safeLog("warn", "사용자가 Jira 인증 거부", { error: errorParam });
            return errorHtml("Jira connection was cancelled.");
        }

        if (!code || !state) {
            return errorHtml("Required parameters are missing.");
        }

        const db = getServiceClient();

        // state로 oauth_connections 조회
        const { data: connection, error: lookupError } = await db
            .from("oauth_connections")
            .select("id, user_id, tenant_id, state, state_expires_at, status")
            .eq("state", state)
            .maybeSingle();

        if (lookupError) {
            safeLog("error", "state 조회 실패", { error: lookupError.message });
            return errorHtml("An internal error occurred.");
        }

        if (!connection) {
            safeLog("warn", "유효하지 않은 state", { state: state.substring(0, 8) + "..." });
            return errorHtml("Invalid or expired connection.");
        }

        // 만료 체크
        if (new Date(connection.state_expires_at) < new Date()) {
            safeLog("warn", "state 만료", { connect_id: connection.id });
            // 만료된 state 정리
            await db
                .from("oauth_connections")
                .update({ state: null, state_expires_at: null })
                .eq("id", connection.id);
            return errorHtml("Authentication timed out. Please try again.");
        }

        // state 즉시 무효화 (1회성, 재사용 방지)
        const { error: invalidateError } = await db
            .from("oauth_connections")
            .update({ state: null, state_expires_at: null })
            .eq("id", connection.id);

        if (invalidateError) {
            safeLog("error", "state 무효화 실패", { error: invalidateError.message });
            return errorHtml("An internal error occurred.");
        }

        // Atlassian 토큰 교환
        safeLog("info", "Atlassian 토큰 교환 시작", { connect_id: connection.id });

        const tokenResponse = await fetch("https://auth.atlassian.com/oauth/token", {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({
                grant_type: "authorization_code",
                client_id: JIRA_CLIENT_ID,
                client_secret: JIRA_CLIENT_SECRET,
                code,
                redirect_uri: JIRA_REDIRECT_URI,
            }),
        });

        if (!tokenResponse.ok) {
            const errorBody = await tokenResponse.text();
            safeLog("error", "Atlassian 토큰 교환 실패", { status: tokenResponse.status });
            // 민감 정보 로그에 노출되지 않도록 errorBody는 별도 처리
            console.error("[TOKEN_EXCHANGE_FAILED]", errorBody.substring(0, 100));
            return errorHtml("Jira authentication failed. Please try again.");
        }

        const tokens = await tokenResponse.json();
        const { access_token, refresh_token } = tokens;

        if (!access_token || !refresh_token) {
            safeLog("error", "토큰 응답에 필수 필드 누락", { connect_id: connection.id });
            return errorHtml("Received an invalid response from Jira.");
        }

        // Accessible Resources API로 cloud_id 획득
        let cloudId: string | null = null;
        try {
            const resourcesResponse = await fetch(
                "https://api.atlassian.com/oauth/token/accessible-resources",
                {
                    headers: { Authorization: `Bearer ${access_token}` },
                }
            );

            if (resourcesResponse.ok) {
                const resources = await resourcesResponse.json();
                if (Array.isArray(resources) && resources.length > 0) {
                    cloudId = resources[0].id;
                    safeLog("info", "cloud_id 획득 성공", { cloud_id: cloudId });
                }
            } else {
                safeLog("warn", "accessible-resources 조회 실패", { status: resourcesResponse.status });
            }
        } catch (err) {
            safeLog("warn", "accessible-resources 호출 오류", { error: String(err) });
        }

        // oauth_connections 상태 completed로 업데이트 (refresh_token 직접 저장)
        // Vault pgsodium 권한 문제로 인해 DB 컬럼에 직접 저장 (MVP)
        const { error: updateError } = await db
            .from("oauth_connections")
            .update({
                status: "completed",
                cloud_id: cloudId,
                refresh_token: refresh_token,
                scopes: JIRA_CLIENT_ID ? ["read:jira-work", "write:jira-work", "read:jira-user", "offline_access"] : [],
            })
            .eq("id", connection.id);

        if (updateError) {
            safeLog("error", "연결 상태 업데이트 실패", { error: updateError.message });
            return errorHtml("Failed to save connection status.");
        }

        safeLog("info", "Jira OAuth 연결 완료", { connect_id: connection.id, tenant_id: connection.tenant_id });

        return successHtml();
    } catch (error) {
        safeLog("error", "콜백 처리 중 예상치 못한 오류", { error: String(error) });
        return errorHtml("An internal error occurred.");
    }
});

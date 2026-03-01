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
    const html = `<!DOCTYPE html>
<html lang="ko">
<head>
  <meta charset="UTF-8">
  <meta name="viewport" content="width=device-width, initial-scale=1.0">
  <title>Jira 연결 완료</title>
  <style>
    body {
      font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif;
      display: flex;
      flex-direction: column;
      align-items: center;
      justify-content: center;
      height: 100vh;
      margin: 0;
      background: #f4f5f7;
      color: #172b4d;
    }
    .card {
      background: white;
      border-radius: 8px;
      padding: 40px;
      text-align: center;
      box-shadow: 0 4px 12px rgba(0,0,0,0.1);
    }
    .icon { font-size: 48px; margin-bottom: 16px; }
    h1 { margin: 0 0 8px; font-size: 24px; }
    p { color: #6b778c; margin: 0; }
  </style>
</head>
<body>
  <div class="card">
    <div class="icon">✓</div>
    <h1>Jira 연결이 완료되었습니다!</h1>
    <p>이 창을 닫아주세요.</p>
  </div>
  <script>
    // 1초 후 자동으로 창 닫기
    setTimeout(function() {
      window.close();
    }, 1500);
  </script>
</body>
</html>`;

    return new Response(html, {
        status: 200,
        headers: { "Content-Type": "text/html; charset=utf-8" },
    });
}

function errorHtml(message: string): Response {
    const html = `<!DOCTYPE html>
<html lang="ko">
<head>
  <meta charset="UTF-8">
  <title>연결 실패</title>
  <style>
    body {
      font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif;
      display: flex;
      flex-direction: column;
      align-items: center;
      justify-content: center;
      height: 100vh;
      margin: 0;
      background: #f4f5f7;
      color: #172b4d;
    }
    .card {
      background: white;
      border-radius: 8px;
      padding: 40px;
      text-align: center;
      box-shadow: 0 4px 12px rgba(0,0,0,0.1);
    }
    .icon { font-size: 48px; margin-bottom: 16px; }
    h1 { margin: 0 0 8px; font-size: 24px; color: #de350b; }
    p { color: #6b778c; margin: 0; }
  </style>
</head>
<body>
  <div class="card">
    <div class="icon">✗</div>
    <h1>연결에 실패했습니다</h1>
    <p>${message}</p>
    <p style="margin-top: 16px; font-size: 12px;">이 창을 닫고 다시 시도해주세요.</p>
  </div>
</body>
</html>`;

    return new Response(html, {
        status: 400,
        headers: { "Content-Type": "text/html; charset=utf-8" },
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
            return errorHtml("Jira 연결이 취소되었습니다.");
        }

        if (!code || !state) {
            return errorHtml("필수 파라미터가 누락되었습니다.");
        }

        const db = getServiceClient();

        // state로 oauth_connections 조회
        const { data: connection, error: lookupError } = await db
            .schema("private")
            .from("oauth_connections")
            .select("id, user_id, tenant_id, state, state_expires_at, status")
            .eq("state", state)
            .maybeSingle();

        if (lookupError) {
            safeLog("error", "state 조회 실패", { error: lookupError.message });
            return errorHtml("내부 오류가 발생했습니다.");
        }

        if (!connection) {
            safeLog("warn", "유효하지 않은 state", { state: state.substring(0, 8) + "..." });
            return errorHtml("유효하지 않거나 만료된 연결입니다.");
        }

        // 만료 체크
        if (new Date(connection.state_expires_at) < new Date()) {
            safeLog("warn", "state 만료", { connect_id: connection.id });
            // 만료된 state 정리
            await db
                .schema("private")
                .from("oauth_connections")
                .update({ state: null, state_expires_at: null })
                .eq("id", connection.id);
            return errorHtml("인증 시간이 초과되었습니다. 다시 시도해주세요.");
        }

        // state 즉시 무효화 (1회성, 재사용 방지)
        const { error: invalidateError } = await db
            .schema("private")
            .from("oauth_connections")
            .update({ state: null, state_expires_at: null })
            .eq("id", connection.id);

        if (invalidateError) {
            safeLog("error", "state 무효화 실패", { error: invalidateError.message });
            return errorHtml("내부 오류가 발생했습니다.");
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
            return errorHtml("Jira 인증에 실패했습니다. 다시 시도해주세요.");
        }

        const tokens = await tokenResponse.json();
        const { access_token, refresh_token } = tokens;

        if (!access_token || !refresh_token) {
            safeLog("error", "토큰 응답에 필수 필드 누락", { connect_id: connection.id });
            return errorHtml("Jira로부터 잘못된 응답을 받았습니다.");
        }

        // Vault에 refresh_token 저장
        const { data: secretData, error: vaultError } = await db.rpc("store_refresh_token", {
            p_tenant_id: connection.tenant_id,
            p_user_id: connection.user_id,
            p_token: refresh_token,
        }, { schema: "private" });

        if (vaultError) {
            safeLog("error", "Vault 저장 실패", { error: vaultError.message });
            return errorHtml("토큰 저장에 실패했습니다.");
        }

        const refreshSecretId = secretData;

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

        // oauth_connections 상태 completed로 업데이트
        const { error: updateError } = await db
            .schema("private")
            .from("oauth_connections")
            .update({
                status: "completed",
                cloud_id: cloudId,
                refresh_secret_id: refreshSecretId,
                scopes: JIRA_CLIENT_ID ? ["read:jira-work", "write:jira-work", "read:jira-user", "offline_access"] : [],
            })
            .eq("id", connection.id);

        if (updateError) {
            safeLog("error", "연결 상태 업데이트 실패", { error: updateError.message });
            return errorHtml("연결 상태 저장에 실패했습니다.");
        }

        safeLog("info", "Jira OAuth 연결 완료", { connect_id: connection.id, tenant_id: connection.tenant_id });

        return successHtml();
    } catch (error) {
        safeLog("error", "콜백 처리 중 예상치 못한 오류", { error: String(error) });
        return errorHtml("내부 오류가 발생했습니다.");
    }
});

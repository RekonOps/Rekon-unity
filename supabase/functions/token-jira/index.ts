/**
 * POST /functions/v1/token-jira
 *
 * Jira access_token을 갱신합니다.
 * X-Client-Token 헤더의 JWT를 검증하고, Vault에서 refresh_token을 조회하여
 * Atlassian API로 새 access_token을 발급받습니다.
 * refresh_token 로테이션 및 동시 갱신 방지 처리 포함.
 */

import { getServiceClient } from "../_shared/supabase-client.ts";
import { verifySessionToken } from "../_shared/jwt-helper.ts";
import { rateLimitMiddleware } from "../_shared/rate-limiter.ts";
import { AppError, errorResponse, jsonResponse } from "../_shared/error-handler.ts";
import { safeLog } from "../_shared/log-redactor.ts";
import { decryptToken, encryptToken, isEncrypted } from "../_shared/token-encryptor.ts";

const JIRA_CLIENT_ID = Deno.env.get("JIRA_CLIENT_ID")!;
const JIRA_CLIENT_SECRET = Deno.env.get("JIRA_CLIENT_SECRET")!;
const REFRESH_LOCK_TTL_SECONDS = 5;

interface TokenResponse {
    access_token: string;
    expires_at: string;
    cloud_id: string;
}

interface AtlassianTokenResponse {
    access_token: string;
    refresh_token?: string;
    expires_in: number;
    token_type: string;
    scope?: string;
}

Deno.serve(async (req: Request): Promise<Response> => {
    // CORS 처리
    if (req.method === "OPTIONS") {
        return new Response(null, {
            status: 204,
            headers: {
                "Access-Control-Allow-Origin": "*",
                "Access-Control-Allow-Methods": "POST, OPTIONS",
                "Access-Control-Allow-Headers": "Content-Type, Authorization, X-Client-Token",
            },
        });
    }

    if (req.method !== "POST") {
        return errorResponse(new AppError(405, "Method Not Allowed"));
    }

    try {
        // JWT 추출 및 검증
        const clientToken = req.headers.get("X-Client-Token");
        if (!clientToken) {
            throw new AppError(401, "X-Client-Token header is required");
        }

        let jwtPayload: { tenant_id: string; user_id: string };
        try {
            jwtPayload = await verifySessionToken(clientToken);
        } catch (err) {
            safeLog("warn", "JWT 검증 실패", { error: String(err) });
            throw new AppError(401, "Invalid or expired token");
        }

        const { tenant_id, user_id } = jwtPayload;

        // Tenant Rate Limit 체크
        const rateLimitError = rateLimitMiddleware(req, tenant_id);
        if (rateLimitError) return rateLimitError;

        const db = getServiceClient();

        // oauth_connections 조회 (refresh_token 컬럼 포함)
        const { data: connection, error: connError } = await db
            .from("oauth_connections")
            .select("id, cloud_id, refreshing_at, status, refresh_token")
            .eq("user_id", user_id)
            .eq("tenant_id", tenant_id)
            .eq("provider", "jira")
            .maybeSingle();

        if (connError) {
            safeLog("error", "연결 조회 실패", { error: connError.message });
            throw new AppError(500, "Database error");
        }

        if (!connection) {
            throw new AppError(404, "Jira connection not found. Please connect first.");
        }

        if (connection.status !== "completed") {
            throw new AppError(400, "Jira connection is not completed");
        }

        // 동시 refresh 방지: refreshing_at 락 체크 (5초 TTL)
        if (connection.refreshing_at) {
            const lockTime = new Date(connection.refreshing_at).getTime();
            const now = Date.now();
            const lockAge = (now - lockTime) / 1000;

            if (lockAge < REFRESH_LOCK_TTL_SECONDS) {
                safeLog("warn", "동시 refresh 감지, 락 대기", { connection_id: connection.id, lock_age_s: lockAge });
                throw new AppError(429, "Token refresh in progress, please retry in a moment");
            }
        }

        // refreshing_at 락 설정
        const { error: lockError } = await db
            .from("oauth_connections")
            .update({ refreshing_at: new Date().toISOString() })
            .eq("id", connection.id);

        if (lockError) {
            safeLog("error", "갱신 락 설정 실패", { error: lockError.message });
            throw new AppError(500, "Failed to acquire refresh lock");
        }

        try {
            // oauth_connections 테이블에서 refresh_token 조회 후 복호화
            const storedRefreshToken = connection.refresh_token;

            if (!storedRefreshToken) {
                safeLog("error", "oauth_connections에서 refresh_token 조회 실패", {
                    connection_id: connection.id,
                    has_token: false,
                });
                throw new AppError(500, "Failed to retrieve refresh token");
            }

            // refresh_token 복호화 (평문 폴백 지원)
            const refreshToken = await decryptToken(storedRefreshToken);

            // 마이그레이션: 평문이었으면 암호화하여 즉시 재저장
            if (!isEncrypted(storedRefreshToken)) {
                safeLog("info", "평문 refresh_token 감지 - 암호화하여 재저장", {
                    connection_id: connection.id,
                });
                try {
                    const reEncrypted = await encryptToken(refreshToken);
                    const { error: migrateError } = await db
                        .from("oauth_connections")
                        .update({ refresh_token: reEncrypted })
                        .eq("id", connection.id);
                    if (migrateError) {
                        safeLog("warn", "평문 토큰 재암호화 저장 실패 (계속 진행)", {
                            error: migrateError.message,
                        });
                    } else {
                        safeLog("info", "평문 refresh_token 재암호화 완료", {
                            connection_id: connection.id,
                        });
                    }
                } catch (migrationEncryptError) {
                    // 재암호화 실패는 경고만 남기고 현재 요청은 계속 진행
                    safeLog("warn", "평문 토큰 재암호화 실패 (계속 진행)", {
                        error: String(migrationEncryptError),
                    });
                }
            }

            // Atlassian API로 access_token 갱신
            safeLog("info", "Atlassian access_token 갱신 시작", { tenant_id });

            const tokenResponse = await fetch("https://auth.atlassian.com/oauth/token", {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({
                    grant_type: "refresh_token",
                    client_id: JIRA_CLIENT_ID,
                    client_secret: JIRA_CLIENT_SECRET,
                    refresh_token: refreshToken,
                }),
            });

            if (!tokenResponse.ok) {
                const errBody = await tokenResponse.text();
                safeLog("error", "Atlassian 토큰 갱신 실패", { status: tokenResponse.status });
                console.error("[REFRESH_FAILED]", errBody.substring(0, 100));

                // refresh_token이 만료된 경우 연결 상태를 error로 변경
                if (tokenResponse.status === 400 || tokenResponse.status === 401) {
                    await db
                        .from("oauth_connections")
                        .update({ status: "error", refreshing_at: null })
                        .eq("id", connection.id);
                    throw new AppError(401, "Jira connection expired. Please reconnect.");
                }

                throw new AppError(502, "Failed to refresh Jira token");
            }

            const newTokens: AtlassianTokenResponse = await tokenResponse.json();

            // 회전 refresh token 처리: 새 refresh_token이 있으면 암호화 후 DB 업데이트
            if (newTokens.refresh_token) {
                safeLog("info", "refresh_token 로테이션 감지, 암호화 후 DB 갱신", { tenant_id });

                try {
                    const encryptedNewRefreshToken = await encryptToken(newTokens.refresh_token);
                    const { error: updateTokenError } = await db
                        .from("oauth_connections")
                        .update({ refresh_token: encryptedNewRefreshToken })
                        .eq("id", connection.id);

                    if (updateTokenError) {
                        // DB 갱신 실패는 로그만 남기고 access_token은 반환 (다음 갱신 시 재시도)
                        safeLog("error", "회전 refresh_token DB 갱신 실패", { error: updateTokenError.message });
                    } else {
                        safeLog("info", "로테이션된 refresh_token 암호화 저장 완료", { tenant_id });
                    }
                } catch (rotationEncryptError) {
                    safeLog("error", "로테이션 refresh_token 암호화 실패", {
                        error: String(rotationEncryptError),
                    });
                    // 암호화 실패 시에도 access_token은 반환 (보안상 평문 저장 금지)
                }
            }

            // refreshing_at 락 해제
            await db
                .from("oauth_connections")
                .update({ refreshing_at: null })
                .eq("id", connection.id);

            // expires_at 계산 (현재 시각 + expires_in 초)
            const expiresAt = new Date(Date.now() + (newTokens.expires_in || 3600) * 1000).toISOString();

            safeLog("info", "access_token 갱신 성공", { tenant_id });

            const response: TokenResponse = {
                access_token: newTokens.access_token,
                expires_at: expiresAt,
                cloud_id: connection.cloud_id || "",
            };

            return jsonResponse(response);
        } catch (innerError) {
            // 예외 발생 시 락 해제 보장
            try {
                await db
                    .from("oauth_connections")
                    .update({ refreshing_at: null })
                    .eq("id", connection.id);
            } catch (unlockError) {
                safeLog("error", "락 해제 실패", { error: String(unlockError) });
            }
            throw innerError;
        }
    } catch (error) {
        return errorResponse(error);
    }
});

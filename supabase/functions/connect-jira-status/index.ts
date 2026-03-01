/**
 * GET /functions/v1/connect-jira-status
 *
 * Jira OAuth 연결 상태를 폴링합니다.
 * completed 상태면 JWT 세션 토큰을 함께 반환합니다.
 */

import { getServiceClient } from "../_shared/supabase-client.ts";
import { AppError, errorResponse, jsonResponse } from "../_shared/error-handler.ts";
import { createSessionToken } from "../_shared/jwt-helper.ts";
import { safeLog } from "../_shared/log-redactor.ts";
import { rateLimitMiddleware } from "../_shared/rate-limiter.ts";

type ConnectionStatus = "pending" | "completed" | "error";

interface StatusResponse {
    status: ConnectionStatus;
    session_token?: string;
}

Deno.serve(async (req: Request): Promise<Response> => {
    // CORS 처리
    if (req.method === "OPTIONS") {
        return new Response(null, {
            status: 204,
            headers: {
                "Access-Control-Allow-Origin": "*",
                "Access-Control-Allow-Methods": "GET, OPTIONS",
                "Access-Control-Allow-Headers": "Content-Type, Authorization",
            },
        });
    }

    if (req.method !== "GET") {
        return errorResponse(new AppError(405, "Method Not Allowed"));
    }

    try {
        // Rate Limit 체크
        const rateLimitError = rateLimitMiddleware(req);
        if (rateLimitError) return rateLimitError;

        const url = new URL(req.url);
        const connectId = url.searchParams.get("connect_id");

        if (!connectId) {
            throw new AppError(400, "connect_id is required");
        }

        // UUID 형식 검증
        const uuidRegex = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
        if (!uuidRegex.test(connectId)) {
            throw new AppError(400, "connect_id must be a valid UUID");
        }

        const db = getServiceClient();

        // 연결 상태 조회
        const { data: connection, error: queryError } = await db
            .from("oauth_connections")
            .select("id, user_id, tenant_id, status")
            .eq("id", connectId)
            .maybeSingle();

        if (queryError) {
            safeLog("error", "연결 상태 조회 실패", { connect_id: connectId, error: queryError.message });
            throw new AppError(500, "Database error");
        }

        if (!connection) {
            throw new AppError(404, "Connection not found");
        }

        const response: StatusResponse = {
            status: connection.status as ConnectionStatus,
        };

        // completed 상태면 JWT 세션 토큰 발급
        if (connection.status === "completed") {
            try {
                response.session_token = await createSessionToken(
                    connection.tenant_id,
                    connection.user_id
                );
                safeLog("info", "세션 토큰 발급 성공", { connect_id: connectId });
            } catch (jwtError) {
                safeLog("error", "JWT 생성 실패", { error: String(jwtError) });
                throw new AppError(500, "Failed to create session token");
            }
        }

        return jsonResponse(response);
    } catch (error) {
        return errorResponse(error);
    }
});

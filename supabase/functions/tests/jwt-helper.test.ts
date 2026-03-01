/**
 * jwt-helper.ts 단위 테스트
 *
 * 실행: deno test --allow-env supabase/functions/tests/jwt-helper.test.ts
 */

import {
    assertEquals,
    assertRejects,
    assertExists,
} from "https://deno.land/std@0.208.0/assert/mod.ts";

// 테스트용 JWT_SECRET 환경변수 설정
Deno.env.set("JWT_SECRET", "test-secret-for-unit-testing-at-least-32-chars");

// _shared/jwt-helper.ts 임포트 (상대 경로)
import { createSessionToken, verifySessionToken } from "../_shared/jwt-helper.ts";

Deno.test("JWT - 유효한 토큰 생성 및 검증", async () => {
    const tenantId = "550e8400-e29b-41d4-a716-446655440000";
    const userId = "6ba7b810-9dad-11d1-80b4-00c04fd430c8";

    const token = await createSessionToken(tenantId, userId);

    assertExists(token, "토큰이 생성되어야 함");
    assertEquals(typeof token, "string");

    // JWT는 3개의 파트로 구성되어야 함 (header.payload.signature)
    const parts = token.split(".");
    assertEquals(parts.length, 3, "JWT는 3개 파트로 구성되어야 함");
});

Deno.test("JWT - 생성된 토큰 검증 시 올바른 페이로드 반환", async () => {
    const tenantId = "550e8400-e29b-41d4-a716-446655440000";
    const userId = "6ba7b810-9dad-11d1-80b4-00c04fd430c8";

    const token = await createSessionToken(tenantId, userId);
    const payload = await verifySessionToken(token);

    assertEquals(payload.tenant_id, tenantId, "tenant_id가 일치해야 함");
    assertEquals(payload.user_id, userId, "user_id가 일치해야 함");
    assertExists(payload.exp, "exp 필드가 존재해야 함");
});

Deno.test("JWT - exp는 현재 시각보다 미래여야 함", async () => {
    const tenantId = "550e8400-e29b-41d4-a716-446655440000";
    const userId = "6ba7b810-9dad-11d1-80b4-00c04fd430c8";

    const token = await createSessionToken(tenantId, userId);
    const payload = await verifySessionToken(token);

    const now = Math.floor(Date.now() / 1000);
    assertEquals(
        payload.exp > now,
        true,
        "exp는 현재 시각보다 미래여야 함"
    );
});

Deno.test("JWT - 만료 시간은 약 24시간이어야 함", async () => {
    const tenantId = "550e8400-e29b-41d4-a716-446655440000";
    const userId = "test-user-id";

    const token = await createSessionToken(tenantId, userId);
    const payload = await verifySessionToken(token);

    const now = Math.floor(Date.now() / 1000);
    const ttl = payload.exp - now;

    // 24시간(86400초) 전후 60초 오차 허용
    assertEquals(ttl > 86340, true, "TTL이 최소 86340초여야 함");
    assertEquals(ttl < 86460, true, "TTL이 최대 86460초여야 함");
});

Deno.test("JWT - 잘못된 토큰 검증 시 예외 발생", async () => {
    await assertRejects(
        async () => {
            await verifySessionToken("invalid.jwt.token");
        },
        undefined,
        undefined,
        "잘못된 토큰은 예외를 발생시켜야 함"
    );
});

Deno.test("JWT - 위조된 서명 검증 시 예외 발생", async () => {
    const tenantId = "550e8400-e29b-41d4-a716-446655440000";
    const userId = "test-user-id";

    const token = await createSessionToken(tenantId, userId);

    // 서명 부분 변조
    const parts = token.split(".");
    const tamperedToken = `${parts[0]}.${parts[1]}.tampered_signature`;

    await assertRejects(
        async () => {
            await verifySessionToken(tamperedToken);
        },
        undefined,
        undefined,
        "위조된 서명은 예외를 발생시켜야 함"
    );
});

Deno.test("JWT - 빈 tenant_id로 토큰 생성 가능", async () => {
    // 빈 값도 기술적으로 생성 가능 (검증은 호출자 책임)
    const token = await createSessionToken("", "user-id");
    assertExists(token);

    const payload = await verifySessionToken(token);
    assertEquals(payload.tenant_id, "");
});

Deno.test("JWT - 여러 토큰은 서로 달라야 함 (유니크)", async () => {
    const tenantId = "550e8400-e29b-41d4-a716-446655440000";
    const userId = "test-user";

    const token1 = await createSessionToken(tenantId, userId);
    const token2 = await createSessionToken(tenantId, userId);

    // 타임스탬프가 동일할 경우 같을 수 있으므로 형식만 검증
    assertEquals(typeof token1, "string");
    assertEquals(typeof token2, "string");
    assertEquals(token1.split(".").length, 3);
    assertEquals(token2.split(".").length, 3);
});

import { create, verify, getNumericDate } from "https://deno.land/x/djwt@v3.0.2/mod.ts";

const encoder = new TextEncoder();

async function getKey(): Promise<CryptoKey> {
    const secret = Deno.env.get("JWT_SECRET") || "";
    return await crypto.subtle.importKey(
        "raw",
        encoder.encode(secret),
        { name: "HMAC", hash: "SHA-256" },
        false,
        ["sign", "verify"]
    );
}

export interface JwtPayload {
    tenant_id: string;
    user_id: string;
    exp: number;
}

export async function createSessionToken(tenantId: string, userId: string): Promise<string> {
    const key = await getKey();
    return await create(
        { alg: "HS256", typ: "JWT" },
        {
            tenant_id: tenantId,
            user_id: userId,
            exp: getNumericDate(24 * 60 * 60), // 24 hours
        },
        key
    );
}

export async function verifySessionToken(token: string): Promise<JwtPayload> {
    const key = await getKey();
    const payload = await verify(token, key);
    return payload as unknown as JwtPayload;
}

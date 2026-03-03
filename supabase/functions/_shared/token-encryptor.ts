/**
 * AES-256-GCM 기반 토큰 암호화/복호화 유틸리티
 *
 * - Deno Web Crypto API (crypto.subtle) 사용
 * - 저장 형식: `v1:base64(iv + ciphertext)`
 * - REFRESH_TOKEN_ENCRYPTION_KEY 환경변수 필수 (없으면 에러)
 */

const ALGORITHM = "AES-GCM";
const IV_LENGTH = 12; // 96비트 IV (GCM 권장값)
const KEY_VERSION = "v1";

/**
 * 환경변수에서 암호화 키를 가져옵니다.
 * 키가 없으면 에러를 던져 OAuth 플로우를 중단시킵니다.
 */
function getEncryptionKey(): string {
    const key = Deno.env.get("REFRESH_TOKEN_ENCRYPTION_KEY");
    if (!key) {
        throw new Error(
            "REFRESH_TOKEN_ENCRYPTION_KEY 환경변수가 설정되지 않았습니다. " +
            "OAuth 플로우를 진행할 수 없습니다."
        );
    }
    return key;
}

/**
 * Uint8Array를 base64 문자열로 안전하게 변환합니다.
 * 스프레드 연산자(...) 방식은 대용량 배열에서 스택 오버플로우 위험이 있어 루프로 처리합니다.
 */
function uint8ArrayToBase64(bytes: Uint8Array): string {
    let binary = "";
    for (let i = 0; i < bytes.length; i++) {
        binary += String.fromCharCode(bytes[i]);
    }
    return btoa(binary);
}

/**
 * base64 문자열을 Uint8Array로 안전하게 변환합니다.
 */
function base64ToUint8Array(base64: string): Uint8Array {
    const binary = atob(base64);
    const bytes = new Uint8Array(binary.length);
    for (let i = 0; i < binary.length; i++) {
        bytes[i] = binary.charCodeAt(i);
    }
    return bytes;
}

/**
 * Base64 인코딩된 키를 CryptoKey 객체로 변환합니다.
 * AES-256 키는 반드시 32바이트여야 합니다.
 */
async function importKey(keyBase64: string): Promise<CryptoKey> {
    const keyBytes = base64ToUint8Array(keyBase64);
    if (keyBytes.length !== 32) {
        throw new Error(`AES-256 키는 32바이트여야 합니다. 현재: ${keyBytes.length}바이트`);
    }
    return crypto.subtle.importKey(
        "raw",
        keyBytes,
        { name: ALGORITHM },
        false,
        ["encrypt", "decrypt"]
    );
}

/**
 * 평문 토큰을 AES-256-GCM으로 암호화합니다.
 * @returns `v1:base64(iv + ciphertext)` 형식의 문자열
 */
export async function encryptToken(plaintext: string): Promise<string> {
    const keyBase64 = getEncryptionKey();
    const key = await importKey(keyBase64);

    // 무작위 IV 생성 (매 암호화마다 고유한 IV 사용)
    const iv = crypto.getRandomValues(new Uint8Array(IV_LENGTH));
    const encoded = new TextEncoder().encode(plaintext);

    const ciphertext = await crypto.subtle.encrypt(
        { name: ALGORITHM, iv },
        key,
        encoded
    );

    // iv + ciphertext 결합 후 base64 인코딩
    const combined = new Uint8Array(iv.length + new Uint8Array(ciphertext).length);
    combined.set(iv);
    combined.set(new Uint8Array(ciphertext), iv.length);

    const base64 = uint8ArrayToBase64(combined);
    return `${KEY_VERSION}:${base64}`;
}

/**
 * 암호화된 토큰을 복호화합니다.
 * v1: prefix가 없으면 평문으로 간주하여 그대로 반환합니다 (마이그레이션 폴백).
 */
export async function decryptToken(stored: string): Promise<string> {
    // 키 버전 prefix 확인 - 없으면 평문 폴백 (마이그레이션 기간)
    if (!stored.startsWith(`${KEY_VERSION}:`)) {
        return stored;
    }

    const keyBase64 = getEncryptionKey();
    const key = await importKey(keyBase64);

    // `v1:` prefix 제거 후 base64 디코딩
    const base64Data = stored.substring(KEY_VERSION.length + 1);
    const combined = base64ToUint8Array(base64Data);

    // iv와 ciphertext 분리
    const iv = combined.slice(0, IV_LENGTH);
    const ciphertext = combined.slice(IV_LENGTH);

    const decrypted = await crypto.subtle.decrypt(
        { name: ALGORITHM, iv },
        key,
        ciphertext
    );

    return new TextDecoder().decode(decrypted);
}

/**
 * 저장된 값이 암호화된 형식인지 확인합니다.
 * @returns v1: prefix가 있으면 true (암호화됨), 없으면 false (평문)
 */
export function isEncrypted(stored: string): boolean {
    return stored.startsWith(`${KEY_VERSION}:`);
}

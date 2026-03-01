const SENSITIVE_PATTERNS = [
    /("?(?:token|secret|password|refresh_token|access_token|api_key)"?\s*[:=]\s*)"[^"]+"/gi,
    /Bearer\s+[A-Za-z0-9\-._~+/]+=*/gi,
];

export function redact(message: string): string {
    let result = message;
    for (const pattern of SENSITIVE_PATTERNS) {
        result = result.replace(pattern, "[REDACTED]");
    }
    return result;
}

export function safeLog(level: "info" | "warn" | "error", message: string, data?: unknown): void {
    const redacted = redact(message);
    const dataStr = data ? redact(JSON.stringify(data)) : "";
    console[level](`[${new Date().toISOString()}] ${redacted}`, dataStr);
}

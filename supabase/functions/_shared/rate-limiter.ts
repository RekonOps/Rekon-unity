interface RateLimitEntry {
    count: number;
    resetAt: number;
}

const store = new Map<string, RateLimitEntry>();

export interface RateLimitConfig {
    windowMs: number;
    maxRequests: number;
}

export const TENANT_LIMIT: RateLimitConfig = { windowMs: 60_000, maxRequests: 60 };
export const IP_LIMIT: RateLimitConfig = { windowMs: 60_000, maxRequests: 30 };

export function checkRateLimit(key: string, config: RateLimitConfig): { allowed: boolean; retryAfter?: number } {
    const now = Date.now();
    const entry = store.get(key);

    if (!entry || now > entry.resetAt) {
        store.set(key, { count: 1, resetAt: now + config.windowMs });
        return { allowed: true };
    }

    if (entry.count >= config.maxRequests) {
        const retryAfter = Math.ceil((entry.resetAt - now) / 1000);
        return { allowed: false, retryAfter };
    }

    entry.count++;
    return { allowed: true };
}

export function rateLimitMiddleware(req: Request, tenantId?: string): Response | null {
    const ip = req.headers.get("x-forwarded-for") || "unknown";

    const ipCheck = checkRateLimit(`ip:${ip}`, IP_LIMIT);
    if (!ipCheck.allowed) {
        return new Response(
            JSON.stringify({ error: "Too many requests" }),
            {
                status: 429,
                headers: {
                    "Content-Type": "application/json",
                    "Retry-After": String(ipCheck.retryAfter),
                },
            }
        );
    }

    if (tenantId) {
        const tenantCheck = checkRateLimit(`tenant:${tenantId}`, TENANT_LIMIT);
        if (!tenantCheck.allowed) {
            return new Response(
                JSON.stringify({ error: "Too many requests for this tenant" }),
                {
                    status: 429,
                    headers: {
                        "Content-Type": "application/json",
                        "Retry-After": String(tenantCheck.retryAfter),
                    },
                }
            );
        }
    }

    return null; // Allowed
}

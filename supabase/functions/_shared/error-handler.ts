export class AppError extends Error {
    constructor(
        public statusCode: number,
        message: string,
        public code?: string
    ) {
        super(message);
    }
}

export function errorResponse(error: unknown): Response {
    if (error instanceof AppError) {
        return new Response(
            JSON.stringify({ error: error.message, code: error.code }),
            { status: error.statusCode, headers: { "Content-Type": "application/json" } }
        );
    }

    console.error("[UNHANDLED]", error);
    return new Response(
        JSON.stringify({ error: "Internal server error" }),
        { status: 500, headers: { "Content-Type": "application/json" } }
    );
}

export function jsonResponse(data: unknown, status = 200): Response {
    return new Response(
        JSON.stringify(data),
        { status, headers: { "Content-Type": "application/json" } }
    );
}

import { createClient, SupabaseClient } from "https://esm.sh/@supabase/supabase-js@2";

let client: SupabaseClient | null = null;

export function getServiceClient(): SupabaseClient {
    if (!client) {
        const url = Deno.env.get("SUPABASE_URL")!;
        const key = Deno.env.get("SUPABASE_SERVICE_ROLE_KEY")!;
        client = createClient(url, key, {
            auth: { autoRefreshToken: false, persistSession: false },
        });
    }
    return client;
}

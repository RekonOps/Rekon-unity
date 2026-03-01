-- Vault helper functions for refresh token management
-- Name convention: oauth_refresh:jira:{tenant_id}:{user_id}
-- SECURITY DEFINER: 서비스 역할에서만 호출 가능

CREATE OR REPLACE FUNCTION private.store_refresh_token(
    p_tenant_id UUID,
    p_user_id UUID,
    p_token TEXT
) RETURNS UUID AS $$
DECLARE
    v_secret_name TEXT;
    v_secret_id UUID;
BEGIN
    v_secret_name := 'oauth_refresh:jira:' || p_tenant_id || ':' || p_user_id;

    -- Delete existing secret if any
    DELETE FROM vault.secrets WHERE name = v_secret_name;

    -- Insert new secret
    INSERT INTO vault.secrets (name, secret)
    VALUES (v_secret_name, p_token)
    RETURNING id INTO v_secret_id;

    RETURN v_secret_id;
END;
$$ LANGUAGE plpgsql SECURITY DEFINER;

CREATE OR REPLACE FUNCTION private.get_refresh_token(
    p_tenant_id UUID,
    p_user_id UUID
) RETURNS TEXT AS $$
DECLARE
    v_secret_name TEXT;
    v_token TEXT;
BEGIN
    v_secret_name := 'oauth_refresh:jira:' || p_tenant_id || ':' || p_user_id;

    SELECT decrypted_secret INTO v_token
    FROM vault.decrypted_secrets
    WHERE name = v_secret_name;

    RETURN v_token;
END;
$$ LANGUAGE plpgsql SECURITY DEFINER;

CREATE OR REPLACE FUNCTION private.delete_refresh_token(
    p_tenant_id UUID,
    p_user_id UUID
) RETURNS VOID AS $$
DECLARE
    v_secret_name TEXT;
BEGIN
    v_secret_name := 'oauth_refresh:jira:' || p_tenant_id || ':' || p_user_id;
    DELETE FROM vault.secrets WHERE name = v_secret_name;
END;
$$ LANGUAGE plpgsql SECURITY DEFINER;

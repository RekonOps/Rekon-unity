-- Auth Broker 테이블 생성
-- private 스키마: 서비스 역할 키로만 접근 가능

-- Private schema for auth data
CREATE SCHEMA IF NOT EXISTS private;

-- Tenants table
CREATE TABLE private.tenants (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    name TEXT NOT NULL,
    created_at TIMESTAMPTZ DEFAULT now(),
    updated_at TIMESTAMPTZ DEFAULT now()
);

-- Users table
CREATE TABLE private.users (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id UUID NOT NULL REFERENCES private.tenants(id),
    external_id TEXT NOT NULL, -- Unity user ID
    display_name TEXT,
    created_at TIMESTAMPTZ DEFAULT now(),
    updated_at TIMESTAMPTZ DEFAULT now(),
    UNIQUE(tenant_id, external_id)
);

-- OAuth connections
CREATE TABLE private.oauth_connections (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id UUID NOT NULL REFERENCES private.users(id),
    tenant_id UUID NOT NULL REFERENCES private.tenants(id),
    provider TEXT NOT NULL DEFAULT 'jira',
    cloud_id TEXT,
    project_key TEXT,
    scopes TEXT[] DEFAULT '{}',
    refresh_secret_id UUID, -- Reference to vault.secrets
    status TEXT NOT NULL DEFAULT 'pending',
    state TEXT, -- CSRF state
    state_expires_at TIMESTAMPTZ,
    refreshing_at TIMESTAMPTZ, -- 동시 refresh 방지용 락
    created_at TIMESTAMPTZ DEFAULT now(),
    updated_at TIMESTAMPTZ DEFAULT now(),
    UNIQUE(user_id, provider)
);

-- Indexes
CREATE INDEX idx_users_tenant ON private.users(tenant_id);
CREATE INDEX idx_users_external ON private.users(external_id);
CREATE INDEX idx_connections_user ON private.oauth_connections(user_id);
CREATE INDEX idx_connections_state ON private.oauth_connections(state);

-- RLS (서비스 역할만 접근)
ALTER TABLE private.tenants ENABLE ROW LEVEL SECURITY;
ALTER TABLE private.users ENABLE ROW LEVEL SECURITY;
ALTER TABLE private.oauth_connections ENABLE ROW LEVEL SECURITY;

-- Updated_at 트리거
CREATE OR REPLACE FUNCTION private.update_updated_at()
RETURNS TRIGGER AS $$
BEGIN
    NEW.updated_at = now();
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

CREATE TRIGGER tenants_updated_at BEFORE UPDATE ON private.tenants
    FOR EACH ROW EXECUTE FUNCTION private.update_updated_at();
CREATE TRIGGER users_updated_at BEFORE UPDATE ON private.users
    FOR EACH ROW EXECUTE FUNCTION private.update_updated_at();
CREATE TRIGGER connections_updated_at BEFORE UPDATE ON private.oauth_connections
    FOR EACH ROW EXECUTE FUNCTION private.update_updated_at();

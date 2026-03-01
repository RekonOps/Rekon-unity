-- private 스키마 권한 수정
-- service_role에 private 스키마 접근 권한 부여

-- 1. 스키마 사용 권한
GRANT USAGE ON SCHEMA private TO service_role;
GRANT ALL ON ALL TABLES IN SCHEMA private TO service_role;
GRANT ALL ON ALL SEQUENCES IN SCHEMA private TO service_role;
GRANT ALL ON ALL FUNCTIONS IN SCHEMA private TO service_role;

-- 향후 생성되는 객체에도 자동 권한 부여
ALTER DEFAULT PRIVILEGES IN SCHEMA private
    GRANT ALL ON TABLES TO service_role;
ALTER DEFAULT PRIVILEGES IN SCHEMA private
    GRANT ALL ON SEQUENCES TO service_role;
ALTER DEFAULT PRIVILEGES IN SCHEMA private
    GRANT ALL ON FUNCTIONS TO service_role;

-- 2. PostgREST에서 private 스키마 노출 (supabase_admin 필요)
-- Supabase Cloud에서는 대시보드에서 설정해야 하므로 주석 처리
-- ALTER ROLE authenticator SET pgrst.db_schemas = 'public, storage, private';
-- NOTIFY pgrst, 'reload config';

-- 3. RLS 정책 추가 - service_role은 모든 작업 허용
CREATE POLICY "service_role_all_tenants" ON private.tenants
    FOR ALL
    TO service_role
    USING (true)
    WITH CHECK (true);

CREATE POLICY "service_role_all_users" ON private.users
    FOR ALL
    TO service_role
    USING (true)
    WITH CHECK (true);

CREATE POLICY "service_role_all_oauth_connections" ON private.oauth_connections
    FOR ALL
    TO service_role
    USING (true)
    WITH CHECK (true);

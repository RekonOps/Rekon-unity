-- private 스키마 테이블을 public 스키마로 이동
-- PostgREST가 private 스키마를 캐시에 로드하지 못하는 Supabase Cloud 제한 해결

-- 1. 테이블 이동
ALTER TABLE private.tenants SET SCHEMA public;
ALTER TABLE private.users SET SCHEMA public;
ALTER TABLE private.oauth_connections SET SCHEMA public;

-- 2. 기존 private 스키마 RLS 정책 삭제 (스키마 이동 시 자동 이동됨, 확인용)
-- DROP POLICY IF EXISTS "service_role_all_tenants" ON public.tenants;
-- DROP POLICY IF EXISTS "service_role_all_users" ON public.users;
-- DROP POLICY IF EXISTS "service_role_all_oauth_connections" ON public.oauth_connections;

-- 3. RLS 정책 재설정 - anon/authenticated 역할 차단, service_role만 허용
-- service_role은 기본적으로 RLS bypass이므로 별도 정책 불필요
-- anon과 authenticated에 대해 모든 접근을 차단하는 정책

-- tenants: anon/authenticated 접근 차단
DROP POLICY IF EXISTS "service_role_all_tenants" ON public.tenants;
CREATE POLICY "deny_anon_tenants" ON public.tenants
    FOR ALL TO anon USING (false);
CREATE POLICY "deny_authenticated_tenants" ON public.tenants
    FOR ALL TO authenticated USING (false);

-- users: anon/authenticated 접근 차단
DROP POLICY IF EXISTS "service_role_all_users" ON public.users;
CREATE POLICY "deny_anon_users" ON public.users
    FOR ALL TO anon USING (false);
CREATE POLICY "deny_authenticated_users" ON public.users
    FOR ALL TO authenticated USING (false);

-- oauth_connections: anon/authenticated 접근 차단
DROP POLICY IF EXISTS "service_role_all_oauth_connections" ON public.oauth_connections;
CREATE POLICY "deny_anon_oauth_connections" ON public.oauth_connections
    FOR ALL TO anon USING (false);
CREATE POLICY "deny_authenticated_oauth_connections" ON public.oauth_connections
    FOR ALL TO authenticated USING (false);

-- 4. Vault 헬퍼 함수도 public 스키마로 이동
ALTER FUNCTION private.store_refresh_token(UUID, UUID, TEXT) SET SCHEMA public;
ALTER FUNCTION private.get_refresh_token(UUID, UUID) SET SCHEMA public;
ALTER FUNCTION private.delete_refresh_token(UUID, UUID) SET SCHEMA public;

-- 5. private 스키마가 비었으므로 삭제 (선택적)
-- DROP SCHEMA IF EXISTS private;

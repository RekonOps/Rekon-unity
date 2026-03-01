-- Vault 대신 oauth_connections 테이블에 refresh_token을 직접 저장
-- pgsodium 권한 문제(permission denied for function _crypto_aead_det_noncegen)로 인해
-- MVP 단계에서는 평문 저장, 프로덕션에서는 Vault 또는 별도 암호화 사용 권장

ALTER TABLE public.oauth_connections
ADD COLUMN IF NOT EXISTS refresh_token TEXT;

-- refresh_token 컬럼 설명
COMMENT ON COLUMN public.oauth_connections.refresh_token IS 'Jira OAuth refresh token - MVP에서는 평문 저장, 프로덕션에서는 Vault 사용 권장';

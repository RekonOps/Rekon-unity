-- oauth_connections 테이블에 site_url 컬럼 추가
-- Jira OAuth accessible-resources API 응답에서 추출한 사이트 URL 저장
-- Unity 클라이언트가 jiraSiteUrl을 자동으로 설정하는 데 사용됩니다

ALTER TABLE public.oauth_connections
ADD COLUMN IF NOT EXISTS site_url TEXT;

COMMENT ON COLUMN public.oauth_connections.site_url IS 'Jira 사이트 기본 URL (예: https://yourcompany.atlassian.net). accessible-resources API 응답에서 자동 추출';

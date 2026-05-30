# Rekon Unity SDK — 보안 정책 & SDK 무결성 검증

> 최종 수정: 2026-05-30 | 버전: v0.4.0+

---

## 1. 보안 취약점 제보

보안 취약점을 발견하셨으면 **공개 이슈 대신** 아래 채널로 제보해 주세요.

- **이메일**: rekonops.dev@gmail.com (제목: `[SECURITY] 간단 설명`)
- **응답 목표**: 영업일 3일 이내 초기 응답, 14일 이내 처리 계획 공유

제보 내용에 포함해 주시면 도움이 됩니다:
- 영향 범위(어떤 버전, 어떤 환경)
- 재현 방법 또는 PoC
- 예상 심각도(CVSS 또는 직관적 설명)

---

## 2. SDK 산출물 무결성 검증 (A7)

모든 릴리스에는 `rekon-unity-vX.Y.Z.tgz` tarball과 함께 아래 두 파일이 첨부됩니다:

| 파일 | 설명 |
|------|------|
| `rekon-unity-vX.Y.Z.tgz` | UPM 설치용 소스 tarball (git archive) |
| `rekon-unity-vX.Y.Z.tgz.sha256` | SHA-256 체크섬 파일 |
| `rekon-unity-vX.Y.Z-sbom.json` | CycloneDX 1.5 형식 SBOM |

### 2-1. SHA-256 체크섬 검증 방법

"내가 다운로드한 tarball이 RekonOps가 서명한 릴리스와 동일한가"를 확인합니다.

**macOS / Linux**

```bash
# 1. GitHub Releases 에서 두 파일 다운로드
curl -LO https://github.com/RekonOps/Rekon-unity/releases/download/v1.0.0/rekon-unity-v1.0.0.tgz
curl -LO https://github.com/RekonOps/Rekon-unity/releases/download/v1.0.0/rekon-unity-v1.0.0.tgz.sha256

# 2. 검증 (통과 시 "OK" 출력)
sha256sum -c rekon-unity-v1.0.0.tgz.sha256
```

**Windows (PowerShell)**

```powershell
# 1. 두 파일 다운로드
Invoke-WebRequest -Uri "https://github.com/RekonOps/Rekon-unity/releases/download/v1.0.0/rekon-unity-v1.0.0.tgz" -OutFile "rekon-unity-v1.0.0.tgz"
Invoke-WebRequest -Uri "https://github.com/RekonOps/Rekon-unity/releases/download/v1.0.0/rekon-unity-v1.0.0.tgz.sha256" -OutFile "rekon-unity-v1.0.0.tgz.sha256"

# 2. 체크섬 파일에서 기대값 추출
$expected = (Get-Content "rekon-unity-v1.0.0.tgz.sha256" -Raw).Split(" ")[0].Trim()

# 3. 실제 파일 해시 계산
$actual = (Get-FileHash "rekon-unity-v1.0.0.tgz" -Algorithm SHA256).Hash.ToLower()

# 4. 비교
if ($expected -eq $actual) { Write-Host "OK — 체크섬 일치" } else { Write-Host "FAIL — 파일이 변조되었을 수 있습니다" }
```

> **검증 실패 시**: 다운로드를 삭제하고 위의 보안 채널로 즉시 제보해 주세요.

### 2-2. Git URL 방식으로 설치한 경우

UPM Git URL(`https://github.com/RekonOps/Rekon-unity.git#vX.Y.Z`) 방식은 Unity Package Manager가 지정 태그를 직접 체크아웃합니다. 무결성 확인 방법:

```bash
# Unity 프로젝트의 PackageCache 경로에서 확인
# (일반적으로 Library/PackageCache/dev.rekonops.rekon@<hash>/)
# 체크아웃된 HEAD가 올바른 태그를 가리키는지 확인
git -C Library/PackageCache/dev.rekonops.rekon@* log -1 --format="%H %D"
```

공식 커밋 해시는 [GitHub Releases](https://github.com/RekonOps/Rekon-unity/releases) 페이지에서 확인 가능합니다.

### 2-3. SBOM 확인

`-sbom.json` 파일은 [CycloneDX 1.5](https://cyclonedx.org/specification/overview/) 형식입니다.
`metadata.component.hashes[].content` 필드에 tarball SHA-256이 포함되어 있어 체크섬 파일과 교차 검증이 가능합니다.

```bash
# jq 로 SBOM 내 SHA-256 추출
jq -r '.metadata.component.hashes[] | select(.alg=="SHA-256") | .content' rekon-unity-v1.0.0-sbom.json
```

> Rekon Unity SDK는 외부 npm/pip 런타임 의존성이 없습니다(Unity 2022.3+ 내장 API만 사용).
> SBOM `components` 배열이 비어 있는 것은 정상입니다.

---

## 3. 보안 통제 요약

| 영역 | 현황 |
|------|------|
| **아웃바운드 전송처** | RekonOps 통제 도메인 하드코딩 — 임의 URL 전송 경로 없음 |
| **SDK 내 민감 키** | `service_role` / `anon` 키 미포함 — 모든 통신은 웹 프록시 경유 |
| **전송 암호화** | HTTPS (TLS 1.2+) 강제 |
| **로컬 세션 토큰** | AES-256-CBC 암호화 저장 |
| **로그 마스킹** | 이메일·공인IP·OAuth 토큰·Bearer 자동 마스킹 (always-on) |
| **산출물 무결성** | SHA-256 체크섬 + CycloneDX SBOM — 매 릴리스 자동 생성·첨부 |

---

## 4. 지원 버전

| 버전 | 보안 패치 지원 |
|------|----------------|
| 최신 릴리스 (main) | 지원 |
| 이전 마이너 | 치명적 취약점에 한해 지원 |
| 그 이전 | 미지원 (업그레이드 권장) |

---

## 5. 관련 문서

- [README.md](./README.md) — 설치 및 아키텍처 개요
- [CHANGELOG.md](./CHANGELOG.md) — 버전별 변경 이력
- [GitHub Releases](https://github.com/RekonOps/Rekon-unity/releases) — tarball + 체크섬 + SBOM 다운로드

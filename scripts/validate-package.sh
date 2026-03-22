#!/usr/bin/env bash
# validate-package.sh
# Rekon UPM 패키지 검증 스크립트
#
# 기능:
#   1. package.json 필수 필드 검증
#   2. Assembly Definition (.asmdef) 참조 무결성 검증
#   3. 네임스페이스 일관성 검증 (RekonOps.Rekon)
#
# 사용법:
#   chmod +x scripts/validate-package.sh
#   ./scripts/validate-package.sh
#
# 종료 코드:
#   0 = 검증 성공
#   1 = 검증 실패

set -euo pipefail

# ──────────────────────────────────────────────────────────────────────────────
# 색상 출력 설정
# ──────────────────────────────────────────────────────────────────────────────

RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# ──────────────────────────────────────────────────────────────────────────────
# 상태 추적 변수
# ──────────────────────────────────────────────────────────────────────────────

ERRORS=0
WARNINGS=0

# ──────────────────────────────────────────────────────────────────────────────
# 로그 함수
# ──────────────────────────────────────────────────────────────────────────────

log_info()    { echo -e "${BLUE}[정보]${NC} $1"; }
log_ok()      { echo -e "${GREEN}[통과]${NC} $1"; }
log_warn()    { echo -e "${YELLOW}[경고]${NC} $1"; WARNINGS=$((WARNINGS + 1)); }
log_error()   { echo -e "${RED}[오류]${NC} $1"; ERRORS=$((ERRORS + 1)); }
log_section() { echo -e "\n${BLUE}━━━ $1 ━━━${NC}"; }

# ──────────────────────────────────────────────────────────────────────────────
# 실행 경로 확인 (패키지 루트에서 실행해야 함)
# ──────────────────────────────────────────────────────────────────────────────

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PKG_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

log_info "패키지 루트: $PKG_ROOT"

if [ ! -f "$PKG_ROOT/package.json" ]; then
    log_error "package.json을 찾을 수 없습니다. 패키지 루트 디렉토리에서 실행해 주세요."
    exit 1
fi

# ──────────────────────────────────────────────────────────────────────────────
# 1단계: package.json 필수 필드 검증
# ──────────────────────────────────────────────────────────────────────────────

log_section "1단계: package.json 필수 필드 검증"

PACKAGE_JSON="$PKG_ROOT/package.json"

# jq가 설치되어 있는지 확인
if ! command -v jq &> /dev/null; then
    log_warn "jq가 설치되어 있지 않습니다. package.json 필드 검증을 grep 방식으로 수행합니다."
    USE_JQ=false
else
    USE_JQ=true
fi

# 필수 필드 목록 (UPM 요구사항)
REQUIRED_FIELDS=("name" "version" "displayName" "description" "unity" "author")

for field in "${REQUIRED_FIELDS[@]}"; do
    if $USE_JQ; then
        value=$(jq -r ".$field // empty" "$PACKAGE_JSON" 2>/dev/null)
    else
        value=$(grep -o "\"$field\"[[:space:]]*:[[:space:]]*\"[^\"]*\"" "$PACKAGE_JSON" | head -1)
    fi

    if [ -z "$value" ]; then
        log_error "package.json 필수 필드 누락: '$field'"
    else
        log_ok "package.json '$field' 필드 존재"
    fi
done

# name 형식 검증 (com.{company}.{package} 형식)
if $USE_JQ; then
    PKG_NAME=$(jq -r '.name // empty' "$PACKAGE_JSON")
else
    PKG_NAME=$(grep -o '"name"[[:space:]]*:[[:space:]]*"[^"]*"' "$PACKAGE_JSON" | sed 's/.*: *"//' | sed 's/"//')
fi

if [[ "$PKG_NAME" =~ ^com\.[a-z0-9.-]+\.[a-z0-9-]+$ ]]; then
    log_ok "package.json name 형식 올바름: $PKG_NAME"
else
    log_error "package.json name 형식 오류: '$PKG_NAME' (예상: com.{company}.{package})"
fi

# 버전 형식 검증 (Semantic Versioning)
if $USE_JQ; then
    PKG_VERSION=$(jq -r '.version // empty' "$PACKAGE_JSON")
else
    PKG_VERSION=$(grep -o '"version"[[:space:]]*:[[:space:]]*"[^"]*"' "$PACKAGE_JSON" | sed 's/.*: *"//' | sed 's/"//')
fi

if [[ "$PKG_VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+(-[a-zA-Z0-9.]+)?$ ]]; then
    log_ok "package.json 버전 형식 올바름: $PKG_VERSION"
else
    log_error "package.json 버전 형식 오류: '$PKG_VERSION' (예상: X.Y.Z)"
fi

# unity 버전 형식 검증
if $USE_JQ; then
    UNITY_VER=$(jq -r '.unity // empty' "$PACKAGE_JSON")
else
    UNITY_VER=$(grep -o '"unity"[[:space:]]*:[[:space:]]*"[^"]*"' "$PACKAGE_JSON" | sed 's/.*: *"//' | sed 's/"//')
fi

if [[ "$UNITY_VER" =~ ^[0-9]{4}\.[0-9]+$ ]]; then
    log_ok "package.json unity 버전 형식 올바름: $UNITY_VER"
else
    log_error "package.json unity 버전 형식 오류: '$UNITY_VER' (예상: YYYY.X)"
fi

# ──────────────────────────────────────────────────────────────────────────────
# 2단계: Assembly Definition (.asmdef) 참조 무결성 검증
# ──────────────────────────────────────────────────────────────────────────────

log_section "2단계: Assembly Definition 참조 무결성 검증"

# asmdef 파일 목록 수집
ASMDEF_FILES=()
while IFS= read -r -d '' file; do
    ASMDEF_FILES+=("$file")
done < <(find "$PKG_ROOT" -name "*.asmdef" -not -path "*/\.*" -print0 2>/dev/null)

if [ ${#ASMDEF_FILES[@]} -eq 0 ]; then
    log_warn ".asmdef 파일을 찾을 수 없습니다."
else
    log_info "${#ASMDEF_FILES[@]}개의 .asmdef 파일을 찾았습니다."

    # 등록된 asmdef 이름 목록
    ASMDEF_NAMES=()
    for asmdef_file in "${ASMDEF_FILES[@]}"; do
        if $USE_JQ; then
            asmdef_name=$(jq -r '.name // empty' "$asmdef_file" 2>/dev/null)
        else
            asmdef_name=$(grep -o '"name"[[:space:]]*:[[:space:]]*"[^"]*"' "$asmdef_file" | head -1 | sed 's/.*: *"//' | sed 's/"//')
        fi
        if [ -n "$asmdef_name" ]; then
            ASMDEF_NAMES+=("$asmdef_name")
            log_ok "asmdef 발견: $asmdef_name ($(basename "$asmdef_file"))"
        fi
    done

    # 각 asmdef의 references 참조 검증
    for asmdef_file in "${ASMDEF_FILES[@]}"; do
        asmdef_basename=$(basename "$asmdef_file")

        if $USE_JQ; then
            refs=$(jq -r '.references[]? // empty' "$asmdef_file" 2>/dev/null)
        else
            # grep 기반 대체: references 배열의 값 추출
            refs=$(grep -o '"[A-Za-z0-9._-]*"' "$asmdef_file" | tr -d '"' | grep '\.' || true)
        fi

        if [ -n "$refs" ]; then
            while IFS= read -r ref; do
                # GUID 형식(GUID:xxxx)은 건너뜀
                if [[ "$ref" =~ ^GUID: ]]; then
                    continue
                fi

                # 외부 패키지 참조(Unity, UnityEditor 등)는 건너뜀
                if [[ "$ref" =~ ^Unity\. ]] || [[ "$ref" =~ ^UnityEngine ]] || [[ "$ref" =~ ^UnityEditor ]]; then
                    continue
                fi

                # 내부 참조가 존재하는지 확인
                found=false
                for name in "${ASMDEF_NAMES[@]}"; do
                    if [ "$name" = "$ref" ]; then
                        found=true
                        break
                    fi
                done

                if ! $found; then
                    log_warn "asmdef '$asmdef_basename': 참조 '$ref'가 패키지 내에 없습니다 (외부 패키지일 수 있음)"
                fi
            done <<< "$refs"
        fi
    done
fi

# ──────────────────────────────────────────────────────────────────────────────
# 3단계: 네임스페이스 일관성 검증
# ──────────────────────────────────────────────────────────────────────────────

log_section "3단계: 네임스페이스 일관성 검증"

EXPECTED_NAMESPACE="RekonOps.Rekon"
NAMESPACE_ERRORS=0

# Runtime과 Editor의 .cs 파일만 검사 (Tests, Samples는 제외)
CS_FILES=()
while IFS= read -r -d '' file; do
    CS_FILES+=("$file")
done < <(find "$PKG_ROOT/Runtime" "$PKG_ROOT/Editor" -name "*.cs" -print0 2>/dev/null)

if [ ${#CS_FILES[@]} -eq 0 ]; then
    log_warn "검사할 .cs 파일을 찾을 수 없습니다."
else
    log_info "${#CS_FILES[@]}개의 .cs 파일을 검사합니다."

    for cs_file in "${CS_FILES[@]}"; do
        cs_basename=$(basename "$cs_file")

        # namespace 선언이 있는지 확인
        if ! grep -q "namespace" "$cs_file" 2>/dev/null; then
            log_warn "$cs_basename: namespace 선언 없음"
            NAMESPACE_ERRORS=$((NAMESPACE_ERRORS + 1))
            continue
        fi

        # 예상 네임스페이스 포함 여부 확인
        if ! grep -q "namespace $EXPECTED_NAMESPACE" "$cs_file" 2>/dev/null; then
            # 서브네임스페이스(예: RekonOps.Rekon.Samples)는 허용
            if ! grep -q "namespace ${EXPECTED_NAMESPACE}\." "$cs_file" 2>/dev/null; then
                actual_ns=$(grep "namespace " "$cs_file" | head -1 | awk '{print $2}' | tr -d '{')
                log_warn "$cs_basename: 네임스페이스 불일치 (발견: '$actual_ns', 예상: '$EXPECTED_NAMESPACE')"
                NAMESPACE_ERRORS=$((NAMESPACE_ERRORS + 1))
            fi
        fi
    done

    if [ $NAMESPACE_ERRORS -eq 0 ]; then
        log_ok "모든 .cs 파일의 네임스페이스가 일관됩니다: $EXPECTED_NAMESPACE"
    fi
fi

# ──────────────────────────────────────────────────────────────────────────────
# 4단계: 필수 파일/폴더 존재 확인
# ──────────────────────────────────────────────────────────────────────────────

log_section "4단계: 필수 파일 존재 확인"

REQUIRED_FILES=(
    "package.json"
    "README.md"
    "CHANGELOG.md"
    "LICENSE"
    "Runtime/Rekon.Runtime.asmdef"
    "Editor/Rekon.Editor.asmdef"
)

for file in "${REQUIRED_FILES[@]}"; do
    if [ -f "$PKG_ROOT/$file" ]; then
        log_ok "필수 파일 존재: $file"
    else
        log_error "필수 파일 누락: $file"
    fi
done

# Samples~ 폴더 확인 (없어도 경고만)
if [ -d "$PKG_ROOT/Samples~" ]; then
    log_ok "Samples~ 폴더 존재"
else
    log_warn "Samples~ 폴더 없음 (선택 사항)"
fi

# ──────────────────────────────────────────────────────────────────────────────
# 최종 결과 출력
# ──────────────────────────────────────────────────────────────────────────────

echo ""
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
echo "  Rekon 패키지 검증 결과"
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
echo -e "  패키지 이름: ${BLUE}$PKG_NAME${NC}"
echo -e "  버전:        ${BLUE}$PKG_VERSION${NC}"
echo -e "  오류:        $([ $ERRORS -eq 0 ] && echo "${GREEN}$ERRORS${NC}" || echo "${RED}$ERRORS${NC}")"
echo -e "  경고:        $([ $WARNINGS -eq 0 ] && echo "${GREEN}$WARNINGS${NC}" || echo "${YELLOW}$WARNINGS${NC}")"
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"

if [ $ERRORS -eq 0 ]; then
    echo -e "${GREEN}검증 통과: 패키지를 배포할 준비가 되었습니다.${NC}"
    exit 0
else
    echo -e "${RED}검증 실패: 위의 오류를 수정한 후 다시 실행해 주세요.${NC}"
    exit 1
fi

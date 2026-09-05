#!/bin/sh

set -eu

root_dir=$(CDPATH= cd -- "$(dirname -- "$0")/../.." && pwd)
temp_dir=$(mktemp -d)
trap 'rm -rf "$temp_dir"' EXIT HUP INT TERM

fail()
{
    printf '%s\n' "Account deletion smoke test failed: $1" >&2
    exit 1
}

smoke="$root_dir/deploy/smoke-account-deletion.sh"
[ -f "$smoke" ] || fail "smoke script is missing."
sh -n "$smoke" || fail "smoke script has invalid POSIX shell syntax."

deployment="$temp_dir/deployment"
mkdir -p "$deployment"
release='3333333333333333333333333333333333333333'
printf '%s\n' "$release" > "$deployment/.billwatch-release"

password_file="$temp_dir/delete-password"
printf '%s\n' 'DisposablePassword!123' > "$password_file"
chmod 600 "$password_file"

fake_bin="$temp_dir/bin"
mkdir -p "$fake_bin"
cat > "$fake_bin/git" <<'EOF'
#!/bin/sh
set -eu
if [ "${1:-}" = "-C" ]; then
    shift 2
fi
case "${1:-} ${2:-}" in
    'rev-parse HEAD') printf '%s\n' "${BILLWATCH_TEST_GIT_HEAD:?}" ;;
    'status --porcelain') printf '%s' "${BILLWATCH_TEST_GIT_STATUS:-}" ;;
    *) printf 'unexpected fake git invocation: %s\n' "$*" >&2; exit 2 ;;
esac
EOF
chmod 700 "$fake_bin/git"

cat > "$fake_bin/curl" <<'EOF'
#!/bin/sh
set -eu
: "${BILLWATCH_TEST_CURL_LOG:?}"
: "${BILLWATCH_TEST_CURL_COUNT:?}"
count=0
[ ! -f "$BILLWATCH_TEST_CURL_COUNT" ] || count="$(cat "$BILLWATCH_TEST_CURL_COUNT")"
count=$((count + 1))
printf '%s\n' "$count" > "$BILLWATCH_TEST_CURL_COUNT"
printf '%s\n' "$*" >> "$BILLWATCH_TEST_CURL_LOG"

output=''
while [ "$#" -gt 0 ]; do
    case "$1" in
        --output) output="$2"; shift 2 ;;
        *) shift ;;
    esac
done

case "$count" in
    1)
        [ -n "$output" ] && printf '{"accessToken":"delete-token"}' > "$output"
        printf '200'
        ;;
    2) printf '%s' "${BILLWATCH_TEST_PREDELETE_EXPORT_CODE:-200}" ;;
    3) printf '%s' "${BILLWATCH_TEST_DELETE_CODE:-204}" ;;
    4) printf '%s' "${BILLWATCH_TEST_POSTDELETE_EXPORT_CODE:-404}" ;;
    5) printf '%s' "${BILLWATCH_TEST_RELOGIN_CODE:-401}" ;;
    *) exit 2 ;;
esac
EOF
chmod 700 "$fake_bin/curl"

evidence="$temp_dir/account-delete.state"

run_smoke()
{
    rm -f "$evidence" "$temp_dir/curl.count"
    : > "$temp_dir/curl.log"
    PATH="$fake_bin:$PATH" \
    BILLWATCH_TEST_GIT_HEAD="${BILLWATCH_TEST_GIT_HEAD_VALUE:-$release}" \
    BILLWATCH_TEST_GIT_STATUS="${BILLWATCH_TEST_GIT_STATUS_VALUE:-}" \
    BILLWATCH_TEST_CURL_LOG="$temp_dir/curl.log" \
    BILLWATCH_TEST_CURL_COUNT="$temp_dir/curl.count" \
    BILLWATCH_TEST_PREDELETE_EXPORT_CODE="${BILLWATCH_TEST_PREDELETE_EXPORT_CODE_VALUE:-200}" \
    BILLWATCH_TEST_DELETE_CODE="${BILLWATCH_TEST_DELETE_CODE_VALUE:-204}" \
    BILLWATCH_TEST_POSTDELETE_EXPORT_CODE="${BILLWATCH_TEST_POSTDELETE_EXPORT_CODE_VALUE:-404}" \
    BILLWATCH_TEST_RELOGIN_CODE="${BILLWATCH_TEST_RELOGIN_CODE_VALUE:-401}" \
    BILLWATCH_ACCOUNT_DELETE_SMOKE_ALLOW="${BILLWATCH_ACCOUNT_DELETE_SMOKE_ALLOW_VALUE:-true}" \
    BILLWATCH_ACCOUNT_DELETE_SMOKE_CONFIRM="${BILLWATCH_ACCOUNT_DELETE_SMOKE_CONFIRM_VALUE:-DELETE-THROWAWAY-ACCOUNT}" \
    BILLWATCH_ACCOUNT_DELETE_SMOKE_EMAIL="${BILLWATCH_ACCOUNT_DELETE_SMOKE_EMAIL_VALUE:-delete-me@example.test}" \
    BILLWATCH_ACCOUNT_DELETE_SMOKE_CONFIRM_EMAIL="${BILLWATCH_ACCOUNT_DELETE_SMOKE_CONFIRM_EMAIL_VALUE:-delete-me@example.test}" \
    BILLWATCH_ACCOUNT_DELETE_SMOKE_PASSWORD_FILE="${BILLWATCH_ACCOUNT_DELETE_SMOKE_PASSWORD_FILE_VALUE:-$password_file}" \
    BILLWATCH_ACCOUNT_DELETE_SMOKE_EVIDENCE_FILE="${BILLWATCH_ACCOUNT_DELETE_SMOKE_EVIDENCE_FILE_VALUE:-$evidence}" \
    BILLWATCH_SMOKE_EMAIL="${BILLWATCH_SMOKE_EMAIL_VALUE:-primary@example.test}" \
    sh "$smoke" "$deployment" https://api.example.test
}

BILLWATCH_ACCOUNT_DELETE_SMOKE_ALLOW_VALUE=false
if run_smoke >/dev/null 2>&1; then fail "smoke accepted execution without explicit destructive opt-in."; fi
[ ! -f "$temp_dir/curl.count" ] || fail "smoke made a request before destructive opt-in passed."
BILLWATCH_ACCOUNT_DELETE_SMOKE_ALLOW_VALUE=true

BILLWATCH_ACCOUNT_DELETE_SMOKE_CONFIRM_VALUE='DELETE'
if run_smoke >/dev/null 2>&1; then fail "smoke accepted an insufficient destructive confirmation phrase."; fi
BILLWATCH_ACCOUNT_DELETE_SMOKE_CONFIRM_VALUE='DELETE-THROWAWAY-ACCOUNT'

BILLWATCH_ACCOUNT_DELETE_SMOKE_CONFIRM_EMAIL_VALUE='other@example.test'
if run_smoke >/dev/null 2>&1; then fail "smoke accepted a confirmation email that did not match the disposable identity."; fi
BILLWATCH_ACCOUNT_DELETE_SMOKE_CONFIRM_EMAIL_VALUE='delete-me@example.test'

BILLWATCH_SMOKE_EMAIL_VALUE='delete-me@example.test'
if run_smoke >/dev/null 2>&1; then fail "smoke accepted an identity also configured for another acceptance phase."; fi
BILLWATCH_SMOKE_EMAIL_VALUE='primary@example.test'

chmod 644 "$password_file"
if run_smoke >/dev/null 2>&1; then fail "smoke accepted a weakly permissioned password file."; fi
chmod 600 "$password_file"

ln -s "$password_file" "$temp_dir/password-link"
BILLWATCH_ACCOUNT_DELETE_SMOKE_PASSWORD_FILE_VALUE="$temp_dir/password-link"
if run_smoke >/dev/null 2>&1; then fail "smoke accepted a symbolic-link password file."; fi
BILLWATCH_ACCOUNT_DELETE_SMOKE_PASSWORD_FILE_VALUE="$password_file"

BILLWATCH_TEST_GIT_HEAD_VALUE='4444444444444444444444444444444444444444'
if run_smoke >/dev/null 2>&1; then fail "smoke accepted a deployment head different from the release marker."; fi
BILLWATCH_TEST_GIT_HEAD_VALUE="$release"

BILLWATCH_TEST_GIT_STATUS_VALUE=' M deploy/example.sh'
if run_smoke >/dev/null 2>&1; then fail "smoke accepted tracked deployment modifications."; fi
BILLWATCH_TEST_GIT_STATUS_VALUE=''

BILLWATCH_ACCOUNT_DELETE_SMOKE_EVIDENCE_FILE_VALUE="$deployment/delete.state"
if run_smoke >/dev/null 2>&1; then fail "smoke accepted an evidence path inside the deployment checkout."; fi
BILLWATCH_ACCOUNT_DELETE_SMOKE_EVIDENCE_FILE_VALUE="$evidence"

run_smoke > "$temp_dir/pass.out"
grep -q 'disposable account deletion proof passed' "$temp_dir/pass.out" ||
    fail "successful deletion proof was not reported."
[ "$(cat "$temp_dir/curl.count")" = '5' ] || fail "expected five controlled HTTP operations."
[ -f "$evidence" ] || fail "successful proof did not write release evidence."
[ "$(stat -c '%a' "$evidence")" = '600' ] || fail "evidence file is not mode 600."
grep -q '^RESULT=complete$' "$evidence" || fail "evidence did not record completion."
grep -q "^RELEASE_SHA=$release$" "$evidence" || fail "evidence did not pin the deployed release."
grep -q '^PASSED_PHASES=account-deletion$' "$evidence" || fail "evidence omitted the deletion phase."
if grep -q 'DisposablePassword!123\|delete-token' "$temp_dir/curl.log" "$evidence"; then
    fail "credentials or bearer tokens leaked into command arguments/evidence."
fi
grep -q -- '--request DELETE' "$temp_dir/curl.log" || fail "proof never issued the account deletion request."

BILLWATCH_TEST_DELETE_CODE_VALUE=409
if run_smoke >/dev/null 2>&1; then fail "smoke accepted a failed account deletion."; fi
BILLWATCH_TEST_DELETE_CODE_VALUE=204

BILLWATCH_TEST_POSTDELETE_EXPORT_CODE_VALUE=200
if run_smoke >/dev/null 2>&1; then fail "smoke accepted an identity that still resolved after deletion."; fi
BILLWATCH_TEST_POSTDELETE_EXPORT_CODE_VALUE=404

BILLWATCH_TEST_RELOGIN_CODE_VALUE=200
if run_smoke >/dev/null 2>&1; then fail "smoke accepted credentials that still authenticated after deletion."; fi
BILLWATCH_TEST_RELOGIN_CODE_VALUE=401

if BILLWATCH_ACCOUNT_DELETE_SMOKE_ALLOW=true \
   BILLWATCH_ACCOUNT_DELETE_SMOKE_CONFIRM=DELETE-THROWAWAY-ACCOUNT \
   BILLWATCH_ACCOUNT_DELETE_SMOKE_EMAIL=delete-me@example.test \
   BILLWATCH_ACCOUNT_DELETE_SMOKE_CONFIRM_EMAIL=delete-me@example.test \
   BILLWATCH_ACCOUNT_DELETE_SMOKE_PASSWORD_FILE="$password_file" \
   BILLWATCH_ACCOUNT_DELETE_SMOKE_EVIDENCE_FILE="$evidence" \
   sh "$smoke" "$deployment" http://api.example.test >/dev/null 2>&1; then
    fail "smoke accepted a non-HTTPS API URL."
fi

printf '%s\n' 'Account deletion smoke tests passed.'

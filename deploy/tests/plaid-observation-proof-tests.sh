#!/bin/sh
set -eu
root_dir=$(CDPATH= cd -- "$(dirname -- "$0")/../.." && pwd)
temp=$(mktemp -d); trap 'rm -rf "$temp"' EXIT HUP INT TERM
fail(){ printf '%s\n' "Plaid observation proof test failed: $1" >&2; exit 1; }
script="$root_dir/deploy/run-plaid-observation-proof.sh"
sh -n "$script" || fail "invalid POSIX shell syntax"
release=1234567890abcdef1234567890abcdef12345678
connection=11111111-2222-3333-4444-555555555555
session=aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee
deployment="$temp/deployment"; evidence_dir="$temp/evidence"; bin="$temp/bin"
mkdir -p "$deployment" "$evidence_dir" "$bin"
printf '%s\n' "$release" > "$deployment/.billwatch-release"
password="$temp/password"; printf '%s\n' 'correct horse battery staple' > "$password"; chmod 600 "$password"
cat > "$bin/git" <<'EOF'
#!/bin/sh
case "$*" in *'rev-parse HEAD'*) printf '%s\n' "$TEST_RELEASE";; *'status --porcelain --untracked-files=no'*) printf '%s' "${TEST_STATUS:-}";; *) exit 1;; esac
EOF
cat > "$bin/curl" <<'EOF'
#!/bin/sh
output=; url=
while [ "$#" -gt 0 ]; do case "$1" in --output) shift; output=$1;; http*) url=$1;; esac; shift; done
code=200
case "$url" in
*/api/auth/login) body='{"accessToken":"test-token"}' ;;
*/api/bank-connections) body="[{\"id\":\"$TEST_CONNECTION\",\"status\":0,\"lastSuccessfulSyncAtUtc\":\"2026-09-05T12:00:00Z\"}]" ;;
*/update-link-token) body="{\"sessionId\":\"$TEST_SESSION\",\"hostedLinkUrl\":\"${TEST_HOSTED_URL:-https://secure.plaid.com/link/test}\"}" ;;
*/link-session/*/complete) body='{"status":"Completed"}' ;;
*/accounts/sync|*/transactions/sync) body='{}' ;;
*) code=404; body='{}' ;;
esac
[ -z "$output" ] || printf '%s\n' "$body" > "$output"
printf '%s' "$code"
EOF
chmod 700 "$bin/git" "$bin/curl"
pending="$evidence_dir/plaid.pending"; evidence="$evidence_dir/plaid.state"
run(){ env PATH="$bin:$PATH" TEST_RELEASE="$release" TEST_CONNECTION="$connection" TEST_SESSION="$session" BILLWATCH_PLAID_OBSERVATION_EMAIL=tester@billwatch.test BILLWATCH_PLAID_OBSERVATION_PASSWORD_FILE="$password" BILLWATCH_PLAID_OBSERVATION_CONNECTION_ID="$connection" BILLWATCH_PLAID_OBSERVATION_PENDING_FILE="$pending" BILLWATCH_PLAID_OBSERVATION_EVIDENCE_FILE="$evidence" "$@" sh "$script" prepare "$deployment" https://api.billwatch.test; }
if run >/dev/null 2>&1; then fail "prepare succeeded without explicit opt-in"; fi
chmod 644 "$password"; if run BILLWATCH_PLAID_OBSERVATION_ALLOW_PREPARE=true >/dev/null 2>&1; then fail "weak password permissions were accepted"; fi; chmod 600 "$password"
if run BILLWATCH_PLAID_OBSERVATION_ALLOW_PREPARE=true TEST_HOSTED_URL=https://evil.example/link >/dev/null 2>&1; then fail "non-Plaid Hosted Link URL was accepted"; fi
run BILLWATCH_PLAID_OBSERVATION_ALLOW_PREPARE=true >/dev/null || fail "valid prepare failed"
[ "$(stat -c '%a' "$pending")" = 600 ] || fail "pending evidence is not mode 600"
confirm(){ env PATH="$bin:$PATH" TEST_RELEASE="$release" TEST_CONNECTION="$connection" TEST_SESSION="$session" BILLWATCH_PLAID_OBSERVATION_EMAIL=tester@billwatch.test BILLWATCH_PLAID_OBSERVATION_PASSWORD_FILE="$password" BILLWATCH_PLAID_OBSERVATION_CONNECTION_ID="$connection" BILLWATCH_PLAID_OBSERVATION_PENDING_FILE="$pending" BILLWATCH_PLAID_OBSERVATION_EVIDENCE_FILE="$evidence" "$@" sh "$script" confirm "$deployment" https://api.billwatch.test; }
if confirm >/dev/null 2>&1; then fail "confirm succeeded without human confirmation phrase"; fi
confirm BILLWATCH_PLAID_OBSERVATION_CONFIRMATION='I completed the BillWatch Plaid update flow in Plaid Hosted Link' >/dev/null || fail "valid confirmation failed"
[ "$(stat -c '%a' "$evidence")" = 600 ] || fail "completed evidence is not mode 600"
grep -qx 'PASSED_PHASES=plaid-hosted-link-observed,plaid-update-completed,plaid-post-update-sync-active' "$evidence" || fail "completed phases are wrong"
if grep -Eq '11111111|aaaaaaaa|plaid\.com|test-token|correct horse' "$evidence"; then fail "completed evidence contains session, connection, URL, or credential material"; fi
[ ! -e "$pending" ] || fail "pending evidence remained after completion"
printf '%s\n' 'Plaid observation proof regression tests passed.'

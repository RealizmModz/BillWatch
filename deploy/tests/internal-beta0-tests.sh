#!/bin/sh

set -eu

root_dir=$(CDPATH= cd -- "$(dirname -- "$0")/../.." && pwd)
temp_dir=$(mktemp -d)
trap 'rm -rf "$temp_dir"' EXIT HUP INT TERM

fail()
{
    printf '%s\n' "Internal Beta 0 test failed: $1" >&2
    exit 1
}

runner="$root_dir/deploy/run-internal-beta0.sh"
[ -f "$runner" ] || fail "runner is missing."
sh -n "$runner" || fail "runner has invalid POSIX shell syntax."

deployment="$temp_dir/deployment"
mkdir -p "$deployment/deploy"
release='1111111111111111111111111111111111111111'
printf '%s\n' "$release" > "$deployment/.billwatch-release"

phase_log="$temp_dir/phases.log"
for script in \
    smoke-private-beta.sh \
    smoke-web-bff.sh \
    smoke-access-key-lifecycle.sh \
    smoke-plaid-lifecycle.sh \
    smoke-statement-lifecycle.sh \
    review-statement-semantics.sh
do
    cat > "$deployment/deploy/$script" <<'EOF'
#!/bin/sh
set -eu
: "${BILLWATCH_TEST_PHASE_LOG:?}"
printf '%s|%s\n' "$(basename "$0")" "$*" >> "$BILLWATCH_TEST_PHASE_LOG"
EOF
    chmod 700 "$deployment/deploy/$script"
done

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

run_beta0()
{
    PATH="$fake_bin:$PATH" \
    BILLWATCH_TEST_GIT_HEAD="${BILLWATCH_TEST_GIT_HEAD_VALUE:-$release}" \
    BILLWATCH_TEST_GIT_STATUS="${BILLWATCH_TEST_GIT_STATUS_VALUE:-}" \
    BILLWATCH_TEST_PHASE_LOG="$phase_log" \
    BILLWATCH_BETA0_ALLOW="${BILLWATCH_BETA0_ALLOW_VALUE:-true}" \
    BILLWATCH_BETA0_ALLOW_PARTIAL="${BILLWATCH_BETA0_ALLOW_PARTIAL_VALUE:-false}" \
    BILLWATCH_BETA0_RUN_ACCESS_KEY="${BILLWATCH_BETA0_RUN_ACCESS_KEY_VALUE:-true}" \
    BILLWATCH_BETA0_RUN_PLAID="${BILLWATCH_BETA0_RUN_PLAID_VALUE:-true}" \
    BILLWATCH_BETA0_RUN_STATEMENT="${BILLWATCH_BETA0_RUN_STATEMENT_VALUE:-true}" \
    BILLWATCH_BETA0_RUN_STATEMENT_SEMANTICS="${BILLWATCH_BETA0_RUN_STATEMENT_SEMANTICS_VALUE:-true}" \
    BILLWATCH_BETA0_EVIDENCE_FILE="${BILLWATCH_BETA0_EVIDENCE_FILE_VALUE:-}" \
    BILLWATCH_TEST_SENTINEL_SECRET='never-write-this-value' \
    sh "$runner" "$deployment" https://api.example.test https://web.example.test
}

BILLWATCH_BETA0_ALLOW_VALUE=false
if run_beta0 >/dev/null 2>&1; then
    fail "runner accepted execution without explicit opt-in."
fi
BILLWATCH_BETA0_ALLOW_VALUE=true

BILLWATCH_BETA0_RUN_ACCESS_KEY_VALUE=false
if run_beta0 >/dev/null 2>&1; then
    fail "runner accepted an incomplete run without explicit partial opt-in."
fi
BILLWATCH_BETA0_RUN_ACCESS_KEY_VALUE=true

BILLWATCH_BETA0_RUN_STATEMENT_SEMANTICS_VALUE=false
if run_beta0 >/dev/null 2>&1; then
    fail "runner accepted a complete result with semantic review disabled."
fi
BILLWATCH_BETA0_RUN_STATEMENT_SEMANTICS_VALUE=true

: > "$phase_log"
run_beta0 > "$temp_dir/full.out"
expected_phases='smoke-private-beta.sh|https://api.example.test https://web.example.test
smoke-web-bff.sh|https://web.example.test
smoke-access-key-lifecycle.sh|https://api.example.test
smoke-plaid-lifecycle.sh|https://api.example.test
smoke-statement-lifecycle.sh|https://api.example.test
review-statement-semantics.sh|https://api.example.test'
[ "$(cat "$phase_log")" = "$expected_phases" ] || fail "complete run did not execute all acceptance phases in order."
grep -q 'automated acceptance passed' "$temp_dir/full.out" || fail "complete run did not report completion."

BILLWATCH_TEST_GIT_HEAD_VALUE='2222222222222222222222222222222222222222'
if run_beta0 >/dev/null 2>&1; then
    fail "runner accepted a checkout that differed from the verified release marker."
fi
BILLWATCH_TEST_GIT_HEAD_VALUE="$release"

BILLWATCH_TEST_GIT_STATUS_VALUE=' M deploy/example.sh'
if run_beta0 >/dev/null 2>&1; then
    fail "runner accepted tracked deployment modifications."
fi
BILLWATCH_TEST_GIT_STATUS_VALUE=''

BILLWATCH_BETA0_RUN_ACCESS_KEY_VALUE=false
BILLWATCH_BETA0_RUN_STATEMENT_VALUE=false
BILLWATCH_BETA0_RUN_STATEMENT_SEMANTICS_VALUE=false
BILLWATCH_BETA0_ALLOW_PARTIAL_VALUE=true
: > "$phase_log"
run_beta0 > "$temp_dir/partial.out"
grep -q 'partial acceptance passed' "$temp_dir/partial.out" || fail "explicit partial run was not labeled partial."
if grep -Eq 'smoke-access-key-lifecycle\.sh|smoke-statement-lifecycle\.sh|review-statement-semantics\.sh' "$phase_log"; then
    fail "partial run executed a disabled phase."
fi
BILLWATCH_BETA0_RUN_ACCESS_KEY_VALUE=true
BILLWATCH_BETA0_RUN_STATEMENT_VALUE=true
BILLWATCH_BETA0_RUN_STATEMENT_SEMANTICS_VALUE=true
BILLWATCH_BETA0_ALLOW_PARTIAL_VALUE=false

evidence="$temp_dir/beta0.state"
BILLWATCH_BETA0_EVIDENCE_FILE_VALUE="$evidence"
run_beta0 >/dev/null
[ -f "$evidence" ] || fail "runner did not write requested evidence."
[ "$(stat -c '%a' "$evidence")" = '600' ] || fail "evidence file is not mode 600."
grep -q '^RESULT=complete$' "$evidence" || fail "evidence did not record a complete result."
grep -q "^RELEASE_SHA=$release$" "$evidence" || fail "evidence did not record the release SHA."
grep -q '^PASSED_PHASES=direct-api,web-bff,access-key,plaid,statement,statement-semantics$' "$evidence" || fail "evidence omitted the statement semantic-review phase."
if grep -q 'never-write-this-value' "$evidence"; then
    fail "evidence leaked an environment secret."
fi

BILLWATCH_BETA0_EVIDENCE_FILE_VALUE="$deployment/evidence.state"
if run_beta0 >/dev/null 2>&1; then
    fail "runner accepted an evidence path inside the deployment checkout."
fi

printf '%s\n' 'Internal Beta 0 runner tests passed.'

#!/bin/sh

set -eu

root_dir=$(CDPATH= cd -- "$(dirname -- "$0")/../.." && pwd)
temp_dir=$(mktemp -d)
trap 'rm -rf "$temp_dir"' EXIT HUP INT TERM

fail()
{
    printf '%s\n' "Alert observation proof test failed: $1" >&2
    exit 1
}

script="$root_dir/deploy/run-alert-observation-proof.sh"
[ -f "$script" ] || fail "alert observation proof script is missing."
sh -n "$script" || fail "alert observation proof script has invalid POSIX shell syntax."

release='1234567890abcdef1234567890abcdef12345678'
deployment="$temp_dir/deployment"
evidence_dir="$temp_dir/evidence"
fake_bin="$temp_dir/bin"
mkdir -p "$deployment/deploy" "$evidence_dir" "$fake_bin"
printf '%s\n' "$release" > "$deployment/.billwatch-release"
cat > "$deployment/.env.production" <<'EOF'
BILLWATCH_OPERATIONS_ALERTING_ENABLED=true
BILLWATCH_OPERATIONS_ALERT_WEBHOOK_URL=https://operations.billwatch.test/proof
EOF
chmod 600 "$deployment/.env.production"
cp "$script" "$deployment/deploy/run-alert-observation-proof.sh"

cat > "$deployment/deploy/send-operations-alert.sh" <<'EOF'
#!/bin/sh
set -eu
printf 'operations|%s|%s\n' "$2" "$3" >> "${BILLWATCH_TEST_SEND_LOG:?}"
EOF
cat > "$deployment/deploy/send-readiness-alert.sh" <<'EOF'
#!/bin/sh
set -eu
printf 'readiness|%s|%s|%s\n' "$1" "$2" "$3" >> "${BILLWATCH_TEST_SEND_LOG:?}"
EOF
chmod 700 "$deployment/deploy/"*.sh

cat > "$fake_bin/git" <<'EOF'
#!/bin/sh
set -eu
case "$*" in
    *'rev-parse HEAD'*) printf '%s\n' "${BILLWATCH_TEST_HEAD:?}" ;;
    *'status --porcelain --untracked-files=no'*) printf '%s' "${BILLWATCH_TEST_STATUS:-}" ;;
    *) exit 1 ;;
esac
EOF
chmod 700 "$fake_bin/git"

pending="$evidence_dir/alert.pending"
evidence="$evidence_dir/alert.state"
send_log="$temp_dir/send.log"

run_proof()
{
    phase=$1
    shift
    env \
        PATH="$fake_bin:$PATH" \
        BILLWATCH_TEST_HEAD="$release" \
        BILLWATCH_TEST_SEND_LOG="$send_log" \
        BILLWATCH_ALERT_PROOF_PENDING_FILE="$pending" \
        BILLWATCH_ALERT_PROOF_EVIDENCE_FILE="$evidence" \
        BILLWATCH_READINESS_ALERT_WEBHOOK_URL=https://readiness.billwatch.test/proof \
        "$@" \
        sh "$deployment/deploy/run-alert-observation-proof.sh" "$phase" "$deployment"
}

if run_proof send >/dev/null 2>&1; then fail "send phase succeeded without explicit opt-in."; fi
run_proof send BILLWATCH_ALERT_PROOF_ALLOW_SEND=true >/dev/null || fail "valid send phase failed."
[ -f "$pending" ] || fail "send phase did not write pending proof."
[ ! -e "$evidence" ] || fail "send phase incorrectly wrote completion evidence."
[ "$(stat -c '%a' "$pending")" = 600 ] || fail "pending proof was not mode 600."
grep -Fxq 'SENT_PHASES=operations-alert,external-readiness-alert' "$pending" || fail "pending proof omitted required sent phases."
challenge=$(sed -n 's/^CHALLENGE=//p' "$pending")
printf '%s\n' "$challenge" | grep -Eq '^[0-9a-f]{32}$' || fail "send phase wrote malformed challenge."
grep -Fxq "operations|private-beta-alert-proof|$challenge" "$send_log" || fail "operations proof did not carry the challenge."
grep -Fxq "readiness|private-beta-alert-proof|$challenge|$release" "$send_log" || fail "readiness proof did not carry the challenge and release."

if run_proof confirm BILLWATCH_ALERT_PROOF_CHALLENGE="$challenge" >/dev/null 2>&1; then fail "confirm phase succeeded without human confirmation phrase."; fi
if run_proof confirm BILLWATCH_ALERT_PROOF_CHALLENGE=ffffffffffffffffffffffffffffffff BILLWATCH_ALERT_PROOF_CONFIRMATION='I observed both BillWatch alert proof messages' >/dev/null 2>&1; then fail "confirm phase accepted the wrong challenge."; fi
run_proof confirm BILLWATCH_ALERT_PROOF_CHALLENGE="$challenge" BILLWATCH_ALERT_PROOF_CONFIRMATION='I observed both BillWatch alert proof messages' >/dev/null || fail "valid observation confirmation failed."
[ -f "$evidence" ] || fail "confirmation did not write evidence."
[ ! -e "$pending" ] || fail "confirmation did not consume pending proof."
[ "$(stat -c '%a' "$evidence")" = 600 ] || fail "completion evidence was not mode 600."
grep -Fxq "RELEASE_SHA=$release" "$evidence" || fail "completion evidence omitted release SHA."
grep -Fxq 'PASSED_PHASES=operations-alert-observed,external-readiness-alert-observed' "$evidence" || fail "completion evidence omitted observed phases."
if grep -Eiq 'webhook|challenge|url|token|secret|password' "$evidence"; then fail "completion evidence leaked proof or credential metadata."; fi

rm "$evidence"
: > "$send_log"
BILLWATCH_ALERT_PROOF_ALLOW_SEND=true BILLWATCH_READINESS_ALERT_WEBHOOK_URL=https://operations.billwatch.test/proof \
    PATH="$fake_bin:$PATH" BILLWATCH_TEST_HEAD="$release" BILLWATCH_TEST_SEND_LOG="$send_log" \
    BILLWATCH_ALERT_PROOF_PENDING_FILE="$pending" BILLWATCH_ALERT_PROOF_EVIDENCE_FILE="$evidence" \
    sh "$deployment/deploy/run-alert-observation-proof.sh" send "$deployment" >/dev/null 2>&1 && fail "send phase accepted one shared webhook destination."

ln -s "$evidence_dir/real-pending" "$pending"
if run_proof send BILLWATCH_ALERT_PROOF_ALLOW_SEND=true >/dev/null 2>&1; then fail "send phase accepted a symlinked pending destination."; fi
rm "$pending"

printf 'VERSION=1\nRESULT=pending-observation\nRELEASE_SHA=%s\nCHALLENGE=aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\nSENT_AT_UTC=2026-09-05T11:00:00Z\nSENT_PHASES=operations-alert,external-readiness-alert\n' "$release" > "$pending"
chmod 644 "$pending"
if run_proof confirm BILLWATCH_ALERT_PROOF_CHALLENGE=aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa BILLWATCH_ALERT_PROOF_CONFIRMATION='I observed both BillWatch alert proof messages' >/dev/null 2>&1; then fail "confirm phase accepted weak pending-proof permissions."; fi
chmod 600 "$pending"

BILLWATCH_TEST_STATUS=' M deploy/file' run_proof confirm BILLWATCH_ALERT_PROOF_CHALLENGE=aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa BILLWATCH_ALERT_PROOF_CONFIRMATION='I observed both BillWatch alert proof messages' >/dev/null 2>&1 && fail "proof accepted a dirty deployment checkout."

printf '%s\n' 'Alert observation proof regression tests passed.'

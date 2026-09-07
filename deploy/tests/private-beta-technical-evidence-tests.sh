#!/bin/sh

set -eu

root_dir=$(CDPATH= cd -- "$(dirname -- "$0")/../.." && pwd)
temp_dir=$(mktemp -d)
trap 'rm -rf "$temp_dir"' EXIT HUP INT TERM

fail()
{
    printf '%s\n' "Private-beta technical evidence test failed: $1" >&2
    exit 1
}

for script in \
    "$root_dir/deploy/run-clean-host-recovery-proof.sh" \
    "$root_dir/deploy/run-controlled-reboot-proof.sh" \
    "$root_dir/deploy/verify-private-beta-technical-evidence.sh"
do
    [ -f "$script" ] || fail "required technical evidence script is missing: $script"
    sh -n "$script" || fail "invalid POSIX shell syntax: $script"
done

release='1234567890abcdef1234567890abcdef12345678'
other_release='abcdef1234567890abcdef1234567890abcdef12'
deployment="$temp_dir/deployment"
evidence_dir="$temp_dir/evidence"
fake_bin="$temp_dir/bin"
mkdir -p "$deployment/deploy" "$evidence_dir" "$fake_bin"
printf '%s\n' "$release" > "$deployment/.billwatch-release"

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

write_evidence()
{
    path=$1
    sha=$2
    phases=$3
    cat > "$path" <<EOF
VERSION=1
RESULT=complete
RELEASE_SHA=$sha
COMPLETED_AT_UTC=2026-09-05T10:00:00Z
PASSED_PHASES=$phases
EOF
    chmod 600 "$path"
}

beta0="$evidence_dir/beta0.state"
recovery="$evidence_dir/recovery.state"
reboot="$evidence_dir/reboot.state"
combined="$evidence_dir/technical.state"
write_evidence "$beta0" "$release" 'account-deletion,direct-api,web-bff,admin-authz,access-key,plaid,statement,statement-semantics,subscription'
write_evidence "$recovery" "$release" 'clean-host-recovery'
write_evidence "$reboot" "$release" 'controlled-reboot-recovery'

run_verifier()
{
    PATH="$fake_bin:$PATH" \
    BILLWATCH_TEST_HEAD="$release" \
    BILLWATCH_BETA0_EVIDENCE_FILE="$beta0" \
    BILLWATCH_RECOVERY_PROOF_EVIDENCE_FILE="$recovery" \
    BILLWATCH_REBOOT_PROOF_EVIDENCE_FILE="$reboot" \
    BILLWATCH_TECHNICAL_EVIDENCE_FILE="${BILLWATCH_TEST_OUTPUT:-}" \
    sh "$root_dir/deploy/verify-private-beta-technical-evidence.sh" "$deployment"
}

run_verifier >/dev/null || fail "valid same-release evidence was rejected."
BILLWATCH_TEST_OUTPUT="$combined" run_verifier >/dev/null || fail "combined technical evidence could not be written."
[ -f "$combined" ] || fail "combined technical evidence was not written."
[ "$(stat -c '%a' "$combined")" = 600 ] || fail "combined technical evidence was not mode 600."
grep -Fxq "RELEASE_SHA=$release" "$combined" || fail "combined evidence omitted the release SHA."
grep -Fxq 'PASSED_PHASES=internal-beta0,clean-host-recovery,controlled-reboot-recovery' "$combined" || fail "combined evidence omitted required phases."
if grep -Eiq 'password|token|account|statement.*text|repository=' "$combined"; then
    fail "combined evidence contains forbidden sensitive-field names."
fi

write_evidence "$recovery" "$other_release" 'clean-host-recovery'
if run_verifier >/dev/null 2>&1; then fail "verifier accepted recovery evidence from another release."; fi
write_evidence "$recovery" "$release" 'clean-host-recovery'

write_evidence "$beta0" "$release" 'direct-api,web-bff'
if run_verifier >/dev/null 2>&1; then fail "verifier accepted incomplete Beta 0 phases."; fi
write_evidence "$beta0" "$release" 'account-deletion,direct-api,web-bff,admin-authz,access-key,plaid,statement,statement-semantics,subscription'

chmod 644 "$reboot"
if run_verifier >/dev/null 2>&1; then fail "verifier accepted world-readable reboot evidence."; fi
chmod 600 "$reboot"

reboot_real="$evidence_dir/reboot-real.state"
mv "$reboot" "$reboot_real"
ln -s "$reboot_real" "$reboot"
if run_verifier >/dev/null 2>&1; then fail "verifier accepted symlinked reboot evidence."; fi
rm "$reboot"
mv "$reboot_real" "$reboot"

proof_root="$temp_dir/proof-root"
proof_evidence="$evidence_dir/recovery-proof.state"
mkdir -p "$proof_root/deploy"
cp "$root_dir/deploy/run-clean-host-recovery-proof.sh" "$proof_root/deploy/"
cat > "$proof_root/deploy/run-clean-host-recovery-drill.sh" <<'EOF'
#!/bin/sh
set -eu
[ -f "$1" ]
exit 0
EOF
chmod 700 "$proof_root/deploy/run-clean-host-recovery-drill.sh"
: > "$temp_dir/recovery.env"
PATH="$fake_bin:$PATH" BILLWATCH_TEST_HEAD="$release" BILLWATCH_RECOVERY_PROOF_EVIDENCE_FILE="$proof_evidence" \
    sh "$proof_root/deploy/run-clean-host-recovery-proof.sh" "$temp_dir/recovery.env" >/dev/null || fail "recovery proof wrapper rejected a successful drill."
grep -Fxq 'PASSED_PHASES=clean-host-recovery' "$proof_evidence" || fail "recovery proof wrapper wrote incorrect evidence."
if PATH="$fake_bin:$PATH" BILLWATCH_TEST_HEAD="$release" BILLWATCH_RECOVERY_PROOF_EVIDENCE_FILE="$proof_evidence" \
    sh "$proof_root/deploy/run-clean-host-recovery-proof.sh" "$temp_dir/recovery.env" >/dev/null 2>&1; then
    fail "recovery proof wrapper overwrote existing evidence."
fi

reboot_root="$temp_dir/reboot-root"
reboot_proof="$evidence_dir/reboot-proof.state"
reboot_log="$temp_dir/reboot-child.log"
mkdir -p "$reboot_root/deploy"
printf '%s\n' "$release" > "$reboot_root/.billwatch-release"
cp "$root_dir/deploy/run-controlled-reboot-proof.sh" "$reboot_root/deploy/"
cat > "$reboot_root/deploy/run-controlled-reboot-drill.sh" <<'EOF'
#!/bin/sh
set -eu
printf '%s\n' "$1" >> "${BILLWATCH_TEST_REBOOT_LOG:?}"
exit 0
EOF
chmod 700 "$reboot_root/deploy/run-controlled-reboot-drill.sh"
PATH="$fake_bin:$PATH" BILLWATCH_TEST_HEAD="$release" BILLWATCH_TEST_REBOOT_LOG="$reboot_log" BILLWATCH_REBOOT_PROOF_EVIDENCE_FILE="$reboot_proof" \
    sh "$reboot_root/deploy/run-controlled-reboot-proof.sh" preflight "$reboot_root" >/dev/null || fail "reboot proof preflight failed."
[ ! -e "$reboot_proof" ] || fail "reboot proof preflight incorrectly wrote completion evidence."
PATH="$fake_bin:$PATH" BILLWATCH_TEST_HEAD="$release" BILLWATCH_TEST_REBOOT_LOG="$reboot_log" BILLWATCH_REBOOT_PROOF_EVIDENCE_FILE="$reboot_proof" \
    sh "$reboot_root/deploy/run-controlled-reboot-proof.sh" postflight "$reboot_root" >/dev/null || fail "reboot proof postflight failed."
grep -Fxq 'PASSED_PHASES=controlled-reboot-recovery' "$reboot_proof" || fail "reboot proof wrapper wrote incorrect evidence."
[ "$(cat "$reboot_log")" = "preflight
postflight" ] || fail "reboot proof wrapper did not delegate both drill phases exactly once."

printf '%s\n' 'Private-beta technical evidence regression tests passed.'

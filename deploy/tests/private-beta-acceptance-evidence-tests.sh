#!/bin/sh
set -eu
root_dir=$(CDPATH= cd -- "$(dirname -- "$0")/../.." && pwd)
temp=$(mktemp -d); trap 'rm -rf "$temp"' EXIT HUP INT TERM
fail(){ printf '%s\n' "Acceptance evidence test failed: $1" >&2; exit 1; }
script="$root_dir/deploy/verify-private-beta-acceptance-evidence.sh"; sh -n "$script" || fail "invalid syntax"
release=1234567890abcdef1234567890abcdef12345678
deployment="$temp/deployment"; evidence="$temp/evidence"; bin="$temp/bin"; mkdir -p "$deployment" "$evidence" "$bin"
printf '%s\n' "$release" > "$deployment/.billwatch-release"
cat > "$bin/git" <<'EOF'
#!/bin/sh
case "$*" in *'rev-parse HEAD'*) printf '%s\n' "$TEST_RELEASE";; *'status --porcelain --untracked-files=no'*) :;; *) exit 1;; esac
EOF
chmod 700 "$bin/git"
write(){ file=$1; phases=$2; rel=${3:-$release}; printf 'VERSION=1\nRESULT=complete\nRELEASE_SHA=%s\nPASSED_PHASES=%s\n' "$rel" "$phases" > "$file"; chmod 600 "$file"; }
technical="$evidence/technical"; alerts="$evidence/alerts"; plaid="$evidence/plaid"; output="$evidence/acceptance"
write "$technical" 'internal-beta0,clean-host-recovery,controlled-reboot-recovery'
write "$alerts" 'operations-alert-observed,external-readiness-alert-observed'
write "$plaid" 'plaid-hosted-link-observed,plaid-update-completed,plaid-post-update-sync-active'
run(){ env PATH="$bin:$PATH" TEST_RELEASE="$release" BILLWATCH_TECHNICAL_EVIDENCE_FILE="$technical" BILLWATCH_ALERT_PROOF_EVIDENCE_FILE="$alerts" BILLWATCH_PLAID_OBSERVATION_EVIDENCE_FILE="$plaid" BILLWATCH_ACCEPTANCE_EVIDENCE_FILE="$output" sh "$script" "$deployment"; }
run >/dev/null || fail "valid same-release evidence was rejected"
[ "$(stat -c '%a' "$output")" = 600 ] || fail "output is not mode 600"
grep -qx 'PASSED_PHASES=machine-technical,alert-observation,plaid-observation' "$output" || fail "combined phases are wrong"
rm -f "$output"; write "$plaid" 'plaid-hosted-link-observed,plaid-update-completed,plaid-post-update-sync-active' 9999999990abcdef1234567890abcdef12345678
if run >/dev/null 2>&1; then fail "cross-release Plaid evidence was accepted"; fi
write "$plaid" 'plaid-hosted-link-observed,plaid-update-completed,plaid-post-update-sync-active'; chmod 644 "$alerts"
if run >/dev/null 2>&1; then fail "weak alert evidence permissions were accepted"; fi
printf '%s\n' 'Private-beta acceptance evidence regression tests passed.'

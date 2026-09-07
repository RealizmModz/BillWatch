#!/bin/sh

set -eu

root_dir=$(CDPATH= cd -- "$(dirname -- "$0")/../.." && pwd)
temp_dir=$(mktemp -d)
trap 'rm -rf "$temp_dir"' EXIT HUP INT TERM

fail()
{
    printf '%s\n' "Reboot recovery drill test failed: $1" >&2
    exit 1
}

drill="$root_dir/deploy/run-controlled-reboot-drill.sh"
[ -f "$drill" ] || fail "controlled reboot drill script is missing."
sh -n "$drill" || fail "controlled reboot drill script has invalid POSIX shell syntax."

if grep -Eq 'systemctl[[:space:]]+reboot|shutdown[[:space:]]+-r|reboot[[:space:]]*$' "$drill"; then
    fail "controlled reboot drill must never initiate the host reboot itself."
fi

deployment="$temp_dir/deployment"
mkdir -p "$deployment/deploy"
release_a='1111111111111111111111111111111111111111'
release_b='2222222222222222222222222222222222222222'
printf '%s\n' "$release_a" > "$deployment/.billwatch-release"

verify_log="$temp_dir/verify.log"
cat > "$deployment/deploy/verify-beta-readiness.sh" <<'EOF'
#!/bin/sh
set -eu
: "${BILLWATCH_TEST_VERIFY_LOG:?}"
printf 'verify:%s\n' "$1" >> "$BILLWATCH_TEST_VERIFY_LOG"
exit "${BILLWATCH_TEST_VERIFY_EXIT:-0}"
EOF
chmod 700 "$deployment/deploy/verify-beta-readiness.sh"

fake_bin="$temp_dir/bin"
mkdir -p "$fake_bin"

cat > "$fake_bin/git" <<'EOF'
#!/bin/sh
set -eu
: "${BILLWATCH_TEST_GIT_HEAD:?}"
printf '%s\n' "$BILLWATCH_TEST_GIT_HEAD"
EOF

cat > "$fake_bin/systemctl" <<'EOF'
#!/bin/sh
set -eu
: "${BILLWATCH_TEST_SYSTEMCTL_LOG:?}"
printf '%s\n' "$*" >> "$BILLWATCH_TEST_SYSTEMCTL_LOG"
exit "${BILLWATCH_TEST_SYSTEMCTL_EXIT:-0}"
EOF

cat > "$fake_bin/cat" <<'EOF'
#!/bin/sh
set -eu
if [ "$#" -eq 1 ] && [ "$1" = "/proc/sys/kernel/random/boot_id" ]; then
    : "${BILLWATCH_TEST_BOOT_ID:?}"
    printf '%s\n' "$BILLWATCH_TEST_BOOT_ID"
    exit 0
fi
exec /bin/cat "$@"
EOF

chmod 700 "$fake_bin/git" "$fake_bin/systemctl" "$fake_bin/cat"

state_file="$temp_dir/state/reboot.state"
systemctl_log="$temp_dir/systemctl.log"

run_drill()
{
    PATH="$fake_bin:$PATH" \
    BILLWATCH_TEST_GIT_HEAD="${BILLWATCH_TEST_GIT_HEAD_VALUE:-$release_a}" \
    BILLWATCH_TEST_BOOT_ID="${BILLWATCH_TEST_BOOT_ID_VALUE:-aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa}" \
    BILLWATCH_TEST_SYSTEMCTL_LOG="$systemctl_log" \
    BILLWATCH_TEST_VERIFY_LOG="$verify_log" \
    BILLWATCH_TEST_VERIFY_EXIT="${BILLWATCH_TEST_VERIFY_EXIT_VALUE:-0}" \
    BILLWATCH_REBOOT_DRILL_STATE_FILE="$state_file" \
    BILLWATCH_REBOOT_DRILL_ALLOW="${BILLWATCH_REBOOT_DRILL_ALLOW_VALUE:-true}" \
    sh "$drill" "$1" "$deployment"
}

BILLWATCH_REBOOT_DRILL_ALLOW_VALUE=false
if run_drill preflight >/dev/null 2>&1; then
    fail "preflight accepted missing explicit opt-in."
fi
BILLWATCH_REBOOT_DRILL_ALLOW_VALUE=true

run_drill preflight >/dev/null || fail "valid preflight failed."
[ -f "$state_file" ] || fail "preflight did not create persistent state."
[ "$(stat -c '%a' "$state_file")" = "600" ] || fail "preflight state was not mode 600."
grep -Fq "RELEASE_SHA=$release_a" "$state_file" || fail "preflight state omitted the verified release."
grep -Fq 'BOOT_ID=aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa' "$state_file" || fail "preflight state omitted the boot ID."
if grep -Eq '(^|[[:space:]])reboot($|[[:space:]])' "$systemctl_log"; then
    fail "preflight attempted to reboot the host."
fi

if run_drill preflight >/dev/null 2>&1; then
    fail "preflight overwrote an unfinished prior drill state."
fi

if run_drill postflight >/dev/null 2>&1; then
    fail "postflight accepted an unchanged boot ID."
fi
[ -f "$state_file" ] || fail "failed postflight discarded diagnostic state."

BILLWATCH_TEST_BOOT_ID_VALUE='bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb'
printf '%s\n' "$release_b" > "$deployment/.billwatch-release"
if run_drill postflight >/dev/null 2>&1; then
    fail "postflight accepted a changed release marker."
fi
[ -f "$state_file" ] || fail "release-mismatch postflight discarded diagnostic state."
printf '%s\n' "$release_a" > "$deployment/.billwatch-release"

run_drill postflight >/dev/null || fail "valid postflight failed."
[ ! -e "$state_file" ] || fail "successful postflight did not remove completed drill state."

verify_count="$(wc -l < "$verify_log" | tr -d ' ')"
[ "$verify_count" = "2" ] || fail "full beta-readiness verification did not run in both phases."
grep -Fq 'is-enabled --quiet docker' "$systemctl_log" || fail "drill did not verify Docker enablement."
grep -Fq 'is-active --quiet docker' "$systemctl_log" || fail "drill did not verify Docker activity."

printf '%s\n' 'Controlled reboot recovery drill regression tests passed.'

#!/bin/sh

set -eu

root_dir=$(CDPATH= cd -- "$(dirname -- "$0")/../.." && pwd)
temp_dir=$(mktemp -d)

trap 'rm -rf "$temp_dir"' EXIT HUP INT TERM

fail()
{
    printf '%s\n' "Runtime watchdog test failed: $1" >&2
    exit 1
}

expect_failure()
{
    if "$@" >/dev/null 2>&1; then
        printf '%s\n' "Expected command to fail: $*" >&2
        exit 1
    fi
}

service_unit="$root_dir/deploy/systemd/billwatch-runtime-readiness.service"
timer_unit="$root_dir/deploy/systemd/billwatch-runtime-readiness.timer"
checker="$root_dir/deploy/check-runtime-watchdog.sh"

[ -f "$service_unit" ] || fail "runtime readiness service is missing."
[ -f "$timer_unit" ] || fail "runtime readiness timer is missing."
[ -f "$checker" ] || fail "runtime watchdog checker is missing."

sh -n "$checker" || fail "runtime watchdog checker has invalid POSIX shell syntax."

grep -Fqx 'User=deploy' "$service_unit" ||
    fail "runtime verification must run as the deploy account."

grep -Fqx 'Requires=docker.service' "$service_unit" ||
    fail "runtime verification must require Docker."

grep -Fqx 'After=docker.service network-online.target' "$service_unit" ||
    fail "runtime verification must wait for Docker and network-online."

if grep -Fq 'ConditionPathExists=/opt/billwatch/.billwatch-release' "$service_unit" ||
   grep -Fq 'ConditionPathExists=/opt/billwatch/.env.production' "$service_unit"; then
    fail "runtime verification must fail and alert when protected deployment state is missing, not silently skip."
fi

grep -Fqx 'OnFailure=billwatch-operations-alert@%n.service' "$service_unit" ||
    fail "runtime verification failures must route to operations alerting."

grep -Fqx 'ExecStart=/bin/sh /opt/billwatch/deploy/verify-production.sh /opt/billwatch' "$service_unit" ||
    fail "runtime verification must execute the guarded production verifier."

grep -Fqx 'OnBootSec=2min' "$timer_unit" ||
    fail "runtime watchdog must verify the host shortly after boot."

grep -Fqx 'OnUnitInactiveSec=5min' "$timer_unit" ||
    fail "runtime watchdog must continue verifying only after the prior check completes."

if grep -Fq 'OnUnitActiveSec=' "$timer_unit"; then
    fail "runtime watchdog must not schedule its next run from the prior check start time."
fi

fake_bin="$temp_dir/bin"
mkdir "$fake_bin"

cat > "$fake_bin/systemctl" <<'SCRIPT'
#!/bin/sh
command=$1
shift

case "$command" in
    cat)
        unit=$1
        case "$unit" in
            billwatch-runtime-readiness.service)
                cat "$BILLWATCH_TEST_RUNTIME_SERVICE"
                ;;
            billwatch-runtime-readiness.timer)
                cat "$BILLWATCH_TEST_RUNTIME_TIMER"
                ;;
            *)
                exit 1
                ;;
        esac
        ;;
    is-enabled)
        [ "${BILLWATCH_TEST_TIMER_ENABLED:-true}" = true ]
        ;;
    is-active)
        [ "${BILLWATCH_TEST_TIMER_ACTIVE:-true}" = true ]
        ;;
    list-timers)
        printf '%s\n' 'Fri 2026-09-04 13:40:00 UTC 4min Fri 2026-09-04 13:35:00 UTC 1min ago billwatch-runtime-readiness.timer billwatch-runtime-readiness.service'
        ;;
    *)
        exit 1
        ;;
esac
SCRIPT

chmod 755 "$fake_bin/systemctl"

run_checker()
{
    env \
        PATH="$fake_bin:$PATH" \
        BILLWATCH_TEST_RUNTIME_SERVICE="$service_unit" \
        BILLWATCH_TEST_RUNTIME_TIMER="$timer_unit" \
        "$@" \
        sh "$checker"
}

run_checker >/dev/null
expect_failure run_checker BILLWATCH_TEST_TIMER_ENABLED=false
expect_failure run_checker BILLWATCH_TEST_TIMER_ACTIVE=false

grep -Fq 'check-runtime-watchdog.sh' "$root_dir/deploy/verify-beta-readiness.sh" ||
    fail "private-beta readiness must require the runtime watchdog."

grep -Fq 'billwatch-runtime-readiness.service' "$root_dir/deploy/check-operations-alerting.sh" ||
    fail "operations alert verification must cover runtime readiness failures."

printf '%s\n' 'Runtime watchdog tests passed.'

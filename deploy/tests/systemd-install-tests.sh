#!/bin/sh

set -eu

root_dir=$(CDPATH= cd -- "$(dirname -- "$0")/../.." && pwd)
temp_dir=$(mktemp -d)
trap 'rm -rf "$temp_dir"' EXIT HUP INT TERM

fail()
{
    printf '%s\n' "Systemd install test failed: $1" >&2
    exit 1
}

fixture_root="$temp_dir/root"
destination="$temp_dir/systemd"
fake_bin="$temp_dir/bin"
mkdir -p "$fixture_root/deploy/systemd" "$destination" "$fake_bin"

for unit in \
    billwatch-backup.service \
    billwatch-backup.timer \
    billwatch-runtime-readiness.service \
    billwatch-runtime-readiness.timer \
    billwatch-operations-alert@.service
do
    cp "$root_dir/deploy/systemd/$unit" "$fixture_root/deploy/systemd/$unit"
    chmod 600 "$fixture_root/deploy/systemd/$unit"
done

systemctl_log="$temp_dir/systemctl.log"
cat > "$fake_bin/systemctl" <<'SCRIPT'
#!/bin/sh
printf '%s\n' "$*" >> "$BILLWATCH_TEST_SYSTEMCTL_LOG"
SCRIPT
chmod 755 "$fake_bin/systemctl"

PATH="$fake_bin:$PATH" \
BILLWATCH_SYSTEMD_DIR="$destination" \
BILLWATCH_TEST_SYSTEMCTL_LOG="$systemctl_log" \
    sh "$root_dir/deploy/install-production-systemd-units.sh" "$fixture_root" >/dev/null

for unit in \
    billwatch-backup.service \
    billwatch-backup.timer \
    billwatch-runtime-readiness.service \
    billwatch-runtime-readiness.timer \
    billwatch-operations-alert@.service
do
    installed="$destination/$unit"
    [ -f "$installed" ] || fail "missing installed unit: $unit"
    [ "$(stat -c '%a' "$installed")" = 644 ] || fail "installed unit is not mode 644: $unit"
done

grep -qx 'daemon-reload' "$systemctl_log" || fail "installer did not reload systemd."
grep -qx 'enable --now billwatch-backup.timer billwatch-runtime-readiness.timer' "$systemctl_log" ||
    fail "installer did not enable both production timers."

printf '%s\n' 'Systemd installation regression tests passed.'

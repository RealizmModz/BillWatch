#!/bin/sh

set -eu

root_dir="${1:-}"
systemd_dir="${BILLWATCH_SYSTEMD_DIR:-/etc/systemd/system}"

fail()
{
    printf '%s\n' "$1" >&2
    exit "${2:-1}"
}

if [ -z "$root_dir" ]; then
    root_dir=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
fi

[ -d "$root_dir/deploy/systemd" ] ||
    fail "BillWatch systemd source directory was not found under $root_dir." 64

[ -d "$systemd_dir" ] ||
    fail "Systemd destination directory does not exist: $systemd_dir" 64

for unit in \
    billwatch-backup.service \
    billwatch-backup.timer \
    billwatch-runtime-readiness.service \
    billwatch-runtime-readiness.timer \
    billwatch-operations-alert@.service
do
    source_path="$root_dir/deploy/systemd/$unit"
    [ -f "$source_path" ] || fail "Required BillWatch systemd unit is missing: $unit" 64
    [ ! -L "$source_path" ] || fail "BillWatch systemd unit must not be a symbolic link: $unit" 64

    install -m 0644 "$source_path" "$systemd_dir/$unit"
done

systemctl daemon-reload
systemctl enable --now \
    billwatch-backup.timer \
    billwatch-runtime-readiness.timer

printf '%s\n' "BillWatch production systemd units installed with mode 0644."

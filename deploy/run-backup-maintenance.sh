#!/bin/sh

set -eu

umask 077

root_dir=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
env_file=${1:-}

fail()
{
    printf '%s\n' "Backup maintenance refused: $1" >&2
    exit "${2:-64}"
}

read_required_env_value()
{
    key=$1
    value=$(awk -v key="$key" '
        BEGIN { count = 0 }
        index($0, key "=") == 1 {
            count++
            value = substr($0, length(key) + 2)
            sub(/\r$/, "", value)
        }
        END {
            if (count != 1 || value == "") exit 65
            print value
        }
    ' "$env_file") || fail "$key must appear exactly once with a non-empty value in the maintenance environment file."
    printf '%s' "$value"
}

[ -n "$env_file" ] || fail "usage: $0 <protected-maintenance-env-file>"
[ -f "$env_file" ] || fail "the maintenance environment file is missing: $env_file" 66
[ ! -L "$env_file" ] || fail "the maintenance environment file must not be a symbolic link." 73

env_file=$(cd "$(dirname "$env_file")" && pwd -P)/$(basename "$env_file")
case "$env_file" in
    "$root_dir"|"$root_dir"/*) fail "the delete-capable maintenance environment file must live outside the BillWatch checkout." 77 ;;
esac

mode=$(stat -c '%a' "$env_file") || fail "maintenance environment permissions cannot be read."
[ "$mode" = 600 ] || fail "the maintenance environment file must have mode 600." 77
owner_uid=$(stat -c '%u' "$env_file") || fail "maintenance environment ownership cannot be read."
[ "$owner_uid" = "$(id -u)" ] || fail "the maintenance environment file must be owned by the current maintenance operator." 77

if grep -q "$(printf '\r')" "$env_file"; then
    fail "the maintenance environment file must use Unix line endings."
fi
invalid_line=$(awk '
    /^[[:space:]]*$/ { next }
    /^[[:space:]]*#/ { next }
    /^[A-Z][A-Z0-9_]*=[^\r\n]*$/ { next }
    { print NR; exit }
' "$env_file")
[ -z "$invalid_line" ] || fail "maintenance environment line $invalid_line is not a KEY=value entry."

release_id=$(read_required_env_value BILLWATCH_RELEASE_ID)
repository=$(read_required_env_value RESTIC_REPOSITORY)
read_required_env_value RESTIC_PASSWORD >/dev/null
client_mode=$(read_required_env_value BILLWATCH_BACKUP_CLIENT_MODE)
allow=$(read_required_env_value BILLWATCH_BACKUP_MAINTENANCE_ALLOW)
retention_enabled=$(read_required_env_value BILLWATCH_BACKUP_RETENTION_ENABLED)

printf '%s\n' "$release_id" | grep -Eq '^[0-9a-f]{40}$' || fail "BILLWATCH_RELEASE_ID must be an exact lowercase 40-character Git commit SHA."
[ "$client_mode" = maintenance ] || fail "BILLWATCH_BACKUP_CLIENT_MODE must be maintenance on the trusted maintenance host." 77
[ "$allow" = true ] || fail "BILLWATCH_BACKUP_MAINTENANCE_ALLOW=true is required before destructive retention maintenance." 77
[ "$retention_enabled" = true ] || fail "BILLWATCH_BACKUP_RETENTION_ENABLED=true is required before maintenance." 77

case "$repository" in
    s3:*|b2:*|azure:*|gs:*|rclone:*|rest:*|sftp:*|swift:*) ;;
    *) fail "maintenance requires an explicitly off-host Restic repository." ;;
esac

command -v git >/dev/null 2>&1 || fail "git is required on the trusted maintenance host." 69
command -v docker >/dev/null 2>&1 || fail "Docker is required on the trusted maintenance host." 69

head_sha=$(git -C "$root_dir" rev-parse HEAD)
[ "$head_sha" = "$release_id" ] || fail "BILLWATCH_RELEASE_ID must match the maintenance checkout exactly." 65
[ -z "$(git -C "$root_dir" status --porcelain --untracked-files=normal)" ] || fail "backup maintenance requires a clean BillWatch checkout." 65

image="billwatch-backup-maintenance:$release_id"
docker build \
    --build-arg "BILLWATCH_RELEASE_ID=$release_id" \
    --tag "$image" \
    "$root_dir/deploy/backup"

docker run \
    --rm \
    --read-only \
    --user 1654:1654 \
    --cap-drop ALL \
    --security-opt no-new-privileges:true \
    --tmpfs /work:rw,noexec,nosuid,nodev,size=256m,uid=1654,gid=1654,mode=0700 \
    --tmpfs /cache:rw,noexec,nosuid,nodev,size=512m,uid=1654,gid=1654,mode=0700 \
    --env-file "$env_file" \
    "$image" \
    retention

printf 'Trusted-host BillWatch retention maintenance passed for release %s.\n' "$release_id"

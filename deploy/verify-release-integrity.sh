#!/bin/sh

set -eu

deployment_directory="${1:-}"

fail()
{
    printf '%s\n' "Production release integrity invalid: $1" >&2
    exit "${2:-1}"
}

if [ -z "$deployment_directory" ]; then
    fail "a BillWatch deployment directory is required." 64
fi

[ -d "$deployment_directory" ] ||
    fail "the deployment directory does not exist." 66

deployment_directory="$(cd "$deployment_directory" && pwd -P)"
environment_file="$deployment_directory/.env.production"
release_file="$deployment_directory/.billwatch-release"

command -v git >/dev/null 2>&1 ||
    fail "git is required." 69

[ -f "$environment_file" ] ||
    fail ".env.production was not found." 66

[ -f "$release_file" ] ||
    fail ".billwatch-release was not found; the deployment has not been verified as a completed release." 66

[ ! -L "$release_file" ] ||
    fail ".billwatch-release must not be a symbolic link." 77

release_owner="$(stat -c '%u' "$release_file")" ||
    fail ".billwatch-release ownership cannot be read." 77

release_permissions="$(stat -c '%a' "$release_file")" ||
    fail ".billwatch-release permissions cannot be read." 77

[ "$release_owner" -eq "$(id -u)" ] ||
    fail ".billwatch-release must be owned by the deployment account." 77

[ "$release_permissions" = "600" ] ||
    fail ".billwatch-release must have mode 600." 77

if grep -q "$(printf '\r')" "$release_file"; then
    fail ".billwatch-release must use Unix line endings." 65
fi

[ "$(wc -l < "$release_file" | tr -d ' ')" -eq 1 ] ||
    fail ".billwatch-release must contain exactly one line." 65

recorded_release="$(cat "$release_file")"

case "$recorded_release" in
    *[!0-9a-f]*|'')
        fail ".billwatch-release must contain one lowercase 40-character Git commit." 65
        ;;
esac

[ "${#recorded_release}" -eq 40 ] ||
    fail ".billwatch-release must contain one lowercase 40-character Git commit." 65

expected_release_count="$(awk -F= '$1 == "BILLWATCH_RELEASE_ID" { count++ } END { print count + 0 }' "$environment_file")"

[ "$expected_release_count" -eq 1 ] ||
    fail "BILLWATCH_RELEASE_ID must appear exactly once in .env.production." 65

expected_release="$(awk -v prefix='BILLWATCH_RELEASE_ID=' 'index($0, prefix) == 1 { print substr($0, length(prefix) + 1); exit }' "$environment_file")"

case "$expected_release" in
    *[!0-9a-f]*|'')
        fail "BILLWATCH_RELEASE_ID must be a lowercase 40-character Git commit." 65
        ;;
esac

[ "${#expected_release}" -eq 40 ] ||
    fail "BILLWATCH_RELEASE_ID must be a lowercase 40-character Git commit." 65

current_release="$(git -C "$deployment_directory" rev-parse --verify HEAD^{commit} 2>/dev/null)" ||
    fail "the deployment directory is not a valid Git checkout." 65

case "$current_release" in
    *[!0-9a-f]*|'')
        fail "the checked-out Git commit could not be resolved safely." 65
        ;;
esac

[ "${#current_release}" -eq 40 ] ||
    fail "the checked-out Git commit could not be resolved safely." 65

[ "$current_release" = "$expected_release" ] ||
    fail "BILLWATCH_RELEASE_ID does not match the checked-out Git commit." 78

[ "$recorded_release" = "$current_release" ] ||
    fail ".billwatch-release does not match the checked-out Git commit." 78

[ -z "$(git -C "$deployment_directory" status --porcelain --untracked-files=normal)" ] ||
    fail "the production deployment checkout contains uncommitted or untracked files." 78

if git -C "$deployment_directory" ls-files --error-unmatch .billwatch-release >/dev/null 2>&1; then
    fail ".billwatch-release must never be tracked by Git." 77
fi

if ! git -C "$deployment_directory" check-ignore -q .billwatch-release; then
    fail ".billwatch-release must be covered by Git ignore rules." 77
fi

printf '%s\n' "BillWatch production release integrity verification passed for $current_release."

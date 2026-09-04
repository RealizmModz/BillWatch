#!/bin/sh

set -eu

root_dir=$(CDPATH= cd -- "$(dirname -- "$0")/../.." && pwd)
temp_dir=$(mktemp -d)

trap 'rm -rf "$temp_dir"' EXIT HUP INT TERM

fail()
{
    printf '%s\n' "Release integrity test failed: $1" >&2
    exit 1
}

expect_failure()
{
    if "$@" >/dev/null 2>&1; then
        printf '%s\n' "Expected command to fail: $*" >&2
        exit 1
    fi
}

release_id=0123456789abcdef0123456789abcdef01234567
other_release=89abcdef0123456789abcdef0123456789abcdef

deployment_root="$temp_dir/deployment"
fake_bin="$temp_dir/bin"
mkdir -p "$deployment_root/deploy" "$fake_bin"

cp "$root_dir/deploy/verify-release-integrity.sh" \
    "$deployment_root/deploy/verify-release-integrity.sh"
cp "$root_dir/deploy/verify-running-release.sh" \
    "$deployment_root/deploy/verify-running-release.sh"

chmod 755 "$deployment_root/deploy/verify-release-integrity.sh"

write_environment()
{
    candidate=${1:-$release_id}
    printf '%s\n' "BILLWATCH_RELEASE_ID=$candidate" > "$deployment_root/.env.production"
    chmod 600 "$deployment_root/.env.production"
}

write_release()
{
    candidate=${1:-$release_id}
    printf '%s\n' "$candidate" > "$deployment_root/.billwatch-release"
    chmod 600 "$deployment_root/.billwatch-release"
}

cat > "$fake_bin/git" <<'SCRIPT'
#!/bin/sh
set -eu

case "$*" in
    *'rev-parse --verify HEAD^{commit}'*)
        [ "${BILLWATCH_TEST_INVALID_GIT:-false}" != true ] || exit 128
        printf '%s\n' "${BILLWATCH_TEST_HEAD:-0123456789abcdef0123456789abcdef01234567}"
        ;;
    *'status --porcelain --untracked-files=normal'*)
        [ "${BILLWATCH_TEST_DIRTY:-false}" != true ] || printf '%s\n' ' M BillWatch.API/Program.cs'
        ;;
    *'ls-files --error-unmatch .billwatch-release'*)
        [ "${BILLWATCH_TEST_MARKER_TRACKED:-false}" = true ] || exit 1
        printf '%s\n' '.billwatch-release'
        ;;
    *'check-ignore -q .billwatch-release'*)
        [ "${BILLWATCH_TEST_MARKER_IGNORED:-true}" = true ]
        ;;
    *)
        exit 2
        ;;
esac
SCRIPT

chmod 755 "$fake_bin/git"

verify()
{
    env \
        PATH="$fake_bin:$PATH" \
        "$@" \
        "$deployment_root/deploy/verify-release-integrity.sh" \
        "$deployment_root"
}

write_environment
write_release
verify >/dev/null

write_release "$other_release"
expect_failure verify
write_release

write_environment "$other_release"
expect_failure verify
write_environment

expect_failure verify BILLWATCH_TEST_HEAD="$other_release"
expect_failure verify BILLWATCH_TEST_DIRTY=true
expect_failure verify BILLWATCH_TEST_MARKER_TRACKED=true
expect_failure verify BILLWATCH_TEST_MARKER_IGNORED=false
expect_failure verify BILLWATCH_TEST_INVALID_GIT=true

chmod 644 "$deployment_root/.billwatch-release"
expect_failure verify
chmod 600 "$deployment_root/.billwatch-release"

rm -f "$deployment_root/.billwatch-release"
ln -s /etc/passwd "$deployment_root/.billwatch-release"
expect_failure verify
rm -f "$deployment_root/.billwatch-release"
write_release

printf '%s\n%s\n' "$release_id" "$release_id" > "$deployment_root/.billwatch-release"
chmod 600 "$deployment_root/.billwatch-release"
expect_failure verify
write_release

printf '%s' "$release_id" > "$deployment_root/.billwatch-release"
chmod 600 "$deployment_root/.billwatch-release"
expect_failure verify
write_release

printf '%s\n' '0123456789ABCDEF0123456789ABCDEF01234567' > "$deployment_root/.billwatch-release"
chmod 600 "$deployment_root/.billwatch-release"
expect_failure verify
write_release

printf '%s\r\n' "$release_id" > "$deployment_root/.billwatch-release"
chmod 600 "$deployment_root/.billwatch-release"
expect_failure verify
write_release

printf '%s\n%s\n' \
    "BILLWATCH_RELEASE_ID=$release_id" \
    "BILLWATCH_RELEASE_ID=$release_id" > "$deployment_root/.env.production"
chmod 600 "$deployment_root/.env.production"
expect_failure verify
write_environment

rm -f "$deployment_root/.billwatch-release"
expect_failure verify
write_release

verify >/dev/null

cat > "$fake_bin/docker" <<'SCRIPT'
#!/bin/sh
set -eu

case "$*" in
    *'ps -q api')
        printf '%s\n' api-container
        ;;
    *'ps -q web')
        printf '%s\n' web-container
        ;;
    *'inspect --format {{.State.Running}} api-container')
        [ "${BILLWATCH_TEST_API_STOPPED:-false}" != true ] && printf '%s\n' true || printf '%s\n' false
        ;;
    *'inspect --format {{.State.Running}} web-container')
        [ "${BILLWATCH_TEST_WEB_STOPPED:-false}" != true ] && printf '%s\n' true || printf '%s\n' false
        ;;
    *'inspect --format {{index .Config.Labels "org.opencontainers.image.revision"}} api-container')
        printf '%s\n' "${BILLWATCH_TEST_API_REVISION:-0123456789abcdef0123456789abcdef01234567}"
        ;;
    *'inspect --format {{index .Config.Labels "org.opencontainers.image.revision"}} web-container')
        printf '%s\n' "${BILLWATCH_TEST_WEB_REVISION:-0123456789abcdef0123456789abcdef01234567}"
        ;;
    *)
        exit 2
        ;;
esac
SCRIPT
chmod 755 "$fake_bin/docker"

verify_running()
{
    env \
        PATH="$fake_bin:$PATH" \
        "$@" \
        sh "$deployment_root/deploy/verify-running-release.sh" \
        "$deployment_root" \
        "$deployment_root/.env.production"
}

write_environment
write_release
verify_running >/dev/null
expect_failure verify_running BILLWATCH_TEST_API_REVISION="$other_release"
expect_failure verify_running BILLWATCH_TEST_WEB_REVISION="$other_release"
expect_failure verify_running BILLWATCH_TEST_API_STOPPED=true
expect_failure verify_running BILLWATCH_TEST_WEB_STOPPED=true

chmod 644 "$deployment_root/.billwatch-release"
expect_failure verify_running
chmod 600 "$deployment_root/.billwatch-release"

rm -f "$deployment_root/.billwatch-release"
expect_failure verify_running
write_release

verify_running >/dev/null

printf '%s\n' 'Release integrity operation tests passed.'

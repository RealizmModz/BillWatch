#!/bin/sh

set -eu

root_dir=$(CDPATH= cd -- "$(dirname -- "$0")/../.." && pwd)
temp_dir=$(mktemp -d)

trap 'rm -rf "$temp_dir"' EXIT HUP INT TERM

fail()
{
    printf '%s\n' "Runtime release test failed: $1" >&2
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

cp "$root_dir/deploy/verify-production-runtime.sh" \
    "$deployment_root/deploy/verify-production-runtime.sh"
cp "$root_dir/compose.production.yml" \
    "$deployment_root/compose.production.yml"

chmod 755 \
    "$deployment_root/deploy/verify-production-runtime.sh"

printf '%s\n' "BILLWATCH_RELEASE_ID=$release_id" > "$deployment_root/.env.production"
printf '%s\n' "$release_id" > "$deployment_root/.billwatch-release"

cat > "$fake_bin/git" <<'SCRIPT'
#!/bin/sh
set -eu

case "$*" in
    *'rev-parse --verify HEAD^{commit}'*)
        printf '%s\n' "${BILLWATCH_TEST_HEAD:-0123456789abcdef0123456789abcdef01234567}"
        ;;
    *) exit 2 ;;
esac
SCRIPT

cat > "$fake_bin/docker" <<'SCRIPT'
#!/bin/sh
set -eu

release_default=0123456789abcdef0123456789abcdef01234567

case "$1" in
    compose)
        case "$*" in
            *'ps --status running --services'*)
                for service in database api web edge
                do
                    [ "${BILLWATCH_TEST_MISSING_SERVICE:-}" = "$service" ] || printf '%s\n' "$service"
                done
                ;;
            *'ps -q database'*) printf '%s\n' 'cid-database' ;;
            *'ps -q api'*) printf '%s\n' 'cid-api' ;;
            *'ps -q web'*) printf '%s\n' 'cid-web' ;;
            *) exit 2 ;;
        esac
        ;;
    inspect)
        container_id=
        for argument in "$@"
        do
            container_id=$argument
        done

        case "$*" in
            *'.State.Health'* )
                if [ "${BILLWATCH_TEST_UNHEALTHY:-}" = "${container_id#cid-}" ]; then
                    printf '%s\n' 'unhealthy'
                else
                    printf '%s\n' 'healthy'
                fi
                ;;
            *'org.opencontainers.image.revision'* )
                case "$container_id" in
                    cid-api) printf '%s\n' "${BILLWATCH_TEST_API_REVISION:-$release_default}" ;;
                    cid-web) printf '%s\n' "${BILLWATCH_TEST_WEB_REVISION:-$release_default}" ;;
                    *) exit 2 ;;
                esac
                ;;
            *) exit 2 ;;
        esac
        ;;
    image)
        [ "$2" = inspect ] || exit 2
        if [ "${BILLWATCH_TEST_BACKUP_MISSING:-false}" = true ]; then
            exit 1
        fi
        printf '%s\n' "${BILLWATCH_TEST_BACKUP_REVISION:-$release_default}"
        ;;
    *)
        exit 2
        ;;
esac
SCRIPT

chmod 755 "$fake_bin/git" "$fake_bin/docker"

verify()
{
    env \
        PATH="$fake_bin:$PATH" \
        "$@" \
        "$deployment_root/deploy/verify-production-runtime.sh" \
        "$deployment_root"
}

verify >/dev/null
expect_failure verify BILLWATCH_TEST_API_REVISION="$other_release"
expect_failure verify BILLWATCH_TEST_WEB_REVISION="$other_release"
expect_failure verify BILLWATCH_TEST_BACKUP_REVISION="$other_release"
expect_failure verify BILLWATCH_TEST_BACKUP_MISSING=true
expect_failure verify BILLWATCH_TEST_UNHEALTHY=api
expect_failure verify BILLWATCH_TEST_MISSING_SERVICE=web
expect_failure verify BILLWATCH_TEST_HEAD="$other_release"

printf '%s\n' "$other_release" > "$deployment_root/.billwatch-release"
expect_failure verify
printf '%s\n' "$release_id" > "$deployment_root/.billwatch-release"

verify >/dev/null

printf '%s\n' 'Runtime release artifact tests passed.'

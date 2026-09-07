#!/bin/sh

set -eu

root_dir=$(CDPATH= cd -- "$(dirname -- "$0")/../.." && pwd)
temp_dir=$(mktemp -d)
trap 'rm -rf "$temp_dir"' EXIT HUP INT TERM

fail()
{
    printf '%s\n' "Admin API smoke test failed: $1" >&2
    exit 1
}

smoke="$root_dir/deploy/smoke-admin-api.sh"
[ -f "$smoke" ] || fail "smoke script is missing."
sh -n "$smoke" || fail "smoke script has invalid POSIX shell syntax."

admin_password="$temp_dir/admin-password"
nonstaff_password="$temp_dir/nonstaff-password"
printf '%s\n' 'AdminPassword!123' > "$admin_password"
printf '%s\n' 'UserPassword!123' > "$nonstaff_password"
chmod 600 "$admin_password" "$nonstaff_password"

fake_bin="$temp_dir/bin"
mkdir -p "$fake_bin"
cat > "$fake_bin/curl" <<'EOF'
#!/bin/sh
set -eu
: "${BILLWATCH_TEST_CURL_LOG:?}"
count_file="${BILLWATCH_TEST_CURL_COUNT:?}"
count=0
[ ! -f "$count_file" ] || count="$(cat "$count_file")"
count=$((count + 1))
printf '%s\n' "$count" > "$count_file"
printf '%s\n' "$*" >> "$BILLWATCH_TEST_CURL_LOG"

output=''
write_out=''
while [ "$#" -gt 0 ]; do
    case "$1" in
        --output) output="$2"; shift 2 ;;
        --write-out) write_out="$2"; shift 2 ;;
        *) shift ;;
    esac
done

case "$count" in
    1)
        [ -n "$output" ] && printf '{"accessToken":"admin-token"}' > "$output"
        printf '200'
        ;;
    2)
        [ -n "$output" ] && printf '{"accessToken":"user-token"}' > "$output"
        printf '200'
        ;;
    3)
        printf '%s' "${BILLWATCH_TEST_ADMIN_CODE:-200}"
        ;;
    4)
        printf '%s' "${BILLWATCH_TEST_NONSTAFF_CODE:-403}"
        ;;
    *) exit 2 ;;
esac
EOF
chmod 700 "$fake_bin/curl"

run_smoke()
{
    : > "$temp_dir/curl.log"
    rm -f "$temp_dir/curl.count"
    PATH="$fake_bin:$PATH" \
    BILLWATCH_TEST_CURL_LOG="$temp_dir/curl.log" \
    BILLWATCH_TEST_CURL_COUNT="$temp_dir/curl.count" \
    BILLWATCH_TEST_ADMIN_CODE="${BILLWATCH_TEST_ADMIN_CODE_VALUE:-200}" \
    BILLWATCH_TEST_NONSTAFF_CODE="${BILLWATCH_TEST_NONSTAFF_CODE_VALUE:-403}" \
    BILLWATCH_ADMIN_SMOKE_EMAIL="${BILLWATCH_ADMIN_SMOKE_EMAIL_VALUE:-owner@example.test}" \
    BILLWATCH_ADMIN_SMOKE_PASSWORD_FILE="${BILLWATCH_ADMIN_SMOKE_PASSWORD_FILE_VALUE:-$admin_password}" \
    BILLWATCH_ADMIN_SMOKE_NONSTAFF_EMAIL="${BILLWATCH_ADMIN_SMOKE_NONSTAFF_EMAIL_VALUE:-user@example.test}" \
    BILLWATCH_ADMIN_SMOKE_NONSTAFF_PASSWORD_FILE="${BILLWATCH_ADMIN_SMOKE_NONSTAFF_PASSWORD_FILE_VALUE:-$nonstaff_password}" \
    sh "$smoke" https://api.example.test
}

run_smoke > "$temp_dir/pass.out"
grep -q 'authorization and non-staff denial smoke test passed' "$temp_dir/pass.out" || fail "successful boundary proof was not reported."
[ "$(cat "$temp_dir/curl.count")" = '4' ] || fail "expected two logins and two authorization probes."
if grep -q 'AdminPassword!123\|UserPassword!123\|admin-token\|user-token' "$temp_dir/curl.log"; then
    fail "credentials or bearer tokens appeared in curl command arguments."
fi

BILLWATCH_TEST_ADMIN_CODE_VALUE=403
if run_smoke >/dev/null 2>&1; then
    fail "smoke accepted an Owner/Admin account denied by the admin endpoint."
fi
BILLWATCH_TEST_ADMIN_CODE_VALUE=200

BILLWATCH_TEST_NONSTAFF_CODE_VALUE=200
if run_smoke >/dev/null 2>&1; then
    fail "smoke accepted a non-staff account that could access the admin endpoint."
fi
BILLWATCH_TEST_NONSTAFF_CODE_VALUE=403

chmod 644 "$admin_password"
if run_smoke >/dev/null 2>&1; then
    fail "smoke accepted a weakly-permissioned admin password file."
fi
chmod 600 "$admin_password"

ln -s "$admin_password" "$temp_dir/admin-link"
BILLWATCH_ADMIN_SMOKE_PASSWORD_FILE_VALUE="$temp_dir/admin-link"
if run_smoke >/dev/null 2>&1; then
    fail "smoke accepted a symbolic-link admin password file."
fi
BILLWATCH_ADMIN_SMOKE_PASSWORD_FILE_VALUE="$admin_password"

BILLWATCH_ADMIN_SMOKE_NONSTAFF_EMAIL_VALUE='owner@example.test'
if run_smoke >/dev/null 2>&1; then
    fail "smoke accepted the same identity for privileged and non-staff probes."
fi
BILLWATCH_ADMIN_SMOKE_NONSTAFF_EMAIL_VALUE='user@example.test'

if BILLWATCH_ADMIN_SMOKE_EMAIL=owner@example.test \
   BILLWATCH_ADMIN_SMOKE_PASSWORD_FILE="$admin_password" \
   BILLWATCH_ADMIN_SMOKE_NONSTAFF_EMAIL=user@example.test \
   BILLWATCH_ADMIN_SMOKE_NONSTAFF_PASSWORD_FILE="$nonstaff_password" \
   sh "$smoke" http://api.example.test >/dev/null 2>&1; then
    fail "smoke accepted a non-HTTPS API URL."
fi

printf '%s\n' 'Admin API smoke tests passed.'

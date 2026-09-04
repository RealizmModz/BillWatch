#!/bin/sh

set -eu

web_base_url="${1:-}"
email="${BILLWATCH_WEB_SMOKE_EMAIL:-}"
password_file="${BILLWATCH_WEB_SMOKE_PASSWORD_FILE:-}"
two_factor_code_file="${BILLWATCH_WEB_SMOKE_TWO_FACTOR_CODE_FILE:-}"
recovery_code_file="${BILLWATCH_WEB_SMOKE_RECOVERY_CODE_FILE:-}"
foreign_bill_stream_id="${BILLWATCH_WEB_SMOKE_FOREIGN_BILL_STREAM_ID:-}"
foreign_statement_upload_id="${BILLWATCH_WEB_SMOKE_FOREIGN_STATEMENT_UPLOAD_ID:-}"

fail()
{
    printf '%s\n' "$1" >&2
    exit "${2:-1}"
}

require_https_url()
{
    value="$1"
    label="$2"

    case "$value" in
        https://*) ;;
        *) fail "$label must use HTTPS." 64 ;;
    esac

    case "$value" in
        *[[:space:]]*) fail "$label must not contain whitespace." 64 ;;
    esac
}

require_secret_file()
{
    path="$1"
    label="$2"

    [ -f "$path" ] || fail "$label must reference a regular file." 64
    [ ! -L "$path" ] || fail "$label must not be a symbolic link." 64

    mode="$(stat -c '%a' "$path" 2>/dev/null || true)"
    [ "$mode" = "600" ] || fail "$label must have mode 600." 64
}

if [ -z "$web_base_url" ]; then
    fail "Usage: $0 <https-web-base-url>" 64
fi

require_https_url "$web_base_url" "The Web/BFF smoke-test base URL"
web_base_url="${web_base_url%/}"

if [ -z "$email" ]; then
    if [ ! -t 0 ]; then
        fail "BILLWATCH_WEB_SMOKE_EMAIL is required for non-interactive smoke tests." 64
    fi

    printf 'BillWatch Web smoke-test account email: ' >&2
    IFS= read -r email
fi

[ -n "$email" ] || fail "An account email is required." 64

if [ -n "$password_file" ]; then
    require_secret_file "$password_file" "BILLWATCH_WEB_SMOKE_PASSWORD_FILE"
else
    if [ ! -t 0 ]; then
        fail "BILLWATCH_WEB_SMOKE_PASSWORD_FILE is required for non-interactive smoke tests." 64
    fi

    password_file="$(mktemp)"
    chmod 600 "$password_file"
    temporary_password_file=true

    printf 'BillWatch password: ' >&2
    stty -echo
    trap 'stty echo 2>/dev/null || true; rm -f "${password_file:-}"' EXIT HUP INT TERM
    IFS= read -r password
    stty echo
    trap - EXIT HUP INT TERM
    printf '\n' >&2

    [ -n "$password" ] || fail "A password is required." 64
    printf '%s' "$password" > "$password_file"
    unset password
fi

if [ -n "$two_factor_code_file" ] && [ -n "$recovery_code_file" ]; then
    fail "Set only one of BILLWATCH_WEB_SMOKE_TWO_FACTOR_CODE_FILE or BILLWATCH_WEB_SMOKE_RECOVERY_CODE_FILE." 64
fi

if [ -n "$two_factor_code_file" ]; then
    require_secret_file "$two_factor_code_file" "BILLWATCH_WEB_SMOKE_TWO_FACTOR_CODE_FILE"
fi

if [ -n "$recovery_code_file" ]; then
    require_secret_file "$recovery_code_file" "BILLWATCH_WEB_SMOKE_RECOVERY_CODE_FILE"
fi

work_directory="$(mktemp -d)"
chmod 700 "$work_directory"

cleanup()
{
    rm -rf "$work_directory"

    if [ "${temporary_password_file:-false}" = "true" ]; then
        rm -f "$password_file"
    fi
}

trap cleanup EXIT HUP INT TERM

cookie_jar="$work_directory/cookies.txt"
login_page="$work_directory/login.html"
login_headers="$work_directory/login-headers.txt"
antiforgery_token_file="$work_directory/antiforgery-token.txt"
bff_antiforgery_response="$work_directory/bff-antiforgery.json"
export_response="$work_directory/account-export.json"

: > "$cookie_jar"
chmod 600 "$cookie_jar"

fetch_login_antiforgery()
{
    path="$1"

    code="$(
        curl \
            --silent \
            --show-error \
            --output "$login_page" \
            --write-out '%{http_code}' \
            --cookie "$cookie_jar" \
            --cookie-jar "$cookie_jar" \
            "$web_base_url$path"
    )"

    [ "$code" = "200" ] ||
        fail "Unable to load $path for Web authentication; received HTTP $code." 69

    token="$(
        sed -n \
            's/.*name="__RequestVerificationToken"[^>]*value="\([^"]*\)".*/\1/p' \
            "$login_page" |
            head -n 1
    )"

    [ -n "$token" ] ||
        fail "The Web login page did not expose an antiforgery form token." 70

    printf '%s' "$token" > "$antiforgery_token_file"
    chmod 600 "$antiforgery_token_file"
    unset token
}

post_login()
{
    second_factor_kind="${1:-}"
    second_factor_file="${2:-}"

    : > "$login_headers"

    set -- \
        --silent \
        --show-error \
        --output /dev/null \
        --dump-header "$login_headers" \
        --write-out '%{http_code}' \
        --request POST \
        --cookie "$cookie_jar" \
        --cookie-jar "$cookie_jar" \
        --header 'Content-Type: application/x-www-form-urlencoded' \
        --data-urlencode "email=$email" \
        --data-urlencode "password@$password_file" \
        --data-urlencode "__RequestVerificationToken@$antiforgery_token_file"

    case "$second_factor_kind" in
        authenticator)
            set -- "$@" \
                --data-urlencode 'twoFactor=true' \
                --data-urlencode "twoFactorCode@$second_factor_file"
            ;;
        recovery)
            set -- "$@" \
                --data-urlencode 'twoFactor=true' \
                --data-urlencode "recoveryCode@$second_factor_file"
            ;;
        '') ;;
        *) fail "Unsupported second-factor mode." 70 ;;
    esac

    curl "$@" "$web_base_url/auth/login"
}

redirect_location()
{
    sed -n 's/^[Ll]ocation:[[:space:]]*\([^\r]*\)\r*$/\1/p' "$login_headers" |
        tail -n 1
}

fetch_login_antiforgery "/login"
login_code="$(post_login)"

case "$login_code" in
    302|303) ;;
    *) fail "Web login returned HTTP $login_code instead of a redirect." 69 ;;
esac

location="$(redirect_location)"

case "$location" in
    /app|/app/*)
        ;;
    /login\?twoFactor=true*)
        if [ -n "$two_factor_code_file" ]; then
            second_factor_kind="authenticator"
            second_factor_file="$two_factor_code_file"
        elif [ -n "$recovery_code_file" ]; then
            second_factor_kind="recovery"
            second_factor_file="$recovery_code_file"
        else
            fail "The account requires two-factor authentication. Supply a current mode-600 authenticator-code file or recovery-code file." 65
        fi

        fetch_login_antiforgery "/login?twoFactor=true"
        login_code="$(post_login "$second_factor_kind" "$second_factor_file")"

        case "$login_code" in
            302|303) ;;
            *) fail "Web two-factor login returned HTTP $login_code instead of a redirect." 69 ;;
        esac

        location="$(redirect_location)"
        case "$location" in
            /app|/app/*) ;;
            *) fail "Web two-factor login did not redirect to /app." 69 ;;
        esac
        ;;
    *)
        fail "Web login did not redirect to /app or the two-factor step." 69
        ;;
esac

printf '%s\n' 'PASS Web form login and encrypted cookie session'

app_code="$(
    curl \
        --silent \
        --show-error \
        --output /dev/null \
        --write-out '%{http_code}' \
        --cookie "$cookie_jar" \
        --cookie-jar "$cookie_jar" \
        "$web_base_url/app"
)"
[ "$app_code" = "200" ] || fail "Authenticated /app smoke probe returned HTTP $app_code." 69
printf '%s\n' 'PASS authenticated /app surface (200)'

probe_bff_get()
{
    path="$1"
    expected="$2"

    code="$(
        curl \
            --silent \
            --show-error \
            --output /dev/null \
            --write-out '%{http_code}' \
            --cookie "$cookie_jar" \
            --cookie-jar "$cookie_jar" \
            "$web_base_url$path"
    )"

    [ "$code" = "$expected" ] ||
        fail "BFF smoke probe failed for $path: expected HTTP $expected, received $code." 69

    printf 'PASS %s (%s)\n' "$path" "$code"
}

for protected_path in \
    /bff/subscription \
    /bff/bank-connections \
    /bff/bank-accounts \
    '/bff/bank-transactions?take=5' \
    '/bff/alerts?take=5'
do
    probe_bff_get "$protected_path" 200
done

export_code="$(
    curl \
        --silent \
        --show-error \
        --output "$export_response" \
        --write-out '%{http_code}' \
        --cookie "$cookie_jar" \
        --cookie-jar "$cookie_jar" \
        "$web_base_url/bff/account/export"
)"
[ "$export_code" = "200" ] || fail "BFF account export returned HTTP $export_code." 69

if grep -Eiq \
    '"(accessToken|refreshToken|protectedAccessToken|encryptedAccessToken|plaidAccessToken|storagePath|storedFilePath|passwordHash|securityStamp)"[[:space:]]*:' \
    "$export_response"; then
    fail "BFF account export contained a forbidden secret or internal-storage field." 70
fi
rm -f "$export_response"
printf '%s\n' 'PASS BFF account export secret/storage boundary'

if [ -n "$foreign_bill_stream_id" ]; then
    probe_bff_get "/bff/bill-streams/$foreign_bill_stream_id" 404
fi

if [ -n "$foreign_bill_stream_id" ] && [ -n "$foreign_statement_upload_id" ]; then
    probe_bff_get \
        "/bff/bill-streams/$foreign_bill_stream_id/statement-uploads/$foreign_statement_upload_id" \
        404
fi

antiforgery_code="$(
    curl \
        --silent \
        --show-error \
        --output "$bff_antiforgery_response" \
        --write-out '%{http_code}' \
        --cookie "$cookie_jar" \
        --cookie-jar "$cookie_jar" \
        "$web_base_url/bff/antiforgery"
)"
[ "$antiforgery_code" = "200" ] || fail "Authenticated BFF antiforgery endpoint returned HTTP $antiforgery_code." 69

bff_token="$(
    sed -n 's/.*"requestToken"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p' \
        "$bff_antiforgery_response"
)"
rm -f "$bff_antiforgery_response"
[ -n "$bff_token" ] || fail "BFF antiforgery endpoint did not return a request token." 70
printf '%s' "$bff_token" > "$antiforgery_token_file"
chmod 600 "$antiforgery_token_file"
unset bff_token
printf '%s\n' 'PASS authenticated BFF antiforgery token issuance'

logout_headers="$work_directory/logout-headers.txt"
logout_code="$(
    curl \
        --silent \
        --show-error \
        --output /dev/null \
        --dump-header "$logout_headers" \
        --write-out '%{http_code}' \
        --request POST \
        --cookie "$cookie_jar" \
        --cookie-jar "$cookie_jar" \
        --header 'Content-Type: application/x-www-form-urlencoded' \
        --data-urlencode "__RequestVerificationToken@$antiforgery_token_file" \
        "$web_base_url/auth/logout"
)"
case "$logout_code" in
    302|303) ;;
    *) fail "Web logout returned HTTP $logout_code instead of a redirect." 69 ;;
esac

logout_location="$(
    sed -n 's/^[Ll]ocation:[[:space:]]*\([^\r]*\)\r*$/\1/p' "$logout_headers" |
        tail -n 1
)"
[ "$logout_location" = "/" ] || fail "Web logout did not redirect to /." 69

post_logout_code="$(
    curl \
        --silent \
        --show-error \
        --output /dev/null \
        --write-out '%{http_code}' \
        --cookie "$cookie_jar" \
        --cookie-jar "$cookie_jar" \
        "$web_base_url/bff/subscription"
)"
case "$post_logout_code" in
    302|401) ;;
    *) fail "BFF remained unexpectedly accessible after logout (HTTP $post_logout_code)." 70 ;;
esac
printf '%s\n' 'PASS antiforgery-protected logout invalidated the Web session'

printf '%s\n' 'BillWatch authenticated Web/BFF smoke harness passed.'

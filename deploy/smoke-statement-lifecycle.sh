#!/bin/sh

set -eu

api_base_url="${1:-}"
bill_stream_id="${BILLWATCH_STATEMENT_SMOKE_BILL_STREAM_ID:-}"
fixture_path="${BILLWATCH_STATEMENT_SMOKE_FIXTURE_PATH:-}"
email="${BILLWATCH_STATEMENT_SMOKE_EMAIL:-}"
password_file="${BILLWATCH_STATEMENT_SMOKE_PASSWORD_FILE:-}"
allow_upload="${BILLWATCH_STATEMENT_SMOKE_ALLOW_UPLOAD:-false}"
expected_status="${BILLWATCH_STATEMENT_SMOKE_EXPECT_STATUS:-any}"
foreign_bill_stream_id="${BILLWATCH_STATEMENT_SMOKE_FOREIGN_BILL_STREAM_ID:-}"
foreign_upload_id="${BILLWATCH_STATEMENT_SMOKE_FOREIGN_UPLOAD_ID:-}"
poll_attempts="${BILLWATCH_STATEMENT_SMOKE_POLL_ATTEMPTS:-60}"
poll_interval_seconds="${BILLWATCH_STATEMENT_SMOKE_POLL_INTERVAL_SECONDS:-2}"

fail()
{
    printf '%s\n' "$1" >&2
    exit "${2:-1}"
}

case "$api_base_url" in
    https://*) ;;
    *) fail "The statement smoke API base URL must use HTTPS." 64 ;;
esac
case "$api_base_url" in
    *[[:space:]]*) fail "The statement smoke API base URL must not contain whitespace." 64 ;;
esac
api_base_url="${api_base_url%/}"

case "$allow_upload" in
    true|false) ;;
    *) fail "BILLWATCH_STATEMENT_SMOKE_ALLOW_UPLOAD must be true or false." 64 ;;
esac
[ "$allow_upload" = "true" ] ||
    fail "Statement upload is disabled by default. Set BILLWATCH_STATEMENT_SMOKE_ALLOW_UPLOAD=true only for a controlled fixture and Bill Stream." 64

case "$expected_status" in
    any|Processed|Failed|NeedsOcr|ReadyForParsing) ;;
    *) fail "BILLWATCH_STATEMENT_SMOKE_EXPECT_STATUS must be any, Processed, Failed, NeedsOcr, or ReadyForParsing." 64 ;;
esac

case "$poll_attempts" in
    ''|*[!0-9]*) fail "BILLWATCH_STATEMENT_SMOKE_POLL_ATTEMPTS must be an integer." 64 ;;
esac
case "$poll_interval_seconds" in
    ''|*[!0-9]*) fail "BILLWATCH_STATEMENT_SMOKE_POLL_INTERVAL_SECONDS must be an integer." 64 ;;
esac
[ "$poll_attempts" -ge 1 ] && [ "$poll_attempts" -le 300 ] ||
    fail "BILLWATCH_STATEMENT_SMOKE_POLL_ATTEMPTS must be between 1 and 300." 64
[ "$poll_interval_seconds" -le 30 ] ||
    fail "BILLWATCH_STATEMENT_SMOKE_POLL_INTERVAL_SECONDS must be between 0 and 30." 64

[ -n "$bill_stream_id" ] || fail "BILLWATCH_STATEMENT_SMOKE_BILL_STREAM_ID is required." 64
[ -n "$fixture_path" ] || fail "BILLWATCH_STATEMENT_SMOKE_FIXTURE_PATH is required." 64
[ -f "$fixture_path" ] || fail "The statement fixture must reference a regular file." 64
[ ! -L "$fixture_path" ] || fail "The statement fixture must not be a symbolic link." 64
[ -s "$fixture_path" ] || fail "The statement fixture must not be empty." 64
case "$fixture_path" in
    *[\"\
]*) fail "The statement fixture path must not contain quotes or newlines." 64 ;;
esac

command -v sha256sum >/dev/null 2>&1 || fail "sha256sum is required for statement download integrity verification." 69

if [ -z "$email" ]; then
    if [ ! -t 0 ]; then
        fail "BILLWATCH_STATEMENT_SMOKE_EMAIL is required for non-interactive execution." 64
    fi
    printf 'BillWatch statement-smoke account email: ' >&2
    IFS= read -r email
fi
[ -n "$email" ] || fail "An account email is required." 64

if [ -n "$password_file" ]; then
    [ -f "$password_file" ] || fail "BILLWATCH_STATEMENT_SMOKE_PASSWORD_FILE must reference a regular file." 64
    [ ! -L "$password_file" ] || fail "BILLWATCH_STATEMENT_SMOKE_PASSWORD_FILE must not be a symbolic link." 64
    password_mode="$(stat -c '%a' "$password_file" 2>/dev/null || true)"
    [ "$password_mode" = "600" ] || fail "BILLWATCH_STATEMENT_SMOKE_PASSWORD_FILE must have mode 600." 64
    IFS= read -r password < "$password_file" || true
else
    if [ ! -t 0 ]; then
        fail "BILLWATCH_STATEMENT_SMOKE_PASSWORD_FILE is required for non-interactive execution." 64
    fi
    printf 'BillWatch password: ' >&2
    stty -echo
    trap 'stty echo 2>/dev/null || true' EXIT HUP INT TERM
    IFS= read -r password
    stty echo
    trap - EXIT HUP INT TERM
    printf '\n' >&2
fi
[ -n "${password:-}" ] || fail "A password is required." 64

work_directory="$(mktemp -d)"
chmod 700 "$work_directory"
cleanup()
{
    rm -rf "$work_directory"
}
trap cleanup EXIT HUP INT TERM

login_payload="$work_directory/login.json"
login_response="$work_directory/login-response.json"
auth_config="$work_directory/auth.curl"
upload_response="$work_directory/upload-response.json"
status_response="$work_directory/status-response.json"
download_file="$work_directory/downloaded-statement"

json_escape()
{
    printf '%s' "$1" | sed 's/\\/\\\\/g; s/"/\\"/g; s/\t/\\t/g'
}

printf '{"email":"%s","password":"%s"}' "$(json_escape "$email")" "$(json_escape "$password")" > "$login_payload"
chmod 600 "$login_payload"
unset password

login_code="$(curl --silent --show-error --output "$login_response" --write-out '%{http_code}' --request POST --header 'Content-Type: application/json' --data-binary "@$login_payload" "$api_base_url/api/auth/login")"
rm -f "$login_payload"
[ "$login_code" = "200" ] || fail "Statement-smoke authentication failed with HTTP $login_code." 69

access_token="$(sed -n 's/.*"accessToken"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p' "$login_response")"
rm -f "$login_response"
[ -n "$access_token" ] || fail "Authentication response did not contain an access token." 69
printf 'header = "Authorization: Bearer %s"\n' "$access_token" > "$auth_config"
chmod 600 "$auth_config"
unset access_token

stream_code="$(curl --silent --show-error --output /dev/null --write-out '%{http_code}' --config "$auth_config" "$api_base_url/api/bill-streams/$bill_stream_id")"
[ "$stream_code" = "200" ] || fail "Controlled Bill Stream ownership check failed with HTTP $stream_code." 69
printf '%s\n' 'PASS controlled Bill Stream ownership'

upload_code="$(curl --silent --show-error --output "$upload_response" --write-out '%{http_code}' --request POST --config "$auth_config" --form "file=@$fixture_path" "$api_base_url/api/bill-streams/$bill_stream_id/statement-uploads")"
[ "$upload_code" = "201" ] || fail "Statement upload failed with HTTP $upload_code." 69

if grep -Eiq '"(storageKey|storagePath|storedFilePath|accessToken|refreshToken|protectedAccessToken|encryptedAccessToken|passwordHash|securityStamp)"[[:space:]]*:' "$upload_response"; then
    fail "Statement upload response contained a forbidden secret or internal-storage field." 70
fi

upload_id="$(sed -n 's/.*"id"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p' "$upload_response")"
response_stream_id="$(sed -n 's/.*"billStreamId"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p' "$upload_response")"
rm -f "$upload_response"
[ -n "$upload_id" ] || fail "Statement upload response did not contain an upload ID." 69
[ "$response_stream_id" = "$bill_stream_id" ] || fail "Statement upload response did not preserve the controlled Bill Stream ID." 70
printf '%s\n' 'PASS guarded statement upload (201)'

status_path="/api/bill-streams/$bill_stream_id/statement-uploads/$upload_id"
terminal_status=""
attempt=1
while [ "$attempt" -le "$poll_attempts" ]
do
    status_code="$(curl --silent --show-error --output "$status_response" --write-out '%{http_code}' --config "$auth_config" "$api_base_url$status_path")"
    [ "$status_code" = "200" ] || fail "Statement status polling failed with HTTP $status_code." 69

    if grep -Eiq '"(storageKey|storagePath|storedFilePath|accessToken|refreshToken|protectedAccessToken|encryptedAccessToken|passwordHash|securityStamp)"[[:space:]]*:' "$status_response"; then
        fail "Statement status response contained a forbidden secret or internal-storage field." 70
    fi

    status="$(sed -n 's/.*"status"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p' "$status_response")"
    case "$status" in
        Processed|Failed|NeedsOcr|ReadyForParsing)
            terminal_status="$status"
            break
            ;;
        Uploaded|Processing) ;;
        *) fail "Statement status response contained an unknown status." 70 ;;
    esac

    attempt=$((attempt + 1))
    [ "$attempt" -le "$poll_attempts" ] && sleep "$poll_interval_seconds"
done
rm -f "$status_response"
[ -n "$terminal_status" ] || fail "Statement upload did not reach a truthful terminal state within the configured polling window." 69

if [ "$expected_status" != "any" ] && [ "$terminal_status" != "$expected_status" ]; then
    fail "Statement reached terminal status $terminal_status; expected $expected_status." 69
fi
printf 'PASS truthful terminal statement status (%s)\n' "$terminal_status"

download_code="$(curl --silent --show-error --output "$download_file" --write-out '%{http_code}' --config "$auth_config" "$api_base_url$status_path/file")"
[ "$download_code" = "200" ] || fail "Statement download integrity probe failed with HTTP $download_code." 69
original_hash="$(sha256sum "$fixture_path" | awk '{print $1}')"
download_hash="$(sha256sum "$download_file" | awk '{print $1}')"
[ "$original_hash" = "$download_hash" ] || fail "Downloaded statement bytes did not match the uploaded controlled fixture." 70
rm -f "$download_file"
printf '%s\n' 'PASS owned statement storage/download SHA-256 integrity'

if [ -n "$foreign_bill_stream_id" ] || [ -n "$foreign_upload_id" ]; then
    [ -n "$foreign_bill_stream_id" ] && [ -n "$foreign_upload_id" ] || fail "Both foreign Bill Stream and upload IDs are required for the cross-user probe." 64
    foreign_path="/api/bill-streams/$foreign_bill_stream_id/statement-uploads/$foreign_upload_id"
    foreign_code="$(curl --silent --show-error --output /dev/null --write-out '%{http_code}' --config "$auth_config" "$api_base_url$foreign_path")"
    [ "$foreign_code" = "404" ] || fail "Cross-user statement status probe expected HTTP 404, received $foreign_code." 70
    foreign_file_code="$(curl --silent --show-error --output /dev/null --write-out '%{http_code}' --config "$auth_config" "$api_base_url$foreign_path/file")"
    [ "$foreign_file_code" = "404" ] || fail "Cross-user statement file probe expected HTTP 404, received $foreign_file_code." 70
    printf '%s\n' 'PASS cross-user statement status/file isolation (404/404)'
fi

printf 'BillWatch guarded statement lifecycle smoke harness passed with terminal status %s.\n' "$terminal_status"

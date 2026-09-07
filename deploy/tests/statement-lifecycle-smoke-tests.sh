#!/bin/sh

set -eu

root_dir=$(CDPATH= cd -- "$(dirname -- "$0")/../.." && pwd)
temp_dir=$(mktemp -d)
trap 'rm -rf "$temp_dir"' EXIT HUP INT TERM

fail()
{
    printf '%s\n' "Statement lifecycle smoke test failed: $1" >&2
    exit 1
}

smoke_script="$root_dir/deploy/smoke-statement-lifecycle.sh"
[ -f "$smoke_script" ] || fail "statement lifecycle smoke harness is missing."
sh -n "$smoke_script" || fail "statement lifecycle smoke harness has invalid POSIX shell syntax."

grep -Fq 'BILLWATCH_STATEMENT_SMOKE_ALLOW_UPLOAD' "$smoke_script" || fail "statement upload must require explicit opt-in."
grep -Fq 'sha256sum' "$smoke_script" || fail "statement smoke harness must verify download integrity."
grep -Fq 'cross-user statement status/file isolation' "$smoke_script" || fail "statement smoke harness must support ownership-isolation proof."

fake_bin="$temp_dir/bin"
mkdir -p "$fake_bin"
curl_log="$temp_dir/curl.log"
poll_state="$temp_dir/poll-state"
fixture="$temp_dir/statement.pdf"
printf '%s' '%PDF-1.7 controlled statement smoke fixture' > "$fixture"

cat > "$fake_bin/curl" <<'EOF'
#!/bin/sh
set -eu

output=""
request="GET"
url=""
form=""

printf '%s\n' "$*" >> "$FAKE_CURL_LOG"

while [ "$#" -gt 0 ]
do
    case "$1" in
        --output)
            output="$2"
            shift 2
            ;;
        --request)
            request="$2"
            shift 2
            ;;
        --form)
            form="$2"
            shift 2
            ;;
        --write-out|--header|--data-binary|--config)
            shift 2
            ;;
        --silent|--show-error)
            shift
            ;;
        https://*)
            url="$1"
            shift
            ;;
        *)
            shift
            ;;
    esac
done

[ -n "$url" ] || exit 90
code=200
body='{}'

case "$url" in
    */api/auth/login)
        [ "$request" = "POST" ] || exit 91
        body='{"accessToken":"FAKE-BEARER-TOKEN","refreshToken":"FAKE-REFRESH-TOKEN"}'
        ;;
    */api/bill-streams/$FAKE_OWNED_STREAM_ID)
        body="{\"id\":\"$FAKE_OWNED_STREAM_ID\"}"
        ;;
    */api/bill-streams/$FAKE_OWNED_STREAM_ID/statement-uploads)
        [ "$request" = "POST" ] || exit 92
        [ -n "$form" ] || exit 93
        code=201
        if [ "${FAKE_EXPOSE_STORAGE:-false}" = "true" ]; then
            body="{\"id\":\"$FAKE_UPLOAD_ID\",\"billStreamId\":\"$FAKE_OWNED_STREAM_ID\",\"status\":\"Uploaded\",\"storagePath\":\"/private/statement\"}"
        else
            body="{\"id\":\"$FAKE_UPLOAD_ID\",\"billStreamId\":\"$FAKE_OWNED_STREAM_ID\",\"status\":\"Uploaded\"}"
        fi
        ;;
    */api/bill-streams/$FAKE_OWNED_STREAM_ID/statement-uploads/$FAKE_UPLOAD_ID/file)
        if [ "${FAKE_CORRUPT_DOWNLOAD:-false}" = "true" ]; then
            body='corrupted bytes'
        else
            cat "$FAKE_FIXTURE" > "$output"
            output=""
            body=''
        fi
        ;;
    */api/bill-streams/$FAKE_OWNED_STREAM_ID/statement-uploads/$FAKE_UPLOAD_ID)
        count=0
        [ ! -f "$FAKE_POLL_STATE" ] || count="$(cat "$FAKE_POLL_STATE")"
        count=$((count + 1))
        printf '%s' "$count" > "$FAKE_POLL_STATE"
        if [ "${FAKE_UNKNOWN_STATUS:-false}" = "true" ]; then
            body="{\"id\":\"$FAKE_UPLOAD_ID\",\"billStreamId\":\"$FAKE_OWNED_STREAM_ID\",\"status\":\"Mystery\"}"
        elif [ "${FAKE_NEVER_TERMINAL:-false}" = "true" ] || [ "$count" -eq 1 ]; then
            body="{\"id\":\"$FAKE_UPLOAD_ID\",\"billStreamId\":\"$FAKE_OWNED_STREAM_ID\",\"status\":\"Processing\"}"
        else
            body="{\"id\":\"$FAKE_UPLOAD_ID\",\"billStreamId\":\"$FAKE_OWNED_STREAM_ID\",\"status\":\"Processed\"}"
        fi
        ;;
    */api/bill-streams/$FAKE_FOREIGN_STREAM_ID/statement-uploads/$FAKE_FOREIGN_UPLOAD_ID|*/api/bill-streams/$FAKE_FOREIGN_STREAM_ID/statement-uploads/$FAKE_FOREIGN_UPLOAD_ID/file)
        code=404
        body='{"message":"Not found."}'
        ;;
esac

if [ -n "$output" ] && [ "$output" != "/dev/null" ]; then
    printf '%s' "$body" > "$output"
fi
printf '%s' "$code"
EOF
chmod 700 "$fake_bin/curl"

password_file="$temp_dir/password"
printf '%s\n' 'StatementSmokePassword!123456' > "$password_file"
chmod 600 "$password_file"

owned_stream_id='11111111-1111-1111-1111-111111111111'
upload_id='22222222-2222-2222-2222-222222222222'
foreign_stream_id='33333333-3333-3333-3333-333333333333'
foreign_upload_id='44444444-4444-4444-4444-444444444444'

run_smoke()
{
    rm -f "$poll_state"
    env \
        PATH="$fake_bin:$PATH" \
        FAKE_CURL_LOG="$curl_log" \
        FAKE_POLL_STATE="$poll_state" \
        FAKE_FIXTURE="$fixture" \
        FAKE_OWNED_STREAM_ID="$owned_stream_id" \
        FAKE_UPLOAD_ID="$upload_id" \
        FAKE_FOREIGN_STREAM_ID="$foreign_stream_id" \
        FAKE_FOREIGN_UPLOAD_ID="$foreign_upload_id" \
        BILLWATCH_STATEMENT_SMOKE_EMAIL='statement-smoke@example.test' \
        BILLWATCH_STATEMENT_SMOKE_PASSWORD_FILE="$password_file" \
        BILLWATCH_STATEMENT_SMOKE_BILL_STREAM_ID="$owned_stream_id" \
        BILLWATCH_STATEMENT_SMOKE_FIXTURE_PATH="$fixture" \
        BILLWATCH_STATEMENT_SMOKE_ALLOW_UPLOAD=true \
        BILLWATCH_STATEMENT_SMOKE_FOREIGN_BILL_STREAM_ID="$foreign_stream_id" \
        BILLWATCH_STATEMENT_SMOKE_FOREIGN_UPLOAD_ID="$foreign_upload_id" \
        BILLWATCH_STATEMENT_SMOKE_POLL_INTERVAL_SECONDS=0 \
        BILLWATCH_STATEMENT_SMOKE_POLL_ATTEMPTS=3 \
        "$@" \
        sh "$smoke_script" 'https://api.example.test'
}

: > "$curl_log"
run_smoke > "$temp_dir/success.out"
grep -Fq 'PASS guarded statement upload (201)' "$temp_dir/success.out" || fail "guarded upload proof did not run."
grep -Fq 'PASS truthful terminal statement status (Processed)' "$temp_dir/success.out" || fail "terminal-state proof did not run."
grep -Fq 'PASS owned statement storage/download SHA-256 integrity' "$temp_dir/success.out" || fail "download integrity proof did not run."
grep -Fq 'PASS cross-user statement status/file isolation (404/404)' "$temp_dir/success.out" || fail "cross-user proof did not run."

for secret in 'StatementSmokePassword!123456' 'FAKE-BEARER-TOKEN' 'FAKE-REFRESH-TOKEN'
do
    if grep -Fq "$secret" "$curl_log"; then
        fail "credential appeared in curl process arguments: $secret"
    fi
done

if env \
    PATH="$fake_bin:$PATH" \
    FAKE_CURL_LOG="$curl_log" \
    FAKE_POLL_STATE="$poll_state" \
    FAKE_FIXTURE="$fixture" \
    FAKE_OWNED_STREAM_ID="$owned_stream_id" \
    FAKE_UPLOAD_ID="$upload_id" \
    FAKE_FOREIGN_STREAM_ID="$foreign_stream_id" \
    FAKE_FOREIGN_UPLOAD_ID="$foreign_upload_id" \
    BILLWATCH_STATEMENT_SMOKE_EMAIL='statement-smoke@example.test' \
    BILLWATCH_STATEMENT_SMOKE_PASSWORD_FILE="$password_file" \
    BILLWATCH_STATEMENT_SMOKE_BILL_STREAM_ID="$owned_stream_id" \
    BILLWATCH_STATEMENT_SMOKE_FIXTURE_PATH="$fixture" \
    sh "$smoke_script" 'https://api.example.test' >/dev/null 2>&1; then
    fail "statement harness uploaded without explicit mutation opt-in."
fi

if run_smoke FAKE_EXPOSE_STORAGE=true >/dev/null 2>&1; then
    fail "statement harness accepted a response exposing an internal storage path."
fi
if run_smoke FAKE_CORRUPT_DOWNLOAD=true >/dev/null 2>&1; then
    fail "statement harness accepted corrupted downloaded bytes."
fi
if run_smoke FAKE_UNKNOWN_STATUS=true >/dev/null 2>&1; then
    fail "statement harness accepted an unknown processing status."
fi
if run_smoke FAKE_NEVER_TERMINAL=true BILLWATCH_STATEMENT_SMOKE_POLL_ATTEMPTS=2 >/dev/null 2>&1; then
    fail "statement harness accepted a non-terminal upload after the polling deadline."
fi
if run_smoke BILLWATCH_STATEMENT_SMOKE_EXPECT_STATUS=Failed >/dev/null 2>&1; then
    fail "statement harness accepted a terminal status different from the configured expectation."
fi

chmod 644 "$password_file"
if run_smoke >/dev/null 2>&1; then
    fail "statement harness accepted an insecure password-file mode."
fi
chmod 600 "$password_file"

if env \
    PATH="$fake_bin:$PATH" \
    FAKE_CURL_LOG="$curl_log" \
    FAKE_POLL_STATE="$poll_state" \
    FAKE_FIXTURE="$fixture" \
    FAKE_OWNED_STREAM_ID="$owned_stream_id" \
    FAKE_UPLOAD_ID="$upload_id" \
    FAKE_FOREIGN_STREAM_ID="$foreign_stream_id" \
    FAKE_FOREIGN_UPLOAD_ID="$foreign_upload_id" \
    BILLWATCH_STATEMENT_SMOKE_EMAIL='statement-smoke@example.test' \
    BILLWATCH_STATEMENT_SMOKE_PASSWORD_FILE="$password_file" \
    BILLWATCH_STATEMENT_SMOKE_BILL_STREAM_ID="$owned_stream_id" \
    BILLWATCH_STATEMENT_SMOKE_FIXTURE_PATH="$fixture" \
    BILLWATCH_STATEMENT_SMOKE_ALLOW_UPLOAD=true \
    sh "$smoke_script" 'http://api.example.test' >/dev/null 2>&1; then
    fail "statement harness accepted an HTTP API URL."
fi

printf '%s\n' 'Guarded statement lifecycle smoke harness tests passed.'

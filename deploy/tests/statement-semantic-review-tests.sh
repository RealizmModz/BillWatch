#!/bin/sh

set -eu

root_dir=$(CDPATH= cd -- "$(dirname -- "$0")/../.." && pwd)
temp_dir=$(mktemp -d)
trap 'rm -rf "$temp_dir"' EXIT HUP INT TERM

fail()
{
    printf '%s\n' "Statement semantic review test failed: $1" >&2
    exit 1
}

script="$root_dir/deploy/review-statement-semantics.sh"
[ -f "$script" ] || fail "semantic review script is missing"
sh -n "$script" || fail "semantic review script has invalid POSIX shell syntax"
command -v jq >/dev/null 2>&1 || fail "jq is required for this regression suite"

fake_bin="$temp_dir/bin"
mkdir -p "$fake_bin"
curl_log="$temp_dir/curl.log"

cat > "$fake_bin/curl" <<'EOF'
#!/bin/sh
set -eu

output=""
url=""

while [ "$#" -gt 0 ]
do
    case "$1" in
        --output)
            output="$2"
            shift 2
            ;;
        --write-out|--request|--header|--data-binary|--config)
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

printf '%s\n' "$url" >> "$BILLWATCH_TEST_CURL_LOG"

case "$url" in
    */api/auth/login)
        printf '%s' '{"accessToken":"semantic-test-token"}' > "$output"
        printf '%s' '200'
        ;;
    */api/bill-streams/foreign-stream)
        : > "$output"
        printf '%s' '404'
        ;;
    */api/bill-streams/controlled-stream)
        if [ "${BILLWATCH_TEST_LEAK_RAW_TEXT:-false}" = "true" ]; then
            printf '%s' '{"id":"controlled-stream","providerName":"Example Electric","category":"Utility","rawText":"private statement text","statements":[],"changes":[]}' > "$output"
        elif [ "${BILLWATCH_TEST_WRONG_TOTAL:-false}" = "true" ]; then
            printf '%s' '{"id":"controlled-stream","providerName":"Example Electric","category":"Utility","statements":[{"id":"statement-1","periodStart":"2026-07-01","periodEnd":"2026-07-31","statementDate":"2026-08-01","dueDate":"2026-08-20","totalAmount":130.00,"currencyCode":"USD"}],"changes":[]}' > "$output"
        else
            printf '%s' '{"id":"controlled-stream","providerName":"Example Electric","category":"Utility","statements":[{"id":"statement-1","periodStart":"2026-07-01","periodEnd":"2026-07-31","statementDate":"2026-08-01","dueDate":"2026-08-20","totalAmount":125.45,"currencyCode":"USD"}],"changes":[{"id":"change-1","changeType":"AmountChanged","confidence":"High","description":"Usage charge increased by $15.45","previousAmount":110.00,"currentAmount":125.45,"amountDifference":15.45,"annualizedImpact":185.40,"isAcknowledged":false,"detectedAtUtc":"2026-08-01T00:00:00Z"}]}' > "$output"
        fi
        printf '%s' '200'
        ;;
    *)
        : > "$output"
        printf '%s' '404'
        ;;
esac
EOF
chmod +x "$fake_bin/curl"

password_file="$temp_dir/password"
printf '%s\n' 'BillWatch!SemanticReview123' > "$password_file"
chmod 600 "$password_file"

run_review()
{
    PATH="$fake_bin:$PATH" \
    BILLWATCH_TEST_CURL_LOG="$curl_log" \
    BILLWATCH_SEMANTIC_REVIEW_BILL_STREAM_ID="controlled-stream" \
    BILLWATCH_SEMANTIC_REVIEW_STATEMENT_ID="statement-1" \
    BILLWATCH_SEMANTIC_REVIEW_EMAIL="semantic-review@billwatch.local" \
    BILLWATCH_SEMANTIC_REVIEW_PASSWORD_FILE="$password_file" \
    BILLWATCH_SEMANTIC_REVIEW_EXPECT_PROVIDER="Example Electric" \
    BILLWATCH_SEMANTIC_REVIEW_EXPECT_CATEGORY="Utility" \
    BILLWATCH_SEMANTIC_REVIEW_EXPECT_PERIOD_START="2026-07-01" \
    BILLWATCH_SEMANTIC_REVIEW_EXPECT_PERIOD_END="2026-07-31" \
    BILLWATCH_SEMANTIC_REVIEW_EXPECT_STATEMENT_DATE="2026-08-01" \
    BILLWATCH_SEMANTIC_REVIEW_EXPECT_DUE_DATE="2026-08-20" \
    BILLWATCH_SEMANTIC_REVIEW_EXPECT_TOTAL_AMOUNT="125.45" \
    BILLWATCH_SEMANTIC_REVIEW_EXPECT_CURRENCY="USD" \
    BILLWATCH_SEMANTIC_REVIEW_CHANGE_ID="change-1" \
    BILLWATCH_SEMANTIC_REVIEW_EXPECT_CHANGE_TYPE="AmountChanged" \
    BILLWATCH_SEMANTIC_REVIEW_EXPECT_CHANGE_CONFIDENCE="High" \
    BILLWATCH_SEMANTIC_REVIEW_EXPECT_CHANGE_PREVIOUS_AMOUNT="110.00" \
    BILLWATCH_SEMANTIC_REVIEW_EXPECT_CHANGE_CURRENT_AMOUNT="125.45" \
    BILLWATCH_SEMANTIC_REVIEW_EXPECT_CHANGE_DIFFERENCE="15.45" \
    BILLWATCH_SEMANTIC_REVIEW_EXPECT_CHANGE_DESCRIPTION_CONTAINS="Usage charge increased" \
    BILLWATCH_SEMANTIC_REVIEW_FOREIGN_BILL_STREAM_ID="foreign-stream" \
    sh "$script" https://billwatch.test
}

: > "$curl_log"
run_review >/dev/null || fail "valid controlled semantic review failed"
grep -q '/api/auth/login$' "$curl_log" || fail "semantic review did not authenticate"
grep -q '/api/bill-streams/controlled-stream$' "$curl_log" || fail "semantic review did not read the controlled Bill Stream"
grep -q '/api/bill-streams/foreign-stream$' "$curl_log" || fail "semantic review did not execute the cross-user isolation probe"

if grep -q 'BillWatch!SemanticReview123' "$curl_log"; then
    fail "password leaked into curl URL/arguments"
fi

if grep -Eq '/statement-uploads|/checkout|/sync|DELETE|POST .*bill-streams' "$curl_log"; then
    fail "read-only semantic review invoked an unexpected mutation-bearing route"
fi

: > "$curl_log"
if PATH="$fake_bin:$PATH" \
   BILLWATCH_TEST_CURL_LOG="$curl_log" \
   BILLWATCH_TEST_WRONG_TOTAL=true \
   BILLWATCH_SEMANTIC_REVIEW_BILL_STREAM_ID="controlled-stream" \
   BILLWATCH_SEMANTIC_REVIEW_STATEMENT_ID="statement-1" \
   BILLWATCH_SEMANTIC_REVIEW_EMAIL="semantic-review@billwatch.local" \
   BILLWATCH_SEMANTIC_REVIEW_PASSWORD_FILE="$password_file" \
   BILLWATCH_SEMANTIC_REVIEW_EXPECT_PROVIDER="Example Electric" \
   BILLWATCH_SEMANTIC_REVIEW_EXPECT_CATEGORY="Utility" \
   BILLWATCH_SEMANTIC_REVIEW_EXPECT_PERIOD_START="2026-07-01" \
   BILLWATCH_SEMANTIC_REVIEW_EXPECT_PERIOD_END="2026-07-31" \
   BILLWATCH_SEMANTIC_REVIEW_EXPECT_TOTAL_AMOUNT="125.45" \
   BILLWATCH_SEMANTIC_REVIEW_EXPECT_CURRENCY="USD" \
   sh "$script" https://billwatch.test >/dev/null 2>&1; then
    fail "semantic review accepted an incorrect persisted amount"
fi

if PATH="$fake_bin:$PATH" \
   BILLWATCH_TEST_CURL_LOG="$curl_log" \
   BILLWATCH_TEST_LEAK_RAW_TEXT=true \
   BILLWATCH_SEMANTIC_REVIEW_BILL_STREAM_ID="controlled-stream" \
   BILLWATCH_SEMANTIC_REVIEW_STATEMENT_ID="statement-1" \
   BILLWATCH_SEMANTIC_REVIEW_EMAIL="semantic-review@billwatch.local" \
   BILLWATCH_SEMANTIC_REVIEW_PASSWORD_FILE="$password_file" \
   BILLWATCH_SEMANTIC_REVIEW_EXPECT_PROVIDER="Example Electric" \
   BILLWATCH_SEMANTIC_REVIEW_EXPECT_CATEGORY="Utility" \
   BILLWATCH_SEMANTIC_REVIEW_EXPECT_PERIOD_START="2026-07-01" \
   BILLWATCH_SEMANTIC_REVIEW_EXPECT_PERIOD_END="2026-07-31" \
   BILLWATCH_SEMANTIC_REVIEW_EXPECT_TOTAL_AMOUNT="125.45" \
   BILLWATCH_SEMANTIC_REVIEW_EXPECT_CURRENCY="USD" \
   sh "$script" https://billwatch.test >/dev/null 2>&1; then
    fail "semantic review accepted a response leaking raw statement text"
fi

if PATH="$fake_bin:$PATH" \
   BILLWATCH_TEST_CURL_LOG="$curl_log" \
   BILLWATCH_SEMANTIC_REVIEW_BILL_STREAM_ID="controlled-stream" \
   BILLWATCH_SEMANTIC_REVIEW_STATEMENT_ID="statement-1" \
   BILLWATCH_SEMANTIC_REVIEW_EMAIL="semantic-review@billwatch.local" \
   BILLWATCH_SEMANTIC_REVIEW_PASSWORD_FILE="$password_file" \
   BILLWATCH_SEMANTIC_REVIEW_EXPECT_PROVIDER="Wrong Provider" \
   BILLWATCH_SEMANTIC_REVIEW_EXPECT_CATEGORY="Utility" \
   BILLWATCH_SEMANTIC_REVIEW_EXPECT_PERIOD_START="2026-07-01" \
   BILLWATCH_SEMANTIC_REVIEW_EXPECT_PERIOD_END="2026-07-31" \
   BILLWATCH_SEMANTIC_REVIEW_EXPECT_TOTAL_AMOUNT="125.45" \
   BILLWATCH_SEMANTIC_REVIEW_EXPECT_CURRENCY="USD" \
   sh "$script" https://billwatch.test >/dev/null 2>&1; then
    fail "semantic review accepted an incorrect provider expectation"
fi

if PATH="$fake_bin:$PATH" \
   BILLWATCH_TEST_CURL_LOG="$curl_log" \
   BILLWATCH_SEMANTIC_REVIEW_BILL_STREAM_ID="controlled-stream" \
   BILLWATCH_SEMANTIC_REVIEW_STATEMENT_ID="statement-1" \
   BILLWATCH_SEMANTIC_REVIEW_EMAIL="semantic-review@billwatch.local" \
   BILLWATCH_SEMANTIC_REVIEW_PASSWORD_FILE="$password_file" \
   BILLWATCH_SEMANTIC_REVIEW_EXPECT_PROVIDER="Example Electric" \
   BILLWATCH_SEMANTIC_REVIEW_EXPECT_CATEGORY="Utility" \
   BILLWATCH_SEMANTIC_REVIEW_EXPECT_PERIOD_START="2026-07-01" \
   BILLWATCH_SEMANTIC_REVIEW_EXPECT_PERIOD_END="2026-07-31" \
   BILLWATCH_SEMANTIC_REVIEW_EXPECT_TOTAL_AMOUNT="125.45" \
   BILLWATCH_SEMANTIC_REVIEW_EXPECT_CURRENCY="USD" \
   sh "$script" http://billwatch.test >/dev/null 2>&1; then
    fail "semantic review accepted a non-HTTPS API URL"
fi

chmod 644 "$password_file"
if PATH="$fake_bin:$PATH" \
   BILLWATCH_TEST_CURL_LOG="$curl_log" \
   BILLWATCH_SEMANTIC_REVIEW_BILL_STREAM_ID="controlled-stream" \
   BILLWATCH_SEMANTIC_REVIEW_STATEMENT_ID="statement-1" \
   BILLWATCH_SEMANTIC_REVIEW_EMAIL="semantic-review@billwatch.local" \
   BILLWATCH_SEMANTIC_REVIEW_PASSWORD_FILE="$password_file" \
   BILLWATCH_SEMANTIC_REVIEW_EXPECT_PROVIDER="Example Electric" \
   BILLWATCH_SEMANTIC_REVIEW_EXPECT_CATEGORY="Utility" \
   BILLWATCH_SEMANTIC_REVIEW_EXPECT_PERIOD_START="2026-07-01" \
   BILLWATCH_SEMANTIC_REVIEW_EXPECT_PERIOD_END="2026-07-31" \
   BILLWATCH_SEMANTIC_REVIEW_EXPECT_TOTAL_AMOUNT="125.45" \
   BILLWATCH_SEMANTIC_REVIEW_EXPECT_CURRENCY="USD" \
   sh "$script" https://billwatch.test >/dev/null 2>&1; then
    fail "semantic review accepted weak password-file permissions"
fi
chmod 600 "$password_file"

for forbidden in 'statementText' 'rawText' 'extractedText' 'storagePath' 'accessToken'
do
    grep -q "$forbidden" "$script" || fail "semantic review does not guard forbidden response field $forbidden"
done

printf '%s\n' 'Statement semantic review tests passed.'

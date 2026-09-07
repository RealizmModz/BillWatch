#!/bin/sh

set -eu

api_base_url="${1:-}"
bill_stream_id="${BILLWATCH_SEMANTIC_REVIEW_BILL_STREAM_ID:-}"
statement_id="${BILLWATCH_SEMANTIC_REVIEW_STATEMENT_ID:-}"
email="${BILLWATCH_SEMANTIC_REVIEW_EMAIL:-}"
password_file="${BILLWATCH_SEMANTIC_REVIEW_PASSWORD_FILE:-}"
expected_provider="${BILLWATCH_SEMANTIC_REVIEW_EXPECT_PROVIDER:-}"
expected_category="${BILLWATCH_SEMANTIC_REVIEW_EXPECT_CATEGORY:-}"
expected_period_start="${BILLWATCH_SEMANTIC_REVIEW_EXPECT_PERIOD_START:-}"
expected_period_end="${BILLWATCH_SEMANTIC_REVIEW_EXPECT_PERIOD_END:-}"
expected_statement_date="${BILLWATCH_SEMANTIC_REVIEW_EXPECT_STATEMENT_DATE:-any}"
expected_due_date="${BILLWATCH_SEMANTIC_REVIEW_EXPECT_DUE_DATE:-any}"
expected_total_amount="${BILLWATCH_SEMANTIC_REVIEW_EXPECT_TOTAL_AMOUNT:-}"
expected_currency="${BILLWATCH_SEMANTIC_REVIEW_EXPECT_CURRENCY:-}"
change_id="${BILLWATCH_SEMANTIC_REVIEW_CHANGE_ID:-}"
expected_change_type="${BILLWATCH_SEMANTIC_REVIEW_EXPECT_CHANGE_TYPE:-}"
expected_change_confidence="${BILLWATCH_SEMANTIC_REVIEW_EXPECT_CHANGE_CONFIDENCE:-}"
expected_change_previous_amount="${BILLWATCH_SEMANTIC_REVIEW_EXPECT_CHANGE_PREVIOUS_AMOUNT:-}"
expected_change_current_amount="${BILLWATCH_SEMANTIC_REVIEW_EXPECT_CHANGE_CURRENT_AMOUNT:-}"
expected_change_difference="${BILLWATCH_SEMANTIC_REVIEW_EXPECT_CHANGE_DIFFERENCE:-}"
expected_change_description_contains="${BILLWATCH_SEMANTIC_REVIEW_EXPECT_CHANGE_DESCRIPTION_CONTAINS:-}"
foreign_bill_stream_id="${BILLWATCH_SEMANTIC_REVIEW_FOREIGN_BILL_STREAM_ID:-}"

fail()
{
    printf '%s\n' "$1" >&2
    exit "${2:-1}"
}

case "$api_base_url" in
    https://*) ;;
    *) fail "The semantic-review API base URL must use HTTPS." 64 ;;
esac
case "$api_base_url" in
    *[[:space:]]*) fail "The semantic-review API base URL must not contain whitespace." 64 ;;
esac
api_base_url="${api_base_url%/}"

command -v jq >/dev/null 2>&1 || fail "jq is required for semantic statement verification." 69

for required_pair in \
    "BILLWATCH_SEMANTIC_REVIEW_BILL_STREAM_ID:$bill_stream_id" \
    "BILLWATCH_SEMANTIC_REVIEW_STATEMENT_ID:$statement_id" \
    "BILLWATCH_SEMANTIC_REVIEW_EXPECT_PROVIDER:$expected_provider" \
    "BILLWATCH_SEMANTIC_REVIEW_EXPECT_CATEGORY:$expected_category" \
    "BILLWATCH_SEMANTIC_REVIEW_EXPECT_PERIOD_START:$expected_period_start" \
    "BILLWATCH_SEMANTIC_REVIEW_EXPECT_PERIOD_END:$expected_period_end" \
    "BILLWATCH_SEMANTIC_REVIEW_EXPECT_TOTAL_AMOUNT:$expected_total_amount" \
    "BILLWATCH_SEMANTIC_REVIEW_EXPECT_CURRENCY:$expected_currency"
do
    required_name=${required_pair%%:*}
    required_value=${required_pair#*:}
    [ -n "$required_value" ] || fail "$required_name is required." 64
done

case "$expected_total_amount" in
    -[0-9]*|[0-9]*) ;;
    *) fail "BILLWATCH_SEMANTIC_REVIEW_EXPECT_TOTAL_AMOUNT must be numeric." 64 ;;
esac

if [ -n "$change_id" ]; then
    for required_pair in \
        "BILLWATCH_SEMANTIC_REVIEW_EXPECT_CHANGE_TYPE:$expected_change_type" \
        "BILLWATCH_SEMANTIC_REVIEW_EXPECT_CHANGE_CONFIDENCE:$expected_change_confidence" \
        "BILLWATCH_SEMANTIC_REVIEW_EXPECT_CHANGE_PREVIOUS_AMOUNT:$expected_change_previous_amount" \
        "BILLWATCH_SEMANTIC_REVIEW_EXPECT_CHANGE_CURRENT_AMOUNT:$expected_change_current_amount" \
        "BILLWATCH_SEMANTIC_REVIEW_EXPECT_CHANGE_DIFFERENCE:$expected_change_difference"
    do
        required_name=${required_pair%%:*}
        required_value=${required_pair#*:}
        [ -n "$required_value" ] || fail "$required_name is required when BILLWATCH_SEMANTIC_REVIEW_CHANGE_ID is set." 64
    done
fi

if [ -z "$email" ]; then
    if [ ! -t 0 ]; then
        fail "BILLWATCH_SEMANTIC_REVIEW_EMAIL is required for non-interactive execution." 64
    fi
    printf 'BillWatch semantic-review account email: ' >&2
    IFS= read -r email
fi
[ -n "$email" ] || fail "An account email is required." 64

if [ -n "$password_file" ]; then
    [ -f "$password_file" ] || fail "BILLWATCH_SEMANTIC_REVIEW_PASSWORD_FILE must reference a regular file." 64
    [ ! -L "$password_file" ] || fail "BILLWATCH_SEMANTIC_REVIEW_PASSWORD_FILE must not be a symbolic link." 64
    password_mode="$(stat -c '%a' "$password_file" 2>/dev/null || true)"
    [ "$password_mode" = "600" ] || fail "BILLWATCH_SEMANTIC_REVIEW_PASSWORD_FILE must have mode 600." 64
    IFS= read -r password < "$password_file" || true
else
    if [ ! -t 0 ]; then
        fail "BILLWATCH_SEMANTIC_REVIEW_PASSWORD_FILE is required for non-interactive execution." 64
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
detail_response="$work_directory/detail-response.json"

json_escape()
{
    printf '%s' "$1" | sed 's/\\/\\\\/g; s/"/\\"/g; s/\t/\\t/g'
}

printf '{"email":"%s","password":"%s"}' "$(json_escape "$email")" "$(json_escape "$password")" > "$login_payload"
chmod 600 "$login_payload"
unset password

login_code="$(curl --silent --show-error --output "$login_response" --write-out '%{http_code}' --request POST --header 'Content-Type: application/json' --data-binary "@$login_payload" "$api_base_url/api/auth/login")"
rm -f "$login_payload"
[ "$login_code" = "200" ] || fail "Semantic-review authentication failed with HTTP $login_code." 69

access_token="$(jq --exit-status --raw-output '.accessToken // empty' "$login_response" 2>/dev/null || true)"
rm -f "$login_response"
[ -n "$access_token" ] || fail "Authentication response did not contain an access token." 69
printf 'header = "Authorization: Bearer %s"\n' "$access_token" > "$auth_config"
chmod 600 "$auth_config"
unset access_token

detail_code="$(curl --silent --show-error --output "$detail_response" --write-out '%{http_code}' --config "$auth_config" "$api_base_url/api/bill-streams/$bill_stream_id")"
[ "$detail_code" = "200" ] || fail "Controlled Bill Stream detail probe failed with HTTP $detail_code." 69

if grep -Eiq '"(storageKey|storagePath|storedFilePath|accessToken|refreshToken|protectedAccessToken|encryptedAccessToken|passwordHash|securityStamp|rawText|extractedText|statementText)"[[:space:]]*:' "$detail_response"; then
    fail "Bill Stream detail response contained a forbidden secret, internal-storage field, or raw statement text." 70
fi

jq --exit-status \
    --arg id "$bill_stream_id" \
    --arg provider "$expected_provider" \
    --arg category "$expected_category" \
    '.id == $id and .providerName == $provider and .category == $category' \
    "$detail_response" >/dev/null ||
    fail "Bill Stream identity/provider/category did not match the controlled semantic-review expectation." 70
printf '%s\n' 'PASS controlled Bill Stream identity/provider/category semantics'

statement_match="$(jq --compact-output --exit-status --arg statementId "$statement_id" '.statements[] | select(.id == $statementId)' "$detail_response" 2>/dev/null || true)"
[ -n "$statement_match" ] || fail "The expected persisted statement was not found on the controlled Bill Stream." 69

printf '%s' "$statement_match" | jq --exit-status \
    --arg periodStart "$expected_period_start" \
    --arg periodEnd "$expected_period_end" \
    --arg totalAmount "$expected_total_amount" \
    --arg currency "$expected_currency" \
    '.periodStart == $periodStart and
     .periodEnd == $periodEnd and
     .totalAmount == ($totalAmount | tonumber) and
     .currencyCode == $currency' >/dev/null ||
    fail "Persisted statement period/amount/currency did not match the controlled fixture expectation." 70

if [ "$expected_statement_date" != "any" ]; then
    if [ "$expected_statement_date" = "null" ]; then
        printf '%s' "$statement_match" | jq --exit-status '.statementDate == null' >/dev/null ||
            fail "Persisted statement date was expected to be null." 70
    else
        printf '%s' "$statement_match" | jq --exit-status --arg expected "$expected_statement_date" '.statementDate == $expected' >/dev/null ||
            fail "Persisted statement date did not match the controlled fixture expectation." 70
    fi
fi

if [ "$expected_due_date" != "any" ]; then
    if [ "$expected_due_date" = "null" ]; then
        printf '%s' "$statement_match" | jq --exit-status '.dueDate == null' >/dev/null ||
            fail "Persisted due date was expected to be null." 70
    else
        printf '%s' "$statement_match" | jq --exit-status --arg expected "$expected_due_date" '.dueDate == $expected' >/dev/null ||
            fail "Persisted due date did not match the controlled fixture expectation." 70
    fi
fi
printf '%s\n' 'PASS persisted statement extraction semantics'

if [ -n "$change_id" ]; then
    change_match="$(jq --compact-output --exit-status --arg changeId "$change_id" '.changes[] | select(.id == $changeId)' "$detail_response" 2>/dev/null || true)"
    [ -n "$change_match" ] || fail "The expected detected bill change was not found on the controlled Bill Stream." 69

    printf '%s' "$change_match" | jq --exit-status \
        --arg type "$expected_change_type" \
        --arg confidence "$expected_change_confidence" \
        --arg previous "$expected_change_previous_amount" \
        --arg current "$expected_change_current_amount" \
        --arg difference "$expected_change_difference" \
        '.changeType == $type and
         .confidence == $confidence and
         .previousAmount == ($previous | tonumber) and
         .currentAmount == ($current | tonumber) and
         .amountDifference == ($difference | tonumber)' >/dev/null ||
        fail "Detected bill-change type/confidence/amount semantics did not match the controlled expectation." 70

    if [ -n "$expected_change_description_contains" ]; then
        printf '%s' "$change_match" | jq --exit-status --arg expected "$expected_change_description_contains" '.description | contains($expected)' >/dev/null ||
            fail "Detected bill-change explanation did not contain the controlled expected phrase." 70
    fi
    printf '%s\n' 'PASS persisted bill-change semantics'
fi

if [ -n "$foreign_bill_stream_id" ]; then
    foreign_code="$(curl --silent --show-error --output /dev/null --write-out '%{http_code}' --config "$auth_config" "$api_base_url/api/bill-streams/$foreign_bill_stream_id")"
    [ "$foreign_code" = "404" ] || fail "Cross-user Bill Stream detail probe expected HTTP 404, received $foreign_code." 70
    printf '%s\n' 'PASS cross-user semantic detail isolation (404)'
fi

rm -f "$detail_response"
printf '%s\n' 'BillWatch controlled statement semantic review passed.'

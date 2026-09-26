#!/bin/sh

set -eu

umask 077

fail()
{
    printf '%s\n' "AI evaluation configuration invalid: $1" >&2
    exit 64
}

env_file=${1:-.env.ai}

[ -f "$env_file" ] || fail "environment file is missing."
[ ! -L "$env_file" ] || fail "environment file must not be a symbolic link."

owner_id=$(stat -c '%u' "$env_file") ||
    fail "environment file ownership cannot be read."

mode=$(stat -c '%a' "$env_file") ||
    fail "environment file permissions cannot be read."

[ "$owner_id" = "$(id -u)" ] ||
    fail "environment file must be owned by the current account."

case "$mode" in
    ?00|??00) ;;
    *) fail "environment file must not grant permissions to group/other users." ;;
esac

if grep -q "$(printf '\r')" "$env_file"; then
    fail "environment file must use Unix line endings."
fi

invalid_line=$(awk '
    /^[[:space:]]*$/ { next }
    /^[[:space:]]*#/ { next }
    /^[A-Z][A-Z0-9_]*=[^\r\n]*$/ { next }
    { print NR; exit }
' "$env_file")

[ -z "$invalid_line" ] ||
    fail "line $invalid_line is not a KEY=value entry."

read_value()
{
    key=$1

    count=$(awk -F= -v key="$key" '
        $1 == key { count++ }
        END { print count + 0 }
    ' "$env_file")

    [ "$count" -eq 1 ] ||
        fail "$key must appear exactly once."

    awk -v prefix="$key=" '
        index($0, prefix) == 1 {
            print substr($0, length(prefix) + 1)
            exit
        }
    ' "$env_file"
}

reject_placeholder()
{
    key=$1
    value=$2

    [ -n "$value" ] ||
        fail "$key is empty."

    case "$value" in
        *replace-with*|*placeholder*|*change-me*|*changeme*)
            fail "$key still contains an example or placeholder value."
            ;;
    esac
}

reject_unsafe_value()
{
    key=$1
    value=$2

    case "$value" in
        *[!A-Za-z0-9._~!@%+=,:/@-]*)
            fail "$key contains whitespace, quoting, interpolation, or unsupported characters."
            ;;
    esac
}

validate_integer_range()
{
    key=$1
    value=$2
    minimum=$3
    maximum=$4

    case "$value" in
        ''|*[!0-9]*)
            fail "$key must be an integer."
            ;;
    esac

    [ "$value" -ge "$minimum" ] &&
    [ "$value" -le "$maximum" ] ||
        fail "$key must be between $minimum and $maximum."
}

image=$(read_value FULLWORTH_LOCAL_AI_IMAGE)
model_path=$(read_value FULLWORTH_LOCAL_AI_MODEL_PATH)
model_sha256=$(read_value FULLWORTH_LOCAL_AI_MODEL_SHA256)
api_key=$(read_value FULLWORTH_LOCAL_AI_API_KEY)
model_alias=$(read_value FULLWORTH_LOCAL_AI_MODEL_ALIAS)
context_size=$(read_value FULLWORTH_LOCAL_AI_CONTEXT_SIZE)
max_predict=$(read_value FULLWORTH_LOCAL_AI_MAX_PREDICT)
threads=$(read_value FULLWORTH_LOCAL_AI_THREADS)
gpu_layers=$(read_value FULLWORTH_LOCAL_AI_GPU_LAYERS)

for required_pair in \
    "FULLWORTH_LOCAL_AI_IMAGE:$image" \
    "FULLWORTH_LOCAL_AI_MODEL_PATH:$model_path" \
    "FULLWORTH_LOCAL_AI_MODEL_SHA256:$model_sha256" \
    "FULLWORTH_LOCAL_AI_API_KEY:$api_key" \
    "FULLWORTH_LOCAL_AI_MODEL_ALIAS:$model_alias"
do
    key=${required_pair%%:*}
    value=${required_pair#*:}

    reject_placeholder "$key" "$value"
    reject_unsafe_value "$key" "$value"
done

printf '%s\n' "$image" |
    grep -Eq '^ghcr\.io/ggml-org/llama\.cpp:[A-Za-z0-9._-]+@sha256:[0-9a-f]{64}$' ||
    fail "FULLWORTH_LOCAL_AI_IMAGE must pin the official llama.cpp image with an immutable sha256 digest."

case "$model_path" in
    /*) ;;
    *) fail "FULLWORTH_LOCAL_AI_MODEL_PATH must be an absolute path." ;;
esac

[ -f "$model_path" ] ||
    fail "FULLWORTH_LOCAL_AI_MODEL_PATH must reference an existing regular file."

[ ! -L "$model_path" ] ||
    fail "FULLWORTH_LOCAL_AI_MODEL_PATH must not be a symbolic link."

[ -s "$model_path" ] ||
    fail "FULLWORTH_LOCAL_AI_MODEL_PATH must not be empty."

printf '%s\n' "$model_sha256" |
    grep -Eq '^[0-9a-f]{64}$' ||
    fail "FULLWORTH_LOCAL_AI_MODEL_SHA256 must be 64 lowercase hexadecimal characters."

command -v sha256sum >/dev/null 2>&1 ||
    fail "sha256sum is required to verify the model artifact."

actual_model_sha256=$(
    sha256sum "$model_path" |
    awk '{ print $1 }'
) || fail "model SHA-256 could not be calculated."

[ "$actual_model_sha256" = "$model_sha256" ] ||
    fail "model SHA-256 does not match the approved artifact."

[ "${#api_key}" -ge 32 ] ||
    fail "FULLWORTH_LOCAL_AI_API_KEY must contain at least 32 characters."

printf '%s\n' "$model_alias" |
    grep -Eq '^[A-Za-z0-9._-]{1,80}$' ||
    fail "FULLWORTH_LOCAL_AI_MODEL_ALIAS contains unsupported characters or length."

validate_integer_range     FULLWORTH_LOCAL_AI_CONTEXT_SIZE     "$context_size"     2048     65536

validate_integer_range     FULLWORTH_LOCAL_AI_MAX_PREDICT     "$max_predict"     256     8192

validate_integer_range     FULLWORTH_LOCAL_AI_THREADS     "$threads"     1     128

validate_integer_range     FULLWORTH_LOCAL_AI_GPU_LAYERS     "$gpu_layers"     0     999

printf '%s\n' "AI evaluation configuration valid."

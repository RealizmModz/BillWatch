#!/bin/sh
set -eu

fail()
{
    printf '%s\n' "local-ai: $*" >&2
    exit 1
}

script_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
repo_root=$(CDPATH= cd -- "$script_dir/.." && pwd)
model_dir="$repo_root/.private/FullWorth.Models"

: "${FULLWORTH_AI_MODEL_FILE:?Set FULLWORTH_AI_MODEL_FILE to the approved GGUF filename}"
: "${FULLWORTH_AI_MODEL_SHA256:?Set FULLWORTH_AI_MODEL_SHA256 to the approved lowercase SHA-256}"
: "${FULLWORTH_AI_API_KEY:?Set FULLWORTH_AI_API_KEY without committing it}"

case "$FULLWORTH_AI_MODEL_FILE" in
    */*|*\\*|.|..)
        fail "FULLWORTH_AI_MODEL_FILE must be a filename, not a path."
        ;;
esac

case "$FULLWORTH_AI_MODEL_SHA256" in
    *[!0-9a-f]*)
        fail "FULLWORTH_AI_MODEL_SHA256 must contain only lowercase hexadecimal characters."
        ;;
esac

[ "${#FULLWORTH_AI_MODEL_SHA256}" -eq 64 ] ||
    fail "FULLWORTH_AI_MODEL_SHA256 must be exactly 64 characters."

[ "${#FULLWORTH_AI_API_KEY}" -ge 32 ] ||
    fail "FULLWORTH_AI_API_KEY must be at least 32 characters."

model_path="$model_dir/$FULLWORTH_AI_MODEL_FILE"

[ -f "$model_path" ] ||
    fail "approved model file was not found under .private/FullWorth.Models."

command -v sha256sum >/dev/null 2>&1 ||
    fail "sha256sum is required to verify the model artifact."

actual_sha256=$(
    sha256sum "$model_path" |
        awk '{print $1}'
)

[ "$actual_sha256" = "$FULLWORTH_AI_MODEL_SHA256" ] ||
    fail "model SHA-256 does not match the approved artifact."

command -v docker >/dev/null 2>&1 ||
    fail "Docker is required."

docker compose version >/dev/null 2>&1 ||
    fail "Docker Compose v2 is required."

printf '%s\n'     "local-ai: model artifact verified; starting isolated llama.cpp runtime."

exec docker compose     --project-directory "$repo_root"     -f "$repo_root/compose.local-ai.yml"     up -d

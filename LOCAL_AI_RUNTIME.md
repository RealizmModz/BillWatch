# FullWorth Local AI Runtime

Status: development-only runtime profile.

This profile exists to run statement-AI experiments without sending financial
statement text to an external AI API. It does not enable AI-derived persistence
and it is not part of the normal production deployment.

## Security boundary

The local runtime is intentionally constrained:

- model weights live under `.private/FullWorth.Models/` and are ignored by Git;
- the model directory is mounted read-only;
- the container filesystem is read-only except for a small temporary filesystem;
- Linux capabilities are dropped and `no-new-privileges` is enabled;
- the llama.cpp Web UI is disabled;
- llama.cpp agent/tools are not enabled;
- the container is attached only to an internal Docker network;
- the only host port is bound to `127.0.0.1`;
- llama.cpp API authentication is required by the Compose profile;
- FullWorth validates the configured model artifact SHA-256 before startup;
- FullWorth still treats model output as untrusted candidate data;
- deterministic FullWorth code remains responsible for evidence validation,
  ownership, financial arithmetic, persistence, comparisons, and alerts.

Do not expose port 8080 publicly and do not add llama.cpp file/shell/agent tools
to this profile.

## Runtime

The profile uses the official llama.cpp server image and its
OpenAI-compatible `/v1/chat/completions` API.

The default image reference is suitable only for local development. Before a
release or controlled production deployment, pin the exact approved image
digest with `FULLWORTH_LLAMA_IMAGE` and record that artifact in FullWorth's
third-party license/provenance inventory.

No model is downloaded automatically. This is deliberate: a model must be
selected, license-reviewed, downloaded from an authoritative source, and
hash-pinned before FullWorth will start it.

## Model candidate

The current evaluation candidate is a quantized Qwen-family instruction model
that fits the available development hardware. Qwen3-14B GGUF is one candidate,
not a permanent architecture requirement.

Do not infer that any file named like a Qwen model is approved. Record and
verify the exact artifact and license before using it.

## Required local values

Keep these values outside Git:

- `FULLWORTH_AI_MODEL_FILE` — filename of the approved GGUF file;
- `FULLWORTH_AI_MODEL_SHA256` — lowercase SHA-256 of that exact file;
- `FULLWORTH_AI_API_KEY` — random secret of at least 32 characters.

Optional:

- `FULLWORTH_LLAMA_IMAGE` — exact llama.cpp server image; pin by digest for
  anything beyond local development;
- `FULLWORTH_AI_PORT` — loopback host port, default `8080`;
- `FULLWORTH_AI_CONTEXT_SIZE` — model context size, default `8192`.

Never commit or paste the API key into source, issue text, logs, or chat.

## Model location

Place the approved GGUF file under:

`.private/FullWorth.Models/`

The startup script rejects paths and accepts only a filename inside that
directory.

## Start the runtime

From a shell that has the required values already set:

```sh
sh deploy/start-local-ai-development.sh
```

The script:

1. validates the model filename;
2. validates the expected SHA-256 format;
3. verifies the on-disk model hash;
4. requires an API key of at least 32 characters;
5. checks Docker and Docker Compose v2;
6. starts only `compose.local-ai.yml`.

It does not print the API key or model contents.

## FullWorth API configuration

For a development API process on the same host, configure:

```text
StatementAi__Local__Enabled=true
StatementAi__Local__Endpoint=http://127.0.0.1:8080/v1/chat/completions
StatementAi__Local__Model=<approved model alias>
StatementAi__Local__ApiKey=<same local runtime key>

StatementAi__OpenAI__Enabled=false

StatementAi__Shadow__Enabled=true
StatementAi__Shadow__AllowProviderCalls=true
```

Do not enable both local AI and OpenAI. FullWorth rejects that ambiguous
configuration when the extractor is resolved.

Shadow-mode provider calls do not authorize AI-derived persistence.

## Verify runtime readiness

llama.cpp exposes a health endpoint. A ready local model should answer on the
loopback address after model loading finishes.

The FullWorth statement extractor uses only the authenticated
`/v1/chat/completions` route.

Do not use a successful llama.cpp health response as proof that statement
extraction is accurate. Accuracy still requires FullWorth's controlled corpus,
ground-truth scorer, deterministic candidate validator, and held-out evaluation.

## Stop the runtime

```sh
docker compose \
  --project-directory . \
  -f compose.local-ai.yml \
  down
```

Do not add `--volumes` to production-oriented cleanup commands. This
development profile currently has no persistent Docker volume, but FullWorth's
general production safety rule still applies.

## Before production use

This profile is not production approval.

Production activation requires at least:

- exact model artifact and image digest pinning;
- license/notice review;
- measured CPU/GPU memory, latency, and throughput;
- a private production network design with no public model port;
- authenticated API-to-model traffic;
- egress restrictions;
- bounded concurrency and timeouts;
- model outage fallback to deterministic behavior;
- held-out extraction quality and unsupported-claim thresholds;
- prompt-injection and malformed-output testing;
- cross-user isolation proof;
- no raw prompt/response logging;
- normal exact-head CI and guarded deployment evidence.

Until those gates pass, the local model remains a development/shadow evaluator.

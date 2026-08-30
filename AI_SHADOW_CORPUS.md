# Private AI shadow corpus

This repository intentionally contains no real provider statements, OCR text, account data, model output, or ground-truth financial facts.

Keep any corpus in an encrypted, access-controlled location outside the repository. If a temporary local corpus must exist beneath the repository during development, use only `.private/BillWatch.AiShadowCorpus/`; Git ignores that directory.

Each case uses a non-sensitive identifier containing only letters, digits, hyphens, and underscores. A future offline-only runner accepts only these fixed names beneath that case directory:

- `statement.txt` — extracted statement text
- `ground-truth.json` — reviewer-approved expected structured facts

Never put API keys, production connection strings, full account numbers, storage paths, or unredacted statements in Git, test fixtures, logs, issue trackers, or chat.

The scorer compares cases in memory and produces aggregate metrics only. A passing readiness gate is not permission to route AI-derived facts into statement persistence.

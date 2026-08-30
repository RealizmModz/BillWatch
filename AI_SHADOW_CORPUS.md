# Private AI shadow corpus

This repository intentionally contains no real provider statements, OCR text, account data, model output, or ground-truth financial facts.

Keep any corpus in an encrypted, access-controlled location outside the repository. If a temporary local corpus must exist beneath the repository during development, use only `.private/BillWatch.AiShadowCorpus/`; Git ignores that directory.

Each case uses a non-sensitive identifier containing only letters, digits, hyphens, and underscores. A future offline-only runner accepts only these fixed names beneath that case directory:

- `statement.txt` — extracted statement text
- `ground-truth.json` — reviewer-approved expected structured facts

The bounded loader rejects missing files, links/reparse points, oversized content, invalid text encoding, unknown JSON properties, invalid dates/currency/money, money beyond cent precision, more than 100 line items, and ground truth with no scored facts. Its errors do not include statement text, parser details, or physical corpus paths.

`ground-truth.json` uses this shape (fictional values only):

```json
{
  "providerKey": "provider-a",
  "totalAmount": 104.99,
  "billingPeriodStart": "2026-08-01",
  "billingPeriodEnd": "2026-08-31",
  "statementDate": null,
  "dueDate": null,
  "currencyCode": "USD",
  "lineItems": [
    {
      "description": "Internet service",
      "amount": 104.99,
      "category": "Service"
    }
  ]
}
```

Never put API keys, production connection strings, full account numbers, storage paths, or unredacted statements in Git, test fixtures, logs, issue trackers, or chat.

The scorer compares cases in memory and produces aggregate metrics only. A passing readiness gate is not permission to route AI-derived facts into statement persistence.

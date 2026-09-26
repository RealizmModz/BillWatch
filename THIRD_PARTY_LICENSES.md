# Third-Party Licensing Policy

FullWorth is proprietary software, but it depends on third-party software and
may use third-party model weights. Those components keep their own licenses.
The root `LICENSE` file applies only to FullWorth-owned material and does not
override third-party rights or obligations.

## Rules

Before a new dependency, model, dataset, font, image, runtime, or other
third-party component becomes part of a FullWorth release:

1. Record the exact component name and version or immutable artifact digest.
2. Record the authoritative upstream source.
3. Record the applicable license and any additional use terms.
4. Verify that commercial use, hosted use, modification, and redistribution are
   compatible with the way FullWorth will use the component.
5. Preserve required copyright and license notices.
6. Record any attribution, source-offer, NOTICE-file, model-card, acceptable-use,
   or redistribution requirements.
7. Reject dependencies whose license obligations cannot be satisfied without
   weakening FullWorth's security, privacy, ownership isolation, or business
   requirements.
8. Re-check the license when changing versions or model artifacts. Do not assume
   that a newer artifact has the same license.
9. Do not treat a package registry label, model filename, README badge, or
   previous version's license as sufficient evidence when authoritative license
   text is available.
10. Keep model weights and large third-party artifacts out of Git unless their
    license, size, provenance, and release process explicitly justify inclusion.

## AI model/runtime policy

FullWorth may use self-hosted open-weight models when their licenses permit the
intended commercial and hosted use.

Model prompts and financial evidence must remain inside FullWorth's authorized
server-side processing boundary unless a separately reviewed architecture
explicitly permits otherwise. A model's license does not grant it access to
data.

The model remains an untrusted candidate generator. FullWorth deterministic
code retains authority over authentication, ownership, security decisions,
financial arithmetic, evidence validation, persistence, comparisons, alert
thresholds, and other verifiable controls.

## Current AI candidates

The following are candidates under evaluation, not a declaration that they are
currently shipped or distributed by FullWorth:

| Component | Intended role | Upstream license observed during evaluation | Release requirement |
| --- | --- | --- | --- |
| Qwen3-14B | Self-hosted model candidate | Apache License 2.0 | Pin exact artifact/version and preserve Apache-required notices if distributed. Re-verify the exact model artifact before release. |
| llama.cpp | Local inference runtime candidate | MIT License | Preserve the MIT copyright/license notice when distributing covered software. Pin the runtime version or image digest. |

These entries must be updated if FullWorth chooses different artifacts.

## Existing application dependencies

FullWorth already uses third-party .NET, JavaScript, OCR, database, operating
system, and infrastructure components. This policy is not a claim that the
repository currently contains a complete third-party notice inventory.

Before broad commercial distribution of a client or bundled server artifact,
generate and review a complete dependency/license inventory for the exact
release and produce a user-visible or distributed notice bundle where required.

## Release gate

A production or distributed release that adds or changes a material third-party
component is not license-complete until:

- the exact artifact is identified;
- its license has been reviewed against the intended use;
- required notices are present;
- incompatible obligations are resolved; and
- the release evidence records the review.

Legal/license review complements technical CI. Passing CI alone is not proof of
license compliance.

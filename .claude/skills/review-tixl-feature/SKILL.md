---
name: review-tixl-feature
description: Review a TiXL feature or change set for elegance, naming, robustness (exceptions, null refs, initialization order, threading) and realtime performance (allocations and work in per-frame paths). Produces a ranked findings list with file:line, the concrete problem, and the proposed fix; applies fixes only when asked. Use when the user wants a feature reviewed, asks "is this well integrated", or invokes /review-tixl-feature.
---

# review-tixl-feature

The procedure is agent-neutral and lives in `.agentic/Reviews/FEATURE_REVIEW.md`. Read that file and
follow it exactly; this stub exists only so `/review-tixl-feature` resolves. Pass the argument
(feature name, file list, commit range, or nothing for the dirty working tree) through unchanged.

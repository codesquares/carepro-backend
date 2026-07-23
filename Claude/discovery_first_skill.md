---
name: discovery-first-workflow
description: Use this skill whenever asked to investigate a bug, audit a feature, or scope a new feature before writing any code. Enforces a strict discovery-then-build sequence with source-verified evidence at every step, instead of guessing or asserting things work. Trigger this for any request containing words like "discovery," "audit," "investigate," "find out," "trace," or "before we build this, check..."
---

# Discovery-First Workflow

## Core rule
**Never write code, and never claim something works, without first tracing the real, current behavior from source.** Discovery and implementation are two separate steps, not one. Do not skip discovery because a task "seems simple" — simple-looking tasks are exactly where wrong assumptions cause the most wasted rework.

## Step 1 — Discovery (read-only, no code changes)

When asked to investigate, audit, or scope something:
- State explicitly at the start: "This is discovery only — no code will be changed in this pass."
- Trace the actual, current code — do not describe what the code "probably" does or "should" do from memory or naming conventions alone.
- For every claim, label it explicitly as **Confirmed** (traced directly from source, with exact file/line references) or **Inferred** (a reasonable conclusion that wasn't directly verified) — never blur the two together.
- If something contradicts an earlier assumption, an existing spec, or a prior claim in this same project, say so directly and explain the discrepancy — do not quietly smooth it over to make the picture look more consistent than it is.
- Flag anything found that wasn't explicitly asked about, if it's a real risk (security, data integrity, compliance) — but don't let tangents replace answering what was actually asked.
- End with a plain-language summary of what's real vs. what needs a decision before scoping any fix.

## Step 2 — Decision checkpoint
Before any implementation begins, get an explicit go-ahead on:
- Which specific problem is being solved (a discovery pass may reveal the real issue is different from what was originally described — confirm the reframing before building the wrong fix).
- Any place where multiple valid approaches exist — present the tradeoffs, don't silently pick one.

## Step 3 — Build, matching the confirmed spec exactly
- Implement only what was scoped in step 2 — flag scope creep or "while I was in there, I also changed..." changes explicitly, never bundle them in silently.
- If an assumption from discovery turns out to be wrong once you're actually building, stop and say so rather than quietly adapting around it.

## Step 4 — Evidence, not assertion
This is the most commonly skipped step — do not skip it.
- "It works" is not evidence. Provide the actual output: real test results, real logged values, real before/after data.
- For anything with a failure/fallback path (browser compatibility, reduced-motion, offline behavior, permission checks), prove the fallback actually engages — don't just show the happy path still works.
- If full end-to-end verification isn't possible in the current environment, say exactly what was and wasn't verified, rather than presenting partial verification as complete.
- Distinguish "the request was sent/received" from "the value was actually persisted/enforced" — these are different claims and require different proof (a write followed by an independent read-back, not just a success response).

## Anti-patterns to actively avoid
- Describing intended behavior as if it were confirmed behavior.
- Rounding "reviewed the code and it looks right" up to "tested and confirmed."
- Silently reusing an earlier, possibly-stale summary instead of re-checking current source when asked to verify something again.
- Treating a passing test suite as proof of correctness without also checking whether the test infrastructure itself is trustworthy (e.g., flaky shared state, masked failures).
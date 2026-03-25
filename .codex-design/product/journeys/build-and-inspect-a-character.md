# Build and inspect a character

Status: available_today_with_preview_edges

## User goal

Create or modify a character in a workbench surface, inspect the result, and trust the math.

## Entry surfaces

* `chummer.run` download or preview entry into the workbench head
* `chummer6-ui` builder, inspector, and comparison surfaces

## Happy path

1. The user opens a build or inspector surface in the workbench head.
2. The UI consumes canonical engine contracts and the current runtime bundle instead of local copied rules truth.
3. Every mechanics-affecting change recalculates deterministically.
4. Inspectors and comparison views expose explain or receipt-bearing evidence for the result.
5. The user can save, compare, or continue refining the build without leaving workbench ownership.

## Failure modes

* If math or legality cannot be explained, the surface must show missing evidence and stop pretending the answer is settled.
* If a comparison flow would invent mechanics or legality, it must be blocked or clearly marked as non-canonical.
* If live-session or mobile-specific behavior leaks into the flow, the fix is a boundary correction, not more UI glue.

## Success evidence

* Explain pointers exist for promoted mechanics-affecting outputs.
* Workbench behavior stays out of the dedicated mobile play shell.
* Shared primitives come from `Chummer.Ui.Kit` rather than repo-local forks.

## Canonical owners

* `chummer6-ui`
* `chummer6-core`
* `chummer6-ui-kit`

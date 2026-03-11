# Lead-dev feedback: core boundary purification

Public audit status: `red/yellow`

Main issues:

* visible cross-boundary source leaks still exist
* README still narrates the old multi-head runtime story
* the repo root is still materially wider than the intended engine boundary

Required next steps:

1. Remove or quarantine `Chummer.Presentation.Contracts`, `Chummer.RunServices.Contracts`, and other non-engine authority surfaces.
2. Rewrite the README so the engine owns mechanics, reducer truth, runtime bundles, and explain canon only.
3. Continue shrinking legacy utility/app residue out of the active engine solution.

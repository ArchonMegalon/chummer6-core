# Designer Feedback Split: core-engine

Source: 2026-03-08 Chummer designer market scan. Distilled direction: community demand is ops-first, not AI-first. The product must reduce Shadowrun bookkeeping, reduce duplicate entry, support cross-device play, explain rules with provenance, preserve house-rule flexibility, and avoid hosted lock-in.

## Your part
core-engine must be the authoritative rules and explainability layer behind the session OS.

Prioritize:
- event/delta-safe mechanics and deterministic projections that support live session bookkeeping
- authoritative calculations for initiative/passes, ammo, condition monitors, wound modifiers, Edge, interrupts, Matrix/deck state, drones, spirits, vehicles, and similar volatile play-state mechanics
- Explain Everywhere traces for every derived number, with runtime fingerprint, pack/profile IDs, rule/evidence pointers, and machine-readable provenance
- house-rule and optional-rule support as first-class configuration, not ad hoc forks
- stable canonical identifiers and exchange-safe shapes that other surfaces can project without recomputing mechanics

Guardrails:
- do not move UI/session-ops concerns into core
- do not hide mechanics behind AI text or chat abstractions
- do not assume a single frozen canon; packs/profiles/house rules matter
- optimize for source-aware truth and reproducibility over presentation polish

Product implication:
If the player shell, GM ops board, or importers need a volatile stat during play, core should be able to compute it, explain it, and identify which content/profile produced it.

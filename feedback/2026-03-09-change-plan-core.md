# Chummer Core - Change Plan

Date: 2026-03-09

## Priority

- Support M0 contract canon around `Chummer.Engine.Contracts`.
- Define the engine mutation side of the canonical session model.
- Remove long-term leaks such as `Chummer.Presentation.Contracts` and `Chummer.RunServices.Contracts` after package cutover.
- Quarantine browser and legacy utility surfaces out of the active engine ownership path.

## Exit direction

Core should converge on engine/runtime/contracts/rulesets/tests only.

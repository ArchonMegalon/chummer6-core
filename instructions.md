# Codex Instructions — core engine

## Read first
1. instructions.md
2. .agent-memory.md
3. AGENT_MEMORY.md
4. chummer-core-engine.design.v2.md
5. AGENTS.md if present
6. audit.md if present

## Scope
Own:
- rulesets, engine logic, parsing, RulePack engine-side compilation
- runtime-lock inputs
- structured Explain API source data
- immutable character/workspace state
- engine-side tests

Do not own:
- UI frameworks
- ASP.NET controllers
- hub/auth/AI orchestration
- hosted persistence

## Hard boundaries
- No Avalonia / Blazor / Portal / Hub logic
- No cloud or LLM SDKs
- No direct dependencies on presentation or run-services implementation

## Quality rules
- Explanations must be localization-ready: keys/codes/parameters, not baked English prose
- Session state semantics must stay event/delta based
- Runtime and provider ordering must be deterministic and testable

## Queue
1. Isolation and compile recovery
2. Contract hardening
3. Structured Explain API hardening
4. Runtime/RulePack determinism hardening
5. Backend primitives for Build Lab / ledger / timeline / validation / explain hooks
6. Extract remaining hardcoded C# math into Lua script Packs.

## Execution style
Inspect current repo state first.
Do not repeat completed work.
Continue silently until the queue is exhausted or you are truly blocked.

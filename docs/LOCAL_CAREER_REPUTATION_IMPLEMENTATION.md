# Local SR5 Career reputation

This branch adds Core-owned arithmetic and exact profile policy for the normal
After Run/Career wizard. It does **not** yet add a workspace mutation service,
an Android input page, a package seal, or release authority. The existing grouped
After Run settlement remains a distinct, independently reviewed proposal flow.

## Canonical behavior

`CharacterCareerReputationRules` separates manual awards from effective totals.
It takes Core-resolved earned Career Karma, rounded active improvements, the
explicit Public Awareness policy and Erased state. These are not editable UI
defaults or a provider's rule claims.

- Street Cred uses earned Career Karma and the active divisor adjustment, then
  subtracts burnt Street Cred; only the effective Street Cred total is clamped
  to zero. Available spending Karma is not earned Career Karma.
- Notoriety includes its active improvement and subtracts integer burnt/2. Its
  effective total can be negative. Burning must not also decrement its manual
  award, which would double-charge the effect.
- Calculated Public Awareness is optional, determined by the saved profile.
  Erased caps positive effective awareness at one; it does not normalize negative
  awareness. Signed division follows the actual C# legacy implementation.
- Burning requires at least two effective Street Cred and changes only the burnt
  counter by two. The input record is immutable and the output is a preview.
- Manual signed adjustments apply only to selected fields. The resulting selected
  entries use the legacy control range0–100; this is **not** a limit on effective
  reputation. Unselected imported values are preserved, not silently clamped.
- Invalid edition/mode, a negative burnt counter, zero divisor, overflow, an
  invalid selected manual target, or a no-op cannot produce an applicable quote.

`TryResolveCareerReputationSettings` uses the existing captured source-input
authority. It requires exactly one explicit, strict profile Boolean and binds its
raw rule state to the selected profile and profile-input digest. Missing, nested,
attributed, duplicated, malformed or changed inputs fail closed. A retained
drifted context does not revive when the source changes back.

## Oracle inspected

Legacy checkout revision: `fe4355d06c98cd9b7feade89f5fc1a0e438f7ce3`.
The source files were read and their clean Git state checked. This is code-source
provenance, not a rulebook page anchor or a claim that legacy Windows executed.

| Source file | Inspected behavior | Complete-file SHA256 |
| --- | --- | --- |
| `Chummer/Backend/Characters/Character.cs` | Calculated/Total Street Cred, CanBurn, Notoriety, Public Awareness; lines31644–32160 | `ab744d6afedb25683459622a37da12fb12eac421c67661a421cfcc4c42ab9f9e` |
| `Chummer/Forms/Character Forms/CharacterCareer.cs` | `cmdBurnStreetCred_Click`, lines10027–10045 | `b1f58def07884877638e7c31a5af194a5ce8869c0020447154f827ba56e813ea` |
| `Chummer/Forms/Character Forms/CharacterCareer.Designer.cs` | Manual numeric bounds, lines20308–20473 | `70d1cf23177e9b06ed7d7dbc9ba355e966b9bf041457abf2c285dc7fcfc2ca07` |

## Remaining implementation — not optional release proof

1. Read exact saved Career Karma/award/burn fields and all relevant improvement
   semantics, including unique/precedence/custom groups and Erased, through Core.
   Do not substitute the partial Presentation projector or zeros for unresolved
   inputs. Astral/Wild reputation need their own source-enabled capabilities.
2. Bind quotes to workspace ID, current/saved revision, complete document and
   auxiliary state, active profile/custom-data/runtime inputs and the exact
   intent. A caller-created input vector or quote alone must not authorize saving.
3. Add an atomic Core persistence command/receipt with explicit confirmation,
   shared mutation ownership, CAS, durable result lookup, and replay/conflict
   recovery. Preserve unrelated XML, expenses, rewards, contacts and settlement
   receipts. Re-read authority before committing; no quote reserves source state.
4. Wire ordinary local native choices and explain before/after totals separately
   from manual deltas. Never manufacture GM actor IDs, approvals or a run proposal.
5. Prove real-file save/reopen/replay and Android process restart; then integrate
   by a deliberate reviewed package reseal/repin. The currently frozen Core PR50
   recipe, UI/Hub graph and Android candidate do not include this branch.

The dedicated arithmetic project consumes the actual Contracts project with its
analyzers enabled. Source-policy tests live in the existing resolver suite, also
compiled by `Chummer.CreationResources.Tests.csproj`. Both suites are local source
verification, not hosted package, Android runtime or publication evidence.

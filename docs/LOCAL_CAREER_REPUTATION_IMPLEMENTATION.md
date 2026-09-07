# Local SR5 Career reputation

This branch adds Core-owned arithmetic, saved-character projection and exact
profile policy for the normal After Run/Career wizard. It does **not** yet add a workspace mutation service,
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

## Saved-character projection

`CharacterCareerReputationProjector.TryRead` now composes the arithmetic from a
clean saved SR5 XML workspace (`workspace` or `sr5/chum5-xml`, schema1) and its
actual source resolver. It retains separate payload/envelope, auxiliary-state,
and raw-profile/settings digests plus workspace/revision identity. It reads the
profile twice and rejects observed drift; this is not a source reservation.

- Earned Career Karma is reconstructed from the expense log, rounding **each**
  included amount away from zero. Refunds and Nuyen are excluded. Negative
  amounts count only when explicitly forced career-visible. Available Karma and
  print/export totals are not used.
- Relevant improvements use enabled and add-to-rating numeric flags, the exact
  empty/`career` condition, and ordinal improved-name groups. Saved names and
  unique keys retain their exact whitespace; conditions are never trimmed into
  applicability. Normal and custom
  partitions stay separate. Ordinary values sum; unique names select the first
  highest row; normal `precedence0`, `precedence1`, and `precedence-1` preserve the
  pinned winner/stack/tie behavior. `customgroup` is a display group, not another
  numeric partition. Rounded bonuses are calculated **after** decimal aggregation.
- Erased uses selected contributor presence, including zero-valued effects, not
  a positive sum or any enabled row. The inspected legacy manager has an unusual
  contributor-cache behavior: a custom unique Erased row contributes presence
  only when its improved-name group has a normal partition. The Core projection
  preserves that observable behavior explicitly and tests it. Likewise, an
  all-`decimal.MinValue` unique group matches the legacy no-winner sentinel.
  These are source-compatibility facts, not claims about printed game rules.
- Row traces are read-only; zero-based row indexes are meaningful only within
  the bound complete payload. They are not globally unique action IDs or
  rulebook/page anchors.
- Required awards, expense/improvement containers, and relevant scalar values
  cannot silently disappear or be guessed. Duplicate, namespaced, nested,
  attributed, malformed or overflowing inputs fail closed. Unsupported expense
  kinds do not inherit the legacy loader's unsafe unknown-string-to-Karma coercion.
  Optional absent flags follow the inspected legacy constructor defaults; a
  malformed present flag never invokes a default. DTDs are prohibited. Input is
  bounded to64Mi characters and100,000 rows per container.

The projector reads the **saved** effect graph. It does not regenerate effects
from changed quality/gear definitions, validate other domains' receipt ledgers,
or bind the eventual mutation to an executable runtime build. Those checks and
the explicit intent binding still belong to the upcoming persistence owner.

## Oracle inspected

Legacy checkout revision: `fe4355d06c98cd9b7feade89f5fc1a0e438f7ce3`.
The source files were read and their clean Git state checked. This is code-source
provenance, not a rulebook page anchor or a claim that legacy Windows executed.

| Source file | Inspected behavior | Complete-file SHA256 |
| --- | --- | --- |
| `Chummer/Backend/Characters/Character.cs` | CareerKarma23758–23823; Calculated/Total reputation31644–32160; Erased43808 | `ab744d6afedb25683459622a37da12fb12eac421c67661a421cfcc4c42ab9f9e` |
| `Chummer/Forms/Character Forms/CharacterCareer.cs` | `cmdBurnStreetCred_Click`, lines10027–10045 | `b1f58def07884877638e7c31a5af194a5ce8869c0020447154f827ba56e813ea` |
| `Chummer/Forms/Character Forms/CharacterCareer.Designer.cs` | Manual numeric bounds, lines20308–20473 | `70d1cf23177e9b06ed7d7dbc9ba355e966b9bf041457abf2c285dc7fcfc2ca07` |
| `Chummer/Backend/Static/Managers/ImprovementManager.cs` | ValueOf defaults409–459; filter, partitions, precedence, contributor cache960–1390 | `0ba804cd4549ac2497e152a1f0aa2f32b17f38cc62da37585c1ecffe70988ffe` |
| `Chummer/Backend/Improvements/Improvement.cs` | Saved fields and loader defaults449–620; numeric AddToRating1038 | `cf62846db157b77476f298cfcb1e74adc97a18e664ba44236bf1c3ffe3df6be3` |
| `Chummer/Backend/Uniques/Expenses.cs` | Constructor/defaults, Save and Load235–367 | `5a8376ffb23f57f2206ca1d23493220b1c0efd4bd3ffdaf85506ca15de9738e8` |
| `Chummer/Backend/Static/Extensions/DecimalExtensions.cs` | StandardRound42–45, ceiling/floor away from zero | `b60e05f94606721cd4ef8087ef9d2ea9b3bda2aabd3371c8f699d0e083cd1ffd` |

## Remaining implementation — not optional release proof

1. Add separate source-enabled Astral/Wild reputation capabilities. The current
   saved projection covers Street Cred, Notoriety and Public Awareness; it does
   not declare those additional domains complete or remove them from the goal.
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

The dedicated arithmetic/projection project consumes the actual Contracts and
Application projects, with Contracts analyzers enabled. Its early-import SDK
properties isolate `obj/career-reputation` and `bin/career-reputation`: restoring
it cannot overwrite the assets graph of neighboring test projects. Source-policy
and real-file projection tests live in the existing resolver suite, also
compiled by `Chummer.CreationResources.Tests.csproj`. Both suites are local source
verification, not hosted package, Android runtime or publication evidence.

The composition test uses the actual file store and existing atomic After Run
reward service: a30-Karma award produces130available Karma but only30earned
Career Karma, then4Street Cred. A burn quote shows2Street Cred,1Notoriety and
2Public Awareness without applying it. Reconstructing the file store and
replaying the reward preserves the saved document, revision and single receipt.
This proves the reward-to-reputation read/quote seam, **not** reputation saving or
an Android user/device journey.

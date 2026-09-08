# Priority talent grants in the Skills wizard

Core now carries confirmed Priority active-skill and skill-group grants into the
Skills snapshot before a Skills draft has been saved. The plan is validated by
the prerequisite lane and joined to the current catalog by typed identity,
canonical name, group membership and effective source digest.

`Rating` and `EffectiveRating` include the free starting rating. `GrantedRating`
identifies that source-bound minimum; it is not an extra pool of spendable
points. Only the increase above it consumes active or group Priority points.
Specializations retain their separate cost. Omitting a granted row from a
request removes only purchased increases, not the underlying grant. Explicit
ratings below the grant are invalid. Group-breaking and maximum-rating rules
remain enforced.

The compatibility oracle is Chummer5a `SelectMetatypePriority.AddFreeSkills`,
which creates Heritage `SkillBase` or `SkillGroupBase` improvements, and
`Skill.Base`/`SkillGroup.Base`, which distinguish paid base points from free
base. This is code-oracle evidence, not a rulebook-page citation.

The new projection property is omitted from JSON when zero, preserving the
existing non-grant payload shape. Changed/granted payloads have new digests.
The normal preview, explicit confirmation, atomic save and receipt recovery
rules are unchanged. A rehashed forged granted rating is not source authority.
Character XML is not changed while confirming a Skills draft.

Returning to Skills after saving another wizard step must remain editable.
Skills receipts form a contiguous **Skills draft/hash chain**, not a contiguous
global workspace-revision chain: a Magic save can legitimately sit between two
Skills saves. Every individual receipt still advances exactly one workspace
revision, the next Skills receipt cannot overlap an earlier one, and the atomic
store checks the current workspace revision and auxiliary digest at commit.
Cold reopen and retry preserve both domains' receipts; retrying an older command
returns its historical receipt without restoring an older draft or writing again.

## Selection-only groups

Canonical SR5 Priority D Aspected Magician requires one choice from Conjuring,
Enchanting or Sorcery, but its source `skillgroupval` is zero. The existing
Priority group prompt must collect that choice; it must not silently select an
aspect or add a second independent choice in the Magic wizard.

The zero-rated entry remains in the validated Priority plan and determines the
source quality's `unlockskills` choice at finalization. It creates neither an
initial Skills row nor a Heritage `SkillGroupBase` improvement. Purchasing
levels later costs the ordinary group points. Negative values, duplicate
choices and rehashed ratings inconsistent with the raw source remain invalid.
This admission does not enable zero-rated active-skill grants.

Native tests distinguish cold Priority restoration before Attributes from the
intentional dependent-draft edit lock afterward. Both must preserve the saved
choice. These managed tests do not establish Activity/process-death authority.

## Verification and remaining integration

The Skills service now binds a separate `TalentAccessV1` policy to the validated
Priority draft. It resolves the selected Talent's effective quality definitions
from the same source-data context, through the shared Quality/Talent source join.
`unlockskills` effects, including the explicit Aspected group choice, determine
available active skills and groups. Core's compatibility oracle is
`SkillsSection.SkillFilter`: ordinary skills exclude Magical Active and
Resonance Active; ungrouped magical skills remain available to the relevant
source filters. Group membership stays canonical while unavailable members do
not break a group. Unknown or unresolved unlock semantics do not grant access.

The catalog is not destructively filtered. Android displays Core's permitted
IDs, rejects hidden entries in its draft, and still requests a Core preview for
each adjustment. No Android Talent-name or skill-category rules are duplicated.
Core rejects unauthorized purchases both at preview and confirmation and
revalidates persisted drafts before finalization. Rehashing a policy's allowed
IDs, source page, prerequisite binding or schema does not authorize a change.

The new policy has its own version and digest inside the Skills authority.
The unchanged point-cost runtime and null-policy JSON shape remain stable for
ordinary runners; no irrelevant field is added to their saved authority.
Missing policy never means unrestricted special-skill access. Previously saved
awakened drafts without the new source binding require explicit re-review; they
are not automatically migrated or rewritten.

Actual canonical Human/Priority-B Magician, Aspected Magician, Adept,
Mystic Adept and Technomancer tests run
Bootstrap, Prerequisites, Attributes, Skills review/confirm, cold file-store
reopen and exact command recovery. They also cover omission, undershooting the
free minimum, rehashed free-rating/cost forgery and malformed projection arrays.
The native consumer must seed
its draft from the Core snapshot rather than requiring a previously saved
Skills draft. Native packet, source-composition and device evidence are
separate requirements.

Whole-character finalization now projects the supported, source-bound Talent
qualities and grants atomically, with paid base values separate from Heritage
improvements. Actual-source tests also cover all three Priority D Aspected
choices, zero free levels, explicit final confirmation, cold saved character
effects and replay. Source-owned skill purchase access now has actual-source
positive, negative, cold-reopen and hostile-packet coverage. This is not
exhaustive awakened legality or device proof: separate MAGAdept allocation,
other Talent variants, additional purchased-quality unlocks and explicit
upstream re-review remain separate work.

## Explicit pre-policy re-review

`ICharacterCreationSkillsReReviewService` is a separate Core capability for
saved drafts created before `TalentAccessV1`, with the same known catalog,
point-cost runtime, prerequisites and attributes. It does not grant ordinary
Preview/Confirm a bypass for `DraftInvalid`.

`LoadReReview` verifies the historical draft/hash chain and reconstructs its
known pre-policy projection. Historical costs, rows, identities and source
anchors must match; a rehashed forgery is not accepted as old authority. The
original blocked Skills snapshot and historical draft remain distinct from the
new current-rule preview. This read never writes or silently clears choices.

`PreviewReReview` accepts proposed choices and returns a current-rule preview
plus an explicit comparison of ratings, costs, specializations, removals and
blockers. Intermediate corrections may remain blocked. For example, removing
one of two unavailable skills does not make the other skill legal. The native
re-review editor must retain such intermediate proposals without enabling Apply.

`ConfirmReReview` requires both explicit confirmation and explicit review of
the changes. Its preview/command identity binds the old draft, old ledger head,
current source and exact workspace/auxiliary revisions. It appends one current
Skills draft and receipt using the existing atomic commit; old receipts are
unchanged. Old ordinary commands and the new re-review command remain replayable
without another write. The distinct command identity prevents normal confirmation
from masquerading as re-review. An intervening Magic save requires a fresh review.

Current tests use canonical source services and deliberately constructed
pre-policy serialized fixtures, not an exported historical Play installation.
They cover legal and obsolete choices, partial corrections, malformed/rehashed
history, stale bindings, explicit review, post-replacement failure, cold reopen
and historical/current receipt recovery.

Android comparison/removal UI and actual upgrade/device proof are still pending.
Older drafts from before grant integration, arbitrary source changes and
upstream prerequisite/attribute revisions are not migrated by this capability.
They remain fail-closed until their own explicit review path exists.

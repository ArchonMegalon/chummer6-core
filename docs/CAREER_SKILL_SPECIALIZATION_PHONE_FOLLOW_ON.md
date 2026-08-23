# Career skill specialization phone follow-on

Status: Core authority contract only. This document does not claim Presentation, Android, or API-36 completion.

## Authority now owned by Core

`CharacterCareerSkillSpecializationRules` is the only commit authority for the next phone slice. A consumer must:

1. identify the saved parent skill with `CharacterCareerSkillIdentity` (`Active` requires a source GUID; custom `Knowledge` uses a null source GUID);
2. resolve `CharacterCareerSkillSpecializationSettings` and non-custom source options through the runner's exact `ICharacterSourceDataContext`;
3. merge any live `SkillSpecializationOption` improvements as typed `Improvement` options with stable provenance identities;
4. pass the complete saved skill XML, effective source state, settings/improvement state, eligibility flags, category blockers, rating, Karma, and group membership to `TryCreateQuote`;
5. show the quote and require a separate explicit confirmation; and
6. call `TryPlanAdd` with the unchanged character, source, rules, and logical revisions, then apply the whole plan under the workspace content-revision compare-and-swap.

The atomic write appends exactly one `<spec>` with the planned GUID, name, `<free>False</free>`, and `<expertise>False</expertise>` to the selected saved skill; writes the planned character Karma; adds one Karma expense; saves/checkpoints once; and refreshes the workspace. Any re-read mismatch, duplicate request, partial write, save failure, or receipt-recovery mismatch means zero committed writes.

## Chummer5 semantics preserved

- Active and knowledge specializations use their separate profile costs.
- `SkillCategorySpecializationKarmaCost` additions and multipliers apply only at or above their minimum rating and use Chummer's away-from-zero rounding order.
- Disabled, exotic, Karma-locked, zero-rating, skill-blocked, and category-blocked skills cannot add a specialization.
- Knowledge skills additionally honor `AllowUpgrade` and native-language restrictions.
- Chummer5 has no Career specialization-count ceiling; existing count is revision-bound, not treated as a mechanical maximum.
- A specialization is not blocked by a populated skill group. When `SpecializationsBreakSkillGroups` is enabled and the group has multiple enabled members, the quote and plan explicitly project that the group becomes broken.
- Expense text is `Learned Specialization {skill} ({specialization})`; amount is negative Karma cost; undo kind is `AddSpecialization`; undo object identity is the new specialization GUID.

## Deferred Presentation and Android contract

The follow-on UI must be a phone Career transaction route, not a generic XML editor:

- list the stable parent skill identity, exact selection origin (`SourceCatalog`, `CombatWeapon`, `Improvement`, or explicit `Custom`), rating, Karma cost/balance, group-break consequence, source anchors, and blocker;
- permit questions through Build Ghost without treating conversation as confirmation;
- use quote -> review -> explicit confirm; Back/cancel performs no mutation;
- on stale character/source/rules/logical revision, discard the plan and re-quote rather than retrying a stale write;
- after confirmation, require exactly one content-revision advance, durable save, clean dirty state, same-session reopen, then two real force-stop/new-process reopen checks; and
- bind the receipt, signed APK, immutable dependency graph, fixture, final XML, expense/undo identity, and every process PID to one API-36 proof digest.

Tablet composition remains deferred and must not be inferred from the phone contract.

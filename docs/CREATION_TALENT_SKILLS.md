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

## Verification and remaining integration

Actual canonical Human/Priority-B Magician, Aspected Magician, Adept,
Mystic Adept and Technomancer tests run
Bootstrap, Prerequisites, Attributes, Skills review/confirm, cold file-store
reopen and exact command recovery. They also cover omission, undershooting the
free minimum, rehashed free-rating/cost forgery and malformed projection arrays.
The native consumer must seed
its draft from the Core snapshot rather than requiring a previously saved
Skills draft. Native packet, source-composition and device evidence are
separate requirements.

Whole-character awakened finalization is **still incomplete**. The finalizer
still blocks talent-grant and awakened-effect projection. It must eventually
write paid base values separately from their Heritage improvements, not write
the total rating and then add the grant again. Magic/talent-quality/power,
spell and tradition effects must all join the same atomic finalization plan.
No finalizer gate was removed by this change.

Historical Skills drafts created before grant integration may no longer match
the current projection. They fail closed rather than silently changing cost or
rewriting a receipt. An explicit upstream-change/re-review migration that
preserves the old receipt history remains to be implemented.

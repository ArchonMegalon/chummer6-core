using Chummer.Contracts.Characters;
using Chummer.Contracts.Content;

namespace Chummer.Application.Characters;

public static class Chummer5aLegacyImportCompatibility
{
    // Pins current retained-input interpretation, NOT equivalence with all Chummer5a effects.
    public const string ProfileV1 = "chummer5a-sr5-retained-import-v1";
    // Explicit opt-in; historical import and current-semantics V1 remain immutable.
    // Bare "chummer5a-sr5-retained-import-v2" was never a defined profile and remains unsupported.
    public const string ProfileV2 = "chummer5a-sr5-retained-import-v2-deferred-ancestors";
    public const string ResolverSemanticsV2 = "chummer-current-selected-xml-retained-v2-deferred-ancestors";
    public const string DefinitionV2 = "chummer5a-sr5-retained-import-v2-deferred-ancestors|v1-immutable|current-semantics-v1-excluded|current-canonical-deferred-unfiltered-ancestors|materialize-only-on-append-or-addifnotfound|addnode-does-not-materialize-ancestors|reselect-each-descendant|filtered-missing-ancestors-unsupported|unknown-operation-ambiguous-target-xpath-blocked|original-bytes-order-unchanged|no-consumer-coverage-grant";
    public const string FingerprintV2 = "sha256:d82b150c08679f81d4ff32f074f91c9db287659a7d3878014e83ab53b93bd17f";
    public static bool IsSupportedProfile(string? profile) => TryGetInterpretation(profile, out _, out _);

    // V1 predates an explicit definition-fingerprint field. Its null value is part
    // of the original private wire identity; never add a field to V1 archives.
    public static bool TryGetInterpretation(string? profile, out string resolverSemantics,
        out string? definitionFingerprint)
    {
        resolverSemantics = string.Empty;
        definitionFingerprint = null;
        switch (profile)
        {
            case ProfileV1:
                resolverSemantics = ResolverSemanticsV1;
                return true;
            case ProfileV2:
                resolverSemantics = ResolverSemanticsV2;
                definitionFingerprint = FingerprintV2;
                return true;
            default:
                return false;
        }
    }
    public const string RulesetId = "sr5";
    public const string ResolverSemanticsV1 = "chummer-current-selected-xml-retained-v1";
    public const string ResolverBaselineSha256 = "c6b4e4b875d354e71d6fe043bf7383ef494d836d21916142084c258b9b885db5";
    public const string SourceOrderV1 = "selected-directory/override-custom-amend/filename-ignorecase-path-ordinal-v1";
    public const string XmlManagerSha256 = "9091d8068ac61af138028713917ec3adad740728d42c532149de4d34f6ef72ac";
    public const string ManifestParserSha256 = "52ba8b9efedee687bde0ae8cfa6f33af097f5d00bdc4d103584328352b656392";
}

/// <summary>A separately selected, bounded source interpretation; never an activation or coverage grant.</summary>
public static class CurrentCharacterRuleSourceSemantics
{
    // A separate interpretation for native Priority Career graphs that V7
    // could not consume. Old profiles and imported selection authority stay fixed.
    public const string ProfileV10 = "chummer-sr5-current-source-semantics-v10-native-priority-career";
    public const string DefinitionV10 = "chummer-sr5-current-source-semantics-v10-native-priority-career|v1-v9-immutable|previously-unavailable-marked-priority-career-effects-only|exact-native-canonical-profile|confirmed-native-prerequisite-and-finalization-history|no-imported-proof-fallback|retained-source-replay|complete-sheet-owner-effect-reconciliation|fourteen-current-domains|source-bound-magic-skill-and-attribute-quotes|unknown-missing-extra-effects-refused|owner-revision-digest-no-replay-unchanged";
    public const string FingerprintV10 = "sha256:5a0e72daf213165860d2866dc953b1ad454e1ab0528fd768e53a5a2e11cbeaab";
    public static bool IsNativePriorityCareerProfile9569(string? profile, string? fingerprint)
        => profile == ProfileV10 && fingerprint == FingerprintV10;

    public static bool IsOwnedPriorityCareerProfile9569(string? profile, string? fingerprint)
        => IsImportedPriorityCareerProfile5667(profile, fingerprint)
            || IsNativePriorityCareerProfile9569(profile, fingerprint);

    // A bounded successor for previously unsupported, unmarked full-profile
    // Priority Career imports. Selection is not domain or adoption authority.
    public const string ProfileV9 = "chummer-sr5-current-source-semantics-v9-imported-priority-career";
    public const string DefinitionV9 = "chummer-sr5-current-source-semantics-v9-imported-priority-career|v1-v2-v3-v4-v5-v6-v7-v8-immutable|unmarked-career-priority-exact-retained-guid-or-typed-standalone-file-settings-authority|standalone-original-empty-guid-name-namespace-filename-byte-digest|standalone-typed-policy-identity-roundtrip-and-consumer-binding|no-synthetic-preset-id|standalone-v9-only-no-historical-or-completion-execution|ordered-custom-settings-source-digests-and-complete-effective-row|full-profile-gameplay-label-source-shape|v7-typed-priority-policy-and-standard-only-fields|canonical-persisted-attribute-ranges-and-augmented-alias|source-reconciled-populated-stock-firearms-armor-native-kernel|item-damage-separate-from-racial-armor-pool|original-stock-capacity-encumbrance-and-unarmed-target-proof|loaded-stock-clip-custody-same-private-sheet-context-in-scope|typed-other-scalars-defaulting-and-skill-cost-domains-separate-from-learned-ranged-shot|actual-play-preview-confirm-journal-readback-original-operation-recovery|unsupported-attack-effects-remain-explicit|retained-sheet-owner-effects-and-career-attribute-kernel|unique-retained-metatype-name-or-consistent-id-including-career-lifestyles|shared-evaluation-owner-closure-profile-metatypes-skills-purchases-loadout-native-policy-attributes|complete-skill-cost-projection-and-unsupported-price-refusal|explicit-imported-saved-priority-talent-rank-and-skill-group-choice-proof-no-rating-or-effect-inference|v9-career-quality-serializer-notoriety-category-defaulting-and-source-costs|mandatory-retained-sheet-policy-execution|v6-stock-lifestyles-without-v7-mechanics-or-prompts|missing-creation-choices-remain-pending|fourteen-domain-owner-source-capture-adoption-fences-unchanged|opaque-character-and-amend-bytes-retained|no-native-method-or-native-grant-source-capability|one-evaluation-owner-sheet-optional-imported-grant-proof|source-derived-mag-res-ranges-grants-unlocks-and-modifiers-to-current-kernels|lease-bound-imported-context-to-session-casting-drain-sustaining-wounds-and-matrix|native-and-xml-only-factories-unchanged|fresh-preview-write-and-readmission-fences-unchanged|retained-owner-leased-spell-ritual-form-learning-existing-kernels-typed-confirm-receipt-and-fourteen-domain-after-readmission|unproved-power-grants-initiation-submersion-and-spirits-remain-required-refusals|source-declarations-defer-imported-racial-effects-to-exhaustive-owner-compiler|retained-quality-and-native-mentor-selected-free-power-clauses-shared-career-kernel-paid-free-sheet-proof-no-write-capability|owner-leased-play-ammunition-settlement-canonical-revision-checkpoint-original-operation-event-digests-preserved-complete-source-closure-export|versioned-retained-grade-sources-nonzero-current-read-existing-history-caps-metamagic-kernel-ordinary-echo-proof-no-purchase-authority|grade5722=store-leased-current-magic-and-initiation-submersion;imported-priority-MAG-RES-proof;typed-atomic-before-after-readmission;original-operation-readonly-recovery;no-imported-purchase-inference|retained-source-proven-spirit-sprite-selectors-and-fetter-pet-unfetter-existing-core-kernels-owner-leased-confirm-atomic-readmission-original-operation-read-only-recovery|free-sprite5732=selected-source-category-and-denial-owner-effects-existing-core-zero-cost-transition;store-owner-lease-preview-confirm-atomic-before-after-readmission;canonical-receipt-original-operation-readonly-recovery;unsupported-force-sources-remain-refused|sprite-force5751=retained-critter-catalog-selected-without-fallback;unique-source-affine-level-witness-all-native-bounds-generated-skill-power-closure;core-arithmetic-RES-no-priority-inference;source-EDG-free-sprite-budget;missing-effective-Denial-refused";
    public const string FingerprintV9 = "sha256:8fe4ac91532e070e95dcbde98a5d62b071626785521117bbc978b2d6e0015841";
    public static bool IsImportedPriorityCareerProfile5667(string? profile, string? fingerprint)
        => profile == ProfileV9 && fingerprint == FingerprintV9;

    // Bounded successor of V3 only. V4-V7 lifestyle capabilities and Creation
    // retain their existing interpretation; this is not a legacy XML profile.
    public const string ProfileV8 = "chummer-sr5-current-source-semantics-v8-career-condition";
    public const string DefinitionV8 = "chummer-sr5-current-source-semantics-v8-career-condition|v1-v2-v3-v4-v5-v6-v7-immutable|v3-native-career-successor-only|retained-source-shape-selection|application-vehicle-policy-v1-shared-interpreter|invalid-explicit-policy-unavailable-no-default|rea-pilot-capability-separate|metatype-conditionmonitor-overflow-signed-integer-minus100-to100|source-compiled-metatype-owner-exact-saved-match|zero-source-effect-retained|complete-character-source-owner-revision-readback-no-replay-unchanged|no-creation-or-lifestyle-capability-expansion";
    public const string FingerprintV8 = "sha256:605cdc60e1beb84cded7899ebe8253125caa59feedaab6e8e41463daba859b3e";
    public static bool IsCareerConditionProfile5497(string? profile, string? fingerprint)
        => profile == ProfileV8 && fingerprint == FingerprintV8;

    public const string ProfileV7 = "chummer-sr5-current-source-semantics-v7";
    public const string DefinitionV7 = "chummer-sr5-current-source-semantics-v7|v1-v2-v3-v4-v5-v6-immutable|explicit-character-capability|source-compiled-lifestyle-predicates|selected-character-and-lifestyle-facts|source-backed-editable-prompt-provenance|quality-aspect-deltas|unknown-minima-refused|retained-byte-recompile|owner-revision-digest-no-replay-unchanged|native-chummer-starting-base-lp-v1|explicit-selected-lifestyle-amend-v1|source-exact-career-lifestyle-mode-v1|v7-career-saved-source-reader|historical-lifestyle-authority-frozen|explicit-new-karma-profile-and-character-quality-text-v1|current-quality-catalog-exact-prompt-source-reprojection|retained-live-capability-parity";
    public const string FingerprintV7 = "sha256:50dec904a11f8e1c941ef520cac0d592038039ba04b48205d65f7f432ffc281d";
    public const string ProfileV6 = "chummer-sr5-current-source-semantics-v6";
    public const string DefinitionV6 = "chummer-sr5-current-source-semantics-v6|v1-v2-v3-v4-v5-immutable|source-bound-complete-lifestyle-catalog|hidden-row-accounting|exact-native-base-quality-location-aspect-grid-projection|bounded-constant-decimal-prices|unsupported-constraints-and-aspect-adjustments-retained-unavailable|selected-unavailable-refused|source-shape-capability-selection|ordered-retained-inputs|owner-revision-digest-no-replay-unchanged|no-ambient-fallback";
    public const string FingerprintV6 = "sha256:6905d7eae273ffb35c091ad16df670642a30062f49926a72de30284fa13eccfc";
    public const string ProfileV5 = "chummer-sr5-current-source-semantics-v5";
    public const string DefinitionV5 = "chummer-sr5-current-source-semantics-v5|v1-v2-v3-v4-immutable|native-method-dispatch|source-bound-lifestyle-quality-availability|exact-native-blocker-projection|blocked-unselected-rows-retained|eligible-supported-future-quotes|selected-disabled-or-unsupported-refused|unmodelled-source-shapes-blocked|ordered-retained-inputs|owner-revision-digest-no-replay-unchanged|no-ambient-fallback";
    public const string FingerprintV5 = "sha256:91bf6c5660c5c8358a2a4d44bc931c841ab044ca9797de232321e237f67e5b4c";

    public const string ProfileV4 = "chummer-sr5-current-source-semantics-v4";
    public const string DefinitionV4 = "chummer-sr5-current-source-semantics-v4|v1-v2-v3-immutable|native-method-v3-dispatch|nonempty-scalar-lifestyle-quality-catalog|exact-source-option-projection|canonical-standard-quality-future-quotes|unknown-quality-mechanics-blocked|ordered-retained-inputs|owner-revision-digest-no-replay-unchanged|no-ambient-fallback";
    public const string FingerprintV4 = "sha256:29839e3f416a898f3da5f187ab39c9898b6fb8f9f57ea72cfa5648f8b72ba254";

    public static bool IsNativeExecutionProfile(string? profile, string? fingerprint)
        => profile == ProfileV3 && fingerprint == FingerprintV3
            || profile == ProfileV4 && fingerprint == FingerprintV4
            || profile == ProfileV5 && fingerprint == FingerprintV5
            || profile == ProfileV6 && fingerprint == FingerprintV6
            || profile == ProfileV7 && fingerprint == FingerprintV7
            || IsCareerConditionProfile5497(profile, fingerprint)
            || IsNativePriorityCareerProfile9569(profile, fingerprint);

    public const string ProfileV3 = "chummer-sr5-current-source-semantics-v3";
    public const string DefinitionV3 = "chummer-sr5-current-source-semantics-v3|v1-v2-immutable|native-method-current-source-profile|partial-karma-ledger-open|life-modules-selected-source-effects|career-current-source-quotes|ordered-retained-inputs|unaccounted-selected-mechanics-blocked|no-complete-request|no-ambient-fallback";
    public const string FingerprintV3 = "sha256:d0362543310ad8429d5cf5fdcdfec65e55a29af20469ce95971329017557e079";

    public static bool IsCurrentExecutionProfile(string? profile, string? fingerprint)
        => profile == ProfileV2 && fingerprint == FingerprintV2
            || IsNativeExecutionProfile(profile, fingerprint)
            || IsImportedPriorityCareerProfile5667(profile, fingerprint);

    public const string ProfileV2 = "chummer-sr5-current-source-semantics-v2";
    public const string DefinitionV2 = "chummer-sr5-current-source-semantics-v2|v1-immutable|retained-original-current-xml-and-aux|fourteen-domain-catalog-and-current-consumption|ordered-merger-entry-and-node-observations|canonical-future-quote-grammar|unknown-global-blocked|no-complete-request|no-ambient-fallback";
    public const string FingerprintV2 = "sha256:d69842bf823ee9fbe301935c9b1e3840265f5abebad936b7a89fd754f23df05e";

    public const string ProfileV1 = "chummer-sr5-current-source-semantics-v1";
    public const string DefinitionV1 = "chummer-sr5-current-source-semantics-v1|retained-inputs-unchanged|selected-directory/override-custom-amend/filename-ignorecase-path-ordinal-v1|id-and-isidnode|scalar-recurse-defaulting|missing-entry-replace-requires-addifnotfound|missing-child-remove-is-noop|override-preserve-outer|missing-recursive-parent-unsupported|whole-addnode-unsupported|whole-append-unsupported|other-branches-unverified|consumer-coverage-not-granted";
    public const string FingerprintV1 = "sha256:afb8989e3f9825eb414eaf19f843cb3566bb6c87747e38a940e422f33cbac3ae";
}

// This reader exposes only independently recompiled source availability. Saved owners,
// choices and prices still require their normal transaction/current-catalog consumers.
public interface ICurrentKarmaQualityPromptSource2742
{
    bool HasKarmaQualityPrompts2742 { get; }
    bool TryReadKarmaQualityPromptCatalog2742(out CharacterCreationKarmaQualitiesCatalog? catalog);
}

// Source-shape inspection only. It grants neither admission nor mutation authority.
public interface ICurrentLifestyleSourceCapabilities2678
{
    bool RequiresStockLifestyles(CharacterCurrentRuleEnvironmentContext current,
        CancellationToken cancellationToken);
}

public interface ICurrentLifestyleMechanicsSource2684
{
    bool HasLifestyleMechanics2684 { get; }
    bool TryReadLifestylePromptSource2684(string fileName, out string xml, out string digest);
    bool TryReadLifestyleCharacterFacts2684(out CharacterCreationLifestyleMechanicsContext context);
}

// Explicit capability: historical source contexts never infer Career mode from saved flags.
public interface ICharacterLifestyleSourceMode2719
{
    bool TryResolveLifestylesAuthority(CharacterLifestyleSourceMode mode, out CharacterCreationLifestylesAuthority authority);
}

public interface ICurrentLifestyleSettingsSource2678
{
    bool TryReadLifestyleSettings(out string xml);
    bool TryReadLifestyleSource(out string xml, out string digest);
    // Source-selection fact, not a caller-supplied authority or digest. Other
    // sources retain the existing whitespace-preserving verifier by default.
    bool TryReadLifestyleLocationRoundTrip2678(out bool roundTrip)
    {
        roundTrip = false;
        return true;
    }
}

/// <summary>
/// Engine-local retention only. The exported bytes contain private paths, manifests,
/// raw rule XML and character inputs. Never send them to prompts, logs or public receipts.
/// Integrity is not authorization, source coverage, or a substitute for live re-admission.
/// </summary>
public abstract class CharacterCreationRuleSourceCapture
{
    public abstract string IntegrityDigest { get; }
    public abstract string EffectivePolicyDigest { get; }

    // Private inspection metadata, not a runtime lock, activation receipt or copy permission.
    // Null preserves historical captures: absence must never imply verified compatibility.
    public virtual string? CompatibilityProfile => null;
    public virtual string CompatibilityStatus => "legacy-unverified";
    public virtual string? RequiredPackageSetDigest => null;
    public virtual IReadOnlyList<ArtifactVersionReference> RequiredRulePacks => [];
    public virtual IReadOnlyList<RulePackAssetDescriptor> RequiredAssets => [];
    // Grouped by selected directory, then catalog query, then override/custom/amend phase.
    // Mode includes the catalog query; this is current resolver order, not historical desktop order.
    public virtual IReadOnlyList<RulePackAssetDescriptor> OrderedCustomSources => [];
    public virtual IReadOnlyList<RulePackResolutionDiagnostic> CompatibilityDiagnostics => [];

    public virtual Chummer.Contracts.Characters.CharacterCreationSourcebookSelectionAuthority? SourcebookSelection => null;

    /// <summary>Returns a detached private archive; mutating it cannot change this capture.</summary>
    public abstract byte[] ExportPrivateInputs();

    // Engine-internal observation only, never an authority or a cached digest.
    // Other capture implementations retain the exact fresh detached export.
    // The trusted Infrastructure friend may hash its owned private bytes without
    // allocating another complete archive; public callers cannot opt into it.
    internal virtual string ComputePrivateInputsDigest() => "sha256:" + Convert.ToHexStringLower(
        System.Security.Cryptography.SHA256.HashData(ExportPrivateInputs()));

    /// <summary>Creates a fresh resolver with no ambient filesystem fallback.</summary>
    public abstract ICharacterSourceDataResolver CreateReplayResolver();

    // Opt-in capabilities. Historical capture implementations cannot claim execution.
    public virtual bool MatchesOriginalCharacter(string characterXml) => false;
    // A fresh retained candidate only; admission still requires every canonical domain.
    public virtual CharacterCreationRuleSourceResolution? CreateCurrentEnvironmentResolution(
        string semanticsProfile, string semanticsFingerprint) => null;

    /// <summary>Observes changed current XML using only the same retained source closure.
    /// This capture observation is neither an execution nor a mutation permission.</summary>
    public virtual CharacterCreationRuleSourceCapture? RecaptureCurrentEnvironment(string characterXml,
        string semanticsProfile, string semanticsFingerprint, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return null;
    }

    public virtual CharacterCreationRuleSourceResolution? CreateExecutionResolution() => null;
    public virtual CharacterCreationRuleSourceResolution? CreateExecutionResolution(string? schema)
        => schema is null or CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV1
            or CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV2
            or CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV3 ? CreateExecutionResolution() : null;
}

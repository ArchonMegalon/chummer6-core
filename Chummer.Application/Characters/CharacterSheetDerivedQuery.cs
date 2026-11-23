using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using Chummer.Contracts.Characters;
using Chummer.Application.Workspaces;

namespace Chummer.Application.Characters;

/// <summary>
/// Bounded SR5 read authority. Resolves settings, metatype, skills and owned source/effect
/// graphs against one captured payload. Saved totalvalue/totaless are not authority.
/// No store access, payload mutation, creation completion claim or SR6 rules.
/// </summary>
public static class CharacterSheetDerivedQuery
{
    private static readonly string[] AttributeNames = ["BOD", "AGI", "REA", "STR", "CHA", "INT", "LOG", "WIL"];
    private const string MissingSource = "The selected source profile or metatype could not be resolved.";
    private const string UnsupportedGraph = "This character contains effects or equipment that the sheet read authority cannot yet resolve.";

    public static CharacterSheetDerivedSection Parse(string xml, ICharacterSourceDataResolver? resolver)
        => ParseCore2912(xml, resolver, false, out _);

    // XML-only callers cannot reconstruct a confirmed Priority selection. An
    // observed workspace supplies history; the compiler still revalidates source
    // and every effect. This read grants no mutation or completion capability.
    public static CharacterSheetDerivedSection Parse(WorkspaceStoredDocument observation, ICharacterSourceDataResolver? resolver, ICharacterCreationRuleSourceRecovery? recovery = null)
        => ParseCore2912(observation.Document.Content, resolver, false, out _,
            CharacterCreationPriorityGrantAuthority3563.Capture(observation, resolver, recovery), observation: observation);

    // Read-only monitor projection retains cumulative physical overflow while
    // deriving capacities from the same saved history and retained rules.
    internal static CharacterSheetDerivedSection ParseConditionDisplay9590(WorkspaceStoredDocument observation,
        ICharacterSourceDataResolver resolver, ICharacterCreationRuleSourceRecovery? recovery)
        => ParseCore2912(observation.Document.Content, resolver, false, out _,
            CharacterCreationPriorityGrantAuthority3563.Capture(observation, resolver, recovery),
            projectUninjured: true, observation: observation, retainOverflow5412: true);

    // Both source admission and history capture use the exact saved observation.
    // A retained resolver cannot admit reserialized/projected XML. The private
    // parser projects only the two counters after admitting the original inputs;
    // that calculation tree never becomes a saved observation or history proof.
    internal static CharacterSheetDerivedSection ParseUninjured3659(WorkspaceStoredDocument observation,
        ICharacterSourceDataResolver? resolver)
        => ParseCore2912(observation.Document.Content, resolver, false, out _,
            CharacterCreationPriorityGrantAuthority3563.Capture(observation, resolver), projectUninjured: true, observation: observation);

    internal static CharacterSheetDerivedSection ParsePlaySustaining5262(WorkspaceStoredDocument observation,
        ICharacterSourceDataResolver resolver, out Chummer.Contracts.Session.SessionPlaySustainingAuthority? sustaining)
        => ParsePlayCasting5296(observation, resolver, out sustaining, out _);

    internal static CharacterSheetDerivedSection ParsePlayCasting5296(WorkspaceStoredDocument observation,
        ICharacterSourceDataResolver resolver, out Chummer.Contracts.Session.SessionPlaySustainingAuthority? sustaining,
        out Chummer.Contracts.Session.SessionPlayCastingAuthority? casting)
    {
        var capture = new SessionPlaySustainingCapture();
        var result = ParseCore2912(observation.Document.Content, resolver, false, out _,
            CharacterCreationPriorityGrantAuthority3563.Capture(observation, resolver),
            projectUninjured: true, observation: observation, playCapture: capture);
        sustaining = result.UnavailableReason is null ? capture.Authority : null;
        casting = result.UnavailableReason is null ? capture.Casting : null;
        return result;
    }

    // Separate admission flag: old neutral projections and receipts keep their exact semantics.
    internal static CharacterSheetDerivedSection ParsePlayInitialCondition5412(WorkspaceStoredDocument observation,
        ICharacterSourceDataResolver resolver, out Chummer.Contracts.Session.SessionPlaySustainingAuthority? sustaining,
        out Chummer.Contracts.Session.SessionPlayCastingAuthority? casting)
    {
        var capture = new SessionPlaySustainingCapture();
        var result = ParseCore2912(observation.Document.Content, resolver, false, out _,
            CharacterCreationPriorityGrantAuthority3563.Capture(observation, resolver),
            projectUninjured: true, observation: observation, playCapture: capture, retainOverflow5412: true);
        sustaining = result.UnavailableReason is null ? capture.Authority : null;
        casting = result.UnavailableReason is null ? capture.Casting : null;
        return result;
    }

    internal static CharacterSheetDerivedSection ParseWithPriorityGrants3563(string xml, ICharacterSourceDataResolver? resolver,
        CharacterCreationPriorityGrantAuthority3563? priorityGrants)
        => ParseCore2912(xml, resolver, false, out _, priorityGrants);

    internal static CharacterSheetDerivedSection ParseForRuleEffects2912(string xml, ICharacterSourceDataResolver? resolver,
        out IReadOnlyList<CharacterRuleEffectValue>? ownerValues, CharacterCreationPriorityGrantAuthority3563? priorityGrants = null)
        => ParseCore2912(xml, resolver, true, out ownerValues, priorityGrants);

    // Only a successful complete sheet calculation can return its reconciled owner
    // compiler. The caller binds it to its private, disposable current evaluation.
    internal sealed record ImportedCareerSheet5699(
        CharacterCurrentRuleEnvironmentContext Current, CharacterSheetDerivedSection Sheet,
        CharacterSheetOwnerGraph? Owners, CharacterCreationPriorityGrantAuthority3563? PriorityGrants,
        Chummer.Contracts.Session.SessionPlaySustainingAuthority? Sustaining,
        Chummer.Contracts.Session.SessionPlayCastingAuthority? Casting)
    {
        internal bool HasPrioritySelection9569 => PriorityGrants is not null && IsCurrent
            && (PriorityGrants.IsImported5693 || CurrentCharacterRuleSourceSemantics.IsNativePriorityCareerProfile9569(
                Current.SemanticsProfile, Current.SemanticsFingerprint));

        internal bool IsCurrent => Owners is not null && Sheet.UnavailableReason is null
            && Sheet.AuthorityDigest is not null && Current.VerifyRetainedArchive();
    }

    internal static ImportedCareerSheet5699 ParseImportedCareer5688(CharacterCurrentRuleEnvironmentContext current,
        bool projectUninjured = false, bool retainOverflow5412 = false)
        => ParseImportedCareerCore5688(current, projectUninjured, retainOverflow5412);

    internal static ImportedCareerSheet5699 ParseImportedCareerForEvaluation6371(CharacterCurrentRuleEnvironmentContext current,
        ICharacterSourceDataContext source, CharacterCurrentRuleEnvironmentConsumers.RetainedEvaluation2764? evaluation)
        => ParseImportedCareerCore5688(current, false, false, evaluation6371: evaluation, source6371: source,
            allowOwnedOperation6371: false);

    private static ImportedCareerSheet5699 ParseImportedCareerCore5688(CharacterCurrentRuleEnvironmentContext current,
        bool projectUninjured, bool retainOverflow5412, PlayAmmunitionCapture5701? ammunition5701 = null,
        CharacterCurrentRuleEnvironmentConsumers.RetainedEvaluation2764? evaluation6371 = null,
        ICharacterSourceDataContext? source6371 = null, bool allowOwnedOperation6371 = true)
    {
        if (evaluation6371 is not null && (source6371 is null || !evaluation6371.Allows(current, source6371)))
            return Failed("The imported retained evaluation no longer agrees with this source.");
        if (!CurrentCharacterRuleSourceSemantics.IsOwnedPriorityCareerProfile9569(
                current.SemanticsProfile, current.SemanticsFingerprint) || !current.VerifyRetainedArchive())
            return Failed("The imported current character and retained sources no longer agree.");
        using var borrowed6371 = source6371 is null ? null
            : evaluation6371?.BorrowImportedSheetOperation6371(current, source6371);
        var retained = borrowed6371 is null
            ? current.Capture.CreateCurrentEnvironmentResolution(current.SemanticsProfile, current.SemanticsFingerprint) : null;
        if (borrowed6371 is null && retained is null) return Failed("The imported retained resolution is unavailable.");
        // Observe existing receipts; absent imported receipts are not fabricated.
        // Current XML/source proof below remains separate from transaction history.
        if (!CharacterCareerInitiationTransaction.ObserveHistory(current.Current))
            return Failed("The saved grade history no longer agrees with its existing receipts.");
        // A caller-supplied resolver must never replace any source in this closure.
        using var owned6371 = borrowed6371 is null && allowOwnedOperation6371
            ? (retained as IRetainedCurrentRuleEvaluationIssuer2764)?.BeginImportedSheetOperation6371(current) : null;
        var operation6371 = borrowed6371 ?? owned6371;
        var resolver = operation6371 ?? retained!.Resolver;
        var root = ParseXml(current.CharacterXml);
        CharacterCreationPriorityGrantAuthority3563? grants = null;
        var retainedContext = resolver.TryCreateContext(current.CharacterXml);
        bool sprite = retainedContext is not null && TryMetatype(root, retainedContext, out var spriteXml, out _, out _)
            && CharacterSpriteSource5751.IsSource(ParseXml(spriteXml));
        if (CurrentCharacterRuleSourceSemantics.IsNativePriorityCareerProfile9569(
                current.SemanticsProfile, current.SemanticsFingerprint))
        {
            // Native history is mandatory, including for zero-grant selections.
            // No failed native capture may be retried as an imported selection.
            if (retainedContext is null || sprite)
                return Failed("The native Priority source context is unavailable.");
            grants = CharacterCreationPriorityGrantAuthority3563.Capture(current.Current, retainedContext);
            if (grants is null || grants.IsImported5693
                || !grants.TryResolve(root, retainedContext, out _, out _, out _))
                return Failed("The native Priority selection does not match its confirmed history and retained sources.");
        }
        else if (!sprite && (Boolean(root, "magenabled", false) || Boolean(root, "resenabled", false)))
        {
            grants = CharacterCreationPriorityGrantAuthority3563.CaptureImported5693(current);
            if (!grants.IsImported5693)
                return Failed("The imported prioritytalent, priorityspecial and priorityskills/priorityskill selection must resolve uniquely in retained sources.");
        }
        var capture = new ImportedCareerOwnerCapture5688();
        var play = projectUninjured ? new SessionPlaySustainingCapture() : null;
        // Keep both context requests and all source queries. Only the privately
        // issued operation can make the precheck and complete parser share reads.
        // Public/custom/changing providers keep the original independent path.
        var sheet = operation6371 is null
            ? ParseCore2912(current.CharacterXml, resolver, false, out _, grants,
                projectUninjured: projectUninjured, observation: current.Current, playCapture: play,
                retainOverflow5412: retainOverflow5412, importedOwners5688: capture, ammunition5701: ammunition5701)
            : ParseInSourceOperation6371(current.CharacterXml, resolver, false, out _, grants,
            projectUninjured: projectUninjured, observation: current.Current, playCapture: play,
            retainOverflow5412: retainOverflow5412, importedOwners5688: capture, ammunition5701: ammunition5701);
        if (!current.VerifyRetainedArchive()) return Failed("The imported current observation changed during calculation.");
        return new(current, sheet, sheet.UnavailableReason is null ? capture.Owners : null,
            grants, sheet.UnavailableReason is null ? play?.Authority : null,
            sheet.UnavailableReason is null ? play?.Casting : null);

        ImportedCareerSheet5699 Failed(string reason) => new(current,
            CharacterSheetDerivedSection.Unavailable(reason), null, null, null, null);
    }

    // The callback is private to the parser and runs before its operation scope
    // is disposed. Only immutable calculation results escape; no reusable owner
    // or retained source context is added to the public Play authority.
    internal static CharacterSheetDerivedSection ParsePlayAmmunition5701(WorkspaceStoredDocument observation,
        ICharacterSourceDataResolver resolver, List<Chummer.Contracts.Session.SessionPlayDiagnostic> diagnostics,
        out IReadOnlyList<CharacterWeaponFireState> weapons, out Chummer.Contracts.Session.SessionPlayAmmunition? ammunition,
        CharacterCurrentRuleEnvironmentContext? importedCurrent5699 = null)
    {
        var root = ParseXml(observation.Document.Content);
        bool hasWeapons = One(root, "weapons")?.Elements("weapon").Any() == true;
        // An empty saved improvement container is not proof of an effect-free
        // runner: missing racial/selected rows must fail the complete compiler too.
        var capture = hasWeapons ? new PlayAmmunitionCapture5701(diagnostics) : null;
        // Imported MAG/RES proof and loaded-round custody must come from the
        // same retained owner calculation; a native grant capture cannot stand
        // in for the imported current context.
        var sheet = importedCurrent5699 is null
            ? ParseCore2912(observation.Document.Content, resolver, false, out _,
                CharacterCreationPriorityGrantAuthority3563.Capture(observation, resolver), observation: observation, ammunition5701: capture)
            : ReferenceEquals(importedCurrent5699.Current, observation)
                ? ParseImportedCareerCore5688(importedCurrent5699, false, false, capture).Sheet
                : CharacterSheetDerivedSection.Unavailable("The imported ammunition observation changed.");
        ammunition = sheet.UnavailableReason is null ? capture?.Ammunition : null;
        weapons = sheet.UnavailableReason is null ? capture?.Weapons ?? [] : [];
        if (hasWeapons && ammunition is null) diagnostics.Add(new("weapon-ammunition-unavailable", "ammo-reload",
            "The complete current owner/effect and loaded-round authority is unavailable."));
        return sheet;
    }

    internal static CharacterSheetDerivedSection ParsePhysicalAmmunition5701(string xml, ICharacterSourceDataResolver resolver,
        List<Chummer.Contracts.Session.SessionPlayDiagnostic> diagnostics, out IReadOnlyList<CharacterRuleEffectValue>? owners,
        out IReadOnlyList<CharacterWeaponFireState> weapons, out Chummer.Contracts.Session.SessionPlayAmmunition? ammunition,
        CharacterCreationPriorityGrantAuthority3563? priorityGrants = null)
    {
        var capture = new PlayAmmunitionCapture5701(diagnostics);
        var sheet = ParseCore2912(xml, resolver, true, out owners, priorityGrants, ammunition5701: capture);
        weapons = sheet.UnavailableReason is null ? capture.Weapons : [];
        ammunition = sheet.UnavailableReason is null ? capture.Ammunition : null;
        return sheet;
    }

    private sealed class PlayAmmunitionCapture5701(List<Chummer.Contracts.Session.SessionPlayDiagnostic> diagnostics)
    {
        internal IReadOnlyList<CharacterWeaponFireState> Weapons { get; private set; } = [];
        internal Chummer.Contracts.Session.SessionPlayAmmunition? Ammunition { get; private set; }
        internal void Project(XElement root, CharacterSheetOwnerGraph owners)
        {
            Weapons = owners.ProjectAmmunition5701(root, diagnostics, out var ammunition);
            Ammunition = ammunition;
        }
    }

    // Current native Career capacity needs the same complete, source-reconciled
    // sheet owner compiler as imported Career. Keep its native semantics and
    // creation-grant history; this does not turn it into a V9 import or grant
    // inventory from a catalog. Callers bind the resolver to the current source.
    internal static CharacterSheetOwnerGraph? ResolveCurrentNativeOwners7846(
        CharacterCurrentRuleEnvironmentContext current, ICharacterSourceDataResolver resolver)
    {
        if (current.Stage != CharacterCurrentRuleStage.Career
            || !CurrentCharacterRuleSourceSemantics.IsNativeExecutionProfile(
                current.SemanticsProfile, current.SemanticsFingerprint)
            || !current.VerifyRetainedArchive()) return null;
        var source = resolver.TryCreateContext(current.CharacterXml);
        if (source is null) return null;
        var ownerEvaluation = current.RetainedEvaluation2764;
        if (ownerEvaluation is not null && ownerEvaluation.Allows(current, source)
            && ownerEvaluation.NativeOwners8048 is { HasNativeStockObservation8048: true } retainedOwners)
        {
            if (retainedOwners.ReobserveNativeOwners8048(current, source, ownerEvaluation)
                && current.VerifyRetainedArchive()) return retainedOwners;
            ownerEvaluation.Invalidate6371();
            return null;
        }
        var grants = CharacterCreationPriorityGrantAuthority3563.Capture(current.Current, source);
        var capture = new ImportedCareerOwnerCapture5688();
        var sheet = ParseCore2912(current.CharacterXml, resolver, false, out _, grants,
            observation: current.Current, importedOwners5688: capture);
        if (sheet.UnavailableReason is not null
            || !CharacterCreationFinalizationDigest.IsCanonical(sheet.AuthorityDigest)
            || !current.VerifyRetainedArchive()) return null;
        if (current.RetainedEvaluation2764 is { } evaluation && evaluation.Allows(current, source))
        {
            capture.Owners?.BindCurrentStockObservation(current, source, evaluation);
            evaluation.NativeOwners8048 = capture.Owners;
        }
        return capture.Owners;
    }

    private sealed class ImportedCareerOwnerCapture5688
    {
        internal CharacterSheetOwnerGraph? Owners { get; set; }
    }

    private static CharacterSheetDerivedSection ParseCore2912(string xml, ICharacterSourceDataResolver? resolver,
        bool includeOwnerValues, out IReadOnlyList<CharacterRuleEffectValue>? ownerValues,
        CharacterCreationPriorityGrantAuthority3563? priorityGrants = null, bool projectUninjured = false,
        WorkspaceStoredDocument? observation = null, SessionPlaySustainingCapture? playCapture = null, bool retainOverflow5412 = false, ImportedCareerOwnerCapture5688? importedOwners5688 = null, PlayAmmunitionCapture5701? ammunition5701 = null)
    {
        ownerValues = null;
        if (resolver is null) return CharacterSheetDerivedSection.Unavailable(MissingSource);
        using var operation = (resolver as ICharacterSourceDataResolverOperationScopeFactory)?.CreateOperationScope();
        return ParseInSourceOperation6371(xml, operation ?? resolver, includeOwnerValues, out ownerValues,
            priorityGrants, projectUninjured, observation, playCapture, retainOverflow5412, importedOwners5688, ammunition5701);
    }

    // The caller owns disposal, including failed/exceptional calculations. Keep
    // ParseCore2912 uniquely named and its default entry/lifetime unchanged.
    private static CharacterSheetDerivedSection ParseInSourceOperation6371(string xml, ICharacterSourceDataResolver resolver,
        bool includeOwnerValues, out IReadOnlyList<CharacterRuleEffectValue>? ownerValues,
        CharacterCreationPriorityGrantAuthority3563? priorityGrants = null, bool projectUninjured = false,
        WorkspaceStoredDocument? observation = null, SessionPlaySustainingCapture? playCapture = null, bool retainOverflow5412 = false,
        ImportedCareerOwnerCapture5688? importedOwners5688 = null, PlayAmmunitionCapture5701? ammunition5701 = null)
    {
        ownerValues = null;
        CharacterSheetOwnerGraph? traitEvidence = null;
        Func<bool>? inputsStillCurrent = null;
        try
        {
            var root = ParseXml(xml);
            Require(root.Name == "character" && !root.HasAttributes
                && Text(root, "gameedition", "SR5") == "SR5");
            var context = resolver.TryCreateContext(xml);
            if (context is null || !context.TryResolveCareerSettingsProfile(out var settingsXml, out var settingsState)
                || !TryMetatype(root, context, out var metatypeXml, out var metatypeState, out var metatypeOwnerId))
                return CharacterSheetDerivedSection.Unavailable(MissingSource);
            var settings = ParseXml(settingsXml);
            var metatype = ParseXml(metatypeXml);
            string calculationXml = xml;
            int observedPhysical = 0, observedStun = 0;
            if (projectUninjured)
            {
                // Read the original scalars before replacing them: duplicate,
                // nested, attributed, noninteger or negative wounds cannot be
                // made valid by projection. Capacities are checked below.
                if (retainOverflow5412) Chummer.Application.Session.SessionPlayInitialConditionSource5412.ValidateScalars(root);
                observedPhysical = Integer(root, "physicalcmfilled", 0);
                observedStun = Integer(root, "stuncmfilled", 0);
                CharacterSheetOwnerGraph.Need(observedPhysical >= 0 && observedStun >= 0
                    && !Boolean(root, "physicalcmiscorecm", false) && !Boolean(root, "stuncmismatrixcm", false)
                    && Integer(root, "physicalcmoverflow", 0) == 0,
                    "uninjured-condition-outside-supported-track", "condition");
                root.SetElementValue("physicalcmfilled", 0);
                root.SetElementValue("stuncmfilled", 0);
                calculationXml = root.ToString(SaveOptions.DisableFormatting);
            }
            bool created = Boolean(root, "created");
            Require(!Boolean(root, "ignorerules", false) && !Boolean(root, "ai", false)
                && !Boolean(root, "depenabled", false));
            // Existing physical initiative rejects weight/armor wound profiles too.
            foreach (string flag in new[] { "doencumbrancepenaltywoundmodifier", "doencumbrancepenaltyphysicallimit",
                         "doencumbrancepenaltyagility", "doencumbrancepenaltyreaction" })
                CharacterSheetOwnerGraph.Need(!Boolean(settings, flag, false), "weight-encumbrance-policy-unconsumed", "settings/" + flag);

            var sourceChecks = new List<Func<bool>>();
            CharacterFinalizedLifeModuleOwnership? modules = null;
            if (created && Text(root, "buildmethod", "") == CharacterCreationBuildMethods.LifeModules)
            {
                CharacterSheetOwnerGraph.Need(observation is not null
                    && CharacterFinalizedLifeModuleOwnership.TryObserve(observation, context, out modules)
                    && modules is not null, "finalized-module-authority-unavailable", "qualities");
                string digest = modules!.Digest;
                sourceChecks.Add(() => CharacterFinalizedLifeModuleOwnership.TryObserve(observation!, context, out var final)
                    && final?.Digest == digest);
            }
            inputsStillCurrent = () => context.TryResolveCareerSettingsProfile(out var finalSettings, out var finalSettingsState)
                && finalSettings == settingsXml && finalSettingsState == settingsState
                && TryMetatype(root, context, out var finalMetatype, out var finalMetatypeState, out var finalMetatypeOwnerId)
                && finalMetatype == metatypeXml && finalMetatypeState == metatypeState && finalMetatypeOwnerId == metatypeOwnerId
                && sourceChecks.All(check => check());
            var authority = CharacterSheetOwnerGraph.Resolve(root, context, sourceChecks, metatype, settings, metatypeOwnerId, out traitEvidence, priorityGrants, modules);
            // Source evidence remains the complete raw source above. Only the
            // established arithmetic consumers receive the observed numeric view.
            metatype = authority.Sprite5751?.Evaluated ?? metatype;
            var attributes = new Dictionary<string, int>(StringComparer.Ordinal);
            var attributeValues = new List<CharacterSheetAttributeValue>();
            var savedAttributes = One(root, "attributes") ?? throw Invalid();
            IEnumerable<string> attributeNames = AttributeNames;
            if (savedAttributes.Elements("attribute").Any(a => Text(a, "name") == "EDG"))
                attributeNames = attributeNames.Append("EDG");
            foreach (string name in attributeNames)
            {
                var rows = savedAttributes.Elements("attribute").Where(a => Text(a, "name") == name).ToArray();
                Require(rows.Length == 1);
                var row = rows[0];
                string key = name.ToLowerInvariant();
                int minimum = Integer(metatype, key + "min"), maximum = Integer(metatype, key + "max");
                int augmented = Integer(metatype, key + "aug");
                Require(minimum >= 1 && maximum >= minimum && augmented >= maximum
                    && Integer(row, "metatypemin") == minimum && Integer(row, "metatypemax") == maximum
                    && Integer(row, "metatypeaugmax") == augmented);
                int allocated = Integer(row, "base"), karma = Integer(row, "karma");
                Require(allocated >= 0 && karma >= 0);
                var native = authority.Attribute(name, minimum, maximum, augmented, allocated, karma,
                    Boolean(settings, "unclampattributeminimum", false));
                Require(checked(minimum + allocated + karma) <= native.Maximum);
                attributes.Add(name, native.Current);
                attributeValues.Add(new(name, native.Current) { Unaugmented = native.Value,
                    AllocatedBase = allocated, AllocatedKarma = karma, AugmentedModifier = native.Current - native.Value });
            }
            foreach (var special in authority.SpecialAttributes)
            {
                attributes.Add(special.Name, special.Total);
                attributeValues.Add(special);
            }
            decimal essence = authority.Essence(metatype, settings);
            Require(essence > 0);
            int minimumDice = Integer(settings, "mininitiativedice"), maximumDice = Integer(settings, "maxinitiativedice");
            int? directDice = One(metatype, "initiativedice") is null ? null : Integer(metatype, "initiativedice");
            Require(minimumDice >= 0 && maximumDice >= 0 && (directDice is null || directDice >= 0));
            var initiativeSource = new CharacterCreationPhysicalInitiativeAuthority("sr5",
                CharacterCreationPhysicalInitiativeAuthority.SemanticsV1, Text(root, "settings"),
                CharacterCreationFinalizationDigest.Compute(settingsState), CharacterCreationFinalizationDigest.Compute(calculationXml),
                Text(metatype, "id"), CharacterCreationFinalizationDigest.Compute(metatypeXml), "sheet-metatype",
                CharacterCreationFinalizationDigest.Compute(metatypeState),
                new(Integer(metatype, "inimin"), Integer(metatype, "inimax"), Integer(metatype, "iniaug"), minimumDice)
                    { DirectDice886 = directDice }, maximumDice);
            var condition = authority.Condition(attributes, essence);
            if (projectUninjured)
                CharacterSheetOwnerGraph.Need((retainOverflow5412 || observedPhysical <= condition.PhysicalMaximum) && observedStun <= condition.StunMaximum,
                    "uninjured-condition-outside-supported-track", "condition");
            var initiative = authority.Initiative(initiativeSource, attributes, condition);
            var movement = authority.Movement(metatype, attributes);
            var skills = ResolveSkills(root, settings, context, attributes, initiative.WoundModifier, created, sourceChecks, authority, movement, playCapture);
            authority.ValidateSkillSelections(skills, settings);
            var matrixDevices = authority.MatrixDevices3474(attributes);
            // Detect drift after resolving all inputs, without reading the workspace again.
            CharacterSheetOwnerGraph.Need(inputsStillCurrent(), "source-profile-or-rule-pack-drift", "source-checks");

            // Character.Dodge and LimitPhysical/Mental/Social. Their native limits exclude
            // per-test LimitModifiers; those remain separately labeled by ParseLimitModifiers.
            // Portable source penalties are separate from persisted Dodge improvements.
            // Capture the same raw basis for play so wounds/sustaining are overlaid once.
            int rawDefense = checked(attributes["REA"] + attributes["INT"] + authority.Rounded("Dodge") + authority.SourceDefensePenalty8587);
            int defense = Math.Max(0, checked(rawDefense + initiative.WoundModifier + authority.SustainingPenalty5064));
            int physical = checked(CeilingThird(2 * attributes["STR"] + attributes["BOD"] + attributes["REA"]) + authority.Rounded("PhysicalLimit"));
            int mental = checked(CeilingThird(2 * attributes["LOG"] + attributes["INT"] + attributes["WIL"]) + authority.Rounded("MentalLimit"));
            int social = checked(CeilingThird(2 * attributes["CHA"] + attributes["WIL"] + StandardRound(essence)) + authority.Rounded("SocialLimit"));
            var result = new CharacterSheetDerivedSection(attributeValues, skills, initiative, authority.DefenseUnavailableReason is null ? defense : null, physical, mental, social, essence, null)
            {
                Condition = condition, GrantedTraits = authority.GrantedTraits, UnlockedSkills = authority.UnlockedSkills, OwnedConditionalLimits = authority.ConditionalLimits, OtherPoolsAndModifiers = authority.Other(attributes),
                SustainedSpellPreparation = authority.SustainedSpellPreparation5064,
                RuleModifiers = authority.RuleModifiers(), DefenseUnavailableReason = authority.DefenseUnavailableReason,
                Movement = movement,
                Vehicles = authority.Vehicles3474.Count == 0 ? null : authority.Vehicles3474,
                ComplexForms = authority.ComplexForms3474.Count == 0 ? null : authority.ComplexForms3474,
                MatrixDevices = matrixDevices.Count == 0 ? null : matrixDevices,
                InitiativeModes = authority.InitiativeModes5082(settings, attributes, initiative, matrixDevices),
                AuthorityDigest = CharacterCreationFinalizationDigest.Compute(string.Join('\0',
                    "chummer.core.sr5-sheet-movement/v1", settingsState, metatypeState, authority.AuthorityDigest, calculationXml))
            };
            CharacterSheetOwnerGraph.Need(inputsStillCurrent(), "source-profile-or-rule-pack-drift", "initiative-source-checks");
            if (playCapture is not null)
            {
                playCapture.Casting = authority.CapturePlayCasting5296(result, playCapture);
                playCapture.Authority = authority.CapturePlaySustaining5262(settings, result, new(
                    authority.DefenseUnavailableReason is null ? rawDefense : null,
                    playCapture.Skills.ToArray(),
                    checked(result.OtherPoolsAndModifiers["Composure"] - authority.SustainingPenalty5064),
                    checked(result.OtherPoolsAndModifiers["Memory"] - authority.SustainingPenalty5064)));
            }
            if (ammunition5701 is not null)
            {
                ammunition5701.Project(root, authority);
                CharacterSheetOwnerGraph.Need(inputsStillCurrent(), "source-profile-or-rule-pack-drift", "ammunition-source-checks");
            }
            if (importedOwners5688 is not null) importedOwners5688.Owners = authority;
            if (!includeOwnerValues) return result;
            var observedOwners = authority.RuleEffectOwners2912();
            CharacterSheetOwnerGraph.Need(inputsStillCurrent(), "source-profile-or-rule-pack-drift", "rule-effect-source-checks");
            ownerValues = observedOwners;
            return result;
        }
        catch (Exception exception) when (exception is InvalidDataException or XmlException or FormatException
            or OverflowException or DivideByZeroException or InvalidOperationException or ArgumentException)
        {
            string reason = exception is InvalidDataException && exception.Message.StartsWith("sheet:", StringComparison.Ordinal)
                ? exception.Message : UnsupportedGraph;
            // An unsupported arithmetic rule does not erase already source-bound
            // trait identities. No arithmetic or authority digest survives here;
            // failed closure, malformed data and source drift still expose none.
            bool independent = false;
            if (traitEvidence is not null && reason.Split(':').ElementAtOrDefault(1)?.EndsWith("-unconsumed", StringComparison.Ordinal) == true)
            {
                try { independent = inputsStillCurrent?.Invoke() == true; }
                catch (Exception e) when (e is InvalidDataException or XmlException or FormatException or OverflowException
                    or InvalidOperationException or ArgumentException) { independent = false; }
            }
            return CharacterSheetDerivedSection.Unavailable(reason) with
            {
                GrantedTraits = independent ? traitEvidence!.GrantedTraits.Select(t => t with { MechanicsUnavailableReason = reason }).ToArray() : []
            };
        }
    }

    internal static bool TryMetatype(XElement root, ICharacterSourceDataContext context,
        out string xml, out string state, out Guid ownerId)
    {
        xml = state = ""; ownerId = Guid.Empty;
        if (context is ICharacterSheetOwnerSourceContext source)
        {
            if (!CharacterMetatypeIdentity.TryRead(root, out var name, out var id,
                out var variantName, out var variantId)) return false;
            if (!source.TryResolveSheetNamedSource("Metatype", id?.ToString("D") ?? name, out var parent) || parent is null
                || parent.Name != name) return false;
            ownerId = parent.SourceId;
            bool selectedId = variantId is { } variantIdentity && variantIdentity != Guid.Empty;
            bool selectedName = variantName is not ("" or "None");
            if (!selectedId && !selectedName)
            {
                xml = parent.SourceXml;
                state = CharacterCreationFinalizationDigest.Compute(parent);
                return true;
            }
            if (!selectedName || !source.TryResolveSheetMetavariant(parent.SourceId,
                selectedId ? variantId!.Value.ToString("D") : variantName, out var variant) || variant is null
                || variant.Name != variantName) return false;
            var baseRow = ParseXml(parent.SourceXml);
            CharacterSheetOwnerGraph.Need(Text(baseRow, "category", "") != "Shapeshifter",
                "metavariant-dual-form-attributes-unconsumed", "metatype/category");
            // Native Create uses the selected variant's ranges/grants/bonus, not
            // an additive union with base grants. Parent remains the bonus owner.
            // Native Character's movement getters fall back to the parent for absent
            // movement fields. Do not inherit ranges/grants/bonuses or saved strings.
            // Both receipts remain in state and in the final source-drift check.
            var selected = ParseXml(variant.SourceXml);
            foreach (string field in new[] { "movement", "walk", "run", "sprint" })
                if (One(selected, field) is null && One(baseRow, field) is { } inherited)
                    selected.Add(new XElement(inherited));
            xml = selected.ToString(SaveOptions.DisableFormatting);
            state = CharacterCreationFinalizationDigest.Compute(new { Parent = parent, Variant = variant });
            return true;
        }
        if (context.TryResolveCareerQualityMetatype(out xml, out state))
        {
            ownerId = CharacterSheetOwnerGraph.Id(ParseXml(xml), "id");
            return true;
        }
        // The native Priority finalizer saves the name and ranges but no metatypeguid.
        // Its canonical catalog already validates source shape, books, profile, overlays,
        // unsupported bonuses and unique identity. Never replace a conflicting saved GUID.
        if (One(root, "metatypeguid") is not null || Text(root, "metavariant", "") is not ("" or "None")
            || !context.TryResolveCreationMetatypeCatalog(out var catalog) || !catalog.IsAuthoritative
            || catalog.Blockers.Count != 0 || !catalog.SourceContext.IsAuthoritative
            || catalog.SourceContext.SettingsProfileId != Text(root, "settings")) return false;
        string savedId = Text(root, "metatypeid", "");
        var matches = catalog.Options.Where(o => o.Label == Text(root, "metatype")
            && (savedId.Length == 0 || o.OptionId == savedId)).ToArray();
        if (matches is not [var option] || !option.IsEnabled || option.Blockers.Count != 0
            || option.GrantedQualities.Count != 0
            || option.BaseBonuses is { } bonus && (bonus.Armor != 0 || bonus.Reach != 0 || bonus.LifestyleCostPercent != 0))
            return false;
        // Adapt the existing typed projection to the same native attribute/initiative
        // read inputs. These are source-projected ranges, not saved display totals.
        var metatype = new XElement("metatype", new XElement("id", option.OptionId), new XElement("name", option.Label),
            option.Attributes.SelectMany(a => new[]
            {
                new XElement(a.AttributeId.ToLowerInvariant() + "min", a.Minimum),
                new XElement(a.AttributeId.ToLowerInvariant() + "max", a.Maximum),
                new XElement(a.AttributeId.ToLowerInvariant() + "aug", a.AugmentedMaximum)
            }), new XElement("inimin", option.Initiative.Minimum), new XElement("inimax", option.Initiative.Maximum),
            new XElement("iniaug", option.Initiative.AugmentedMaximum),
            option.Initiative.DirectDice886 is { } dice ? new XElement("initiativedice", dice) : null);
        ownerId = CharacterSheetOwnerGraph.Id(metatype, "id");
        xml = metatype.ToString(SaveOptions.DisableFormatting);
        state = catalog.SourceContext.AuthorityDigest;
        return true;
    }

    private static IReadOnlyList<CharacterSheetSkillValue> ResolveSkills(XElement root, XElement settings,
        ICharacterSourceDataContext context, IReadOnlyDictionary<string, int> attributes, int wound,
        bool created, List<Func<bool>> checks, CharacterSheetOwnerGraph authority, CharacterSheetMovementValues movement,
        SessionPlaySustainingCapture? playCapture = null)
    {
        var result = new List<CharacterSheetSkillValue>();
        var newSkills = One(root, "newskills");
        Require(newSkills is not null || !authority.HasPriorityGrants3563);
        if (newSkills is null && authority.NamedSkillSources.Count == 0) return result;
        newSkills ??= new XElement("newskills");
        Require(CharacterCreationSkillGroupPolicyAuthority.TryResolve(settings,
            out bool brokenPoints, out bool strictGroups, out _));
        var inputs = (One(newSkills, "skills")?.Elements() ?? [])
            .Select(row => new SheetSkillInput(row, Text(row, "suid", ""), Text(row, "guid", ""), false))
            .Concat((One(newSkills, "knoskills")?.Elements() ?? [])
                .Select(row => new SheetSkillInput(row, Text(row, "suid", ""), Text(row, "guid", ""), true))).ToList();
        Require(inputs.Select(r => r.Identity).Distinct(StringComparer.Ordinal).Count() == inputs.Count);
        Require(authority.PrioritySkillIds3563.All(id => inputs.Count(row => !row.Knowledge && row.SourceId == id) == 1));
        var groupSources = new Dictionary<string, CharacterActiveSkillSource>(StringComparer.Ordinal);
        var groups = ResolveSkillGroups(root, newSkills, settings, context, created,
            strictGroups, inputs, groupSources, checks, authority);
        Require(authority.PriorityGroupNames3563.All(groups.ContainsKey));
        foreach (var (id, skill) in authority.NamedSkillSources)
        {
            groupSources.TryAdd(id, skill);
            // AddSkills creates an unpurchased canonical row. A missing serialized
            // zero row has no learned rating, and a non-defaultable skill has no pool.
            if (!inputs.Any(i => i.SourceId == id)) inputs.Add(new(null, id, id, false));
        }
        foreach (var input in inputs)
        {
            var row = input.Saved;
            string id = input.Identity, name = row is null ? groupSources[input.SourceId].Name
                : Text(row, "name", Text(row, "suid", "Unnamed skill"));
            try
            {
                Require(row is null || row.Name == "skill"
                    && Guid.TryParse(id, out var guid) && guid != Guid.Empty
                    && Boolean(row, "isknowledge") == input.Knowledge);
                string sourceId = input.SourceId;
                bool knowledge = input.Knowledge;
                string attributeName, groupName, sourceXmlText;
                bool movementKnown = true;
                if (knowledge)
                {
                    var savedKnowledge = row ?? throw Invalid();
                    Require(context.TryResolveKnowledgeSkillSource(sourceId, out var source));
                    Require(source.SourceSkillId == sourceId && Text(savedKnowledge, "name") == source.Name
                        && Text(savedKnowledge, "type") == source.SkillCategory);
                    name = source.Name;
                    attributeName = source.DefaultAttribute;
                    groupName = string.Empty;
                    sourceXmlText = source.RawSourceXml;
                    checks.Add(() => context.TryResolveKnowledgeSkillSource(sourceId, out var final) && final == source);
                    if (Boolean(savedKnowledge, "isnativelanguage", false))
                    {
                        Require(source.SkillCategory == "Language" && Integer(savedKnowledge, "base") == 0 && Integer(savedKnowledge, "karma") == 0);
                        result.Add(new(id, name, attributeName, null, null, null, IsNativeLanguage: true));
                        continue;
                    }
                }
                else
                {
                    if (!groupSources.TryGetValue(sourceId, out var source))
                    {
                        Require(context.TryResolveActiveSkillSource(sourceId, out source));
                        var captured = source;
                        checks.Add(() => context.TryResolveActiveSkillSource(sourceId, out var final) && final == captured);
                    }
                    Require(source.SourceSkillId == sourceId && !source.IsExotic);
                    Require(!groups.ContainsKey(source.SkillGroup) || groupSources.ContainsKey(sourceId));
                    name = source.Name;
                    attributeName = source.DefaultAttribute;
                    groupName = source.SkillGroup;
                    sourceXmlText = source.RawSourceXml;
                    // Skill.Enabled consumes the actual movement mode. This is
                    // the same reconciled source result shown on the sheet, not
                    // cached saved rates. Unknown and zero modes stay unavailable.
                    movementKnown = (!source.RequiresGroundMovement || movement.Ground.IsAvailable == true)
                        && (!source.RequiresSwimMovement || movement.Swim.IsAvailable == true)
                        && (!source.RequiresFlyMovement || movement.Fly.IsAvailable == true);
                }
                Require(attributes.ContainsKey(attributeName));
                var sourceXml = ParseXml(sourceXmlText);
                int cap = SkillCap(settings, created, knowledge);
                // Skill.FromData creates an unpurchased member with BasePoints/KarmaPoints
                // zero. Only this proven absence has zero allocations; incomplete saved
                // rows still fail closed rather than acquiring defaults.
                int purchasedBase = row is null ? 0 : Integer(row, "base");
                int purchasedKarma = row is null ? 0 : Integer(row, "karma");
                Require(purchasedBase >= 0 && purchasedKarma >= 0);
                var (groupBase, groupKarma) = knowledge || !groups.TryGetValue(groupName, out var contribution)
                    ? (0, 0) : contribution;
                bool strict = !created && strictGroups;
                // Skill.Base/Karma/LearnedRating precede the separate rating and pool channels.
                // Native Base adds FreeBase even when strict group policy excludes
                // personal BasePoints. The compiler admitted the exact Priority rows.
                int freeBase = knowledge ? 0 : authority.PrioritySkillBase3563(name);
                int baseRating = Math.Min(cap, checked(freeBase + (groupBase > 0
                    ? checked(groupBase + (strict || !brokenPoints ? 0 : purchasedBase)) : purchasedBase)));
                int freeKarma = knowledge ? 0 : authority.Rounded("SkillLevel", name);
                int karmaRating = strict && groupKarma > 0 ? groupKarma : Math.Min(cap, checked(groupKarma + purchasedKarma + freeKarma));
                int learned = checked(baseRating + karmaRating);
                string category = Text(sourceXml, "category", "");
                int rating = checked(learned + authority.SkillModifier(name, category, groupName, attributeName, true));
                Require(rating >= 0);
                int attribute = attributes[attributeName];
                int bonus = authority.SkillModifier(name, category, groupName, attributeName, false);
                // Keep the unclamped pool for situational specialization additions: a
                // negative base cannot gain dice by clamping before the addition.
                bool defaultBlocked = rating == 0 && authority.BlocksDefaulting(category);
                bool canRoll = movementKnown && (rating > 0 || Boolean(sourceXml, "default", false) && !defaultBlocked);
                int rawPool = checked((rating > 0 ? rating : -1) + attribute + wound + authority.SustainingPenalty5064 + bonus);
                int pool = canRoll ? Math.Max(0, rawPool) : 0;
                var specs = ReadSpecializations(row, settings, rating, rawPool, movementKnown && canRoll);
                bool conditional = authority.HasConditionalSkill(name, category, groupName, attributeName);
                result.Add(new(id, name, attributeName, conditional ? null : rating, movementKnown && !conditional && !defaultBlocked ? pool : null,
                    conditional ? "A source-owned conditional skill modifier requires action context."
                        : defaultBlocked ? "The source-owned quality blocks defaulting in this skill category."
                        : movementKnown ? null : "This skill requires an available source-derived movement mode.")
                { LearnedRating = learned, Specializations = conditional ? [] : specs });
                playCapture?.Skills.Add(new(id, checked((rating > 0 ? rating : -1) + attribute + bonus), canRoll));
            }
            catch (Exception exception) when (exception is InvalidDataException or FormatException or OverflowException)
            {
                result.Add(new(id, name, string.Empty, null, null, "This skill's source or effect semantics could not be resolved."));
            }
        }
        return result;
    }

    private static IReadOnlyList<CharacterSheetSpecializationValue> ReadSpecializations(XElement? skill,
        XElement settings, int rating, int rawPool, bool canRoll)
    {
        if (skill is null) return [];
        var rows = One(skill, "specs")?.Elements().ToArray() ?? [];
        var names = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<CharacterSheetSpecializationValue>();
        foreach (var spec in rows)
        {
            Require(spec.Name == "spec" && !spec.HasAttributes);
            string name = Text(spec, "name");
            Require(name.Length > 0 && names.Add(name));
            // Expertise must have a source-bound granting improvement (not supported here).
            Require(!Boolean(spec, "expertise", false));
            int bonus = rating > 0 ? Integer(settings, "specializationbonus", 2) : 0;
            Require(bonus >= 0);
            if (canRoll) result.Add(new(name, bonus, Math.Max(0, checked(rawPool + bonus))));
        }
        return result;
    }

    private sealed record SheetSkillInput(XElement? Saved, string SourceId, string Identity, bool Knowledge);

    private static Dictionary<string, (int Base, int Karma)> ResolveSkillGroups(XElement root,
        XElement newSkills, XElement settings, ICharacterSourceDataContext context, bool created,
        bool strictGroups, List<SheetSkillInput> inputs,
        Dictionary<string, CharacterActiveSkillSource> sources, List<Func<bool>> checks, CharacterSheetOwnerGraph authority)
    {
        var result = new Dictionary<string, (int Base, int Karma)>(StringComparer.Ordinal);
        var rows = One(newSkills, "groups")?.Elements().ToArray() ?? [];
        if (rows.Length == 0) return result;
        string profile = Text(root, "settings");
        // Membership needs the strict source catalog, not creation spending authority:
        // its knowledge-contribution projection rejects ordinary saved quality fields.
        // The owner compiler already consumes the complete source-bound effect graph; keep
        // that payload intact and use this same context for catalog and drift checks.
        Require(context.TryResolveCreationSkillsCatalog(out var resolved));
        var catalog = resolved ?? throw Invalid();
        Require(ValidSkillsCatalog(catalog, profile));
        checks.Add(() => context.TryResolveCreationSkillsCatalog(out var final)
            && ValidSkillsCatalog(final, profile) && final!.CatalogDigest == catalog.CatalogDigest);
        var explicitMembers = new Dictionary<string, SheetSkillInput>(StringComparer.Ordinal);
        foreach (var input in inputs.Where(i => !i.Knowledge))
        {
            Require(Guid.TryParse(input.SourceId, out var sourceId) && sourceId != Guid.Empty);
            Require(explicitMembers.TryAdd(sourceId.ToString("D"), input));
        }
        int cap = SkillCap(settings, created, knowledge: false);
        var groupIds = new HashSet<Guid>();
        foreach (var row in rows)
        {
            Require(row.Name == "group" && !row.HasAttributes && row.Elements().All(e =>
                e.Name == "name" || e.Name == "id" || e.Name == "base" || e.Name == "karma" || e.Name == "isbroken"));
            Require(Guid.TryParse(Text(row, "id"), out var instance) && instance != Guid.Empty && groupIds.Add(instance));
            string name = Text(row, "name");
            var matches = catalog.SkillGroups.Where(g => g.Name == name).ToArray();
            Require(matches.Length == 1 && !result.ContainsKey(name));
            var group = matches[0];
            Require(group.MemberSkillSourceIds.Count >= 2 && group.MemberSkillSourceIds
                .Distinct(StringComparer.Ordinal).Count() == group.MemberSkillSourceIds.Count);
            Require(group.MemberSkillSourceIds.Order(StringComparer.Ordinal).SequenceEqual(catalog.ActiveSkills
                .Where(s => s.SkillGroup == name).Select(s => s.SourceSkillId).Order(StringComparer.Ordinal)));
            bool disabled = authority.IsSkillGroupDisabled(name,
                catalog.ActiveSkills.Where(s => s.SkillGroup == name).Select(s => s.Category));
            int allocatedBase = Integer(row, "base"), allocatedKarma = Integer(row, "karma");
            Require(allocatedBase >= 0 && allocatedKarma >= 0);
            allocatedBase = checked(allocatedBase + authority.PriorityGroupBase3563(name));
            int freeLevels = authority.Rounded("SkillGroupLevel", name);
            _ = Boolean(row, "isbroken", false); // Native saved state; KarmaUnbroken is derived below.
            var ownRatings = new List<(int Base, int Karma)>();
            foreach (string sourceId in group.MemberSkillSourceIds)
            {
                var definitions = catalog.ActiveSkills.Where(s => s.SourceSkillId == sourceId).ToArray();
                Require(definitions.Length == 1);
                var definition = definitions[0];
                Require(definition.Kind == CharacterCreationSkillKinds.Active && !definition.IsExotic);
                Require(context.TryResolveActiveSkillSource(sourceId, out var source));
                // Both views must describe the same enabled, validated catalog member.
                // The catalog compiler consumes source shape/effects, book and hide rules.
                Require(source.SourceSkillId == sourceId && source.Name == definition.Name
                    && source.SkillCategory == definition.Category && source.SkillGroup == name
                    && source.DefaultAttribute == definition.DefaultAttribute && !source.IsExotic
                    && source.RequiresGroundMovement == definition.RequiresGroundMovement
                    && source.RequiresSwimMovement == definition.RequiresSwimMovement
                    && source.RequiresFlyMovement == definition.RequiresFlyMovement
                    && Boolean(ParseXml(source.RawSourceXml), "default") == definition.CanDefault);
                Require(sources.TryAdd(sourceId, source));
                checks.Add(() => context.TryResolveActiveSkillSource(sourceId, out var final) && final == source);
                bool selected = explicitMembers.TryGetValue(sourceId, out var saved);
                int ownBase = selected ? Integer(saved!.Saved!, "base") : 0;
                int ownKarma = selected ? Integer(saved!.Saved!, "karma") : 0;
                Require(ownBase >= 0 && ownKarma >= 0);
                ownRatings.Add((checked(ownBase + authority.PrioritySkillBase3563(source.Name)),
                    checked(ownKarma + authority.Rounded("SkillLevel", source.Name))));
                if (!disabled && !selected && (allocatedBase > 0 || allocatedKarma > 0 || freeLevels > 0))
                    inputs.Add(new(null, sourceId, $"group:{instance:D}:{sourceId}", false));
            }
            // SkillGroup.KarmaUnbroken uses BasePoints/FreeBase and KarmaPoints/FreeKarma,
            // not RatingModifiers. Module FreeLevels survive a broken group; paid Karma does not.
            bool karmaUnbroken = !created && strictGroups
                ? ownRatings.All(r => r.Base == 0 && r.Karma == 0)
                : ownRatings.All(r => checked(r.Base + r.Karma) >= ownRatings.Max(v => v.Base));
            // Native SkillGroup.Base and Karma both return zero while disabled,
            // including paid allocations, Priority FreeBase and module FreeLevels.
            // Member/source validation above still runs; individual skill points remain.
            result.Add(name, disabled ? (0, 0)
                : (Math.Min(cap, allocatedBase), Math.Min(cap, checked(freeLevels + (karmaUnbroken ? allocatedKarma : 0)))));
        }
        return result;
    }

    private static bool ValidSkillsCatalog(CharacterCreationSkillsCatalog? catalog, string profile) =>
        catalog is not null && catalog.SettingsProfileId == profile
        && CharacterCreationSkillsCatalogAuthority.IsValid(catalog);

    private static int SkillCap(XElement settings, bool created, bool knowledge)
    {
        string key = knowledge ? "maxknowledgeskillrating" : "maxskillrating";
        int cap = 12; // CharacterSettings' native Career default, only for absent fields.
        if (created) cap = Integer(settings, key, cap);
        else Require(CharacterCreationStandardPrioritySkillsRules.TryReadCreationRatingLimit(settings, key + "create", key, out cap));
        Require(cap >= 0);
        return cap;
    }

    private static int StandardRound(decimal value) => CharacterCreationEssenceRules1222.Round(value);
    private static int CeilingThird(int value) => checked((int)decimal.Ceiling(value / 3m));
    private static XElement ParseXml(string xml)
    {
        using var input = new StringReader(xml);
        using var reader = XmlReader.Create(input, new XmlReaderSettings
            { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 16 * 1024 * 1024 });
        return XElement.Load(reader);
    }
    private static XElement? One(XElement parent, string name)
    {
        var rows = parent.Elements().Where(e => e.Name.LocalName == name).ToArray();
        Require(rows.Length <= 1 && rows.All(e => e.Name == name));
        return rows.SingleOrDefault();
    }
    private static string Text(XElement parent, string name, string? absent = null)
    {
        var row = One(parent, name);
        if (row is null) return absent ?? throw Invalid();
        Require(!row.HasAttributes && !row.HasElements);
        return row.Value;
    }
    private static int Integer(XElement parent, string name, int? absent = null) =>
        int.TryParse(Text(parent, name, absent?.ToString(CultureInfo.InvariantCulture)), NumberStyles.Integer,
            CultureInfo.InvariantCulture, out int value) ? value : throw Invalid();
    private static decimal Decimal(XElement parent, string name) =>
        decimal.TryParse(Text(parent, name), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture, out decimal value) ? value : throw Invalid();
    private static bool Boolean(XElement parent, string name, bool? absent = null) =>
        bool.TryParse(Text(parent, name, absent?.ToString()), out bool value) ? value : throw Invalid();
    private static void Require(bool condition) { if (!condition) throw Invalid(); }
    private static InvalidDataException Invalid() => new("sheet-derived-source-unresolved");
}

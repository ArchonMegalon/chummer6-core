using System.Globalization;
using System.Runtime.CompilerServices;
using System.Xml;
using System.Xml.Linq;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

/// <summary>Source-bound observation of a rule cost, not permission to learn or spend.</summary>
public sealed class CharacterCurrentMagicRuleQuote1949
{
    internal CharacterCurrentMagicRuleQuote1949(Guid sourceId, string kind, int ruleKarmaCost,
        int effectiveAttribute, string sourceXml, string sourceState, bool alreadyKnown)
        => (SourceId, Kind, RuleKarmaCost, EffectiveAttribute, SourceXml, SourceState, AlreadyKnown) =
            (sourceId, kind, ruleKarmaCost, effectiveAttribute, sourceXml, sourceState, alreadyKnown);
    public Guid SourceId { get; }
    public string Kind { get; }
    public int RuleKarmaCost { get; }
    public int EffectiveAttribute { get; }
    public string SourceXml { get; }
    public string SourceState { get; }
    public bool AlreadyKnown { get; }
}

public sealed class CharacterCurrentMagicOptionQuote1949
{
    internal CharacterCurrentMagicOptionQuote1949(CharacterCreationMagicResonanceOptionFinalizationSource option,
        CharacterCreationMagicResonanceFinalizationContribution? creation,
        CharacterCurrentMagicRuleQuote1949? career, CharacterAdeptPowerPointIncreaseQuote? power)
        => (Option, Creation, Career, Power) = (option, creation, career, power);
    internal CharacterCurrentMagicOptionQuote1949(CharacterCareerPowerSource source, CharacterAdeptPowerPointIncreaseQuote power)
        => (CareerPowerSource, Power) = (source, power);
    public CharacterCreationMagicResonanceOptionFinalizationSource? Option { get; }
    public CharacterCareerPowerSource? CareerPowerSource { get; }
    public CharacterCreationMagicResonanceFinalizationContribution? Creation { get; }
    public CharacterCurrentMagicRuleQuote1949? Career { get; }
    public CharacterAdeptPowerPointIncreaseQuote? Power { get; }
}

public sealed class CharacterCurrentTraditionQuote1949
{
    internal CharacterCurrentTraditionQuote1949(CharacterCreationMagicResonanceOptionFinalizationSource option,
        CharacterTraditionDrainSemantics? drainSelector, int drainPool, string spiritForm, IReadOnlyList<string> spiritNames,
        IReadOnlyList<CharacterSpiritNameChoiceState> currentSpirits,
        CharacterCreationMagicResonanceFinalizationContribution? creation)
        => (Option, DrainSelector, DrainPool, SpiritForm, SpiritNames, CurrentSpirits, Creation) =
            (option, drainSelector, drainPool, spiritForm, spiritNames, currentSpirits, creation);
    public CharacterCreationMagicResonanceOptionFinalizationSource Option { get; }
    public CharacterTraditionDrainSemantics? DrainSelector { get; }
    public int DrainPool { get; }
    public string SpiritForm { get; }
    public IReadOnlyList<string> SpiritNames { get; }
    public IReadOnlyList<CharacterSpiritNameChoiceState> CurrentSpirits { get; }
    public CharacterCreationMagicResonanceFinalizationContribution? Creation { get; }
}

public sealed class CharacterCurrentLoadoutQuote1949
{
    internal CharacterCurrentLoadoutQuote1949(CharacterCreationLoadoutLine line, decimal balanceBefore, decimal balanceAfter)
        => (Line, BalanceBefore, BalanceAfter) = (line, balanceBefore, balanceAfter);
    public CharacterCreationLoadoutLine Line { get; }
    public decimal BalanceBefore { get; }
    public decimal BalanceAfter { get; }
}

public static partial class CharacterCurrentRuleEnvironmentConsumers
{
    private const string TalentsLoadoutGrammar1949 = "chummer.current-talents-loadout.fixed-source.v1";
    private sealed record TalentsCatalog1949(CharacterCurrentRuleEnvironmentContext Current,
        ICharacterSourceDataContext Source, string Frozen);
    private static readonly ConditionalWeakTable<CharacterCurrentRuleCatalogValidation, TalentsCatalog1949>
        TalentsCatalogs1949 = new();

    public static CharacterCurrentLoadoutQuote1949? QuoteCurrentWeapon(CharacterCurrentRuleEnvironmentContext current,
        CharacterCurrentRuleCatalogValidation catalog, ICharacterSourceDataContext source, string catalogKey, int quantity,
        CancellationToken cancellationToken = default)
        => catalog.Domain == CharacterCurrentRuleDomain.Weapons
            ? QuoteFundedCurrentLoadout1949(current, catalog, source, catalogKey, quantity, cancellationToken) : null;

    public static CharacterCurrentLoadoutQuote1949? QuoteCurrentArmor(CharacterCurrentRuleEnvironmentContext current,
        CharacterCurrentRuleCatalogValidation catalog, ICharacterSourceDataContext source, string catalogKey, int quantity,
        CancellationToken cancellationToken = default)
        => catalog.Domain == CharacterCurrentRuleDomain.Armor
            ? QuoteFundedCurrentLoadout1949(current, catalog, source, catalogKey, quantity, cancellationToken) : null;

    public static CharacterCurrentMagicOptionQuote1949? QuoteCurrentSpell(CharacterCurrentRuleEnvironmentContext current,
        CharacterCurrentRuleCatalogValidation catalog, ICharacterSourceDataContext source, string catalogKey,
        CancellationToken cancellationToken = default)
        => catalog.Domain == CharacterCurrentRuleDomain.Spells
            ? QuoteMagicOption1949(current, catalog, source, catalogKey, 1, cancellationToken) : null;

    public static CharacterCurrentMagicOptionQuote1949? QuoteCurrentComplexForm(CharacterCurrentRuleEnvironmentContext current,
        CharacterCurrentRuleCatalogValidation catalog, ICharacterSourceDataContext source, string catalogKey,
        CancellationToken cancellationToken = default)
        => catalog.Domain == CharacterCurrentRuleDomain.ComplexForms
            ? QuoteMagicOption1949(current, catalog, source, catalogKey, 1, cancellationToken) : null;

    public static CharacterCurrentMagicOptionQuote1949? QuoteCurrentPower(CharacterCurrentRuleEnvironmentContext current,
        CharacterCurrentRuleCatalogValidation catalog, ICharacterSourceDataContext source, string catalogKey, int rating,
        CancellationToken cancellationToken = default)
        => catalog.Domain == CharacterCurrentRuleDomain.Powers
            ? QuoteMagicOption1949(current, catalog, source, catalogKey, rating, cancellationToken) : null;

    public static CharacterCurrentTraditionQuote1949? QuoteCurrentTradition(CharacterCurrentRuleEnvironmentContext current,
        CharacterCurrentRuleCatalogValidation catalog, ICharacterSourceDataContext source, string catalogKey,
        CancellationToken cancellationToken = default)
        => catalog.Domain == CharacterCurrentRuleDomain.Traditions
            ? QuoteTradition1949(current, catalog, source, catalogKey, cancellationToken) : null;

    public static CharacterCurrentTraditionQuote1949? QuoteCurrentStream(CharacterCurrentRuleEnvironmentContext current,
        CharacterCurrentRuleCatalogValidation catalog, ICharacterSourceDataContext source, string catalogKey,
        CancellationToken cancellationToken = default)
        => catalog.Domain == CharacterCurrentRuleDomain.Streams
            ? QuoteTradition1949(current, catalog, source, catalogKey, cancellationToken) : null;

    private static CharacterCurrentLoadoutQuote1949? QuoteFundedCurrentLoadout1949(CharacterCurrentRuleEnvironmentContext current,
        CharacterCurrentRuleCatalogValidation catalog, ICharacterSourceDataContext source, string key, int quantity,
        CancellationToken cancellationToken)
    {
        var line = QuoteCurrentLoadout(current, catalog, source, key, quantity, cancellationToken);
        if (line is null || !TalentsCatalogs1949.TryGetValue(catalog, out var retained)) return null;
        try
        {
            decimal balance;
            if (current.Stage == CharacterCurrentRuleStage.Career)
            {
                if (!decimal.TryParse(TextTalents1949(ParseTalents1949(current.CharacterXml), "nuyen"),
                    NumberStyles.Number, CultureInfo.InvariantCulture, out balance) || balance < 0) return null;
            }
            else
            {
                var auxiliary = current.Current.Document.AuxiliaryState;
                var funding = auxiliary.CharacterCreationResourcesDraft;
                if (funding is null || !CharacterCreationResourcesReceiptLedgerIntegrity.IsValidLedger(current.Current.Id,
                        current.Current.ContentRevision, funding, auxiliary.CharacterCreationResourcesReceipts)
                    || funding.BaseRawCharacterXmlDigest != CharacterCreationFinalizationDigest.ComputeUtf8(current.CharacterXml)
                    || !retained.Source.TryResolveCreationResourcesAuthority(out var authority)
                    || !CharacterCreationResourcesRules.IsValidAuthority(authority)
                    || authority.AuthorityDigest != funding.AuthorityDigest || !funding.Budget.IsExact
                    || funding.Budget.Blockers.Count != 0) return null;
                balance = funding.Budget.RemainingNuyen;
                var loadout = auxiliary.CharacterCreationLoadoutDraft;
                if (loadout is not null)
                {
                    if (!CharacterCreationLoadoutReceiptLedgerIntegrity.HasFunding(loadout, funding,
                        auxiliary.CharacterCreationResourcesReceipts)) return null;
                    balance = checked(balance - loadout.PurchaseCost);
                }
            }
            decimal after = checked(balance - line.TotalCost);
            return after >= 0 && current.IsUnchanged ? new(line, balance, after) : null;
        }
        catch (Exception exception) when (TalentsFailure1949(exception)) { return null; }
    }

    private static CharacterCurrentMagicOptionQuote1949? QuoteMagicOption1949(CharacterCurrentRuleEnvironmentContext current,
        CharacterCurrentRuleCatalogValidation catalog, ICharacterSourceDataContext source, string key, int rating,
        CancellationToken cancellationToken)
    {
        if (current.Stage == CharacterCurrentRuleStage.Career && catalog.Domain == CharacterCurrentRuleDomain.Powers)
        {
            if (!TryRetainedTalents1949(current, catalog, source, key, cancellationToken, out var actual, out var entry)) return null;
            try
            {
                Guid id = SourceIdentityTalents1949(catalog.Domain, entry, ParseTalents1949(entry.Xml));
                if (!actual.TryResolveCareerPowerSource(id, out var powerSource) || powerSource is null
                    || !SameTalentsXml1949(ParseTalents1949(entry.Xml), powerSource.RawSourceXml)
                    || !CharacterCareerPowerGraph.TryQuoteCurrent(current.Current, actual, id, rating, out var quote)
                    || quote is null || !current.IsUnchanged) return null;
                return new(powerSource, quote);
            }
            catch (Exception exception) when (TalentsFailure1949(exception)) { return null; }
        }
        var option = ProjectCurrentMagicOption(current, catalog, source, key, rating, cancellationToken);
        if (option is null || !TalentsCatalogs1949.TryGetValue(catalog, out var retained)) return null;
        if (current.Stage == CharacterCurrentRuleStage.Creation)
        {
            var contribution = QuoteCurrentCreationMagic1949(current, catalog.Domain, retained.Source, option, rating);
            return contribution is null ? null : new(option, contribution, null, null);
        }
        var career = QuoteCurrentCareerMagic(current, catalog, source, key, cancellationToken);
        if (career is null) return null;
        try
        {
            var root = ParseTalents1949(current.CharacterXml);
            if (IntTalents1949(root, "karma", -1) < career.RuleKarmaCost) return null;
            if (catalog.Domain == CharacterCurrentRuleDomain.ComplexForms)
            {
                if (career.AlreadyKnown) return null;
                var settings = CurrentTalentsSettings1949(root, retained.Source, out _);
                var resonance = CurrentResonance5699(current, root, retained.Source);
                if (!CharacterCareerComplexFormLearningTransaction.TryEvaluateCurrentCount(settings, resonance,
                    CurrentMagicRows1949(root, catalog.Domain).Length, adding: true, out _)) return null;
            }
            return current.IsUnchanged ? new(option, null, career, null) : null;
        }
        catch (Exception exception) when (TalentsFailure1949(exception)) { return null; }
    }

    private static CharacterCurrentTraditionQuote1949? QuoteTradition1949(CharacterCurrentRuleEnvironmentContext current,
        CharacterCurrentRuleCatalogValidation catalog, ICharacterSourceDataContext source, string key,
        CancellationToken cancellationToken)
    {
        var option = ProjectCurrentMagicOption(current, catalog, source, key, 1, cancellationToken);
        if (option is null || !TalentsCatalogs1949.TryGetValue(catalog, out var retained)) return null;
        try
        {
            var root = ParseTalents1949(current.CharacterXml);
            CurrentCareerEffects1960? careerEffects = null;
            if ((UsesNativeMethods1958(current) || UsesOwnedPriorityCareer9569(current)) && current.Stage == CharacterCurrentRuleStage.Career)
            {
                careerEffects = CompileCurrentCareerEffects1960(current, retained.Source, []);
                if (careerEffects is null) return null;
                if (careerEffects.Imported is not null) root = careerEffects.Graph;
            }
            var owned = root.Elements("tradition").SingleOrDefault();
            if (owned is null) return null;
            var creation = current.Stage == CharacterCurrentRuleStage.Creation
                ? QuoteCurrentCreationMagic1949(current, catalog.Domain, retained.Source, option, 1) : null;
            if (current.Stage == CharacterCurrentRuleStage.Creation && creation is null) return null;
            if (current.Stage == CharacterCurrentRuleStage.Career)
            {
                if (catalog.Domain == CharacterCurrentRuleDomain.Traditions)
                {
                    var settings = CurrentTalentsSettings1949(root, retained.Source, out _);
                    var magic = CurrentMagic5699(current, root, settings, retained.Source);
                    if (!magic.IsSupported || !magic.IsMagician || magic.Magic <= 0) return null;
                }
                else
                {
                    var resonance = CurrentResonance5699(current, root, retained.Source);
                    if (!resonance.IsSupported || !resonance.IsTechnomancer || resonance.EffectiveResonance <= 0) return null;
                }
            }
            var result = EvaluateCurrentTradition1949(current, catalog.Domain, retained.Source, root, owned, option, creation, careerEffects);
            return current.IsUnchanged ? result : null;
        }
        catch (Exception exception) when (TalentsFailure1949(exception)) { return null; }
    }

    private static CharacterCreationMagicResonanceFinalizationContribution? QuoteCurrentCreationMagic1949(
        CharacterCurrentRuleEnvironmentContext current, CharacterCurrentRuleDomain domain, ICharacterSourceDataContext source,
        CharacterCreationMagicResonanceOptionFinalizationSource option, int rating)
    {
        try
        {
            var auxiliary = current.Current.Document.AuxiliaryState;
            var draft = auxiliary.CharacterCreationMagicResonanceDraft;
            var prerequisite = auxiliary.CharacterCreationPrerequisiteDraft;
            var attributes = auxiliary.CharacterCreationAttributesDraft;
            if (draft is null || prerequisite is null || attributes is null
                || draft.PrerequisiteDraftRevision != prerequisite.DraftRevision || draft.PrerequisiteDraftDigest != prerequisite.DraftDigest
                || draft.AttributesDraftRevision != attributes.DraftRevision || draft.AttributesDraftDigest != attributes.DraftDigest
                || !source.TryResolveCreationMagicResonanceAuthority(out var authority)) return null;
            var talent = authority.Talents.SingleOrDefault(row => row.Identity == draft.TalentIdentity);
            if (talent is null) return null;
            var selected = draft.Selections;
            selected = domain switch
            {
                CharacterCurrentRuleDomain.Traditions => selected with { Tradition = option.Identity },
                CharacterCurrentRuleDomain.Streams => selected with { Stream = option.Identity },
                CharacterCurrentRuleDomain.Powers => selected with { AdeptPowers = selected.AdeptPowers
                    .Where(row => row.Identity != option.Identity).Append(new(option.Identity, rating)).ToArray() },
                CharacterCurrentRuleDomain.Spells => selected with { Spells = selected.Spells.Where(row => row != option.Identity).Append(option.Identity).ToArray() },
                CharacterCurrentRuleDomain.ComplexForms => selected with { ComplexForms = selected.ComplexForms.Where(row => row != option.Identity).Append(option.Identity).ToArray() },
                _ => selected
            };
            return CharacterCreationMagicResonanceFinalizationRules.TryCreate(
                CharacterCreationFinalizationDigest.ComputeUtf8(current.CharacterXml), prerequisite.DraftRevision, prerequisite.DraftDigest,
                attributes.DraftRevision, attributes.DraftDigest, authority, talent, selected, attributes, out var contribution, out var blockers)
                && blockers.Length == 0 && current.IsUnchanged ? contribution : null;
        }
        catch (Exception exception) when (TalentsFailure1949(exception)) { return null; }
    }

    public static CharacterCurrentRuleCatalogEntry? FindCurrentTalentsOption(
        CharacterCurrentRuleEnvironmentContext current, CharacterCurrentRuleCatalogValidation catalog,
        ICharacterSourceDataContext source, string catalogKey, CancellationToken cancellationToken = default)
    {
        if (!TryRetainedTalents1949(current, catalog, source, catalogKey, cancellationToken, out _, out var entry)) return null;
        return entry with { };
    }

    public static CharacterCreationLoadoutLine? QuoteCurrentLoadout(
        CharacterCurrentRuleEnvironmentContext current, CharacterCurrentRuleCatalogValidation catalog,
        ICharacterSourceDataContext source, string catalogKey, int quantity = 1, CancellationToken cancellationToken = default)
    {
        if (catalog.Domain is not (CharacterCurrentRuleDomain.Weapons or CharacterCurrentRuleDomain.Armor)
            || !TryRetainedTalents1949(current, catalog, source, catalogKey, cancellationToken, out var retained, out var entry)) return null;
        try
        {
            if (!retained.TryResolveCreationLoadoutAuthority(out var authority)) return null;
            Guid id = SourceIdentityTalents1949(catalog.Domain, entry, ParseTalents1949(entry.Xml));
            string kind = catalog.Domain == CharacterCurrentRuleDomain.Weapons ? "weapon" : "armor";
            var option = authority.Options.SingleOrDefault(row => row.SourceId == id && row.Kind == kind);
            if (option is null) return null;
            var selections = kind == "weapon"
                ? new CharacterCreationLoadoutSelections([new(option.OptionId, quantity)], [])
                : new CharacterCreationLoadoutSelections([], [new(option.OptionId, quantity)]);
            return CharacterCreationLoadoutRules.TryProjectBasket(selections, authority, out var lines, out _, out _)
                && lines.Length == 1 && current.IsUnchanged ? lines[0] : null;
        }
        catch (Exception exception) when (TalentsFailure1949(exception)) { return null; }
    }

    /// <summary>Canonical option projection only. It does not manufacture a completed
    /// Creation choice, current execution, or purchase permission.</summary>
    public static CharacterCreationMagicResonanceOptionFinalizationSource? ProjectCurrentMagicOption(
        CharacterCurrentRuleEnvironmentContext current, CharacterCurrentRuleCatalogValidation catalog,
        ICharacterSourceDataContext source, string catalogKey, int levels = 1, CancellationToken cancellationToken = default)
    {
        if (catalog.Domain is CharacterCurrentRuleDomain.Weapons or CharacterCurrentRuleDomain.Armor
            || !TryRetainedTalents1949(current, catalog, source, catalogKey, cancellationToken, out var retained, out var entry)) return null;
        try
        {
            var resolved = ResolveCurrentMagicCatalog1983(current, catalog.Domain, retained);
            Guid id = SourceIdentityTalents1949(catalog.Domain, entry, ParseTalents1949(entry.Xml));
            var option = resolved.Options.SingleOrDefault(row => row.Identity.SourceId == id.ToString("D"));
            return CharacterCreationMagicResonanceFinalizationRules.TryProjectOption(option, levels, out var projected)
                && current.IsUnchanged ? projected : null;
        }
        catch (Exception exception) when (TalentsFailure1949(exception)) { return null; }
    }

    public static CharacterCurrentMagicRuleQuote1949? QuoteCurrentCareerMagic(
        CharacterCurrentRuleEnvironmentContext current, CharacterCurrentRuleCatalogValidation catalog,
        ICharacterSourceDataContext source, string catalogKey, CancellationToken cancellationToken = default)
    {
        if (current.Stage != CharacterCurrentRuleStage.Career
            || catalog.Domain is not (CharacterCurrentRuleDomain.Spells or CharacterCurrentRuleDomain.ComplexForms)
            || !TryRetainedTalents1949(current, catalog, source, catalogKey, cancellationToken, out var retained, out var entry)) return null;
        try
        {
            var root = ParseTalents1949(current.CharacterXml);
            CurrentCareerEffects1960? careerEffects = null;
            if (catalog.Domain == CharacterCurrentRuleDomain.Spells && UsesOwnedPriorityCareer9569(current))
            {
                careerEffects = CompileCurrentCareerEffects1960(current, retained, []);
                if (careerEffects?.Imported is null) return null;
                root = careerEffects.Graph;
            }
            Guid id = SourceIdentityTalents1949(catalog.Domain, entry, ParseTalents1949(entry.Xml));
            var quote = EvaluateCurrentLearnedMagic1949(current, catalog.Domain, retained, root, id, careerEffects?.Imported?.Read);
            return current.IsUnchanged ? quote : null;
        }
        catch (Exception exception) when (TalentsFailure1949(exception)) { return null; }
    }

    private static bool TryRetainedTalents1949(CharacterCurrentRuleEnvironmentContext current,
        CharacterCurrentRuleCatalogValidation catalog, ICharacterSourceDataContext source, string key,
        CancellationToken cancellationToken, out ICharacterSourceDataContext retained,
        out CharacterCurrentRuleCatalogEntry entry)
    {
        retained = null!; entry = null!;
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrEmpty(key) || key == "$global" || !TalentsLoadoutDomain1949(catalog.Domain)
            || !catalog.IsComplete || !current.IsUnchanged || !TalentsCatalogs1949.TryGetValue(catalog, out var saved)
            || !ReferenceEquals(saved.Current, current) || saved.Frozen != catalog.ContentDigest) return false;
        var matches = catalog.Entries.Where(row => row.Key == key).ToArray();
        if (matches.Length != 1 || matches[0].State != CharacterCurrentRuleEntryState.AvailableNotSelected) return false;
        var executed = ExecuteTalentsLoadout(current, catalog, source, cancellationToken);
        if (executed is null || !executed.IsComplete || !current.IsUnchanged || saved.Frozen != catalog.ContentDigest) return false;
        retained = saved.Source; entry = matches[0]; return true;
    }

    public static CharacterCurrentRuleCatalogValidation? ValidateTalentsLoadoutCatalog(
        CharacterCurrentRuleEnvironmentContext current, CharacterCurrentRuleCatalogInput input,
        ICharacterSourceDataContext source, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(source);
        cancellationToken.ThrowIfCancellationRequested();
        if (!TalentsLoadoutDomain1949(input.Domain)) return null;
        var blockers = new List<string>();
        var unavailable = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            RequireTalents1949(current.IsUnchanged && input.ContextDigest == current.BindingDigest,
                "current-binding-changed");
            string query = TalentsQuery1949(input.Domain);
            RequireTalents1949(input.Query == query && input.Blockers.Count == 0, "catalog-input-unavailable");
            string[] dependencies = [current.CaptureDigest, current.EffectivePolicyDigest,
                current.RequiredPackageSetDigest ?? string.Empty, current.SemanticsProfile, current.SemanticsFingerprint];
            RequireTalents1949(input.Dependencies.SequenceEqual(dependencies), "catalog-dependencies-changed");
            RequireTalents1949(input.Entries.Count <= 65_536
                && input.Entries.Select(entry => entry.Key).Distinct(StringComparer.Ordinal).Count() == input.Entries.Count,
                "catalog-identities-ambiguous");
            for (int i = 0; i < input.Operations.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var operation = input.Operations[i];
                RequireTalents1949(operation.Order == i && operation.Query == query
                    && CharacterCreationFinalizationDigest.IsCanonical(operation.SourceIdentity)
                    && CharacterCreationFinalizationDigest.IsCanonical(operation.SourceDigest)
                    && operation.Dependencies.SequenceEqual(dependencies)
                    && operation.Phase is "override_" or "custom_" or "amend_"
                    && operation.Operation is "" or "RECURSE" or "REPLACE" or "APPEND" or "ADDNODE" or "REMOVE",
                    "catalog-operation-unsupported");
                _ = ParseTalents1949(operation.Selector);
                if (operation.BeforeXml is { } before) _ = ParseTalents1949(before);
                if (operation.AfterXml is { } after) _ = ParseTalents1949(after);
            }
            var global = input.Entries.Where(entry => entry.Key == "$global").ToArray();
            RequireTalents1949(global.Length == 1 && global[0].State == CharacterCurrentRuleEntryState.AvailableNotSelected,
                "catalog-global-missing");
            ValidateTalentsGlobal1949(input.Domain, ParseTalents1949(global[0].Xml));
            var entries = input.Entries.Where(entry => entry.Key != "$global"
                && entry.State != CharacterCurrentRuleEntryState.Tombstone).ToArray();
            RequireTalents1949(entries.All(entry => entry.State == CharacterCurrentRuleEntryState.AvailableNotSelected
                && entry.UnavailableReason is null), "catalog-option-unavailable");
            // Only parse rows as coverage or validation reaches them. Eager parsing would
            // let a malformed later row replace an earlier canonical-source blocker.
            // These detached trees belong to this validation call, never to an admission cache.
            var parsedEntries = new XElement?[entries.Length];
            XElement EntryRow(int index) => parsedEntries[index] ??= ParseTalents1949(entries[index].Xml);
            if (input.Domain is CharacterCurrentRuleDomain.Weapons or CharacterCurrentRuleDomain.Armor)
            {
                RequireTalents1949(TryCurrentLoadoutAuthority6218(current, source, out var authority), "loadout-authority-unavailable");
                string kind = input.Domain == CharacterCurrentRuleDomain.Weapons ? "weapon" : "armor";
                var options = authority.Options.Where(option => option.Kind == kind).ToArray();
                var coveredIds = new HashSet<Guid>();
                int coverageCount = 0;
                bool CoversSource(Guid sourceId)
                {
                    if (coveredIds.Contains(sourceId)) return true;
                    // Previously parsed prefixes are safe to reuse; stop at the same first
                    // match as Any, so an unvisited malformed suffix stays unvisited here.
                    while (coverageCount < entries.Length)
                    {
                        XElement row = EntryRow(coverageCount++);
                        if (Guid.TryParse(row.Element("id")?.Value, out var id))
                        {
                            coveredIds.Add(id);
                            if (id == sourceId) return true;
                        }
                    }
                    return false;
                }
                RequireTalents1949(options.All(option => CoversSource(option.SourceId)),
                    "loadout-catalog-coverage-mismatch");
                // Keep every matching authority row: duplicate identities must still fail.
                var optionsById = options.ToLookup(option => option.SourceId);
                var loadoutOptionProof = CurrentLoadoutOptionProof6237(current, source, authority);
                // The private retained issuer owns this exact source/character and admits
                // only the sealed parser. Its policy is a sealed scalar-only record;
                // resolving it again for every row cannot produce a different answer.
                // Keep this memo inside this one catalog validation. Public sources and
                // injected queries still make every original observation, including failures.
                var retainedEvaluation = loadoutOptionProof ?? (current.Stage == CharacterCurrentRuleStage.Career
                    ? RetainedEvaluationFor2764(current, source) : null);
                CharacterCreationLoadoutLegalityPolicy? retainedLegality = null;
                bool SupportsCareerPrice(string legality)
                {
                    var policy = retainedLegality;
                    if (policy is null)
                    {
                        if (!source.TryResolveCreationLoadoutLegalityPolicy(out policy) || policy is null)
                            return false;
                        if (retainedEvaluation is not null) retainedLegality = policy;
                    }
                    return !policy.RequiresUnsupportedPrice(legality);
                }
                var identities = new HashSet<Guid>();
                for (int entryIndex = 0; entryIndex < entries.Length; entryIndex++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var entry = entries[entryIndex];
                    XElement row = EntryRow(entryIndex);
                    Guid id = SourceIdentityTalents1949(input.Domain, entry, row);
                    RequireTalents1949(identities.Add(id), "loadout-catalog-identity-ambiguous");
                    var matches = optionsById[id];
                    int matchCount = matches.Count();
                    RequireTalents1949(matchCount <= 1, "loadout-source-ambiguous");
                    if (matchCount == 0)
                    {
                        RequireTalents1949(IsLocalTalentsOption1949(input.Domain, row), "loadout-unbounded-source");
                        unavailable.Add(entry.Key, "current-talents-option-not-supported-by-canonical-source");
                        continue;
                    }
                    var option = matches.First();
                    // EntryRow already parsed this immutable entry's XML, and the
                    // private proof owns and validated this exact option. Equal text
                    // must normalize to equal trees; avoid serializing and reparsing
                    // both sides. Differently formatted XML and public sources keep
                    // the original structural comparison and its failure ordering.
                    bool sameOwnedXml6255 = loadoutOptionProof is not null
                        && string.Equals(entry.Xml, option.SourceNodeXml, StringComparison.Ordinal);
                    RequireTalents1949(sameOwnedXml6255 || SameTalentsXml1949(row, option.SourceNodeXml), "loadout-source-changed");
                    bool supported = (loadoutOptionProof is not null || CharacterCreationLoadoutRules.IsValidOption(option))
                        && option.IsSelectable
                        && option.Blockers.Count == 0 && option.PricingIsExact && option.AvailabilityIsExact;
                    if (current.Stage == CharacterCurrentRuleStage.Career)
                        supported &= SupportsCareerPrice(option.Legality);
                    if (!supported)
                    {
                        RequireTalents1949(IsLocalTalentsOption1949(input.Domain, row), "loadout-unbounded-source");
                        unavailable.Add(entry.Key, "current-talents-option-not-supported-by-canonical-source");
                    }
                }
                // All mutable current inputs and the retained source binding are still
                // checked before the report escapes; Evaluate also validates the full
                // discovered source set on disposal. No execution or admission is reused.
                retainedEvaluation?.VerifyBoundary();
            }
            else
            {
                var resolved = ResolveCurrentMagicCatalog1983(current, input.Domain, source);
                var options = resolved.Options;
                CharacterCareerPowerGraph.ReadGraph? careerPowers = null;
                if (input.Domain == CharacterCurrentRuleDomain.Powers && current.Stage == CharacterCurrentRuleStage.Career
                    && BoolTalents1949(ParseTalents1949(current.CharacterXml), "adept", false))
                {
                    careerPowers = UsesOwnedPriorityCareer9569(current)
                        ? ImportedMagicRead5699(current, source).Owners!.CareerPowers5715
                            ?? CharacterCareerPowerGraph.EvaluateRead(current.Current, source)
                        : CharacterCareerPowerGraph.EvaluateRead(current.Current, source);
                    RequireTalents1949(careerPowers.Blockers.Count == 0, "power-current-catalog-kernel-unavailable");
                }
                // The typed authority uses canonical D-format GUIDs; retained stock
                // rows can use uppercase hex (for example Toxic in traditions.xml).
                // Compare the same identity without rewriting the source XML. The
                // per-row identity, uniqueness and exact payload checks still apply.
                var coveredIds = new HashSet<string>(StringComparer.Ordinal);
                int coverageCount = 0;
                bool CoversSource(string sourceId)
                {
                    if (coveredIds.Contains(sourceId)) return true;
                    while (coverageCount < entries.Length)
                    {
                        XElement row = EntryRow(coverageCount++);
                        if (Guid.TryParseExact(row.Element("id")?.Value, "D", out var id) && id != Guid.Empty)
                        {
                            string canonicalId = id.ToString("D");
                            coveredIds.Add(canonicalId);
                            if (canonicalId == sourceId) return true;
                        }
                    }
                    return false;
                }
                RequireTalents1949(options.All(option => CoversSource(option.Identity.SourceId)),
                    "magic-catalog-coverage-mismatch");
                var optionsById = options.ToLookup(option => option.Identity.SourceId, StringComparer.Ordinal);
                var identities = new HashSet<Guid>();
                for (int entryIndex = 0; entryIndex < entries.Length; entryIndex++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var entry = entries[entryIndex];
                    XElement row = EntryRow(entryIndex);
                    Guid id = SourceIdentityTalents1949(input.Domain, entry, row);
                    RequireTalents1949(identities.Add(id), "magic-catalog-identity-ambiguous");
                    RequireTalents1949(IsLocalTalentsOption1949(input.Domain, row), "magic-unbounded-source");
                    if (row.Name == "spirit")
                    {
                        unavailable.Add(entry.Key, "current-talents-spirit-stat-kernel-unavailable");
                        continue;
                    }
                    var matches = optionsById[id.ToString("D")];
                    int matchCount = matches.Count();
                    RequireTalents1949(matchCount <= 1, "magic-source-ambiguous");
                    if (matchCount == 0)
                    {
                        unavailable.Add(entry.Key, "current-talents-option-not-supported-by-canonical-source");
                        continue;
                    }
                    var option = matches.First();
                    RequireTalents1949(SameTalentsXml1949(row, option.CanonicalSourceXml), "magic-source-changed");
                    bool supported = CharacterCreationMagicResonanceFinalizationRules.HasValidOptionPayload(option)
                        && CharacterCreationMagicResonanceFinalizationRules.TryProjectOption(option, 1, out _);
                    try { ValidateTalentsOptionGrammar1949(input.Domain, row); }
                    catch (InvalidDataException) { supported = false; }
                    if (careerPowers is not null)
                        supported = careerPowers.Options.Any(candidate => candidate.Source.SourceId == id
                            && candidate.Identity.Extra.Length == 0 && SameTalentsXml1949(row, candidate.Source.RawSourceXml));
                    if (!supported) unavailable.Add(entry.Key, "current-talents-option-not-supported-by-canonical-source");
                }
            }
            // Known option-local syntax is retained as unavailable. Every typed future
            // query below rejects that exact key before source lookup, without fallback.
            foreach (var tombstone in input.Entries.Where(entry => entry.State == CharacterCurrentRuleEntryState.Tombstone))
            {
                XElement deleted = ParseTalents1949(tombstone.Xml);
                Guid id = SourceIdentityTalents1949(input.Domain, tombstone, deleted);
                RequireTalents1949(input.Operations.Any(operation => operation.IsTombstone
                    && operation.BeforeXml is { } before && ParseTalents1949(before).DescendantsAndSelf()
                        .Any(node => node.Name == deleted.Name && Guid.TryParse(node.Element("id")?.Value, out Guid beforeId) && beforeId == id)),
                    "catalog-tombstone-unobserved");
            }
        }
        catch (Exception exception) when (TalentsFailure1949(exception))
        {
            blockers.Add(TalentsBlocker1949(exception));
        }
        var result = CharacterCurrentRuleCatalogValidation.CreateSnapshot(input,
            Array.AsReadOnly(input.Entries.Select(entry => unavailable.TryGetValue(entry.Key, out var reason)
                ? entry with { State = CharacterCurrentRuleEntryState.Unavailable, UnavailableReason = reason } : entry).ToArray()),
            Array.AsReadOnly(blockers.ToArray()), TalentsLoadoutGrammar1949);
        if (result.IsComplete && current.IsUnchanged)
        {
            TalentsCatalogs1949.Add(result, new(current, source, result.ContentDigest));
            RetainedEvaluationFor2764(current, source)?.RememberTalents3238(result);
        }
        return result;
    }

    public static CharacterCurrentRuleDomainExecution? ExecuteTalentsLoadout(
        CharacterCurrentRuleEnvironmentContext current, CharacterCurrentRuleCatalogValidation catalog,
        ICharacterSourceDataContext source, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(source);
        cancellationToken.ThrowIfCancellationRequested();
        if (!TalentsLoadoutDomain1949(catalog.Domain)) return null;
        var blockers = new List<string>();
        var selected = new List<string>();
        object? outcome = null;
        var state = CharacterCurrentRuleConsumptionState.Evaluated;
        try
        {
            // Observe invalidation even if a binding guard below refuses early;
            // restoring a caller-owned collection must not revive this lifetime.
            var evaluation = RetainedEvaluationFor2764(current, source);
            RequireTalents1949(catalog.IsComplete && current.IsUnchanged
                && catalog.Input.ContextDigest == current.BindingDigest
                && catalog.GrammarProfile == TalentsLoadoutGrammar1949,
                "current-catalog-unavailable");
            RequireTalents1949(TalentsCatalogs1949.TryGetValue(catalog, out var retained)
                && ReferenceEquals(retained.Current, current) && retained.Frozen == catalog.ContentDigest,
                "current-catalog-identity-unavailable");
            // Only an immutable snapshot fully validated in this exact private
            // evaluation can reuse its catalog observation. Allows rechecks all
            // mutable character bindings and source drift. Selected-state kernels
            // below still run, and the issuer still verifies the archive boundary.
            // Foreign adapters and later evaluations keep both source observations.
            if (evaluation is null || !ReferenceEquals(source, retained!.Source)
                || !evaluation.OwnsTalents3238(catalog))
            {
                var checkedCatalog = ValidateTalentsLoadoutCatalog(current, catalog.Input, source, cancellationToken);
                RequireTalents1949(checkedCatalog is { IsComplete: true }
                    && checkedCatalog.ContentDigest == catalog.ContentDigest, "current-catalog-changed");
                if (!ReferenceEquals(source, retained!.Source))
                {
                    checkedCatalog = ValidateTalentsLoadoutCatalog(current, catalog.Input, retained.Source, cancellationToken);
                    RequireTalents1949(checkedCatalog is { IsComplete: true }
                        && checkedCatalog.ContentDigest == catalog.ContentDigest, "retained-catalog-changed");
                }
            }
            source = retained!.Source;
            XElement root = ParseTalents1949(current.CharacterXml);
            if (current.Stage == CharacterCurrentRuleStage.Career)
                RequireTalents1949(CharacterCareerMartialAcquisitionTransaction.TryProjectCurrentMartialEffects2438(root, source, out root),
                    "current-martial-effects-unresolved");
            CurrentCareerEffects1960? careerEffects = null;
            if ((UsesNativeMethods1958(current) || UsesOwnedPriorityCareer9569(current)) && current.Stage == CharacterCurrentRuleStage.Career)
            {
                careerEffects = CompileCurrentCareerEffects1960(current, source, blockers);
                RequireTalents1949(careerEffects is not null, "current-career-selected-effects-unresolved");
                root = careerEffects!.Graph;
            }
            RequireTalents1949(root.Name == "character" && !root.HasAttributes
                && current.Current.Document.RulesetId == "sr5"
                && TextTalents1949(root, "gameedition", "SR5") == "SR5"
                && BoolTalents1949(root, "created", false) == (current.Stage == CharacterCurrentRuleStage.Career),
                "current-stage-invalid");
            if (catalog.Domain is CharacterCurrentRuleDomain.Weapons or CharacterCurrentRuleDomain.Armor)
                outcome = ExecuteCurrentLoadout1949(current, catalog, source, root, selected, out state, careerEffects);
            else if (UsesNativeMethods1958(current) && current.Stage == CharacterCurrentRuleStage.Creation
                && source.TryResolveCreationSourceProfile(out var nativeProfile) && nativeProfile.BuildMethod == CharacterCreationBuildMethods.Karma)
                outcome = ConsumeNativeKarmaMagic1958(current, catalog, source, selected, ref state);
            else if (UsesNativeMethods1958(current) && current.Stage == CharacterCurrentRuleStage.Creation
                && source.TryResolveCreationSourceProfile(out var moduleProfile)
                && moduleProfile.BuildMethod == CharacterCreationBuildMethods.LifeModules)
                outcome = ConsumeCurrentLifeModuleMagic1983(current, catalog, source, ref state);
            else
                outcome = ExecuteCurrentMagic1949(current, catalog, source, root, selected, out state, careerEffects);
            RequireTalents1949(outcome is not null, "current-native-magic-operation-unresolved");
            if (careerEffects is not null)
                outcome = ForwardRuleEffects2912(new { Operation = outcome, CurrentSelectedSourceGraph = careerEffects.Digest }, outcome);
            RequireTalents1949(current.IsUnchanged, "current-binding-changed");
        }
        catch (Exception exception) when (TalentsFailure1949(exception))
        {
            blockers.Add(TalentsBlocker1949(exception));
        }
        return IssueRuleEffects2912(current, catalog, new(current, catalog, state, selected,
            outcome is null ? string.Empty : CharacterCreationFinalizationDigest.Compute(new
            {
                Schema = TalentsLoadoutGrammar1949, current.BindingDigest, current.Stage,
                Catalog = catalog.ContentDigest, Outcome = outcome
            }), blockers), outcome);
    }

    private static object ExecuteCurrentLoadout1949(CharacterCurrentRuleEnvironmentContext current,
        CharacterCurrentRuleCatalogValidation catalog, ICharacterSourceDataContext source,
        XElement root, List<string> selected, out CharacterCurrentRuleConsumptionState state,
        CurrentCareerEffects1960? careerEffects)
    {
        RequireTalents1949(TryCurrentLoadoutAuthority6218(current, source, out var authority), "loadout-authority-unavailable");
        bool weaponsDomain = catalog.Domain == CharacterCurrentRuleDomain.Weapons;
        string container = weaponsDomain ? "weapons" : "armors", kind = weaponsDomain ? "weapon" : "armor";
        XElement[] owned = RowsTalents1949(root, container, kind);
        CharacterSheetOwnerGraph? importedOwners5696 = careerEffects?.Imported?.Owners;
        bool importedStock5696 = importedOwners5696 is not null
            && (RowsTalents1949(root, "weapons", "weapon").Length != 0 || RowsTalents1949(root, "armors", "armor").Length != 0);
        bool importedAmmunition5701 = importedStock5696
            && RowsTalents1949(root, "weapons", "weapon").Any(row => row.Elements("clips").Any(clips => clips.HasElements));
        IReadOnlyList<CharacterWeaponFireState> importedFire5701 = [];
        if (importedStock5696)
        {
            RequireTalents1949(importedOwners5696!.MatchesCurrentStockLoadout5696(root), "loadout-imported-stock-owner-kernel-unavailable");
            if (importedAmmunition5701)
            {
                importedFire5701 = importedOwners5696.ProjectAmmunition5701(root, [], out var custody5701);
                RequireTalents1949(custody5701 is not null
                    && importedFire5701.Count == RowsTalents1949(root, "weapons", "weapon").Length,
                    "weapon-current-ammunition-unsupported");
            }
        }
        // Nested weapons/mods/accessories require their own current consumer. An empty
        // top-level inventory cannot conceal mounted or granted objects elsewhere.
        // Native Armor.Save also writes an <armor> rating scalar directly under
        // each owner. It and a source-proved CritterPower armor bonus are not
        // nested inventory owners; all other occurrences still are.
        RequireTalents1949(root.Descendants(kind)
            .Where(node => importedOwners5696?.IsCurrentCritterArmorBonus5925(root, node) != true)
            .Where(node => weaponsDomain
            || node.Parent is null || !owned.Contains(node.Parent) || node.HasAttributes || node.HasElements)
            .SequenceEqual(owned), "nested-loadout-unsupported");
        foreach (var row in owned)
        {
            RequireTalents1949(!row.Elements().Any(node => node.HasElements && !(importedStock5696 && weaponsDomain && node.Name == "clips"))
                && !row.HasAttributes, "loadout-children-unsupported");
            AddCurrentKey1949(catalog, SourceGuidTalents1949(row), selected);
        }
        var auxiliary = current.Current.Document.AuxiliaryState;
        var draft = current.Stage == CharacterCurrentRuleStage.Creation ? auxiliary.CharacterCreationLoadoutDraft : null;
        RequireTalents1949(draft is null || owned.Length == 0, "loadout-pending-and-applied-ambiguous");
        CharacterCreationLoadoutSelections selections;
        if (draft is not null)
        {
            RequireTalents1949(draft.WorkspaceId == current.Current.Id
                && draft.BaseContentRevision > 0 && draft.BaseContentRevision < current.Current.ContentRevision
                && draft.RawCharacterXmlDigest == CharacterCreationFinalizationDigest.ComputeUtf8(current.CharacterXml)
                && CharacterCreationLoadoutReceiptLedgerIntegrity.IsValidLedger(current.Current.Id, current.Current.ContentRevision,
                    draft, auxiliary.CharacterCreationLoadoutReceipts)
                && CharacterCreationLoadoutReceiptLedgerIntegrity.HasFunding(draft, auxiliary.CharacterCreationResourcesDraft,
                    auxiliary.CharacterCreationResourcesReceipts),
                "loadout-draft-binding-changed");
            RequireTalents1949(CharacterCreationLoadoutRules.TryProjectBasket(draft.Selections, authority,
                out _, out decimal currentPurchaseCost, out _) && currentPurchaseCost == draft.PurchaseCost,
                "loadout-current-funding-recheck-required");
            selections = weaponsDomain ? new(draft.Selections.Weapons, []) : new([], draft.Selections.Armor);
            foreach (string id in selections.Weapons.Select(row => row.OptionId).Concat(selections.Armor.Select(row => row.OptionId)))
            {
                var option = authority.Options.SingleOrDefault(row => row.OptionId == id);
                RequireTalents1949(option is not null && option.Kind == kind, "loadout-pending-source-missing");
                AddCurrentKey1949(catalog, option!.SourceId, selected);
            }
        }
        else
        {
            var groups = owned.GroupBy(SourceGuidTalents1949).ToArray();
            selections = weaponsDomain
                ? new(groups.Select(group => new CharacterCreationWeaponSelection($"weapon:{group.Key:D}", group.Count())).ToArray(), [])
                : new([], groups.Select(group => new CharacterCreationArmorSelection($"armor:{group.Key:D}", group.Count())).ToArray());
        }
        RequireTalents1949(TryCurrentLoadoutBasket6218(current, source, selections, authority,
            out var lines, out decimal value, out var quoteBlockers) && quoteBlockers.Length == 0,
            "loadout-current-quote-unavailable");
        XElement[] compiledWeapons = [], compiledArmor = [];
        if (owned.Length != 0)
        {
            var instances = owned.GroupBy(SourceGuidTalents1949).Select(group =>
                new CharacterCreationLoadoutInstance(kind + ":" + group.Key.ToString("D"), group.Select(row =>
                {
                    RequireTalents1949(Guid.TryParse(TextTalents1949(row, "guid"), out Guid id) && id != Guid.Empty,
                        "loadout-current-instance-invalid");
                    return id;
                }).ToArray())).ToArray();
            RequireTalents1949(CharacterCreationLoadoutSourceProjector.TryBuildGraph(lines, current.BindingDigest,
                out compiledWeapons, out compiledArmor, instances), "loadout-current-source-compiler-unavailable");
            foreach (var row in owned)
            {
                var compiled = (weaponsDomain ? compiledWeapons : compiledArmor)
                    .Single(item => Guid.Parse(TextTalents1949(item, "guid")) == Guid.Parse(TextTalents1949(row, "guid")));
                ValidateCurrentLoadoutShape1949(row, compiled, weaponsDomain, importedStock5696);
            }
        }
        // This is a current source-backed value/legality evaluation, never a demand to
        // purchase every catalog row, and never a write or a completed-runner projection.
        var armorDamage = new List<object>();
        var armorCapacity = new List<StockArmorCapacity3844>();
        var derivedLoadout = new List<CharacterRuleEffectValue>();
        var weaponFire = new List<CharacterWeaponFireState>();
        // V9 only: reuse the SAME privately issued sheet owner/effect compiler,
        // plus the existing stock-source reconciliation and calculation kernel.
        // Nonempty inventories remain actual saved owners, never empty baskets.
        if (importedStock5696)
        {
            RequireTalents1949(CharacterCreationOriginalLoadout3478.TryEvaluateImported5696(root, importedOwners5696!, out _),
                "loadout-imported-stock-owner-kernel-unavailable");
            // These are character channels from the reconciled sheet; e.g. racial
            // Armor must not be added to the item's degradation maximum below.
            foreach (string channel in new[] { "ArmorModifier", "DamageResistanceModifier", "Reach" })
                derivedLoadout.Add(CharacterRuleEffectValues2912.Number("runner", channel, CharacterRuleEffectMetric.Pool,
                    careerEffects!.Imported!.Sheet.OtherPoolsAndModifiers[channel], channel));
        }
        if (weaponsDomain && current.Stage == CharacterCurrentRuleStage.Career)
        foreach (var row in owned)
        {
            var line = lines.Single(item => item.SourceId == SourceGuidTalents1949(row));
            XElement definition = ParseTalents1949(line.SourceNodeXml);
            Guid instanceId = Guid.Parse(TextTalents1949(row, "guid"));
            CharacterWeaponFireState fire;
            if (importedAmmunition5701)
                fire = importedFire5701.Single(fireState5701 => fireState5701.Identity.WeaponId == instanceId);
            else
            {
                RequireTalents1949(instanceId != Guid.Empty && IntTalents1949(row, "ammoremaining", 0) == 0
                    && !row.Descendants("clip").Any(), "weapon-current-ammunition-unsupported");
                var fireSource = new CharacterWeaponFireSource(TextTalents1949(definition, "type"),
                    TextTalents1949(definition, "ammo"), TextTalents1949(definition, "mode"),
                    RequiredBoolTalents1949(row, "allowsingleshot"), RequiredBoolTalents1949(row, "allowshortburst"),
                    RequiredBoolTalents1949(row, "allowlongburst"), RequiredBoolTalents1949(row, "allowfullburst"),
                    RequiredBoolTalents1949(row, "allowsuppressive"),
                    IntTalents1949(row, "singleshot", 0), IntTalents1949(row, "shortburst", 0),
                    IntTalents1949(row, "longburst", 0), IntTalents1949(row, "fullburst", 0), IntTalents1949(row, "suppressive", 0), []);
                RequireTalents1949(CharacterWeaponFireRules.TryCreateState(
                    new(instanceId, IntTalents1949(row, "activeammoslot", 0), Guid.Empty), true,
                    line.Name, 0, null, fireSource, false, out fire), "weapon-current-fire-kernel-unavailable");
            }
            weaponFire.Add(fire);
            derivedLoadout.Add(CharacterRuleEffectValues2912.Number(instanceId.ToString("D"), line.Name, CharacterRuleEffectMetric.AmmoRemaining, fire.AmmoRemaining));
            foreach (var mode in fire.Modes)
                derivedLoadout.Add(CharacterRuleEffectValues2912.Number(instanceId.ToString("D"), line.Name, CharacterRuleEffectMetric.RoundsPerAction, mode.Rounds, mode.Mode.ToString()));
        }
        if (!weaponsDomain)
        foreach (var row in owned)
        {
            var line = lines.Single(item => item.SourceId == SourceGuidTalents1949(row));
            XElement definition = ParseTalents1949(line.SourceNodeXml);
            RequireTalents1949(CharacterArmorDamageRules.TryCalculateMaximum(TextTalents1949(definition, "armor"),
                TextTalents1949(definition, "armoroverride", ""), IntTalents1949(row, "rating", 0), [], out int maximum),
                "armor-current-damage-unavailable");
            int damage = IntTalents1949(row, "damage", 0);
            RequireTalents1949(damage >= 0 && damage <= maximum, "armor-current-damage-invalid");
            armorDamage.Add(new { line.SourceId, Maximum = maximum, Damage = damage,
                CanRepair = CharacterArmorDamageRules.CanRepair(damage),
                CanDegrade = CharacterArmorDamageRules.CanDegrade(damage, maximum) });
            string instance = Guid.Parse(TextTalents1949(row, "guid")).ToString("D");
            // Current source projection and saved-shape validation bind each
            // exact owner on both Creation and Career. A Creation-only original
            // inventory proof or saved capacity total is not authority here.
            var capacity = CalculateStockArmorCapacity3844(row, definition, line.SourceId);
            armorCapacity.Add(capacity);
            derivedLoadout.Add(CharacterRuleEffectValues2912.Text(instance, line.Name, CharacterRuleEffectMetric.Selected,
                line.SourceId.ToString("D"), "armor-owner-source"));
            derivedLoadout.Add(CharacterRuleEffectValues2912.Number(instance, line.Name, CharacterRuleEffectMetric.ArmorCapacity,
                capacity.Remaining, "remaining-stock-armor", capacity.UnavailableReason));
            derivedLoadout.Add(CharacterRuleEffectValues2912.Number(instance, line.Name, CharacterRuleEffectMetric.CapacityUsed,
                capacity.Remaining.HasValue ? 0m : null, "stock-armor", capacity.UnavailableReason));
            derivedLoadout.Add(CharacterRuleEffectValues2912.Number(instance, line.Name, CharacterRuleEffectMetric.Maximum, maximum, "armor-damage"));
            derivedLoadout.Add(CharacterRuleEffectValues2912.Number(instance, line.Name, CharacterRuleEffectMetric.Damage, damage, "armor-damage"));
            derivedLoadout.Add(CharacterRuleEffectValues2912.Flag(instance, line.Name, CharacterRuleEffectMetric.Available, CharacterArmorDamageRules.CanRepair(damage), "repair"));
            derivedLoadout.Add(CharacterRuleEffectValues2912.Flag(instance, line.Name, CharacterRuleEffectMetric.Available, CharacterArmorDamageRules.CanDegrade(damage, maximum), "degrade"));
        }
        if (armorCapacity.Count != 0)
            derivedLoadout.Add(CharacterRuleEffectValues2912.Number("stock-armor", "Stock armor remaining capacity",
                CharacterRuleEffectMetric.ArmorCapacity,
                armorCapacity.All(row => row.Remaining.HasValue) ? armorCapacity.Sum(row => row.Remaining!.Value) : null,
                "remaining-total", "Not every saved armor owner has a source-proven stock capacity."));

        // Effects and global native branches need their own canonical execution.
        // V9's populated path above requires full owner/source reconciliation and
        // the existing stock kernel; unknown combat/capacity effects still refuse.
        // An actually empty weapon/armor selection has no item cost, damage or
        // fire target. Its effects still require the source-compiled graph, and
        // configured global branches below still run against that whole graph.
        RequireTalents1949(RowsTalents1949(root, "improvements", "improvement").Length == 0
            || careerEffects is not null && owned.Length == 0 || importedStock5696,
            "loadout-current-effects-require-kernel");
        RequireTalents1949(source.TryResolveCareerSettingsProfile(out var settingsXml, out var settingsState),
            "loadout-current-native-policy-unavailable");
        // The source adapter preserves the original profile children verbatim. This
        // wrapper adaptation does not synthesize a Creation draft or change Career.
        var settings = ParseTalents1949(settingsXml);
        RequireTalents1949(settings.Name == "settings", "loadout-current-native-policy-unavailable");
        var profile = new XElement("setting", settings.Nodes());
        string[] configured = CharacterCreationNativeLoadoutDefinitions1432.All
            .Select(item => item.Element).Where(name => profile.Elements().Any(node =>
                string.Equals(node.Name.LocalName, name, StringComparison.OrdinalIgnoreCase))).ToArray();
        object native;
        if (configured.Length == 0)
            native = new { Settings = settingsState, ConfiguredNativeLoadoutSettings = configured };
        else
        {
            RequireTalents1949(CharacterCreationNativeLoadout1432.TryReadPolicy(profile, out var policy),
                "loadout-current-native-policy-unavailable");
            var evaluated = CharacterCreationNativeLoadout1432.EvaluateGraph(root, policy!,
                current.Stage == CharacterCurrentRuleStage.Career, authority.SettingsProfileId,
                authority.ProfileDigest, CharacterCreationFinalizationDigest.Compute(profile.ToString(SaveOptions.DisableFormatting)),
                importedOwners5696: importedStock5696 ? importedOwners5696 : null,
                sourceOwners5925: importedOwners5696);
            // Gear-only Career characters have no weapon/armor stock input.
            // V7 Priority also has no imported-career effect compiler. Obtain
            // its actual retained sheet owner proof before using stock capacity;
            // a saved capacity value or a native policy flag is not that proof.
            // Previously supported branches retain their exact execution bytes.
            if (!importedStock5696 && policy!.EnforceCapacity == true
                && evaluated.CapacityBranch == CharacterCreationCapacityBranch1432.Unsupported)
            {
                var capacityOwners = importedOwners5696
                    ?? CharacterSheetDerivedQuery.ResolveCurrentNativeOwners7846(current,
                        new CreationExactSource1949(current.CharacterXml, source));
                if (capacityOwners is not null)
                {
                    // The stock proof consumes the complete source-compiled effect
                    // graph, as on the imported Career path. Saved improvements
                    // (including an absent container) are not that calculation.
                    // Keep every owner and let the existing whole-graph fence
                    // verify this detached tree before and after calculation.
                    var capacityGraph = new XElement(root);
                    capacityGraph.Elements("improvements").Remove();
                    capacityGraph.Add(new XElement("improvements", capacityOwners.Effects.Select(row => new XElement(row))));
                    evaluated = CharacterCreationNativeLoadout1432.EvaluateGraph(capacityGraph, policy,
                        current.Stage == CharacterCurrentRuleStage.Career, authority.SettingsProfileId,
                        authority.ProfileDigest, CharacterCreationFinalizationDigest.Compute(profile.ToString(SaveOptions.DisableFormatting)),
                        importedOwners5696: capacityOwners, sourceOwners5925: capacityOwners);
                }
            }
            RequireTalents1949(configured.All(evaluated.Supports), "loadout-current-native-branch-unsupported");
            native = evaluated;
        }
        state = selected.Count == 0 && configured.Length == 0 && draft is null
            ? CharacterCurrentRuleConsumptionState.NotConfigured : CharacterCurrentRuleConsumptionState.Evaluated;
        return TagRuleEffects2912(new { authority.AuthorityDigest, Lines = lines, CurrentValue = value,
            CompiledCurrentWeapons = compiledWeapons.Select(row => row.ToString(SaveOptions.DisableFormatting)).ToArray(),
            CompiledCurrentArmor = compiledArmor.Select(row => row.ToString(SaveOptions.DisableFormatting)).ToArray(),
            ArmorDamage = armorDamage, WeaponFire = weaponFire, Native = native },
            CharacterRuleEffectValues2912.Loadout(lines, value, native).Concat(derivedLoadout),
            "Replacement prices, native loadout branches and source-proven stock armor capacity are calculated where available. Weapon attack pools, armor stacking and modified, gear or ware capacity totals remain unavailable.");
    }

    private sealed record StockArmorCapacity3844(Guid OwnerId, Guid SourceId, decimal? Remaining, string? UnavailableReason);

    // Called only after the live authority, source factory and saved owner
    // reconciliation above. Native Armor.TotalArmorCapacity -> CalculatedCapacity
    // -> CapacityRemaining equals the explicit source integer for this stock
    // branch: no mods, included items or attached gear. Damage/equipped state do
    // not change that branch. Expressions and missing values never imply zero.
    private static StockArmorCapacity3844 CalculateStockArmorCapacity3844(XElement saved, XElement source, Guid sourceId)
    {
        Guid ownerId = Guid.Parse(TextTalents1949(saved, "guid"));
        string? reason = null;
        decimal? remaining = null;
        string declared = TextTalents1949(source, "armorcapacity", "");
        if (new[] { "mods", "armormods", "gears", "gear" }.Any(name =>
            source.Elements(name).Concat(saved.Elements(name)).Any(node => node.HasAttributes || node.HasElements || !string.IsNullOrWhiteSpace(node.Value))))
            reason = "Armor capacity for modifications or attached gear is not calculated.";
        else if (!int.TryParse(declared, NumberStyles.None, CultureInfo.InvariantCulture, out int capacity))
            reason = "Armor capacity requires an explicit nonnegative integer source; missing values and expressions are not calculated.";
        else if (TextTalents1949(saved, "armorcapacity", "") != declared)
            reason = "Saved armor capacity does not match the selected owner source.";
        else remaining = capacity;
        return new(ownerId, sourceId, remaining, reason);
    }

    private static void ValidateCurrentLoadoutShape1949(XElement current, XElement compiled, bool weapon, bool provenStock5701 = false)
    {
        // Source-backed fields are recomputed above; persisted values can legitimately
        // predate an amendment. Mutable current fields below have dedicated kernels or
        // are presentation/selection state. All other fields must remain canonical.
        string[] recomputed = ["name", "category", "avail", "cost", "weight", "source", "page", "armor", "armorcapacity",
            "type", "spec", "spec2", "reach", "ap", "mode", "rc", "ammo", "ammocategory", "accuracy", "conceal",
            "useskill", "useskillspec", "range", "alternaterange", "sizecategory", "weapontype"];
        string[] mutable = ["notes", "notesColor", "location", "equipped", "stolen", "sortorder", "damage",
            "allowsingleshot", "allowshortburst", "allowlongburst", "allowfullburst", "allowsuppressive",
            "singleshot", "shortburst", "longburst", "fullburst", "suppressive", "activeammoslot"];
        RequireTalents1949(current.Elements().GroupBy(node => node.Name).All(group => group.Count() == 1)
            && IntTalents1949(current, "rating", 0) == 0, "loadout-current-shape-unsupported");
        foreach (var field in current.Elements())
        {
            if (weapon && field.Name == "ammoremaining")
            {
                RequireTalents1949(field.Value == "0", "weapon-current-ammunition-unsupported");
                continue;
            }
            if (weapon && field.Name == "clips")
            {
                // The SAME private stock proof above already checked the complete
                // clip shape, actual ammunition source/owner and conserved quantities.
                if (provenStock5701) continue;
                RequireTalents1949(!field.HasAttributes && !field.HasElements && string.IsNullOrWhiteSpace(field.Value),
                    "weapon-current-ammunition-unsupported");
                continue;
            }
            var expected = compiled.Element(field.Name);
            RequireTalents1949(expected is not null && !field.HasAttributes && !field.HasElements,
                "loadout-current-field-unsupported");
            RequireTalents1949(recomputed.Contains(field.Name.LocalName) || mutable.Contains(field.Name.LocalName)
                || XNode.DeepEquals(field, expected), "loadout-current-effect-unsupported");
        }
    }

    private static object ExecuteCurrentMagic1949(CharacterCurrentRuleEnvironmentContext current,
        CharacterCurrentRuleCatalogValidation catalog, ICharacterSourceDataContext source,
        XElement root, List<string> selected, out CharacterCurrentRuleConsumptionState state,
        CurrentCareerEffects1960? careerEffects)
    {
        var resolved = ResolveCurrentMagicCatalog1983(current, catalog.Domain, source);
        var options = resolved.Options;
        var auxiliary = current.Current.Document.AuxiliaryState;
        var draft = current.Stage == CharacterCurrentRuleStage.Creation ? auxiliary.CharacterCreationMagicResonanceDraft : null;
        var choices = new List<(Guid Id, int Levels)>();
        var owned = CurrentMagicRows1949(root, catalog.Domain);
        foreach (var row in owned)
            choices.Add((SourceGuidTalents1949(row), catalog.Domain == CharacterCurrentRuleDomain.Powers
                ? IntTalents1949(row, "rating", 1) : 1));
        RequireTalents1949(draft is null || owned.Length == 0, "magic-pending-and-applied-ambiguous");
        if (current.Stage == CharacterCurrentRuleStage.Creation)
            RequireTalents1949(RowsTalents1949(root, "improvements", "improvement").Length == 0,
                "magic-current-effects-require-kernel");
        if (draft is not null)
        {
            RequireTalents1949(draft.WorkspaceId == current.Current.Id
                && draft.BaseContentRevision > 0 && draft.BaseContentRevision < current.Current.ContentRevision
                && draft.BaseRawCharacterXmlDigest == CharacterCreationFinalizationDigest.ComputeUtf8(current.CharacterXml)
                && draft.DraftDigest == CharacterCreationMagicResonanceDraftIntegrity.ComputeDigest(draft)
                && CharacterCreationMagicResonanceDraftIntegrity.IsValidReceiptLedger(
                    auxiliary.CharacterCreationMagicResonanceReceipts, current.Current.Id, current.Current.ContentRevision)
                && auxiliary.CharacterCreationMagicResonanceReceipts?.LastOrDefault() is { } lastReceipt
                && lastReceipt.DraftDigest == draft.DraftDigest && lastReceipt.DraftRevision == draft.DraftRevision,
                "magic-draft-binding-changed");
            var selections = draft.Selections;
            IEnumerable<(CharacterCreationMagicResonanceOptionIdentity Identity, int Levels)> pending = catalog.Domain switch
            {
                CharacterCurrentRuleDomain.Traditions => selections.Tradition is { } tradition ? [(tradition, 1)] : [],
                CharacterCurrentRuleDomain.Streams => selections.Stream is { } stream ? [(stream, 1)] : [],
                CharacterCurrentRuleDomain.Powers => selections.AdeptPowers.Select(item => (item.Identity, item.Levels)),
                CharacterCurrentRuleDomain.Spells => selections.Spells.Select(item => (item, 1)),
                CharacterCurrentRuleDomain.ComplexForms => selections.ComplexForms.Select(item => (item, 1)),
                _ => []
            };
            foreach (var choice in pending)
            {
                RequireTalents1949(Guid.TryParseExact(choice.Identity.SourceId, "D", out Guid id) && id != Guid.Empty,
                    "magic-pending-identity-invalid");
                choices.Add((id, choice.Levels));
            }
        }
        RequireTalents1949(current.Stage == CharacterCurrentRuleStage.Career && catalog.Domain == CharacterCurrentRuleDomain.Spells
            || choices.Select(item => item.Id).Distinct().Count() == choices.Count, "magic-selections-ambiguous");
        var projections = new List<CharacterCreationMagicResonanceOptionFinalizationSource>();
        foreach (var choice in choices)
        {
            AddCurrentKey1949(catalog, choice.Id, selected);
            if (current.Stage == CharacterCurrentRuleStage.Career && catalog.Domain == CharacterCurrentRuleDomain.Powers)
                continue; // Actual source/effect/grant compilation belongs to EvaluateRead below.
            var matches = options.Where(option => option.Identity.SourceId == choice.Id.ToString("D")).ToArray();
            // A Career grant may have zero paid levels. The payload projection only
            // validates syntax; EvaluateRead below computes actual paid/free levels.
            int payloadLevels = current.Stage == CharacterCurrentRuleStage.Career
                && catalog.Domain == CharacterCurrentRuleDomain.Powers && choice.Levels == 0 ? 1 : choice.Levels;
            RequireTalents1949(matches.Length == 1
                && CharacterCreationMagicResonanceFinalizationRules.TryProjectOption(matches[0], payloadLevels, out _),
                "magic-current-source-unavailable");
            CharacterCreationMagicResonanceFinalizationRules.TryProjectOption(matches[0], payloadLevels, out var projection);
            projections.Add(projection!);
        }
        var derivedMagic = new List<CharacterRuleEffectValue>(CharacterRuleEffectValues2912.Magic(projections));
        object? canonical = null;
        bool selectionKernelExecuted = false;
        if (current.Stage == CharacterCurrentRuleStage.Career && catalog.Domain == CharacterCurrentRuleDomain.Powers
            && BoolTalents1949(root, "adept", false))
        {
            var evaluated = careerEffects?.Imported?.Owners.CareerPowers5715
                ?? CharacterCareerPowerGraph.EvaluateRead(current.Current, source);
            RequireTalents1949(evaluated.Blockers.Count == 0, "power-current-kernel-unavailable");
            canonical = evaluated;
            derivedMagic.AddRange(PowerEffects2912(evaluated));
            selectionKernelExecuted = true;
        }
        else if (current.Stage == CharacterCurrentRuleStage.Creation && draft is not null)
        {
            var authority = resolved.PriorityAuthority;
            RequireTalents1949(authority is not null, "magic-current-priority-authority-unavailable");
            var prerequisite = auxiliary.CharacterCreationPrerequisiteDraft;
            var attributes = auxiliary.CharacterCreationAttributesDraft;
            RequireTalents1949(prerequisite is not null && attributes is not null
                && draft.PrerequisiteDraftDigest == prerequisite.DraftDigest
                && draft.PrerequisiteDraftRevision == prerequisite.DraftRevision
                && draft.AttributesDraftDigest == attributes.DraftDigest
                && draft.AttributesDraftRevision == attributes.DraftRevision,
                "magic-current-prerequisites-unavailable");
            var talent = authority!.Talents.SingleOrDefault(item => item.Identity == draft.TalentIdentity);
            RequireTalents1949(talent is not null, "magic-current-talent-unavailable");
            RequireTalents1949(CharacterCreationMagicResonanceFinalizationRules.TryCreate(
                CharacterCreationFinalizationDigest.ComputeUtf8(current.CharacterXml),
                draft.PrerequisiteDraftRevision, draft.PrerequisiteDraftDigest,
                draft.AttributesDraftRevision, draft.AttributesDraftDigest, authority, talent!, draft.Selections,
                attributes, out var contribution, out var failures) && failures.Length == 0,
                "magic-current-creation-kernel-unavailable");
            canonical = contribution;
            selectionKernelExecuted = true;
        }
        else if (current.Stage == CharacterCurrentRuleStage.Career
            && catalog.Domain is CharacterCurrentRuleDomain.Spells or CharacterCurrentRuleDomain.ComplexForms
            && (choices.Count != 0 || BoolTalents1949(root,
                catalog.Domain == CharacterCurrentRuleDomain.Spells ? "magician" : "technomancer", false)))
        {
            foreach (var row in owned) ValidateCurrentLearnedMagicShape1949(catalog.Domain, row, UsesNativePriorityCareer9569(current));
            RequireTalents1949(owned.Select(row => TextTalents1949(row, "guid")).Distinct(StringComparer.OrdinalIgnoreCase).Count() == owned.Length,
                "current-learned-magic-instance-ambiguous");
            // Each current selection is independently joined to its retained source;
            // no learning request, fake owner, balance or future purchase is created.
            var evaluated = choices.Select(choice => EvaluateCurrentLearnedMagic1949(
                current, catalog.Domain, source, root, choice.Id, careerEffects?.Imported?.Read)).ToArray();
            foreach (var learned in evaluated)
            {
                derivedMagic.Add(CharacterRuleEffectValues2912.Number(learned.SourceId.ToString("D"), learned.Kind, CharacterRuleEffectMetric.RuleKarmaCost, learned.RuleKarmaCost, "learn-rule-cost"));
                derivedMagic.Add(CharacterRuleEffectValues2912.Number(learned.SourceId.ToString("D"), learned.Kind,
                    catalog.Domain == CharacterCurrentRuleDomain.Spells ? CharacterRuleEffectMetric.Magic : CharacterRuleEffectMetric.Resonance, learned.EffectiveAttribute));
            }
            object currentAttribute;
            if (catalog.Domain == CharacterCurrentRuleDomain.Spells)
            {
                var magicSettings = CurrentTalentsSettings1949(root, source, out _);
                var magic = CurrentMagic5699(current, root, magicSettings, source);
                RequireTalents1949(magic.IsSupported && magic.IsMagician && magic.Magic > 0, "spell-current-magic-unavailable");
                if (!UsesOwnedPriorityCareer9569(current)) AuthenticateCurrentSpellEffects1949(root, source);
                currentAttribute = magic;
            }
            else
            {
                var resonance = CurrentResonance5699(current, root, source);
                RequireTalents1949(resonance.IsSupported && resonance.IsTechnomancer && resonance.EffectiveResonance > 0,
                    "form-current-resonance-unavailable");
                currentAttribute = resonance;
            }
            canonical = new { CurrentAttribute = currentAttribute, Known = evaluated };
            selectionKernelExecuted = true;
        }
        else if (choices.Count != 0 && catalog.Domain is CharacterCurrentRuleDomain.Traditions or CharacterCurrentRuleDomain.Streams)
        {
            RequireTalents1949(owned.Length == 1 && choices.Count == 1, "current-tradition-ambiguous");
            // Imported selected entities already passed complete owner/source and effect proof.
            // This domain evaluates the tradition selector and drain, never creature combat stats.
            RequireTalents1949(RowsTalents1949(root, "spirits", "spirit").Length == 0
                || careerEffects?.Imported is not null, "current-referenced-spirit-source-proof-unavailable");
            canonical = EvaluateCurrentTradition1949(current, catalog.Domain, source, root, owned[0], projections[0],
                careerEffects: careerEffects);
            var tradition = (CharacterCurrentTraditionQuote1949)canonical;
            derivedMagic.Add(CharacterRuleEffectValues2912.Number(tradition.Option.Identity.ToString(), tradition.Option.Name,
                catalog.Domain == CharacterCurrentRuleDomain.Streams ? CharacterRuleEffectMetric.FadePool : CharacterRuleEffectMetric.DrainPool, tradition.DrainPool));
            selectionKernelExecuted = true;
        }
        else
        {
            RequireTalents1949(choices.Count == 0, "magic-current-selected-kernel-extension-required");
            string? sustainedAuthority = null;
            // This evidence authorizes only this empty Career magic selection.
            // Never promote it to native Career effects or admit an unrelated
            // improvement graph merely because the collection is absent/empty.
            if (careerEffects is null && current.Stage == CharacterCurrentRuleStage.Career
                && TextTalents1949(root, "buildmethod", "") is
                    CharacterCreationBuildMethods.Priority or CharacterCreationBuildMethods.SumToTen)
                RequireTalents1949(TrySustainedMovement5080(current, root, out sustainedAuthority),
                    "magic-current-sustained-source-unavailable");
            RequireTalents1949(RowsTalents1949(root, "improvements", "improvement").Length == 0
                || careerEffects is not null || sustainedAuthority is not null,
                "magic-current-effects-require-kernel");
            // A source-backed empty selection is real evidence for incomplete Creation.
            // It is not a successful execution of an omitted selected-state kernel.
            canonical = new { EmptySelection = true, resolved.AuthorityDigest };
            if (careerEffects is not null)
            {
                canonical = new { EmptyCurrentSelection = true, resolved.AuthorityDigest,
                    CurrentSelectedSourceGraph = careerEffects.Digest };
                selectionKernelExecuted = true;
            }
            else if (sustainedAuthority is not null)
            {
                canonical = new { EmptyCurrentSelection = true, resolved.AuthorityDigest,
                    SustainedSourceGraph = sustainedAuthority };
                selectionKernelExecuted = true;
            }
        }
        RequireTalents1949(source.TryResolveCareerSettingsProfile(out var settingsXml, out var settingsState),
            "magic-current-native-policy-unavailable");
        var settings = ParseTalents1949(settingsXml);
        RequireTalents1949(settings.Name == "settings", "magic-current-native-policy-unavailable");
        string[] configured = CharacterCreationNativeMagicDefinitions1430.All
            .Select(item => item.Element).Where(name => settings.Elements().Any(node =>
                string.Equals(node.Name.LocalName, name, StringComparison.OrdinalIgnoreCase))).ToArray();
        RequireTalents1949(configured.All(name => settings.Elements(name).Count() == 1), "magic-current-native-policy-invalid");
        object? native = configured.Length == 0
            ? new { Settings = settingsState, ConfiguredNativeMagicSettings = configured } : null;
        var attributeContainers = root.Elements("attributes").ToArray();
        bool missingCreationGraph = current.Stage == CharacterCurrentRuleStage.Creation && choices.Count == 0 && draft is null
            && auxiliary.CharacterCreationAttributesDraft is null && attributeContainers.Length <= 1
            && attributeContainers.All(node => !node.HasAttributes && !node.HasElements && string.IsNullOrWhiteSpace(node.Value))
            && IntTalents1949(root, "initiategrade", 0) == 0 && IntTalents1949(root, "submersiongrade", 0) == 0
            && new[] { "arts", "metamagics", "critterpowers", "sustainedobjects", "spirits" }
                .All(name => !root.Elements(name).Any(node => node.HasElements))
            && !BoolTalents1949(root, "magenabled", false) && !BoolTalents1949(root, "resenabled", false);
        if (configured.Length != 0 && missingCreationGraph)
        {
            var policy = resolved.NativePolicy is { } nativePolicy
                ? CharacterCreationNativeMagic1430.ReadCurrentPolicy(nativePolicy, current)
                : CharacterCreationNativeMagic1430.ReadCurrentPolicy(resolved.PriorityAuthority!.MysticAdeptPowerPointPolicy,
                    TextTalents1949(root, "settings"), TextTalents1949(root, "buildmethod"),
                    ImportedSettingsFile5691(current, source));
            RequireTalents1949(policy is not null, "magic-current-native-policy-unavailable");
            native = new { Settings = settingsState, Policy = policy, MissingCreationAttributeGraph = true };
        }
        else if (configured.Length != 0)
        {
            bool attributeBranch = configured.Any(name => name is "boundspiritexpression" or "registeredspriteexpression")
                || configured.Contains("spiritforcebasedontotalmag") && BoolTalents1949(root, "magenabled", false)
                || configured.Contains("ignorecomplexformlimit") && BoolTalents1949(root, "resenabled", false)
                || configured.Contains("freespiritpowerpointsmag") && TextTalents1949(root, "metatype", "") == "Free Spirit";
            if (attributeBranch) ValidateCurrentNaturalAttributeSources1949(current, root, source, careerEffects);
            native = careerEffects?.Imported is { } imported
                ? CharacterCreationNativeMagic1430.EvaluateImportedCurrent5699(root,
                    resolved.PriorityAuthority!.MysticAdeptPowerPointPolicy, imported.Read,
                    ImportedSettingsFile5691(current, source))
                : resolved.NativePolicy is { } nativePolicy
                ? CharacterCreationNativeMagic1430.EvaluateCurrent(root, nativePolicy, current, source)
                : CharacterCreationNativeMagic1430.EvaluateCurrent(root, resolved.PriorityAuthority!.MysticAdeptPowerPointPolicy,
                    TextTalents1949(root, "settings"), TextTalents1949(root, "buildmethod"), source);
        }
        RequireTalents1949(native is not null, "magic-current-native-policy-unavailable");
        state = choices.Count == 0 && (configured.Length == 0 || missingCreationGraph) && !selectionKernelExecuted
            ? CharacterCurrentRuleConsumptionState.NotConfigured : CharacterCurrentRuleConsumptionState.Evaluated;
        return TagRuleEffects2912(new { resolved.AuthorityDigest, Projections = projections, Canonical = canonical, Native = native }, derivedMagic,
            "Selected option, current power, learned magic and drain/fade values are calculated where configured. Other native magic constraints remain separately scoped.");
    }

    private static XElement CurrentTalentsSettings1949(XElement root, ICharacterSourceDataContext source, out string sourceState)
    {
        RequireTalents1949(source.TryResolveCareerSettingsProfile(out var xml, out sourceState)
            && !string.IsNullOrWhiteSpace(sourceState), "current-settings-unavailable");
        var settings = ParseTalents1949(xml);
        RequireTalents1949(settings.Name == "settings" && MatchesSettingsRow5691(source, settings, TextTalents1949(root, "settings")),
            "current-settings-binding-changed");
        return settings;
    }

    private static CharacterCurrentTraditionQuote1949 EvaluateCurrentTradition1949(
        CharacterCurrentRuleEnvironmentContext current, CharacterCurrentRuleDomain domain,
        ICharacterSourceDataContext source, XElement root, XElement owned,
        CharacterCreationMagicResonanceOptionFinalizationSource projection,
        CharacterCreationMagicResonanceFinalizationContribution? creation = null,
        CurrentCareerEffects1960? careerEffects = null)
    {
        bool stream = domain == CharacterCurrentRuleDomain.Streams;
        string entityType = stream ? "Sprite" : "Spirit";
        var definition = ParseTalents1949(projection.CanonicalSourceXml);
        RequireTalents1949(BoolTalents1949(root, stream ? "resenabled" : "magenabled", false),
            "current-tradition-talent-unconfigured");
        ValidateCurrentNaturalAttributeSources1949(current, root, source, careerEffects);
        int drainPool;
        bool drainResolved = careerEffects?.Imported is { } imported
            ? CharacterCreationNativeMagic1430.TryEvaluateOwnedDrain5699(definition, stream, imported.Read, out drainPool)
            : CharacterCreationNativeMagic1430.TryEvaluateCurrentDrain(root, definition, stream, out drainPool);
        RequireTalents1949(drainResolved, "current-tradition-drain-kernel-unavailable");
        RequireTalents1949(CharacterTraditionSpiritFormRules.TryRead(definition, out var spiritForm), "tradition-spirit-form-unsupported");
        RequireTalents1949(source.TryResolveTraditionSpiritNames(entityType, projection.Identity.SourceId, out var names),
            "tradition-spirit-source-unavailable");
        RequireTalents1949(!owned.HasAttributes && TextTalents1949(owned, "extra", "").Length == 0,
            "current-tradition-choice-unsupported");
        string[] traditionFields = ["sourceid", "guid", "traditiontype", "name", "extra", "spiritform", "drain", "source", "page",
            "spiritcombat", "spiritdetection", "spirithealth", "spiritillusion", "spiritmanipulation", "spirits", "bonus"];
        RequireTalents1949(owned.Elements().All(node => !node.HasAttributes && traditionFields.Contains(node.Name.LocalName)
            && (node.Name == "spirits" ? node.Elements().All(spirit => spirit.Name == "spirit" && !spirit.HasElements && !spirit.HasAttributes)
                : !node.HasElements)) && owned.Elements().GroupBy(node => node.Name).All(group => group.Count() == 1),
            "current-tradition-shape-unsupported");
        // The fixed source expression is part of the canonical option projection.
        // DrainSelector represents the editable custom override only; ordinary
        // source traditions have no such override, and no fabricated value is passed.
        CharacterTraditionDrainSemantics? drain = null;
        Guid sourceId = Guid.Parse(projection.Identity.SourceId);
        if (!stream && sourceId == CharacterTraditionNameRules.CustomMagicalTraditionSourceId)
        {
            bool hasExpressions = source.TryResolveTraditionDrainExpressions(out var expressions);
            RequireTalents1949(Guid.TryParse(TextTalents1949(owned, "guid"), out var traditionId)
                && hasExpressions, "tradition-drain-source-unavailable");
            RequireTalents1949(CharacterTraditionDrainRules.TryCreateSemantics(traditionId, sourceId, "MAG",
                BoolTalents1949(root, "adept", false), BoolTalents1949(root, "magician", false),
                TextTalents1949(owned, "drain", ""), expressions, out var selector), "tradition-drain-choice-unsupported");
            drain = selector;
        }
        var effects = RowsTalents1949(root, "improvements", "improvement");
        RequireTalents1949(!effects.Any(effect => ImprovementEnabledTalents1949(effect)
            && TextTalents1949(effect, "improvementttype") is "AddSpirit" or "AddSprite" or "LimitSpiritCategory"
                or "DrainResistance" or "FadingResistance"), "tradition-source-effects-require-kernel");
        var spirits = RowsTalents1949(root, "spirits", "spirit");
        var ids = spirits.Select(row => Guid.Parse(TextTalents1949(row, "guid"))).ToArray();
        var projected = new List<CharacterSpiritNameChoiceState>();
        for (int index = 0; index < spirits.Length; index++)
        {
            var spirit = spirits[index];
            if (TextTalents1949(spirit, "type") != entityType) continue;
            RequireTalents1949(CharacterSpiritNameChoiceRules.TryProject(ids[index], ids, entityType,
                TextTalents1949(spirit, "name"), names, [], BoolTalents1949(root, "magenabled", false),
                BoolTalents1949(root, "resenabled", false), [], [], out var result)
                && result is not null && CharacterSpiritNameChoiceRules.CanSet(result, TextTalents1949(spirit, "name")),
                "current-spirit-choice-unsupported");
            projected.Add(result!);
        }
        return new(projection, drain, drainPool, spiritForm, Array.AsReadOnly(names.ToArray()), Array.AsReadOnly(projected.ToArray()), creation);
    }

    private static void ValidateCurrentNaturalAttributeSources1949(CharacterCurrentRuleEnvironmentContext current,
        XElement root, ICharacterSourceDataContext source,
        CurrentCareerEffects1960? careerEffects)
    {
        if (UsesOwnedPriorityCareer9569(current))
        {
            var read = careerEffects?.Imported?.Read;
            RequireTalents1949(current.IsUnchanged && read is { IsCurrent: true } && ReferenceEquals(read.Current, current)
                && careerEffects is not null && ReferenceEquals(root, careerEffects.Graph),
                "current-imported-natural-attribute-owner-unavailable");
            // Full sheet reconciliation already checked all ranges, owners and modifiers.
            // Downstream imported kernels receive this result, not saved display totals.
            return;
        }
        RequireTalents1949(source.TryResolveCareerQualityMetatype(out var metatypeXml, out var sourceState)
            && !string.IsNullOrWhiteSpace(sourceState), "current-native-metatype-unavailable");
        var metatype = ParseTalents1949(metatypeXml);
        // Overflow changes condition capacity, not a natural attribute or drain.
        // Admit only the exact clause already compiled against the saved owner/effect
        // graph for this Career capability; unrelated metatype bonuses stay refused.
        bool compiledConditionBonus = UsesCareerCondition5497(current) && careerEffects is not null
            && careerEffects.Metatype.OptionId == TextTalents1949(metatype, "id")
            && careerEffects.Metatype.BaseBonuses?.ConditionOverflow5497 is int expectedOverflow
            && metatype.Elements("bonus").ToArray() is [var bonus] && !bonus.HasAttributes
            && bonus.Nodes().All(node => node is XElement || node is XText text
                && text.GetType() == typeof(XText) && string.IsNullOrWhiteSpace(text.Value))
            && bonus.Elements().ToArray() is [var condition]
            && CharacterCareerMetatypeConditionSource5497.TryReadOverflow(condition, out int overflow)
            && overflow == expectedOverflow;
        RequireTalents1949(TextTalents1949(root, "metatypeguid") == TextTalents1949(metatype, "id")
            && TextTalents1949(root, "metatype") == TextTalents1949(metatype, "name")
            && TextTalents1949(root, "metavariantguid", "") is "" or "00000000-0000-0000-0000-000000000000"
            && (!metatype.Elements("bonus").Any(node => node.HasElements) || compiledConditionBonus)
            && !root.Elements("cyberwares").Any(node => node.HasElements)
            && !root.Elements("biowares").Any(node => node.HasElements)
            && IntTalents1949(root, "initiategrade", 0) == 0 && IntTalents1949(root, "submersiongrade", 0) == 0,
            "current-native-attribute-source-unsupported");
        foreach (var attribute in RowsTalents1949(root, "attributes", "attribute"))
        {
            string name = TextTalents1949(attribute, "name");
            string sourceName = name == "MAGAdept" ? "mag" : name.ToLowerInvariant();
            int minimum = IntTalents1949(metatype, sourceName + "min", -2);
            int maximum = IntTalents1949(metatype, sourceName + "max", -2);
            // Native attribute quotes retain disabled MAG/RES rows with zero bounds.
            // Enabled rows still require the exact selected metatype range.
            bool disabled = name switch
            {
                "MAG" => !BoolTalents1949(root, "magenabled", false),
                "RES" => !BoolTalents1949(root, "resenabled", false),
                _ => false
            };
            RequireTalents1949(minimum >= 0 && maximum >= minimum
                && TextTalents1949(attribute, "metatypecategory", "Standard") == "Standard"
                && IntTalents1949(attribute, "metatypemin", -1) == (disabled ? 0 : minimum)
                && IntTalents1949(attribute, "metatypemax", -1) == (disabled ? 0 : maximum),
                "current-native-attribute-bound-changed");
        }
        foreach (var quality in RowsTalents1949(root, "qualities", "quality"))
        {
            RequireTalents1949(source.TryResolveCareerQualitySource(SourceGuidTalents1949(quality), out var definition)
                && definition is not null, "current-native-quality-source-unavailable");
            var node = ParseTalents1949(definition!.RawSourceXml);
            // The native compiler has already validated the exact gear sources,
            // saved instances and owner effects. addgear produces a Gear effect,
            // not a natural-attribute modifier; an uncompiled grant stays refused.
            bool compiledGearGrant = careerEffects?.NativeGrants.Any(grant =>
                grant.SourceId == SourceGuidTalents1949(quality).ToString("D")
                && grant.GrantedGearSources is { Count: > 0 }
                && SameTalentsXml1949(node, grant.CanonicalSourceXml)) == true;
            // A finalized text prompt is selection metadata, not an attribute effect.
            // Reuse its exact archive/source/owner proof; a saved bonus or extra alone
            // cannot admit it. Other bonus mechanics still pass the checks below.
            bool compiledPromptSelection = careerEffects is not null
                && node.Elements("bonus").Elements("selecttext").Any()
                && TryCurrentKarmaPromptOwners2726(current.Current, source, out var promptOwners)
                && promptOwners.TryGetValue(TextTalents1949(quality, "guid"), out var promptOwner)
                && promptOwner.SourceId == SourceGuidTalents1949(quality)
                && SameCurrentInstance1960(quality, promptOwner.Quality);
            RequireTalents1949(!node.Elements("firstlevelbonus").Any(bonus => bonus.HasElements)
                && node.Elements("bonus").SelectMany(bonus => bonus.Elements()).All(effect =>
                    effect.Name.Namespace == XNamespace.None
                    && (effect.Name == "selecttext" && compiledPromptSelection
                        || !effect.HasAttributes
                        && (effect.Name.LocalName is "enableattribute" or "enabletab" or "unlockskills" or "specificskill"
                        or "newcomplexformkarmacost" or "newcomplexformkarmacostmultiplier" or "complexformlimit"
                        or "attributekarmacost" or "attributekarmacostmultiplier" or "attributepointcost" or "attributepointcostmultiplier"
                        || effect.Name == "addgear" && compiledGearGrant))),
                "current-native-attribute-source-effects-unsupported");
        }
    }

    private static CharacterCurrentMagicRuleQuote1949 EvaluateCurrentLearnedMagic1949(
        CharacterCurrentRuleEnvironmentContext current, CharacterCurrentRuleDomain domain,
        ICharacterSourceDataContext source, XElement root, Guid id,
        CharacterSheetDerivedQuery.ImportedCareerSheet5699? retained = null)
    {
        var settings = CurrentTalentsSettings1949(root, source, out var settingsState);
        RequireTalents1949(!BoolTalents1949(root, "ignorerules", false), "current-magic-ignore-rules-unsupported");
        var karma = settings.Elements("karmacost").Single();
        bool known = CurrentMagicRows1949(root, domain).Any(row => SourceGuidTalents1949(row) == id);
        if (domain == CharacterCurrentRuleDomain.Spells)
        {
            var magic = CurrentMagic5699(current, root, settings, source);
            RequireTalents1949(magic.IsSupported && magic.IsMagician && magic.Magic > 0, "spell-current-magic-unavailable");
            var tradition = root.Elements("tradition").Single();
            Guid traditionId = SourceGuidTalents1949(tradition);
            RequireTalents1949(TextTalents1949(tradition, "traditiontype") == "MAG"
                && TextTalents1949(tradition, "extra", "").Length == 0
                && source is ICharacterCareerSpellLearningSourceContext, "spell-current-tradition-unavailable");
            RequireTalents1949(((ICharacterCareerSpellLearningSourceContext)source).TryResolveCareerSpellLearningSource(
                id, traditionId, out var resolved) && resolved is not null && !string.IsNullOrWhiteSpace(resolved.AuthorityState),
                "spell-current-source-unavailable");
            var spell = ParseTalents1949(resolved!.SpellXml);
            if (UsesNativePriorityCareer9569(current))
            {
                foreach (var saved in CurrentMagicRows1949(root, domain).Where(row => SourceGuidTalents1949(row) == id))
                {
                    foreach (string field in new[] { "name", "category", "type", "range", "damage", "duration", "dv", "useskill", "source", "page" })
                        RequireTalents1949(TextTalents1949(saved, field, "") == TextTalents1949(spell, field, ""),
                            "current-native-spell-saved-source-mismatch");
                    RequireTalents1949(TextTalents1949(saved, "descriptors", "") == TextTalents1949(spell, "descriptor", ""),
                        "current-native-spell-descriptor-source-mismatch");
                }
            }
            var traditionSource = ParseTalents1949(resolved.TraditionXml);
            var kind = TextTalents1949(spell, "category") == "Rituals"
                ? CharacterCareerSpellLearningKind.Ritual : CharacterCareerSpellLearningKind.Spell;
            RequireTalents1949(CharacterCareerSpellLearningTransaction.TryValidateCurrentSource(
                id, kind, spell, traditionSource, traditionId, out _), "spell-current-source-unsupported");
            RequireTalents1949(CharacterCareerSpellLearningTransaction.CurrentSourceVisible(settings, spell),
                "spell-current-source-filtered-by-profile");
            if (!UsesOwnedPriorityCareer9569(current)) AuthenticateCurrentSpellEffects1949(root, source);
            RequireTalents1949(CharacterCareerSpellLearningTransaction.TryEvaluateCurrentEffects(root, spell, out var effects, out _, retained),
                "spell-current-effects-unsupported");
            RequireTalents1949(CharacterCareerSpellLearningTransaction.TryCalculateCurrentRuleCost(
                IntTalents1949(karma, "karmaspell", -1), effects, kind, out int cost), "spell-current-cost-unavailable");
            return new(id, kind.ToString(), cost, magic.Magic, resolved.SpellXml,
                CharacterCreationFinalizationDigest.Compute(new { settingsState, resolved.AuthorityState, resolved.TraditionXml,
                    Magic = magic, Effects = effects }), known);
        }
        RequireTalents1949(domain == CharacterCurrentRuleDomain.ComplexForms
            && source is ICharacterCareerComplexFormLearningSourceContext, "form-current-source-unavailable");
        var resonance = CurrentResonance5699(current, root, source);
        RequireTalents1949(resonance.IsSupported && resonance.IsTechnomancer && resonance.EffectiveResonance > 0,
            "form-current-resonance-unavailable");
        RequireTalents1949(((ICharacterCareerComplexFormLearningSourceContext)source).TryResolveCareerComplexFormLearningSource(
            id, out var formSource) && formSource is not null && !string.IsNullOrWhiteSpace(formSource.AuthorityState),
            "form-current-source-unavailable");
        RequireTalents1949(CharacterCareerComplexFormLearningTransaction.TryValidateCurrentSource(
            id, ParseTalents1949(formSource!.ComplexFormXml), out _), "form-current-source-unsupported");
        RequireTalents1949(CharacterCareerComplexFormLearningTransaction.TryCalculateCurrentRuleCost(
            IntTalents1949(karma, "karmanewcomplexform", -1), resonance.CostEffects, out int formCost), "form-current-cost-unavailable");
        int count = CurrentMagicRows1949(root, domain).Length;
        RequireTalents1949(CharacterCareerComplexFormLearningTransaction.TryEvaluateCurrentCount(settings, resonance, count,
                adding: false, out decimal limit),
            "form-current-count-limit");
        return new(id, "ComplexForm", formCost, resonance.EffectiveResonance, formSource.ComplexFormXml,
            CharacterCreationFinalizationDigest.Compute(new { settingsState, formSource.AuthorityState, Resonance = resonance,
                Count = count, Limit = limit }), known);
    }

    private static void AuthenticateCurrentSpellEffects1949(XElement root, ICharacterSourceDataContext source)
    {
        // ReadEffects is the calculation owner. Before invoking it, prove the supported
        // source markers against actual selected quality IDs and their retained bonuses.
        // Source-defined spell price/restriction effects need a wider source compiler;
        // their absence is checked in both source and current graph, never assumed.
        var qualities = RowsTalents1949(root, "qualities", "quality");
        var markerSources = new Dictionary<Guid, XElement>();
        foreach (var quality in qualities)
        {
            Guid id = SourceGuidTalents1949(quality);
            bool resolvedSource = source.TryResolveCareerQualitySource(id, out var resolved);
            RequireTalents1949(Guid.TryParse(TextTalents1949(quality, "guid"), out var instance) && instance != Guid.Empty
                && !markerSources.ContainsKey(instance) && resolvedSource
                && resolved is not null && resolved.SourceId == id, "spell-current-quality-source-unavailable");
            var definition = ParseTalents1949(resolved!.RawSourceXml);
            var bonuses = definition.Elements("bonus").ToArray();
            RequireTalents1949(bonuses.Length <= 1 && !definition.Elements("firstlevelbonus").Any(node => node.HasElements),
                "spell-current-quality-effects-unsupported");
            var bonus = bonuses.SingleOrDefault() ?? new XElement("bonus");
            RequireTalents1949(bonus.Elements().All(node => node.Name.Namespace == XNamespace.None && !node.HasAttributes
                && node.Name.LocalName is "enableattribute" or "enabletab" or "unlockskills" or "specificskill"),
                "spell-current-quality-effect-source-unsupported");
            // Native grant writers discard indentation; retained source readers preserve it.
            // Compare the complete bonus with the existing structural XML comparator.
            var savedBonuses = quality.Elements("bonus").ToArray();
            RequireTalents1949(savedBonuses.Length <= 1
                && SameTalentsXml1949(savedBonuses.SingleOrDefault() ?? new XElement("bonus"),
                    bonus.ToString(SaveOptions.DisableFormatting)),
                "spell-current-quality-effect-source-changed");
            markerSources.Add(instance, bonus);
        }
        foreach (var effect in RowsTalents1949(root, "improvements", "improvement"))
        {
            if (!ImprovementEnabledTalents1949(effect)) continue;
            string type = TextTalents1949(effect, "improvementttype"), target = TextTalents1949(effect, "improvedname", "");
            RequireTalents1949(!type.Contains("Spell", StringComparison.OrdinalIgnoreCase), "spell-current-effect-source-unsupported");
            if (type != "SpecialSkills" && !(type == "SpecialTab" && target.Equals("Magician", StringComparison.OrdinalIgnoreCase))) continue;
            bool ownerValid = Guid.TryParse(TextTalents1949(effect, "sourcename"), out var owner);
            markerSources.TryGetValue(owner, out var bonus);
            RequireTalents1949(TextTalents1949(effect, "improvementsource") == "Quality"
                && ownerValid && bonus is not null, "spell-current-marker-source-unavailable");
            RequireTalents1949(type == "SpecialSkills"
                ? bonus!.Elements("unlockskills").Any(node => !node.HasAttributes && !node.HasElements && node.Value == target)
                : bonus!.Elements("enabletab").Any(node => !node.HasAttributes && node.Elements().Count() == 1
                    && node.Element("name")?.Value.Equals(target, StringComparison.OrdinalIgnoreCase) == true),
                "spell-current-marker-source-changed");
        }
    }

    private static void ValidateCurrentLearnedMagicShape1949(CharacterCurrentRuleDomain domain, XElement row, bool nativePriority9569 = false)
    {
        RequireTalents1949(Guid.TryParseExact(TextTalents1949(row, "guid"), "D", out var instanceId) && instanceId != Guid.Empty,
            "current-learned-magic-instance-missing");
        string[] common = ["guid", "sourceid", "name", "source", "page", "notes", "notesColor", "sortorder", "extra", "useskill", "altnotes"];
        string[] fields = domain == CharacterCurrentRuleDomain.Spells
            ? ["category", "type", "range", "damage", "duration", "dv", "descriptor", "limited", "extended", "alchemical", "free"]
            : ["target", "duration", "fv", "rating", "free"];
        string[] nativeFields9569 = nativePriority9569 && domain == CharacterCurrentRuleDomain.Spells
            ? ["descriptors", "customextended", "freebonus", "barehandedadept", "improvementsource", "grade"] : [];
        RequireTalents1949(!row.HasAttributes && row.Elements().All(node => !node.HasAttributes && !node.HasElements
            && (common.Contains(node.Name.LocalName) || fields.Contains(node.Name.LocalName)
                || nativeFields9569.Contains(node.Name.LocalName)))
            && row.Elements().GroupBy(node => node.Name).All(group => group.Count() == 1)
            && TextTalents1949(row, "extra", "").Length == 0 && TextTalents1949(row, "useskill", "").Length == 0,
            "current-learned-magic-shape-unsupported");
        if (domain == CharacterCurrentRuleDomain.Spells)
            RequireTalents1949(!BoolTalents1949(row, "limited", false) && !BoolTalents1949(row, "extended", false)
                && !BoolTalents1949(row, "alchemical", false), "current-spell-variant-unsupported");
        if (nativePriority9569 && domain == CharacterCurrentRuleDomain.Spells)
            RequireTalents1949(row.Element("descriptors") is not null && row.Element("descriptor") is null
                && !BoolTalents1949(row, "customextended", false) && !BoolTalents1949(row, "freebonus", false)
                && !BoolTalents1949(row, "barehandedadept", false)
                && TextTalents1949(row, "improvementsource", "") == "Spell"
                && IntTalents1949(row, "grade", -1) == 0, "current-native-spell-variant-unsupported");
    }

    // This is a view of the retained method's catalog, not a new persisted
    // authority or a conversion of native talents into Priority identities.
    private sealed record CurrentMagicCatalog1983(string AuthorityDigest,
        IReadOnlyList<CharacterCreationMagicResonanceCatalogOption> Options,
        CharacterCreationMagicResonanceAuthority? PriorityAuthority = null,
        CharacterCreationKarmaMagicPolicy? NativePolicy = null);

    private static CurrentMagicCatalog1983 ResolveCurrentMagicCatalog1983(
        CharacterCurrentRuleEnvironmentContext current, CharacterCurrentRuleDomain domain,
        ICharacterSourceDataContext source)
    {
        // Legacy Priority contexts retain their existing missing-field handling.
        // Native interpretation still requires an explicit, source-matched method.
        string method = ParseTalents1949(current.CharacterXml).Element("buildmethod")?.Value ?? string.Empty;
        if (method is not (CharacterCreationBuildMethods.Karma or CharacterCreationBuildMethods.LifeModules))
        {
            RequireTalents1949(TryCurrentMagicAuthority6242(current, source, out var priority), "magic-authority-unavailable");
            return new(priority.AuthorityDigest, MagicOptions1949(domain, priority), PriorityAuthority: priority);
        }

        RequireTalents1949(UsesNativeMethods1958(current) && current.IsUnchanged
            && current.Current.Document.RulesetId == "sr5", "magic-native-method-semantics-unavailable");
        RequireTalents1949(CharacterNativeMagicCatalogAuthority5102.TryResolve(source, method,
            TextTalents1949(ParseTalents1949(current.CharacterXml), "settings"), out var native)
            && current.IsUnchanged, "magic-native-source-binding-changed");
        string kind = domain switch
        {
            CharacterCurrentRuleDomain.Traditions => CharacterCreationMagicResonanceKinds.Tradition,
            CharacterCurrentRuleDomain.Streams => CharacterCreationMagicResonanceKinds.Stream,
            CharacterCurrentRuleDomain.Powers => CharacterCreationMagicResonanceKinds.AdeptPower,
            CharacterCurrentRuleDomain.Spells => CharacterCreationMagicResonanceKinds.Spell,
            CharacterCurrentRuleDomain.ComplexForms => CharacterCreationMagicResonanceKinds.ComplexForm,
            _ => throw new InvalidDataException("current-talents-domain-unsupported")
        };
        return new(native!.AuthorityDigest, native.Catalogs.Single(slice => slice.Kind == kind).Options, NativePolicy: native.Policy);
    }

    private static object? ConsumeCurrentLifeModuleMagic1983(CharacterCurrentRuleEnvironmentContext current,
        CharacterCurrentRuleCatalogValidation catalog, ICharacterSourceDataContext source,
        ref CharacterCurrentRuleConsumptionState state)
    {
        if (!TryCurrentNativeMethod1958(current, source, out var method, []) || method?.LifeModules is not { } modules)
            return null;
        var root = ParseTalents1949(current.CharacterXml);
        // Life Modules keeps its paid talent/magic selections in the finalization
        // request, not a Priority draft. Observe the real pending sequence here;
        // QuoteCurrentLifeModuleFinalization1960 passes the caller's full request
        // to the native planner, including attributes, grants and purchase costs.
        if (CurrentMagicRows1949(root, catalog.Domain).Length != 0
            || BoolTalents1949(root, "magenabled", false) || BoolTalents1949(root, "resenabled", false)
            || BoolTalents1949(root, "depenabled", false)
            || new[] { "arts", "metamagics", "critterpowers", "sustainedobjects", "spirits" }
                .Any(name => root.Elements(name).Any(row => row.HasElements))) return null;
        var resolved = ResolveCurrentMagicCatalog1983(current, catalog.Domain, source);
        var policy = CharacterCreationNativeMagic1430.ReadCurrentPolicy(resolved.NativePolicy, current);
        if (policy is null || !current.IsUnchanged) return null;
        state = CharacterCurrentRuleConsumptionState.NotConfigured;
        return new { method.OutcomeDigest, resolved.AuthorityDigest, Policy = policy,
            modules.Sequence, modules.Effects, modules.Racial, modules.Attributes,
            MissingTalentAndMagicSelection = true };
    }

    private static IReadOnlyList<CharacterCreationMagicResonanceCatalogOption> MagicOptions1949(
        CharacterCurrentRuleDomain domain, CharacterCreationMagicResonanceAuthority authority) => domain switch
    {
        CharacterCurrentRuleDomain.Traditions => authority.Traditions,
        CharacterCurrentRuleDomain.Streams => authority.Streams,
        CharacterCurrentRuleDomain.Powers => authority.AdeptPowers,
        CharacterCurrentRuleDomain.Spells => authority.Spells,
        CharacterCurrentRuleDomain.ComplexForms => authority.ComplexForms,
        _ => throw new InvalidDataException("current-talents-domain-unsupported")
    };

    private static void ValidateTalentsOptionGrammar1949(CharacterCurrentRuleDomain domain, XElement row)
    {
        string[] common = ["id", "name", "source", "page", "translate", "altpage", "notes", "altnotes", "notesColor"];
        string[] fields = domain switch
        {
            CharacterCurrentRuleDomain.Traditions or CharacterCurrentRuleDomain.Streams => ["drain", "spirits", "spiritform"],
            CharacterCurrentRuleDomain.Powers => ["points", "levels", "maxlevels", "extracost", "adeptway", "adeptwayrequires", "action"],
            CharacterCurrentRuleDomain.Spells => ["category", "type", "range", "damage", "duration", "dv", "descriptor", "useskill"],
            CharacterCurrentRuleDomain.ComplexForms => ["target", "duration", "fv"],
            _ => []
        };
        RequireTalents1949(!row.HasAttributes && row.Elements().All(node => common.Contains(node.Name.LocalName)
            || fields.Contains(node.Name.LocalName)), "magic-option-syntax-unsupported");
        RequireTalents1949(row.DescendantsAndSelf().All(node => node.Name.Namespace == XNamespace.None)
            && row.Elements().GroupBy(node => node.Name).All(group => group.Count() == 1), "magic-option-shape-unsupported");
    }

    private static bool KnownLocalTalentsBonuses1949(XElement row)
    {
        // Versioned local bonus vocabulary from the retained SR5 item catalogs. An
        // unfamiliar effect is not made local just by putting it under <bonus>.
        string[] local = ["addmetamagic", "addqualities", "addquality", "drainresist", "enableattribute", "enabletab", "initiativepass",
            "limitspellcategory", "limitspiritcategory", "metamagiclimit", "optionalpowers", "skilldisable", "skillgroupdisable", "specificpower",
            "specificskill", "spellcategory", "addweapon", "armor", "coldarmor", "conditionmonitor", "detectionspellresist", "disablequality", "dodge",
            "fatigueresist", "firearmor", "judgeintentions", "judgeintentionsdefense", "manaillusionresist", "mentallimit", "mentalmanipulationresist",
            "pathogencontactresist", "pathogeningestionresist", "pathogeninhalationresist", "pathogeninjectionresist", "physicalcmrecovery",
            "physicalillusionresist", "physicallimit", "selectattribute", "selectlimit", "selectskill", "selectspell", "selecttext", "sociallimit",
            "specificattribute", "spellresistance", "sprintbonus", "stuncmrecovery", "surprise", "throwrangestr", "throwstr", "toxincontactresist",
            "toxiningestionresist", "toxininhalationresist", "toxininjectionresist", "unarmedap", "unarmeddv", "unarmeddvphysical", "unlockskills",
            "walkmultiplier", "weaponcategorydice", "weaponcategorydv", "weaponskillaccuracy", "addspirit", "electricityarmor", "limitmodifier",
            "pathogencontactimmune", "pathogeninhalationimmune", "radiationresist", "selectarmor", "toxincontactimmune", "toxininhalationimmune"];
        return row.Elements().Where(node => node.Name.LocalName is "bonus" or "wirelessbonus" or "wirelessweaponbonus")
            .All(bonus => (!bonus.HasAttributes || IsLocalStockArmorModBonus2981(row, bonus))
                && bonus.Elements().All(effect => local.Contains(effect.Name.LocalName)
                || IsLocalStockLoadoutBonus2971(row, bonus, effect)));
    }

    // Retain the stock resistance grouping on auxiliary armor mods without
    // applying its stacking semantics or making those mods purchasable.
    private static bool IsLocalStockArmorModBonus2981(XElement row, XElement bonus)
    {
        if (row.Name != "mod" || bonus.Name != "bonus" || bonus.Attributes().Count() != 1
            || bonus.Attribute("unique") is not { } unique
            || bonus.Nodes().OfType<XText>().Any(text => !string.IsNullOrWhiteSpace(text.Value))) return false;
        string[] effects = unique.Value switch
        {
            "chemicalprotection" => ["toxincontactresist", "pathogencontactresist"],
            "fireresistance" => ["firearmor"],
            "insulation" => ["coldarmor"],
            "nonconductivity" => ["electricityarmor"],
            _ => []
        };
        return effects.Length > 0 && bonus.Elements().Count() == effects.Length
            && effects.All(name => bonus.Elements(name).Count() == 1)
            && bonus.Elements().All(effect => LocalLoadoutText2971(effect)
                && (effect.Value == "Rating" || int.TryParse(effect.Value, NumberStyles.None,
                    CultureInfo.InvariantCulture, out int value) && value > 0))
            && bonus.Elements().Select(effect => effect.Value).Distinct(StringComparer.Ordinal).Count() == 1;
    }

    // These stock payloads remain option-local and unavailable to the loadout
    // projector. Recognizing their exact shape never executes a wireless effect.
    private static bool IsLocalStockLoadoutBonus2971(XElement row, XElement bonus, XElement effect)
    {
        if (bonus.Elements().Count() != 1
            || bonus.Nodes().OfType<XText>().Any(text => !string.IsNullOrWhiteSpace(text.Value))) return false;
        if (row.Name == "weapon" && bonus.Name == "wirelessweaponbonus" && effect.Name == "accuracy")
            return LocalLoadoutText2971(effect)
                && int.TryParse(effect.Value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _);
        if (row.Name != "armor" || bonus.Name != "wirelessbonus" || effect.Name != "skillcategory") return false;
        return !effect.HasAttributes
            && !effect.Nodes().OfType<XText>().Any(text => !string.IsNullOrWhiteSpace(text.Value))
            && effect.Elements().Count() == 2 && effect.Elements("name").Count() == 1 && effect.Elements("bonus").Count() == 1
            && effect.Elements().All(LocalLoadoutText2971)
            && effect.Element("name")!.Value is "Social Active" or "Social"
            && int.TryParse(effect.Element("bonus")!.Value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _);
    }

    private static bool LocalLoadoutText2971(XElement node)
        => !node.HasAttributes && !node.HasElements && !string.IsNullOrWhiteSpace(node.Value)
            && node.Value == node.Value.Trim() && node.Value.Length <= 256 && !node.Value.Any(char.IsControl);

    private static bool LocalLoadoutReferences2971(IEnumerable<XElement> rows)
        => rows.All(LocalLoadoutText2971)
            && rows.Select(row => row.Value).Distinct(StringComparer.Ordinal).Count() == rows.Count();

    private static bool IsLocalTalentsOption1949(CharacterCurrentRuleDomain domain, XElement row)
    {
        // These are bounded object definitions. Bonus/requirement payloads attach only
        // to this named object; an unknown top-level instruction has no such proof.
        string[] common = ["id", "name", "source", "page", "translate", "altpage", "notes", "altnotes", "notesColor",
            "bonus", "required", "forbidden"];
        string[] fields = row.Name == "spirit" ?
            ["bod", "agi", "rea", "str", "cha", "int", "log", "wil", "edg", "ess", "mag", "ini", "initiative",
             "skills", "powers", "optionalpowers", "category", "movement", "armor", "reach", "dep", "res", "ignoreboundspiritlimit", "qualities", "run", "sprint", "walk", "weaknesses"] : domain switch
        {
            CharacterCurrentRuleDomain.Traditions or CharacterCurrentRuleDomain.Streams => ["drain", "spirits", "spiritform", "limitspiritchoices"],
            CharacterCurrentRuleDomain.Powers => ["points", "levels", "maxlevel", "maxlevels", "limit", "extracost", "extrapointcost", "doublecost", "hide", "includeinlimit", "adeptway", "adeptwayrequires", "action"],
            CharacterCurrentRuleDomain.Spells => ["category", "type", "range", "damage", "duration", "dv", "descriptor", "useskill"],
            CharacterCurrentRuleDomain.ComplexForms => ["target", "duration", "fv", "useskill"],
            CharacterCurrentRuleDomain.Weapons => ["category", "type", "spec", "spec2", "reach", "damage", "ap", "mode", "rc", "ammo", "ammocategory",
                "accuracy", "conceal", "useskill", "useskillspec", "range", "alternaterange", "sizecategory", "weapontype", "avail", "cost", "weight",
                "rating", "minrating", "maxrating", "accessories", "allowaccessory", "allowgear", "mount", "extramount", "addweapon", "cyberware", "underbarrel", "underbarrels", "accessorymounts",
                "ammoslots", "doubledcostaccessorymounts", "hide", "requireammo", "shortburst", "singleshot", "wirelessweaponbonus"],
            CharacterCurrentRuleDomain.Armor => ["category", "armor", "armoroverride", "armorcapacity", "avail", "cost", "weight", "rating", "minrating", "maxrating",
                "capacity", "addweapon", "mods", "gears", "addgears", "selectmods", "encumbrance", "addmodcategory", "selectmodsfromcategory", "wirelessbonus"],
            _ => []
        };
        return KnownLocalTalentsBonuses1949(row) && !row.HasAttributes && row.DescendantsAndSelf().All(node => node.Name.Namespace == XNamespace.None)
            && row.Elements().All(node => common.Contains(node.Name.LocalName) || fields.Contains(node.Name.LocalName))
            && row.Elements().GroupBy(node => node.Name).All(group => group.Count() == 1
                || domain == CharacterCurrentRuleDomain.Weapons && group.Key == "addweapon" && LocalLoadoutReferences2971(group));
    }

    private static void ValidateTalentsGlobal1949(CharacterCurrentRuleDomain domain, XElement root)
    {
        RequireTalents1949(root.Name == "chummer" && !root.HasAttributes
            && root.Elements().GroupBy(node => node.Name).All(group => group.Count() == 1)
            && !root.Nodes().OfType<XText>().Any(text => !string.IsNullOrWhiteSpace(text.Value)), "global-syntax-unsupported");
        foreach (var container in root.Elements())
        {
            if (domain == CharacterCurrentRuleDomain.Streams && container.Name == "spirits")
            {
                ValidateStreamSpriteDefinitions1949(container);
                continue;
            }
            if (domain == CharacterCurrentRuleDomain.Traditions && container.Name == "drainattributes")
            {
                ValidateTraditionDrainDefinitions2952(container);
                continue;
            }
            bool categories = container.Name == "categories" && domain is CharacterCurrentRuleDomain.Spells
                or CharacterCurrentRuleDomain.Weapons or CharacterCurrentRuleDomain.Armor;
            string? localKind = (domain, container.Name.LocalName) switch
            {
                (CharacterCurrentRuleDomain.Powers, "enhancements") => "enhancement",
                (CharacterCurrentRuleDomain.Weapons, "accessories") => "accessory",
                (CharacterCurrentRuleDomain.Armor, "mods") => "mod",
                _ => null
            };
            if (localKind is not null)
            {
                // Auxiliary object collections cannot apply themselves. Current nested
                // objects are refused by their actual-state kernels; the exact-key API
                // exposes no lookup/quote for these auxiliary objects or $global.
                RequireTalents1949(!container.HasAttributes && container.Elements().All(row =>
                    row.Name == localKind && IsLocalAuxiliaryTalents1949(localKind, row)), "global-unbounded-auxiliary");
                continue;
            }
            bool modCategories = domain == CharacterCurrentRuleDomain.Armor && container.Name == "modcategories";
            RequireTalents1949(!container.HasAttributes && (categories || modCategories)
                && !container.Nodes().OfType<XText>().Any(text => !string.IsNullOrWhiteSpace(text.Value))
                && container.Elements().Select(node => node.Value).Distinct(StringComparer.Ordinal).Count() == container.Elements().Count(),
                "global-mechanic-unsupported");
            foreach (var row in container.Elements())
            {
                RequireTalents1949(row.Name == "category" && !row.HasElements
                    && !string.IsNullOrWhiteSpace(row.Value), "global-entry-unsupported");
                foreach (var attribute in row.Attributes())
                {
                    bool supported = attribute.Name == "translate"
                        || domain == CharacterCurrentRuleDomain.Weapons && attribute.Name == "type"
                            && attribute.Value is "gun" or "melee" or "bow" or "crossbow" or "cannon" or "energy" or "exotic" or "flame" or "glauncher" or "mlauncher" or "taser"
                        || domain == CharacterCurrentRuleDomain.Weapons && attribute.Name == "gunneryspec"
                            && attribute.Value is "Ballistic" or "Energy" or "Artillery" or "Rocket"
                        || attribute.Name == "blackmarket" && (domain == CharacterCurrentRuleDomain.Weapons && attribute.Value == "Weapons"
                            || domain == CharacterCurrentRuleDomain.Armor && (attribute.Value == "Armor"
                                // Retained stock mod-category mapping; auxiliary mods are never quoted here.
                                || modCategories && attribute.Value == "Armor,Electronics,Magic,Software"))
                        || domain == CharacterCurrentRuleDomain.Spells && attribute.Name == "useskill"
                            && attribute.Value == (row.Value == "Rituals" ? "Ritual Spellcasting" : row.Value == "Enchantments" ? "Artificing" : "Spellcasting")
                        || domain == CharacterCurrentRuleDomain.Spells && attribute.Name == "alchemicalskill" && attribute.Value == "Alchemy"
                        || domain == CharacterCurrentRuleDomain.Spells && attribute.Name == "barehandedadeptskill" && attribute.Value == "Unarmed Combat";
                    RequireTalents1949(supported, "global-category-mechanic-unsupported");
                }
            }
        }
    }

    private static void ValidateTraditionDrainDefinitions2952(XElement container)
    {
        // The retained stock catalog and TryResolveTraditionDrainExpressions use
        // identified <drainattribute><id/><name/></drainattribute> rows, not leaf
        // categories. Keep source expressions exact; this is selector admission,
        // not a drain-pool evaluation or permission to select an unavailable option.
        RequireTalents1949(!container.HasAttributes && container.HasElements
            && !container.Nodes().OfType<XText>().Any(text => !string.IsNullOrWhiteSpace(text.Value)),
            "global-drain-selector-unsupported");
        var identities = new HashSet<Guid>();
        var expressions = new List<string>();
        foreach (var row in container.Elements())
        {
            RequireTalents1949(row.Name == "drainattribute" && !row.HasAttributes
                && !row.Nodes().OfType<XText>().Any(text => !string.IsNullOrWhiteSpace(text.Value))
                && row.Elements().Count() == 2 && row.Elements("id").Count() == 1 && row.Elements("name").Count() == 1
                && row.Elements().All(field => !field.HasAttributes && !field.HasElements),
                "global-drain-selector-unsupported");
            RequireTalents1949(Guid.TryParseExact(row.Element("id")!.Value, "D", out var id)
                && id != Guid.Empty && identities.Add(id), "global-drain-selector-identity-invalid");
            expressions.Add(row.Element("name")!.Value);
        }
        // The typed selector API requires its empty-choice sentinel followed by
        // the exact source choices. It validates the whole list (including duplicate,
        // blank, multiline and oversized expressions), without rewriting any bytes.
        RequireTalents1949(CharacterTraditionDrainRules.TryValidateRequestedExpression(
            expressions[0], [string.Empty, .. expressions], out _), "global-drain-selector-expression-unsupported");
    }

    private static void ValidateStreamSpriteDefinitions1949(XElement container)
    {
        // ReadCurrentCatalog leaves Streams sprite definitions in $global. Admit only
        // this stock definition grammar, retaining its XML/digest; it grants no owner,
        // stat evaluation, power effect or advancement to a selected sprite.
        string[] scalars = ["id", "name", "page", "source", "bod", "agi", "rea", "str", "cha", "int", "log", "wil", "ini"];
        string[] lists = ["powers", "optionalpowers", "skills"];
        static bool TextOnly(XElement node) => !node.HasElements && !string.IsNullOrWhiteSpace(node.Value)
            && node.Value == node.Value.Trim() && node.Value.Length <= CharacterSpiritNameChoiceRules.MaximumNameLength
            && node.Value.IndexOfAny(['\r', '\n', '\0']) < 0;
        static bool NoMixedText(XElement node)
            => !node.Nodes().OfType<XText>().Any(text => !string.IsNullOrWhiteSpace(text.Value));
        var ids = new HashSet<Guid>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        RequireTalents1949(container.DescendantsAndSelf().All(node => node.Name.Namespace == XNamespace.None && !node.HasAttributes)
            && NoMixedText(container) && container.Elements().Count() <= 65_536, "global-stream-sprite-syntax-unsupported");
        foreach (var row in container.Elements())
        {
            RequireTalents1949(row.Name == "spirit" && NoMixedText(row)
                && row.Elements().All(node => scalars.Contains(node.Name.LocalName) || lists.Contains(node.Name.LocalName))
                && row.Elements().GroupBy(node => node.Name).All(group => group.Count() == 1)
                && scalars.All(name => row.Element(name) is { } scalar && TextOnly(scalar))
                && row.Element("skills") is not null
                && (row.Element("powers") is not null || row.Element("optionalpowers") is not null),
                "global-stream-sprite-definition-unsupported");
            RequireTalents1949(Guid.TryParseExact(row.Element("id")!.Value, "D", out var id) && id != Guid.Empty
                && ids.Add(id) && names.Add(row.Element("name")!.Value), "global-stream-sprite-identity-ambiguous");
            // streams.xsd declares page as unsignedShort. Formula tokens below are
            // the bounded spellings in stock streams.xml, not an expression evaluator.
            RequireTalents1949(ushort.TryParse(row.Element("page")!.Value, NumberStyles.None, CultureInfo.InvariantCulture, out _)
                && new[] { "bod", "agi", "rea", "str" }.All(name => row.Element(name)!.Value == "0")
                && new[] { "cha", "int", "log", "wil" }.All(name => row.Element(name)!.Value
                    is "F" or "F-1" or "F+0" or "F+1" or "F+2" or "F+3" or "F+4")
                && row.Element("ini")!.Value is "(F*2)" or "(F*2)+1" or "(F*2)+2" or "(F*2)+3" or "(F*2)+4",
                "global-stream-sprite-value-unsupported");
            foreach (var list in row.Elements().Where(node => lists.Contains(node.Name.LocalName)))
                RequireTalents1949(NoMixedText(list) && list.HasElements && list.Elements().Count() <= 65_536
                    && list.Elements().All(node => node.Name == (list.Name == "skills" ? "skill" : "power") && TextOnly(node))
                    && list.Elements().Select(node => node.Value).Distinct(StringComparer.Ordinal).Count() == list.Elements().Count(),
                    "global-stream-sprite-list-unsupported");
        }
    }

    private static bool IsLocalAuxiliaryTalents1949(string kind, XElement row)
    {
        string[] common = ["id", "name", "source", "page", "translate", "altpage", "bonus", "required", "forbidden", "notes", "notesColor"];
        string[] fields = kind switch
        {
            "enhancement" => ["power"],
            "accessory" => ["accessorycostmultiplier", "accuracy", "addmount", "addunderbarrels", "allowgear", "ammobonus", "ammoreplace",
                "ammoslots", "ap", "avail", "conceal", "cost", "damage", "damagetype", "extramount", "gears", "hide", "modifyammocapacity",
                "mount", "rangemodifier", "rating", "rc", "rcdeployable", "rcgroup", "reach", "replacerange", "specialmodification"],
            "mod" => ["addoncategory", "armor", "armorcapacity", "avail", "category", "cost", "gearcapacity", "hide", "maxrating", "wirelessbonus"],
            _ => []
        };
        return KnownLocalTalentsBonuses1949(row) && !row.HasAttributes && row.DescendantsAndSelf().All(node => node.Name.Namespace == XNamespace.None)
            && Guid.TryParse(row.Element("id")?.Value, out var id) && id != Guid.Empty
            && !string.IsNullOrWhiteSpace(row.Element("name")?.Value)
            && row.Elements().All(node => common.Contains(node.Name.LocalName) || fields.Contains(node.Name.LocalName))
            && row.Elements().GroupBy(node => node.Name).All(group => group.Count() == 1
                || kind == "mod" && group.Key == "addoncategory" && LocalLoadoutReferences2971(group));
    }

    private static XElement[] CurrentMagicRows1949(XElement root, CharacterCurrentRuleDomain domain)
    {
        if (domain is CharacterCurrentRuleDomain.Traditions or CharacterCurrentRuleDomain.Streams)
        {
            var traditions = root.Elements("tradition").ToArray();
            RequireTalents1949(traditions.Length <= 1, "current-tradition-ambiguous");
            if (traditions.Length == 0 || !traditions[0].HasElements) return [];
            string type = TextTalents1949(traditions[0], "traditiontype");
            RequireTalents1949(type is "MAG" or "RES", "current-tradition-type-unsupported");
            return (domain == CharacterCurrentRuleDomain.Traditions ? type == "MAG" : type == "RES") ? traditions : [];
        }
        string name = domain switch
        {
            CharacterCurrentRuleDomain.Powers => "power", CharacterCurrentRuleDomain.Spells => "spell",
            CharacterCurrentRuleDomain.ComplexForms => "complexform", _ => throw new InvalidDataException("current-talents-domain-unsupported")
        };
        var rows = RowsTalents1949(root, name + "s", name);
        RequireTalents1949(root.Descendants(name).SequenceEqual(rows), "current-nested-magic-unsupported");
        return rows;
    }

    private static void AddCurrentKey1949(CharacterCurrentRuleCatalogValidation catalog, Guid id, List<string> selected)
    {
        var matches = catalog.Entries.Where(entry => entry.Key != "$global"
            && entry.State == CharacterCurrentRuleEntryState.AvailableNotSelected
            && Guid.TryParse(TextTalents1949(ParseTalents1949(entry.Xml), "id"), out Guid sourceId) && sourceId == id).ToArray();
        RequireTalents1949(matches.Length == 1, "current-selected-entry-unavailable");
        string key = matches[0].Key;
        if (!selected.Contains(key, StringComparer.Ordinal)) selected.Add(key);
    }

    private static Guid SourceIdentityTalents1949(CharacterCurrentRuleDomain domain, CharacterCurrentRuleCatalogEntry entry, XElement row)
    {
        string expected = domain switch
        {
            CharacterCurrentRuleDomain.Traditions or CharacterCurrentRuleDomain.Streams => "tradition",
            CharacterCurrentRuleDomain.Powers => "power", CharacterCurrentRuleDomain.Spells => "spell",
            CharacterCurrentRuleDomain.ComplexForms => "complexform", CharacterCurrentRuleDomain.Weapons => "weapon",
            CharacterCurrentRuleDomain.Armor => "armor", _ => string.Empty
        };
        RequireTalents1949(row.Name == expected || domain == CharacterCurrentRuleDomain.Traditions
            && row.Name == "spirit" && entry.Key.StartsWith("spirits/", StringComparison.Ordinal), "catalog-entry-kind-unsupported");
        RequireTalents1949(Guid.TryParseExact(TextTalents1949(row, "id"), "D", out Guid id) && id != Guid.Empty
            && !string.IsNullOrWhiteSpace(entry.Key)
            && entry.Name == TextTalents1949(row, "name"), "catalog-entry-identity-invalid");
        return id;
    }
    private static string TalentsQuery1949(CharacterCurrentRuleDomain domain) => domain.ToString().ToLowerInvariant() + ".xml";
    private static bool TalentsLoadoutDomain1949(CharacterCurrentRuleDomain domain) => domain is
        CharacterCurrentRuleDomain.Traditions or CharacterCurrentRuleDomain.Streams or CharacterCurrentRuleDomain.Powers
        or CharacterCurrentRuleDomain.Spells or CharacterCurrentRuleDomain.ComplexForms or CharacterCurrentRuleDomain.Weapons or CharacterCurrentRuleDomain.Armor;
    private static XElement ParseTalents1949(string xml) => ParseTalents1949(xml, ignoreWhitespace: false);
    private static XElement ParseTalents1949(string xml, bool ignoreWhitespace)
    {
        using var reader = CreateCurrentRuleXmlReader(xml, new XmlReaderSettings
            { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, IgnoreWhitespace = ignoreWhitespace });
        var root = XElement.Load(reader, LoadOptions.None);
        RequireTalents1949(!root.DescendantNodes().OfType<XProcessingInstruction>().Any(), "xml-instruction-unsupported");
        return root;
    }
    private static bool SameTalentsXml1949(XElement row, string xml)
        // Typed catalogs use XElement.Parse(..., LoadOptions.None); retained rows can still
        // contain indentation from their XmlReader. Normalize only this structural comparison.
        => XNode.DeepEquals(ParseTalents1949(row.ToString(SaveOptions.DisableFormatting), ignoreWhitespace: true),
            ParseTalents1949(xml, ignoreWhitespace: true));
    private static XElement[] RowsTalents1949(XElement root, string container, string entry)
    {
        var boxes = root.Elements(container).ToArray();
        RequireTalents1949(boxes.Length <= 1, "current-collection-ambiguous");
        if (boxes.Length == 0) return [];
        RequireTalents1949(!boxes[0].HasAttributes && boxes[0].Elements().All(node => node.Name == entry), "current-collection-unsupported");
        return boxes[0].Elements(entry).ToArray();
    }
    private static Guid SourceGuidTalents1949(XElement row)
    {
        RequireTalents1949(Guid.TryParse(TextTalents1949(row, "sourceid"), out Guid id) && id != Guid.Empty, "current-source-id-missing");
        return id;
    }
    private static string TextTalents1949(XElement root, string name, string? absent = null)
    {
        var leaves = root.Elements(name).ToArray();
        if (leaves.Length == 0 && absent is not null) return absent;
        RequireTalents1949(leaves.Length == 1 && !leaves[0].HasAttributes && !leaves[0].HasElements, "current-scalar-invalid");
        return leaves[0].Value;
    }
    private static int IntTalents1949(XElement root, string name, int absent)
    {
        RequireTalents1949(int.TryParse(TextTalents1949(root, name, absent.ToString(CultureInfo.InvariantCulture)),
            NumberStyles.Integer, CultureInfo.InvariantCulture, out int value), "current-integer-invalid");
        return value;
    }
    private static bool BoolTalents1949(XElement root, string name, bool absent)
    {
        RequireTalents1949(bool.TryParse(TextTalents1949(root, name, absent ? "True" : "False"), out bool value), "current-boolean-invalid");
        return value;
    }
    private static bool ImprovementEnabledTalents1949(XElement effect)
    {
        // Native Improvement.Save and the selected-grant projector write 0/1 here.
        // Keep this representation local to improvement state; character flags
        // still require booleans. Missing, duplicate and malformed values fail closed.
        RequireTalents1949(effect.Elements().Count(node => node.Name.LocalName == "enabled") == 1,
            "current-scalar-invalid");
        string value = TextTalents1949(effect, "enabled");
        return value switch
        {
            "0" => false,
            "1" => true,
            _ => RequiredBoolTalents1949(effect, "enabled")
        };
    }
    private static bool RequiredBoolTalents1949(XElement root, string name)
    {
        RequireTalents1949(bool.TryParse(TextTalents1949(root, name), out bool value), "current-boolean-invalid");
        return value;
    }
    private static void RequireTalents1949(bool condition, string blocker)
    {
        if (!condition) throw new InvalidDataException("current-talents-" + blocker);
    }
    private static bool TalentsFailure1949(Exception exception) => exception is InvalidDataException or XmlException
        or InvalidOperationException or ArgumentException or FormatException or OverflowException;
    private static string TalentsBlocker1949(Exception exception) => exception is InvalidDataException
        && exception.Message.StartsWith("current-talents-", StringComparison.Ordinal) ? exception.Message : "current-talents-kernel-unavailable";
}

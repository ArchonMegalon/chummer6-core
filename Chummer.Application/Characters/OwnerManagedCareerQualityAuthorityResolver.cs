using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;

namespace Chummer.Application.Characters;

/// <summary>
/// Local owner-edited Career policy, matching ordinary Chummer5 quality changes.
/// Composition selects this policy, never request booleans. It does not grant
/// delegated-GM authority, remote campaign approval or discretionary free cost.
/// The owning service must admit the full owner transition before calling it.
/// Only completely compiled, unconditional source/effect families are offered.
/// </summary>
public sealed class OwnerManagedCareerQualityAuthorityResolver(ICharacterSourceDataResolver sources)
    : ICharacterCareerQualityAuthorityResolver
{
    public const string PolicyIdentity = "chummer.core.owner-managed-career-quality/v1;no-discretionary-free-cost";

    public ICharacterCareerQualityAdmission? Open(OwnerScope owner, WorkspaceStoredDocument saved,
        CharacterCareerQualityIdentity identity, CharacterCareerQualityOperation operation, string runtimeFingerprint)
    {
        if (!ValidWorkspace(owner, saved, runtimeFingerprint) || identity is null) return null;
        try
        {
            var context = sources.TryCreateContext(saved.Document.Content);
            if (context is null || !TryReadAuthority(context, saved.Document.Content, out var settings,
                    out var catalog, out var rawRules, out var digest, out var metatype, out var arms, out var authorityDigest)) return null;
            (XElement Character, XElement Metatype)? restrictionGraphs = null;
            var source = catalog.SingleOrDefault(row => row.SourceId == identity.SourceId);
            if (source is null || !TryInput(owner, saved, identity, operation, runtimeFingerprint,
                    source, settings, rawRules, digest, metatype, arms, ref restrictionGraphs, out var input)) return null;
            return new Admission(sources, context, saved.Document.Content, source, input!, authorityDigest, arms);
        }
        catch (Exception exception) when (exception is XmlException or InvalidDataException or ArgumentException
            or InvalidOperationException or IOException or UnauthorizedAccessException) { return null; }
    }

    public ICharacterCareerQualityCatalogAdmission? Read(OwnerScope owner, WorkspaceStoredDocument saved,
        string runtimeFingerprint)
    {
        if (!ValidWorkspace(owner, saved, runtimeFingerprint)) return null;
        try
        {
            var context = sources.TryCreateContext(saved.Document.Content);
            if (context is null || !TryReadAuthority(context, saved.Document.Content, out var settings,
                    out var catalog, out var rawRules, out var digest, out var metatype, out var arms, out var authorityDigest)
                || !CharacterCareerQualityGraph.TryReadIdentities(saved.Document.Content, out var identities)) return null;
            var candidates = new List<CharacterCareerQualityInput>();
            // Detached, read-only trees live only for this catalog request. Keep
            // canonical candidate projection and both source-currentness fences.
            (XElement Character, XElement Metatype)? restrictionGraphs = null;
            var ownedBySource = identities.ToLookup(row => row.SourceId);
            foreach (var source in catalog)
            {
                // Do not turn unsupported requirements or partial effect graphs
                // into apparently available purchases. No fabricated anatomy.
                if (!CharacterCreationLegacySourceProjector.IsQualitySourceProjectable(source.RawSourceXml, arms)) continue;
                var acquired = new CharacterCareerQualityIdentity(StableAcquisitionId(owner, saved, source.SourceId, digest), source.SourceId);
                Add(acquired, CharacterCareerQualityOperation.AcquireLevel);
                foreach (var existing in ownedBySource[source.SourceId])
                {
                    Add(existing, CharacterCareerQualityOperation.RemoveLevel);
                    Add(existing, CharacterCareerQualityOperation.RemoveAllLevels);
                }
                if (candidates.Count > 16_384) return null;
                void Add(CharacterCareerQualityIdentity identity, CharacterCareerQualityOperation operation)
                {
                    if (TryInput(owner, saved, identity, operation, runtimeFingerprint, source, settings,
                            rawRules, digest, metatype, arms, ref restrictionGraphs, out var input)) candidates.Add(input!);
                }
            }
            var binding = new CharacterCareerQualityExecutionBinding(owner.NormalizedValue, saved.Id.Value,
                saved.ContentRevision, saved.SavedRevision, runtimeFingerprint, digest);
            var admission = new CatalogAdmission(sources, context, saved.Document.Content, binding,
                Array.AsReadOnly(candidates.ToArray()), authorityDigest);
            if (admission.IsCurrent()) return admission;
            admission.Dispose();
            return null;
        }
        catch (Exception exception) when (exception is XmlException or InvalidDataException or ArgumentException
            or InvalidOperationException or IOException or UnauthorizedAccessException) { return null; }
    }

    private static bool ValidWorkspace(OwnerScope owner, WorkspaceStoredDocument saved, string runtimeFingerprint)
        => CharacterCareerQualityTransaction.IsValidOwner(owner) && saved is not null
            && saved.Document.RulesetId == "sr5" && saved.ContentRevision == saved.SavedRevision
            && saved.ContentRevision > 0 && CharacterCareerQualityTransaction.IsValidHistory(saved)
            && CharacterCareerReputationTransaction.IsDigest(runtimeFingerprint);

    private static Guid StableAcquisitionId(OwnerScope owner, WorkspaceStoredDocument saved, Guid sourceId, string digest)
        => new(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\0', PolicyIdentity, owner.NormalizedValue,
            saved.Id.Value, saved.ContentRevision.ToString(CultureInfo.InvariantCulture), sourceId.ToString("D"), digest)))[..16]);

    private static bool TryReadAuthority(ICharacterSourceDataContext context, string characterXml,
        out CharacterCareerQualitySettings settings, out IReadOnlyList<CharacterCareerQualitySource> catalog,
        out string rawRules, out string digest, out string metatype, out int? arms, out string authorityDigest)
    {
        arms = null;
        rawRules = digest = metatype = authorityDigest = string.Empty;
        catalog = [];
        settings = new(0, false, false);
        if (!context.TryResolveCareerQualitySettings(out settings, out var rules)
            || !context.TryResolveCareerQualityMetatype(out metatype, out var metatypeAuthority)
            || !context.TryResolveCareerQualityCatalog(out catalog)
            || catalog.Count > 4096 || catalog.Any(row => row is null)
            || catalog.Select(row => row.SourceId).Distinct().Count() != catalog.Count) return false;
        rawRules = string.Join('\0', PolicyIdentity, rules, metatypeAuthority);
        if (rawRules.Length > CharacterCareerQualityRules.MaximumRuleTextLength) return false;
        string? anatomyDigest = null, anatomyGeneration = null;
        var resolvedCatalog = catalog;
        if (context.TryResolveCareerQualityAnatomySources(out var anatomy) && anatomy is not null
            && anatomy.Schema == CharacterCareerQualityAnatomySources.Sr5CareerV1
            && anatomy.MetatypeXml == metatype && anatomy.MetatypeAuthority == metatypeAuthority
            && anatomy.SettingsAuthority.Length > 0 && anatomy.SettingsXml.Length > 0
            && anatomy.QualityInputsAuthority.Length > 0
            && anatomy.Qualities is not null
            && anatomy.Qualities.All(typed => typed is not null && Guid.TryParseExact(typed.SourceId, "D", out var id)
                && resolvedCatalog.SingleOrDefault(row => row.SourceId == id) is { } raw
                // Match the typed projector's LoadOptions.None whitespace policy;
                // the catalog and anatomy digests still bind the original source bytes.
                && Parse(raw.RawSourceXml, ignoreWhitespace: true).ToString(SaveOptions.DisableFormatting) == typed.CanonicalSourceXml)
            // The anatomy producer hashes the character parsed with LoadOptions.None.
            // Match that detached view after acquisition copies source indentation
            // into saved bonus nodes; keep the original saved graph for all mutations.
            && CharacterCreationLegacySourceProjector.TryResolveCareerArmCount(
                Parse(characterXml, ignoreWhitespace: true).ToString(SaveOptions.DisableFormatting),
                anatomy, out var resolved))
        {
            arms = resolved;
            anatomyDigest = anatomy.Digest;
            // Receipt recovery spans this character's own committed revisions.
            // Bind its content generation to source authority, not the saved
            // character digest, selected contributors or resulting arm count.
            anatomyGeneration = CharacterCareerQualityGraph.Hash(JsonSerializer.Serialize(new
            {
                anatomy.Schema, anatomy.BuildMethod, anatomy.SettingsProfileId,
                anatomy.MetatypeId, anatomy.MetavariantId, anatomy.MetavariantName,
                anatomy.MetatypeXml, anatomy.MetatypeAuthority,
                anatomy.SettingsXml, anatomy.SettingsAuthority, anatomy.QualityInputsAuthority
            }));
        }
        // Unsupported anatomy remains null without withdrawing unrelated quality
        // operations. A previously resolved proof becoming unavailable changes the
        // authority digest and permanently invalidates its retained lease.
        digest = CharacterCareerQualityGraph.Hash(JsonSerializer.Serialize(new
        {
            PolicyIdentity, Rules = rawRules, Sources = catalog.OrderBy(row => row.SourceId),
            AnatomySemantics = anatomyDigest is null ? null : CharacterCareerQualityAnatomySources.Sr5CareerV1,
            AnatomyGeneration = anatomyGeneration
        }));
        // Keep the complete character-bound proof in both retained/fresh lease
        // checks. Separating generation identity never relaxes anatomy admission.
        authorityDigest = CharacterCareerQualityGraph.Hash(JsonSerializer.Serialize(new
        {
            ContentDigest = digest, AnatomyDigest = anatomyDigest, ResolvedArmCount = arms
        }));
        return true;
    }

    private static bool TryInput(OwnerScope owner, WorkspaceStoredDocument saved, CharacterCareerQualityIdentity identity,
        CharacterCareerQualityOperation operation, string runtimeFingerprint, CharacterCareerQualitySource source,
        CharacterCareerQualitySettings settings, string rawRules, string digest, string metatype, int? arms,
        ref (XElement Character, XElement Metatype)? restrictionGraphs, out CharacterCareerQualityInput? input)
    {
        input = null;
        if (!CharacterCareerQualityGraph.TryProject(saved.Document.Content, source, identity, operation, arms, out var graph)
            // These source-defined free-cost cases need their full grant authority.
            // Never charge regular Karma or guess a free grant in its absence.
            || graph!.Definition.Metagenic || graph.Definition.Name == "Mentor Spirit"
            || graph.Definition.MentorSpiritWayFreeCostEligible) return false;
        // Parse lazily: rejected candidates must not introduce a new parse failure.
        restrictionGraphs ??= (Parse(saved.Document.Content), Parse(metatype));
        if (!TryRestriction(restrictionGraphs.Value.Character, graph.Definition, out bool characterAllowed)
            || !TryRestriction(restrictionGraphs.Value.Metatype, graph.Definition, out bool metatypeAllowed)) return false;
        bool permitted = characterAllowed && metatypeAllowed;
        string eligibility = CharacterCareerQualityGraph.Hash(JsonSerializer.Serialize(new
        { digest, identity.SourceId, graph.CharacterDigest, CharacterAllowed = characterAllowed, MetatypeAllowed = metatypeAllowed }));
        input = new(operation, identity, true, "sr5", true, true,
            operation == CharacterCareerQualityOperation.AcquireLevel, operation != CharacterCareerQualityOperation.AcquireLevel,
            GmAllows: true, GmFreeCostApproved: false, HasMentorSpiritWay: false, MetagenicLimit: 0,
            graph.KarmaBefore, graph.Extra, graph.SourceName, graph.Definition, settings,
            new(true, permitted, true, true, true, true, [], permitted ? [] : ["metatype.qualityrestriction"], eligibility),
            graph.Effects, graph.InstancesBefore,
            new(owner.NormalizedValue, saved.Id.Value, saved.ContentRevision, saved.SavedRevision, runtimeFingerprint, digest),
            graph.SourceState, rawRules);
        return CharacterCareerQualityRules.TryCreateQuote(input, out _);
    }

    private static bool TryRestriction(XElement parent, CharacterCareerQualityDefinition definition, out bool allowed)
    {
        allowed = false;
        var nodes = parent.Elements().Where(row => row.Name.LocalName == "qualityrestriction").Take(2).ToArray();
        if (nodes.Length == 0) { allowed = true; return true; }
        if (nodes.Length != 1 || nodes[0].Name != "qualityrestriction" || nodes[0].HasAttributes
            || nodes[0].Nodes().OfType<XText>().Any(node => !string.IsNullOrWhiteSpace(node.Value))) return false;
        var categories = nodes[0].Elements().ToArray();
        if (categories.Any(row => row.Name != "positive" && row.Name != "negative")
            || categories.Select(row => row.Name).Distinct().Count() != categories.Length) return false;
        foreach (var category in categories)
        {
            if (category.HasAttributes || category.Nodes().OfType<XText>().Any(node => !string.IsNullOrWhiteSpace(node.Value))) return false;
            foreach (var quality in category.Elements())
                if (quality.Name != "quality" || quality.HasAttributes || quality.HasElements
                    || string.IsNullOrWhiteSpace(quality.Value) || quality.Value != quality.Value.Trim()) return false;
        }
        string selected = definition.Type == CharacterCareerQualityType.Positive ? "positive" : "negative";
        allowed = categories.Where(row => row.Name == selected).Elements("quality")
            .Any(row => row.Value == definition.Name);
        return true;
    }

    private static XElement Parse(string xml, bool ignoreWhitespace = false)
    {
        using var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings
        { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, IgnoreWhitespace = ignoreWhitespace,
            MaxCharactersInDocument = CharacterCareerReputationProjector.MaximumCharacterXmlLength });
        return XElement.Load(reader);
    }

    private sealed class Admission(ICharacterSourceDataResolver sources, ICharacterSourceDataContext context, string characterXml,
        CharacterCareerQualitySource source, CharacterCareerQualityInput input, string authorityDigest, int? arms)
        : AuthorityLease(sources, context, characterXml, authorityDigest), ICharacterCareerQualityAdmission
    {
        public CharacterCareerQualityInput Input => input;
        public CharacterCareerQualitySource Source => source;
        public int? ResolvedArmCount => arms;
    }

    private sealed class CatalogAdmission(ICharacterSourceDataResolver sources, ICharacterSourceDataContext context,
        string characterXml, CharacterCareerQualityExecutionBinding binding, IReadOnlyList<CharacterCareerQualityInput> candidates,
        string authorityDigest)
        : AuthorityLease(sources, context, characterXml, authorityDigest), ICharacterCareerQualityCatalogAdmission
    {
        public CharacterCareerQualityExecutionBinding Binding => binding;
        public IReadOnlyList<CharacterCareerQualityInput> Candidates => candidates;
    }

    private abstract class AuthorityLease(ICharacterSourceDataResolver sources, ICharacterSourceDataContext context,
        string characterXml, string authorityDigest) : IDisposable
    {
        private bool disposed, invalidated;
        public bool IsCurrent()
        {
            if (disposed || invalidated) return false;
            try
            {
                // Retained context catches observed file ABA; fresh context also
                // checks changed overlay/profile membership in the host catalog.
                bool retained = TryReadAuthority(context, characterXml, out _, out _, out _, out _, out _, out _, out var current) && current == authorityDigest;
                var fresh = retained ? sources.TryCreateContext(characterXml) : null;
                bool same = fresh is not null && TryReadAuthority(fresh, characterXml, out _, out _, out _, out _, out _, out _, out current)
                    && current == authorityDigest;
                invalidated = !same;
                return same;
            }
            catch (Exception exception) when (exception is XmlException or InvalidDataException or ArgumentException
                or InvalidOperationException or IOException or UnauthorizedAccessException) { invalidated = true; return false; }
        }
        public void Dispose() => disposed = true;
    }
}

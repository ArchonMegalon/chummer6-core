using System.Xml;
using System.Xml.Linq;
using Chummer.Contracts.Characters;
using D = Chummer.Contracts.Characters.CharacterCreationPolicyDomain;

namespace Chummer.Application.Characters;

/// <summary>
/// In-process admission of the captured resolved profile and exact book selection.
/// This does not execute individual settings, source rules, amendments or original
/// character mechanics. Neither serialized diagnostics nor policy hashes create it.
/// </summary>
internal sealed class CharacterCreationCompleteProfileExecution
{
    private readonly string authorityDigest;
    private readonly string profileEvaluation;
    private readonly string? booksEvaluation;

    private CharacterCreationCompleteProfileExecution(CharacterCreationCompleteAssessment authority,
        CharacterCreationEffectivePolicyContext policy, CharacterCreationMysticAdeptPowerPointPolicy source,
        XElement settings)
    {
        authorityDigest = CharacterCreationFinalizationDigest.Compute(authority);
        profileEvaluation = CharacterCreationFinalizationDigest.Compute(new
        {
            Authority = authorityDigest, Scope = "resolved-profile-selection-admission",
            source.SettingsProfileId, source.SettingsInputsDigest, policy.ProfileInputsDigest,
            source.CanonicalSourceXml
        });
        if (TryReadBooks(policy, settings, out var books))
            booksEvaluation = CharacterCreationFinalizationDigest.Compute(new
            {
                Authority = authorityDigest, Scope = "enabled-book-selection-admission",
                Profile = profileEvaluation, CanonicalBooksXml = settings.Element("books")!.ToString(SaveOptions.DisableFormatting),
                EnabledSourcebooks = books
            });
    }

    internal static CharacterCreationCompleteProfileExecution? FromFull(
        CharacterCreationCompletionReviewInputs inputs, CharacterCreationCompletionReviewRequest request,
        CharacterCreationCompleteAssessment authority,
        CharacterCreationCompleteRuleSourceExecution? ruleSourceExecution = null)
    {
        if (authority.Schema != CharacterCreationCompleteAssessment.SchemaV4
            || inputs.EffectivePolicy is not { } policy
            || inputs.SnapshotDigest != request.InputsSnapshotDigest
            || inputs.SnapshotDigest != CharacterCreationCompletionReviewRules.ComputeInputsDigest(inputs)
            || !CharacterCreationEffectivePolicyValidation.MayEvaluate(policy, request)
            || !CharacterCreationEffectivePolicyValidation.IsValid(policy, inputs)
            || !TryReadProfile(policy, inputs.Magic.Authority.MysticAdeptPowerPointPolicy, out var settings)
            || !CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(authority,
                CharacterCreationCompleteAssessmentService.FromFullAuthority(inputs, request, ruleSourceExecution))) return null;
        return new(authority, policy, inputs.Magic.Authority.MysticAdeptPowerPointPolicy!, settings!);
    }

    internal static CharacterCreationCompleteProfileExecution? FromSelected(
        CharacterCreationSelectedCompletionWitness witness, CharacterCreationCompletionReviewRequest request,
        CharacterCreationCompletionOriginalSelections original, IReadOnlyList<CharacterContactRuleEffect> effects,
        CharacterCreationCompleteAssessment authority,
        CharacterCreationCompleteRuleSourceExecution? ruleSourceExecution = null)
    {
        if (authority.Schema != CharacterCreationCompleteAssessment.SchemaV4
            || witness.EffectivePolicy is not { } policy
            || witness.ProvenanceInputsSnapshotDigest != request.InputsSnapshotDigest
            || !CharacterCreationEffectivePolicyValidation.MayEvaluate(policy, request)
            || !CharacterCreationEffectivePolicyValidation.IsValidSelected(witness, original, effects)
            || !TryReadProfile(policy, witness.Magic.MysticAdeptPowerPointPolicy, out var settings)
            || !CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(authority,
                CharacterCreationCompleteAssessmentService.FromSelectedAuthority(witness, request, original, effects, ruleSourceExecution))) return null;
        return new(authority, policy, witness.Magic.MysticAdeptPowerPointPolicy!, settings!);
    }

    internal IReadOnlyDictionary<string, string> EvaluationsFor(CharacterCreationCompleteAssessment authority)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (authority.Schema != CharacterCreationCompleteAssessment.SchemaV4
            || authorityDigest != CharacterCreationFinalizationDigest.Compute(authority)) return result;
        foreach (var row in authority.Obligations)
        {
            if (row.Kind != CharacterCreationCompleteObligationKind.PolicyDomain
                || row.Evaluability != CharacterCreationCompleteEvaluability.CandidateDependent
                || row.Reason != CharacterCreationCompleteReason.CandidateEvaluationRequired) continue;
            string? digest = row.Domain switch { D.Profile => profileEvaluation, D.Books => booksEvaluation, _ => null };
            if (digest is not null) result.Add(row.Id, digest);
        }
        return result;
    }

    internal static bool SupportsDomain(CharacterCreationEffectivePolicyContext policy,
        CharacterCreationMysticAdeptPowerPointPolicy? source, D domain)
        => domain is D.Profile or D.Books && HasResolvedDomain(policy, domain)
            && TryReadProfile(policy, source, out var settings)
            && (domain == D.Profile || TryReadBooks(policy, settings!, out _));

    private static bool HasResolvedDomain(CharacterCreationEffectivePolicyContext policy, D domain)
    {
        var rows = policy.Domains.Where(row => row.Domain == domain).Take(2).ToArray();
        return rows.Length == 1 && rows[0].Disposition == CharacterCreationPolicyDisposition.Effective
            && rows[0].Reason == CharacterCreationPolicyReason.ResolvedAuthority;
    }

    private static bool TryReadProfile(CharacterCreationEffectivePolicyContext policy,
        CharacterCreationMysticAdeptPowerPointPolicy? source, out XElement? settings)
    {
        settings = null;
        if (source is null || source.SettingsProfileId != policy.SettingsProfileId
            || !HasResolvedDomain(policy, D.Profile)
            || !CharacterCreationMysticAdeptPowerPointRules.IsValidPolicy(source)
            || CharacterCreationBootstrapProfiles.BindSettingsInputsDigest(source.SettingsInputsDigest,
                source.SettingsProfileId) != policy.ProfileInputsDigest) return false;
        var profiles = policy.Sources.Where(row => row.Kind == CharacterCreationPolicySourceKind.Rules
            && row.SourceId == "profile").Take(2).ToArray();
        if (profiles.Length != 1 || profiles[0].ContentDigest != policy.ProfileInputsDigest) return false;
        // IsValidPolicy already enforces bounded, DTD-free canonical XML and an
        // exact single profile id. Reuse that parser/binding, not a defaults engine.
        try
        {
            settings = XElement.Parse(source.CanonicalSourceXml, LoadOptions.PreserveWhitespace);
            return true;
        }
        catch (Exception error) when (error is XmlException or ArgumentException or InvalidOperationException)
        { return false; }
    }

    private static bool TryReadBooks(CharacterCreationEffectivePolicyContext policy, XElement settings,
        out string[] books)
    {
        books = [];
        if (!HasResolvedDomain(policy, D.Books)) return false;
        XElement[] containers = settings.Elements("books").Take(2).ToArray();
        if (containers.Length != 1 || containers[0].HasAttributes
            || containers[0].Nodes().OfType<XText>().Any(node => !string.IsNullOrWhiteSpace(node.Value))) return false;
        XElement[] nodes = containers[0].Elements().ToArray();
        if (nodes.Length > 256 || nodes.Any(node => node.Name != "book" || node.HasElements || node.HasAttributes)) return false;
        string[] values = nodes.Select(node => node.Value.Trim()).ToArray();
        if (values.Any(value => value.Length is < 1 or > 32
                || value.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '-'))
            || values.Distinct(StringComparer.OrdinalIgnoreCase).Count() != values.Length) return false;
        // Exact existing resolver/FreeGridsActivationMatchesSource selection rule.
        // Ambiguous duplicates and malformed nodes are refused before normalization.
        books = values.Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.Ordinal).ToArray();
        return policy.EnabledSourcebooks is not null && books.SequenceEqual(policy.EnabledSourcebooks)
            // Same canonical books-node digest used by TryResolveCreationEffectivePolicy.
            && CharacterCreationFinalizationDigest.Compute(containers[0].ToString(SaveOptions.DisableFormatting))
                == policy.BooksInputsDigest;
    }
}

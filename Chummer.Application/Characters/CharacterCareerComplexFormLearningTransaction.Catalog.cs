using System.Text.Json;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

internal sealed record CharacterCareerComplexFormLearningCatalogSnapshot(
    CharacterCareerComplexFormLearningCatalogBinding Binding, CharacterCareerComplexFormLearningSourceCatalog Sources,
    ICharacterSourceDataContext Context)
{
    internal CharacterCurrentRuleEnvironmentConsumers.LearningAuthority5703? Current5703 { get; init; }
}

public static partial class CharacterCareerComplexFormLearningTransaction
{
    private static bool IsValidCatalogBinding(CharacterCareerComplexFormLearningCatalogBinding? binding, CharacterWorkspaceId workspaceId)
        => binding is null || binding.WorkspaceId == workspaceId
            && binding.OwnerId is { Length: > 0 and <= 512 } && new OwnerScope(binding.OwnerId).NormalizedValue == binding.OwnerId
            && binding.WorkspaceRevision is > 0 and < long.MaxValue && binding.SettingsProfileId is { Length: > 0 and <= 512 }
            && IsDigest(binding.SourceDigest) && IsDigest(binding.AuxiliaryStateDigest) && IsDigest(binding.RuntimeDigest)
            && IsDigest(binding.ProfileDigest) && IsDigest(binding.CatalogSourceDigest) && IsDigest(binding.ContextDigest);

    private static bool TryReadCatalogCore(OwnerScope owner, WorkspaceStoredDocument saved, ICharacterSourceDataContext context,
        string runtimeDigest, out CharacterCareerComplexFormLearningCatalogSnapshot? snapshot, out string error,
        CharacterCurrentRuleEnvironmentConsumers.LearningAuthority5703? learning = null)
    {
        snapshot = null;
        error = "complex_form_learning_catalog_unavailable";
        try
        {
            Need(IsValidOwner(owner) && saved.ContentRevision is > 0 and < long.MaxValue
                && saved.SavedRevision == saved.ContentRevision && IsDigest(runtimeDigest), "complex_form_learning_workspace_invalid");
            Need(learning is null || learning.Matches(saved, context), "complex_form_learning_current_changed");
            Need(CharacterCareerPowerGovernance.Allows(saved), "complex_form_learning_requires_local_saved_sr5_career");
            var root = Parse(saved.Document.Content, "character");
            Need(Flag(root, "created") && !Flag(root, "ignorerules", false), "complex_form_learning_requires_rules_career");
            Need(context.TryResolveCareerSettingsProfile(out var profile, out var settingsAuthority)
                && !string.IsNullOrWhiteSpace(settingsAuthority), "complex_form_learning_profile_unavailable");
            var settings = Parse(profile, "settings");
            string profileId = Scalar(root, "settings");
            Need(CharacterCareerSettingsProfileBinding.MatchesSelectedProfile(profileId, settings, context, "complex_form_learning"),
                "complex_form_learning_profile_binding_invalid");
            Need(context is ICharacterCareerComplexFormLearningCatalogSourceContext, "complex_form_learning_catalog_capability_unavailable");
            Need(((ICharacterCareerComplexFormLearningCatalogSourceContext)context).TryResolveCareerComplexFormLearningCatalog(out var sources)
                && sources is not null && sources.Sources.Count <= 4096 && !string.IsNullOrWhiteSpace(sources.AuthorityState),
                "complex_form_learning_catalog_source_unavailable");
            // Unsupported eligibility remains visible as entry blockers; saved flags never confer authority.
            var resonance = learning is null ? CharacterCareerResonanceEvaluator.Evaluate(root, context)
                : learning.Resonance ?? throw new InvalidDataException("complex_form_learning_imported_resonance_unavailable");
            var binding = new CharacterCareerComplexFormLearningCatalogBinding(owner.NormalizedValue, saved.Id, saved.ContentRevision,
                SourceDigest(saved.Document), saved.Document.AuxiliaryStateDigest, runtimeDigest, profileId,
                Hash(settingsAuthority), Hash(JsonSerializer.Serialize(sources)), "");
            binding = binding with { ContextDigest = Hash("sr5-career-complex-form-learning-catalog-context/v1\0"
                + JsonSerializer.Serialize(new { Binding = binding, Resonance = resonance })) };
            snapshot = new(binding, sources!, context) { Current5703 = learning };
            error = "";
            return true;
        }
        catch (InvalidDataException e) { error = e.Message; return false; }
        catch (Exception e) when (Malformed(e) || e is IOException or UnauthorizedAccessException) { return false; }
    }

    internal static bool TryCatalog(OwnerScope owner, WorkspaceStoredDocument saved, ICharacterSourceDataResolver sources,
        string runtimeDigest, out CharacterCareerComplexFormLearningCatalogSnapshot? snapshot,
        out CharacterCareerComplexFormLearningCatalog? catalog, out string error, CharacterCurrentRuleEnvironmentContext? current = null)
    {
        snapshot = null;
        catalog = null;
        error = "complex_form_learning_catalog_source_unavailable";
        try
        {
            var context = sources.TryCreateContext(saved.Document.Content);
            var learning = context is null ? null : CharacterCurrentRuleEnvironmentConsumers.CreateLearningAuthority5703(
                saved, current, context, true);
            if (context is null || !TryReadCatalogCore(owner, saved, context, runtimeDigest, out snapshot, out error, learning)) return false;
            var entries = new List<CharacterCareerComplexFormLearningCatalogEntry>();
            foreach (var row in snapshot!.Sources.Sources)
            {
                bool available = false;
                int? cost = null;
                // Query-only feasibility probe. This stable identity/date is never a persisted user operation.
                var request = new CharacterCareerComplexFormLearningRequest(saved.Id,
                    new Guid(Hash("sr5-complex-form-catalog-probe/v1\0" + snapshot.Binding.ContextDigest + "\0" + row.SourceId)[..32]),
                    row.SourceId, new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Unspecified));
                if (TryReadCore(owner, saved, request, context, runtimeDigest, out var evaluated, out var blocker, learning))
                {
                    Need(evaluated!.Quote.ComplexFormXml == row.ComplexFormXml, "complex_form_learning_catalog_source_changed");
                    available = TryPreview(evaluated, request with { CatalogBinding = snapshot.Binding }, out var preview, out blocker);
                    if (available && !TryBuild(owner, saved, evaluated, preview!.Command with { ExplicitlyConfirmed = true }, out _, out _))
                    {
                        available = false;
                        blocker = "complex_form_learning_edit_unavailable";
                    }
                    if (available) cost = evaluated.Quote.KarmaCost;
                }
                IReadOnlyList<string> blockers = available ? Array.Empty<string>() : Array.AsReadOnly(new[] { blocker });
                entries.Add(new(row.SourceId, row.Name, row.Book, row.Page, available, cost, blockers));
            }
            catalog = new(snapshot.Binding, snapshot.Sources.EnabledSourcebooks, Array.AsReadOnly(entries.ToArray()));
            error = "";
            return true;
        }
        catch (InvalidDataException e) { error = e.Message; return false; }
        catch (Exception e) when (Malformed(e) || e is IOException or UnauthorizedAccessException) { return false; }
    }

    internal static bool IsCatalogCurrent(OwnerScope owner, WorkspaceStoredDocument saved, ICharacterSourceDataResolver sources,
        CharacterCareerComplexFormLearningCatalogSnapshot snapshot)
    {
        try
        {
            if (!TryReadCatalogCore(owner, saved, snapshot.Context, snapshot.Binding.RuntimeDigest, out var retained, out _, snapshot.Current5703)
                || retained!.Binding != snapshot.Binding) return false;
            var context = sources.TryCreateContext(saved.Document.Content);
            return context is not null && TryReadCatalogCore(owner, saved, context, snapshot.Binding.RuntimeDigest, out var fresh, out _, context is null ? null
                    : CharacterCurrentRuleEnvironmentConsumers.CreateLearningAuthority5703(saved,
                        snapshot.Current5703?.Current, context, true))
                && fresh!.Binding == snapshot.Binding
                && TryReadCatalogCore(owner, saved, snapshot.Context, snapshot.Binding.RuntimeDigest, out retained, out _, snapshot.Current5703)
                && retained!.Binding == snapshot.Binding;
        }
        catch (Exception e) when (Malformed(e) || e is IOException or UnauthorizedAccessException) { return false; }
    }
}

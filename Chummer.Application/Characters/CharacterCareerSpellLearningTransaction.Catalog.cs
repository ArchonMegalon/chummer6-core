using System.Text.Json;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

internal sealed record CharacterCareerSpellLearningCatalogSnapshot(
    CharacterCareerSpellLearningCatalogBinding Binding, CharacterCareerSpellLearningSourceCatalog Sources,
    ICharacterSourceDataContext Context)
{
    internal CharacterCurrentRuleEnvironmentConsumers.LearningAuthority5703? Current5703 { get; init; }
}

public static partial class CharacterCareerSpellLearningTransaction
{
    private static CharacterCareerSpellLearningKind? CatalogKind(string category) => category switch
    {
        "Rituals" => CharacterCareerSpellLearningKind.Ritual,
        "Combat" or "Detection" or "Health" or "Illusion" or "Manipulation" => CharacterCareerSpellLearningKind.Spell,
        _ => null
    };

    private static bool IsValidCatalogBinding(CharacterCareerSpellLearningCatalogBinding? binding,
        CharacterWorkspaceId workspaceId)
        => binding is null || binding.WorkspaceId == workspaceId
            && binding.OwnerId is { Length: > 0 and <= 512 }
            && new OwnerScope(binding.OwnerId).NormalizedValue == binding.OwnerId
            && binding.WorkspaceRevision is > 0 and < long.MaxValue
            && binding.SettingsProfileId is { Length: > 0 and <= 512 }
            && IsDigest(binding.SourceDigest) && IsDigest(binding.AuxiliaryStateDigest)
            && IsDigest(binding.RuntimeDigest) && IsDigest(binding.ProfileDigest)
            && IsDigest(binding.CatalogSourceDigest) && IsDigest(binding.ContextDigest);

    private static bool TryReadCatalogCore(OwnerScope owner, WorkspaceStoredDocument saved,
        ICharacterSourceDataContext context, string runtimeDigest,
        out CharacterCareerSpellLearningCatalogSnapshot? snapshot, out string error,
        CharacterCurrentRuleEnvironmentConsumers.LearningAuthority5703? learning = null)
    {
        snapshot = null;
        error = "spell_learning_catalog_unavailable";
        try
        {
            Need(IsValidOwner(owner) && saved.ContentRevision is > 0 and < long.MaxValue
                && saved.SavedRevision == saved.ContentRevision && IsDigest(runtimeDigest), "spell_learning_workspace_invalid");
            Need(learning is null || learning.Matches(saved, context), "spell_learning_current_changed");
            Need(CharacterCareerPowerGovernance.Allows(saved), "spell_learning_requires_local_saved_sr5_career");
            var root = Parse(saved.Document.Content, "character");
            Need(Flag(root, "created") && !Flag(root, "ignorerules", false), "spell_learning_requires_rules_career");
            Need(context.TryResolveCareerSettingsProfile(out var profile, out var settingsAuthority)
                && !string.IsNullOrWhiteSpace(settingsAuthority), "spell_learning_profile_unavailable");
            var settings = Parse(profile, "settings");
            string profileId = Scalar(root, "settings");
            Need(CharacterCareerSettingsProfileBinding.MatchesSelectedProfile(profileId, settings, context, "spell_learning"),
                "spell_learning_profile_binding_invalid");
            Need(context is ICharacterCareerSpellLearningCatalogSourceContext,
                "spell_learning_catalog_capability_unavailable");
            Need(((ICharacterCareerSpellLearningCatalogSourceContext)context).TryResolveCareerSpellLearningCatalog(out var sources)
                && sources is not null && sources.Sources.Count <= 4096 && !string.IsNullOrWhiteSpace(sources.AuthorityState),
                "spell_learning_catalog_source_unavailable");
            // The same Magic evaluator includes external Essence authority in its digest. Unsupported Magic
            // remains visible through entry blockers; no character flag becomes a grant to learn.
            var magic = learning is null ? CharacterCareerMagicEvaluator.Evaluate(root, settings, sourceContext: context)
                : learning.Magic ?? throw new InvalidDataException("spell_learning_imported_magic_unavailable");
            var binding = new CharacterCareerSpellLearningCatalogBinding(owner.NormalizedValue, saved.Id,
                saved.ContentRevision, SourceDigest(saved.Document), saved.Document.AuxiliaryStateDigest, runtimeDigest,
                profileId, Hash(settingsAuthority), Hash(JsonSerializer.Serialize(sources)), "");
            binding = binding with { ContextDigest = Hash("sr5-career-spell-learning-catalog-context/v1\0"
                + JsonSerializer.Serialize(new { Binding = binding, Magic = magic })) };
            snapshot = new(binding, sources!, context) { Current5703 = learning };
            error = "";
            return true;
        }
        catch (InvalidDataException e) { error = e.Message; return false; }
        catch (Exception e) when (Malformed(e) || e is IOException or UnauthorizedAccessException) { return false; }
    }

    internal static bool TryCatalog(OwnerScope owner, WorkspaceStoredDocument saved, ICharacterSourceDataResolver sources,
        string runtimeDigest, out CharacterCareerSpellLearningCatalogSnapshot? snapshot,
        out CharacterCareerSpellLearningCatalog? catalog, out string error, CharacterCurrentRuleEnvironmentContext? current = null)
    {
        snapshot = null;
        catalog = null;
        error = "spell_learning_catalog_source_unavailable";
        try
        {
            var context = sources.TryCreateContext(saved.Document.Content);
            var learning = context is null ? null : CharacterCurrentRuleEnvironmentConsumers.CreateLearningAuthority5703(
                saved, current, context, false);
            if (context is null || !TryReadCatalogCore(owner, saved, context, runtimeDigest, out snapshot, out error, learning)) return false;
            var entries = new List<CharacterCareerSpellLearningCatalogEntry>();
            foreach (var row in snapshot!.Sources.Sources)
            {
                var kind = CatalogKind(row.Category);
                bool available = false;
                int? cost = null;
                string blocker = "spell_learning_category_mismatch";
                if (kind is { } knownKind)
                {
                    // Query-only probe of the exact Preview evaluator and detached edit feasibility.
                    // This stable identity/date is internal, never returned as an operation or persisted.
                    var request = new CharacterCareerSpellLearningRequest(saved.Id,
                        new Guid(Hash("sr5-spell-catalog-probe/v1\0" + snapshot.Binding.ContextDigest + "\0" + row.SourceId)[..32]),
                        row.SourceId, knownKind, new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Unspecified));
                    if (TryReadCore(owner, saved, request, context, runtimeDigest, out var evaluated, out blocker, learning))
                    {
                        Need(evaluated!.Quote.SpellXml == row.SpellXml, "spell_learning_catalog_source_changed");
                        available = TryPreview(evaluated, request with { CatalogBinding = snapshot.Binding }, out var preview, out blocker);
                        if (available && !TryBuild(owner, saved, evaluated,
                                preview!.Command with { ExplicitlyConfirmed = true }, out _, out _))
                        {
                            available = false;
                            blocker = "spell_learning_edit_unavailable";
                        }
                        if (available) cost = evaluated.Quote.KarmaCost;
                    }
                }
                IReadOnlyList<string> blockers = available ? Array.Empty<string>() : Array.AsReadOnly(new[] { blocker });
                var options = new List<CharacterCareerSpellLearningCatalogOption>
                    { new(CharacterCareerSpellLearningOptionKind.Ordinary, available, blockers) };
                foreach (var option in new[] { CharacterCareerSpellLearningOptionKind.Limited,
                    CharacterCareerSpellLearningOptionKind.Extended, CharacterCareerSpellLearningOptionKind.Alchemical,
                    CharacterCareerSpellLearningOptionKind.Free })
                    options.Add(new(option, false, Array.AsReadOnly(new[] { "spell_learning_variant_unsupported" })));
                entries.Add(new(row.SourceId, row.Name, kind, row.Category, row.Book, row.Page,
                    available, cost, blockers, Array.AsReadOnly(options.ToArray())));
            }
            catalog = new(snapshot.Binding, snapshot.Sources.EnabledSourcebooks,
                Array.AsReadOnly(entries.Select(row => row.Category).Where(category => category.Length > 0)
                    .Distinct(StringComparer.Ordinal).OrderBy(category => category, StringComparer.Ordinal).ToArray()),
                Array.AsReadOnly(entries.ToArray()));
            error = "";
            return true;
        }
        catch (InvalidDataException e) { error = e.Message; return false; }
        catch (Exception e) when (Malformed(e) || e is IOException or UnauthorizedAccessException) { return false; }
    }

    internal static bool IsCatalogCurrent(OwnerScope owner, WorkspaceStoredDocument saved,
        ICharacterSourceDataResolver sources, CharacterCareerSpellLearningCatalogSnapshot snapshot)
    {
        try
        {
            // Re-admit retained, fresh, retained inputs, just as Preview does for an individual source.
            if (!TryReadCatalogCore(owner, saved, snapshot.Context, snapshot.Binding.RuntimeDigest, out var retained, out _, snapshot.Current5703)
                || retained!.Binding != snapshot.Binding) return false;
            var context = sources.TryCreateContext(saved.Document.Content);
            return context is not null
                && TryReadCatalogCore(owner, saved, context, snapshot.Binding.RuntimeDigest, out var fresh, out _, context is null ? null
                    : CharacterCurrentRuleEnvironmentConsumers.CreateLearningAuthority5703(saved,
                        snapshot.Current5703?.Current, context, false))
                && fresh!.Binding == snapshot.Binding
                && TryReadCatalogCore(owner, saved, snapshot.Context, snapshot.Binding.RuntimeDigest, out retained, out _, snapshot.Current5703)
                && retained!.Binding == snapshot.Binding;
        }
        catch (Exception e) when (Malformed(e) || e is IOException or UnauthorizedAccessException) { return false; }
    }
}

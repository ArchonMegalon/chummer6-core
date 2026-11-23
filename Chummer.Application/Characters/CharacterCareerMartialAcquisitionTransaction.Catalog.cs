using System.Text.Json;
using Chummer.Application.Owners;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

public static partial class CharacterCareerMartialAcquisitionTransaction
{
    private static bool IsValidCatalogBinding(CharacterCareerMartialAcquisitionCatalogBinding? b, CharacterWorkspaceId id)
        => b is null || b.WorkspaceId == id && b.OwnerId is { Length: > 0 and <= 512 }
            && new OwnerScope(b.OwnerId).NormalizedValue == b.OwnerId && !string.IsNullOrWhiteSpace(b.OwnerAuthorityInstanceId)
            && b.OwnerTransitionRevision >= 0 && b.WorkspaceRevision is > 0 and < long.MaxValue
            && Guid.TryParseExact(b.LocalHistoryIncarnationId, "N", out var incarnation) && incarnation != Guid.Empty
            && IsDigest(b.SourceDigest) && IsDigest(b.AuxiliaryStateDigest) && IsDigest(b.CatalogDigest) && IsDigest(b.RuntimeDigest);

    private static CharacterCareerMartialAcquisitionCatalogBinding CatalogBinding(OwnerContextStamp owner,
        WorkspaceStoredDocument saved, ICharacterSourceDataContext context, string runtime,
        out CharacterCareerMartialAcquisitionSourceCatalog catalog)
    {
        Need(context is ICharacterCareerMartialAcquisitionSourceContext, "martial_acquisition_catalog_capability_unavailable");
        Need(((ICharacterCareerMartialAcquisitionSourceContext)context).TryResolveCareerMartialAcquisitionCatalog(out var rows)
            && rows is not null && rows.Sources.Count <= 4096 && !string.IsNullOrWhiteSpace(rows.AuthorityState),
            "martial_acquisition_catalog_unavailable");
        Need(context.TryResolveCareerSettingsProfile(out var profile, out var authority)
            && !string.IsNullOrWhiteSpace(authority), "martial_acquisition_profile_unavailable");
        var settings = Parse(profile, "settings");
        Need(Scalar(Parse(saved.Document.Content, "character"), "settings") == Scalar(settings, "id"),
            "martial_acquisition_profile_binding_invalid");
        catalog = rows!;
        return new(owner.Owner.NormalizedValue, owner.Owner.IsLocalSingleUser, owner.AuthorityInstanceId, owner.TransitionRevision,
            saved.Id, saved.ContentRevision, saved.LocalHistory!.IncarnationId, SourceDigest(saved.Document),
            saved.Document.AuxiliaryStateDigest, Hash(JsonSerializer.Serialize(new { rows, authority })), runtime);
    }
    public static bool TryCatalog(OwnerContextStamp owner, WorkspaceStoredDocument saved, ICharacterSourceDataResolver sources,
        string runtime, out IReadOnlyList<CharacterCareerMartialAcquisitionEntry>? entries, out string error)
    {
        entries = null; error = "martial_acquisition_catalog_unavailable";
        try
        {
            Need(owner.IsValid && IsValidHistory(saved) && CharacterCareerPowerGovernance.Allows(saved)
                && saved.SavedRevision == saved.ContentRevision, "martial_acquisition_workspace_unavailable");
            var context = sources.TryCreateContext(saved.Document.Content);
            Need(context is not null, error);
            var binding = CatalogBinding(owner, saved, context!, runtime, out var catalog);
            var rows = new List<CharacterCareerMartialAcquisitionEntry>();
            var root = Parse(saved.Document.Content, "character");
            void Add(CharacterCareerMartialAcquisitionCatalogSource source, Guid? parentId, string parentName)
            {
                Need(rows.Count < 4096, "martial_acquisition_catalog_too_large");
                var row = Parse(source.Xml, source.Kind == CharacterCareerMartialAcquisitionKind.Art ? "martialart" : "technique");
                var request = new CharacterCareerMartialAcquisitionRequest(saved.Id,
                    new Guid(Hash("sr5-martial-catalog-probe/v1\0" + binding.CatalogDigest + "\0" + source.SourceId + "\0" + parentId)[..32]),
                    source.SourceId, source.Kind, parentId, new DateTime(2000, 1, 1));
                bool available = TryReadCore(owner, saved, request, context!, runtime, out var snapshot, out string blocker);
                int? cost = null;
                if (available)
                {
                    Need(snapshot!.Quote.SourceXml == source.Xml, "martial_acquisition_catalog_source_changed");
                    available = TryPreview(snapshot, request with { CatalogBinding = binding }, out var preview)
                        && TryBuild(owner, saved, snapshot, preview!.Command with { ExplicitlyConfirmed = true }, out _, out _);
                    if (available) cost = snapshot.Quote.KarmaCost; else blocker = "martial_acquisition_preview_unavailable";
                }
                rows.Add(new(source.SourceId, source.Kind, parentId, Scalar(row, "name"), parentName,
                    Scalar(row, "source"), Scalar(row, "page"), cost, available, available ? [] : [blocker], binding));
            }
            foreach (var row in catalog.Sources.Where(x => x.Kind == CharacterCareerMartialAcquisitionKind.Art)) Add(row, null, "");
            foreach (var art in MartialArts(root))
            {
                var canonical = catalog.Sources.SingleOrDefault(x => x.Kind == CharacterCareerMartialAcquisitionKind.Art
                    && x.SourceId == GuidValue(art, "sourceid"));
                if (canonical is null) continue; // missing source is not synthesized into a selectable technique.
                var parent = Parse(canonical.Xml, "martialart");
                bool first = !Box(art, "martialartadvantages").HasElements && !Box(art, "martialarttechniques").HasElements;
                foreach (var technique in catalog.Sources.Where(x => x.Kind == CharacterCareerMartialAcquisitionKind.Technique
                    && AllowsTechnique(parent, Scalar(Parse(x.Xml, "technique"), "name"), first)))
                    Add(technique, GuidValue(art, "guid"), Scalar(art, "name"));
            }
            Need(CatalogBinding(owner, saved, context!, runtime, out _) == binding, "martial_acquisition_catalog_changed");
            var fresh = sources.TryCreateContext(saved.Document.Content);
            Need(fresh is not null && CatalogBinding(owner, saved, fresh, runtime, out _) == binding,
                "martial_acquisition_catalog_changed");
            entries = rows.AsReadOnly(); error = ""; return true;
        }
        catch (InvalidDataException e) { error = e.Message; return false; }
        catch (Exception e) when (Malformed(e) || e is IOException or UnauthorizedAccessException) { return false; }
    }
}

using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Rulesets;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

/// <summary>
/// Source-owned, read-only entry to Karma foundation. Never converts Karma into
/// a Priority or Life Modules draft, writes character XML, or grants free points.
/// Quotes apply only to the empty, bound bootstrap; later spending is not ignored.
/// </summary>
public sealed class CharacterCreationKarmaMetatypeService(
    IWorkspaceStore workspaceStore,
    ICharacterSourceDataResolver sourceDataResolver)
{
    private readonly IWorkspaceStore _workspaceStore = workspaceStore
        ?? throw new ArgumentNullException(nameof(workspaceStore));
    private readonly ICharacterSourceDataResolver _sourceDataResolver = sourceDataResolver
        ?? throw new ArgumentNullException(nameof(sourceDataResolver));

    public CharacterCreationFoundationResult<CharacterCreationKarmaMetatypeState> Load(
        CharacterWorkspaceId workspaceId)
    {
        WorkspaceStoreReadResult read = _workspaceStore.Get(workspaceId);
        if (!read.Success || read.Value is not { } workspace)
            return Blocked<CharacterCreationKarmaMetatypeState>(
                CharacterCreationKarmaMetatypeBlockers.WorkspaceUnavailable);

        WorkspaceDocument document = workspace.Document;
        CharacterCreationBootstrapBinding? bootstrap = document.AuxiliaryState
            .CharacterCreationBootstrapBinding;
        // This slice quotes the first selection only. Never reset a partially
        // spent budget by overlooking an existing draft, receipt or archive.
        if (bootstrap is null
            || document.RulesetId != RulesetDefaults.Sr5
            || bootstrap.BuildMethod != CharacterCreationBuildMethods.Karma
            || !Equals(document.AuxiliaryState,
                new WorkspaceDocumentAuxiliaryState(CharacterCreationBootstrapBinding: bootstrap)))
        {
            return Blocked<CharacterCreationKarmaMetatypeState>(
                CharacterCreationKarmaMetatypeBlockers.PendingKarmaBootstrapRequired);
        }

        try
        {
            ICharacterSourceDataContext? context = _sourceDataResolver.TryCreateContext(document.Content);
            if (context is null
                || !CharacterCreationBootstrapAuthority.TryPrepareBinding(
                    workspaceId, document, context, out var currentBootstrap, out _, out _)
                || !CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(
                    bootstrap, currentBootstrap))
            {
                return Blocked<CharacterCreationKarmaMetatypeState>(
                    CharacterCreationKarmaMetatypeBlockers.PendingKarmaBootstrapRequired);
            }

            if (!context.TryResolveCreationSourceProfile(out var profile)
                || profile.BuildMethod != CharacterCreationBuildMethods.Karma
                || profile.SettingsProfileId != bootstrap.SettingsProfileId
                || profile.BuildPoints is not >= 0
                || !CharacterCreationPrerequisiteAuthorityDigest.IsCanonical(profile.RawProfileInputsDigest)
                // The existing profile DTO evaluates a Life Modules budget.
                // Only its exact method-mismatch is inapplicable to this Karma
                // quote. Malformed/duplicate/absent buildpoints still block it.
                || profile.BudgetBlockers.Any(blocker => blocker !=
                    CharacterCreationFoundationBlockers.LifeModuleBudgetProfileBuildMethodMismatch))
            {
                return Blocked<CharacterCreationKarmaMetatypeState>(
                    CharacterCreationKarmaMetatypeBlockers.BudgetAuthorityRequired);
            }

            if (!context.TryResolveCreationMetatypeCatalog(out var catalog)
                || !catalog.IsAuthoritative || catalog.Blockers.Count != 0
                || catalog.Schema != CharacterCreationMetatypeCatalogSchemas.CatalogV1
                || !catalog.SourceContext.IsAuthoritative
                || catalog.SourceContext.Blockers.Count != 0
                || catalog.SourceContext.SettingsProfileId != profile.SettingsProfileId
                || catalog.SourceContext.RawProfileInputsDigest != profile.RawProfileInputsDigest
                || !CharacterCreationPrerequisiteAuthorityDigest.IsCanonical(catalog.SourceContext.AuthorityDigest)
                || catalog.Options.Count == 0
                || catalog.Options.GroupBy(option => option.OptionId, StringComparer.Ordinal)
                    .Any(group => group.Count() != 1)
                || catalog.Options.Any(option => option.KarmaCost < 0 || option.SourceAnchorIds.Count == 0))
            {
                return Blocked<CharacterCreationKarmaMetatypeState>(
                    CharacterCreationKarmaMetatypeBlockers.MetatypeAuthorityRequired);
            }

            // Admit the source capture again after catalog resolution, then the
            // persisted workspace. A source or workspace changed during load is
            // not a usable UI selection even if its initial read was valid.
            if (!CharacterCreationBootstrapAuthority.TryPrepareBinding(
                    workspaceId, document, context, out var finalBootstrap, out _, out _)
                || !CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(
                    bootstrap, finalBootstrap))
            {
                return Blocked<CharacterCreationKarmaMetatypeState>(
                    CharacterCreationKarmaMetatypeBlockers.StaleBinding);
            }
            WorkspaceStoreReadResult finalRead = _workspaceStore.Get(workspaceId);
            if (!finalRead.Success || finalRead.Value is not { } finalWorkspace
                || finalWorkspace.ContentRevision != workspace.ContentRevision
                || finalWorkspace.SavedRevision != workspace.SavedRevision
                || finalWorkspace.Document.Content != document.Content
                || finalWorkspace.Document.AuxiliaryStateDigest != document.AuxiliaryStateDigest)
            {
                return Blocked<CharacterCreationKarmaMetatypeState>(
                    CharacterCreationKarmaMetatypeBlockers.StaleBinding);
            }

            var binding = new CharacterCreationKarmaMetatypeBinding(
                workspaceId, workspace.ContentRevision, workspace.SavedRevision,
                CharacterCreationFoundationDraftLedgerIntegrity.ComputeRawCharacterXmlDigest(document.Content),
                document.AuxiliaryStateDigest,
                bootstrap.BindingDigest,
                profile.RawProfileInputsDigest, catalog.SourceContext.AuthorityDigest);
            string[] anchors = profile.SourceAnchorIds.Concat(catalog.SourceContext.SourceAnchorIds)
                .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            var state = new CharacterCreationKarmaMetatypeState(
                CharacterCreationKarmaMetatypeSchemas.SnapshotV1, binding, profile.SettingsProfileId,
                Budget(profile.BuildPoints.Value, 0, []), catalog.Options.ToArray(), anchors, string.Empty);
            state = state with
            {
                SnapshotDigest = CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(state)
            };
            return new(CharacterCreationFoundationOutcomes.Success, state, []);
        }
        catch (Exception error) when (error is ArgumentException or FormatException
            or IOException or InvalidOperationException or UnauthorizedAccessException
            or System.Xml.XmlException)
        {
            return Blocked<CharacterCreationKarmaMetatypeState>(
                CharacterCreationKarmaMetatypeBlockers.MetatypeAuthorityRequired);
        }
    }

    public CharacterCreationFoundationResult<CharacterCreationKarmaMetatypeQuote> Preview(
        CharacterCreationKarmaMetatypeBinding binding, string metatypeOptionId)
    {
        ArgumentNullException.ThrowIfNull(binding);
        CharacterCreationFoundationResult<CharacterCreationKarmaMetatypeState> loaded = Load(binding.WorkspaceId);
        if (loaded.Value is not { } state)
            return new(loaded.Outcome, null, loaded.Blockers);
        if (state.Binding != binding)
            return Blocked<CharacterCreationKarmaMetatypeQuote>(CharacterCreationKarmaMetatypeBlockers.StaleBinding);

        CharacterCreationMetatypeOptionProjection? option = state.Options.SingleOrDefault(
            candidate => string.Equals(candidate.OptionId, metatypeOptionId, StringComparison.Ordinal));
        if (option is not { IsEnabled: true } || option.Blockers.Count != 0)
            return Blocked<CharacterCreationKarmaMetatypeQuote>(CharacterCreationKarmaMetatypeBlockers.OptionUnavailable);

        string[] blockers = option.KarmaCost <= state.KarmaBudget.Total
            ? [] : [CharacterCreationKarmaMetatypeBlockers.BudgetExceeded];
        var quote = new CharacterCreationKarmaMetatypeQuote(
            CharacterCreationKarmaMetatypeSchemas.QuoteV1, state.Binding, state.SnapshotDigest,
            option, Budget((int)state.KarmaBudget.Total, option.KarmaCost, blockers), blockers.Length == 0,
            blockers, state.SourceAnchorIds.Concat(option.SourceAnchorIds)
                .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(), string.Empty);
        quote = quote with
        {
            QuoteDigest = CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(quote)
        };
        return new(CharacterCreationFoundationOutcomes.Success, quote, blockers);
    }

    private static CharacterCreationBudgetState Budget(int total, int used, IReadOnlyList<string> blockers)
        => new(CharacterCreationBudgetIds.Karma, "Creation Karma", total, used, total - used,
            true, blockers, "karma");

    private static CharacterCreationFoundationResult<T> Blocked<T>(string blocker) where T : class
        => new(CharacterCreationFoundationOutcomes.Blocked, null, [blocker]);
}

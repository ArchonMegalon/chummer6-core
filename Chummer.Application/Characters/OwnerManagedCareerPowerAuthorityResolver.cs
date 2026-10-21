using System.Xml;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;

namespace Chummer.Application.Characters;

/// <summary>
/// Bounded local Career policy. Campaign or unknown governance is denied here
/// as well as by the composed resolver and final transaction builder. Saved
/// source/profile bytes bind the operation; they never imply remote GM approval.
/// </summary>
public sealed class OwnerManagedCareerPowerAuthorityResolver(ICharacterSourceDataResolver sources)
    : ICharacterCareerPowerAuthorityResolver
{
    public ICharacterCareerPowerAdmission? Open(OwnerScope owner, WorkspaceStoredDocument saved,
        CharacterCareerPowerCommand command, string runtimeFingerprint)
        => OpenDetailed(owner, saved, command, runtimeFingerprint, out _);

    public ICharacterCareerPowerAdmission? OpenDetailed(OwnerScope owner, WorkspaceStoredDocument saved,
        CharacterCareerPowerCommand command, string runtimeFingerprint, out string? blocker)
    {
        blocker = "power_workspace_or_runtime_unavailable";
        if (!CharacterCareerQualityTransaction.IsValidOwner(owner) || saved is null || command is null
            || saved.Document.RulesetId != "sr5" || saved.ContentRevision <= 0
            || saved.ContentRevision != saved.SavedRevision || !CharacterCareerPowerTransaction.IsValidHistory(saved)
            || !CharacterCareerReputationTransaction.IsDigest(runtimeFingerprint)) return null;
        try
        {
            if (!CharacterCareerPowerGovernance.Allows(saved))
            { blocker = "power_campaign_authority_unavailable"; return null; }
            if (command.ReadBinding is { } binding && !CharacterCareerMagicReadBinding.MatchesSaved(binding, owner, saved))
            { blocker = "power_read_binding_changed"; return null; }
            var context = sources.TryCreateContext(saved.Document.Content);
            if (context is null) { blocker = "power_source_context_unavailable"; return null; }
            var graph = CharacterCareerPowerGraph.Build(saved.Document.Content, context, command);
            var plan = new CharacterCareerPowerPlan(owner.NormalizedValue, saved.Id.Value, saved.ContentRevision,
                saved.SavedRevision, CharacterCareerPowerTransaction.Hash(saved.Document.Content),
                saved.Document.AuxiliaryStateDigest, graph.AuthorityDigest, runtimeFingerprint, command,
                graph.Quote, CharacterCareerPowerTransaction.Hash(graph.Xml));
            if (!CharacterCareerPowerTransaction.IsValidPlan(plan)) { blocker = "power_plan_invalid"; return null; }
            blocker = null;
            return new Admission(sources, context, saved.Document.Content, plan, graph);
        }
        catch (Exception e) when (Unavailable(e))
        {
            blocker = e is InvalidDataException && e.Message.StartsWith("power_", StringComparison.Ordinal)
                ? e.Message : "power_source_semantics_unavailable";
            return null;
        }
    }

    private static bool Unavailable(Exception e) => e is XmlException or InvalidDataException or ArgumentException
        or InvalidOperationException or IOException or UnauthorizedAccessException or OverflowException;

    private sealed class Admission(ICharacterSourceDataResolver sources, ICharacterSourceDataContext retained,
        string original, CharacterCareerPowerPlan plan, CharacterCareerPowerGraph.Projection graph)
        : ICharacterCareerPowerAdmission
    {
        private bool disposed, invalidated;
        public CharacterCareerPowerPlan Plan => plan;
        public string CharacterXml => graph.Xml;
        public IReadOnlyList<CharacterCareerPowerGrant> Grants => graph.Grants;
        public bool IsCurrent()
        {
            if (disposed || invalidated) return false;
            try
            {
                // The retained context detects observed file ABA; the fresh
                // context detects changed overlay membership or selected profile.
                var current = CharacterCareerPowerGraph.Build(original, retained, plan.Command);
                var fresh = sources.TryCreateContext(original);
                bool valid = fresh is not null && Same(current)
                    && Same(CharacterCareerPowerGraph.Build(original, fresh, plan.Command));
                invalidated = !valid;
                return valid;
            }
            catch (Exception e) when (Unavailable(e)) { invalidated = true; return false; }
        }
        private bool Same(CharacterCareerPowerGraph.Projection value) =>
            value.AuthorityDigest == plan.ExpectedAuthorityDigest
            && CharacterCareerPowerTransaction.Hash(value.Xml) == plan.ResultCharacterDigest
            && CharacterCareerPowerTransaction.Same(value.Quote, plan.Quote)
            && CharacterCareerPowerTransaction.Same(value.Grants, graph.Grants);
        public void Dispose() => disposed = true;
    }
}

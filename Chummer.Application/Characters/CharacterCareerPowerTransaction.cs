using System.Text.Json;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

public static class CharacterCareerPowerTransaction
{
    public static string Hash(string value) => CharacterCareerReputationTransaction.Hash(value);
    public static bool Same<T>(T left, T right) => JsonSerializer.Serialize(left) == JsonSerializer.Serialize(right);
    public static string LedgerRoot => Hash("chummer.core.sr5-career-power-ledger/v1");
    public static string EntryDigest(CharacterCareerPowerEntry entry) =>
        Hash(JsonSerializer.Serialize(entry with { EntryDigest = "" }));

    public static bool IsValidPlan(CharacterCareerPowerPlan? p)
    {
        try
        {
            if (p is null || p.Command?.Identity is not { } id || p.Quote is not { } q
                || string.IsNullOrWhiteSpace(p.OwnerId) || string.IsNullOrWhiteSpace(p.WorkspaceId)
                || p.OwnerId.Length > 1024 || p.WorkspaceId.Length > 1024
                || p.ExpectedWorkspaceRevision is <= 0 or long.MaxValue
                || p.ExpectedSavedRevision != p.ExpectedWorkspaceRevision
                || p.Command.TransactionId == Guid.Empty || id.InternalId == Guid.Empty || id.SourceId == Guid.Empty
                || id.Extra is null || id.Extra.Length > 1024 || id.Extra != id.Extra.Trim()
                || p.Command.PaidRating is <= 0 or > 1000
                || q.Current is null || q.Proposed is null || !q.HasEnoughPoints
                || q.Current.PaidRating < 0 || q.Current.PaidRating >= p.Command.PaidRating
                || q.Proposed.PaidRating != p.Command.PaidRating
                || q.Current.TotalRating >= q.Proposed.TotalRating || q.Proposed.TotalRating > 1000
                || q.Current.UsedPoints < 0 || q.Proposed.UsedPoints < q.Current.UsedPoints
                || q.TotalPoints < 0 || q.OtherPowersUsedPoints < 0
                || q.CurrentRemainingPoints < 0 || q.ProposedRemainingPoints < 0
                || q.IncrementalCost != q.Proposed.UsedPoints - q.Current.UsedPoints
                || q.CurrentRemainingPoints != q.TotalPoints - q.OtherPowersUsedPoints - q.Current.UsedPoints
                || q.ProposedRemainingPoints != q.TotalPoints - q.OtherPowersUsedPoints - q.Proposed.UsedPoints)
                return false;
            if (p.Command.ReadBinding is { } binding && (binding.OwnerId != p.OwnerId
                || binding.WorkspaceId != p.WorkspaceId || string.IsNullOrWhiteSpace(binding.OwnerAuthorityInstanceId)
                || binding.OwnerTransitionRevision < 0 || binding.WorkspaceRevision != p.ExpectedWorkspaceRevision
                || binding.SavedRevision != p.ExpectedSavedRevision || binding.CharacterDigest != p.ExpectedCharacterDigest
                || binding.AuxiliaryDigest != p.ExpectedAuxiliaryDigest)) return false;
            return new[] { p.ExpectedCharacterDigest, p.ExpectedAuxiliaryDigest, p.ExpectedAuthorityDigest,
                p.ExpectedRuntimeFingerprint, p.ResultCharacterDigest }.All(CharacterCareerReputationTransaction.IsDigest);
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or OverflowException
            or NullReferenceException) { return false; }
    }

    public static bool IsValidLedger(CharacterWorkspaceId workspace, long revision,
        IReadOnlyList<CharacterCareerPowerEntry>? entries)
    {
        if (entries is null) return true;
        if (entries.Count > 1024) return false;
        var ids = new HashSet<Guid>();
        string previous = LedgerRoot;
        long last = 0, bytes = 0;
        foreach (var entry in entries)
        {
            if (entry is null || !IsValidPlan(entry.Plan) || entry.Plan.WorkspaceId != workspace.Value
                || !ids.Add(entry.Plan.Command.TransactionId) || entry.Plan.TargetWorkspaceRevision <= last
                || entry.Plan.TargetWorkspaceRevision > revision || entry.PreviousEntryDigest != previous
                || entry.Grants is null || entry.Grants.Count > 4096 || entry.Grants.Any(g => g is null
                    || g.InstanceId == Guid.Empty || g.SourceId == Guid.Empty || g.FreeLevels < 0
                    || g.FreePointQuarterUnits < 0 || g.PoolPoints < 0
                    || g.Kind is not ("Quality" or "MentorSpirit" or "Metamagic")
                    || g.PowerName is null || g.Extra is null || g.PowerName.Length > 1024 || g.Extra.Length > 1024)
                || EntryDigest(entry) != entry.EntryDigest) return false;
            bytes += JsonSerializer.SerializeToUtf8Bytes(entry).Length;
            if (bytes > 8 * 1024 * 1024) return false;
            last = entry.Plan.TargetWorkspaceRevision;
            previous = entry.EntryDigest;
        }
        return true;
    }

    public static bool IsValidHistory(WorkspaceStoredDocument saved)
    {
        var entries = saved.Document.AuxiliaryState.CharacterCareerPowerTransactions;
        return IsValidLedger(saved.Id, saved.ContentRevision, entries)
            && (entries is not { Count: > 0 } || saved.SavedRevision >= entries[^1].Plan.TargetSavedRevision
                && (saved.ContentRevision != entries[^1].Plan.TargetWorkspaceRevision
                    || Hash(saved.Document.Content) == entries[^1].Plan.ResultCharacterDigest));
    }

    public static CharacterCareerPowerResult Lookup(OwnerScope owner, WorkspaceStoredDocument saved,
        CharacterCareerPowerPlan plan)
    {
        if (!CharacterCareerQualityTransaction.IsValidOwner(owner) || !IsValidPlan(plan)
            || owner.NormalizedValue != plan.OwnerId || saved.Id.Value != plan.WorkspaceId)
            return new(CharacterCareerPowerOutcome.Conflict);
        if (!IsValidHistory(saved)) return new(CharacterCareerPowerOutcome.Corrupt, Error: "power_history_invalid");
        var entry = saved.Document.AuxiliaryState.CharacterCareerPowerTransactions?
            .SingleOrDefault(e => e.Plan.Command.TransactionId == plan.Command.TransactionId);
        if (entry is null) return new(CharacterCareerPowerOutcome.NotFound, CurrentRevision: saved.ContentRevision);
        if (!Same(entry.Plan, plan) || !saved.CanReplayReceipt(entry.Plan.TargetWorkspaceRevision))
            return new(CharacterCareerPowerOutcome.IdempotencyConflict, CurrentRevision: saved.ContentRevision);
        return new(CharacterCareerPowerOutcome.Replayed, entry, saved.ContentRevision);
    }

    public static bool TryBuild(OwnerScope owner, WorkspaceStoredDocument saved, CharacterCareerPowerPlan plan,
        ICharacterCareerPowerAdmission admission, string runtime, out WorkspaceDocument? replacement,
        out CharacterCareerPowerEntry? entry)
    {
        replacement = null;
        entry = null;
        if (Lookup(owner, saved, plan).Outcome != CharacterCareerPowerOutcome.NotFound
            || plan.Command.ReadBinding is not { } binding
            || !CharacterCareerMagicReadBinding.MatchesSaved(binding, owner, saved)
            || !CharacterCareerPowerGovernance.Allows(saved)
            || saved.Document.Format != WorkspaceDocumentFormat.NativeXml || saved.Document.RulesetId != "sr5"
            || saved.Document.SchemaVersion != 1 || saved.Document.PayloadKind is not ("workspace" or "sr5/chum5-xml")
            || saved.ContentRevision != plan.ExpectedWorkspaceRevision || saved.SavedRevision != plan.ExpectedSavedRevision
            || Hash(saved.Document.Content) != plan.ExpectedCharacterDigest
            || saved.Document.AuxiliaryStateDigest != plan.ExpectedAuxiliaryDigest
            || runtime != plan.ExpectedRuntimeFingerprint || !Same(plan, admission.Plan)
            || Hash(admission.CharacterXml) != plan.ResultCharacterDigest || !admission.IsCurrent()) return false;
        var history = saved.Document.AuxiliaryState.CharacterCareerPowerTransactions ?? [];
        var candidate = new CharacterCareerPowerEntry(plan, admission.Grants,
            history.Count == 0 ? LedgerRoot : history[^1].EntryDigest, "");
        candidate = candidate with { EntryDigest = EntryDigest(candidate) };
        var appended = history.Append(candidate).ToArray();
        if (!IsValidLedger(saved.Id, plan.TargetWorkspaceRevision, appended)) return false;
        replacement = saved.Document with { State = saved.Document.State with {
            Payload = admission.CharacterXml,
            AuxiliaryState = saved.Document.AuxiliaryState with {
                CharacterCareerPowerTransactions = Array.AsReadOnly(appended) } } };
        entry = candidate;
        return true;
    }
}

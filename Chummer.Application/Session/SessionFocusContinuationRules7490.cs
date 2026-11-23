using Chummer.Application.Characters;
using Chummer.Contracts.Session;
using static Chummer.Application.Session.SessionPlayEncoding;

namespace Chummer.Application.Session;

public static class SessionFocusContinuationRules7490
{
    public const int Maximum = 1024;
    public static void Validate(SessionFocusContinuationIntent7490 value)
    {
        var b = value.Before; var r = value.ExpectedReceipt; var c = r.Command;
        Conflict(value.Digest == Digest(value with { Digest = "" })
            && CharacterCareerFocusBondingTransaction.IsCoherent(r)
            && value.EventCount is >= 0 and <= SessionPlayLimits.MaxEvents
            && value.PlayOrdinal is >= 0 and <= SessionPlayLimits.MaxOperations
            && CharacterCareerFocusBondingTransaction.IsDigest(value.AfterAuxiliaryDigest)
            && CharacterCareerFocusBondingTransaction.IsDigest(value.AfterSourceEnvironment)
            && CharacterCareerFocusBondingTransaction.IsDigest(value.RuleEnvironmentDigest)
            && CharacterCareerFocusBondingTransaction.IsDigest(value.OriginDigest)
            && !string.IsNullOrWhiteSpace(b.SlotGeneration)
            && b.IncarnationId == c.Binding.LocalHistoryIncarnationId
            && b.ContentRevision == b.SavedRevision && b.ContentRevision == c.Binding.WorkspaceRevision
            && b.AuxiliaryDigest == c.Binding.AuxiliaryStateDigest
            && b.Authority.Binding.OwnerId == c.Binding.OwnerId
            && b.Authority.Binding.Version.CharacterId == c.Request.WorkspaceId.Value
            && b.Authority.Binding.Version.VersionId == b.ContentRevision.ToString(System.Globalization.CultureInfo.InvariantCulture)
            && r.CommittedWorkspaceRevision == b.ContentRevision + 1
            && value.After.Binding.OwnerId == b.Authority.Binding.OwnerId
            && value.After.Binding.Version == (b.Authority.Binding.Version with {
                VersionId = r.CommittedWorkspaceRevision.ToString(System.Globalization.CultureInfo.InvariantCulture) }),
            "focus-continuation-binding-divergence");
        foreach (var a in new[] { b.Authority, value.After })
            Conflict(a.Binding.Version.RulesetId == "sr5" && a.Binding.AuthorityDigest == Digest(a with {
                Binding = a.Binding with { AuthorityDigest = "" } }), "focus-continuation-authority-divergence");
    }
    public static SessionFocusContinuationIntent7490 Admitted(SessionFocusContinuationStored7490 row)
    {
        Validate(row.Intent);
        var chosen = row.Receipt?.Canonical.NeverApplied == true ? row.Retirement ?? row.Intent : row.Intent;
        Validate(chosen);
        Conflict(row.Receipt is not null && Digest(row.Receipt.Canonical) == Digest(chosen.ExpectedReceipt)
            && row.Receipt.Digest == Digest(row.Receipt with { Digest = "" })
            && row.Receipt.SourceEnvironment == chosen.AfterSourceEnvironment
            && row.Receipt.ContinuedAuthority.Binding.Version == chosen.After.Binding.Version
            && row.Receipt.ContinuedAuthority.Binding.SourceDigest == chosen.After.Binding.SourceDigest
            && row.Receipt.ContinuedAuthority.Binding.OwnerId == chosen.After.Binding.OwnerId
            && row.Receipt.ContinuedAuthority.Binding.AuthorityDigest == Digest(row.Receipt.ContinuedAuthority with {
                Binding = row.Receipt.ContinuedAuthority.Binding with { AuthorityDigest = "" } }),
            "focus-continuation-original-receipt-divergence");
        return chosen;
    }
    // Original recovery may hold a renewed lease for the SAME owner after process restart.
    // The command and canonical preimage stay immutable; the actual recovery lease is recorded separately in Before.
    public static bool SameCanonicalPreimage(SessionConditionTransferBinding left, SessionConditionTransferBinding right)
        => Digest(left) == Digest(right with { OwnerAuthorityDigest = left.OwnerAuthorityDigest });
    public static void ValidateRetirement(SessionFocusContinuationIntent7490 original, SessionFocusContinuationIntent7490 retirement)
    {
        Validate(original); Validate(retirement);
        Conflict(!original.ExpectedReceipt.NeverApplied && retirement.ExpectedReceipt.NeverApplied
            && original.ExpectedReceipt.CommandDigest == retirement.ExpectedReceipt.CommandDigest
            && original.OriginDigest == retirement.OriginDigest && original.Cursor == retirement.Cursor
            && original.EventCount == retirement.EventCount && original.PlayOrdinal == retirement.PlayOrdinal
            && SameCanonicalPreimage(original.Before, retirement.Before)
            && original.RuleEnvironmentDigest == retirement.RuleEnvironmentDigest,
            "focus-continuation-retirement-divergence");
    }
}

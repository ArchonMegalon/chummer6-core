using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Chummer.Contracts.Characters;

public enum CharacterCareerInitiationBenefitKind { Metamagic, PowerPoint }
public enum CharacterCareerInitiationOutcome
{
    NotFound, Applied, Replayed, Missing, Conflict, IdempotencyConflict, Corrupt, Unavailable
}

/// <summary>A choice, never caller-provided prices, effects, grades or permission.</summary>
public sealed record CharacterCareerInitiationSelection(
    Guid BenefitSourceId, bool Group = false, bool Ordeal = false,
    bool Schooling = false, bool AcceptObligations = false);

public sealed record CharacterCareerInitiationBinding(
    string OwnerId, string WorkspaceId, string OwnerAuthorityInstanceId, long OwnerTransitionRevision,
    long WorkspaceRevision, long SavedRevision, string DocumentDigest, string AuxiliaryDigest,
    string RuntimeFingerprint);

public sealed record CharacterCareerInitiationBenefit(
    Guid SourceId, string Name, string Book, string Page, string SourceXml,
    CharacterCareerInitiationBenefitKind Kind, decimal PowerPoints,
    IReadOnlyList<string> Blockers);

public sealed record CharacterCareerInitiationAdjustment(
    string Id, bool Selected, decimal Discount, decimal Nuyen,
    string Obligation, IReadOnlyList<string> Blockers);

public sealed record CharacterCareerInitiationQuote(
    CharacterCareerInitiationBinding Binding, CharacterCareerInitiationSelection Selection,
    int CurrentGrade, int NextGrade, int EffectiveMagic, int? EffectiveAdeptMagic,
    bool SeparateAdeptMagic, int KarmaBefore, decimal NuyenBefore,
    int KarmaCost, decimal NuyenCost, string SourceAuthorityDigest,
    IReadOnlyList<CharacterCareerInitiationAdjustment> Adjustments,
    IReadOnlyList<CharacterCareerInitiationBenefit> Benefits,
    IReadOnlyList<string> Obligations, IReadOnlyList<string> Blockers)
{
    public bool CanApply => Blockers.Count == 0;
}

/// <summary>The complete reviewed quote is compared with fresh store-side authority.</summary>
public sealed record CharacterCareerInitiationPlan(
    Guid TransactionId, CharacterCareerInitiationQuote Quote, DateTime ExpenseDateLocal)
{
    public string OwnerId => Quote.Binding.OwnerId;
    public string WorkspaceId => Quote.Binding.WorkspaceId;
    public long ExpectedWorkspaceRevision => Quote.Binding.WorkspaceRevision;
    public long ExpectedSavedRevision => Quote.Binding.SavedRevision;
    public long TargetWorkspaceRevision => checked(ExpectedWorkspaceRevision + 1);
    public long TargetSavedRevision => TargetWorkspaceRevision;
}

public sealed record CharacterCareerInitiationEntry(
    CharacterCareerInitiationPlan Plan, Guid GradeId, Guid BenefitId,
    Guid KarmaExpenseId, Guid? NuyenExpenseId, string CharacterDigestAfter,
    string PreviousEntryDigest, string EntryDigest);

public sealed record CharacterCareerInitiationResult(
    CharacterCareerInitiationOutcome Outcome, CharacterCareerInitiationEntry? Entry = null,
    long? CurrentRevision = null, string? Error = null);

public static class CharacterCareerInitiationRules
{
    public const int MaximumGrade = 1000;
    public static string Hash(string value)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    public static string Digest<T>(T value) => Hash(JsonSerializer.Serialize(value));
    public static bool Same<T>(T left, T right) => Digest(left) == Digest(right);
    public static bool IsDigest(string? value)
        => value is { Length: 64 } && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');

    // SR5 StandardRound: costs round away from zero, including non-half fractions.
    // Flat, per-grade and selected percentage inputs come from the effective profile.
    public static int Cost(decimal flat, decimal perGrade, int nextGrade, decimal discount)
    {
        if (flat < 0 || perGrade < 0 || nextGrade is < 1 or > MaximumGrade || discount is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(discount));
        return decimal.ToInt32(decimal.Ceiling(checked((flat + nextGrade * perGrade) * (1 - discount))));
    }

    public static bool IsValid(CharacterCareerInitiationPlan? plan)
    {
        try
        {
            if (plan is null || plan.TransactionId == Guid.Empty || plan.Quote is not { } q
                || q.Binding is not { } b || q.Selection is null || !q.CanApply
                || string.IsNullOrWhiteSpace(b.OwnerId) || string.IsNullOrWhiteSpace(b.WorkspaceId)
                || string.IsNullOrWhiteSpace(b.OwnerAuthorityInstanceId) || b.OwnerTransitionRevision < 0
                || b.WorkspaceRevision <= 0 || b.WorkspaceRevision >= long.MaxValue
                || b.SavedRevision != b.WorkspaceRevision
                || !IsDigest(b.DocumentDigest) || !IsDigest(b.AuxiliaryDigest) || !IsDigest(b.RuntimeFingerprint)
                || !IsDigest(q.SourceAuthorityDigest) || q.NextGrade != q.CurrentGrade + 1
                || q.CurrentGrade < 0 || q.NextGrade > MaximumGrade || q.NextGrade > q.EffectiveMagic
                || q.SeparateAdeptMagic && (q.EffectiveAdeptMagic is null || q.NextGrade > q.EffectiveAdeptMagic)
                || q.KarmaCost < 0 || q.KarmaBefore < q.KarmaCost || q.NuyenCost < 0 || q.NuyenBefore < q.NuyenCost
                || q.Benefits.Count > 4096 || q.Adjustments.Count != 3 || q.Obligations.Count > 32
                || q.Obligations.Count > 0 && !q.Selection.AcceptObligations
                || plan.ExpenseDateLocal.Kind != DateTimeKind.Unspecified
                || plan.ExpenseDateLocal.Year is < 1753 or > 9998)
                return false;
            var benefit = q.Benefits.SingleOrDefault(x => x.SourceId == q.Selection.BenefitSourceId);
            return benefit is not null && benefit.Blockers.Count == 0 && benefit.SourceId != Guid.Empty
                && benefit.SourceXml.Length is > 0 and <= 1_048_576
                && (benefit.Kind is CharacterCareerInitiationBenefitKind.Metamagic or CharacterCareerInitiationBenefitKind.PowerPoint)
                && (benefit.Kind == CharacterCareerInitiationBenefitKind.PowerPoint
                    ? benefit.PowerPoints > 0 : benefit.PowerPoints == 0);
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or NullReferenceException or OverflowException)
        { return false; }
    }

    public static Guid Id(Guid transaction, string purpose)
    {
        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(transaction.ToString("D") + "\0" + purpose));
        return new Guid(bytes.AsSpan(0, 16));
    }
}

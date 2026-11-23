using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Chummer.Contracts.Characters;

public enum CharacterCareerInitiationBenefitKind { Metamagic, PowerPoint, Echo }
public enum CharacterCareerInitiationFamily
{
    Magic,
    Resonance
}

public enum CharacterCareerInitiationMode
{
    GradeAndBenefit = 0,
    GradeOnly = 1,
    DeferredBenefit = 2
}

public enum CharacterCareerInitiationOutcome
{
    NotFound, Applied, Replayed, Missing, Conflict, IdempotencyConflict, Corrupt, Unavailable
}

/// <summary>A choice, never caller-provided prices, effects, grades or permission.</summary>
public sealed record CharacterCareerInitiationSelection(
    Guid BenefitSourceId, bool Group = false, bool Ordeal = false,
    bool Schooling = false, bool AcceptObligations = false)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public CharacterCareerInitiationMode Mode { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public Guid? TargetGradeId { get; init; }
}

/// <summary>Canonical saved grade and occupancy, not proof of an imported grade's past payment.</summary>
public sealed record CharacterCareerSubmersionGrade(
    Guid GradeId, int Grade, string GradeXml, bool FirstBenefitPending, IReadOnlyList<string> Blockers);

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

/// <summary>Exact owned instance and effect replacement, compiled from retained source at both grades.</summary>
public sealed record CharacterCareerInitiationMetamagicRecompilation(
    Guid InstanceId, Guid SourceId, string InstanceXml, string SourceXml,
    IReadOnlyList<string> EffectsBefore, IReadOnlyList<string> EffectsAfter);

public sealed record CharacterCareerInitiationQuote(
    CharacterCareerInitiationBinding Binding, CharacterCareerInitiationSelection Selection,
    int CurrentGrade, int NextGrade, int EffectiveMagic, int? EffectiveAdeptMagic,
    bool SeparateAdeptMagic, int KarmaBefore, decimal NuyenBefore,
    int KarmaCost, decimal NuyenCost, string SourceAuthorityDigest,
    IReadOnlyList<CharacterCareerInitiationAdjustment> Adjustments,
    IReadOnlyList<CharacterCareerInitiationBenefit> Benefits,
    IReadOnlyList<string> Obligations, IReadOnlyList<string> Blockers)
{
    // Omit for historical quotes so pre-extension receipt digests remain stable.
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<CharacterCareerInitiationMetamagicRecompilation>? RecompiledMetamagics { get; init; }

    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public CharacterCareerInitiationFamily Family { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public int? EffectiveResonance { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<CharacterCareerSubmersionGrade>? SubmersionGrades { get; init; }
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
                || !IsDigest(q.SourceAuthorityDigest) || !ValidMode(q)
                || q.CurrentGrade < 0 || q.NextGrade > MaximumGrade || (q.Family == CharacterCareerInitiationFamily.Resonance
                    ? q.EffectiveResonance is null || q.Selection.Mode != CharacterCareerInitiationMode.DeferredBenefit && q.NextGrade > q.EffectiveResonance
                    : q.NextGrade > q.EffectiveMagic)
                || q.SeparateAdeptMagic && (q.EffectiveAdeptMagic is null || q.NextGrade > q.EffectiveAdeptMagic)
                || q.KarmaCost < 0 || q.KarmaBefore < q.KarmaCost || q.NuyenCost < 0 || q.NuyenBefore < q.NuyenCost
                || q.Benefits.Count > 4096 || q.Adjustments.Count != 3 || q.Obligations.Count > 32
                || q.Obligations.Count > 0 && !q.Selection.AcceptObligations
                || plan.ExpenseDateLocal.Kind != DateTimeKind.Unspecified
                || plan.ExpenseDateLocal.Year is < 1753 or > 9998)
                return false;
            if (!Enum.IsDefined(q.Family)
                || q.Family == CharacterCareerInitiationFamily.Magic && q.EffectiveResonance is not null
                || q.Family == CharacterCareerInitiationFamily.Resonance
                    && (q.EffectiveMagic != 0 || q.EffectiveResonance is not > 0 || q.EffectiveAdeptMagic is not null
                        || q.SeparateAdeptMagic || !ValidSubmersionAdjustments(q)
                        || q.NuyenCost != 0
                        || q.Obligations.Count != 0 || q.RecompiledMetamagics is not null)) return false;
            if (q.RecompiledMetamagics is { } updates
                && (updates.Count is 0 or > 4096
                    || updates.Select(x => x.InstanceId).Distinct().Count() != updates.Count
                    || updates.Any(x => x.InstanceId == Guid.Empty || x.SourceId == Guid.Empty
                        || x.InstanceXml.Length is 0 or > 1_048_576 || x.SourceXml.Length is 0 or > 1_048_576
                        || x.EffectsBefore.Count is 0 or > 128 || x.EffectsAfter.Count != x.EffectsBefore.Count
                        || x.EffectsBefore.Concat(x.EffectsAfter).Any(xml => xml.Length is 0 or > 65536))))
                return false;
            if (q.Selection.Mode == CharacterCareerInitiationMode.GradeOnly)
                return q.Selection.BenefitSourceId == Guid.Empty;
            var benefit = q.Benefits.SingleOrDefault(x => x.SourceId == q.Selection.BenefitSourceId);
            return benefit is not null && benefit.Blockers.Count == 0 && benefit.SourceId != Guid.Empty
                && benefit.SourceXml.Length is > 0 and <= 1_048_576
                && (q.Family == CharacterCareerInitiationFamily.Resonance
                    ? benefit.Kind == CharacterCareerInitiationBenefitKind.Echo
                    : benefit.Kind is CharacterCareerInitiationBenefitKind.Metamagic or CharacterCareerInitiationBenefitKind.PowerPoint)
                && (benefit.Kind == CharacterCareerInitiationBenefitKind.PowerPoint
                    ? benefit.PowerPoints > 0 : benefit.PowerPoints == 0);
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or NullReferenceException or OverflowException)
        { return false; }
    }

    private static bool ValidSubmersionAdjustments(CharacterCareerInitiationQuote q)
    {
        var expected = new[] { ("group", q.Selection.Group), ("ordeal", q.Selection.Ordeal), ("schooling", q.Selection.Schooling) };
        if (q.Selection.Mode == CharacterCareerInitiationMode.DeferredBenefit
            && expected.Any(x => x.Item2)) return false;
        foreach (var (id, selected) in expected)
        {
            var adjustment = q.Adjustments.SingleOrDefault(a => a.Id == id);
            if (adjustment is null || adjustment.Selected != selected || adjustment.Discount is < 0 or > 1
                || adjustment.Nuyen != 0 || adjustment.Obligation.Length != 0
                || selected && adjustment.Blockers.Count != 0) return false;
        }
        // Legacy ordinary/deferred quotes have three zero adjustments; no new
        // serialized fields or receipt digest versions are needed to accept them.
        return q.Adjustments.Where(a => a.Selected).Sum(a => a.Discount) <= 1;
    }

    private static bool ValidMode(CharacterCareerInitiationQuote q)
    {
        if (!Enum.IsDefined(q.Selection.Mode)) return false;
        if (q.Family != CharacterCareerInitiationFamily.Resonance
            && (q.Selection.Mode != CharacterCareerInitiationMode.GradeAndBenefit
                || q.Selection.TargetGradeId is not null || q.SubmersionGrades is not null)) return false;
        if (q.SubmersionGrades is { } grades
            && (grades.Count != q.CurrentGrade || grades.Count > MaximumGrade
                || grades.Select(g => g.GradeId).Distinct().Count() != grades.Count
                || !grades.Select(g => g.Grade).OrderBy(g => g).SequenceEqual(Enumerable.Range(1, grades.Count))
                || grades.Any(g => g.GradeId == Guid.Empty || g.GradeXml.Length is 0 or > 65536
                    || g.Blockers.Count > 32))) return false;
        if (q.Selection.Mode != CharacterCareerInitiationMode.DeferredBenefit)
            return q.NextGrade == q.CurrentGrade + 1 && q.Selection.TargetGradeId is null;
        var target = q.SubmersionGrades?.SingleOrDefault(g => g.GradeId == q.Selection.TargetGradeId);
        return q.CurrentGrade > 0 && q.NextGrade == q.CurrentGrade && q.KarmaCost == 0 && q.NuyenCost == 0
            && target is { FirstBenefitPending: true } && target.Blockers.Count == 0;
    }

    public static Guid Id(Guid transaction, string purpose)
    {
        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(transaction.ToString("D") + "\0" + purpose));
        return new Guid(bytes.AsSpan(0, 16));
    }
}

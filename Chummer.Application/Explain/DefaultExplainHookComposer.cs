using Chummer.Contracts;

namespace Chummer.Application.Explain;

public sealed class DefaultExplainHookComposer : IExplainHookComposer
{
    public ExplainHookReference CreateReference(
        string targetKind,
        string targetId,
        string traceId,
        string subjectId,
        string? capabilityId = null,
        string? providerId = null,
        string? packId = null,
        string? runtimeFingerprint = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetKind);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetId);
        ArgumentException.ThrowIfNullOrWhiteSpace(traceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectId);

        string normalizedTargetKind = targetKind.Trim().ToLowerInvariant();
        string normalizedTargetId = targetId.Trim();
        string normalizedTraceId = traceId.Trim();
        string normalizedSubjectId = subjectId.Trim();
        string hookId = $"{normalizedTargetKind}:{normalizedTargetId}:{normalizedTraceId}";

        return new ExplainHookReference(
            HookId: hookId,
            TraceId: normalizedTraceId,
            SubjectId: normalizedSubjectId,
            CapabilityId: NullIfEmpty(capabilityId),
            ProviderId: NullIfEmpty(providerId),
            PackId: NullIfEmpty(packId),
            RuntimeFingerprint: NullIfEmpty(runtimeFingerprint));
    }

    public ExplainHookComposition Compose(string compositionId, IReadOnlyList<ExplainHookAttachment> attachments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(compositionId);
        ArgumentNullException.ThrowIfNull(attachments);

        ExplainHookAttachment[] normalized = attachments
            .Select(NormalizeAttachment)
            .Distinct(ExplainHookAttachmentComparer.Instance)
            .OrderBy(static attachment => attachment.TargetKind, StringComparer.Ordinal)
            .ThenBy(static attachment => attachment.TargetId, StringComparer.Ordinal)
            .ThenBy(static attachment => attachment.Explain.HookId, StringComparer.Ordinal)
            .ToArray();

        return new ExplainHookComposition(compositionId.Trim(), normalized);
    }

    private static ExplainHookAttachment NormalizeAttachment(ExplainHookAttachment attachment)
    {
        ArgumentNullException.ThrowIfNull(attachment);
        ArgumentNullException.ThrowIfNull(attachment.Explain);

        return attachment with
        {
            TargetKind = attachment.TargetKind.Trim().ToLowerInvariant(),
            TargetId = attachment.TargetId.Trim(),
            Explain = attachment.Explain with
            {
                HookId = attachment.Explain.HookId.Trim(),
                TraceId = attachment.Explain.TraceId.Trim(),
                SubjectId = attachment.Explain.SubjectId.Trim(),
                CapabilityId = NullIfEmpty(attachment.Explain.CapabilityId),
                ProviderId = NullIfEmpty(attachment.Explain.ProviderId),
                PackId = NullIfEmpty(attachment.Explain.PackId),
                RuntimeFingerprint = NullIfEmpty(attachment.Explain.RuntimeFingerprint)
            }
        };
    }

    private static string? NullIfEmpty(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed class ExplainHookAttachmentComparer : IEqualityComparer<ExplainHookAttachment>
    {
        public static readonly ExplainHookAttachmentComparer Instance = new();

        public bool Equals(ExplainHookAttachment? x, ExplainHookAttachment? y)
        {
            if (ReferenceEquals(x, y))
            {
                return true;
            }

            if (x is null || y is null)
            {
                return false;
            }

            return string.Equals(x.TargetKind, y.TargetKind, StringComparison.Ordinal)
                   && string.Equals(x.TargetId, y.TargetId, StringComparison.Ordinal)
                   && string.Equals(x.Explain.HookId, y.Explain.HookId, StringComparison.Ordinal);
        }

        public int GetHashCode(ExplainHookAttachment obj)
            => HashCode.Combine(obj.TargetKind, obj.TargetId, obj.Explain.HookId);
    }
}

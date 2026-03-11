using Chummer.Contracts;

namespace Chummer.Application.Explain;

public interface IExplainHookComposer
{
    ExplainHookReference CreateReference(
        string targetKind,
        string targetId,
        string traceId,
        string subjectId,
        string? capabilityId = null,
        string? providerId = null,
        string? packId = null,
        string? runtimeFingerprint = null);

    ExplainHookComposition Compose(string compositionId, IReadOnlyList<ExplainHookAttachment> attachments);
}

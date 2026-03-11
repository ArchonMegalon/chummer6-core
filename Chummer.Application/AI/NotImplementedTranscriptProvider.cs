using Chummer.Contracts.AI;
using Chummer.Contracts.Owners;

namespace Chummer.Application.AI;

public sealed class NotImplementedTranscriptProvider : ITranscriptProvider
{
    public AiApiResult<AiTranscriptDocumentReceipt> SubmitTranscript(OwnerScope owner, AiTranscriptSubmissionRequest? request)
        => AiApiResult<AiTranscriptDocumentReceipt>.FromNotImplemented(
            CreateReceipt(owner, AiTranscriptApiOperations.SubmitTranscript));

    public AiApiResult<AiTranscriptDocumentReceipt> GetTranscript(OwnerScope owner, string transcriptId)
        => AiApiResult<AiTranscriptDocumentReceipt>.FromNotImplemented(
            CreateReceipt(owner, AiTranscriptApiOperations.GetTranscript));

    private static AiNotImplementedReceipt CreateReceipt(OwnerScope owner, string operation)
        => new(
            Error: "ai_not_implemented",
            Operation: operation,
            Message: "The Chummer AI transcript surface is not implemented yet.",
            OwnerId: owner.NormalizedValue);
}

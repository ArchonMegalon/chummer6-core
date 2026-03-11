using Chummer.Contracts.AI;
using Chummer.Contracts.Owners;

namespace Chummer.Application.AI;

public interface ITranscriptProvider
{
    AiApiResult<AiTranscriptDocumentReceipt> SubmitTranscript(OwnerScope owner, AiTranscriptSubmissionRequest? request);

    AiApiResult<AiTranscriptDocumentReceipt> GetTranscript(OwnerScope owner, string transcriptId);
}

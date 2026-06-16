#nullable enable annotations

using Chummer.Application.AI;
using Chummer.Contracts.AI;
using Chummer.Contracts.Owners;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public sealed class AiTranscriptAndRecapDraftServiceTests
{
    [TestMethod]
    public void Not_implemented_ai_transcript_provider_exposes_explicit_transcript_boundaries()
    {
        NotImplementedTranscriptProvider provider = new();

        AiApiResult<AiTranscriptDocumentReceipt> submit = provider.SubmitTranscript(
            OwnerScope.LocalSingleUser,
            new AiTranscriptSubmissionRequest(
                FileName: "session-audio.wav",
                ContentType: "audio/wav",
                SessionId: "session-1"));
        AiApiResult<AiTranscriptDocumentReceipt> detail = provider.GetTranscript(
            OwnerScope.LocalSingleUser,
            "transcript-1");

        Assert.IsFalse(submit.IsImplemented);
        Assert.AreEqual(AiTranscriptApiOperations.SubmitTranscript, submit.NotImplemented?.Operation);
        Assert.IsNotNull(submit.NotImplemented?.Envelope);
        Assert.AreEqual(ReceiptProvenanceClasses.Runtime, submit.NotImplemented!.Envelope!.ProvenanceClass);
        Assert.IsFalse(detail.IsImplemented);
        Assert.AreEqual(AiTranscriptApiOperations.GetTranscript, detail.NotImplemented?.Operation);
        Assert.IsNotNull(detail.NotImplemented?.Envelope);
    }

    [TestMethod]
    public void Not_implemented_ai_recap_draft_service_exposes_explicit_recap_boundaries()
    {
        NotImplementedAiRecapDraftService service = new();

        AiApiResult<AiRecapDraftCatalog> list = service.ListRecapDrafts(
            OwnerScope.LocalSingleUser,
            new AiRecapDraftQuery(SessionId: "session-1", MaxCount: 5));
        AiApiResult<AiRecapDraftReceipt> create = service.CreateRecapDraft(
            OwnerScope.LocalSingleUser,
            new AiRecapDraftRequest(
                SourceKind: "transcript",
                SourceId: "transcript-1",
                Title: "Session recap",
                SessionId: "session-1"));

        Assert.IsFalse(list.IsImplemented);
        Assert.AreEqual(AiRecapDraftApiOperations.ListRecapDrafts, list.NotImplemented?.Operation);
        Assert.IsNotNull(list.NotImplemented?.Envelope);
        Assert.IsFalse(create.IsImplemented);
        Assert.AreEqual(AiRecapDraftApiOperations.CreateRecapDraft, create.NotImplemented?.Operation);
        Assert.IsNotNull(create.NotImplemented?.Envelope);
    }
}

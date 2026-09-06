using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;
using Chummer.Application.Workspaces;
using System.Text.Json;

namespace Chummer.Application.Characters;

public static class CharacterAfterRunRewardReceiptLedgerIntegrity
{
    public const int MaximumEntries = 4096;

    /// <summary>
    /// Resource budget for the canonical serialized receipt-array UTF-8 bytes.
    /// This is not a mechanics or reason-length limit: evidence is never trimmed
    /// or discarded to admit another operation. A full ledger rejects new writes.
    /// </summary>
    public const long MaximumLedgerUtf8Bytes = 4 * 1024 * 1024;

    public static bool IsValidLedger(
        CharacterWorkspaceId workspaceId,
        long currentRevision,
        IReadOnlyList<CharacterAfterRunRewardReceipt>? entries)
    {
        if (entries is null) return true;
        if (currentRevision <= 0 || entries.Count > MaximumEntries
            || !TryMeasureLedgerUtf8Bytes(entries, out _))
            return false;
        HashSet<Guid> operations = [];
        HashSet<Guid> rewards = [];
        HashSet<Guid> associatedExpenses = [];
        long lastRevision = 0;
        foreach (CharacterAfterRunRewardReceipt receipt in entries)
        {
            if (!IsCoherent(workspaceId, receipt)
                || receipt.CommittedWorkspaceRevision > currentRevision
                || receipt.CommittedWorkspaceRevision <= lastRevision
                || !operations.Add(receipt.OperationId) || !rewards.Add(receipt.RewardId)
                || (receipt.KarmaExpenseId is { } karmaId && !associatedExpenses.Add(karmaId))
                || (receipt.NuyenExpenseId is { } nuyenId && !associatedExpenses.Add(nuyenId)))
                return false;
            lastRevision = receipt.CommittedWorkspaceRevision;
        }
        return true;
    }

    /// <summary>
    /// Counts the same minified default System.Text.Json array representation
    /// used by persistence, including field names and escaped strings. Each
    /// coherent, individually bounded receipt is flushed to a counting sink;
    /// neither the complete JSON text nor its byte array is materialized.
    /// True returns the exact size. Budget overflow returns false and limit+1;
    /// invalid entry shape returns false without authorizing or repairing it.
    /// This measures admission only, not append authority or ledger uniqueness.
    /// </summary>
    public static bool TryMeasureLedgerUtf8Bytes(
        IReadOnlyList<CharacterAfterRunRewardReceipt>? entries,
        out long utf8Bytes)
    {
        utf8Bytes = 0;
        using var sink = new BoundedLedgerCountingStream();
        try
        {
            using var writer = new Utf8JsonWriter(sink);
            if (entries is null)
            {
                writer.WriteNullValue();
            }
            else
            {
                writer.WriteStartArray();
                writer.Flush();
                foreach (CharacterAfterRunRewardReceipt receipt in entries)
                {
                    // Coherence bounds every serialized string and limits
                    // selected evidence to two entries before a writer can
                    // allocate space for a potentially oversized string token.
                    if (receipt?.Command is null || !IsCoherent(receipt.Command.WorkspaceId, receipt))
                        return false;
                    JsonSerializer.Serialize(writer, receipt);
                    writer.Flush();
                }
                writer.WriteEndArray();
            }
            writer.Flush();
            utf8Bytes = sink.BytesWritten;
            return true;
        }
        catch (LedgerByteBudgetExceededException)
        {
            utf8Bytes = MaximumLedgerUtf8Bytes + 1;
            return false;
        }
    }

    private sealed class LedgerByteBudgetExceededException : IOException { }

    private sealed class BoundedLedgerCountingStream : Stream
    {
        public long BytesWritten { get; private set; }
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => BytesWritten;
        public override long Position
        {
            get => BytesWritten;
            set => throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
            => Write(buffer.AsSpan(offset, count));

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            if (buffer.Length > MaximumLedgerUtf8Bytes - BytesWritten)
                throw new LedgerByteBudgetExceededException();
            BytesWritten += buffer.Length;
        }

        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }

    public static string ComputeReceiptDigest(CharacterAfterRunRewardReceipt receipt)
        => CharacterAfterRunRewardProjector.ReceiptDigest(receipt);

    public static bool IsCoherent(CharacterWorkspaceId workspaceId, CharacterAfterRunRewardReceipt? receipt)
    {
        if (receipt is null || receipt.Schema != CharacterAfterRunRewardProjector.ReceiptSchema
            || !CharacterAfterRunRewardProjector.IsValidCommand(receipt.Command)
            || !CharacterAfterRunRewardProjector.IsValidBeforeEvidence(receipt.Before)
            || receipt.Command.WorkspaceId != workspaceId
            || receipt.Before.WorkspaceId != workspaceId
            || receipt.OperationId != receipt.Command.OperationId
            || receipt.RewardId != receipt.Command.RewardId
            || receipt.CommandDigest != receipt.Command.CommandDigest()
            || receipt.ExpectedWorkspaceRevision != receipt.Command.ExpectedWorkspaceRevision
            || receipt.CommittedWorkspaceRevision != receipt.ExpectedWorkspaceRevision + 1
            || receipt.KarmaBefore != receipt.Before.AvailableKarma
            || receipt.NuyenBefore != receipt.Before.AvailableNuyen
            || !CharacterAfterRunRewardProjector.IsDigest(receipt.CharacterPayloadDigestAfter)
            || !CharacterAfterRunRewardProjector.IsDigest(receipt.ReceiptDigest)
            || !CharacterAfterRunRewardProjector.TryEvaluateEvidence(receipt.Command, receipt.Before,
                out int karmaAfter, out decimal nuyenAfter, out Guid? karmaId, out Guid? nuyenId, out _)
            || receipt.KarmaAfter != karmaAfter || receipt.NuyenAfter != nuyenAfter
            || receipt.KarmaExpenseId != karmaId || receipt.NuyenExpenseId != nuyenId)
            return false;
        CharacterAfterRunRewardPreview historicalPreview = CharacterAfterRunRewardProjector.CreatePreview(
            receipt.Command, receipt.KarmaBefore, karmaAfter, receipt.NuyenBefore, nuyenAfter, karmaId, nuyenId);
        if (!string.Equals(receipt.Command.ExpectedPreviewDigest, historicalPreview.PreviewDigest, StringComparison.Ordinal))
            return false;
        return string.Equals(receipt.ReceiptDigest, ComputeReceiptDigest(receipt), StringComparison.Ordinal);
    }

    public static bool IsValidAppendTransition(
        CharacterWorkspaceId workspaceId,
        long previousRevision,
        long previousSavedRevision,
        long nextRevision,
        WorkspaceDocument currentDocument,
        WorkspaceDocument replacementDocument)
    {
        if (previousRevision is <= 0 or long.MaxValue
            || previousSavedRevision != previousRevision || nextRevision != previousRevision + 1)
            return false;
        IReadOnlyList<CharacterAfterRunRewardReceipt>? current = currentDocument.AuxiliaryState.CharacterAfterRunRewardReceipts;
        IReadOnlyList<CharacterAfterRunRewardReceipt>? replacement = replacementDocument.AuxiliaryState.CharacterAfterRunRewardReceipts;
        if (replacement is null || replacement.Count != (current?.Count ?? 0) + 1
            || !IsValidLedger(workspaceId, previousRevision, current)
            || !IsValidLedger(workspaceId, nextRevision, replacement))
            return false;
        if (current is not null && !current.Zip(replacement.Take(current.Count)).All(pair =>
                CharacterAfterRunRewardProjector.CanonicalEquals(pair.First, pair.Second)))
            return false;
        CharacterAfterRunRewardReceipt appended = replacement[^1];
        if (appended.ExpectedWorkspaceRevision != previousRevision
            || appended.CommittedWorkspaceRevision != nextRevision)
            return false;
        var saved = new WorkspaceStoredDocument(workspaceId, currentDocument,
            previousRevision, previousSavedRevision, DateTimeOffset.UnixEpoch);
        if (!CharacterAfterRunRewardProjector.TryBuild(saved, appended.Command,
                out WorkspaceDocument expected, out CharacterAfterRunRewardReceipt expectedReceipt, out _))
            return false;
        // Validate mechanics, exact allowed XML, envelope identity, and every sibling
        // auxiliary lane. Rehashing an arbitrary replacement cannot authorize it.
        return expected.Format == replacementDocument.Format
               && expected.SchemaVersion == replacementDocument.SchemaVersion
               && string.Equals(expected.RulesetId, replacementDocument.RulesetId, StringComparison.Ordinal)
               && string.Equals(expected.PayloadKind, replacementDocument.PayloadKind, StringComparison.Ordinal)
               && string.Equals(expected.Content, replacementDocument.Content, StringComparison.Ordinal)
               && string.Equals(expected.AuxiliaryStateDigest, replacementDocument.AuxiliaryStateDigest, StringComparison.Ordinal)
               && CharacterAfterRunRewardProjector.CanonicalEquals(expectedReceipt, appended);
    }
}

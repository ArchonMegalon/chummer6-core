using System.Globalization;
using System.Text.Json;
using Chummer.Application.Session;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Session;
using static Chummer.Application.Session.SessionPlayEncoding;

namespace Chummer.Infrastructure.Files;

public sealed partial class FileSessionLedgerStore
{
    private sealed partial class Transaction
    {
        public IReadOnlyList<SessionConditionTransferStored> ReadConditionTransfers()
        {
            var files = Directory.EnumerateFiles(scope, "*.transfer-intent.json").Order(StringComparer.Ordinal).ToArray();
            Require(files.Length <= SessionConditionTransferRules.MaximumTransfers, "condition-transfer-capacity-invalid");
            var acknowledgements = Directory.EnumerateFiles(scope, "*.transfer-ack.json").ToArray();
            Conflict(acknowledgements.All(f => File.Exists(f.Replace(".transfer-ack.json", ".transfer-intent.json", StringComparison.Ordinal))),
                "condition-transfer-orphan-ack");
            var origin = ReadOrigin();
            var list = new List<SessionConditionTransferStored>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < files.Length; i++)
            {
                var prefix = (i + 1).ToString("D6", CultureInfo.InvariantCulture);
                Conflict(Path.GetFileName(files[i]) == prefix + ".transfer-intent.json", "condition-transfer-sequence-gap");
                var intent = Read<SessionConditionTransferIntent>(prefix + ".transfer-intent.json")!;
                SessionConditionTransferRules.ValidateReview(intent.Review, owner == OwnerScope.LocalSingleUser.NormalizedValue
                    ? OwnerScope.LocalSingleUser : new OwnerScope(owner), character);
                Conflict(origin is not null && intent.Review.OriginDigest == Digest(origin) && intent.Review.OverlayId == origin.OverlayId
                    && ids.Add(intent.Review.OperationId), "condition-transfer-origin-divergence");
                var receipt = Read<SessionConditionTransferReceipt>(prefix + ".transfer-ack.json");
                Conflict(receipt is not null || i == files.Length - 1, "condition-transfer-pending-predecessor");
                if (receipt is not null) SessionConditionTransferRules.ValidateAcknowledgement(receipt, intent);
                list.Add(new(i + 1, intent, receipt));
            }
            return list;
        }

        public SessionConditionTransferStored PrepareConditionTransfer(SessionConditionTransferIntent intent)
        {
            Conflict(ReadFocusContinuations7490().All(x => x.Receipt is not null
                && x.Intent.ExpectedReceipt.Command.Request.OperationId.ToString("D") != intent.Review.OperationId),
                "focus-continuation-requires-original-recovery");
            Conflict(ReadNaturalRecoveries().All(x => x.Receipt is not null && x.Intent.Command.Request.OperationId.ToString("D") != intent.Review.OperationId),
                "natural-recovery-requires-original-recovery");
            var list = ReadConditionTransfers();
            Require(list.Count < SessionConditionTransferRules.MaximumTransfers, "condition-transfer-capacity-reached");
            Conflict(list.All(t => t.Receipt is not null && t.Intent.Review.OperationId != intent.Review.OperationId)
                && ReadOperations().All(o => o.Result is not null && o.Intent.OperationId != intent.Review.OperationId),
                "condition-transfer-pending-or-duplicate");
            SessionConditionTransferRules.ValidateReview(intent.Review, owner == OwnerScope.LocalSingleUser.NormalizedValue
                ? OwnerScope.LocalSingleUser : new OwnerScope(owner), character);
            Conflict(intent.Review.OriginDigest == Digest(ReadOrigin()), "condition-transfer-origin-divergence");
            // The final acknowledgement includes this review and one authority. Bound it before canonical mutation.
            Require(JsonSerializer.SerializeToUtf8Bytes(intent, Json).Length * 3L + 8192 < SessionPlayLimits.MaxRecordBytes,
                "condition-transfer-result-capacity");
            var prepared = new SessionConditionTransferStored(list.Count + 1L, intent, null);
            Write(prepared.Ordinal.ToString("D6", CultureInfo.InvariantCulture) + ".transfer-intent.json", intent,
                SessionPlayLimits.MaxRecordBytes, false);
            return prepared;
        }

        public void AcknowledgeConditionTransfer(SessionConditionTransferStored original, SessionConditionTransferReceipt receipt)
        {
            var current = ReadConditionTransfers().SingleOrDefault(t => t.Ordinal == original.Ordinal);
            Conflict(current is not null && current.Receipt is null && Digest(current.Intent) == Digest(original.Intent),
                "condition-transfer-ack-divergence");
            SessionConditionTransferRules.ValidateAcknowledgement(receipt, original.Intent);
            Write(original.Ordinal.ToString("D6", CultureInfo.InvariantCulture) + ".transfer-ack.json", receipt, 0, false);
        }
    }
}

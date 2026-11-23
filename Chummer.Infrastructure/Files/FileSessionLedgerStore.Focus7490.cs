using System.Globalization;
using System.Text.Json;
using Chummer.Application.Session;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Session;
using static Chummer.Application.Session.SessionPlayEncoding;

namespace Chummer.Infrastructure.Files;

public sealed partial class FileSessionLedgerStore
{
    private sealed partial class Transaction
    {
        private static string FocusFile7490(long ordinal, string kind)
            => ordinal.ToString("D6", CultureInfo.InvariantCulture) + ".focus-" + kind + ".json";
        public IReadOnlyList<SessionFocusContinuationStored7490> ReadFocusContinuations7490()
        {
            var files = Directory.EnumerateFiles(scope, "*.focus-intent.json").Order(StringComparer.Ordinal).ToArray();
            Require(files.Length <= SessionFocusContinuationRules7490.Maximum, "focus-continuation-capacity-invalid");
            foreach (var suffix in new[] { "ack", "retire" })
                Conflict(Directory.EnumerateFiles(scope, "*.focus-" + suffix + ".json").All(f =>
                    File.Exists(f.Replace(".focus-" + suffix + ".json", ".focus-intent.json", StringComparison.Ordinal))),
                    "focus-continuation-orphan-result");
            var origin = ReadOrigin(); var rows = new List<SessionFocusContinuationStored7490>();
            var identities = new HashSet<Guid>();
            for (int i = 0; i < files.Length; i++)
            {
                Conflict(Path.GetFileName(files[i]) == FocusFile7490(i + 1, "intent"), "focus-continuation-sequence-gap");
                var intent = Read<SessionFocusContinuationIntent7490>(FocusFile7490(i + 1, "intent"))!;
                SessionFocusContinuationRules7490.Validate(intent);
                var command = intent.ExpectedReceipt.Command;
                Conflict(origin is not null && Digest(origin) == intent.OriginDigest
                    && command.Binding.OwnerId == owner && command.Request.WorkspaceId.Value == character
                    && identities.Add(command.Request.OperationId), "focus-continuation-origin-divergence");
                var retirement = Read<SessionFocusContinuationIntent7490>(FocusFile7490(i + 1, "retire"));
                if (retirement is not null) SessionFocusContinuationRules7490.ValidateRetirement(intent, retirement);
                var receipt = Read<SessionFocusContinuationReceipt7490>(FocusFile7490(i + 1, "ack"));
                Conflict(receipt is not null || i == files.Length - 1, "focus-continuation-pending-predecessor");
                var row = new SessionFocusContinuationStored7490(i + 1, intent, retirement, receipt);
                if (receipt is not null) _ = SessionFocusContinuationRules7490.Admitted(row);
                rows.Add(row);
            }
            return rows;
        }
        public SessionFocusContinuationStored7490 PrepareFocusContinuation7490(SessionFocusContinuationIntent7490 intent)
        {
            SessionFocusContinuationRules7490.Validate(intent);
            var rows = ReadFocusContinuations7490();
            Require(rows.Count < SessionFocusContinuationRules7490.Maximum, "focus-continuation-capacity-reached");
            string id = intent.ExpectedReceipt.Command.Request.OperationId.ToString("D");
            Conflict(rows.All(x => x.Receipt is not null && x.Intent.ExpectedReceipt.Command.Request.OperationId.ToString("D") != id)
                && ReadOperations().All(x => x.Result is not null && x.Intent.OperationId != id)
                && ReadConditionTransfers().All(x => x.Receipt is not null && x.Intent.Review.OperationId != id)
                && ReadNaturalRecoveries().All(x => x.Receipt is not null && x.Intent.Command.Request.OperationId.ToString("D") != id)
                && Digest(ReadOrigin()) == intent.OriginDigest, "focus-continuation-pending-or-duplicate");
            Require(JsonSerializer.SerializeToUtf8Bytes(intent, Json).Length * 3L + 8192 < SessionPlayLimits.MaxRecordBytes,
                "focus-continuation-result-capacity");
            var row = new SessionFocusContinuationStored7490(rows.Count + 1L, intent, null, null);
            Write(FocusFile7490(row.Ordinal, "intent"), intent, SessionPlayLimits.MaxRecordBytes * 2L, false);
            return row;
        }
        public void PrepareFocusRetirement7490(SessionFocusContinuationStored7490 original, SessionFocusContinuationIntent7490 intent)
        {
            var current = ReadFocusContinuations7490().Single(x => x.Ordinal == original.Ordinal);
            Conflict(current.Receipt is null && current.Retirement is null && Digest(current.Intent) == Digest(original.Intent),
                "focus-continuation-retirement-divergence");
            SessionFocusContinuationRules7490.ValidateRetirement(original.Intent, intent);
            Write(FocusFile7490(original.Ordinal, "retire"), intent, 0, false);
        }
        public void AcknowledgeFocusContinuation7490(SessionFocusContinuationStored7490 original, SessionFocusContinuationReceipt7490 receipt)
        {
            var current = ReadFocusContinuations7490().Single(x => x.Ordinal == original.Ordinal);
            Conflict(current.Receipt is null && Digest(current.Intent) == Digest(original.Intent), "focus-continuation-ack-divergence");
            _ = SessionFocusContinuationRules7490.Admitted(current with { Receipt = receipt });
            Write(FocusFile7490(original.Ordinal, "ack"), receipt, 0, false);
        }
    }
}

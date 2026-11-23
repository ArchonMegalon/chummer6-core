using System.Globalization;
using System.Text.Json;
using Chummer.Application.Session;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Session;
using static Chummer.Application.Session.SessionPlayEncoding;

namespace Chummer.Infrastructure.Files;

public sealed partial class FileSessionLedgerStore
{
    private sealed partial class Transaction
    {
        public IReadOnlyList<SessionNaturalRecoveryStored> ReadNaturalRecoveries()
        {
            var files = Directory.EnumerateFiles(scope, "*.recovery-intent.json").Order(StringComparer.Ordinal).ToArray();
            Require(files.Length <= SessionNaturalRecoveryRules.MaximumOperations, "recovery-capacity-invalid");
            foreach (string suffix in new[] { ".recovery-fence.json", ".recovery-ack.json" })
                Conflict(Directory.EnumerateFiles(scope, "*" + suffix).All(f => File.Exists(f.Replace(suffix, ".recovery-intent.json", StringComparison.Ordinal))), "recovery-orphan-ack");
            var origin = ReadOrigin(); var list = new List<SessionNaturalRecoveryStored>(); var ids = new HashSet<Guid>();
            for (int i = 0; i < files.Length; i++)
            {
                string prefix = (i + 1).ToString("D6", CultureInfo.InvariantCulture);
                Conflict(Path.GetFileName(files[i]) == prefix + ".recovery-intent.json", "recovery-sequence-gap");
                var intent = Read<SessionNaturalRecoveryIntent>(prefix + ".recovery-intent.json")!;
                SessionNaturalRecoveryRules.ValidateReview(intent.Review,
                    owner == OwnerScope.LocalSingleUser.NormalizedValue ? OwnerScope.LocalSingleUser : new OwnerScope(owner), character);
                Conflict(origin is not null && Digest(origin) == intent.Review.OriginDigest && origin.OverlayId == intent.Review.OverlayId
                    && ids.Add(intent.Command.Request.OperationId) && intent.Command == intent.Review.Recovery.Command with { ExplicitlyConfirmed = true }, "recovery-origin-divergence");
                var fence = Read<SessionNaturalRecoveryReceipt>(prefix + ".recovery-fence.json");
                var receipt = Read<SessionNaturalRecoveryReceipt>(prefix + ".recovery-ack.json");
                if (fence is not null)
                {
                    SessionNaturalRecoveryRules.ValidateReceipt(fence, intent);
                    Conflict(fence.Operation.Activity.Phase == Sr5RecoveryPhase.RollUnresolved, "recovery-fence-invalid");
                }
                if (receipt is not null)
                {
                    SessionNaturalRecoveryRules.ValidateReceipt(receipt, intent);
                    Conflict(receipt.Operation.Activity.Phase != Sr5RecoveryPhase.RollUnresolved
                        && (intent.Command.Request.Action != Sr5RecoveryAction.Roll || fence is not null), "recovery-terminal-invalid");
                }
                Conflict(receipt is not null || i == files.Length - 1, "recovery-pending-predecessor");
                list.Add(new(i + 1, intent, fence, receipt));
            }
            return list;
        }
        public SessionNaturalRecoveryStored PrepareNaturalRecovery(SessionNaturalRecoveryIntent intent)
        {
            Conflict(ReadFocusContinuations7490().All(x => x.Receipt is not null
                && x.Intent.ExpectedReceipt.Command.Request.OperationId.ToString("D") != intent.Command.Request.OperationId.ToString("D")),
                "focus-continuation-requires-original-recovery");
            var list = ReadNaturalRecoveries(); string id = intent.Command.Request.OperationId.ToString("D");
            Conflict(list.All(x => x.Receipt is not null && x.Intent.Command.Request.OperationId != intent.Command.Request.OperationId)
                && ReadConditionTransfers().All(x => x.Receipt is not null && x.Intent.Review.OperationId != id)
                && ReadOperations().All(x => x.Result is not null && x.Intent.OperationId != id), "recovery-pending-or-duplicate");
            Require(list.Count < SessionNaturalRecoveryRules.MaximumOperations, "recovery-capacity-reached");
            SessionNaturalRecoveryRules.ValidateReview(intent.Review,
                owner == OwnerScope.LocalSingleUser.NormalizedValue ? OwnerScope.LocalSingleUser : new OwnerScope(owner), character);
            Conflict(Digest(ReadOrigin()) == intent.Review.OriginDigest, "recovery-origin-divergence");
            Require(JsonSerializer.SerializeToUtf8Bytes(intent, Json).Length * 3L + 8192 < SessionPlayLimits.MaxRecordBytes, "recovery-result-capacity");
            var stored = new SessionNaturalRecoveryStored(list.Count + 1L, intent, null, null);
            Write(stored.Ordinal.ToString("D6", CultureInfo.InvariantCulture) + ".recovery-intent.json", intent,
                SessionPlayLimits.MaxRecordBytes * (intent.Command.Request.Action == Sr5RecoveryAction.Roll ? 2L : 1L), false);
            return stored;
        }
        public void AcknowledgeNaturalRecovery(SessionNaturalRecoveryStored original, SessionNaturalRecoveryReceipt receipt)
        {
            var current = ReadNaturalRecoveries().SingleOrDefault(x => x.Ordinal == original.Ordinal);
            Conflict(current is not null && Digest(current.Intent) == Digest(original.Intent), "recovery-ack-divergence");
            SessionNaturalRecoveryRules.ValidateReceipt(receipt, original.Intent);
            bool fence = receipt.Operation.Activity.Phase == Sr5RecoveryPhase.RollUnresolved;
            var prior = fence ? current!.Unresolved : current!.Receipt;
            if (prior is not null) { Conflict(Digest(prior) == Digest(receipt), "recovery-original-result-changed"); return; }
            Conflict(current!.Receipt is null && (fence || original.Intent.Command.Request.Action != Sr5RecoveryAction.Roll || current.Unresolved is not null), "recovery-fence-missing");
            Write(original.Ordinal.ToString("D6", CultureInfo.InvariantCulture) + (fence ? ".recovery-fence.json" : ".recovery-ack.json"), receipt, 0, false);
        }
        private static int RecoveryReservations(IEnumerable<string> files)
            => files.Where(f => f.EndsWith(".recovery-intent.json", StringComparison.Ordinal)).Sum(f => {
                // Reservation shape is determined from the immutable intent, never a filename guess.
                using var stream = File.OpenRead(f);
                var record = JsonSerializer.Deserialize<Record<SessionNaturalRecoveryIntent>>(stream, Json);
                Conflict(record is not null && record.Digest == Digest(record.Payload), "recovery-reservation-corrupt");
                int remaining = File.Exists(f.Replace(".recovery-intent.json", ".recovery-ack.json", StringComparison.Ordinal)) ? 0 : 1;
                if (record!.Payload.Command.Request.Action == Sr5RecoveryAction.Roll
                    && !File.Exists(f.Replace(".recovery-intent.json", ".recovery-fence.json", StringComparison.Ordinal))) remaining++;
                return remaining;
            });
    }
}

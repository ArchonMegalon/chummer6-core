using System.Text.Json;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Session;
using static Chummer.Application.Session.SessionPlayEncoding;

namespace Chummer.Application.Session;

public sealed partial class DurableSessionPlayService
{
    private static SessionPlaySettlementContinuation5712 CaptureSettlement5712(ISessionPlayReader reader,
        SessionConditionTransferIntent intent)
    {
        var value = new SessionPlaySettlementContinuation5712(reader.ReadOrigin()!, reader.ReadOperations(),
            reader.ReadConditionTransfers(), reader.ReadNaturalRecoveries(), intent, "") {
                Focuses7490 = reader.ReadFocusContinuations7490().Count == 0 ? null : reader.ReadFocusContinuations7490() };
        value = value with { Digest = Digest(value) };
        ValidateSettlement5712(value, intent); return value;
    }

    public static void ValidateSettlement5712(SessionPlaySettlementContinuation5712 value, SessionConditionTransferIntent intent)
    {
        Conflict(value is not null && value.Origin is not null && value.Digest == Digest(value with { Digest = "" })
            && Digest(value.Settlement) == Digest(intent) && intent.Review.Ammunition is not null
            && Digest(value.Origin) == intent.Review.OriginDigest && value.Origin.OverlayId == intent.Review.OverlayId
            && value.Operations.Count <= SessionPlayLimits.MaxOperations && value.Transfers.Count <= SessionConditionTransferRules.MaximumTransfers
            && value.Operations.All(x => x.Result is not null) && value.Transfers.All(x => x.Receipt is not null)
            && value.Recoveries.All(x => x.Receipt is not null)
            && (value.Focuses7490 is null || value.Focuses7490.Count <= SessionFocusContinuationRules7490.Maximum
                && value.Focuses7490.All(x => x.Receipt is not null)), "ammunition-continuation-invalid");
        Require(JsonSerializer.SerializeToUtf8Bytes(value, Json).Length <= SessionPlayLimits.MaxRecordBytes,
            "ammunition-continuation-capacity");
        Require(value.ImportedHistory?.Count is null or <= 32, "ammunition-imported-history-capacity");
        foreach (var prior in value.ImportedHistory ?? [])
        {
            Conflict(prior.ImportedHistory is null, "ammunition-imported-history-must-be-flat");
            ValidateSettlement5712(prior, prior.Settlement);
        }
        var reader = new SettlementReader5712(value);
        long ordinal = 0; var identities = new HashSet<string>(StringComparer.Ordinal);
        foreach (var operation in value.Operations)
        {
            var i = operation.Intent; var result = operation.Result!;
            Conflict(operation.Ordinal == ++ordinal && identities.Add(i.OperationId) && i.OverlayId == value.Origin.OverlayId
                && Canonical(i.OperationId, i.Kind, i.OverlayId, i.BaseCharacterVersion, i.Events, i.ClientCursor).PayloadDigest == i.PayloadDigest
                && result.OperationId == i.OperationId && result.PayloadDigest == i.PayloadDigest,
                "ammunition-continuation-operation-divergence");
        }
        _ = ValidateContinuation(reader, intent.Review.Canonical.Authority, intent.Review.Canonical);
        var snapshot = Replay(reader, value.Origin, History(value.Operations),
            value.Operations.LastOrDefault(x => x.Result?.Snapshot is not null)?.Intent.OperationId ?? "");
        CheckReplay(reader, value.Operations, snapshot);
        Conflict(snapshot.Play?.ReplicaCursor == intent.Review.Cursor && snapshot.Play.Condition5412 == intent.Review.Condition
            && Digest(SessionAmmunitionSettlementRules5712.Review(reader, intent.Review.Canonical.Authority.Ammunition,
                snapshot.Play.Ammunition)) == Digest(intent.Review.Ammunition), "ammunition-continuation-replay-divergence");
    }

    // A canonical checkpoint does not subtract from the original replay seed. Replay retains the
    // original events and authority; new envelopes use the exact latest admitted canonical version.
    private static CharacterVersionReference CurrentPlayVersion5712(ISessionPlayReader reader, SessionPlayAuthority origin)
        => CurrentPlayBinding5712(reader, origin).Version;

    private static SessionPlayBinding CurrentPlayBinding5712(ISessionPlayReader reader, SessionPlayAuthority origin)
        => VersionEdges5712(reader).OrderBy(x => x.Revision).LastOrDefault()?.Binding ?? origin.Binding;

    private sealed record VersionEdge5712(long Revision, int EventCount, SessionPlayBinding Binding)
    {
        internal CharacterVersionReference Version => Binding.Version;
    }

    private static IReadOnlyList<VersionEdge5712> VersionEdges5712(ISessionPlayReader reader)
    {
        var transfers = reader.ReadConditionTransfers();
        var recoveries = reader.ReadNaturalRecoveries();
        var focuses = reader.ReadFocusContinuations7490().Where(x => x.Receipt is not null)
            .Select(SessionFocusContinuationRules7490.Admitted).ToArray();
        // Preserve old journals until a typed version checkpoint actually exists.
        var starts = transfers.Where(x => x.Receipt is not null && x.Intent.Review.Ammunition is not null)
            .Select(x => x.Receipt!.Canonical.RecordedRevision)
            .Concat(focuses.Select(x => x.ExpectedReceipt.CommittedWorkspaceRevision)).ToArray();
        if (starts.Length == 0) return [];
        long firstSettlement = starts.Min();
        var events = History(reader.ReadOperations());
        var cursors = Enumerable.Range(0, events.Length + 1).ToDictionary(n => Cursor(events.Take(n)), n => n, StringComparer.Ordinal);
        var edges = new List<VersionEdge5712>();
        foreach (var item in transfers.Where(x => x.Receipt is not null && x.Receipt.Canonical.RecordedRevision >= firstSettlement))
        {
            SessionConditionTransferRules.ValidateAcknowledgement(item.Receipt!, item.Intent);
            Conflict(cursors.TryGetValue(item.Intent.Review.Cursor, out int count), "ammunition-continuation-cursor-missing");
            edges.Add(new(item.Receipt!.Canonical.RecordedRevision, count, item.Receipt.ContinuedAuthority.Binding));
        }
        foreach (var item in recoveries.Where(x => x.Receipt is not null && x.Receipt.Operation.RecordedRevision >= firstSettlement))
        {
            SessionNaturalRecoveryRules.ValidateReceipt(item.Receipt!, item.Intent);
            edges.Add(new(item.Receipt!.Operation.RecordedRevision, item.Intent.Review.EventCount,
                item.Receipt.ContinuedAuthority.Binding));
        }
        foreach (var focus in focuses)
        {
            Conflict(cursors.TryGetValue(focus.Cursor, out int count) && count == focus.EventCount,
                "focus-continuation-cursor-divergence");
            var receipt = reader.ReadFocusContinuations7490().Single(x =>
                x.Intent.ExpectedReceipt.Command.Request.OperationId == focus.ExpectedReceipt.Command.Request.OperationId).Receipt!;
            edges.Add(new(focus.ExpectedReceipt.CommittedWorkspaceRevision, count, receipt.ContinuedAuthority.Binding));
        }
        return edges;
    }

    private static Func<SessionEventEnvelope, bool> AdmittedEventVersions5712(ISessionPlayReader reader, SessionPlayOrigin origin)
    {
        var edges = VersionEdges5712(reader);
        return e => e.BaseCharacterVersion == (edges.Where(x => e.Sequence > x.EventCount).OrderBy(x => x.Revision)
            .LastOrDefault()?.Version ?? origin.Authority.Binding.Version);
    }

    private static void ValidateCurrentEnvelope5712(ISessionPlayReader reader, SessionPlayOrigin origin, SessionEventEnvelope e)
    {
        Conflict(e.BaseCharacterVersion == CurrentPlayVersion5712(reader, origin.Authority), "event-base-runtime-edition-mismatch");
        SessionPlayReducer.ValidateEnvelope(origin, e with { BaseCharacterVersion = origin.Authority.Binding.Version });
    }

    private static SessionOverlaySnapshot ReplaySettlement5712(ISessionPlayReader reader, SessionPlayOrigin origin,
        IReadOnlyList<SessionEventEnvelope> events, string operationId, long? beforeOrdinal,
        out SessionPlayElementalProjection elemental)
    {
        var ammunition = reader.ReadConditionTransfers().Where(x => x.Receipt is not null && x.Intent.Review.Ammunition is not null
            && (beforeOrdinal is null || x.Intent.Review.Ammunition.PlayOrdinal < beforeOrdinal.Value))
            .OrderBy(x => x.Receipt!.Canonical.RecordedRevision)
            .Select(x => new SessionAmmunitionCheckpoint5712(x.Intent.Review.Ammunition!.EventCount, x.Receipt!.ContinuedAuthority)).ToArray();
        var snapshot = SessionPlayReducer.ReplayWithNaturalRecovery(origin, events, operationId,
            RecoveryCheckpoints(reader, beforeOrdinal), out elemental, AdmittedEventVersions5712(reader, origin), ammunition,
            FocusCheckpoints7490(reader, beforeOrdinal));
        var operation = reader.ReadOperations().SingleOrDefault(x => x.Intent.OperationId == operationId);
        return snapshot with { BaseCharacterVersion = operation?.Intent.BaseCharacterVersion ?? CurrentPlayVersion5712(reader, origin.Authority) };
    }

    private sealed class SettlementReader5712(SessionPlaySettlementContinuation5712 value) : ISessionPlayReader
    {
        public IReadOnlyList<SessionFocusContinuationStored7490> ReadFocusContinuations7490() => value.Focuses7490 ?? [];
        public SessionPlayOrigin ReadOrigin() => value.Origin;
        public IReadOnlyList<SessionPlayStoredOperation> ReadOperations() => value.Operations;
        public IReadOnlyList<SessionConditionTransferStored> ReadConditionTransfers() => value.Transfers;
        public IReadOnlyList<SessionNaturalRecoveryStored> ReadNaturalRecoveries() => value.Recoveries;
    }
}

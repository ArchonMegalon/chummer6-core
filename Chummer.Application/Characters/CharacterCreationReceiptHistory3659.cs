using System.Runtime.CompilerServices;
using System.Text.Json;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

/// <summary>Validated historical data only; no source, owner, activation or write authority.</summary>
internal sealed class CharacterCreationReceiptHistory3659
{
    // Object identity only, never a digest-keyed cache. Ordinary record copies
    // and JSON round trips cannot copy this proof; record equality stays unchanged.
    private static readonly ConditionalWeakTable<WorkspaceStoredDocument, CharacterCreationReceiptHistory3659> Proofs = new();
    private readonly string binding;
    private readonly string historyJson;
    private readonly long revision;

    private CharacterCreationReceiptHistory3659(string binding, string historyJson, long revision)
        => (this.binding, this.historyJson, this.revision) = (binding, historyJson, revision);

    internal static CharacterCreationReceiptHistory3659? Find(WorkspaceStoredDocument observation)
        => Proofs.TryGetValue(observation, out var proof) ? proof : null;

    internal static WorkspaceStoredDocument Attach(WorkspaceStoredDocument observation,
        ICharacterCreationRuleSourceRecovery? recovery)
    {
        if (observation.Document.AuxiliaryState.CharacterCreationFinalizationArchive is null)
            return observation;
        // Validate a detached historical graph, fenced against the original
        // observation before and after recovery. A caller cannot race a mutable
        // auxiliary collection between validation and proof issuance.
        string binding = Binding(observation);
        var auxiliary = JsonSerializer.Deserialize<WorkspaceDocumentAuxiliaryState>(
            JsonSerializer.Serialize(observation.Document.AuxiliaryState))!;
        var unproven = observation with
        {
            Document = observation.Document with
            { State = observation.Document.State with { AuxiliaryState = auxiliary } }
        };
        if (Binding(unproven) != binding) throw new InvalidDataException("Creation observation changed.");
        if (!CharacterCreationFinalizationReceiptLedgerIntegrity.TryReadReceiptHistory(
                unproven, out var history, out long revision, recovery))
            throw new InvalidDataException("Creation receipt history is unavailable.");
        string historyJson = JsonSerializer.Serialize(history);
        if (Binding(unproven) != binding || Binding(observation) != binding)
            throw new InvalidDataException("Creation observation changed.");
        var proven = observation with { };
        Proofs.Add(proven, new(binding, historyJson, revision));
        return proven;
    }

    internal bool TryRead(WorkspaceStoredDocument observation,
        out WorkspaceDocumentAuxiliaryState history, out long historyRevision)
    {
        history = null!;
        historyRevision = 0;
        if (binding != Binding(observation)) return false;
        // Readers cannot mutate the private, validated historical graph.
        history = JsonSerializer.Deserialize<WorkspaceDocumentAuxiliaryState>(historyJson)!;
        historyRevision = revision;
        return history is not null;
    }

    private static string Binding(WorkspaceStoredDocument observation) =>
        CharacterCreationFinalizationDigest.Compute(new
        {
            observation.Id, observation.ContentRevision, observation.SavedRevision,
            observation.LastUpdatedUtc, observation.Document, observation.LocalHistory,
            observation.HasDelegatedGmHistory, observation.DelegatedGmHistorySegmentStarts
        });
}

using Chummer.Contracts.Campaign;
using Chummer.Contracts.Owners;

namespace Chummer.Application.Campaign;

public interface IOppositionPacketContractService
{
    IReadOnlyList<OppositionPacketContract> ListOppositionPackets(OwnerScope owner, string? rulesetId = null);

    OppositionPacketContract? GetOppositionPacket(OwnerScope owner, string packetId, string? rulesetId = null);

    IReadOnlyList<ScenePacketContract> ListScenePackets(OwnerScope owner, string? rulesetId = null);

    ScenePacketContract? GetScenePacket(OwnerScope owner, string scenePacketId, string? rulesetId = null);
}

using Chummer.Contracts.Session;

namespace Chummer.Application.Session;

public interface ISessionOverlayProjectionService
{
    SessionOverlayProjection Replay(
        string overlayId,
        string characterId,
        string runtimeFingerprint,
        IReadOnlyList<SessionEventEnvelope> events);
}

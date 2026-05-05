using Chummer.Contracts.Diagnostics;

namespace Chummer.Application.Explain;

public interface IExplainValuePacketService
{
    ExplainValuePacket CreatePacket(ExplainValuePacketInput input);
}

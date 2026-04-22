using Chummer.Contracts.Diagnostics;

namespace Chummer.Application.Explain;

public interface ICalculationReportService
{
    CalculationReportPacket CreatePacket(CalculationReportInput input);
}

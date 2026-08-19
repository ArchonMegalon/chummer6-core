using Chummer.Contracts.BuildGhost;

namespace Chummer.Application.BuildGhost;

public interface IBuildGhostAnalysisService
{
    BuildGhostAnalysisPacket Analyze(BuildGhostAnalysisRequest request);

    BuildGhostProviderValidationResult ValidateProviderAnswer(
        BuildGhostAnalysisPacket packet,
        BuildGhostProviderAnswer answer);
}

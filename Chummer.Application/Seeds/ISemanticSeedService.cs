using Chummer.Contracts.Seeds;

namespace Chummer.Application.Seeds;

public interface ISemanticSeedService
{
    CharacterDossierSeed BuildCharacterDossierSeed(string characterId, string rulesetId, string runtimeFingerprint);
    NpcDossierSeed BuildNpcDossierSeed(string npcId, string rulesetId);
    RunSummarySeed BuildRunSummarySeed(string runId, string rulesetId);
    BuildIdeaSeed BuildBuildIdeaSeed(string ideaId, string rulesetId);
    ShadowfeedSeed BuildShadowfeedSeed(string feedId, string rulesetId);
}

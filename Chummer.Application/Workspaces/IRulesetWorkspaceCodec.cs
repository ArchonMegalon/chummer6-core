using Chummer.Contracts.Api;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Rulesets;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Workspaces;

public interface IRulesetWorkspaceCodec
{
    string RulesetId { get; }

    int SchemaVersion { get; }

    string PayloadKind { get; }

    WorkspacePayloadEnvelope WrapImport(string rulesetId, WorkspaceImportDocument document);

    CharacterFileSummary ParseSummary(WorkspacePayloadEnvelope envelope);

    object ParseSection(string sectionId, WorkspacePayloadEnvelope envelope);

    CharacterOverviewProjection ParseOverview(WorkspacePayloadEnvelope envelope)
    {
        return new CharacterOverviewProjection(
            Profile: RequireSection<CharacterProfileSection>("profile", envelope),
            Progress: RequireSection<CharacterProgressSection>("progress", envelope),
            Skills: RequireSection<CharacterSkillsSection>("skills", envelope),
            Rules: RequireSection<CharacterRulesSection>("rules", envelope),
            Build: RequireSection<CharacterBuildSection>("build", envelope),
            Movement: RequireSection<CharacterMovementSection>("movement", envelope),
            Awakening: RequireSection<CharacterAwakeningSection>("awakening", envelope));
    }

    CharacterValidationResult Validate(WorkspacePayloadEnvelope envelope);

    WorkspacePayloadEnvelope UpdateMetadata(WorkspacePayloadEnvelope envelope, UpdateWorkspaceMetadata command);

    WorkspaceDownloadReceipt BuildDownload(
        CharacterWorkspaceId id,
        WorkspacePayloadEnvelope envelope,
        WorkspaceDocumentFormat format);

    DataExportBundle BuildExportBundle(WorkspacePayloadEnvelope envelope);

    private TSection RequireSection<TSection>(
        string sectionId,
        WorkspacePayloadEnvelope envelope)
        where TSection : class
        => ParseSection(sectionId, envelope) as TSection
            ?? throw new InvalidOperationException(
                $"Ruleset '{RulesetId}' section '{sectionId}' did not return {typeof(TSection).Name}.");
}

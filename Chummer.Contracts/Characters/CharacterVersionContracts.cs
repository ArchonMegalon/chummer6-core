using Chummer.Contracts.Content;
using Chummer.Contracts.Rulesets;

namespace Chummer.Contracts.Characters;

public sealed record CharacterVersionReference(
    string CharacterId,
    string VersionId,
    string RulesetId,
    string RuntimeFingerprint);

public sealed record CharacterVersion(
    CharacterVersionReference Reference,
    ResolvedRuntimeLock RuntimeLock,
    WorkspacePayloadEnvelope PayloadEnvelope,
    DateTimeOffset CreatedAtUtc,
    CharacterFileSummary? Summary = null,
    string? SourceWorkspaceId = null);

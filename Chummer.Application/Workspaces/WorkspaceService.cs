using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using Chummer.Contracts.Api;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Rulesets;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Workspaces;

public sealed class WorkspaceService : IWorkspaceService
{
    private readonly IWorkspaceStore _workspaceStore;
    private readonly IRulesetWorkspaceCodecResolver _workspaceCodecResolver;
    private readonly IWorkspaceImportRulesetDetector _workspaceImportRulesetDetector;

    public WorkspaceService(
        IWorkspaceStore workspaceStore,
        IRulesetWorkspaceCodecResolver workspaceCodecResolver,
        IWorkspaceImportRulesetDetector workspaceImportRulesetDetector)
    {
        _workspaceStore = workspaceStore;
        _workspaceCodecResolver = workspaceCodecResolver;
        _workspaceImportRulesetDetector = workspaceImportRulesetDetector;
    }

    public WorkspaceImportResult Import(WorkspaceImportDocument document)
    {
        return Import(OwnerScope.LocalSingleUser, document);
    }

    public WorkspaceImportResult Import(OwnerScope owner, WorkspaceImportDocument document)
    {
        string? rulesetId = RulesetDefaults.NormalizeOptional(document.RulesetId)
            ?? _workspaceImportRulesetDetector.Detect(document);
        if (rulesetId is null)
            throw new InvalidOperationException("Workspace ruleset is required or must be detectable from import content.");

        IRulesetWorkspaceCodec codec = _workspaceCodecResolver.Resolve(rulesetId);
        WorkspacePayloadEnvelope envelope = codec.WrapImport(rulesetId, document);
        CharacterFileSummary summary = codec.ParseSummary(envelope);

        CharacterWorkspaceId id = _workspaceStore.Create(owner, new WorkspaceDocument(
            PayloadEnvelope: envelope,
            Format: document.Format));
        return new WorkspaceImportResult(id, summary, envelope.RulesetId);
    }

    public IReadOnlyList<WorkspaceListItem> List(int? maxCount = null)
    {
        return List(OwnerScope.LocalSingleUser, maxCount);
    }

    public IReadOnlyList<WorkspaceListItem> List(OwnerScope owner, int? maxCount = null)
    {
        List<WorkspaceListItem> workspaces = [];
        int? normalizedMaxCount = maxCount is > 0 ? maxCount : null;

        foreach (WorkspaceStoreEntry entry in _workspaceStore.List(owner))
        {
            if (normalizedMaxCount is not null && workspaces.Count >= normalizedMaxCount.Value)
            {
                break;
            }

            CharacterWorkspaceId id = entry.Id;
            if (!_workspaceStore.TryGet(owner, id, out WorkspaceDocument document))
            {
                continue;
            }

            WorkspacePayloadEnvelope envelope = ResolveEnvelope(document);
            CharacterFileSummary summary;
            try
            {
                IRulesetWorkspaceCodec codec = _workspaceCodecResolver.Resolve(envelope.RulesetId);
                summary = codec.ParseSummary(envelope);
            }
            catch
            {
                summary = new CharacterFileSummary(
                    Name: $"Workspace {id.Value}",
                    Alias: string.Empty,
                    Metatype: string.Empty,
                    BuildMethod: string.Empty,
                    CreatedVersion: string.Empty,
                    AppVersion: string.Empty,
                    Karma: 0m,
                    Nuyen: 0m,
                    Created: false);
            }

            workspaces.Add(new WorkspaceListItem(
                Id: id,
                Summary: summary,
                LastUpdatedUtc: entry.LastUpdatedUtc,
                RulesetId: envelope.RulesetId));
        }

        return workspaces;
    }

    public bool Close(CharacterWorkspaceId id)
    {
        return Close(OwnerScope.LocalSingleUser, id);
    }

    public bool Close(OwnerScope owner, CharacterWorkspaceId id)
    {
        return _workspaceStore.Delete(owner, id);
    }

    public object? GetSection(CharacterWorkspaceId id, string sectionId)
    {
        return GetSection(OwnerScope.LocalSingleUser, id, sectionId);
    }

    public object? GetSection(OwnerScope owner, CharacterWorkspaceId id, string sectionId)
    {
        if (!TryResolveEnvelope(owner, id, out WorkspacePayloadEnvelope envelope))
        {
            return null;
        }

        IRulesetWorkspaceCodec codec = _workspaceCodecResolver.Resolve(envelope.RulesetId);
        return codec.ParseSection(sectionId, envelope);
    }

    public CharacterFileSummary? GetSummary(CharacterWorkspaceId id)
    {
        return GetSummary(OwnerScope.LocalSingleUser, id);
    }

    public CharacterFileSummary? GetSummary(OwnerScope owner, CharacterWorkspaceId id)
    {
        if (!TryResolveEnvelope(owner, id, out WorkspacePayloadEnvelope envelope))
        {
            return null;
        }

        IRulesetWorkspaceCodec codec = _workspaceCodecResolver.Resolve(envelope.RulesetId);
        return codec.ParseSummary(envelope);
    }

    public CharacterValidationResult? Validate(CharacterWorkspaceId id)
    {
        return Validate(OwnerScope.LocalSingleUser, id);
    }

    public CharacterValidationResult? Validate(OwnerScope owner, CharacterWorkspaceId id)
    {
        if (!TryResolveEnvelope(owner, id, out WorkspacePayloadEnvelope envelope))
        {
            return null;
        }

        IRulesetWorkspaceCodec codec = _workspaceCodecResolver.Resolve(envelope.RulesetId);
        return codec.Validate(envelope);
    }

    public CharacterProfileSection? GetProfile(CharacterWorkspaceId id)
    {
        return GetProfile(OwnerScope.LocalSingleUser, id);
    }

    public CharacterProfileSection? GetProfile(OwnerScope owner, CharacterWorkspaceId id)
    {
        return TryParseSection<CharacterProfileSection>(owner, id, "profile");
    }

    public CharacterProgressSection? GetProgress(CharacterWorkspaceId id)
    {
        return GetProgress(OwnerScope.LocalSingleUser, id);
    }

    public CharacterProgressSection? GetProgress(OwnerScope owner, CharacterWorkspaceId id)
    {
        return TryParseSection<CharacterProgressSection>(owner, id, "progress");
    }

    public CharacterSkillsSection? GetSkills(CharacterWorkspaceId id)
    {
        return GetSkills(OwnerScope.LocalSingleUser, id);
    }

    public CharacterSkillsSection? GetSkills(OwnerScope owner, CharacterWorkspaceId id)
    {
        return TryParseSection<CharacterSkillsSection>(owner, id, "skills");
    }

    public CharacterRulesSection? GetRules(CharacterWorkspaceId id)
    {
        return GetRules(OwnerScope.LocalSingleUser, id);
    }

    public CharacterRulesSection? GetRules(OwnerScope owner, CharacterWorkspaceId id)
    {
        return TryParseSection<CharacterRulesSection>(owner, id, "rules");
    }

    public CharacterBuildSection? GetBuild(CharacterWorkspaceId id)
    {
        return GetBuild(OwnerScope.LocalSingleUser, id);
    }

    public CharacterBuildSection? GetBuild(OwnerScope owner, CharacterWorkspaceId id)
    {
        return TryParseSection<CharacterBuildSection>(owner, id, "build");
    }

    public CharacterMovementSection? GetMovement(CharacterWorkspaceId id)
    {
        return GetMovement(OwnerScope.LocalSingleUser, id);
    }

    public CharacterMovementSection? GetMovement(OwnerScope owner, CharacterWorkspaceId id)
    {
        return TryParseSection<CharacterMovementSection>(owner, id, "movement");
    }

    public CharacterAwakeningSection? GetAwakening(CharacterWorkspaceId id)
    {
        return GetAwakening(OwnerScope.LocalSingleUser, id);
    }

    public CharacterAwakeningSection? GetAwakening(OwnerScope owner, CharacterWorkspaceId id)
    {
        return TryParseSection<CharacterAwakeningSection>(owner, id, "awakening");
    }

    public CommandResult<CharacterProfileSection> UpdateMetadata(CharacterWorkspaceId id, UpdateWorkspaceMetadata command)
    {
        return UpdateMetadata(OwnerScope.LocalSingleUser, id, command);
    }

    public CommandResult<CharacterProfileSection> UpdateMetadata(OwnerScope owner, CharacterWorkspaceId id, UpdateWorkspaceMetadata command)
    {
        if (!_workspaceStore.TryGet(owner, id, out WorkspaceDocument document))
        {
            return new CommandResult<CharacterProfileSection>(
                Success: false,
                Value: null,
                Error: "Workspace not found.");
        }

        WorkspacePayloadEnvelope envelope = ResolveEnvelope(document);
        IRulesetWorkspaceCodec codec = _workspaceCodecResolver.Resolve(envelope.RulesetId);
        WorkspacePayloadEnvelope updatedEnvelope = codec.UpdateMetadata(envelope, command);

        _workspaceStore.Save(owner, id, CreateUpdatedDocument(document, updatedEnvelope));

        CharacterProfileSection? profile = codec.ParseSection("profile", updatedEnvelope) as CharacterProfileSection;
        if (profile is null)
        {
            return new CommandResult<CharacterProfileSection>(
                Success: false,
                Value: null,
                Error: "Profile section was not available after metadata update.");
        }

        return new CommandResult<CharacterProfileSection>(
            Success: true,
            Value: profile,
            Error: null);
    }

    public CommandResult<WorkspaceSaveReceipt> Save(CharacterWorkspaceId id)
    {
        return Save(OwnerScope.LocalSingleUser, id);
    }

    public CommandResult<WorkspaceSaveReceipt> Save(OwnerScope owner, CharacterWorkspaceId id)
    {
        if (!_workspaceStore.TryGet(owner, id, out WorkspaceDocument document))
        {
            return new CommandResult<WorkspaceSaveReceipt>(
                Success: false,
                Value: null,
                Error: "Workspace not found.");
        }

        WorkspacePayloadEnvelope envelope = ResolveEnvelope(document);
        return new CommandResult<WorkspaceSaveReceipt>(
                Success: true,
                Value: new WorkspaceSaveReceipt(
                    Id: id,
                    DocumentLength: envelope.Payload.Length,
                    RulesetId: envelope.RulesetId),
                Error: null);
    }

    public CommandResult<WorkspaceDownloadReceipt> Download(CharacterWorkspaceId id)
    {
        return Download(OwnerScope.LocalSingleUser, id);
    }

    public CommandResult<WorkspaceDownloadReceipt> Download(OwnerScope owner, CharacterWorkspaceId id)
    {
        if (!_workspaceStore.TryGet(owner, id, out WorkspaceDocument document))
        {
            return new CommandResult<WorkspaceDownloadReceipt>(
                Success: false,
                Value: null,
                Error: "Workspace not found.");
        }

        WorkspacePayloadEnvelope envelope = ResolveEnvelope(document);
        IRulesetWorkspaceCodec codec = _workspaceCodecResolver.Resolve(envelope.RulesetId);
        WorkspaceDownloadReceipt receipt = codec.BuildDownload(id, envelope, document.Format);

        return new CommandResult<WorkspaceDownloadReceipt>(
            Success: true,
            Value: receipt,
                Error: null);
    }

    public CommandResult<WorkspaceExportReceipt> Export(CharacterWorkspaceId id)
    {
        return Export(OwnerScope.LocalSingleUser, id);
    }

    public CommandResult<WorkspaceExportReceipt> Export(OwnerScope owner, CharacterWorkspaceId id)
    {
        if (!_workspaceStore.TryGet(owner, id, out WorkspaceDocument document))
        {
            return new CommandResult<WorkspaceExportReceipt>(
                Success: false,
                Value: null,
                Error: "Workspace not found.");
        }

        WorkspacePayloadEnvelope envelope = ResolveEnvelope(document);
        IRulesetWorkspaceCodec codec = _workspaceCodecResolver.Resolve(envelope.RulesetId);
        DataExportBundle bundle = codec.BuildExportBundle(envelope);
        WorkspaceExportReceipt receipt = BuildExportReceipt(id, envelope.RulesetId, bundle);

        return new CommandResult<WorkspaceExportReceipt>(
            Success: true,
            Value: receipt,
                Error: null);
    }

    public CommandResult<WorkspacePrintReceipt> Print(CharacterWorkspaceId id)
    {
        return Print(OwnerScope.LocalSingleUser, id);
    }

    public CommandResult<WorkspacePrintReceipt> Print(OwnerScope owner, CharacterWorkspaceId id)
    {
        if (!_workspaceStore.TryGet(owner, id, out WorkspaceDocument document))
        {
            return new CommandResult<WorkspacePrintReceipt>(
                Success: false,
                Value: null,
                Error: "Workspace not found.");
        }

        WorkspacePayloadEnvelope envelope = ResolveEnvelope(document);
        IRulesetWorkspaceCodec codec = _workspaceCodecResolver.Resolve(envelope.RulesetId);
        DataExportBundle bundle = codec.BuildExportBundle(envelope);
        WorkspacePrintReceipt receipt = BuildPrintReceipt(id, envelope.RulesetId, bundle);

        return new CommandResult<WorkspacePrintReceipt>(
            Success: true,
            Value: receipt,
                Error: null);
    }

    private TSection? TryParseSection<TSection>(OwnerScope owner, CharacterWorkspaceId id, string sectionId)
        where TSection : class
    {
        return GetSection(owner, id, sectionId) as TSection;
    }

    private static WorkspaceExportReceipt BuildExportReceipt(
        CharacterWorkspaceId id,
        string rulesetId,
        DataExportBundle bundle)
    {
        string json = JsonSerializer.Serialize(bundle, new JsonSerializerOptions
        {
            WriteIndented = true
        });
        byte[] bytes = Encoding.UTF8.GetBytes(json);
        string baseFileName = string.IsNullOrWhiteSpace(bundle.Summary.Name) ? id.Value : bundle.Summary.Name;
        string fileName = $"{SanitizeFileName(baseFileName)}-export.json";

        return new WorkspaceExportReceipt(
            Id: id,
            Format: WorkspaceDocumentFormat.Json,
            ContentBase64: Convert.ToBase64String(bytes),
            FileName: fileName,
            DocumentLength: bytes.Length,
            RulesetId: RulesetDefaults.NormalizeOptional(rulesetId) ?? string.Empty);
    }

    private static WorkspacePrintReceipt BuildPrintReceipt(
        CharacterWorkspaceId id,
        string rulesetId,
        DataExportBundle bundle)
    {
        string title = string.IsNullOrWhiteSpace(bundle.Summary.Name)
            ? $"Character {id.Value}"
            : bundle.Summary.Name;
        string html = BuildPrintHtml(bundle, title);
        byte[] bytes = Encoding.UTF8.GetBytes(html);

        return new WorkspacePrintReceipt(
            Id: id,
            ContentBase64: Convert.ToBase64String(bytes),
            FileName: $"{SanitizeFileName(title)}-print.html",
            MimeType: "text/html",
            DocumentLength: bytes.Length,
            Title: title,
            RulesetId: RulesetDefaults.NormalizeOptional(rulesetId) ?? string.Empty);
    }

    private static string BuildPrintHtml(DataExportBundle bundle, string title)
    {
        string encodedTitle = WebUtility.HtmlEncode(title);
        string alias = WebUtility.HtmlEncode(bundle.Profile?.Alias ?? bundle.Summary.Alias);
        string metatype = WebUtility.HtmlEncode(bundle.Profile?.Metatype ?? bundle.Summary.Metatype);
        string buildMethod = WebUtility.HtmlEncode(bundle.Profile?.BuildMethod ?? bundle.Summary.BuildMethod);
        string playerName = WebUtility.HtmlEncode(bundle.Profile?.PlayerName ?? string.Empty);
        string concept = WebUtility.HtmlEncode(bundle.Profile?.Concept ?? string.Empty);
        string karma = bundle.Progress?.Karma.ToString("0.##") ?? bundle.Summary.Karma.ToString("0.##");
        string nuyen = bundle.Progress?.Nuyen.ToString("0.##") ?? bundle.Summary.Nuyen.ToString("0.##");
        string streetCred = bundle.Progress?.StreetCred.ToString() ?? "0";
        string initiative = bundle.Progress?.InitiateGrade.ToString() ?? "0";
        string attributeCount = bundle.Attributes?.Count.ToString() ?? "0";
        string skillCount = bundle.Skills?.Count.ToString() ?? "0";
        string gearCount = bundle.Inventory?.GearCount.ToString() ?? "0";
        string contactCount = bundle.Contacts?.Count.ToString() ?? "0";

        StringBuilder html = new();
        html.AppendLine("<!DOCTYPE html>");
        html.AppendLine("<html lang=\"en\">");
        html.AppendLine("<head>");
        html.AppendLine("  <meta charset=\"utf-8\" />");
        html.AppendLine($"  <title>{encodedTitle}</title>");
        html.AppendLine("  <style>");
        html.AppendLine("    body { font-family: 'Segoe UI', sans-serif; margin: 2rem; color: #111827; }");
        html.AppendLine("    h1, h2 { margin-bottom: 0.5rem; }");
        html.AppendLine("    .grid { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 0.75rem 1.5rem; }");
        html.AppendLine("    .card { border: 1px solid #d1d5db; border-radius: 12px; padding: 1rem 1.25rem; margin-bottom: 1rem; }");
        html.AppendLine("    dt { font-weight: 700; }");
        html.AppendLine("    dd { margin: 0 0 0.5rem 0; }");
        html.AppendLine("  </style>");
        html.AppendLine("</head>");
        html.AppendLine("<body>");
        html.AppendLine($"  <h1>{encodedTitle}</h1>");
        html.AppendLine("  <div class=\"card\">");
        html.AppendLine("    <h2>Profile</h2>");
        html.AppendLine("    <dl class=\"grid\">");
        html.AppendLine($"      <dt>Alias</dt><dd>{alias}</dd>");
        html.AppendLine($"      <dt>Metatype</dt><dd>{metatype}</dd>");
        html.AppendLine($"      <dt>Build Method</dt><dd>{buildMethod}</dd>");
        html.AppendLine($"      <dt>Player</dt><dd>{playerName}</dd>");
        html.AppendLine($"      <dt>Concept</dt><dd>{concept}</dd>");
        html.AppendLine("    </dl>");
        html.AppendLine("  </div>");
        html.AppendLine("  <div class=\"card\">");
        html.AppendLine("    <h2>Progress</h2>");
        html.AppendLine("    <dl class=\"grid\">");
        html.AppendLine($"      <dt>Karma</dt><dd>{WebUtility.HtmlEncode(karma)}</dd>");
        html.AppendLine($"      <dt>Nuyen</dt><dd>{WebUtility.HtmlEncode(nuyen)}</dd>");
        html.AppendLine($"      <dt>Street Cred</dt><dd>{WebUtility.HtmlEncode(streetCred)}</dd>");
        html.AppendLine($"      <dt>Initiate Grade</dt><dd>{WebUtility.HtmlEncode(initiative)}</dd>");
        html.AppendLine("    </dl>");
        html.AppendLine("  </div>");
        html.AppendLine("  <div class=\"card\">");
        html.AppendLine("    <h2>Coverage</h2>");
        html.AppendLine("    <dl class=\"grid\">");
        html.AppendLine($"      <dt>Attributes</dt><dd>{WebUtility.HtmlEncode(attributeCount)}</dd>");
        html.AppendLine($"      <dt>Skills</dt><dd>{WebUtility.HtmlEncode(skillCount)}</dd>");
        html.AppendLine($"      <dt>Gear</dt><dd>{WebUtility.HtmlEncode(gearCount)}</dd>");
        html.AppendLine($"      <dt>Contacts</dt><dd>{WebUtility.HtmlEncode(contactCount)}</dd>");
        html.AppendLine("    </dl>");
        html.AppendLine("  </div>");
        html.AppendLine("</body>");
        html.AppendLine("</html>");
        return html.ToString();
    }

    private static string SanitizeFileName(string value)
    {
        char[] invalidChars = Path.GetInvalidFileNameChars();
        StringBuilder builder = new(value.Length);
        foreach (char character in value)
        {
            builder.Append(invalidChars.Contains(character) ? '_' : character);
        }

        string sanitized = builder.ToString().Trim();
        return string.IsNullOrWhiteSpace(sanitized) ? "workspace" : sanitized;
    }

    private bool TryResolveEnvelope(OwnerScope owner, CharacterWorkspaceId id, out WorkspacePayloadEnvelope envelope)
    {
        if (!_workspaceStore.TryGet(owner, id, out WorkspaceDocument document))
        {
            envelope = default!;
            return false;
        }

        envelope = ResolveEnvelope(document);
        return true;
    }

    private WorkspacePayloadEnvelope ResolveEnvelope(WorkspaceDocument document)
    {
        WorkspaceDocumentState state = document.State;
        string normalizedRulesetId = state.RulesetId;
        IRulesetWorkspaceCodec codec = _workspaceCodecResolver.Resolve(normalizedRulesetId);
        int schemaVersion = state.SchemaVersion > 0
            ? state.SchemaVersion
            : codec.SchemaVersion;
        string payloadKind = string.IsNullOrWhiteSpace(state.PayloadKind)
            ? codec.PayloadKind
            : state.PayloadKind;
        return new WorkspacePayloadEnvelope(
            RulesetId: normalizedRulesetId,
            SchemaVersion: schemaVersion,
            PayloadKind: payloadKind,
            Payload: state.Payload);
    }

    private static WorkspaceDocument CreateUpdatedDocument(WorkspaceDocument current, WorkspacePayloadEnvelope updatedEnvelope)
    {
        return new WorkspaceDocument(
            State: new WorkspaceDocumentState(updatedEnvelope),
            Format: current.Format);
    }
}

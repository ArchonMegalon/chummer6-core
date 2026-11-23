using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

public static class CharacterCareerGearEquipmentTransaction
{
    public const int MaximumReceipts = 1024;
    public const int MaximumLedgerBytes = 4 * 1024 * 1024;
    public static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    public static bool IsDigest(string? value) => value is { Length: 64 }
        && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
    public static bool IsGeneration(string? value) => Guid.TryParseExact(value, "N", out var id)
        && id != Guid.Empty && id.ToString("N") == value;
    public static bool Same<T>(T left, T right) => JsonSerializer.Serialize(left) == JsonSerializer.Serialize(right);
    public static string CommandDigest(CharacterCareerGearEquipmentCommand command)
        => Hash("chummer.gear-equipped.command/v1\0" + JsonSerializer.Serialize(command));
    public static string ReceiptDigest(CharacterCareerGearEquipmentReceipt receipt)
        => Hash("chummer.gear-equipped.receipt/v1\0" + JsonSerializer.Serialize(receipt with { ReceiptDigest = "" }));
    // Detach mutable identity path collections before validating or hashing an original.
    public static CharacterCareerGearEquipmentCommand? FreezeConfirmedCommand(CharacterCareerGearEquipmentCommand command)
    {
        try
        {
            var frozen = JsonSerializer.Deserialize<CharacterCareerGearEquipmentCommand>(JsonSerializer.SerializeToUtf8Bytes(command));
            return IsConfirmedCommand(frozen) ? frozen : null;
        }
        catch (Exception e) when (e is JsonException or ArgumentException or InvalidOperationException)
        { return null; }
    }
    public static bool MatchesOwner(OwnerScope owner, CharacterCareerGearEquipmentBinding binding)
        => !string.IsNullOrWhiteSpace(owner.NormalizedValue) && owner.NormalizedValue.Length <= 512
            && owner.NormalizedValue == binding.OwnerId && owner.IsLocalSingleUser == binding.TrustedLocalOwner
            && (!owner.UsesLocalSingleUserValue || owner.IsLocalSingleUser);
    public static bool IsConfirmedCommand(CharacterCareerGearEquipmentCommand? c)
        => c is { ExplicitlyConfirmed: true, Request: not null, Binding: not null }
            && c.Request.OperationId != Guid.Empty
            && CharacterAfterRunSettlementServiceIntegrity.IsValidWorkspaceId(c.Request.WorkspaceId)
            && c.Request.WorkspaceId == c.Binding.WorkspaceId
            && c.Binding.OwnerId is { Length: > 0 and <= 512 } owner && owner == owner.Trim().ToLowerInvariant()
            && (owner == OwnerScope.LocalSingleUser.NormalizedValue) == c.Binding.TrustedLocalOwner
            && c.Binding.ContentRevision is > 0 and < long.MaxValue && c.Binding.SavedRevision == c.Binding.ContentRevision
            && IsDigest(c.Binding.SourceDigest) && IsDigest(c.Binding.AuxiliaryDigest)
            && IsDigest(c.Binding.EnvironmentDigest) && IsDigest(c.Binding.RuntimeDigest)
            && IsGeneration(c.Binding.IncarnationId) && IsGeneration(c.Binding.SlotGeneration)
            && (c.Loadout is not null ? CharacterCareerLoadoutTransaction.IsConfirmed(c)
            : c.Request.Loadout is null && c.Before is not null
        && c.Request.Identity?.GearPath is { Count: > 0 and <= 64 }
            && CharacterGearEquipmentRules.IsValidIdentity(c.Request.Identity)
            && CharacterGearEquipmentRules.IdentityEquals(c.Request.Identity, c.Before.Identity)
            && c.Before.DisplayPath is { Length: <= 16384 } && c.Before.Phase == CharacterGearEquipmentPhase.Career
            && CharacterGearEquipmentRules.TryValidateMutation(c.Before, c.Request.ExpectedNodeRevision, c.Request.Equipped));

    public static bool TryRead(OwnerScope owner, WorkspaceStoredDocument saved, ICharacterSourceDataResolver sources,
        string runtime, out CharacterCareerGearEquipmentCatalog? catalog, string? slotGeneration = null)
        => TryReadBound(owner, saved, runtime,
            () => TryEnvironment(sources, saved.Document.Content, out string environment) ? environment : null,
            out catalog, slotGeneration);

    // An opaque canonical execution, never a caller-supplied environment digest.
    // The store retains owner, workspace and source custody around this pure projection.
    public static bool TryReadCurrentEnvironment(OwnerScope owner, WorkspaceStoredDocument saved,
        CharacterCreationRuleSourceCapture capture, CharacterCurrentRuleEnvironmentExecution execution,
        ICharacterFileQueries queries, string runtime, out CharacterCareerGearEquipmentCatalog? catalog,
        string? slotGeneration, CancellationToken cancellationToken = default)
        => TryReadCurrentEnvironmentCore(owner, saved, capture, execution, queries, runtime,
            out catalog, slotGeneration, cancellationToken, displayObservation: false);

    public static bool TryReadCurrentEnvironmentForDisplay(OwnerScope owner, WorkspaceStoredDocument saved,
        CharacterCreationRuleSourceCapture capture, CharacterCurrentRuleEnvironmentExecution execution,
        ICharacterFileQueries queries, string runtime, out CharacterCareerGearEquipmentCatalog? catalog,
        string? slotGeneration, CancellationToken cancellationToken = default)
        => TryReadCurrentEnvironmentCore(owner, saved, capture, execution, queries, runtime,
            out catalog, slotGeneration, cancellationToken, displayObservation: true);

    private static bool TryReadCurrentEnvironmentCore(OwnerScope owner, WorkspaceStoredDocument saved,
        CharacterCreationRuleSourceCapture capture, CharacterCurrentRuleEnvironmentExecution execution,
        ICharacterFileQueries queries, string runtime, out CharacterCareerGearEquipmentCatalog? catalog,
        string? slotGeneration, CancellationToken cancellationToken, bool displayObservation)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CharacterCurrentRuleCatalogValidation? ReadCatalog(CharacterCurrentRuleDomain domain)
            => displayObservation
                ? execution.QueryCatalogDisplayObservation(saved, capture, queries, domain, cancellationToken)
                : execution.QueryCatalog(saved, capture, queries, domain, cancellationToken);
        catalog = null;
        if (!capture.MatchesOriginalCharacter(saved.Document.Content)
            || ReadCatalog(CharacterCurrentRuleDomain.Gear) is null)
            return false;
        var resolution = capture.CreateCurrentEnvironmentResolution(execution.ExecutionProfile, execution.ExecutionProfileFingerprint);
        if (resolution is null || !TryCapturedEnvironment(capture, resolution.Resolver, saved.Document.Content, out string environment)
            || !TryReadBound(owner, saved, runtime, () => environment, out catalog, slotGeneration)) return false;
        if (catalog?.Loadouts?.Inventory.Any(n => n.ArmorId is not null) == true
            && ReadCatalog(CharacterCurrentRuleDomain.Armor) is null)
            catalog = catalog with { Loadouts = null, LoadoutError = "Current Armor rule authority is unavailable." };
        cancellationToken.ThrowIfCancellationRequested();
        if (displayObservation && capture.SourcebookSelection is null
            ? execution.RevalidateDisplayObservation(saved, capture, queries, cancellationToken)
            : execution.Revalidate(saved, capture, queries, cancellationToken)) return true;
        catalog = null;
        return false;
    }

    private static bool TryReadBound(OwnerScope owner, WorkspaceStoredDocument saved, string runtime,
        Func<string?> readEnvironment, out CharacterCareerGearEquipmentCatalog? catalog, string? slotGeneration)
    {
        catalog = null;
        if (saved.ContentRevision is <= 0 or long.MaxValue || saved.SavedRevision != saved.ContentRevision
            || !IsDigest(runtime) || !IsValidHistory(saved) || !IsGeneration(slotGeneration)
            || saved.LocalHistory is not { } local || !local.IsValid(saved.ContentRevision)) return false;
        var document = saved.Document;
        if (document.Format != WorkspaceDocumentFormat.NativeXml || document.RulesetId != "sr5"
            || document.SchemaVersion != 1 || document.PayloadKind is not ("workspace" or "sr5/chum5-xml")) return false;
        try
        {
            var nodes = CharacterGearEquipmentCapture.ProjectAll(document.Content);
            if (nodes.Any(n => n.Phase != CharacterGearEquipmentPhase.Career)) return false;
            string? environment = readEnvironment();
            if (environment is null) return false;
            var binding = new CharacterCareerGearEquipmentBinding(owner.NormalizedValue, owner.IsLocalSingleUser,
                saved.Id, saved.ContentRevision, saved.SavedRevision, SourceDigest(document),
                document.AuxiliaryStateDigest, environment, runtime, local.IncarnationId, slotGeneration!);
            if (!MatchesOwner(owner, binding)) return false;
            catalog = new(binding, nodes);
            try { catalog = catalog with { Loadouts = CharacterCareerLoadoutTransaction.Capture(document.Content,
                document.AuxiliaryState.CharacterCareerLoadoutPresets, nodes) }; }
            catch (Exception e) when (e is InvalidOperationException or ArgumentException or XmlException)
            { catalog = catalog with { LoadoutError = "Inventory identities, configuration or equipped flags are not supported for presets." }; }
            return true;
        }
        catch (Exception e) when (e is XmlException or InvalidOperationException or ArgumentException
            or IOException or UnauthorizedAccessException or FormatException) { return false; }
    }

    public static bool TryEnvironment(ICharacterSourceDataResolver sources, string xml, out string digest)
    {
        digest = "";
        // Use the existing Core private source capture, including selected profile,
        // source ordering and effective policy. Never accept a UI-supplied environment digest.
        if (sources is not ICharacterCreationRuleSourceCapture captureAuthority
            || !captureAuthority.TryCaptureRuleSources(xml, out var capture) || capture is null
            || !capture.MatchesOriginalCharacter(xml) || !CharacterCreationFinalizationDigest.IsCanonical(capture.IntegrityDigest)
            || !CharacterCreationFinalizationDigest.IsCanonical(capture.EffectivePolicyDigest)) return false;
        return TryCapturedEnvironment(capture, capture.CreateReplayResolver(), xml, out digest);
    }

    internal static bool TryCapturedEnvironment(CharacterCreationRuleSourceCapture capture,
        ICharacterSourceDataResolver resolver, string xml, out string digest)
    {
        digest = "";
        if (!capture.MatchesOriginalCharacter(xml) || !CharacterCreationFinalizationDigest.IsCanonical(capture.IntegrityDigest)
            || !CharacterCreationFinalizationDigest.IsCanonical(capture.EffectivePolicyDigest)) return false;
        var context = resolver.TryCreateContext(xml);
        if (context is null || !context.TryResolveCareerSettingsProfile(out string profile, out string state)
            || string.IsNullOrWhiteSpace(profile) || string.IsNullOrWhiteSpace(state)
            || !context.TryResolveCareerSettingsProfile(out string again, out string stateAgain)
            || profile != again || state != stateAgain) return false;
        digest = Hash("chummer.gear-equipped.environment/v1\0" + JsonSerializer.Serialize(new
            { capture.IntegrityDigest, capture.EffectivePolicyDigest, profile, state }));
        return true;
    }

    private static string SourceDigest(WorkspaceDocument d) => Hash(JsonSerializer.Serialize(new
        { d.Format, d.RulesetId, d.SchemaVersion, d.PayloadKind, PayloadDigest = Hash(d.Content) }));

    public static bool TryPlan(CharacterCareerGearEquipmentCatalog catalog, CharacterCareerGearEquipmentRequest request,
        out CharacterCareerGearEquipmentCommand? command)
    {
        command = null;
        if (request.Loadout is not null) return CharacterCareerLoadoutTransaction.TryPlan(catalog, request, out command);
        var node = catalog.Nodes.SingleOrDefault(n => CharacterGearEquipmentRules.IdentityEquals(n.Identity, request.Identity));
        if (node is null) return false;
        var candidate = new CharacterCareerGearEquipmentCommand(request with { Identity = new(
            Array.AsReadOnly(request.Identity.GearPath.ToArray())) }, catalog.Binding, node, true);
        if (!IsConfirmedCommand(candidate)) return false;
        command = candidate with { ExplicitlyConfirmed = false };
        return true;
    }

    public static CharacterCareerGearEquipmentReceipt Seal(CharacterCareerGearEquipmentCommand command,
        CharacterCareerGearEquipmentOutcome outcome, long revision, string? payload = null, string? rejection = null)
    {
        var entry = new CharacterCareerGearEquipmentReceipt(command, CommandDigest(command), outcome, revision,
            outcome == CharacterCareerGearEquipmentOutcome.Applied ? revision : null,
            payload is null ? null : Hash(payload), rejection, "");
        return entry with { ReceiptDigest = ReceiptDigest(entry) };
    }
    public static bool IsCoherent(CharacterCareerGearEquipmentReceipt? r)
        => r is not null && IsConfirmedCommand(r.Command) && r.RecordedRevision > 0
            && r.CommandDigest == CommandDigest(r.Command) && r.ReceiptDigest == ReceiptDigest(r)
            && (r.Outcome switch
            {
                CharacterCareerGearEquipmentOutcome.Applied => r.RecordedRevision == r.Command.Binding.ContentRevision + 1
                    && r.CommittedRevision == r.RecordedRevision && IsDigest(r.PayloadDigestAfter) && r.Rejection is null,
                CharacterCareerGearEquipmentOutcome.Pending => r.RecordedRevision == r.Command.Binding.ContentRevision
                    && r.CommittedRevision is null && r.PayloadDigestAfter is null && r.Rejection is null,
                CharacterCareerGearEquipmentOutcome.Rejected => r.CommittedRevision is null && r.PayloadDigestAfter is null
                    && r.Rejection is { Length: > 0 and <= 128 },
                _ => false
            });
    public static CharacterCareerGearEquipmentOriginalOutcome Original(CharacterCareerGearEquipmentReceipt r)
    {
        var c = r.Command; var b = c.Binding;
        return new(b.OwnerId, b.TrustedLocalOwner, b.WorkspaceId, b.IncarnationId, b.SlotGeneration,
            c.Request.OperationId, r.CommandDigest, b.ContentRevision, r.Outcome, r.RecordedRevision,
            r.CommittedRevision, r.Outcome == CharacterCareerGearEquipmentOutcome.Pending ? null : r.ReceiptDigest);
    }
    public static bool IsCoherentOriginal(CharacterCareerGearEquipmentOriginalOutcome? p)
        => p is not null && p.OwnerId is { Length: > 0 and <= 512 }
            && p.OwnerId == p.OwnerId.Trim().ToLowerInvariant()
            && (p.OwnerId == OwnerScope.LocalSingleUser.NormalizedValue) == p.TrustedLocalOwner
            && CharacterAfterRunSettlementServiceIntegrity.IsValidWorkspaceId(p.WorkspaceId)
            && IsGeneration(p.IncarnationId) && IsGeneration(p.SlotGeneration) && p.OperationId != Guid.Empty
            && IsDigest(p.CommandDigest) && p.ExpectedRevision is > 0 and < long.MaxValue && p.RecordedRevision > 0
            && (p.Outcome switch
            {
                // NeverApplied's revision is the original binding, not today's saved revision.
                CharacterCareerGearEquipmentOutcome.Pending or CharacterCareerGearEquipmentOutcome.NeverApplied
                    => p.RecordedRevision == p.ExpectedRevision
                    && p.CommittedRevision is null && p.ReceiptDigest is null,
                CharacterCareerGearEquipmentOutcome.Applied => p.RecordedRevision == p.ExpectedRevision + 1
                    && p.CommittedRevision == p.RecordedRevision && IsDigest(p.ReceiptDigest),
                CharacterCareerGearEquipmentOutcome.Rejected => p.CommittedRevision is null && IsDigest(p.ReceiptDigest),
                _ => false
            });
    public static bool MatchesOriginal(CharacterCareerGearEquipmentCommand c, CharacterCareerGearEquipmentOriginalOutcome? p)
        => IsCoherentOriginal(p) && p!.OwnerId == c.Binding.OwnerId && p.TrustedLocalOwner == c.Binding.TrustedLocalOwner
            && p.WorkspaceId == c.Request.WorkspaceId && p.IncarnationId == c.Binding.IncarnationId
            && p.SlotGeneration == c.Binding.SlotGeneration && p.OperationId == c.Request.OperationId
            && p.CommandDigest == CommandDigest(c) && p.ExpectedRevision == c.Binding.ContentRevision;

    public static bool IsValidLedger(CharacterWorkspaceId id, long revision,
        IReadOnlyList<CharacterCareerGearEquipmentReceipt>? entries)
    {
        if (entries is null) return true;
        if (entries.Count > MaximumReceipts) return false;
        var ids = new HashSet<Guid>(); long bytes = 2;
        foreach (var r in entries)
        {
            if (!IsCoherent(r) || r.Command.Request.WorkspaceId != id || r.RecordedRevision > revision
                || !ids.Add(r.Command.Request.OperationId)) return false;
            bytes += JsonSerializer.SerializeToUtf8Bytes(r).Length + 1;
            if (bytes > MaximumLedgerBytes) return false;
        }
        return entries.Count(r => r.Outcome == CharacterCareerGearEquipmentOutcome.Pending) <= 1;
    }
    public static bool IsValidHistory(WorkspaceStoredDocument saved)
    {
        var history = saved.Document.AuxiliaryState.CharacterCareerGearEquipmentReceipts;
        return WorkspaceDocumentAuxiliaryState.HasSafeExtensionData(saved.Document.AuxiliaryState)
            && IsValidLedger(saved.Id, saved.ContentRevision, history)
            && (history ?? []).All(r => r.CommittedRevision is not { } revision || saved.SavedRevision >= revision)
            && (history ?? []).All(r => r.Outcome != CharacterCareerGearEquipmentOutcome.Pending
                || r.RecordedRevision == saved.ContentRevision)
            && (history ?? []).All(r => r.CommittedRevision != saved.ContentRevision
                || r.PayloadDigestAfter == Hash(saved.Document.Content));
    }
    public static CharacterCareerGearEquipmentResult Lookup(OwnerScope owner, WorkspaceStoredDocument saved,
        Guid operation, string digest)
    {
        if (!IsValidHistory(saved)) return new(CharacterCareerGearEquipmentOutcome.Corrupt);
        var entry = saved.Document.AuxiliaryState.CharacterCareerGearEquipmentReceipts?
            .SingleOrDefault(r => r.Command.Request.OperationId == operation);
        if (entry is null) return new(CharacterCareerGearEquipmentOutcome.NotFound, CurrentRevision: saved.ContentRevision);
        if (!MatchesOwner(owner, entry.Command.Binding) || entry.CommandDigest != digest
            || saved.LocalHistory?.IncarnationId != entry.Command.Binding.IncarnationId
            || entry.Outcome == CharacterCareerGearEquipmentOutcome.Applied && !saved.CanReplayReceipt(entry.RecordedRevision))
            return new(CharacterCareerGearEquipmentOutcome.Conflict);
        return new(entry.Outcome == CharacterCareerGearEquipmentOutcome.Applied
            ? CharacterCareerGearEquipmentOutcome.Recorded : entry.Outcome, entry, saved.ContentRevision);
    }

    public static bool TryBuild(WorkspaceStoredDocument original, CharacterCareerGearEquipmentCatalog current,
        CharacterCareerGearEquipmentCommand command, out WorkspaceDocument? replacement,
        out CharacterCareerGearEquipmentReceipt? receipt)
    {
        replacement = null; receipt = null;
        if (!IsConfirmedCommand(command) || !Same(current.Binding, command.Binding)
            || original.LocalHistory?.IncarnationId != current.Binding.IncarnationId
            || original.Id != current.Binding.WorkspaceId || original.ContentRevision != current.Binding.ContentRevision
            || original.SavedRevision != current.Binding.SavedRevision || SourceDigest(original.Document) != current.Binding.SourceDigest
            || original.Document.AuxiliaryStateDigest != current.Binding.AuxiliaryDigest || !IsValidHistory(original)
            || original.Document.AuxiliaryState.CharacterCareerGearEquipmentReceipts?.Any(
                r => r.Command.Request.OperationId == command.Request.OperationId || r.Outcome == CharacterCareerGearEquipmentOutcome.Pending) == true
            || !TryPlan(current, command.Request, out var plan)
            || !Same(plan! with { ExplicitlyConfirmed = true }, command)) return false;
        WorkspaceDocument proposed;
        if (command.Loadout is { } loadout)
            proposed = CharacterCareerLoadoutTransaction.Apply(original.Document, loadout);
        else
        {
            var document = CharacterGearEquipmentCapture.Parse(original.Document.Content);
            var node = CharacterGearEquipmentCapture.FindNode(document.Root!, command.Request.Identity);
            node.Elements("equipped").Single().Value = command.Request.Equipped ? "True" : "False";
            proposed = original.Document with { State = original.Document.State with { Payload = document.ToString(SaveOptions.DisableFormatting) } };
        }
        string payload = proposed.Content;
        receipt = Seal(command, CharacterCareerGearEquipmentOutcome.Applied, original.ContentRevision + 1, payload);
        var history = original.Document.AuxiliaryState.CharacterCareerGearEquipmentReceipts ?? [];
        var entries = history.Append(receipt).ToArray();
        if (!IsValidLedger(original.Id, receipt.RecordedRevision, entries)) return false;
        replacement = proposed with { State = proposed.State with {
            AuxiliaryState = proposed.AuxiliaryState with { CharacterCareerGearEquipmentReceipts = entries } } };
        return true;
    }
}

public static class CharacterGearEquipmentCapture
{
    public static IReadOnlyList<CharacterGearEquipmentState> ProjectValue(
        string xml,
        Guid rootGearId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(xml);
        if (rootGearId == Guid.Empty)
        {
            throw new InvalidOperationException(
                "Gear Equipped editing requires a stable root Gear Guid.");
        }

        XDocument document = Parse(xml);
        XElement root = document.Root is { Name.LocalName: "character" }
            ? document.Root
            : throw new InvalidOperationException("Workspace XML must use <character> as the root node.");
        bool created = ReadRequiredBoolean(root, "created", "creation/career state");
        XElement gears = ReadRequiredContainer(root, "gears");
        HashSet<Guid> loaded = LoadedGear(root);
        XElement selectedRoot = FindUniqueDirectByGuid(
            gears, "gear", rootGearId, "root Gear");

        var states = new List<CharacterGearEquipmentState>();
        var seenIds = new HashSet<Guid>();
        AddStateRecursive(
            selectedRoot,
            Array.Empty<Guid>(),
            parentDisplayPath: null,
            created,
            states,
            seenIds, loaded);
        return Array.AsReadOnly(states.ToArray());
    }

    public static XElement FindNode(
        XElement root,
        CharacterGearEquipmentIdentity identity)
    {
        if (!CharacterGearEquipmentRules.IsValidIdentity(identity))
        {
            throw new InvalidOperationException("The selected Gear hierarchy is invalid.");
        }

        XElement current = FindUniqueDirectByGuid(
            ReadRequiredContainer(root, "gears"),
            "gear",
            identity.GearPath[0],
            "root Gear");
        for (int index = 1; index < identity.GearPath.Count; index++)
        {
            current = FindUniqueDirectByGuid(
                ReadRequiredContainer(current, "children"),
                "gear",
                identity.GearPath[index],
                "nested Gear");
        }
        return current;
    }

    private static void AddStateRecursive(
        XElement gear,
        IReadOnlyList<Guid> parentPath,
        string? parentDisplayPath,
        bool created,
        ICollection<CharacterGearEquipmentState> states,
        ISet<Guid> seenIds, HashSet<Guid> loaded)
    {
        if (parentPath.Count >= 64 || states.Count >= 10000) throw new InvalidOperationException("Gear hierarchy is too large.");
        Guid gearId = ReadGuid(gear);
        if (!seenIds.Add(gearId))
        {
            throw new InvalidOperationException(
                "Gear Equipped requires unique stable Guid identity throughout the selected tree.");
        }

        Guid[] path = [.. parentPath, gearId];
        string name = DisplayName(gear);
        string displayPath = parentDisplayPath is null ? name : $"{parentDisplayPath} > {name}";
        string parentId = ReadOptionalSingleText(gear, "parentid");
        var identity = new CharacterGearEquipmentIdentity(Array.AsReadOnly(path));
        if (!CharacterGearEquipmentRules.TryCreateState(
                identity,
                created,
                includedInParent: parentId.Length != 0,
                loadedIntoClip: loaded.Contains(gearId),
                displayPath,
                ReadRequiredBoolean(gear, "equipped", "equipped state"),
                out CharacterGearEquipmentState state))
        {
            throw new InvalidOperationException(
                "Gear Equipped requires exact saved hierarchical identity and state.");
        }
        states.Add(state);

        XElement? children = ReadOptionalContainer(gear, "children");
        if (children is null)
        {
            return;
        }
        foreach (XElement child in children.Elements("gear"))
        {
            AddStateRecursive(child, path, displayPath, created, states, seenIds, loaded);
        }
    }

    private static bool ReadRequiredBoolean(XElement parent, string name, string kind)
    {
        XElement[] values = parent.Elements(name).Take(2).ToArray();
        if (values.Length != 1 || !bool.TryParse(values[0].Value.Trim(), out bool value))
        {
            throw new InvalidOperationException(
                $"Gear Equipped editing requires one exact saved {kind} Boolean.");
        }
        return value;
    }

    private static string ReadOptionalSingleText(XElement parent, string name)
    {
        XElement[] values = parent.Elements(name).Take(2).ToArray();
        return values.Length switch
        {
            0 => string.Empty,
            1 => values[0].Value.Trim(),
            _ => throw new InvalidOperationException(
                $"The selected Gear has duplicate <{name}> values.")
        };
    }

    private static Guid ReadGuid(XElement element)
    {
        XElement[] values = element.Elements("guid").Take(2).ToArray();
        if (values.Length != 1
            || !Guid.TryParseExact(values[0].Value.Trim(), "D", out Guid id)
            || id == Guid.Empty)
        {
            throw new InvalidOperationException("Gear requires one stable Guid.");
        }
        return id;
    }

    private static XElement FindUniqueDirectByGuid(
        XElement container,
        string elementName,
        Guid id,
        string kind)
    {
        XElement[] matches = container.Elements(elementName)
            .Where(candidate => Guid.TryParseExact(
                    candidate.Elements("guid").SingleOrDefault()?.Value.Trim(),
                    "D",
                    out Guid candidateId)
                && candidateId == id)
            .Take(2)
            .ToArray();
        return matches.Length == 1
            ? matches[0]
            : throw new InvalidOperationException(
                $"{kind} identity is missing or ambiguous in the saved hierarchy.");
    }

    private static XElement ReadRequiredContainer(XElement parent, string name)
        => ReadOptionalContainer(parent, name)
            ?? throw new InvalidOperationException($"The saved hierarchy is missing <{name}>.");

    private static XElement? ReadOptionalContainer(XElement parent, string name)
    {
        XElement[] values = parent.Elements(name).Take(2).ToArray();
        return values.Length switch
        {
            0 => null,
            1 => values[0],
            _ => throw new InvalidOperationException(
                $"The saved hierarchy has duplicate <{name}> collections.")
        };
    }

    private static string DisplayName(XElement element)
    {
        XElement[] names = element.Elements("name").Take(2).ToArray();
        return names.Length switch
        {
            0 => "Unnamed Gear",
            1 when names[0].Value.Length != 0 => names[0].Value,
            1 => "Unnamed Gear",
            _ => throw new InvalidOperationException("A Gear node has duplicate <name> values.")
        };
    }

    public static XDocument Parse(string xml)
    {
        if (xml.Length is 0 or > 67108864) throw new InvalidOperationException("Gear payload is unavailable.");
        using var text = new StringReader(xml);
        using var reader = XmlReader.Create(text, new XmlReaderSettings
        { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 67108864 });
        var document = XDocument.Load(reader, LoadOptions.PreserveWhitespace);
        if (document.Root?.Name != "character") throw new InvalidOperationException("Gear requires character XML.");
        return document;
    }

    public static IReadOnlyList<CharacterGearEquipmentState> ProjectAll(string xml)
    {
        var root = Parse(xml).Root!;
        bool created = ReadRequiredBoolean(root, "created", "creation/career state");
        if (!created) throw new InvalidOperationException("Before run requires Career.");
        var gears = ReadRequiredContainer(root, "gears");
        var states = new List<CharacterGearEquipmentState>();
        var seen = new HashSet<Guid>();
        var loaded = LoadedGear(root);
        foreach (var gear in gears.Elements("gear"))
            AddStateRecursive(gear, [], null, created, states, seen, loaded);
        return Array.AsReadOnly(states.ToArray());
    }

    private static HashSet<Guid> LoadedGear(XElement root)
    {
        var ids = new HashSet<Guid>();
        foreach (var weapon in root.Descendants("weapon"))
        {
            // Match the saved-loader branch: empty/zero ammo has no loaded Gear.
            string ammo = ReadOptionalSingleText(weapon, "ammo");
            if (string.IsNullOrWhiteSpace(ammo) || ammo == "0") continue;
            var clips = ReadOptionalContainer(weapon, "clips");
            if (clips is not null)
            {
                // Modern clips replace (not combine with) stale legacy slots. The
                // loader requires a count but does not require it to be positive.
                foreach (var clip in clips.Elements("clip"))
                    if (int.TryParse(ReadOptionalSingleText(clip, "count"), System.Globalization.NumberStyles.Integer,
                            System.Globalization.CultureInfo.InvariantCulture, out _)
                        && Guid.TryParse(ReadOptionalSingleText(clip, "id"), out var id) && id != Guid.Empty)
                        ids.Add(id);
                continue;
            }
            string require = ReadOptionalSingleText(weapon, "requireammo");
            if (bool.TryParse(require, out bool requireAmmo) && !requireAmmo) continue;
            // Weapon.s_OldClipValues contains only these four suffixes. Malformed
            // or empty legacy slots do not become loaded Gear in the pinned loader.
            foreach (string suffix in new[] { "", "2", "3", "4" })
                if (int.TryParse(ReadOptionalSingleText(weapon, "ammoremaining" + suffix), System.Globalization.NumberStyles.Integer,
                        System.Globalization.CultureInfo.InvariantCulture, out int count) && count > 0
                    && Guid.TryParse(ReadOptionalSingleText(weapon, "ammoloaded" + suffix), out var id) && id != Guid.Empty)
                    ids.Add(id);
        }
        return ids;
    }
}

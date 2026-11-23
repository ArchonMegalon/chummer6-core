using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

/// <summary>
/// Pure prerequisite for a typed sustained-spell transaction. No store, dispatch,
/// receipt, authorization or replay authority. The integration MUST hold its
/// existing owner-bound workspace lease and record the original operation with
/// the replacement atomically before this can become a native commit action.
/// </summary>
public static class CharacterSustainedSpellPreparationPlanner
{
    public static bool TryPreview(OwnerScope owner, WorkspaceStoredDocument saved,
        ICharacterSourceDataResolver sources, CharacterSustainedSpellPreparationRequest request,
        out CharacterSustainedSpellPreparationReview? review, CharacterCreationRuleSourceCapture? captured = null,
        CharacterCurrentRuleEnvironmentExecution? currentExecution = null)
        => TryBuild(owner, saved, sources, request, out review, out _, captured, currentExecution);

    // Producing this replacement is NOT committing it. In particular this method
    // cannot prove one commit or recover an unknown result. Do not expose it as an
    // arbitrary XML replacement endpoint or retry a store dispatch after timeout.
    public static bool TryBuildConfirmedReplacement(OwnerScope owner, WorkspaceStoredDocument current,
        ICharacterSourceDataResolver sources, CharacterSustainedSpellPreparationReview reviewed,
        bool explicitlyConfirmed, out WorkspaceDocument? replacement, CharacterCreationRuleSourceCapture? captured = null,
        CharacterCurrentRuleEnvironmentExecution? currentExecution = null)
    {
        replacement = null;
        if (!explicitlyConfirmed || reviewed is null
            || !TryBuild(owner, current, sources, reviewed.Request, out var fresh, out var candidate, captured, currentExecution)
            || !CharacterCareerGearEquipmentTransaction.Same(reviewed, fresh)) return false;
        replacement = candidate;
        return true;
    }

    private static bool TryBuild(OwnerScope owner, WorkspaceStoredDocument saved,
        ICharacterSourceDataResolver sources, CharacterSustainedSpellPreparationRequest request,
        out CharacterSustainedSpellPreparationReview? review, out WorkspaceDocument? replacement, CharacterCreationRuleSourceCapture? captured,
        CharacterCurrentRuleEnvironmentExecution? currentExecution)
    {
        review = null;
        replacement = null;
        if ((captured is null) != (currentExecution is null)
            || captured is not null && currentExecution!.CaptureDigest != captured.IntegrityDigest
            || saved is null || saved.Document is null || sources is null || request is null || request.SpellId == Guid.Empty
            || string.IsNullOrWhiteSpace(owner.NormalizedValue) || owner.NormalizedValue.Length > 512
            || owner.UsesLocalSingleUserValue && !owner.IsLocalSingleUser
            || !CharacterAfterRunSettlementServiceIntegrity.IsValidWorkspaceId(saved.Id)
            || saved.ContentRevision is <= 0 or long.MaxValue || saved.SavedRevision != saved.ContentRevision
            || saved.Document.Format != WorkspaceDocumentFormat.NativeXml || saved.Document.RulesetId != "sr5"
            || saved.Document.SchemaVersion != 1 || saved.Document.PayloadKind is not ("workspace" or "sr5/chum5-xml")
            || request.Force is < CharacterSustainedObjectRules.MinimumForce or > CharacterSustainedObjectRules.MaximumForce
            || request.NetHits is < CharacterSustainedObjectRules.MinimumNetHits or > CharacterSustainedObjectRules.MaximumNetHits)
            return false;
        try
        {
            using var scope = (sources as ICharacterSourceDataResolverOperationScopeFactory)?.CreateOperationScope();
            var resolver = scope ?? sources;
            string xml = saved.Document.Content;
            bool Environment(out string digest) => captured is null
                ? CharacterCareerGearEquipmentTransaction.TryEnvironment(resolver, xml, out digest)
                : CharacterCareerGearEquipmentTransaction.TryCapturedEnvironment(captured, resolver, xml, out digest);
            if (!Environment(out string environment)) return false;
            var grants = CharacterCreationPriorityGrantAuthority3563.Capture(saved, resolver);
            var before = CharacterSheetDerivedQuery.ParseWithPriorityGrants3563(xml, resolver, grants);
            if (before.UnavailableReason is not null || before.AuthorityDigest is null
                || before.SustainedSpellPreparation is not { } preparation || preparation.Objects.Count >= 1024)
                return false;
            var option = preparation.Options.SingleOrDefault(o => o.SpellId == request.SpellId);
            if (option is null) return false;
            using var input = new StringReader(xml);
            using var reader = XmlReader.Create(input, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null,
                MaxCharactersInDocument = 16 * 1024 * 1024
            });
            var document = XDocument.Load(reader, LoadOptions.PreserveWhitespace);
            var root = document.Root!;
            var container = root.Elements("sustainedobjects").SingleOrDefault();
            if (container is null) root.Add(container = new XElement("sustainedobjects"));
            var identity = new CharacterSustainedObjectIdentity("Spell", request.SpellId,
                preparation.Objects.Count(o => o.Identity.LinkedObjectId == request.SpellId));
            string linkedIdentity = root.Element("spells")!.Elements("spell")
                .Single(s => Guid.TryParse(s.Element("guid")?.Value, out var id) && id == request.SpellId)
                .Element("guid")!.Value;
            container.Add(new XElement("sustainedobject",
                new XElement("linkedobject", linkedIdentity),
                new XElement("linkedobjecttype", "Spell"), new XElement("force", request.Force),
                new XElement("nethits", request.NetHits), new XElement("self", true)));
            string proposed = document.ToString(SaveOptions.DisableFormatting);
            // A retained resolver admits only its exact original character XML. Observe
            // the proposal from the same private source closure with the admitted
            // semantics; never relax that resolver's original-character fence.
            var afterResolver = resolver;
            if (captured is not null)
            {
                var afterCapture = captured.RecaptureCurrentEnvironment(proposed,
                    currentExecution!.ExecutionProfile, currentExecution.ExecutionProfileFingerprint);
                if (afterCapture is null || !afterCapture.MatchesOriginalCharacter(proposed)
                    || afterCapture.EffectivePolicyDigest != captured.EffectivePolicyDigest) return false;
                var afterResolution = afterCapture.CreateCurrentEnvironmentResolution(
                    currentExecution.ExecutionProfile, currentExecution.ExecutionProfileFingerprint);
                if (afterResolution is null) return false;
                afterResolver = afterResolution.Resolver;
            }
            var after = CharacterSheetDerivedQuery.ParseWithPriorityGrants3563(proposed, afterResolver, grants);
            if (after.UnavailableReason is not null || after.AuthorityDigest is null
                || after.SustainedSpellPreparation is not { } next
                || next.Objects.Count != preparation.Objects.Count + 1
                || !next.Objects.Take(preparation.Objects.Count).SequenceEqual(preparation.Objects)
                || next.Objects.Last() != new CharacterSustainedObjectState(identity, option.DisplayName,
                    request.Force, request.NetHits, true, true)
                || !Environment(out string finalEnvironment)
                || finalEnvironment != environment) return false;
            var binding = new CharacterSustainedSpellPreparationBinding(owner.NormalizedValue, owner.IsLocalSingleUser,
                saved.Id, saved.ContentRevision, saved.SavedRevision,
                CharacterCareerGearEquipmentTransaction.Hash(JsonSerializer.Serialize(new
                {
                    saved.Document.Format, saved.Document.RulesetId, saved.Document.SchemaVersion,
                    saved.Document.PayloadKind, PayloadDigest = CharacterCareerGearEquipmentTransaction.Hash(xml)
                })), saved.Document.AuxiliaryStateDigest, environment);
            review = new(binding, request, identity, option.DisplayName, preparation.DicePoolPenalty,
                next.DicePoolPenalty, before.AuthorityDigest, after.AuthorityDigest);
            // Preserve the entire existing envelope and auxiliary object, including
            // unknown extension data. Only one native sustained occurrence is added.
            replacement = saved.Document with { State = saved.Document.State with { Payload = proposed } };
            return true;
        }
        catch (Exception error) when (error is InvalidDataException or XmlException or InvalidOperationException
            or ArgumentException or FormatException or OverflowException or IOException or UnauthorizedAccessException)
        {
            review = null;
            replacement = null;
            return false;
        }
    }
}

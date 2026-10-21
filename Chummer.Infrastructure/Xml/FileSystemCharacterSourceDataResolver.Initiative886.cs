using System.Globalization;
using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;

namespace Chummer.Infrastructure.Xml;

public sealed partial class FileSystemCharacterSourceDataResolver
{
    private sealed partial class SourceDataContext
    {
        public bool TryResolveCreationPhysicalInitiative886(CharacterCreationPrerequisiteDraft draft,
            out CharacterCreationPhysicalInitiativeAuthority? result)
        {
            result = null;
            using var read = _sourceInputs.Enter();
            if (_sourceInputs.CreationSourceExecutionSchema != CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV4
                || draft.HeritageSelection is not { MetavariantSourceId: null } heritage
                || !TryResolveCreationPrerequisiteAuthority(out var prerequisite)
                || !CharacterCreationPrerequisiteService.TryExecuteSourceSelection718(prerequisite, draft, out _)
                || !TryResolveCreationMetatypeCatalog(out var catalog) || !catalog.IsAuthoritative
                || !TryHasSelectedCustomDataInputFor(_customDirectories, SettingsFile, out bool custom) || custom
                || !NoOverlayForSource(SettingsFile)
                || !TryResolveTarget(SettingsFile, ["settings"], "setting", _settingsProfileId, string.Empty, out var profile)
                || profile is null || !CreationScalar886(profile, "maxinitiativedice", out int maximum)
                || maximum < 0 || !CreationScalar886(profile, "mininitiativedice", out int minimum)
                || !TryResolveTarget("metatypes.xml", ["metatypes"], "metatype", heritage.MetatypeSourceId,
                    string.Empty, out var node) || node is null || RawSource718(node) != heritage.MetatypeSourceNodeDigest
                || node.DescendantNodes().OfType<XProcessingInstruction>().Any()
                || catalog.Options.Where(o => o.OptionId == heritage.MetatypeSourceId).ToArray() is not [var option]
                || !option.IsEnabled || option.Blockers.Count != 0 || option.Initiative.MinimumDiceFallback != minimum
                || catalog.SourceContext.SettingsProfileId != draft.SettingsProfileId
                || !TryResolveCreationEffectivePolicy(out var inventory) || inventory is null
                || inventory.Sources.Where(s => s.Kind == CharacterCreationPolicySourceKind.Rules && s.SourceId == "metatypes").ToArray() is not [var metatypeSource]
                || _sourceInputs.HasSourceDrift) return false;
            // Weight/armor encumbrance is a separate consumer; never silently substitute zero.
            var encumbrance = profile.Elements("doencumbrancepenaltywoundmodifier").ToArray();
            if (encumbrance.Length != 0 && (encumbrance is not [var flag] || flag.HasAttributes || flag.HasElements
                || !bool.TryParse(flag.Value, out bool enabled) || enabled)) return false;
            result = new("sr5", CharacterCreationPhysicalInitiativeAuthority.SemanticsV1, _settingsProfileId,
                _rawProfileInputsDigest, draft.DraftDigest, heritage.MetatypeSourceId, heritage.MetatypeSourceNodeDigest,
                $"source:{metatypeSource.Kind}:{metatypeSource.Order}", metatypeSource.ContentDigest!, option.Initiative, maximum);
            return true;
        }

        private static bool CreationScalar886(XElement parent, string field, out int value)
        {
            value = 0;
            return parent.Elements(field).ToArray() is [var node] && !node.HasAttributes && !node.HasElements
                && !node.Nodes().OfType<XProcessingInstruction>().Any()
                && int.TryParse(node.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }
    }
}

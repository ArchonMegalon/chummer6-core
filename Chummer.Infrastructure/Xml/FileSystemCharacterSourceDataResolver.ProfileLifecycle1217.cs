using System.Globalization;
using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using D1217 = Chummer.Application.Characters.CharacterCreationSourceOperationDisposition;

namespace Chummer.Infrastructure.Xml;

public sealed partial class FileSystemCharacterSourceDataResolver
{
    private sealed partial class SourceDataContext
    {
        private void ExecuteProfileLifecycle1217(XElement profile, CharacterCreationPrerequisiteAuthority authority,
            CharacterCreationPrerequisiteDraft draft, CharacterCreationRuleSourceScopeInvocation invocation,
            SourceScopeLedger ledger)
        {
            if (!invocation.HasLifecycleResultV1
                || _sourceInputs.CreationSourceExecutionSchema != CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV4
                || draft.BuildMethod is not (CharacterCreationBuildMethods.Priority or CharacterCreationBuildMethods.SumToTen)
                || _character.Name != "character" || PlainLeaf1217(_character, "gameedition")?.Value != "SR5"
                || !Boolean1217(_character, "created", out bool career) || career
                || authority.SettingsProfileId != draft.SettingsProfileId
                || authority.RawProfileInputsDigest != _rawProfileInputsDigest
                || invocation.CreationDraft718 is null || invocation.WorkspaceId718 is null
                || _sourceInputs.HasSourceDrift) return;

            if (Integer1217(profile, "minastralinitiativedice", out int minimum)
                && Integer1217(profile, "maxastralinitiativedice", out int maximum)
                && invocation.TryGetExecutedAstralInitiative1249(authority.SettingsProfileId,
                    authority.RawProfileInputsDigest,
                    CharacterCreationFinalizationDigest.Compute(profile.ToString(SaveOptions.DisableFormatting)), out var native)
                && native!.Minimum == minimum && native.Maximum == maximum
                && native.Dice == Math.Min(minimum, maximum))
            {
                // Pinned legacy Character.AstralInitiativeDice (Character.cs:29333).
                // This is also evaluated for a mundane candidate: the canonical
                // property has no talent/lifecycle/empty-inventory early return.
                int dice = Math.Min(minimum, maximum);
                foreach (string field in new[] { "minastralinitiativedice", "maxastralinitiativedice" })
                    ledger.Tree(PlainLeaf1217(profile, field)!, D1217.Compiled, new
                    { Operation = "AstralInitiativeDice", Minimum = minimum, Maximum = maximum, Dice = dice,
                        NativeResult = native, draft.DraftDigest, authority.RawProfileInputsDigest });
            }
            if (Boolean1217(profile, "autobackstory", out bool automatic))
                ledger.Tree(PlainLeaf1217(profile, "autobackstory")!, D1217.Compiled, new
                { Operation = "EnableAutomaticStoryButton", Input = automatic, draft.BuildMethod,
                    Enabled = draft.BuildMethod == CharacterCreationBuildMethods.LifeModules && automatic });

            foreach (string field in new[] { "dontdoublequalities", "dontdoublequalityrefunds", "mysaddppcareer" })
                if (Boolean1217(profile, field, out bool value))
                    ledger.Tree(PlainLeaf1217(profile, field)!, D1217.NotSelected, new
                    { Operation = "creation-excludes-career-command", Field = field, Input = value,
                        Created = career, draft.BuildMethod, draft.DraftDigest, invocation.RequestDigest });
            if (PlainLeaf1217(profile, "nuyenperbpwftp") is { } exchange
                && decimal.TryParse(exchange.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal rate)
                && rate > 0)
                ledger.Tree(exchange, D1217.NotSelected, new { Operation = "creation-excludes-career-exchange",
                    Rate = rate, Created = career, draft.BuildMethod, draft.DraftDigest, invocation.RequestDigest });

            // Retain the real native settings for the creation/Career handoff.
            // Neither a license prompt nor weapon damage is executed by this review.
            if (invocation.TryGetExecutedNativeSettings1420(authority.SettingsProfileId,
                authority.RawProfileInputsDigest, CharacterCreationFinalizationDigest.Compute(
                    profile.ToString(SaveOptions.DisableFormatting)), out var nativeSettings))
            {
                foreach (var definition in CharacterCreationSettingDefinitions.NativeLifecycle1420)
                {
                    bool? retained = definition.Key == CharacterCreationObservedSetting.LicenseRestricted
                        ? nativeSettings!.LicenseRestricted : nativeSettings!.MoreLethalGameplay;
                    if (retained is bool expected && Boolean1217(profile, definition.Element, out bool current)
                        && current == expected)
                        ledger.Tree(PlainLeaf1217(profile, definition.Element)!, D1217.Compiled, nativeSettings);
                }
            }

            ExecuteNativeMagic1430(profile, authority, invocation, ledger);
            ExecuteMatrixLifecycle(profile, authority, invocation, ledger);
            ExecuteNativeContactSpecialty(profile, authority, invocation, ledger);
            ExecuteNativeLoadout1432(profile, authority, invocation, ledger);
            ExecuteProfileMetatypePolicy1217(profile, authority, draft, ledger);
            ExecuteProfileSkillPolicies1217(profile, authority, invocation, ledger);
        }

        private void ExecuteProfileMetatypePolicy1217(XElement profile, CharacterCreationPrerequisiteAuthority authority,
            CharacterCreationPrerequisiteDraft draft, SourceScopeLedger ledger)
        {
            if (!Boolean1217(profile, "metatypecostskarma", out bool enabled) || !enabled
                || !Integer1217(profile, "metatypecostskarmamultiplier", out int multiplier)
                || draft.HeritageSelection is not { MetavariantSourceId: null } heritage
                || !TryResolveCreationMetatypeCatalog(out var catalog) || !catalog.IsAuthoritative
                || catalog.SourceContext.SettingsProfileId != authority.SettingsProfileId
                || catalog.SourceContext.RawProfileInputsDigest != authority.RawProfileInputsDigest
                || catalog.SourceContext.MetatypeKarmaMultiplier != multiplier
                || catalog.Options.Where(option => option.OptionId == heritage.MetatypeSourceId).ToArray() is not [var selected]
                || !selected.IsEnabled || selected.Blockers.Count != 0 || _sourceInputs.HasSourceDrift) return;
            // ResolveMetatypeProfileAuthority validates the actual Karma-mode
            // gate; CharacterCreationMetatypeCatalogProjector.TryProjectOption computes
            // the selected source's checked BaseKarma * multiplier. Preserve the
            // Priority heritage budget separately: it is not this catalogue cost.
            foreach (string field in new[] { "metatypecostskarma", "metatypecostskarmamultiplier" })
                ledger.Tree(PlainLeaf1217(profile, field)!, D1217.Compiled, new
                { Operation = "canonical-selected-metatype-catalogue-cost", Enabled = enabled, Multiplier = multiplier,
                    selected.OptionId, selected.BaseKarma, selected.KarmaCost, catalog.SourceContext, draft.DraftDigest });
        }

        private void ExecuteProfileSkillPolicies1217(XElement profile, CharacterCreationPrerequisiteAuthority authority,
            CharacterCreationRuleSourceScopeInvocation invocation, SourceScopeLedger ledger)
            => ExecuteProfileRegrouping3207(profile, authority, invocation, ledger);

        private static XElement? PlainLeaf1217(XElement parent, string field)
        {
            var nodes = parent.Elements().Where(node => node.Name.LocalName == field).ToArray();
            return nodes is [var leaf] && leaf.Name == field && !leaf.HasAttributes
                && leaf.Nodes().ToArray() is [XText text] && text.GetType() == typeof(XText) ? leaf : null;
        }
        private static bool Boolean1217(XElement parent, string field, out bool value)
        {
            value = false;
            return PlainLeaf1217(parent, field) is { } leaf && bool.TryParse(leaf.Value, out value);
        }
        private static bool Integer1217(XElement parent, string field, out int value)
        {
            value = 0;
            return PlainLeaf1217(parent, field) is { } leaf
                && int.TryParse(leaf.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) && value >= 0;
        }
    }
}

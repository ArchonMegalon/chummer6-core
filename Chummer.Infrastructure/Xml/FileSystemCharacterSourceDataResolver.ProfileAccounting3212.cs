using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using D = Chummer.Application.Characters.CharacterCreationSourceOperationDisposition;

namespace Chummer.Infrastructure.Xml;

public sealed partial class FileSystemCharacterSourceDataResolver
{
    private sealed partial class SourceDataContext
    {
        private sealed record ProfileAmendmentSelection3212(CharacterCreationRuleSourceScopeInvocation Invocation,
            string InvocationDigest, string ProfileDigest, string Execution);
        private ProfileAmendmentSelection3212? profileAmendmentSelection3212;

        // Called only by retained RuleSourceAccounting after its source/policy checks.
        // Reuse the real amendment resolver, including the global overlay catalogue,
        // selected directories and original character; empty XML is not evidence.
        internal void BindProfileAmendmentSelection3212(RetainedCharacterRuleSourceInputs retained,
            string capture, CharacterCreationRuleSourceScopeInvocation invocation)
        {
            profileAmendmentSelection3212 = null;
            using var read = _sourceInputs.Enter();
            if (capture != invocation.CaptureDigest
                || !TryResolveTarget(SettingsFile, ["settings"], "setting", _settingsProfileId, string.Empty, out var profile)
                || profile is null || !TryResolveCreationPrerequisiteAuthority(out var authority)
                || !invocation.HasCurrentProfileInvocation3212(authority.SettingsProfileId, authority.RawProfileInputsDigest,
                    CharacterCreationFinalizationDigest.Compute(profile.ToString(SaveOptions.DisableFormatting)), _rawCharacterXmlDigest)
                || ExecuteAmendmentAbsence954(retained, capture) is not { } execution
                || _sourceInputs.HasSourceDrift) return;
            profileAmendmentSelection3212 = new(invocation, ProfileInvocationDigest3212(invocation),
                CharacterCreationFinalizationDigest.Compute(profile.ToString(SaveOptions.DisableFormatting)), execution);
        }

        private static string ProfileInvocationDigest3212(CharacterCreationRuleSourceScopeInvocation invocation)
            => CharacterCreationFinalizationDigest.Compute(new
            {
                invocation.PolicyDigest, invocation.CaptureDigest, invocation.RequestDigest, invocation.GraphDigest,
                invocation.CompiledEffects, invocation.CreationDraft718, invocation.WorkspaceId718,
                invocation.Attributes718, invocation.AttributePolicy718, invocation.Skills718,
                invocation.ResourcesExecution718,
                Consumer = invocation.TryGetCompletedConsumer("profile", out var consumer) ? consumer : null
            });

        private void ExecuteProfileSecondMaximum3212(XElement profile,
            CharacterCreationPrerequisiteAuthority authority, CharacterCreationRuleSourceScopeInvocation invocation,
            SourceScopeLedger ledger)
        {
            // ResolveMaximumAttributesAtCap is shared with the live reader. Its
            // Boolean fallback becomes a count, while an explicit count wins.
            var legacyNodes = profile.Elements().Where(node => node.Name.LocalName == "allow2ndmaxattribute").ToArray();
            var primaryNodes = profile.Elements().Where(node => node.Name.LocalName == "maxnumbermaxattributescreate").ToArray();
            if (legacyNodes is not [var legacy] || legacy.Name != "allow2ndmaxattribute"
                || legacy.HasAttributes || legacy.Nodes().ToArray() is not [XText text]
                || text.GetType() != typeof(XText) || legacy.Value != legacy.Value.Trim()
                || !bool.TryParse(legacy.Value, out bool allowSecond)
                || primaryNodes.Length > 1 || primaryNodes.Any(node => node.Name != "maxnumbermaxattributescreate"
                    || node.HasAttributes || node.Nodes().ToArray() is not [XText value]
                    || value.GetType() != typeof(XText))
                || CharacterCreationAttributePolicyAuthority.ResolveMaximumAttributesAtCap(profile) is not int maximum
                || maximum != authority.MaxNumberMaxAttributesCreate
                || invocation.AttributePolicy718?.MaxNumberMaxAttributesCreate != maximum
                || !invocation.TryGetExecutedProfileSecondMaximum3212(authority.SettingsProfileId, authority.RawProfileInputsDigest,
                    CharacterCreationFinalizationDigest.Compute(profile.ToString(SaveOptions.DisableFormatting)), _rawCharacterXmlDigest,
                    CharacterCreationFinalizationDigest.Compute(legacy.ToString(SaveOptions.DisableFormatting)),
                    out bool executedBoolean, out int executedMaximum, out bool executedShadowed, out var evaluated, out var execution)
                || evaluated is null || evaluated.Attributes.Count == 0
                || executedBoolean != allowSecond || executedMaximum != maximum || executedShadowed != (primaryNodes.Length == 1)
                || _sourceInputs.HasSourceDrift) return;
            bool shadowed = primaryNodes.Length == 1;
            if (!shadowed && maximum != (allowSecond ? 2 : 1)) return;
            ledger.Tree(legacy, shadowed ? D.NotSelected : D.Compiled, new
            {
                Operation = "EvaluateAllocations-maximum-attribute-count-legacy3212",
                Input = allowSecond, ResolvedMaximum = maximum, ExplicitMaximumSelected = shadowed,
                Result = evaluated, Execution = execution
            });
        }

        private void ExecuteProfileContainers3212(XElement profile, CharacterCreationPrerequisiteAuthority prerequisite,
            CharacterCreationRuleSourceScopeInvocation invocation, SourceScopeLedger ledger)
        {
            if (_sourceInputs.HasSourceDrift) return;
            string profileDigest = CharacterCreationFinalizationDigest.Compute(profile.ToString(SaveOptions.DisableFormatting));
            var directories = profile.Elements().Where(node => node.Name.LocalName == "customdatadirectorynames").ToArray();
            if (directories is [var directory] && EmptyAmendmentSelection954(profile)
                && profileAmendmentSelection3212 is { } absence && ReferenceEquals(absence.Invocation, invocation)
                && absence.InvocationDigest == ProfileInvocationDigest3212(invocation)
                && absence.ProfileDigest == profileDigest)
                ledger.Node(directory, D.Compiled, new
                {
                    Operation = "resolved-global-and-selected-amendment-absence954",
                    Selection = directory.ToString(SaveOptions.DisableFormatting), Result = absence.Execution
                });

            // The existing loadout private receipt proves each child and both ware
            // scopes. Mark only the exact parent, preserving original child receipts.
            if (!TryResolveCreationLoadoutAuthority(out var authority)
                || !CharacterCreationLoadoutRules.IsValidAuthority(authority)
                || !TryResolveCreationLoadoutLegalityPolicy(out var legality) || legality is null
                || authority.SettingsProfileId != prerequisite.SettingsProfileId
                || authority.ProfileDigest != prerequisite.RawProfileInputsDigest || _sourceInputs.HasSourceDrift) return;
            foreach (var container in profile.Elements().Where(node => node.Name == "bannedwaregrades" || node.Name == "redlinerexclusion"))
            {
                string digest = CharacterCreationFinalizationDigest.Compute(container.ToString(SaveOptions.DisableFormatting));
                if (invocation.TryGetExecutedProfileLoadout1215(authority, legality, _rawCharacterXmlDigest,
                    profileDigest, ProfileRelativePath1215(profile, container), digest, out var result))
                    ledger.Node(container, D.Compiled, new { Operation = "selected-profile-loadout-container3212", Result = result });
            }
        }
    }
}

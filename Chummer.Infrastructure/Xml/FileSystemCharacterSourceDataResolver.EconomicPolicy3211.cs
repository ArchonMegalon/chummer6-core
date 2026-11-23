using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using S3211 = Chummer.Contracts.Characters.CharacterCreationObservedSetting;
using C3211 = Chummer.Contracts.Characters.CharacterCreationSettingConsumer;
using D3211 = Chummer.Application.Characters.CharacterCreationSourceOperationDisposition;

namespace Chummer.Infrastructure.Xml;

public sealed partial class FileSystemCharacterSourceDataResolver
{
    private sealed partial class SourceDataContext
    {
        private void ExecuteEconomicPolicy3211(XElement profile, CharacterCreationPrerequisiteAuthority prerequisite,
            CharacterCreationRuleSourceScopeInvocation invocation, SourceScopeLedger ledger)
        {
            string profileDigest = CharacterCreationFinalizationDigest.Compute(profile.ToString(SaveOptions.DisableFormatting));
            foreach (var (key, field, container) in new (S3211, string, string?)[]
            {
                (S3211.FreeGrids, "allowfreegrids", null),
                (S3211.GroupContactKarma, "karmacontact", "karmacost"),
                (S3211.MaximumAvailability, "availability", null)
            })
            {
                XElement parent = profile;
                if (container is not null)
                {
                    var containers = profile.Elements().Where(row => row.Name.LocalName == container).ToArray();
                    if (containers is not [var selected] || selected.Name != container || selected.HasAttributes) continue;
                    parent = selected;
                }
                var leaves = parent.Elements().Where(row => row.Name.LocalName == field).ToArray();
                if (profile.HasAttributes || leaves is not [var leaf] || leaf.Name != field || leaf.HasAttributes
                    || leaf.Nodes().ToArray() is not [XText text] || text.GetType() != typeof(XText)) continue;
                string leafDigest = CharacterCreationFinalizationDigest.Compute(leaf.ToString(SaveOptions.DisableFormatting));
                var proofs = new List<string>();
                var dispositions = new List<bool>();
                // These resolver calls use the retained original source receipt.
                // Each setting/consumer needs its own authority even when values coincide.
                if (key == S3211.FreeGrids && TryResolveCreationLifestylesAuthority(out var lifestyles))
                    Add(C3211.Lifestyles, lifestyles);
                if (key == S3211.GroupContactKarma && TryResolveCreationContactsPolicy(out var contacts) && contacts is not null)
                    Add(C3211.Contacts, contacts);
                if (key == S3211.MaximumAvailability)
                {
                    if (TryResolveCreationGearAuthority(out var gear)) Add(C3211.Gear, gear);
                    if (TryResolveCreationLoadoutAuthority(out var loadout)) Add(C3211.Loadout, loadout);
                }
                if (_sourceInputs.HasSourceDrift || proofs.Count == 0 || dispositions.Distinct().Count() != 1) continue;
                ledger.Tree(leaf, dispositions[0] ? D3211.NotSelected : D3211.Compiled,
                    new { Operation = "canonical-economic-profile3211", Proofs = proofs.ToArray() });

                void Add(C3211 consumer, object source)
                {
                    if (!invocation.TryGetExecutedEconomicSetting3211(key, consumer, prerequisite.SettingsProfileId,
                        prerequisite.RawProfileInputsDigest, profileDigest, leafDigest, source, out var result, out bool inactive)
                        || result is null) return;
                    proofs.Add(result); dispositions.Add(inactive);
                }
            }
        }
    }
}

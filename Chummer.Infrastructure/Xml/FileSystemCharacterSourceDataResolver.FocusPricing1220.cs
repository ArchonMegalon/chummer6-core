using System.Text.Json;
using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using D = Chummer.Application.Characters.CharacterCreationSourceOperationDisposition;

namespace Chummer.Infrastructure.Xml;

public sealed partial class FileSystemCharacterSourceDataResolver
{
    private sealed partial class SourceDataContext
    {
        private void ExecuteProfileFocus1220(XElement profile, CharacterCreationPrerequisiteAuthority authority,
            CharacterCreationRuleSourceScopeInvocation invocation, SourceScopeLedger ledger)
        {
            if (profile.Descendants().Where(node => node.Name.LocalName == "karmacost").ToArray() is not [var costs]
                || costs.Parent != profile || costs.Name != "karmacost" || costs.HasAttributes) return;
            string profileDigest = CharacterCreationFinalizationDigest.Compute(profile.ToString(SaveOptions.DisableFormatting));
            string? admittedResult = null, admission = null;
            foreach (var leaf in costs.Elements())
            {
                if (leaf.Name.NamespaceName.Length != 0 || leaf.HasAttributes
                    || leaf.Nodes().ToArray() is not [XText text] || text.GetType() != typeof(XText)
                    || !invocation.TryGetExecutedFocusPricing1220(_settingsProfileId, authority.RawProfileInputsDigest,
                        profileDigest, leaf.Name.LocalName, CharacterCreationFinalizationDigest.Compute(leaf.ToString(SaveOptions.DisableFormatting)),
                        out var result, out var execution) || _sourceInputs.HasSourceDrift) continue;
                // Result comes from the invocation's private compiled-graph binding.
                // It is never accepted as a quote or an alternate report input.
                if (admittedResult != result)
                {
                    if (!TryAdmitFocusSources1239(invocation, result!, out admission)) return;
                    admittedResult = result;
                }
                ledger.Tree(leaf, D.Compiled, new { Operation = "selected-profile-focus-binding-karma",
                    Field = leaf.Name.LocalName, Result = result, Execution = execution, Admission = admission });
            }
        }

        private bool TryAdmitFocusSources1239(CharacterCreationRuleSourceScopeInvocation invocation,
            string result, out string? admission)
        {
            admission = null;
            try
            {
                using var parsed = JsonDocument.Parse(result);
                var calculation = parsed.RootElement.GetProperty("Calculation");
                var inventory = calculation.GetProperty("Inventory");
                if (inventory.GetProperty("GraphDigest").GetString() != invocation.GraphDigest
                    || calculation.GetProperty("GraphDigest").GetString() != invocation.GraphDigest
                    || inventory.GetProperty("Containers").GetArrayLength() != 7
                    || calculation.GetProperty("Applicability").GetArrayLength() != 16
                    || !CharacterCreationFinalizationDigest.IsCanonical(inventory.GetProperty("Admission1253").GetString())) return false;
                // The current compiler trace has no canonical focus-bonus source
                // receipt. Retain its arithmetic, but do not certify an applied
                // saved improvement merely from source-name/value diagnostics.
                // Individualized source-grade adjustments need no such receipt.
                if (calculation.GetProperty("Charges").EnumerateArray().Any(charge =>
                    charge.GetProperty("Components").EnumerateArray().Any(component =>
                        component.GetProperty("AppliedModifiers").GetArrayLength() != 0))) return false;
                var identities = new HashSet<string>(StringComparer.Ordinal);
                var paths = new HashSet<string>(StringComparer.Ordinal);
                var sources = new List<object>();
                // Validate every source, including unbonded/non-focus items and stack
                // components. A forged name cannot make a canonical focus irrelevant.
                foreach (var item in inventory.GetProperty("Nodes").EnumerateArray())
                {
                    string kind = item.GetProperty("Kind").GetString()!;
                    string id = item.GetProperty("Id").GetString()!;
                    string sourceId = item.GetProperty("SourceId").GetString()!;
                    string path = item.GetProperty("Path").GetString()!;
                    if (!identities.Add(id) || !paths.Add(path) || identities.Count > 100000
                        || !Guid.TryParseExact(id, "D", out var instance) || instance == Guid.Empty
                        || !Guid.TryParseExact(sourceId, "D", out var sourceGuid)) return false;
                    if (item.GetProperty("DerivedStackOwner1253").GetBoolean())
                    {
                        var stack = calculation.GetProperty("Charges").EnumerateArray().Where(c => c.GetProperty("Stacked").GetBoolean()
                            && c.GetProperty("OwnerGearId").GetString() == id).ToArray();
                        if (kind != "gear" || sourceGuid != Guid.Empty || stack.Length != 1
                            || stack[0].GetProperty("Components").GetArrayLength() < 2) return false;
                        sources.Add(new { Path = path, Id = id, Operation = "native-derived-stack-owner1253",
                            Components = stack[0].GetProperty("Components").Clone(),
                            GraphNode = item.GetProperty("XmlDigest").GetString() });
                        continue;
                    }
                    if (sourceGuid == Guid.Empty) return false;
                    (string File, string Container, string Entry)[] targets = kind switch
                    {
                        "gear" => [("gear.xml", "gears", "gear")],
                        "armor" => [("armor.xml", "armors", "armor")],
                        "weapon" => [("weapons.xml", "weapons", "weapon")],
                        "cyberware" => [("cyberware.xml", "cyberwares", "cyberware"), ("bioware.xml", "biowares", "bioware")],
                        "vehicle" => [("vehicles.xml", "vehicles", "vehicle")],
                        _ => []
                    };
                    var admitted = new List<object>();
                    foreach (var target in targets)
                    {
                        if (!TryHasSelectedCustomDataInputFor(_customDirectories, target.File, out bool custom) || custom
                            || !NoOverlayForSource(target.File)) return false;
                        if (!TryResolveTarget(target.File, [target.Container], target.Entry, sourceId, string.Empty, out var source)
                            || source is null) continue;
                        if (source.Name != target.Entry || source.HasAttributes
                            || source.Elements("name").ToArray() is not [var name]
                            || name.HasAttributes || name.HasElements
                            || FocusSourceName1239(name.Value) != FocusSourceName1239(item.GetProperty("Name").GetString()!)
                            || !SourceGate800(target.File, source).Eligible) return false;
                        var gate = SourceGate800(target.File, source);
                        admitted.Add(new { Path = path, Id = id, SourceId = sourceId, Kind = kind, File = target.File,
                            Source = CharacterCreationFinalizationDigest.Compute(source.ToString(SaveOptions.DisableFormatting)),
                            GraphNode = item.GetProperty("XmlDigest").GetString(), SourceBook = gate.Book,
                            SourceRule = gate.Rule, Eligible = true });
                    }
                    if (admitted.Count != 1) return false;
                    sources.Add(admitted[0]);
                }
                if (_sourceInputs.HasSourceDrift) return false;
                admission = CharacterCreationFinalizationDigest.Compute(new
                {
                    Operation = "complete-owned-focus-inventory-source-admission1239", invocation.PolicyDigest,
                    invocation.CaptureDigest, invocation.RequestDigest, invocation.GraphDigest,
                    Inventory = inventory, Sources = sources
                });
                return true;
            }
            catch (Exception error) when (error is JsonException or InvalidOperationException or ArgumentException
                or KeyNotFoundException or IOException) { return false; }
        }

        private static string FocusSourceName1239(string name)
        {
            int suffix = name.IndexOf(", Individualized,", StringComparison.Ordinal);
            if (suffix >= 0) name = name[..suffix];
            // Focus specializations are selected by the typed compiler, not a new
            // catalogue item. Their original name/grade/target stays in GraphDigest.
            if (name.Contains("Focus", StringComparison.Ordinal))
            {
                int choice = name.IndexOf(" (", StringComparison.Ordinal);
                if (choice >= 0) name = name[..choice];
            }
            return name;
        }
    }
}

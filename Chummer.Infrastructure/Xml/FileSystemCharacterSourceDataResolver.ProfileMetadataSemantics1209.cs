using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;

namespace Chummer.Infrastructure.Xml;

public sealed partial class FileSystemCharacterSourceDataResolver
{
    private sealed record MetadataExecution1209(string ProfileId, string ProfileDigest, string ProfileXmlDigest,
        string Field, string LeafDigest, string GraphDigest, string RequestDigest, string Result, string Digest);

    private sealed partial class RuleSourceAccounting
    {
        private readonly ConditionalWeakTable<CharacterCreationRuleSourceScopeInvocation,
            Dictionary<string, MetadataExecution1209>> metadataExecutions1209 = new();

        // Called at the end of the actual retained source traversal. No public
        // diagnostic/report, including 1202's Structural result, registers here.
        private void RememberMetadata1209(SourceDataContext context, CharacterCreationRuleSourceScopeInvocation invocation)
        {
            metadataExecutions1209.Remove(invocation);
            if (context.SnapshotMetadata1209() is not { } completed) return;
            metadataExecutions1209.Add(invocation, completed);
        }

        public override bool TryGetExecutedProfileMetadata1209(CharacterCreationRuleSourceScopeInvocation invocation,
            string field, out string? digest)
        {
            digest = null;
            if (field is not ("gameplayoptionname" or "nuyenformat")
                || invocation.CaptureDigest != captureDigest
                || !metadataExecutions1209.TryGetValue(invocation, out var rows)
                || !rows.TryGetValue(field, out var result)
                || !invocation.TryGetProfileMetadataCandidate1209(result.ProfileId, result.ProfileDigest,
                    result.ProfileXmlDigest, out var graph, out var request)
                || result.GraphDigest != CharacterCreationFinalizationDigest.Compute(graph!)
                || result.RequestDigest != CharacterCreationFinalizationDigest.Compute(request)) return false;
            digest = result.Digest;
            return true;
        }
    }

    private sealed partial class SourceDataContext
    {
        private readonly Dictionary<string, MetadataExecution1209> metadataResults1209 = new(StringComparer.Ordinal);

        internal Dictionary<string, MetadataExecution1209>? SnapshotMetadata1209()
            => _sourceInputs.HasSourceDrift ? null
                : metadataResults1209.ToDictionary(row => row.Key, row => row.Value, StringComparer.Ordinal);

        // ExecuteProfile718 has already checked the exact source, unique selected
        // profile, retained capture, overlays and actual prerequisite execution.
        private void ExecuteProfileMetadataSemantics1209(XElement profile, CharacterCreationPrerequisiteAuthority authority,
            CharacterCreationRuleSourceScopeInvocation invocation, SourceScopeLedger ledger)
        {
            metadataResults1209.Clear();
            string profileXmlDigest = Digest1209(profile);
            if (!invocation.TryGetProfileMetadataCandidate1209(authority.SettingsProfileId,
                    authority.RawProfileInputsDigest, profileXmlDigest, out string? graphXml, out var request)
                || request is null || graphXml is null || _sourceInputs.HasSourceDrift) return;
            var graph = XElement.Parse(graphXml, LoadOptions.PreserveWhitespace);
            foreach (string field in new[] { "gameplayoptionname", "nuyenformat" })
            {
                // Same exact leaf envelope as 1202; its format parser is reused.
                // Its Structural marker alone never reaches the registration below.
                var leaves = profile.Elements().Where(row => row.Name.LocalName == field).Take(2).ToArray();
                if (leaves is not [var leaf] || leaf.Name != field || leaf.HasAttributes
                    || leaf.Nodes().ToArray() is not [XText text] || text.GetType() != typeof(XText)
                    || string.IsNullOrWhiteSpace(text.Value)) continue;
                object? result = null;
                if (field == "gameplayoptionname")
                {
                    if (TryExecuteGameplay1209(leaf.Value, graph, invocation, out var predicates)) result = predicates;
                }
                else if (TryProfileFormat1202(leaf.Value, false, out _, out int decimals)
                    && ExecuteNuyenInputs1209(graph, request, decimals, out var quantities)) result = quantities;
                if (result is null || _sourceInputs.HasSourceDrift) continue;
                string diagnostic = JsonSerializer.Serialize(result);
                string leafDigest = Digest1209(leaf);
                string digest = CharacterCreationFinalizationDigest.Compute(new
                {
                    Operation = "selected-profile-mechanics1209", authority.SettingsProfileId,
                    authority.RawProfileInputsDigest, ProfileXmlDigest = profileXmlDigest, Field = field,
                    LeafDigest = leafDigest, invocation.PolicyDigest, invocation.CaptureDigest,
                    invocation.RequestDigest, invocation.GraphDigest,
                    Consumer = invocation.TryGetCompletedConsumer("profile", out var consumer) ? consumer : null,
                    Result = result
                });
                metadataResults1209.Add(field, new(authority.SettingsProfileId, authority.RawProfileInputsDigest,
                    profileXmlDigest, field, leafDigest, invocation.GraphDigest, invocation.RequestDigest, diagnostic, digest));
                ledger.Tree(leaf, CharacterCreationSourceOperationDisposition.Compiled,
                    new { Operation = "selected-profile-mechanics1209", Field = field, Execution = digest, Result = result });
            }
            // essenceformat intentionally remains at 1202 Structural accounting.
            // Purchase Quote rounds installed ware, but Complete has no exhaustive
            // source-bound ESS/special-attribute-loss/minimum-validation consumer.
        }

        private sealed record GameplayPredicate1209(string Source, int SourceOrder, string? SourceIdentity,
            string SourceDigest, string OwnerDigest, string OwnerId, string PredicateDigest,
            string Expected, bool Matches, bool Selected);

        private sealed record SelectedIdentity1209(string Scope, string Definition, Guid SourceId);
        private sealed record OwnedObject1209(string Scope, string Definition, XElement Node);

        // Saved object ownership, not arbitrary descendant names. These are the
        // root/child domains written by the canonical creation/legacy projectors.
        // Opaque notes, source bonuses and descriptive XML are never traversed.
        private static bool TryOwnedObjects1209(XElement graph, out List<OwnedObject1209> objects)
        {
            var rows = new List<OwnedObject1209>();
            objects = rows;
            if (graph.Name != "character") return false;
            foreach (var (container, entry) in new[]
            {
                ("cyberwares", "cyberware"), ("biowares", "bioware"), ("vehicles", "vehicle"),
                ("martialarts", "martialart"), ("metamagics", "metamagic"), ("echoes", "echo"),
                ("critterpowers", "critterpower")
            })
                if (!Children(graph, container, entry, out var unsupported) || unsupported.Length != 0) return false;
            return Add(graph, "qualities", "quality", "qualities", "qualities/quality", 0)
                && Add(graph, "gears", "gear", "gear", "gears/gear", 0)
                && Add(graph, "weapons", "weapon", "weapons", "weapons/weapon", 0)
                && Add(graph, "armors", "armor", "armor", "armors/armor", 0)
                && Add(graph, "lifestyles", "lifestyle", "lifestyles", "lifestyles/lifestyle", 0);

            bool Add(XElement parent, string container, string entry, string scope, string definition, int depth)
            {
                if (depth > 128 || !Children(parent, container, entry, out var children)) return false;
                foreach (var child in children)
                {
                    rows.Add(new(scope, definition, child));
                    if (rows.Count > 131072) return false;
                    bool valid = definition switch
                    {
                        "gears/gear" => Add(child, "children", "gear", "gear", "gears/gear", depth + 1),
                        "weapons/weapon" => Add(child, "underbarrel", "weapon", "weapons", "weapons/weapon", depth + 1)
                            && Add(child, "accessories", "accessory", "weapons", "accessories/accessory", depth + 1),
                        "armors/armor" => Add(child, "armormods", "armormod", "armor", "mods/mod", depth + 1)
                            && Add(child, "gears", "gear", "gear", "gears/gear", depth + 1),
                        "mods/mod" or "accessories/accessory" => Add(child, "gears", "gear", "gear", "gears/gear", depth + 1),
                        "lifestyles/lifestyle" => Add(child, "lifestylequalities", "lifestylequality", "lifestyles", "qualities/quality", depth + 1),
                        _ => true
                    };
                    if (!valid) return false;
                }
                return true;
            }

            static bool Children(XElement parent, string container, string entry, out XElement[] children)
            {
                children = [];
                var containers = parent.Elements().Where(row => row.Name.LocalName == container).ToArray();
                if (containers.Length == 0) return true;
                if (containers is not [var found] || found.Name != container || found.HasAttributes
                    || found.Nodes().Any(node => node is not XElement && !(node is XText text
                        && node.GetType() == typeof(XText) && string.IsNullOrWhiteSpace(text.Value)))
                    || found.Elements().Any(row => row.Name != entry)) return false;
                children = found.Elements(entry).ToArray();
                return true;
            }
        }

        private static bool TrySelectedIdentities1209(XElement graph, CharacterCreationRuleSourceScopeInvocation invocation,
            out HashSet<SelectedIdentity1209> selected)
        {
            var identities = new HashSet<SelectedIdentity1209>();
            selected = identities;
            if (!TryOwnedObjects1209(graph, out var objects)
                || invocation.CreationDraft718?.HeritageSelection is not { } heritage) return false;
            foreach (var row in objects)
                if (!ExactScalar1209(row.Node, "sourceid", out string id) || !Add(row.Scope, row.Definition, id)) return false;
            foreach (var effect in invocation.CompiledEffects)
            {
                if (effect.Scope is "weapons" or "armor")
                {
                    // Loadout compilation now has its own private trace. These
                    // owners were traversed above; require that exact identity
                    // instead of treating the trace as a magic selection or
                    // adding an owner absent from the actual compiled graph.
                    string ownedDefinition = effect.Scope == "weapons" ? "weapons/weapon" : "armors/armor";
                    if (!Guid.TryParse(effect.SourceId, out var sourceId) || sourceId == Guid.Empty
                        || !identities.Contains(new(effect.Scope, ownedDefinition, sourceId))) return false;
                    continue;
                }
                string? definition = effect.Scope switch
                {
                    "traditions" or "streams" => "traditions/tradition",
                    "powers" => "powers/power", "spells" => "spells/spell", "complexforms" => "complexforms/complexform",
                    _ => null
                };
                if (definition is null || !Add(effect.Scope, definition, effect.SourceId)) return false;
            }
            return Add("metatypes", "metatypes/metatype", heritage.MetatypeSourceId)
                && (string.IsNullOrEmpty(heritage.MetavariantSourceId)
                    || Add("metatypes", "metatypes/metatype/metavariants/metavariant", heritage.MetavariantSourceId));

            bool Add(string scope, string definition, string id)
            {
                if (!Guid.TryParse(id, out var sourceId) || sourceId == Guid.Empty) return false;
                identities.Add(new(scope, definition, sourceId));
                return true;
            }
        }

        private bool TryExecuteGameplay1209(string label, XElement graph,
            CharacterCreationRuleSourceScopeInvocation invocation, out IReadOnlyList<GameplayPredicate1209>? result)
        {
            result = null;
            if (!TryResolveCreationEffectivePolicy(out var policy) || policy is null) return false;
            if (!TrySelectedIdentities1209(graph, invocation, out var selected)) return false;
            var checks = new List<GameplayPredicate1209>();
            // Read the complete retained rule-source inventory, including unselected
            // definitions. Exact comparisons evaluate catalogue eligibility too;
            // this is not an empty basket/zero-spend inference.
            foreach (var source in policy.Sources.Where(row => row.Kind == CharacterCreationPolicySourceKind.Rules
                && row.SourceId != "profile"))
            {
                string file = source.SourceId + ".xml";
                if (source.ContentDigest is null || !NoOverlayForSource(file)
                    || !TryHasSelectedCustomDataInputFor(_customDirectories, file, out bool custom) || custom
                    || !TryComputeEffectiveInputDigest(_catalog, file, out string digest) || digest != source.ContentDigest
                    || !TryLoadEffectiveDocument(_catalog, file, out var document) || document?.Root is not { } root)
                    return false;
                foreach (var (definition, owner) in GameplayOwners1209(root, source.SourceId))
                foreach (var restriction in owner.Elements().Where(row => row.Name == "required" || row.Name == "forbidden"))
                foreach (var condition in restriction.Descendants().Where(row => row.Name.LocalName is "setting" or "gameplayoption"))
                {
                    if (owner.Elements("id").ToArray() is not [var id]
                        || string.IsNullOrWhiteSpace(id.Value) || !PlainPredicate1209(condition)) return false;
                    // Qualify by catalogue and exact definition ownership as well
                    // as the canonical ID. A gear ID cannot select a quality.
                    if (!Guid.TryParse(id.Value, out var sourceId) || sourceId == Guid.Empty) return false;
                    bool active = selected.Contains(new(source.SourceId, definition, sourceId));
                    bool matches = condition.Value == label; // SelectionShared.cs:1847, ordinal and verbatim.
                    checks.Add(new(source.SourceId, source.Order, source.IdentityDigest, digest, Digest1209(owner),
                        id.Value, Digest1209(condition), condition.Value, matches, active));
                    if (active && (!TryGameplayRestriction1209(restriction, label, out bool allowed) || !allowed)) return false;
                }
            }
            if (_sourceInputs.HasSourceDrift) return false;
            result = checks.AsReadOnly();
            return true;
        }

        private static IEnumerable<(string Definition, XElement Owner)> GameplayOwners1209(XElement root, string scope)
        {
            string[] definitions = scope switch
            {
                "qualities" => ["qualities/quality"], "gear" => ["gears/gear"],
                "weapons" => ["weapons/weapon", "accessories/accessory"], "armor" => ["armors/armor", "mods/mod"],
                "lifestyles" => ["lifestyles/lifestyle", "qualities/quality"],
                "metatypes" => ["metatypes/metatype", "metatypes/metatype/metavariants/metavariant"],
                "priorities" => ["priorities/priority"], "skills" => ["skills/skill", "knowledgeskills/skill"],
                "traditions" or "streams" => ["traditions/tradition", "spirits/spirit"],
                "powers" => ["powers/power", "enhancements/enhancement"],
                "spells" => ["spells/spell"], "complexforms" => ["complexforms/complexform"],
                _ => []
            };
            foreach (string definition in definitions)
            {
                IEnumerable<XElement> owners = new[] { root };
                foreach (string segment in definition.Split('/')) owners = owners.Elements(segment);
                foreach (var owner in owners) yield return (definition, owner);
            }
        }

        private static bool PlainPredicate1209(XElement node)
            => node.Name is { NamespaceName: "" } && node.Name.LocalName is "setting" or "gameplayoption"
                && !node.HasAttributes && node.Nodes().ToArray() is [XText text]
                && text.GetType() == typeof(XText) && !string.IsNullOrWhiteSpace(text.Value);

        // Legacy SelectionShared checks each required oneof/allof group and
        // rejects on any matching forbidden/oneof item. Forbidden/allof is not
        // that grammar; keep it unresolved rather than inventing AND semantics.
        // Mixed predicates stay unresolved; no normalization or unknown=false.
        private static bool TryGameplayRestriction1209(XElement restriction, string label, out bool allowed)
        {
            allowed = false;
            if (restriction.Name != "required" && restriction.Name != "forbidden" || restriction.HasAttributes
                || restriction.Nodes().Any(row => row is not XElement && !(row is XText text
                    && row.GetType() == typeof(XText) && string.IsNullOrWhiteSpace(text.Value)))
                || !restriction.HasElements) return false;
            bool accepted = true;
            foreach (var group in restriction.Elements())
            {
                if (group.Name != "oneof" && (restriction.Name != "required" || group.Name != "allof")
                    || group.HasAttributes || !group.HasElements
                    || group.Nodes().Any(row => row is not XElement && !(row is XText text
                        && row.GetType() == typeof(XText) && string.IsNullOrWhiteSpace(text.Value)))
                    || !group.Elements().All(PlainPredicate1209)) return false;
                bool match = group.Name == "oneof" ? group.Elements().Any(row => row.Value == label)
                    : group.Elements().All(row => row.Value == label);
                accepted &= restriction.Name == "required" ? match : !match;
            }
            allowed = accepted;
            return true;
        }

        private sealed record NuyenInput1209(string NodeDigest, string Name, string Category,
            decimal Quantity, int Decimals, decimal Increment);

        private static bool ExecuteNuyenInputs1209(XElement graph, CharacterCreationCompletionReviewRequest request,
            int decimals, out object? result)
        {
            result = null;
            // Finish's explicit request grammar contains integer Gear/Loadout
            // quantities and no arbitrary Nuyen/variable-price/expense input.
            // Percentage fields are dimensionless and must never be cash-rounded.
            if (request.Selections.Gear.Any(row => row.Quantity <= 0)
                || request.Selections.Loadout is not { } loadout
                || loadout.Weapons.Any(row => row.Quantity <= 0) || loadout.Armor.Any(row => row.Quantity <= 0)) return false;
            var checks = new List<NuyenInput1209>();
            if (!TryOwnedObjects1209(graph, out var objects)) return false;
            foreach (var gear in objects.Where(row => row.Scope == "gear").Select(row => row.Node))
            {
                if (!ExactScalar1209(gear, "name", out var name) || !ExactScalar1209(gear, "category", out var category)
                    || !ExactScalar1209(gear, "qty", out var quantity)
                    || !decimal.TryParse(quantity, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount)
                    || !CharacterGearQuantityRules.TryResolvePrecision(name, category, decimals, out int places, out decimal step)
                    || !CharacterGearQuantityRules.IsValidAmount(amount, step)) return false;
                checks.Add(new(Digest1209(gear), name, category, amount, places, step));
            }
            result = new
            {
                Operation = "canonical-gear-quantity-input-precision-and-Finish-request-branch1209",
                MaximumNuyenDecimals = decimals, Quantities = checks,
                RequestSelections = CharacterCreationFinalizationDigest.Compute(request.Selections),
                // Computed source prices remain exact: format precision is an
                // input bound, not permission to round a calculated purchase cost.
                SourcePriceRounding = false
            };
            return true;
        }

        private static bool ExactScalar1209(XElement parent, string field, out string value)
        {
            value = string.Empty;
            var rows = parent.Elements().Where(row => row.Name.LocalName == field).ToArray();
            if (rows is not [var leaf] || leaf.Name != field || leaf.HasAttributes
                || leaf.Nodes().ToArray() is not [XText text] || text.GetType() != typeof(XText)) return false;
            value = text.Value;
            return true;
        }

        private static string Digest1209(XElement value)
            => CharacterCreationFinalizationDigest.Compute(value.ToString(SaveOptions.DisableFormatting));
    }
}

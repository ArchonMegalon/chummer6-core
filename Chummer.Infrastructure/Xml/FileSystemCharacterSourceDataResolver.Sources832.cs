using System.Globalization;
using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using D = Chummer.Application.Characters.CharacterCreationSourceOperationDisposition;

namespace Chummer.Infrastructure.Xml;

public sealed partial class FileSystemCharacterSourceDataResolver
{
    private sealed partial class SourceDataContext
    {
        private static void ExecuteMetatypeGraph832(XElement row, CharacterCreationPriorityHeritageSelection heritage,
            CharacterCreationRuleSourceScopeInvocation invocation, SourceScopeLedger ledger)
        {
            // Caller has already matched the full selected source node digest.
            if (invocation.CompiledMovement832 is { } movement && movement == heritage.Movement)
            {
                foreach (var (field, rate) in new[] { ("walk", movement.Walk), ("run", movement.Run), ("sprint", movement.Sprint) })
                {
                    string value = string.Join('/', rate.Ground.ToString(CultureInfo.InvariantCulture),
                        rate.Swim.ToString(CultureInfo.InvariantCulture), rate.Fly.ToString(CultureInfo.InvariantCulture));
                    Scalar718(ledger, row, field, value, "CompileSelectedGraph-movement", movement);
                }
            }
            // An empty effect container has no selected gameplay instruction.
            // A nonempty/attributed/duplicate container remains unconsumed.
            if (row.Elements("bonus").ToArray() is [var bonus] && !bonus.HasAttributes
                && !bonus.HasElements && !HasDirectScopeText(bonus) && !bonus.Nodes().OfType<XProcessingInstruction>().Any())
                ledger.Node(bonus, D.Structural, "empty-selected-metatype-bonus");
        }

        private static void ExecuteLifestyleSupporting832(XElement root, CharacterCreationLifestylesAuthority authority,
            IReadOnlyList<CharacterCreationLifestyleProjection> projections, SourceScopeLedger ledger)
        {
            // Execute the canonical calculator using the freshly resolved retained
            // source. Both paid and fallback configurations must reproduce the
            // actual completed projections before any negative selection proof.
            foreach (var projection in projections)
                if (!CharacterCreationLifestylesRules.TryProject(projection.Configuration, authority, out var replay, out _)
                    || CharacterCreationFinalizationDigest.Compute(replay) != CharacterCreationFinalizationDigest.Compute(projection))
                    return;
            var options = projections.Select(p => authority.LifestyleOptions.Single(o =>
                o.OptionId == p.Configuration.BaseLifestyleOptionId)).DistinctBy(o => o.OptionId).ToArray();
            ConsumeAspects832(root, "comforts", "comfort", options, projections, ledger,
                o => o.BaseComforts, o => o.MaximumComforts);
            ConsumeAspects832(root, "neighborhoods", "neighborhood", options, projections, ledger,
                o => o.BaseArea, o => o.MaximumArea);
            ConsumeAspects832(root, "securities", "security", options, projections, ledger,
                o => o.BaseSecurity, o => o.MaximumSecurity);

            // Include both explicit choices and source-provided built-ins. Never
            // declare a freegrid's quality inactive just because it wasn't bought.
            var qualityOptions = projections.SelectMany(p => p.Configuration.Qualities).Select(q => q.OptionId)
                .Concat(options.SelectMany(o => o.BuiltInQualities).Select(q => q.QualityOptionId))
                .ToHashSet(StringComparer.Ordinal);
            if (qualityOptions.Any(id => authority.QualityOptions.Count(o => o.OptionId == id) != 1)) return;
            var selectedQualityIds = authority.QualityOptions.Where(o => qualityOptions.Contains(o.OptionId))
                .Select(o => o.SourceId.ToString("D")).ToHashSet(StringComparer.Ordinal);
            if (root.Element("qualities") is { } qualities
                && qualities.Elements().All(row => row.Name == "quality")
                && Container718(ledger, qualities, "completed-lifestyle-quality-selection"))
            {
                var rows = qualities.Elements().ToArray();
                var identities = new List<string>();
                foreach (var row in rows)
                {
                    if (!TryScopeRowIdentity(row, new("qualities", "quality", true), out var id)) return;
                    identities.Add(id);
                }
                if (identities.Distinct(StringComparer.Ordinal).Count() != identities.Count
                    || !selectedQualityIds.IsSubsetOf(identities)) return;
                for (int i = 0; i < rows.Length; i++)
                    if (!selectedQualityIds.Contains(identities[i]))
                        ledger.Tree(rows[i], D.NotSelected, new { Operation = "completed-lifestyle-quality-selection",
                            Candidate = identities[i], Selected = selectedQualityIds.Order(StringComparer.Ordinal).ToArray(),
                            Projections = projections.Select(p => p.LifestyleDigest).ToArray() });
            }

            ExecuteLifestyleAccounting1000(root, authority, projections, ledger);
            ExecuteLifestyleLocations1008(root, authority, projections, ledger);
            ExecuteLifestylePrompts1008(root, authority, projections, ledger);
        }

        private static void ConsumeAspects832(XElement root, string containerName, string entryName,
            IReadOnlyList<CharacterCreationLifestyleCatalogOption> options,
            IReadOnlyList<CharacterCreationLifestyleProjection> projections, SourceScopeLedger ledger,
            Func<CharacterCreationLifestyleCatalogOption, int> minimum,
            Func<CharacterCreationLifestyleCatalogOption, int> maximum)
        {
            if (root.Element(containerName) is not { } container
                || container.Elements().Any(row => row.Name != entryName)) return;
            var rows = container.Elements().ToArray();
            if (!UniqueNamedRows832(rows) || !Container718(ledger, container, "BuildLifestyleElement-aspect-selection")) return;
            foreach (var row in rows)
            {
                string name = row.Element("name")!.Value.Trim();
                var selected = options.Where(o => o.Name == name).ToArray();
                if (selected.Length == 0)
                {
                    ledger.Tree(row, D.NotSelected, new { Operation = "completed-lifestyle-aspect-selection",
                        Projections = projections.Select(p => p.LifestyleDigest).ToArray() });
                    continue;
                }
                if (selected is not [var option] || !Container718(ledger, row, "BuildLifestyleElement-aspect")) continue;
                object result = new { Operation = "BuildLifestyleElement-and-TryProject-aspect-bounds", option.OptionId,
                    Minimum = minimum(option), Maximum = maximum(option),
                    Projections = projections.Where(p => p.Configuration.BaseLifestyleOptionId == option.OptionId).ToArray() };
                Scalar718(ledger, row, "name", option.Name, "lifestyle-aspect-source-selection", result);
                Scalar718(ledger, row, "minimum", minimum(option), "BuildLifestyleElement-base-aspect", result);
                Scalar718(ledger, row, "limit", maximum(option), "TryProject-and-BuildLifestyleElement-aspect-limit", result);
            }
        }

        private static bool UniqueNamedRows832(IReadOnlyList<XElement> rows)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var row in rows)
                if (row.HasAttributes || HasDirectScopeText(row) || row.Elements("name").ToArray() is not [var name]
                    || name.HasAttributes || name.HasElements || string.IsNullOrWhiteSpace(name.Value)
                    || !names.Add(name.Value.Trim())) return false;
            return true;
        }
    }
}

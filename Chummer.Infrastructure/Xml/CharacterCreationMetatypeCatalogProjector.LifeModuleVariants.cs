using System.Xml.Linq;
using Chummer.Contracts.Characters;

namespace Chummer.Infrastructure.Xml;

internal static partial class CharacterCreationMetatypeCatalogProjector
{
    // Enumerate the actual nested source, including unsupported rows. Never consult
    // a private inventory, flatten global children, or inherit a parent's bonuses.
    internal static CharacterCreationMetatypeCatalogAuthority ProjectLifeModuleVariants(
        XDocument document, CharacterCreationMetatypeCatalogAuthority catalog, string? schema,
        bool careerCondition5497 = false)
    {
        if (!catalog.IsAuthoritative || catalog.SourceContext.MetatypeKarmaMultiplier is not int multiplier
            || catalog.SourceContext.MinimumInitiativeDiceFallback is not int dice) return catalog;
        var variants = new List<CharacterCreationMetatypeOptionProjection>();
        foreach (var parent in catalog.Options.Where(option => option.OptionId is HumanId or ElfId or OrkId or DwarfId or TrollId))
        {
            var parents = document.Root?.Element("metatypes")?.Elements("metatype")
                .Where(row => Read(row, "id") == parent.OptionId && Read(row, "name") == parent.Label).ToArray();
            if (parents is not [var source]) return Blocked(catalog.SourceContext, CharacterCreationMetatypeCatalogBlockers.BaseEntryInvalid);
            var containers = source.Elements("metavariants").ToArray();
            if (containers.Length == 0) continue;
            if (containers is not [var container] || container.HasAttributes
                || !TryReadMetavariants(source, parent.OptionId, out var locators))
                return Blocked(catalog.SourceContext, CharacterCreationMetatypeCatalogBlockers.BaseEntryDuplicate);
            foreach (var locator in locators)
            {
                var row = container.Elements("metavariant").SingleOrDefault(item => Read(item, "id") == locator.OptionId);
                string anchor = $"metatypes.xml#metatype:{parent.OptionId}/metavariant:{locator.OptionId}";
                var identity = new CharacterCreationMetatypeSelectionIdentity(parent.OptionId, locator.OptionId);
                CharacterCreationMetatypeOptionProjection? projected = null;
                var movementAnchors = new List<string>();
                if (row is not null && Read(row, "category") == "Metavariant"
                    && !row.DescendantsAndSelf().Any(node => node.Name.Namespace != XNamespace.None
                        || node.Nodes().Any(child => child is not XElement and not XComment and not XText)
                        || node.HasElements && node.Nodes().OfType<XText>().Any(text => !string.IsNullOrWhiteSpace(text.Value)))
                    && !row.Elements().Any(field => !SupportedBaseFields.Contains(field.Name.LocalName)
                        && !(schema == CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV4 && field.Name == "initiativedice")
                        || field.Name == "metavariants")
                    && !row.Elements().GroupBy(field => field.Name).Any(group => group.Count() != 1))
                {
                    var effective = new XElement(row);
                    // Character.cs applies per-field variant ?? parent movement fallback.
                    // This is the only inherited row data; ranges, Karma, qualities and
                    // bonus all come from the child (an absent bonus means no effects).
                    foreach (string field in new[] { "movement", "walk", "run", "sprint" })
                        if (!effective.Elements(field).Any() && source.Element(field) is { } fallback)
                        {
                            effective.Add(new XElement(fallback));
                            movementAnchors.Add($"metatypes.xml#metatype:{parent.OptionId}/{field}");
                        }
                    if (!effective.Elements("bonus").Any()) effective.Add(new XElement("bonus"));
                    if (TryProjectOption(effective, locator.OptionId, locator.Label, multiplier, dice,
                        catalog.SourceContext.EnabledSourcebooks, true, out var candidate, schema,
                        careerCondition5497, metavariant: true)) projected = candidate;
                }
                projected ??= new(locator.OptionId, locator.Label, "Metavariant", locator.SourceBook ?? string.Empty,
                    locator.SourcePage ?? 0, 0, 0, [], new(0, 0, 0, 0),
                    CharacterCreationMetatypeMovementProjection.Unavailable, [], [], false,
                    [CharacterCreationMetatypeCatalogBlockers.MetavariantUnsupported], [anchor]);
                string oldAnchor = $"metatypes.xml#metatype:{locator.OptionId}";
                variants.Add(projected with
                {
                    MetatypeIdentity = identity, ParentMetatypeLabel = parent.Label, ParentMetatypeCategory = parent.Category,
                    IsEnabled = parent.IsEnabled && projected.IsEnabled,
                    Blockers = projected.Blockers.Concat(parent.IsEnabled ? [] : parent.Blockers).Distinct(StringComparer.Ordinal).ToArray(),
                    SourceAnchorIds = new[] { $"metatypes.xml#metatype:{parent.OptionId}", anchor }
                        .Concat(movementAnchors).ToArray(),
                    GrantedQualities = projected.GrantedQualities.Select(quality => quality with
                    { SourceAnchorIds = quality.SourceAnchorIds.Select(value => value.Replace(oldAnchor, anchor, StringComparison.Ordinal)).ToArray() }).ToArray()
                });
            }
        }
        return catalog with { MetavariantOptions = variants.Count == 0 ? null : variants.ToArray() };
    }
}

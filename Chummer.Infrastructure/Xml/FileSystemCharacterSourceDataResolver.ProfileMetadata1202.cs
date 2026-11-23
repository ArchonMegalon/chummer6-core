using System.Xml;
using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using D1202 = Chummer.Application.Characters.CharacterCreationSourceOperationDisposition;

namespace Chummer.Infrastructure.Xml;

public sealed partial class FileSystemCharacterSourceDataResolver
{
    private sealed partial class SourceDataContext
    {
        // Called only on the actual selected profile after the existing source,
        // capture and creation-execution checks. This preserves metadata structure;
        // it does NOT execute gameplay-option predicates, cash input or Essence math.
        private static void ExecuteProfileMetadata1202(XElement profile, string profileId,
            CharacterCreationPolicySource source, SourceScopeLedger ledger)
        {
            foreach (string field in new[] { "gameplayoptionname", "nuyenformat", "essenceformat" })
            {
                // Count by local name first: a qualified shadow is still a duplicate.
                var matches = profile.Elements().Where(node => node.Name.LocalName == field).Take(2).ToArray();
                if (matches is not [var leaf] || leaf.Name != field || leaf.HasAttributes
                    || leaf.Nodes().ToArray() is not [var text] || text.NodeType != XmlNodeType.Text
                    || string.IsNullOrWhiteSpace(((XText)text).Value)) continue;

                int? minimumDecimals = null, maximumDecimals = null;
                if (field != "gameplayoptionname")
                {
                    if (!TryProfileFormat1202(leaf.Value, field == "essenceformat", out int minimum, out int maximum))
                        continue;
                    minimumDecimals = minimum;
                    maximumDecimals = maximum;
                }

                // Legacy CharacterSettings returns the label verbatim; SelectionShared
                // also uses it for exact gameplay-option predicates. Format precision
                // feeds numeric input and, for Essence, rounding/validation. Retain
                // these parameters without claiming those mechanical consumers ran.
                ledger.ProfileMetadata1202(leaf, new
                {
                    Operation = "selected-profile-metadata-structure1202",
                    ProfileId = profileId,
                    ProfileNodeDigest = CharacterCreationFinalizationDigest.Compute(profile.ToString(SaveOptions.DisableFormatting)),
                    SourceId = source.SourceId,
                    SourceKind = source.Kind,
                    SourceOrder = source.Order,
                    SourceIdentityDigest = source.IdentityDigest,
                    SourceDigest = source.ContentDigest,
                    Field = field,
                    Value = leaf.Value,
                    MinimumDecimals = minimumDecimals,
                    MaximumDecimals = maximumDecimals,
                    MechanicalCoverage = false
                });
            }
        }

        private static bool TryProfileFormat1202(string value, bool essence, out int minimum, out int maximum)
        {
            minimum = maximum = 0;
            int separator = value.IndexOf('.');
            string integer = separator < 0 ? value : value[..separator];
            if (integer is not ("0" or "#,0")) return false;
            if (separator < 0) return !essence;
            string fraction = value[(separator + 1)..];
            if (fraction.Length is < 1 or > 28 || essence && fraction.Length < 2) return false;
            bool optional = false;
            foreach (char digit in fraction)
            {
                if (digit == '#') optional = true;
                else if (digit == '0' && !optional) minimum++;
                else return false;
            }
            maximum = fraction.Length;
            // Only the bounded literal decimal-placeholder grammar is executed.
            // No trimming/defaulting/normalization, sections, literals, scaling,
            // exponents or legacy Essence-format repair is silently accepted.
            return true;
        }
    }

    private sealed partial class SourceScopeLedger
    {
        internal void ProfileMetadata1202(XElement leaf, object metadata)
        {
            // Both observations are independently bound to their exact source order,
            // path and digest, in addition to Consume's invocation/consumer binding.
            // The caller has proved there is exactly one plain text node.
            foreach (XObject node in new XObject[] { leaf, leaf.Nodes().Single() })
            {
                if (!indices.TryGetValue(node, out int index)) continue;
                var observation = operations[index];
                Consume(new[] { node }, D1202.Structural, new
                {
                    ObligationId = obligation886,
                    observation.Order,
                    observation.Path,
                    observation.SourceDigest,
                    Metadata = metadata
                });
            }
        }
    }
}

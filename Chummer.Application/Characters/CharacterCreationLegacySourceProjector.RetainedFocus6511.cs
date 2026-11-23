using System.Xml.Linq;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

public static partial class CharacterCreationLegacySourceProjector
{
    // A retained inventory capability, deliberately separate from purchase selection.
    internal sealed class RetainedCareerFocus6511
    {
        private readonly XElement source;
        private readonly XElement economics;
        internal string SourceDigest { get; }
        internal RetainedCareerFocus6511(XElement source)
        {
            this.source = source;
            SourceDigest = CharacterCreationFinalizationDigest.Compute(source.ToString(SaveOptions.DisableFormatting));
            // The typed focus compiler has consumed the complete original bonus.
            // This detached serializer input is never a source or capture identity.
            economics = new XElement(source);
            economics.Element("bonus")!.Remove();
        }

        internal bool ValidateCatalog(CharacterCareerGearPurchaseRequest probe, string settings)
            => ValidateCatalogCore8012(probe, settings, null);

        internal bool ValidateCatalog(CharacterCareerGearPurchaseRequest probe, CareerGearPricePolicyEvaluation policy)
            => ValidateCatalogCore8012(probe, null, policy);

        private bool ValidateCatalogCore8012(CharacterCareerGearPurchaseRequest probe, string? settings,
            CareerGearPricePolicyEvaluation? policy)
        {
            if (!TryReadCareerGearRatings(economics, out int minimum, out int maximum, out _, out _, out _)) return false;
            for (int rating = minimum; rating <= maximum; rating++)
            {
                var request = probe with { Rating = rating };
                bool valid = policy is null
                    ? TryBuildCareerGearFromSource(economics, request, settings, null,
                        probe.SourceId, out _, out _, out _, out _, out _, out _)
                    : policy.TryBuildPurchasedGearFromSource(economics, request,
                        probe.SourceId, out _, out _, out _, out _, out _, out _);
                if (!valid) return false;
            }
            return true;
        }

        internal bool TryProjectSaved(XElement character, XElement saved, CharacterCareerGearPurchaseRequest request,
            string settings, Guid instance, out string payload, out decimal unit, out decimal total,
            out int availability, out string legality, out XElement[] effects)
        {
            payload = legality = string.Empty; unit = total = 0m; availability = 0; effects = [];
            if (request.Quantity != 1 || instance == Guid.Empty
                || !Guid.TryParse(ReadOptionalScalar(saved, "guid"), out var savedId) || savedId != instance
                || !Guid.TryParse(ReadOptionalScalar(saved, "sourceid"), out var sourceId) || sourceId != request.SourceId
                || !int.TryParse(ReadOptionalScalar(saved, "rating"), out int rating) || rating != request.Rating
                || saved.Element("discountedcost") is not null
                    && (!bool.TryParse(ReadOptionalScalar(saved, "discountedcost"), out bool discounted) || discounted)
                || !CharacterCareerFocusBondingTransaction.TryCompileRetainedFocus6511(character, saved, source, out effects)
                || !TryBuildCareerGearFromSource(economics, request, settings, null, instance,
                    out payload, out unit, out total, out availability, out legality, out _)) return false;
            // Preserve the complete supported native saved shape for the consumer's
            // shape comparison; economics above are current source replacement values.
            payload = saved.ToString(SaveOptions.DisableFormatting);
            return true;
        }
    }

    internal static bool TryReadRetainedCareerFocus6511(CharacterCareerGearPurchaseSource authority,
        out RetainedCareerFocus6511? focus)
    {
        focus = null;
        if (!CharacterCareerFocusBondingTransaction.TryReadRetainedFocusSource6511(authority, out var source)) return false;
        focus = new(source!);
        return true;
    }
}

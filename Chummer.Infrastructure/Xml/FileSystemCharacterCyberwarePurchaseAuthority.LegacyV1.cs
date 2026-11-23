using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;

namespace Chummer.Infrastructure.Xml;

public sealed partial class FileSystemCharacterCyberwarePurchaseAuthority
{
    // Receipt verification only. Frozen from 70345d64b53b3c1c7a628115f50438bc2f9dad71:
    // original entry admission/exclusion reasons, grade ordering, scalar formatting,
    // canonical catalog inputs and rating-zero quote arithmetic/digest. The shared
    // Prepare path retains the original XML, source pin, settings and overlay checks.
    // Never call this from public Quote/Commit or reinterpret a v2 quote as v1.
    private static partial class LegacyV1ReceiptAuthority
    {
        private const decimal MinimumMarkupPercent = -99m;
        private const decimal MaximumMarkupPercent = 1_000m;
        private const int MarkupDecimalPlaces = 2;
        private const int MinimumEssenceDiscountPercent = -100;
        private const int MaximumEssenceDiscountPercent = 100;

        private static int StandardRound(decimal value)
            => decimal.ToInt32(value >= 0m ? decimal.Ceiling(value) : decimal.Floor(value));

        private static readonly HashSet<string> s_AdmittedSourceFields = new(StringComparer.Ordinal)
        {
            "id", "name", "translate", "category", "ess", "capacity", "avail", "cost",
            "source", "page", "forcegrade", "bannedgrades"
        };

        public static bool TryProjectCatalog(
            XDocument document,
            ICharacterSourceDataContext context,
            CharacterCyberwarePurchaseSettings settings,
            bool exCon,
            out List<CharacterCyberwarePurchaseCatalogEntry> entries,
            out List<CharacterCyberwarePurchaseCatalogExclusion> exclusions,
            out string? blocker)
        {
            entries = [];
            exclusions = [];
            blocker = null;
            XElement? root = document.Root;
            XElement[] gradeContainers = root?.Elements("grades").Take(2).ToArray() ?? [];
            XElement[] wareContainers = root?.Elements("cyberwares").Take(2).ToArray() ?? [];
            XElement[] categoryContainers = root?.Elements("categories").Take(2).ToArray() ?? [];
            if (root is null || root.Name.NamespaceName.Length != 0 || root.Name.LocalName != "chummer"
                || gradeContainers.Length != 1 || wareContainers.Length != 1 || categoryContainers.Length != 1)
            {
                blocker = "The pinned Cyberware catalog containers are absent or ambiguous.";
                return false;
            }

            var rowsById = new Dictionary<Guid, XElement>();
            foreach (XElement row in wareContainers[0].Elements("cyberware"))
            {
                if (!TryReadScalar(row, "id", out string idText)
                    || !Guid.TryParseExact(idText, "D", out Guid id)
                    || id == Guid.Empty
                    || !rowsById.TryAdd(id, row))
                {
                    blocker = "The pinned Cyberware catalog contains an invalid or duplicate source ID.";
                    return false;
                }
            }
            var gradesById = new Dictionary<Guid, XElement>();
            foreach (XElement row in gradeContainers[0].Elements("grade"))
            {
                if (!TryReadScalar(row, "id", out string idText)
                    || !Guid.TryParseExact(idText, "D", out Guid id)
                    || id == Guid.Empty
                    || !gradesById.TryAdd(id, row))
                {
                    blocker = "The pinned Cyberware grades contain an invalid or duplicate source ID.";
                    return false;
                }
            }
            HashSet<string> blackMarketCategories = categoryContainers[0].Elements("category")
                .Where(node => string.Equals((string?)node.Attribute("blackmarket"), "Cyberware", StringComparison.Ordinal))
                .Select(node => node.Value)
                .ToHashSet(StringComparer.Ordinal);

            foreach ((Guid id, XElement row) in rowsById.OrderBy(item => item.Key))
            {
                string name = ReadOptionalScalar(row, "name");
                var sourceId = new CharacterCyberwareSourceId(id);
                string? reason = SourceExclusionReason(row, id, name, exCon);
                CharacterCyberwareCommerceSource resolved = CharacterCyberwareCommerceSource.Unavailable;
                if (reason is null
                    && (!context.TryResolveCyberwareCommerceSource(id.ToString("D"), name, "Cyberware", out resolved)
                        || resolved.SourceEntryUsesGeneratedOrImprovementSemantics
                        || !string.Equals(resolved.SourceId, id.ToString("D"), StringComparison.OrdinalIgnoreCase)
                        || !string.Equals(resolved.Name, name, StringComparison.Ordinal)))
                {
                    reason = "Exact enabled-book/profile source resolution failed.";
                }
                if (reason is not null)
                {
                    exclusions.Add(new CharacterCyberwarePurchaseCatalogExclusion(sourceId, name, reason));
                    continue;
                }

                var grades = new List<CharacterCyberwarePurchaseGrade>();
                foreach (CharacterCyberwareCommerceGradeSource candidate in resolved.Grades)
                {
                    if (!Guid.TryParseExact(candidate.Id, "D", out Guid gradeId)
                        || candidate.SpecialSemantics
                        || !gradesById.TryGetValue(gradeId, out XElement? gradeRow)
                        || !TryReadInteger(gradeRow, "avail", out int availabilityModifier)
                        || string.Equals(candidate.Name, "None", StringComparison.Ordinal)
                           && !string.Equals(resolved.ForcedGrade, "None", StringComparison.Ordinal)
                        || settings.BannedGrades.Contains(candidate.Name, StringComparer.Ordinal)
                        || resolved.BannedGrades.Contains(candidate.Name, StringComparer.Ordinal)
                        || resolved.ForcedGrade.Length != 0
                           && !string.Equals(resolved.ForcedGrade, candidate.Name, StringComparison.Ordinal))
                    {
                        continue;
                    }
                    grades.Add(new CharacterCyberwarePurchaseGrade(
                        new CharacterCyberwareGradeId(gradeId),
                        candidate.Name,
                        candidate.CostMultiplier,
                        candidate.EssenceMultiplier,
                        availabilityModifier));
                }
                if (grades.Count == 0)
                {
                    exclusions.Add(new CharacterCyberwarePurchaseCatalogExclusion(sourceId, name,
                        "No side-effect-free enabled grade remains."));
                    continue;
                }
                grades.Sort((left, right) => left.Id.Value.CompareTo(right.Id.Value));
                string category = ReadOptionalScalar(row, "category");
                entries.Add(new CharacterCyberwarePurchaseCatalogEntry(
                    sourceId,
                    name,
                    category,
                    ReadOptionalScalar(row, "ess"),
                    ReadOptionalScalar(row, "capacity"),
                    ReadOptionalScalar(row, "avail"),
                    ReadOptionalScalar(row, "cost"),
                    ReadOptionalScalar(row, "source"),
                    ReadOptionalScalar(row, "page"),
                    blackMarketCategories.Contains(category),
                    resolved.ForcedGrade,
                    resolved.BannedGrades.OrderBy(value => value, StringComparer.Ordinal).ToArray(),
                    grades.ToArray()));
            }
            return true;
        }

        private static string? SourceExclusionReason(XElement row, Guid id, string name, bool exCon)
        {
            if (id.ToString("D") is EssenceHoleSourceId or EssenceAntiHoleSourceId)
                return "Essence Hole pseudo-Cyberware is not purchasable in this lane.";
            if (row.Name.NamespaceName.Length != 0 || row.HasAttributes
                || row.Elements().Any(element => element.Name.NamespaceName.Length != 0
                                                 || !s_AdmittedSourceFields.Contains(element.Name.LocalName)))
                return "The source row contains generated, child, prompt, matrix, gear, mount, or otherwise unsupported fields.";
            string[] required = ["id", "name", "category", "ess", "capacity", "avail", "cost", "source", "page"];
            if (required.Any(field => !TryReadScalar(row, field, out string value) || string.IsNullOrWhiteSpace(value)))
                return "The source row is missing a unique required scalar.";
            if (!string.Equals(name, name.Trim(), StringComparison.Ordinal) || name.IndexOfAny(['\r', '\n', '\0']) >= 0)
                return "The source name is not a stable scalar.";
            if (row.Elements("translate").Count() > 1
                || row.Elements("forcegrade").Count() > 1
                || row.Elements("bannedgrades").Count() > 1
                || row.Elements("bannedgrades").Any(container => container.HasAttributes
                    || container.Elements().Any(node => node.Name.LocalName != "grade" || node.HasAttributes || node.HasElements)))
                return "The source grade constraints are ambiguous.";
            if (!decimal.TryParse(ReadOptionalScalar(row, "cost"), NumberStyles.Number, CultureInfo.InvariantCulture, out decimal cost)
                || !decimal.TryParse(ReadOptionalScalar(row, "ess"), NumberStyles.Number, CultureInfo.InvariantCulture, out decimal essence)
                || cost < 0m || essence < 0m)
                return "Dynamic or negative cost/Essence expressions are outside the fixed-value slice.";
            if (!FixedAvailability().IsMatch(ReadOptionalScalar(row, "avail")))
                return "Dynamic availability expressions are outside the fixed-value slice.";
            if (exCon && (ReadOptionalScalar(row, "avail").EndsWith('R')
                          || ReadOptionalScalar(row, "avail").EndsWith('F')))
                return "Ex-Con characters cannot buy restricted or forbidden Cyberware in the bounded lane.";
            return null;
        }

        public static string ComputeCatalogDigest(
            string profileId,
            string cyberwareDigest,
            CharacterCyberwarePurchaseSettings settings,
            IEnumerable<CharacterCyberwarePurchaseCatalogEntry> entries,
            IEnumerable<CharacterCyberwarePurchaseCatalogExclusion> exclusions)
        {
            var lines = new List<string>
            {
                "career-cyberware-purchase-catalog-v1",
                profileId,
                cyberwareDigest,
                settings.AllowEssenceDiscounts.ToString(CultureInfo.InvariantCulture),
                settings.MultiplyRestrictedCost.ToString(CultureInfo.InvariantCulture),
                settings.RestrictedCostMultiplier.ToString(CultureInfo.InvariantCulture),
                settings.MultiplyForbiddenCost.ToString(CultureInfo.InvariantCulture),
                settings.ForbiddenCostMultiplier.ToString(CultureInfo.InvariantCulture),
                settings.EssenceDecimals.ToString(CultureInfo.InvariantCulture),
                settings.DoNotRoundEssenceInternally.ToString(CultureInfo.InvariantCulture)
            };
            lines.AddRange(settings.BannedGrades.Select(value => $"profile-banned-grade:{value}"));
            lines.AddRange(CharacterCyberwarePurchaseLegacyAuthority.CanonicalInputs);
            foreach (CharacterCyberwarePurchaseCatalogEntry entry in entries.OrderBy(item => item.SourceId.Value))
            {
                lines.Add(string.Join("|",
                    "entry", entry.SourceId.Value.ToString("D"), entry.Name, entry.Category,
                    entry.EssenceExpression, entry.CapacityExpression, entry.AvailabilityExpression,
                    entry.CostExpression, entry.SourceBook, entry.Page,
                    entry.BlackMarketEligible.ToString(CultureInfo.InvariantCulture), entry.ForcedGrade,
                    string.Join(",", entry.BannedGrades)));
                lines.AddRange(entry.Grades.OrderBy(item => item.Id.Value).Select(grade => string.Join("|",
                    "grade", grade.Id.Value.ToString("D"), grade.Name,
                    grade.CostMultiplier.ToString(CultureInfo.InvariantCulture),
                    grade.EssenceMultiplier.ToString(CultureInfo.InvariantCulture),
                    grade.AvailabilityModifier.ToString(CultureInfo.InvariantCulture))));
            }
            lines.AddRange(exclusions.OrderBy(item => item.SourceId.Value).Select(exclusion =>
                string.Join("|", "exclude", exclusion.SourceId.Value.ToString("D"), exclusion.Name, exclusion.Reason)));
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", lines))))
                .ToLowerInvariant();
        }

        public static CharacterCyberwarePurchaseQuote Quote(
            CharacterCyberwarePurchasePreparation preparation,
            CharacterCyberwarePurchaseSelection selection)
        {
            ArgumentNullException.ThrowIfNull(preparation);
            ArgumentNullException.ThrowIfNull(selection);
            if (!preparation.Exact)
                return Blocked(selection, preparation.Blockers.Count == 0 ? null : preparation.Blockers[0]);
            if (selection.SourceId.Value == Guid.Empty || selection.GradeId.Value == Guid.Empty)
                return Blocked(selection, CharacterCyberwarePurchaseBlockers.IdentityInvalid);
            if (selection.Rating != 0)
                return Blocked(selection, "The bounded catalog admits only fixed-rating Cyberware; rating must be zero.");
            if (selection.MarkupPercent < MinimumMarkupPercent
                || selection.MarkupPercent > MaximumMarkupPercent
                || decimal.Round(selection.MarkupPercent, MarkupDecimalPlaces, MidpointRounding.AwayFromZero)
                   != selection.MarkupPercent)
            {
                return Blocked(selection, "Markup must be an exact value from -99.00 through 1000.00 with at most two decimal places.");
            }
            if (selection.EssenceDiscountPercent < MinimumEssenceDiscountPercent
                || selection.EssenceDiscountPercent > MaximumEssenceDiscountPercent
                || selection.EssenceDiscountPercent != 0 && !preparation.Settings.AllowEssenceDiscounts)
            {
                return Blocked(selection, "The integer Essence discount is outside the exact enabled settings profile.");
            }

            CharacterCyberwarePurchaseCatalogEntry[] entries = preparation.Entries
                .Where(candidate => candidate.SourceId == selection.SourceId)
                .Take(2)
                .ToArray();
            if (entries.Length != 1)
                return Blocked(selection, "The selected Cyberware source ID is absent or ambiguous.");
            CharacterCyberwarePurchaseCatalogEntry entry = entries[0];
            CharacterCyberwarePurchaseGrade[] grades = entry.Grades
                .Where(candidate => candidate.Id == selection.GradeId)
                .Take(2)
                .ToArray();
            if (grades.Length != 1)
                return Blocked(selection, "The selected Cyberware grade ID is absent or ambiguous.");
            CharacterCyberwarePurchaseGrade grade = grades[0];
            if (entry.BannedGrades.Contains(grade.Name, StringComparer.Ordinal)
                || entry.ForcedGrade.Length != 0
                   && !string.Equals(entry.ForcedGrade, grade.Name, StringComparison.Ordinal))
            {
                return Blocked(selection, "The selected grade violates the source row's exact grade constraints.");
            }
            if (selection.BlackMarketDiscount && !entry.BlackMarketEligible)
                return Blocked(selection, "Black-market discount is unavailable for the selected source category.");
            if (!decimal.TryParse(entry.CostExpression, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal sourceCost)
                || !decimal.TryParse(entry.EssenceExpression, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal sourceEssence)
                || sourceCost < 0m
                || sourceEssence < 0m)
            {
                return Blocked(selection, "The selected source row no longer has fixed exact cost and Essence values.");
            }

            try
            {
                decimal baseCost = checked(sourceCost * grade.CostMultiplier);
                decimal chargedCost = baseCost;
                if (selection.BlackMarketDiscount)
                    chargedCost = checked(chargedCost * 0.9m);
                char suffix = AvailabilitySuffix(entry.AvailabilityExpression);
                if (suffix == 'R' && preparation.Settings.MultiplyRestrictedCost)
                    chargedCost = checked(chargedCost * preparation.Settings.RestrictedCostMultiplier);
                else if (suffix == 'F' && preparation.Settings.MultiplyForbiddenCost)
                    chargedCost = checked(chargedCost * preparation.Settings.ForbiddenCostMultiplier);
                if (selection.MarkupPercent != 0m)
                    chargedCost = checked(chargedCost * (1m + selection.MarkupPercent / 100m));
                if (selection.FreeCost)
                    chargedCost = 0m;
                if (chargedCost < 0m || chargedCost > preparation.AvailableNuyen)
                    return Blocked(selection, "The exact purchase price is invalid or exceeds available Nuyen.");

                decimal essenceMultiplier = grade.EssenceMultiplier;
                if (selection.EssenceDiscountPercent != 0)
                    essenceMultiplier = checked(essenceMultiplier * (1m - selection.EssenceDiscountPercent / 100m));
                decimal installedEssence = checked(sourceEssence * Math.Max(0m, essenceMultiplier));
                if (!preparation.Settings.DoNotRoundEssenceInternally)
                    installedEssence = decimal.Round(
                        installedEssence,
                        preparation.Settings.EssenceDecimals,
                        MidpointRounding.AwayFromZero);
                int centiEssence = StandardRound(checked(installedEssence * 100m));
                int? newHole = preparation.EssenceHoleRating.HasValue
                    ? Math.Max(0, preparation.EssenceHoleRating.Value - centiEssence)
                    : null;

                var unsigned = new CharacterCyberwarePurchaseQuote(
                    Exact: true,
                    BlockReason: string.Empty,
                    selection.SourceId,
                    selection.GradeId,
                    entry.Name,
                    grade.Name,
                    selection.Rating,
                    baseCost,
                    chargedCost,
                    NuyenDelta: -chargedCost,
                    installedEssence,
                    newHole,
                    preparation.EssenceAntiHoleRating,
                    QuoteDigest: string.Empty);
                return unsigned with { QuoteDigest = ComputeQuoteDigest(preparation, selection, unsigned) };
            }
            catch (Exception exception) when (exception is OverflowException or ArgumentOutOfRangeException)
            {
                return Blocked(selection, "Cyberware purchase arithmetic exceeded the exact saved-data range.");
            }
        }

        private static char AvailabilitySuffix(string expression)
        {
            string value = expression.Trim();
            return value.EndsWith('R') ? 'R' : value.EndsWith('F') ? 'F' : '\0';
        }

        private static CharacterCyberwarePurchaseQuote Blocked(
            CharacterCyberwarePurchaseSelection selection,
            string? reason)
            => new(
                Exact: false,
                BlockReason: string.IsNullOrWhiteSpace(reason)
                    ? "Exact Cyberware purchase authority is unavailable."
                    : reason,
                selection.SourceId,
                selection.GradeId,
                Name: string.Empty,
                GradeName: string.Empty,
                selection.Rating,
                BaseCost: 0m,
                ChargedCost: 0m,
                NuyenDelta: 0m,
                InstalledEssence: 0m,
                NewEssenceHoleRating: null,
                NewEssenceAntiHoleRating: null,
                QuoteDigest: string.Empty);

        private static string ComputeQuoteDigest(
            CharacterCyberwarePurchasePreparation preparation,
            CharacterCyberwarePurchaseSelection selection,
            CharacterCyberwarePurchaseQuote quote)
        {
            string canonical = string.Join(
                "\n",
                "career-cyberware-purchase-quote-v1",
                preparation.ContentRevision.ToString(CultureInfo.InvariantCulture),
                preparation.CharacterDigest,
                preparation.CatalogDigest,
                selection.SourceId.Value.ToString("D"),
                selection.GradeId.Value.ToString("D"),
                selection.Rating.ToString(CultureInfo.InvariantCulture),
                selection.EssenceDiscountPercent.ToString(CultureInfo.InvariantCulture),
                selection.BlackMarketDiscount.ToString(CultureInfo.InvariantCulture),
                selection.MarkupPercent.ToString(CultureInfo.InvariantCulture),
                selection.FreeCost.ToString(CultureInfo.InvariantCulture),
                quote.Name,
                quote.GradeName,
                quote.BaseCost.ToString(CultureInfo.InvariantCulture),
                quote.ChargedCost.ToString(CultureInfo.InvariantCulture),
                quote.NuyenDelta.ToString(CultureInfo.InvariantCulture),
                quote.InstalledEssence.ToString(CultureInfo.InvariantCulture),
                quote.NewEssenceHoleRating?.ToString(CultureInfo.InvariantCulture) ?? "absent",
                quote.NewEssenceAntiHoleRating?.ToString(CultureInfo.InvariantCulture) ?? "absent");
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
        }

        [GeneratedRegex("^[0-9]+[RF]?$", RegexOptions.CultureInvariant)]
        private static partial Regex FixedAvailability();
    }
}

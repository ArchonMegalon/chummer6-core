using System.Globalization;
using System.Xml;
using System.Xml.Linq;

namespace Chummer.Contracts.Characters;

/// <summary>Effective profile evidence, never a client-supplied price override.</summary>
public sealed record CharacterCreationMysticAdeptPowerPointPolicy(
    string SettingsProfileId,
    string SettingsInputsDigest,
    int KarmaPerPowerPoint,
    bool PrioritySpellsAsPowerPoints,
    bool UsesSeparateMagicAttribute,
    string CanonicalSourceXml,
    string CanonicalSourceXmlDigest,
    IReadOnlyList<string> SourceAnchorIds,
    string PolicyDigest);

public sealed record CharacterCreationMysticAdeptPowerPointAllocation(
    int PowerPoints,
    int MaximumPowerPoints,
    int ExchangedSpellSlots,
    int SpellBudget,
    int KarmaCost,
    CharacterCreationMysticAdeptPowerPointPolicy Policy);

/// <summary>Chummer5's creation purchase rule: PP are capped by current MAG;
/// when enabled, priority spell slots pay first, then remaining PP cost profile Karma.
/// Separate MAGAdept requires its own attribute allocation, not this purchase rule.</summary>
public static class CharacterCreationMysticAdeptPowerPointRules
{
    public static bool TryCreatePolicy(string profileId, string settingsInputsDigest, string canonicalSourceXml,
        out CharacterCreationMysticAdeptPowerPointPolicy? policy)
    {
        policy = null;
        if (string.IsNullOrWhiteSpace(profileId)
            || !CharacterCreationMagicResonanceDigest.IsCanonical(settingsInputsDigest)
            || string.IsNullOrWhiteSpace(canonicalSourceXml) || canonicalSourceXml.Length > 262144)
            return false;
        try
        {
            using var input = new StringReader(canonicalSourceXml);
            using var reader = XmlReader.Create(input, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 262144
            });
            XDocument document = XDocument.Load(reader, LoadOptions.PreserveWhitespace);
            XElement? root = document.Root;
            if (root is null || root.Name != "setting" || root.HasAttributes
                || document.DescendantNodes().Any(node => node is XProcessingInstruction)
                || root.Descendants().Any(node => node.Name.NamespaceName.Length != 0)
                || root.ToString(SaveOptions.DisableFormatting) != canonicalSourceXml
                || !TryScalar(root, "id", out string id) || id != profileId
                || !TryScalar(root, "priorityspellsasadeptpowers", out string exchangeText)
                || !bool.TryParse(exchangeText, out bool exchange)
                || !TryScalar(root, "mysadeptsecondmagattribute", out string separateText)
                || !bool.TryParse(separateText, out bool separate))
                return false;
            XElement[] costs = root.Elements("karmacost").Take(2).ToArray();
            if (costs.Length != 1 || costs[0].HasAttributes
                || !TryScalar(costs[0], "karmamysadpp", out string karmaText)
                || !int.TryParse(karmaText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int karma)
                || karma < 0)
                return false;
            string anchor = $"settings.xml#setting:{profileId}";
            var candidate = new CharacterCreationMysticAdeptPowerPointPolicy(profileId, settingsInputsDigest,
                karma, exchange, separate, canonicalSourceXml,
                CharacterCreationMagicResonanceDigest.ComputeUtf8(canonicalSourceXml),
                [anchor + ":karmacost/karmamysadpp", anchor + ":mysadeptsecondmagattribute",
                    anchor + ":priorityspellsasadeptpowers"], string.Empty);
            policy = candidate with { PolicyDigest = ComputePolicyDigest(candidate) };
            return true;
        }
        catch (Exception exception) when (exception is XmlException or InvalidOperationException or ArgumentException)
        {
            return false;
        }
    }

    public static string ComputePolicyDigest(CharacterCreationMysticAdeptPowerPointPolicy value) =>
        CharacterCreationMagicResonanceDigest.Compute(value with { PolicyDigest = string.Empty });

    public static bool IsValidPolicy(CharacterCreationMysticAdeptPowerPointPolicy? policy) =>
        policy is not null
        && TryCreatePolicy(policy.SettingsProfileId, policy.SettingsInputsDigest, policy.CanonicalSourceXml, out var expected)
        && CharacterCreationMagicResonanceDigest.EqualsFixedTime(policy.PolicyDigest, expected!.PolicyDigest)
        && CharacterCreationMagicResonanceDigest.EqualsFixedTime(
            CharacterCreationMagicResonanceDigest.Compute(policy), CharacterCreationMagicResonanceDigest.Compute(expected));

    public static bool TryEvaluate(CharacterCreationMysticAdeptPowerPointPolicy? policy,
        string talentKind, int currentMagic, int sourceSpellBudget, int selectedPowerPoints,
        out CharacterCreationMysticAdeptPowerPointAllocation? allocation)
    {
        allocation = null;
        if (talentKind != CharacterCreationMagicResonanceKinds.MysticAdept)
            return selectedPowerPoints == 0;
        // Historical source authorities can still represent a zero-purchase draft.
        // They cannot authorize a purchase or a different price.
        if (policy is null) return selectedPowerPoints == 0;
        if (!IsValidPolicy(policy) || policy.UsesSeparateMagicAttribute
            || currentMagic < 0 || sourceSpellBudget < 0
            || selectedPowerPoints < 0 || selectedPowerPoints > currentMagic)
            return false;
        int exchange = policy.PrioritySpellsAsPowerPoints ? Math.Min(sourceSpellBudget, selectedPowerPoints) : 0;
        try
        {
            allocation = new(selectedPowerPoints, currentMagic, exchange, sourceSpellBudget - exchange,
                checked((selectedPowerPoints - exchange) * policy.KarmaPerPowerPoint), policy);
            return true;
        }
        catch (OverflowException) { return false; }
    }

    private static bool TryScalar(XElement parent, string name, out string value)
    {
        XElement[] nodes = parent.Elements(name).Take(2).ToArray();
        value = nodes.Length == 1 ? nodes[0].Value.Trim() : string.Empty;
        return nodes.Length == 1 && !nodes[0].HasAttributes && !nodes[0].HasElements
            && value.Length > 0;
    }
}

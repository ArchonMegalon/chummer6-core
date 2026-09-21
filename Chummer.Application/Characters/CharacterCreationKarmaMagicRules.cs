using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

/// <summary>Karma-specific source prices. Never synthesizes a Priority talent or free spell slots.</summary>
public static class CharacterCreationKarmaMagicRules
{
    public static string ComputePolicyDigest(CharacterCreationKarmaMagicPolicy policy) =>
        CharacterCreationMagicResonanceDigest.Compute(policy with { PolicyDigest = string.Empty });

    public static string ComputeCatalogDigest(CharacterCreationKarmaMagicCatalog catalog) =>
        CharacterCreationMagicResonanceDigest.Compute(catalog with { AuthorityDigest = string.Empty });

    public static bool TryCreatePolicy(string profileId, string settingsInputsDigest, string canonicalSourceXml,
        out CharacterCreationKarmaMagicPolicy? policy)
    {
        policy = null;
        if (!CharacterCreationMysticAdeptPowerPointRules.TryCreatePolicy(profileId, settingsInputsDigest,
                canonicalSourceXml, out var powerPoints)) return false;
        try
        {
            using var input = new StringReader(canonicalSourceXml);
            using var reader = XmlReader.Create(input, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 262144
            });
            // The shared PP reader has already checked canonical XML, namespaces,
            // profile identity and PP fields; the resolver admits the effective
            // settings digest against the captured source context.
            XElement root = XElement.Load(reader, LoadOptions.PreserveWhitespace);
            if (!TryScalar(root, "buildmethod", out string method) || method != "Karma"
                || !TryScalar(root, "ignorecomplexformlimit", out string limitText)
                || !bool.TryParse(limitText, out bool ignoreLimit)
                || root.Element("karmacost") is not { } costs
                || !TryCost(costs, "karmaspell", out int spell)
                || !TryCost(costs, "karmanewcomplexform", out int form)) return false;
            string anchor = $"settings.xml#setting:{profileId}";
            var result = new CharacterCreationKarmaMagicPolicy(CharacterCreationKarmaMagicPolicy.SchemaV1,
                profileId, settingsInputsDigest, spell, form, ignoreLimit, powerPoints!, canonicalSourceXml,
                CharacterCreationMagicResonanceDigest.ComputeUtf8(canonicalSourceXml),
                [.. powerPoints!.SourceAnchorIds, anchor + ":karmacost/karmaspell",
                    anchor + ":karmacost/karmanewcomplexform", anchor + ":ignorecomplexformlimit"], string.Empty);
            policy = result with { PolicyDigest = ComputePolicyDigest(result) };
            return true;
        }
        catch (Exception error) when (error is XmlException or InvalidOperationException or ArgumentException)
        {
            return false;
        }
    }

    public static bool IsValidPolicy(CharacterCreationKarmaMagicPolicy? policy) =>
        policy is not null
        && TryCreatePolicy(policy.SettingsProfileId, policy.SettingsInputsDigest, policy.CanonicalSourceXml,
            out var expected)
        && CharacterCreationMagicResonanceDigest.EqualsFixedTime(
            CharacterCreationMagicResonanceDigest.Compute(policy), CharacterCreationMagicResonanceDigest.Compute(expected));

    /// <summary>
    /// Quotes counts already admitted by a caller's typed selection validation.
    /// Karma has zero Priority spell slots even when that exchange house rule is on.
    /// Talent access, source identities and character limits remain caller duties.
    /// </summary>
    public static bool TryCalculateCost(CharacterCreationKarmaMagicPolicy? policy, string talentKind,
        int currentMagic, int spellCount, int complexFormCount, int selectedMysticPowerPoints,
        out CharacterCreationKarmaMagicPurchaseCost? cost)
    {
        cost = null;
        if (!IsValidPolicy(policy) || currentMagic < 0 || spellCount < 0 || complexFormCount < 0
            || talentKind is not (CharacterCreationMagicResonanceKinds.Mundane
                or CharacterCreationMagicResonanceKinds.Adept or CharacterCreationMagicResonanceKinds.Magician
                or CharacterCreationMagicResonanceKinds.MysticAdept or CharacterCreationMagicResonanceKinds.AspectedMagician
                or CharacterCreationMagicResonanceKinds.Technomancer)
            || !CharacterCreationMysticAdeptPowerPointRules.TryEvaluate(policy!.PowerPointPolicy, talentKind,
                currentMagic, sourceSpellBudget: 0, selectedMysticPowerPoints, out var powerPoints)) return false;
        try
        {
            int spells = checked(spellCount * policy.KarmaPerSpell);
            int forms = checked(complexFormCount * policy.KarmaPerComplexForm);
            cost = new(spellCount, complexFormCount, spells, forms, powerPoints,
                checked(spells + forms + (powerPoints?.KarmaCost ?? 0)), policy.PolicyDigest);
            return true;
        }
        catch (OverflowException) { return false; }
    }

    private static bool TryCost(XElement root, string name, out int value)
    {
        value = 0;
        return TryScalar(root, name, out string text)
            && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) && value >= 0;
    }

    private static bool TryScalar(XElement root, string name, out string value)
    {
        XElement[] nodes = root.Elements(name).Take(2).ToArray();
        value = nodes.Length == 1 ? nodes[0].Value.Trim() : string.Empty;
        return nodes.Length == 1 && !nodes[0].HasAttributes && !nodes[0].HasElements && value.Length > 0;
    }
}

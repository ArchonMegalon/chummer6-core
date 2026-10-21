using System.Globalization;
using System.Xml.Linq;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

internal static class CharacterCreationPhysicalInitiative886
{
    internal const string Blocker = "physical-initiative-graph-unresolved";

    internal static bool Matches(CharacterCreationPhysicalInitiativeAuthority? authority, CharacterCreationPrerequisiteDraft draft)
        => authority is { RulesetId: "sr5", Semantics: CharacterCreationPhysicalInitiativeAuthority.SemanticsV1, Metatype: not null }
            && draft.HeritageSelection is { MetavariantSourceId: null } heritage
            && CharacterCreationFinalizationDigest.IsCanonical(authority.ProfileInputsDigest)
            && CharacterCreationFinalizationDigest.IsCanonical(authority.MetatypesInputsDigest)
            && CharacterCreationFinalizationDigest.IsCanonical(authority.MetatypeSourceNodeDigest)
            && !string.IsNullOrEmpty(authority.MetatypeObligationId)
            && authority.PrerequisiteDraftDigest == draft.DraftDigest
            && authority.SettingsProfileId == draft.SettingsProfileId
            && authority.MetatypeSourceId == heritage.MetatypeSourceId
            && authority.MetatypeSourceNodeDigest == heritage.MetatypeSourceNodeDigest
            && authority.Metatype.Minimum >= 0 && authority.Metatype.Minimum <= authority.Metatype.Maximum
            && authority.Metatype.Maximum <= authority.Metatype.AugmentedMaximum
            && authority.Metatype.MinimumDiceFallback is >= 0 and <= 99 && authority.MaximumDice >= 0;

    internal static bool TryInitialize(XElement root, CharacterCreationPrerequisiteDraft draft,
        CharacterCreationPhysicalInitiativeAuthority? authority)
    {
        if (!Matches(authority, draft) || !bool.TryParse(Scalar(root, "created"), out bool created) || created) return false;
        // Creation initializes the persisted base exactly once; a preexisting operative
        // scalar must agree. Never overwrite an imported choice or store improved dice.
        var saved = root.Elements().Where(n => n.Name.LocalName == "initiativedice").ToArray();
        if (saved.Length > 1 || saved.Length == 1 && (saved[0].Name != "initiativedice"
            || Integer(root, "initiativedice") != authority!.BaseDice)) return false;
        if (saved.Length == 0) root.Add(new XElement("initiativedice", authority!.BaseDice));
        return true;
    }

    internal static CharacterPhysicalInitiativeProjection Evaluate(XElement root,
        CharacterCreationPhysicalInitiativeAuthority authority)
    {
        if (root.Name != "character" || authority.RulesetId != "sr5" || authority.Semantics != CharacterCreationPhysicalInitiativeAuthority.SemanticsV1
            || !bool.TryParse(Scalar(root, "created"), out bool created)
            || Integer(root, "initiativedice") != authority.BaseDice) throw Invalid();
        var improvements = One(root, "improvements") ?? throw Invalid();
        if (improvements.HasAttributes || improvements.Elements().Any(n => n.Name != "improvement")) throw Invalid();
        foreach (var row in improvements.Elements())
        {
            string type = Scalar(row, "improvementttype");
            // Explicit admitted channels only. Attribute/CM/augmentation/suppression
            // shapes need their complete consumer; unknown types never become zero.
            if (type is not ("Initiative" or "InitiativeDice" or "InitiativeDiceAdd"
                    or "Ambidextrous" or "FriendsInHighPlaces" or "Erased" or "Overclocker"
                    or "LifestyleCost" or "BasicLifestyleCost" or "TrustFund"
                    or "StreetCredMultiplier" or "StreetCred" or "Notoriety" or "PublicAwareness")
                && !CharacterCreationSkillKarmaModifierRules.IsSupportedType(type)) throw Invalid();
        }
        foreach (string name in new[] { "cyberwares", "biowares", "drugs", "armors" })
            if (One(root, name)?.HasElements == true) throw Invalid();
        if (!bool.TryParse(Scalar(root, "ai", "False"), out bool ai) || ai) throw Invalid();
        int reaction = Attribute(root, "REA"), intuition = Attribute(root, "INT");
        int physical = Integer(root, "physicalcmfilled", 0), stun = Integer(root, "stuncmfilled", 0);
        if (physical < 0 || stun < 0) throw Invalid();
        // Default unmodified condition monitors. Unsupported threshold/monitor effects
        // are refused above; the source authority refuses encumbrance wound profiles.
        int physicalMaximum = checked(8 + (Attribute(root, "BOD") + 1) / 2);
        int stunMaximum = checked(8 + (Attribute(root, "WIL") + 1) / 2);
        int wound = -Math.Min(physical, physicalMaximum) / 3 - Math.Min(stun, stunMaximum) / 3;
        int initiative = Channel(root, "Initiative", created);
        int dice = Channel(root, "InitiativeDice", created);
        int additive = Channel(root, "InitiativeDiceAdd", created);
        return new(Math.Max(0, checked(reaction + intuition + wound + initiative)),
            Math.Min(authority.MaximumDice, checked(authority.BaseDice + dice + additive)),
            authority.BaseDice, reaction, intuition, wound, initiative, dice, additive, created,
            CharacterCreationFinalizationDigest.Compute(authority),
            CharacterCreationFinalizationDigest.Compute(improvements.ToString(SaveOptions.DisableFormatting)));
    }

    private static int Channel(XElement root, string type, bool created)
    {
        decimal value = CharacterCareerReputationProjector.ResolvePhysicalChannel886(root, type, created);
        return decimal.ToInt32(value >= 0 ? decimal.Ceiling(value) : decimal.Floor(value));
    }

    private static int Attribute(XElement root, string name)
    {
        var container = One(root, "attributes") ?? throw Invalid();
        var matches = container.Elements("attribute").Where(n => Scalar(n, "name") == name).ToArray();
        if (matches is not [var row]) throw Invalid();
        int value = Integer(row, "totalvalue");
        if (value < 0 || value > Integer(row, "metatypeaugmax")) throw Invalid();
        return value;
    }

    private static XElement? One(XElement root, string name)
    {
        var nodes = root.Elements().Where(n => n.Name.LocalName == name).ToArray();
        if (nodes.Length > 1 || nodes.Length == 1 && nodes[0].Name != name) throw Invalid();
        return nodes.SingleOrDefault();
    }
    private static string Scalar(XElement root, string name, string? absent = null)
    {
        var node = One(root, name);
        if (node is null) return absent ?? throw Invalid();
        if (node.HasAttributes || node.HasElements || node.Nodes().OfType<XProcessingInstruction>().Any()) throw Invalid();
        return node.Value;
    }
    private static int Integer(XElement root, string name, int? absent = null)
        => int.TryParse(Scalar(root, name, absent?.ToString(CultureInfo.InvariantCulture)), NumberStyles.Integer,
            CultureInfo.InvariantCulture, out int value) ? value : throw Invalid();
    private static InvalidDataException Invalid() => new(Blocker);
}

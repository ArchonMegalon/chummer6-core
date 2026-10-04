using Chummer.Contracts.Characters;
using System.Xml;
using System.Xml.Linq;

namespace Chummer.Application.Characters;

public static class CharacterCreationLifeModuleTalentAuthority
{
    public static bool TryProjectUnlockChoices(CharacterCreationKarmaTalentOption option, out IReadOnlyList<string> choices)
    {
        choices = [];
        if (option.OptionId == CharacterCreationKarmaTalentCatalog.MundaneOptionId) return true;
        try
        {
            if (option.SourceNodeXml is not { Length: > 0 and <= 32 * 1024 }) return false;
            using var text = new StringReader(option.SourceNodeXml);
            using var reader = XmlReader.Create(text, new XmlReaderSettings
                { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 32 * 1024 });
            XElement[] unlocks = XElement.Load(reader).Element("bonus")?.Elements("unlockskills").ToArray() ?? [];
            if (unlocks.Length == 0) return true;
            string[]? prompt = null;
            foreach (var unlock in unlocks)
            {
                string[] values = unlock.Value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                if (values.Length == 0 || values.Any(value =>
                    !CharacterCreationSkillsAccessRules.TryChooseUnlock(unlock, [value], string.Empty, out var actual) || actual != value))
                    return false;
                // Separate effects are cumulative grants, not alternatives. Apprentice
                // unlocks both Sorcery and Conjuring without asking the player to pick one.
                // Preserve the historical single fixed-unlock catalog shape.
                if (unlocks.Length == 1) { choices = values; return true; }
                if (values.Length == 1) continue;
                if (prompt is not null) return false; // One typed chooser cannot answer two prompts.
                prompt = values;
            }
            choices = prompt ?? [];
            return true;
        }
        catch (XmlException) { return false; }
    }

    public static string ComputeDigest(CharacterCreationLifeModuleTalentCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        return CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(
            catalog with { AuthorityDigest = string.Empty });
    }
}

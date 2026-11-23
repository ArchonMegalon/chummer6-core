using System.Xml.Linq;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

internal static class CharacterCreationIdentityEvaluator
{
    // Existing runner identity is read-only in completion. Compare whole XML
    // elements (including attributes, markup, multiplicity and order), rather
    // than normalizing or inventing personal facts. Mechanical creation fields
    // and equipment such as fake SINs have their own evaluated consumers.
    private static readonly string[] fields =
    [
        "guid", "name", "alias", "playername", "age", "sex", "gender", "ethnicity",
        "height", "weight", "skin", "eyes", "hair", "description", "background", "concept",
        "notes", "gamenotes", "groupnotes", "gmnotes", "mugshots", "mainmugshotindex"
    ];
    private static readonly HashSet<string> fieldSet = new(fields, StringComparer.OrdinalIgnoreCase);

    internal static CharacterCreationIdentityEvaluation Evaluate(CharacterCreationCompleteAssessment authority,
        XElement original, XElement evaluated, string compilationDigest)
    {
        string OriginalDigest(XElement graph) => CharacterCreationFinalizationDigest.Compute(new
        {
            Schema = CharacterCreationIdentityEvaluation.SchemaV1,
            Fields = fields,
            Elements = graph.Elements().Where(row => fieldSet.Contains(row.Name.LocalName))
                .Select(row => row.ToString(SaveOptions.DisableFormatting)).ToArray()
        });
        string before = OriginalDigest(original), after = OriginalDigest(evaluated);
        string[] blockers = original.Name != "character" || evaluated.Name != "character"
            ? [CharacterCreationIdentityEvaluation.InvalidGraph]
            : before != after ? [CharacterCreationIdentityEvaluation.Changed] : [];
        return new(CharacterCreationIdentityEvaluation.SchemaV1, authority.InputsSnapshotDigest,
            authority.RequestDigest, authority.RawCharacterXmlDigest, compilationDigest, before, after, blockers);
    }
}

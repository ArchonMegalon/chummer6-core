using System.Xml.Linq;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

public static partial class CharacterCareerInitiationProjection
{
    // A current-state read proof, never a reconstructed purchase receipt or write admission.
    internal sealed record CurrentGradeOwner5719(Guid Id, Guid SourceId, string SavedXml,
        string SourceXml, IReadOnlyList<string> Effects);
    internal sealed record CurrentGrades5719(bool Resonance, int Grade, string SourceDigest,
        IReadOnlyList<string> Caps, IReadOnlyList<CurrentGradeOwner5719> Owners);

    internal static CurrentGrades5719 ReadCurrentGrades5719(XElement root, XElement settings,
        ICharacterSourceDataContext context)
    {
        void Need(bool valid, string reason)
        { if (!valid) throw new InvalidDataException("current-grade-" + reason); }
        Need(Boolean(root, "created") && !Boolean(root, "ignorerules", false), "career-required");
        bool resonance = Boolean(root, "resenabled", false);
        Need(resonance != Boolean(root, "magenabled", false), "talent-conflict");
        Need(IntegerOrZero("initiategrade") == 0 || !resonance, "mixed-history");
        Need(IntegerOrZero("submersiongrade") == 0 || resonance, "mixed-history");
        // There is no selected-Art source authority in this typed catalog. Do not
        // turn its names into prerequisites or silently drop its source/bonus fields.
        Need(!Container(root, "arts").HasElements && string.IsNullOrWhiteSpace(Container(root, "arts").Value),
            "art-source-authority-unavailable");
        foreach (string name in new[] { "initiationgrades", "metamagics" })
            Need(Container(root, name).Nodes().All(n => n is XElement || n is XText t && string.IsNullOrWhiteSpace(t.Value)),
                "container-shape-unsupported");
        int grade = resonance ? CharacterCareerResonanceEvaluator.ValidateSubmersionState(root)
            : CharacterCareerMagicEvaluator.ValidateInitiationState(root);
        Need(context is ICharacterCareerInitiationSourceContext, "source-capability-unavailable");
        Need(((ICharacterCareerInitiationSourceContext)context).TryResolveCareerInitiationSources(out var sources)
            && sources is not null && !string.IsNullOrWhiteSpace(sources.AuthorityState), "sources-unavailable");
        Need(XNode.DeepEquals(Parse(sources!.SettingsXml, "settings"), settings), "settings-mismatch");
        string digest = CharacterCareerInitiationRules.Digest(sources);
        var selected = Container(root, "metamagics").Elements().ToArray();
        var result = new List<CurrentGradeOwner5719>();
        if (resonance)
        {
            // Existing Core authenticates the ordinary Echo vocabulary, identities,
            // grade occupancy and source limits. Unsupported Echo effects still refuse.
            Need(grade > 0 && CharacterCareerResonanceEvaluator.AuthenticateEchoHistory(root, context, grade) == digest,
                "echo-source-mismatch");
            foreach (var owned in selected)
            {
                Guid sourceId = Guid.Parse(Scalar(owned, "sourceid"));
                var row = sources.Echoes!.Single(x => Guid.Parse(Scalar(Parse(x.Xml, "echo"), "id")) == sourceId);
                result.Add(new(Guid.Parse(Scalar(owned, "guid")), sourceId,
                    owned.ToString(SaveOptions.DisableFormatting), row.Xml, Array.Empty<string>()));
            }
        }
        else
        {
            Need(grade > 0, "nonzero-history-required");
            // Validate native historical choices, without repricing them against
            // today's group membership, discounts, training or settings.
            foreach (var row in Container(root, "initiationgrades").Elements())
            {
                Need(OnlyScalars(row, "guid", "res", "grade", "group", "ordeal", "schooling", "notes", "notesColor")
                    && !Boolean(row, "res"), "grade-shape-unsupported");
                _ = Boolean(row, "group"); _ = Boolean(row, "ordeal"); _ = Boolean(row, "schooling");
            }
            Need(selected.Length == grade && selected.All(x => x.Name == "metamagic")
                && selected.Select(x => Integer(x, "grade")).Order().SequenceEqual(Enumerable.Range(1, grade)),
                "benefit-history-unresolved");
            Need(Container(root, "spells").Elements().All(x => Number(x, "grade", 0) <= 0),
                "spell-grade-benefit-unconsumed");
            var benefits = sources.Metamagics.Select(x => Benefit(x, root, Boolean(root, "adept", false),
                Boolean(root, "magician", false), grade - 1, Boolean(settings, "ignoreart"),
                Boolean(settings, "careerinitiationpowerpointenabled", true), adding: false)).ToArray();
            Need(benefits.Length <= 4096 && benefits.Select(b => b.SourceId).Distinct().Count() == benefits.Length,
                "metamagic-catalog-invalid");
            var blockers = new List<string>();
            // Same source validator/effect compiler as an existing grade transition,
            // with the current grade on both sides: no hypothetical purchase is made.
            _ = RecompileOwnedMetamagics(root, benefits, grade, grade, blockers);
            Need(blockers.Count == 0, "metamagic-source-or-effect-unavailable");
            foreach (var owned in selected)
            {
                Guid id = Guid.Parse(Scalar(owned, "guid")), sourceId = Guid.Parse(Scalar(owned, "sourceid"));
                Need(id != Guid.Empty && root.Descendants("guid").Count(x => Guid.TryParse(x.Value, out var other) && other == id) == 1,
                    "owner-identity-invalid");
                Need(!owned.HasAttributes && owned.Elements().GroupBy(x => x.Name).All(g => g.Count() == 1)
                    && owned.Nodes().All(n => n is XElement || n is XText t && string.IsNullOrWhiteSpace(t.Value))
                    && owned.Elements().All(x => x.Name == "bonus" || OnlyScalars(new XElement("fields", new XElement(x)),
                        "guid", "sourceid", "name", "source", "page", "grade", "improvementsource",
                        "paidwithkarma", "extra", "forced", "notes", "notesColor")), "owner-shape-unsupported");
                var benefit = benefits.Single(b => b.SourceId == sourceId);
                Need(!benefit.Blockers.Any(x => x is "initiation_benefit_duplicate"
                    or "initiation_benefit_limit_unsupported" or "initiation_prerequisite_unsupported"),
                    "metamagic-limit-or-prerequisite-unsupported");
                // PP grants need the separate shared pool/grant proof. This adapter
                // never hides that effect by substituting an empty modifier array.
                Need(benefit.Kind == CharacterCareerInitiationBenefitKind.Metamagic, "power-point-proof-unavailable");
                Need(!Boolean(owned, "paidwithkarma") && Scalar(owned, "page") == benefit.Page,
                    "benefit-history-unsupported");
                Need(TryCompileMetamagicEffects(Parse(benefit.SourceXml, "metamagic"), id, grade, out var effects),
                    "metamagic-effect-unsupported");
                result.Add(new(id, sourceId, owned.ToString(SaveOptions.DisableFormatting), benefit.SourceXml,
                    effects.Select(x => x.ToString(SaveOptions.DisableFormatting)).ToArray()));
            }
        }
        string kind = resonance ? "Submersion" : "Initiation";
        var caps = Container(root, "improvements").Elements()
            .Where(x => Scalar(x, "improvementsource", "") == kind)
            .Select(x => x.ToString(SaveOptions.DisableFormatting)).ToArray();
        // Caps have already passed the existing exact history/cap validators.
        return new(resonance, grade, digest, caps, result.AsReadOnly());

        int IntegerOrZero(string name) => int.Parse(Scalar(root, name, "0"), Invariant);
    }

    private static bool OnlyScalars(XElement row, params string[] names)
        => !row.HasAttributes && row.Nodes().All(n => n is XElement || n is XText t && string.IsNullOrWhiteSpace(t.Value))
            && row.Elements().GroupBy(x => x.Name).All(g => g.Count() == 1)
            && row.Elements().All(x => x.Name.Namespace == XNamespace.None && names.Contains(x.Name.LocalName)
                && !x.HasAttributes && !x.HasElements);
}

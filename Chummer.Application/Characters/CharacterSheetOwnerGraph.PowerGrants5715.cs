using System.Xml.Linq;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

internal sealed partial class CharacterSheetOwnerGraph
{
    // Calculation proof from this sheet's source operation, never a write capability.
    internal CharacterCareerPowerGraph.ReadGraph? CareerPowers5715 { get; private set; }
    private readonly HashSet<CharacterCareerPowerGraph.ReadGrant5715> consumedPowerGrants5715 = [];
    private readonly HashSet<Guid> selectedMentors5715 = [];

    private void BindImportedPowerRead5715(CharacterCreationPriorityGrantAuthority3563? priority)
    {
        if (priority?.IsImported5693 != true || !Flag(root, "created") || !Flag(root, "adept", false)) return;
        bool selectedGrant = (Node(root, "improvements")?.Elements() ?? []).Any(e =>
            Text(e, "improvementttype", "") is "AdeptPowerFreeLevels" or "AdeptPowerFreePoints" or "AdeptPowerPoints");
        bool savedGrant = (Node(root, "powers")?.Elements() ?? []).Any(p =>
            Dec(p, "freepoints", 0) != 0 || Int(p, "freelevels", 0) != 0);
        if (!selectedGrant && !savedGrant && Node(root, "mentorspirits")?.HasElements != true) return;
        // Grade/history and focus/spirit grants remain separate authority domains.
        Need(Int(root, "initiategrade", 0) == 0 && Node(root, "metamagics")?.HasElements != true,
            "power-grant-grade-history-unconsumed", "powers");
        var read = CharacterCareerPowerGraph.EvaluateSheetRead5715(root.ToString(SaveOptions.DisableFormatting), context, finalizedModules);
        Need(read.Blockers.Count == 0 && read.RemainingPoints is not null
            && read.ProvenGrants.All(g => g.Grant.Kind is "Quality" or "MentorSpirit"),
            "power-grant-kernel-unavailable", "powers");
        Need(read.ProvenGrants.Where(g => g.Grant.PowerName.Length > 0).All(g => read.ProvenPowers.Count(p =>
            Text(Parse(p.SourceXml), "name") == g.Grant.PowerName && p.Identity.Extra == g.Grant.Extra) == 1),
            "power-grant-target-unavailable", "powers");
        CareerPowers5715 = read;
        string digest = PowerReadDigest5715(read);
        sourceEvidence.Add(digest);
        checks.Add(() => PowerReadDigest5715(CharacterCareerPowerGraph.EvaluateSheetRead5715(
            root.ToString(SaveOptions.DisableFormatting), context, finalizedModules)) == digest);
    }

    private static string PowerReadDigest5715(CharacterCareerPowerGraph.ReadGraph read)
        => CharacterCreationFinalizationDigest.Compute(new { Read = read, read.ProvenPowers, read.ProvenGrants });

    private int PowerTotalRating5715(XElement saved, XElement source, int paid, string path)
    {
        decimal freePoints = Dec(saved, "freepoints", 0);
        int freeLevels = Int(saved, "freelevels", 0);
        if (CareerPowers5715 is not { } read)
        {
            Need(freePoints == 0 && freeLevels == 0, "power-free-grant-unconsumed", path);
            return paid;
        }
        var matches = read.ProvenPowers.Where(p => p.Identity.InternalId == Id(saved, "guid")
            && p.Identity.SourceId == Id(saved, "sourceid") && p.Identity.Extra == Text(saved, "extra", "")).ToArray();
        Need(matches.Length == 1, "power-grant-owner-unavailable", path);
        var proof = matches[0];
        Need(XNode.DeepEquals(Parse(proof.SourceXml), source) && proof.Cost.PaidRating == paid,
            "power-grant-source-mismatch", path);
        // Native Save retains a legacy freepoints field (normally zero); computed
        // FreePoints/FreeLevels come from improvements. A nonzero imported cache
        // must agree with the kernel, never replace or add to its grant inputs.
        Need((freePoints == 0 || freePoints == proof.Cost.FreePoints)
            && (freeLevels == 0 || freeLevels == proof.Cost.FreeLevels),
            "power-grant-saved-cache-mismatch", path);
        return proof.Cost.TotalRating;
    }

    private void ReadImportedMentors5715()
    {
        var container = Node(root, "mentorspirits");
        if (container is null) return;
        Need(!container.HasAttributes && container.Nodes().All(n => n is XElement
            || n is XText text && string.IsNullOrWhiteSpace(text.Value)), "mentor-container-shape", "mentorspirits");
        if (!container.HasElements) return;
        Need(container.Elements().Count() == 1, "mentor-grant-proof-unavailable", "mentorspirits");
        var saved = container.Elements().Single();
        const string path = "mentorspirits/0";
        Need(saved.Name == "mentorspirit" && !saved.HasAttributes && owners.Count < 4096, "owner-shape", path);
        Guid id = Id(saved, "guid"), sourceId = Id(saved, "sourceid");
        Need(instances.Add(id), "duplicate-owner-instance", path);
        XElement source;
        if (CareerPowers5715 is { } read)
        {
            var grants = read.ProvenGrants.Where(g => g.Grant.Kind == "MentorSpirit"
                && g.Grant.InstanceId == id && g.Grant.SourceId == sourceId).ToArray();
            Need(grants.Length > 0 && grants.Select(g => g.SourceXml).Distinct().Count() == 1,
                "mentor-grant-source-unavailable", path);
            source = Parse(grants[0].SourceXml);
        }
        else
        {
            // Ordinary magician mentors have no adept-power grant to prove. Resolve
            // the canonical source through the existing mentor grant resolver, which
            // owns enabled books/amends and source drift. Reconcile the selecting
            // quality and every saved effect below; capture the complete receipt.
            Need(Flag(root, "created") && Flag(root, "magenabled", false) && Flag(root, "magician", false)
                && !Flag(root, "adept", false) && Int(root, "initiategrade", 0) == 0
                && Node(root, "metamagics")?.HasElements != true
                && !(Node(root, "improvements")?.Elements() ?? []).Any(e =>
                    Text(e, "improvementttype", "") == "DisableQuality" && Flag(e, "enabled")),
                "mentor-ordinary-context-unconsumed", path);
            Need(context.TryResolveCareerPowerGrantSource("MentorSpirit", sourceId, out var resolved) && resolved is not null,
                "mentor-grant-source-unavailable", path);
            var receipt = resolved!;
            Need(receipt.Kind == "MentorSpirit" && receipt.SourceId == sourceId
                && !string.IsNullOrWhiteSpace(receipt.AuthorityState), "mentor-grant-source-identity", path);
            sourceEvidence.Add(CharacterCreationFinalizationDigest.Compute(receipt));
            checks.Add(() => context.TryResolveCareerPowerGrantSource("MentorSpirit", sourceId, out var final) && final == receipt);
            source = Parse(receipt.RawSourceXml);
            Need(Id(source, "id") == sourceId, "mentor-grant-source-identity", path);
        }
        Need(source.Name == "mentor" && !source.HasAttributes
            && source.Elements().GroupBy(e => e.Name).All(g => g.Count() == 1)
            && source.Nodes().All(n => n is XElement || n is XText text && string.IsNullOrWhiteSpace(text.Value)),
            "mentor-source-shape", path);
        string[] metadata = ["id", "name", "translate", "source", "page", "altpage", "advantage", "disadvantage",
            "notes", "altnotes", "notesColor", "nameonpage", "implemented"];
        Need(source.Elements().All(e => e.Name.Namespace == XNamespace.None && !e.HasAttributes
            && (metadata.Contains(e.Name.LocalName) && !e.HasElements || e.Name == "bonus" || e.Name == "choices"
                || e.Name == "required" || e.Name == "forbidden")), "mentor-source-field-unconsumed", path);
        Need(Text(saved, "mentortype", "MentorSpirit") == "MentorSpirit" && !Flag(saved, "mentormask", false)
            && Text(saved, "name") == Text(source, "name") && Text(saved, "source") == Text(source, "source"),
            "mentor-saved-mechanic-unconsumed", path);
        var owner = new Owner("MentorSpirit", id, sourceId, saved, source, 1, Text(saved, "extra", ""), true, path);
        ValidateSavedMechanics(owner);
        // The shared policy resolves both legacy named and native bonus choices.
        _ = CharacterCareerPowerGraph.SelectedGrantBonuses5715(owner.Kind, saved, source);
        owners.Add(owner);
    }

    private void CompileMentorChoices5715(Owner owner)
    {
        foreach (var choice in CharacterCareerPowerGraph.SelectedGrantBonuses5715(owner.Kind, owner.Saved, owner.Source).Skip(1))
        {
            Need(choice.Extra.Length == 0, "mentor-choice-prompt-unconsumed", owner.Path);
            var selected = owner with { Extra = choice.Extra, Path = owner.Path + "/" + choice.Slot };
            CompilePowerGrantContainer5715(selected, choice.Bonus, owner.Enabled, "");
        }
    }

    private void CompilePowerGrantContainer5715(Owner owner, XElement? container, bool enabled, string suffix)
    {
        if (container is null || !container.Elements().Any(PowerGrantClause5715))
        { CompileContainer(owner, container, enabled, suffix); return; }
        var read = CareerPowers5715;
        // Without the adept kernel only the ordinary mentor-selection join is admitted.
        Need(read is not null || container.Elements().Where(PowerGrantClause5715).All(c => c.Name == "selectmentorspirit"),
            "power-grant-proof-unavailable", owner.Path);
        Need(owner.Kind is "Quality" or "MentorSpirit" && enabled && suffix.Length == 0
            && container.Attributes().All(a => a.Name == "useselected" && bool.TryParse(a.Value, out _))
            && container.Nodes().All(n => n is XElement || n is XText text && string.IsNullOrWhiteSpace(text.Value)),
            "power-grant-context-unconsumed", owner.Path);
        // Dispatch every clause exactly once. The complete original source/saved
        // nodes remain in the owner graph and digest; unsupported siblings still fail.
        var ordinary = new XElement(container);
        ordinary.Elements().Where(PowerGrantClause5715).Remove();
        CompileContainer(owner, ordinary, enabled, suffix);
        foreach (var clause in container.Elements().Where(PowerGrantClause5715))
        {
            if (clause.Name == "selectmentorspirit")
            {
                Need(owner.Kind == "Quality" && !clause.HasAttributes && !clause.HasElements
                    && string.IsNullOrWhiteSpace(clause.Value), "mentor-selection-source-shape", owner.Path);
                var mentors = owners.Where(o => o.Kind == "MentorSpirit" && Text(o.Source, "name") == owner.Extra).ToArray();
                Need(mentors.Length == 1 && selectedMentors5715.Add(mentors[0].Id), "mentor-selection-owner-unavailable", owner.Path);
                var mentor = mentors[0];
                expected.Add(Improvement(owner, "", true, "MentorSpirit", mentor.Id.ToString("D"),
                    0, 0, 0, 0, 0, false, "", 1, mentor.SourceId.ToString("D"), ""));
                boundChoices.Add(owner.Id);
                continue;
            }
            var matches = read!.ProvenGrants.Where(g => g.Grant.Kind == owner.Kind && g.Grant.InstanceId == owner.Id
                && g.Grant.SourceId == owner.SourceId && XNode.DeepEquals(Parse(g.SourceXml), owner.Source)
                && XNode.DeepEquals(Parse(g.ClauseXml), clause)).ToArray();
            Need(matches.Length == 1 && consumedPowerGrants5715.Add(matches[0]), "power-grant-source-clause-unproved", owner.Path);
            var proof = matches[0];
            expected.Add(Improvement(owner, "", true, proof.Type, proof.Grant.PowerName, proof.Grant.PoolPoints,
                0, 0, 0, 0, false, "", proof.Rating, proof.Grant.Extra, ""));
        }
    }

    private static bool PowerGrantClause5715(XElement clause)
        => clause.Name == "specificpower" || clause.Name == "adeptpowerpoints" || clause.Name == "selectmentorspirit";
}

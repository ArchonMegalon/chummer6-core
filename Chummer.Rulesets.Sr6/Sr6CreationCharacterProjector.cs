using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;

namespace Chummer.Rulesets.Sr6;

/// <summary>
/// Builds character content from the revalidated saved ledger, never from a
/// client-supplied preview. Does not write, discard creation points or set
/// created=true. Native attribute ratings and permanent effects stay separate.
/// </summary>
public static class Sr6CreationCharacterProjector
{
    public static CharacterCreationFoundationResult<Sr6CreationCharacterProjection> Project(WorkspaceStoredDocument saved)
    {
        var loaded = Sr6CreationFoundationRules.Load(saved);
        if (loaded.Value is not { Selection: { } selection, DraftSummary: { } summary } state)
            return new(CharacterCreationFoundationOutcomes.Blocked, null,
                loaded.Blockers.Count > 0 ? loaded.Blockers : ["sr6-character-foundation-required"]);

        XElement root = XDocument.Parse(saved.Document.Content).Root!;
        // This is a copy, not a replacement for the pending document. Its
        // bootstrap marker must not attest the newly materialized content.
        root.Elements(CharacterCreationBootstrapXml.MarkerElement).Remove();
        Set("created", false);
        Set("metatype", selection.Selection.MetatypeId switch
        { "human" => "Human", "elf" => "Elf", "dwarf" => "Dwarf", "ork" => "Ork", "troll" => "Troll", _ => throw new InvalidOperationException() });
        Set("prioritytalent", selection.Selection.TalentId);
        Set("magenabled", selection.BaseMagic > 0);
        Set("resenabled", selection.BaseResonance > 0);
        Set("adept", selection.Selection.TalentId is "adept" or "mystic-adept");
        Set("magician", selection.Selection.TalentId is "magician" or "aspected-magician" or "mystic-adept");
        Set("technomancer", selection.Selection.TalentId == "technomancer");
        foreach (var assignment in selection.Selection.Assignments)
            Set(assignment.CategoryId switch
            {
                CharacterCreationPriorityCategoryIds.Heritage => "prioritymetatype",
                CharacterCreationPriorityCategoryIds.Talent => "priorityspecial",
                CharacterCreationPriorityCategoryIds.Attributes => "priorityattributes",
                CharacterCreationPriorityCategoryIds.Skills => "priorityskills",
                CharacterCreationPriorityCategoryIds.Resources => "priorityresources",
                _ => throw new InvalidOperationException("Unknown SR6 priority category.")
            }, assignment.Rank);

        var incomplete = summary.Steps.Where(step => step.Status is Sr6CreationDraftStepStatuses.Missing
            or Sr6CreationDraftStepStatuses.Waiting).Select(step => step.Id).ToList();
        incomplete.Add("finalization-transaction");
        var projection = new XElement("sr6creationprojection",
            new XAttribute("stage", "materialized-draft"),
            new XElement("foundationpreviewdigest", selection.PreviewDigest),
            new XElement("budget", new XElement("attributes", selection.Budget.AttributePoints),
                new XElement("skills", selection.Budget.SkillPoints), new XElement("resources", selection.Budget.ResourcesNuyen),
                new XElement("adjustment", selection.Budget.MetatypeAdjustmentPoints)),
            selection.PointBuy is { } cp ? new XElement("characterpoints", new XAttribute("spent", cp.PointsSpent),
                new XAttribute("remaining", cp.PointsRemaining)) : null,
            new XElement("aspectedskill", selection.Selection.Skills?.AspectedSkillId),
            new XElement("unspent", summary.Steps.SelectMany(step => step.Remainders.Select(remainder =>
                new XElement("pool", new XAttribute("step", step.Id), new XAttribute("id", remainder.Id), remainder.Amount)))));
        root.Add(projection);

        if (summary.NaturalValues?.Attributes is { } attributes)
            Replace(new XElement("attributes", attributes.Select(row => new XElement("attribute",
                new XElement("name", AttributeCode(row.AttributeId)),
                new XElement("sr6id", row.AttributeId),
                new XElement("base", row.BaseValue + row.AttributePoints + row.AdjustmentPoints),
                new XElement("karma", row.KarmaIncrease), new XElement("totalvalue", row.Rating),
                new XElement("metatypemax", row.Maximum),
                new XElement("sr6basevalue", row.BaseValue), new XElement("sr6attributepoints", row.AttributePoints),
                new XElement("sr6adjustmentpoints", row.AdjustmentPoints)))));

        var skills = new XElement("skills");
        if (summary.NaturalValues?.Skills is { } active)
            foreach (var row in active.Where(row => row.Available || row.Rating > 0))
                skills.Add(new XElement("skill", Identity("skill", row.SkillId),
                    new XElement("suid", "sr6.skill." + row.SkillId), new XElement("name", row.SkillId),
                    new XElement("isknowledge", false), new XElement("skillcategory", "SR6 Active"),
                    new XElement("base", row.PoolRating), new XElement("karma", row.KarmaIncrease),
                    new XElement("specs", row.Specializations.Select(spec => new XElement("spec",
                        new XElement("name", spec.Subject), new XElement("sr6dicebonus", spec.DicePoolBonus))))));
        if (summary.NaturalValues?.Knowledge is { } knowledge)
        {
            skills.Add(Language(StableId("native-language", "native"), knowledge.NativeLanguage, "Native", null));
            foreach (var row in knowledge.Languages)
                skills.Add(Language(row.Id, row.Name, row.Level, row.ComprehensionBonus));
            foreach (var row in knowledge.KnowledgeSkills)
                skills.Add(new XElement("skill", new XElement("guid", row.Id.ToString("D")), new XElement("name", row.Name),
                    new XElement("isknowledge", true), new XElement("skillcategory", "SR6 Knowledge"),
                    new XElement("sr6unrated", true)));
        }
        if (summary.NaturalValues is { Skills: not null } or { Knowledge: not null })
            Replace(new XElement("newskills", skills));

        Replace(new XElement("qualities", (selection.Qualities?.Values ?? []).Select(row => new XElement("quality",
            Identity("quality", row.Id), new XElement("name", row.Name), new XElement("sr6id", row.Id),
            new XElement("source", row.SourceAnchorId), new XElement("sr6karmacost", row.KarmaCost),
            new XElement("sr6family", row.FamilyId), new XElement("extra", row.AttributeId ?? row.SkillId),
            row.Rating is { } rating ? new XElement("sr6rating", new XAttribute("total", rating.Total),
                new XAttribute("innate", rating.Innate), new XAttribute("purchased", rating.Purchased)) : null))));
        // Source identities preserve innate traits without synthesizing purchased
        // quality records or charging their free levels a second time.
        projection.Add(new XElement("metatype", selection.Selection.MetatypeId));

        if (selection.Contacts is { } contacts)
        {
            Set("contactpoints", contacts.Options.PointBudget);
            Set("contactpointsused", contacts.PointsSpent);
            Replace(new XElement("contacts", contacts.Contacts.Select(row => new XElement("contact",
                new XElement("guid", row.Contact.Id.ToString("D")), new XElement("type", "Contact"),
                new XElement("name", row.Contact.Name), new XElement("role", row.Contact.Role),
                new XElement("connection", row.Contact.Connection), new XElement("loyalty", row.Contact.Loyalty),
                new XElement("sr6pointcost", row.PointCost), new XElement("sr6gmreviewrequired", true)))));
        }

        var gear = new XElement("gears");
        var weapons = new XElement("weapons");
        var armor = new XElement("armors");
        foreach (var row in selection.Gear?.Items ?? [])
        {
            var profile = summary.Equipment.Single(item => item.ItemId == row.Choice.Id);
            string kind = row.Option.CategoryId switch { "melee" or "projectile" or "firearm" or "launcher" => "weapon", "armor" => "armor", _ => "gear" };
            var item = new XElement(kind, new XElement("guid", row.Choice.Id.ToString("D")),
                new XElement("name", row.Option.SourceName), new XElement("sr6id", row.Choice.CatalogId),
                new XElement("category", row.Option.CategoryId), new XElement("source", row.Option.SourceAnchorId),
                new XElement("qty", row.Choice.Quantity), new XElement("cost", row.Option.UnitPrice),
                new XElement("sr6totalcost", row.TotalPrice), new XElement("avail", row.Option.Availability),
                new XElement("sr6legality", row.Option.Legality), new XElement("equipped", false),
                new XElement("sr6runtimestatsavailable", profile.StatisticsAvailable),
                Sr6CreationEquipmentProfiles.ToXml(profile),
                row.Option.Rating is { } rating ? new XElement("rating", rating) : null);
            (kind == "weapon" ? weapons : kind == "armor" ? armor : gear).Add(item);
        }
        Replace(gear); Replace(weapons); Replace(armor);
        if (summary.Equipment.Any(row => !row.StatisticsAvailable)) incomplete.Add("equipment-runtime-stats");
        if (selection.Lifestyle is { } lifestyle)
            Replace(new XElement("lifestyles", new XElement("lifestyle", Identity("lifestyle", "primary"),
                new XElement("name", lifestyle.Option.Id), new XElement("baselifestyle", lifestyle.Option.Id),
                new XElement("cost", lifestyle.Option.MonthlyNuyen), new XElement("totalmonthlycost", lifestyle.Option.MonthlyNuyen),
                new XElement("months", lifestyle.Selection.Months), new XElement("source", lifestyle.Option.SourceAnchorId))));

        Replace(new XElement("spells", (selection.Spells?.Spells ?? []).Select(row => new XElement("spell",
            Identity("spell", row.Id), new XElement("sr6id", row.Id), new XElement("name", row.SourceName),
            new XElement("category", row.CategoryId), new XElement("sr6kind", row.Kind),
            new XElement("source", row.SourceAnchorId), new XElement("sr6use", selection.Spells!.UseId)))));
        Replace(new XElement("powers", (selection.AdeptPowers?.Powers ?? []).Select(row => new XElement("power",
            Identity("power", row.Option.Id), new XElement("sr6id", row.Option.Id), new XElement("name", row.Option.SourceName),
            new XElement("rating", row.Rating), new XElement("pointsperlevel", row.Option.QuarterPointsPerRating / 4m),
            new XElement("sr6totalpoints", row.QuarterPointsSpent / 4m), new XElement("sr6use", row.Option.UseId),
            new XElement("extra", row.Option.SubjectId), new XElement("source", row.Option.SourceAnchorId)))));
        Replace(new XElement("complexforms", (selection.ComplexForms?.Forms ?? []).Select(row => new XElement("complexform",
            Identity("form", row.Choice.CatalogId + "\0" + row.Choice.Subject), new XElement("sr6id", row.Choice.CatalogId),
            new XElement("name", row.SourceName), new XElement("extra", row.Choice.Subject),
            new XElement("source", row.SourceAnchorId), new XElement("sr6gmreviewrequired", row.SubjectNeedsGmReview)))));
        if (selection.Selection.TalentId is "magician" or "mystic-adept" or "aspected-magician")
            incomplete.Add("magical-tradition");
        if (selection.Spells is { Spells.Count: > 0 }) incomplete.Add("spell-runtime-stats");
        if (selection.ComplexForms is { Forms.Count: > 0 }) incomplete.Add("complex-form-runtime-stats");

        if (summary.PassiveValues is { } passive)
        {
            // Only materialize known Core-derived monitor sizes. Overflow
            // capacity stays edition-owned below; legacy overflow is damage.
            foreach (var (id, field) in new[] { ("physical-monitor", "physicalcm"), ("stun-monitor", "stuncm") })
                if (passive.Derived.Single(row => row.Id == id).Value is { } size) Set(field, size);
            projection.Add(new XElement("passiveattributes", passive.Attributes.Select(row => new XElement("attribute",
                new XAttribute("id", row.AttributeId), new XElement("natural", row.NaturalRating),
                Optional("bonus", row.PermanentBonus), Optional("effective", row.Rating)))),
                new XElement("passiveskills", (passive.Skills ?? []).Select(row => new XElement("skill",
                    new XAttribute("id", row.SkillId), new XElement("natural", row.NaturalRating),
                    new XElement("alwaysbonus", row.AlwaysBonus), new XElement("alluses", row.AllUsesRating),
                    new XElement("noncombatbonus", row.NoncombatOnlyBonus), new XElement("noncombat", row.NoncombatRating)))),
                new XElement("derived", passive.Derived.Select(row => new XElement("stat", new XAttribute("id", row.Id),
                    Optional("value", row.Value), new XElement("calculation", row.Calculation), new XElement("source", row.SourceAnchorId)))),
                new XElement("warnings", passive.WarningIds.Select(id => new XElement("warning", id))));
            if (passive.Derived.Any(row => row.Value is null)) incomplete.Add("passive-effect-conflict");
        }
        if (summary.Balances is { } balances)
        {
            Set("karma", balances.RemainingKarma);
            Set("nuyen", balances.RemainingNuyen);
            projection.Add(new XElement("carryover", new XElement("karma", balances.ProjectedStartingKarma),
                new XElement("nuyen", balances.ProjectedStartingNuyen), new XElement("excesskarma", balances.KarmaAboveCarryOver),
                new XElement("excessnuyen", balances.NuyenAboveCarryOver)));
        }
        string[] unresolved = incomplete.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        projection.Add(new XElement("incomplete", unresolved.Select(id => new XElement("domain", id))));
        string[] anchors = summary.SourceAnchorIds.Concat(summary.PassiveValues?.SourceAnchorIds ?? [])
            .Concat(summary.Equipment.SelectMany(row => row.SourceAnchorIds)).Distinct(StringComparer.Ordinal).ToArray();
        projection.Add(new XElement("sources", anchors.Select(anchor => new XElement("anchor", anchor))));
        string xml = root.ToString(SaveOptions.DisableFormatting);
        return new(CharacterCreationFoundationOutcomes.Success,
            new(state.Binding, new CharacterDocument(xml), "sha256:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(xml))),
                unresolved, anchors), []);

        void Set(string name, object value) { root.Elements(name).Remove(); root.Add(new XElement(name, value)); }
        void Replace(XElement element) { root.Elements(element.Name).Remove(); root.Add(element); }
        Guid StableId(string domain, string id) => new(SHA256.HashData(Encoding.UTF8.GetBytes(saved.Id.Value + "\0sr6\0" + domain + "\0" + id)).AsSpan(0, 16));
        XElement Identity(string domain, string id) => new("guid", StableId(domain, id).ToString("D"));
    }

    private static XElement Optional(string name, int? value) => value is { } known
        ? new(name, known) : new(name, new XAttribute("unresolved", true));

    private static XElement Language(Guid id, string name, string level, int? bonus) => new("skill",
        new XElement("guid", id.ToString("D")), new XElement("name", name), new XElement("isknowledge", true),
        new XElement("skillcategory", "SR6 Language"), new XElement("sr6level", level),
        bonus is { } known ? new XElement("sr6comprehensionbonus", known) : null);

    private static string AttributeCode(string id) => id switch
    {
        "Body" => "BOD", "Agility" => "AGI", "Reaction" => "REA", "Strength" => "STR", "Willpower" => "WIL",
        "Logic" => "LOG", "Intuition" => "INT", "Charisma" => "CHA", "Edge" => "EDG", "Magic" => "MAG", "Resonance" => "RES",
        _ => throw new InvalidOperationException("Unknown SR6 attribute.")
    };
}

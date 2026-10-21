using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Chummer.Infrastructure.Workspaces;

namespace Chummer.Tests;

public sealed partial class CharacterCareerInitiationTests
{
    [TestMethod]
    public void Native_legacy_grade_one_to_two_updates_exact_caps_without_changing_Magic_or_purchased_PP()
    {
        using var f = new Fixture();
        ImportNativeGradeOne(f);
        XElement before = f.Xml();
        var oldCaps = NativeCaps(before);
        var oldMagic = EvaluateMagic(before);
        Assert.IsTrue(oldMagic.IsSupported, string.Join(";", oldMagic.Blockers));
        Assert.AreEqual(7, oldMagic.MagicMaximum);
        var plan = f.Plan(Centering);
        Assert.AreEqual(1, plan.Quote.CurrentGrade);
        Assert.AreEqual(2, plan.Quote.NextGrade);
        Assert.AreEqual(16, plan.Quote.KarmaCost);
        Assert.AreEqual(CharacterCareerInitiationOutcome.Applied, f.Service.Commit(f.Owners.Capture(), plan).Outcome);
        var after = f.Xml();
        var nextMagic = EvaluateMagic(after);
        Assert.IsTrue(nextMagic.IsSupported, string.Join(";", nextMagic.Blockers));
        Assert.AreEqual(8, nextMagic.MagicMaximum); // no second +grade arithmetic
        Assert.AreEqual(oldMagic.Magic, nextMagic.Magic);
        Assert.AreEqual(before.Element("attributes")!.ToString(), after.Element("attributes")!.ToString());
        Assert.AreEqual(before.Element("magsplitadept")!.Value, after.Element("magsplitadept")!.Value);
        Assert.AreEqual(before.Element("adeptpowerpoints")!.Value, after.Element("adeptpowerpoints")!.Value);
        Assert.AreEqual(2, NativeCaps(after).Length);
        foreach (var cap in oldCaps)
        {
            cap.SetElementValue("rating", 2);
            Assert.IsTrue(XNode.DeepEquals(cap, NativeCaps(after).Single(x =>
                x.Element("improvedname")!.Value == cap.Element("improvedname")!.Value)));
        }
        foreach (var retained in before.Element("improvements")!.Elements().Where(x =>
                     x.Element("improvementsource")?.Value != "Initiation"))
            Assert.IsTrue(after.Element("improvements")!.Elements().Any(x => XNode.DeepEquals(x, retained)));
        foreach (var retained in before.Element("metamagics")!.Elements())
            Assert.IsTrue(after.Element("metamagics")!.Elements().Any(x => XNode.DeepEquals(x, retained)));
        var reopened = new OwnerBoundCharacterCareerInitiationService(
            new FileWorkspaceStore(f.StorePath), f.Owners, f.Authority);
        f.SetProfile("careerinitiationenabled", "False");
        Assert.AreEqual(CharacterCareerInitiationOutcome.Replayed, reopened.Commit(f.Owners.Capture(), plan).Outcome);
        Assert.AreEqual(1, f.Xml().Element("expenses")!.Elements().Count());
    }

    [TestMethod]
    public void Native_grade_cap_admits_paid_Magic_above_metatype_maximum_without_granting_another_point()
    {
        using var f = new Fixture();
        ImportNativeGradeOne(f);
        f.Edit(root =>
        {
            var a = root.Element("attributes")!.Elements().Single(x => x.Element("name")!.Value == "MAG");
            a.SetElementValue("base", 6); // min 1 + 6 = 7, permitted by the existing grade-one cap
            a.SetElementValue("totalvalue", 7);
        });
        var plan = f.Plan(Centering);
        Assert.AreEqual(7, plan.Quote.EffectiveMagic);
        Assert.AreEqual(CharacterCareerInitiationOutcome.Applied, f.Service.Commit(f.Owners.Capture(), plan).Outcome);
        var magic = EvaluateMagic(f.Xml());
        Assert.IsTrue(magic.IsSupported);
        Assert.AreEqual(7, magic.Magic);
        Assert.AreEqual(8, magic.MagicMaximum);
        Assert.AreEqual("3", f.Xml().Element("magsplitadept")!.Value);
    }

    [DataTestMethod]
    [DataRow("remove")]
    [DataRow("duplicate")]
    [DataRow("target")]
    [DataRow("rating")]
    [DataRow("max")]
    [DataRow("min")]
    [DataRow("aug")]
    [DataRow("val")]
    [DataRow("enabled")]
    [DataRow("source")]
    [DataRow("sourcename")]
    [DataRow("unique")]
    [DataRow("condition")]
    [DataRow("custom")]
    [DataRow("addtorating")]
    [DataRow("order")]
    [DataRow("duplicate-field")]
    [DataRow("namespace-field")]
    [DataRow("duplicate-grade-guid")]
    public void Forged_or_ambiguous_native_caps_never_issue_an_applicable_quote(string mutation)
    {
        using var f = new Fixture();
        ImportNativeGradeOne(f);
        f.Edit(root =>
        {
            var cap = NativeCaps(root)[0];
            switch (mutation)
            {
                case "remove": cap.Remove(); break;
                case "duplicate": cap.Parent!.Add(new XElement(cap)); break;
                case "target": cap.SetElementValue("improvedname", "MAGAdept"); break;
                case "source": cap.SetElementValue("improvementsource", "Quality"); break;
                case "sourcename": cap.SetElementValue("sourcename", Guid.NewGuid()); break;
                case "unique": cap.SetElementValue("unique", "enableattribute"); break;
                case "condition": cap.SetElementValue("condition", "career"); break;
                case "custom": cap.SetElementValue("custom", true); break;
                case "enabled": cap.SetElementValue("enabled", false); break;
                case "addtorating": cap.SetElementValue("addtorating", true); break;
                case "duplicate-field": cap.Add(new XElement("max", 1)); break;
                case "namespace-field": cap.Add(new XElement(XName.Get("rating", "urn:forged"), 1)); break;
                case "duplicate-grade-guid":
                    root.Add(new XElement("other", new XElement("guid",
                        root.Element("initiationgrades")!.Element("initiationgrade")!.Element("guid")!.Value)));
                    break;
                default: cap.SetElementValue(mutation, 2); break;
            }
        });
        string before = f.Saved().Document.Content;
        var quote = f.Read(new(Centering, AcceptObligations: true));
        Assert.IsFalse(quote.CanApply);
        Assert.IsTrue(quote.Blockers.Any(b => b.StartsWith("magic_", StringComparison.Ordinal)));
        Assert.AreEqual(CharacterCareerInitiationOutcome.Conflict,
            f.Service.Commit(f.Owners.Capture(), new(Guid.NewGuid(), quote, Date)).Outcome);
        Assert.AreEqual(before, f.Saved().Document.Content);
    }

    [DataTestMethod]
    [DataRow(true, false, true)]
    [DataRow(false, true, true)]
    [DataRow(false, false, false)]
    public void Saved_and_active_native_initiation_disable_flags_deny_only_initiation(
        bool savedFlag, bool activeEffect, bool denied)
    {
        using var f = new Fixture(adept: true, magician: false);
        f.Edit(root =>
        {
            root.SetElementValue("initiationdisabled", savedFlag);
            root.Element("improvements")!.Add(new XElement("improvement",
                new XElement("improvementttype", "SpecialTab"), new XElement("improvedname", "Initiation"),
                new XElement("unique", "disabletab"), new XElement("enabled", activeEffect),
                new XElement("improvementsource", "Quality"), new XElement("sourcename", Guid.NewGuid())));
        });
        string before = f.Saved().Document.Content;
        var magic = EvaluateMagic(f.Xml());
        Assert.IsTrue(magic.IsSupported);
        Assert.AreEqual(denied, magic.InitiationDisabled);
        Assert.AreEqual(5, magic.EffectivePowerMagic);
        var quote = f.Read(new(PowerPoint, AcceptObligations: true));
        Assert.AreEqual(!denied, quote.CanApply);
        if (denied)
        {
            Assert.IsTrue(quote.Blockers.Contains("initiation_disabled"));
            Assert.AreEqual(CharacterCareerInitiationOutcome.Conflict,
                f.Service.Commit(f.Owners.Capture(), new(Guid.NewGuid(), quote, Date)).Outcome);
            Assert.AreEqual(before, f.Saved().Document.Content);
        }
    }

    [DataTestMethod]
    [DataRow(false, false, 5)]
    [DataRow(false, true, 5)] // the profile toggle alone does not give a pure adept separate Magic
    [DataRow(true, false, 5)]
    [DataRow(true, true, 4)]
    public void Native_zero_enable_marker_survives_PP_acquisition_and_shared_evaluation(
        bool magician, bool separate, int powerMagic)
    {
        using var f = new Fixture(adept: true, magician: magician, separate: separate);
        var before = f.Xml();
        var marker = new XElement(before.Element("improvements")!.Elements().Single());
        var plan = f.Plan(PowerPoint);
        Assert.AreEqual(CharacterCareerInitiationOutcome.Applied, f.Service.Commit(f.Owners.Capture(), plan).Outcome);
        var after = f.Xml();
        var evaluated = EvaluateMagic(after, separate);
        Assert.IsTrue(evaluated.IsSupported, string.Join(";", evaluated.Blockers));
        Assert.AreEqual(powerMagic, evaluated.EffectivePowerMagic);
        Assert.AreEqual(7, evaluated.MagicMaximum);
        if (magician && separate) Assert.AreEqual(7, evaluated.AdeptMagicMaximum!.Value);
        Assert.AreEqual(before.Element("magsplitadept")!.Value, after.Element("magsplitadept")!.Value);
        Assert.IsTrue(after.Element("improvements")!.Elements().Any(x => XNode.DeepEquals(x, marker)));
        Assert.AreEqual(2, NativeCaps(after).Length);
        var grant = after.Element("improvements")!.Elements().Single(x =>
            x.Element("improvementttype")!.Value == "AdeptPowerPoints");
        Assert.AreEqual("1", grant.Element("val")!.Value);
        Assert.AreEqual(after.Element("metamagics")!.Elements().Single().Element("guid")!.Value,
            grant.Element("sourcename")!.Value);
    }

    [DataTestMethod]
    [DataRow("val")]
    [DataRow("max")]
    [DataRow("rating")]
    public void Enable_marker_cannot_hide_a_numeric_effect(string field)
    {
        using var f = new Fixture(adept: true, magician: false);
        f.Edit(root => root.Element("improvements")!.Elements().Single().SetElementValue(field, 1));
        Assert.IsFalse(f.Read(new(PowerPoint, AcceptObligations: true)).CanApply);
    }

    [TestMethod]
    public void Enable_marker_identity_cannot_be_duplicated_or_lose_its_native_source()
    {
        using var f = new Fixture(adept: true, magician: false);
        var root = f.Xml();
        root.Element("improvements")!.Add(new XElement(root.Element("improvements")!.Elements().Single()));
        Assert.IsFalse(EvaluateMagic(root).IsSupported);
        root = f.Xml();
        root.Element("improvements")!.Elements().Single().Element("sourcename")!.Remove();
        Assert.IsFalse(EvaluateMagic(root).IsSupported);
    }

    [TestMethod]
    public void FreeBase_uses_metatype_range_once_and_saved_total_never_becomes_authority()
    {
        using var f = new Fixture();
        ImportNativeGradeOne(f);
        f.Edit(root =>
        {
            var mag = root.Element("attributes")!.Elements().Single(x => x.Element("name")!.Value == "MAG");
            mag.SetElementValue("base", 0);
            mag.SetElementValue("karma", 1);
            mag.SetElementValue("totalvalue", 7);
            root.Element("improvements")!.Add(new XElement("improvement",
                new XElement("improvementttype", "Attributelevel"), new XElement("improvedname", "MAG"),
                new XElement("enabled", true), new XElement("val", 20)));
        });
        var result = EvaluateMagic(f.Xml());
        Assert.IsTrue(result.IsSupported);
        Assert.AreEqual(7, result.Magic); // min 1 + min(free 20, raw range 5) + karma 1
        Assert.AreEqual(7, result.MagicMaximum);
        f.Edit(root => root.Element("attributes")!.Elements().Single(x =>
            x.Element("name")!.Value == "MAG").SetElementValue("totalvalue", 8));
        Assert.IsFalse(EvaluateMagic(f.Xml()).IsSupported);
    }

    [TestMethod]
    public void Grade_zero_cannot_reuse_caps_and_a_new_grade_cannot_unlock_overallocated_Magic()
    {
        using var f = new Fixture();
        ImportNativeGradeOne(f);
        var root = f.Xml();
        root.SetElementValue("initiategrade", 0);
        root.Element("initiationgrades")!.RemoveNodes();
        Assert.IsFalse(EvaluateMagic(root).IsSupported);
        root = f.Xml();
        var mag = root.Element("attributes")!.Elements().Single(x => x.Element("name")!.Value == "MAG");
        mag.SetElementValue("base", 7);
        mag.SetElementValue("totalvalue", 7); // displayed clamp cannot confer authority for raw 8
        var state = EvaluateMagic(root);
        Assert.IsFalse(state.IsSupported);
        Assert.IsTrue(state.Blockers.Contains("magic_allocation_exceeds_cap"));
    }

    [TestMethod]
    public void Missing_augmented_bounds_source_metatype_drift_and_unsupported_effects_fail_closed()
    {
        using var f = new Fixture();
        var root = f.Xml();
        var mag = root.Element("attributes")!.Elements().Single(x => x.Element("name")!.Value == "MAG");
        mag.Element("metatypeaugmax")!.Remove();
        Assert.IsFalse(EvaluateMagic(root).IsSupported);
        var result = CharacterCareerMagicEvaluator.Evaluate(f.Xml(),
            new XElement("settings", new XElement("mysadeptsecondmagattribute", false)),
            new XElement("metatype", new XElement("magmin", 1), new XElement("magmax", 5)));
        Assert.IsFalse(result.IsSupported);
        root = f.Xml();
        root.Element("improvements")!.Add(new XElement("improvement",
            new XElement("improvementttype", "EssencePenalty"), new XElement("enabled", true),
            new XElement("val", 1)));
        Assert.IsFalse(EvaluateMagic(root).IsSupported);
        root = f.Xml();
        root.Element("improvements")!.Add(new XElement("improvement",
            new XElement("improvementttype", "Attribute"), new XElement("improvedname", "ESS"),
            new XElement("enabled", true), new XElement("max", 1)));
        Assert.IsFalse(EvaluateMagic(root).IsSupported);
    }

    [TestMethod]
    public void Native_attribute_purchase_price_effect_does_not_change_current_Magic_authority()
    {
        using var f = new Fixture();
        var root = f.Xml();
        root.Element("improvements")!.Add(new XElement("improvement",
            new XElement("improvementttype", "AttributeKarmaCostMultiplier"),
            new XElement("improvedname", "MAG"), new XElement("enabled", true), new XElement("val", 50)));
        var evaluated = EvaluateMagic(root);
        Assert.IsTrue(evaluated.IsSupported);
        Assert.AreEqual(5, evaluated.Magic);
        Assert.AreEqual(6, evaluated.MagicMaximum);
    }

    [TestMethod]
    public void Grade_dependent_existing_metamagic_is_blocked_without_discarding_its_effects()
    {
        using var f = new Fixture();
        ImportNativeGradeOne(f);
        f.Edit(root => root.Element("metamagics")!.Add(new XElement("metamagic",
            new XElement("guid", Guid.NewGuid()), new XElement("name", "Rating-sensitive fixture"),
            new XElement("bonus", new XElement("initiative", "Rating")))));
        string before = f.Saved().Document.Content;
        var quote = f.Read(new(Centering, AcceptObligations: true));
        Assert.IsTrue(quote.Blockers.Contains("initiation_grade_dependent_effect_unsupported"));
        Assert.IsFalse(quote.CanApply);
        Assert.AreEqual(before, f.Saved().Document.Content);
    }

    [TestMethod]
    public void Cap_changes_are_absent_before_atomic_replace_and_present_after_original_key_recovery()
    {
        var fault = new Fault();
        using var f = new Fixture(fault: fault);
        ImportNativeGradeOne(f);
        var plan = f.Plan(Centering);
        string before = f.Saved().Document.Content;
        fault.Action = stage =>
        {
            if (stage == FileWorkspaceStoreFaultStage.AfterTempFileFlushed) throw new IOException("before replacement");
        };
        Assert.AreEqual(CharacterCareerInitiationOutcome.Unavailable, f.Service.Commit(f.Owners.Capture(), plan).Outcome);
        Assert.AreEqual(before, f.Saved().Document.Content);
        fault.Action = stage =>
        {
            if (stage == FileWorkspaceStoreFaultStage.AfterTargetReplaced) throw new IOException("after replacement");
        };
        var result = f.Service.Commit(f.Owners.Capture(), plan);
        Assert.IsTrue(result.Outcome is CharacterCareerInitiationOutcome.Applied or CharacterCareerInitiationOutcome.Replayed);
        fault.Action = null;
        f.SetProfile("karmainitiationflat", "999");
        var reopened = new OwnerBoundCharacterCareerInitiationService(new FileWorkspaceStore(f.StorePath), f.Owners, f.Authority);
        Assert.AreEqual(CharacterCareerInitiationOutcome.Replayed, reopened.Commit(f.Owners.Capture(), plan).Outcome);
        Assert.IsTrue(NativeCaps(f.Xml()).All(c => c.Element("rating")!.Value == "2"));
        Assert.AreEqual(1, f.Xml().Element("expenses")!.Elements().Count());
    }

    private static CharacterCareerMagicEvaluation EvaluateMagic(XElement root, bool separate = false)
        => CharacterCareerMagicEvaluator.Evaluate(root, new XElement("settings",
            new XElement("mysadeptsecondmagattribute", separate)));

    private static XElement[] NativeCaps(XElement root)
        => root.Element("improvements")!.Elements().Where(e => e.Element("improvementsource")?.Value == "Initiation").ToArray();

    private static void ImportNativeGradeOne(Fixture f)
    {
        // Real legacy serialization, including historical boolean addtorating/enabled.
        // Grade/cap/enable and the existing effect-free benefit are imported.
        // Whole Munin has essence/ware semantics outside this evaluator's supported set.
        var native = XElement.Load(Path.Combine(FindCoreRoot(), "Chummer.Tests", "TestFiles", "Munin_Career.chum5"));
        f.Edit(root =>
        {
            root.SetElementValue("initiategrade", 1);
            root.Element("initiationgrades")!.ReplaceWith(new XElement(native.Element("initiationgrades")!));
            root.Element("metamagics")!.ReplaceWith(new XElement(native.Element("metamagics")!));
            root.Element("improvements")!.ReplaceNodes(native.Element("improvements")!.Elements().Where(e =>
                e.Element("improvementsource")?.Value == "Initiation" || e.Element("unique")?.Value == "enableattribute")
                .Select(e => new XElement(e)));
        });
    }
}

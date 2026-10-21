using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;
using Chummer.Infrastructure.Files;
using Chummer.Infrastructure.Workspaces;
using Chummer.Infrastructure.Xml;

namespace Chummer.Tests;

[TestClass]
public sealed partial class CharacterCareerInitiationTests
{
    private static readonly Guid Centering = new("e021b9ba-2cbe-4c17-865d-0ebc9a2df4cc");
    private static readonly Guid Masking = new("505d9fe3-852c-459b-8f9e-3e22c1b91b9a");
    private static readonly Guid PowerPoint = new("406f096a-c093-4a02-b60f-002eb01a20b9");
    private static readonly DateTime Date = new(2026, 10, 20, 12, 0, 0, DateTimeKind.Unspecified);

    [TestMethod]
    public void Mage_buys_exact_next_grade_and_chosen_source_metamagic_and_reopens()
    {
        using var f = new Fixture();
        var plan = f.Plan(Centering);
        Assert.AreEqual(0, plan.Quote.CurrentGrade);
        Assert.AreEqual(1, plan.Quote.NextGrade);
        Assert.AreEqual(13, plan.Quote.KarmaCost); // actual Standard settings.xml: 10 + 3
        string beforeMagic = f.Xml().Element("attributes")!.ToString();
        var result = f.Service.Commit(f.Owners.Capture(), plan);
        Assert.AreEqual(CharacterCareerInitiationOutcome.Applied, result.Outcome, result.Error);
        XElement saved = f.Xml();
        Assert.AreEqual("87", saved.Element("karma")!.Value);
        Assert.AreEqual("1", saved.Element("initiategrade")!.Value);
        Assert.AreEqual(beforeMagic, saved.Element("attributes")!.ToString());
        Assert.AreEqual(1, saved.Element("metamagics")!.Elements().Count());
        Assert.AreEqual(Centering.ToString("D"), saved.Element("metamagics")!.Element("metamagic")!.Element("sourceid")!.Value);
        Assert.AreEqual(3, saved.Element("improvements")!.Elements().Count()); // enable marker + both native caps
        Assert.AreEqual(1, saved.Element("expenses")!.Elements().Count());
        Assert.AreEqual(2L, f.Saved().SavedRevision);
        var reopened = new OwnerBoundCharacterCareerInitiationService(new FileWorkspaceStore(f.StorePath), f.Owners, f.Authority);
        Assert.AreEqual(CharacterCareerInitiationOutcome.Replayed, reopened.Commit(f.Owners.Capture(), plan).Outcome);
        var second = f.Plan(Masking);
        Assert.AreEqual(2, second.Quote.NextGrade);
        Assert.AreEqual(16, second.Quote.KarmaCost);
        Assert.AreEqual(CharacterCareerInitiationOutcome.Applied, reopened.Commit(f.Owners.Capture(), second).Outcome);
        Assert.AreEqual(2, f.Xml().Element("expenses")!.Elements().Count());
    }

    [DataTestMethod]
    [DataRow(false, false)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public void Adept_and_mystic_select_PP_without_changing_purchased_pool_or_Magic(bool mystic, bool separate)
    {
        using var f = new Fixture(adept: true, magician: mystic, separate: separate);
        var before = f.Xml();
        var plan = f.Plan(PowerPoint);
        Assert.AreEqual(separate, plan.Quote.SeparateAdeptMagic);
        if (separate) Assert.AreEqual(4, plan.Quote.EffectiveAdeptMagic!.Value);
        Assert.AreEqual(CharacterCareerInitiationOutcome.Applied, f.Service.Commit(f.Owners.Capture(), plan).Outcome);
        var after = f.Xml();
        Assert.AreEqual(before.Element("adeptpowerpoints")!.Value, after.Element("adeptpowerpoints")!.Value);
        Assert.AreEqual(before.Element("attributes")!.ToString(), after.Element("attributes")!.ToString());
        var grant = after.Element("improvements")!.Elements().Single(x => x.Element("improvementttype")!.Value == "AdeptPowerPoints");
        Assert.AreEqual("AdeptPowerPoints", grant.Element("improvementttype")!.Value);
        Assert.AreEqual("1", grant.Element("val")!.Value);
        Assert.AreEqual(after.Element("metamagics")!.Elements().Single().Element("guid")!.Value, grant.Element("sourcename")!.Value);
        Assert.AreEqual("false", after.Element("metamagics")!.Elements().Single().Element("paidwithkarma")!.Value);
    }

    [TestMethod]
    public void Effective_settings_fixture_controls_cost_additive_discounts_schooling_and_obligations()
    {
        using var f = new Fixture(configured: true);
        var selection = new CharacterCareerInitiationSelection(Centering, true, true, true, true);
        var quote = f.Read(selection);
        Assert.IsTrue(quote.CanApply, string.Join(";", quote.Blockers));
        // Configured 7 + 4, multiplied by (1 - .20 - .10 - .05), StandardRound => 8.
        Assert.AreEqual(8, quote.KarmaCost);
        Assert.AreEqual(1234m, quote.NuyenCost);
        Assert.AreEqual(4, quote.Obligations.Count);
        Assert.AreEqual(CharacterCareerInitiationOutcome.Applied,
            f.Service.Commit(f.Owners.Capture(), new(Guid.NewGuid(), quote, Date)).Outcome);
        var saved = f.Xml();
        Assert.AreEqual("92", saved.Element("karma")!.Value);
        Assert.AreEqual("18766", saved.Element("nuyen")!.Value);
        Assert.AreEqual(2, saved.Element("expenses")!.Elements().Count());
        Assert.AreEqual("true", saved.Element("initiationgrades")!.Elements().Single().Element("schooling")!.Value);
        Assert.IsTrue(saved.Element("initiationgrades")!.Elements().Single().Element("notes")!.Value.Contains("Schooling fixture obligation"));
    }

    [TestMethod]
    public void Missing_adjustment_authority_and_cancelled_choice_never_charge_an_empty_grade()
    {
        using var f = new Fixture();
        string before = f.Saved().Document.Content;
        var cancelled = f.Read(new(Guid.Empty, AcceptObligations: true));
        Assert.IsTrue(cancelled.Blockers.Contains("initiation_benefit_choice_required"));
        Assert.AreEqual(CharacterCareerInitiationOutcome.Conflict,
            f.Service.Commit(f.Owners.Capture(), new(Guid.NewGuid(), cancelled, Date)).Outcome);
        Assert.IsTrue(f.Read(new(Centering, Group: true, AcceptObligations: true)).Blockers.Contains("initiation_group_policy_not_admitted"));
        Assert.IsTrue(f.Read(new(Centering)).Blockers.Contains("initiation_obligations_not_accepted"));
        Assert.AreEqual(before, f.Saved().Document.Content);
    }

    [TestMethod]
    public void Insufficient_karma_unavailable_duplicate_and_unsupported_benefits_are_blocked()
    {
        using var f = new Fixture();
        Assert.IsTrue(f.Read(new(Guid.NewGuid(), AcceptObligations: true)).Blockers.Contains("initiation_benefit_unavailable"));
        Assert.IsTrue(f.Read(new(PowerPoint, AcceptObligations: true)).Blockers.Contains("initiation_power_points_require_adept"));
        var quickening = new Guid("4ea558ed-0fe8-4b9e-b2fa-afffb3eb2476");
        Assert.IsTrue(f.Read(new(quickening, AcceptObligations: true)).Blockers.Contains("initiation_benefit_effect_unsupported"));
        var plan = f.Plan(Centering);
        Assert.AreEqual(CharacterCareerInitiationOutcome.Applied, f.Service.Commit(f.Owners.Capture(), plan).Outcome);
        Assert.IsTrue(f.Read(new(Centering, AcceptObligations: true)).Blockers.Contains("initiation_benefit_duplicate"));
        f.Edit(x => x.SetElementValue("karma", 1));
        Assert.IsTrue(f.Read(new(Masking, AcceptObligations: true)).Blockers.Contains("initiation_insufficient_karma"));
    }

    [TestMethod]
    public void Saved_grade_and_separate_adept_effective_Magic_cap_are_required()
    {
        using var f = new Fixture(adept: true, magician: true, separate: true);
        f.Edit(x =>
        {
            var a = x.Element("attributes")!.Elements().Single(a => a.Element("name")!.Value == "MAGAdept");
            a.SetElementValue("base", 0);
            a.SetElementValue("totalvalue", 1);
        });
        var first = f.Plan(PowerPoint);
        Assert.AreEqual(CharacterCareerInitiationOutcome.Applied, f.Service.Commit(f.Owners.Capture(), first).Outcome);
        Assert.IsTrue(f.Read(new(PowerPoint, AcceptObligations: true)).Blockers.Contains("initiation_magic_cap"));
        f.Edit(x => x.SetElementValue("initiategrade", 3));
        Assert.IsTrue(f.Read(new(PowerPoint, AcceptObligations: true)).Blockers.Contains("initiation_saved_grade_inconsistent"));
        Assert.AreEqual(CharacterCareerInitiationOutcome.Conflict,
            f.Service.Commit(f.Owners.Capture(), first with { TransactionId = Guid.NewGuid(), Quote = first.Quote with { NextGrade = 7 } }).Outcome);
    }

    [TestMethod]
    public void Revision_owner_transition_and_source_drift_are_rejected_before_any_purchase()
    {
        using var f = new Fixture();
        var original = f.Plan(Centering);
        var captured = f.Owners.Capture();
        f.Owners.Set(new OwnerScope("other-initiation-owner"));
        f.Owners.Set(OwnerScope.LocalSingleUser);
        Assert.AreEqual(CharacterCareerInitiationOutcome.Conflict, f.Service.Commit(captured, original).Outcome);
        Assert.AreEqual(CharacterCareerInitiationOutcome.Conflict, f.Service.Commit(f.Owners.Capture(), original).Outcome);
        var current = f.Plan(Centering);
        f.Edit(x => x.SetElementValue("notes", "changed revision"));
        Assert.AreEqual(CharacterCareerInitiationOutcome.Conflict, f.Service.Commit(f.Owners.Capture(), current).Outcome);
        current = f.Plan(Centering);
        f.SetProfile("karmainitiationflat", "11");
        Assert.AreEqual(CharacterCareerInitiationOutcome.Conflict, f.Service.Commit(f.Owners.Capture(), current).Outcome);
        Assert.AreEqual(0, f.Xml().Element("expenses")!.Elements().Count());
    }

    [TestMethod]
    public void Source_change_at_atomic_replace_is_fenced_and_lost_response_replays_once()
    {
        var fault = new Fault();
        using var f = new Fixture(fault: fault);
        var stale = f.Plan(Centering);
        fault.Action = stage =>
        {
            if (stage == FileWorkspaceStoreFaultStage.AfterTempFileFlushed) f.SetProfile("karmainitiationflat", "12");
        };
        Assert.AreEqual(CharacterCareerInitiationOutcome.Unavailable, f.Service.Commit(f.Owners.Capture(), stale).Outcome);
        Assert.AreEqual(0, f.Xml().Element("expenses")!.Elements().Count());
        fault.Action = null;
        var plan = f.Plan(Centering);
        fault.Action = stage =>
        {
            if (stage == FileWorkspaceStoreFaultStage.AfterTargetReplaced) throw new IOException("simulated lost response");
        };
        var first = f.Service.Commit(f.Owners.Capture(), plan);
        Assert.IsTrue(first.Outcome is CharacterCareerInitiationOutcome.Applied or CharacterCareerInitiationOutcome.Replayed);
        fault.Action = null;
        // Previously committed identity must be recovered even when current source costs change.
        f.SetProfile("karmainitiationflat", "99");
        Assert.AreEqual(CharacterCareerInitiationOutcome.Replayed, f.Service.Commit(f.Owners.Capture(), plan).Outcome);
        Assert.AreEqual(1, f.Xml().Element("expenses")!.Elements().Count());
        Assert.AreEqual(1, f.Saved().Document.AuxiliaryState.CharacterCareerInitiationTransactions!.Count);
        Assert.AreEqual(CharacterCareerInitiationOutcome.IdempotencyConflict,
            f.Service.Commit(f.Owners.Capture(), plan with { ExpenseDateLocal = Date.AddMinutes(1) }).Outcome);
    }

    [TestMethod]
    public void Auxiliary_binding_and_generic_ledger_writes_fail_closed()
    {
        using var f = new Fixture();
        var plan = f.Plan(Centering);
        var saved = f.Saved();
        using var admission = f.Authority.Open(f.Owners.Capture(), saved, plan.Quote.Selection, f.Store.CareerInitiationRuntimeFingerprint);
        Assert.IsNotNull(admission);
        var changed = saved with { Document = saved.Document with { State = saved.Document.State with
        { AuxiliaryState = saved.Document.AuxiliaryState with { CharacterCareerInitiationTransactions = [] } } } };
        Assert.IsFalse(CharacterCareerInitiationTransaction.TryBuild(f.Owners.Capture(), changed, plan, admission!,
            f.Store.CareerInitiationRuntimeFingerprint, out _, out _));
        Assert.IsTrue(CharacterCareerInitiationTransaction.TryBuild(f.Owners.Capture(), saved, plan, admission!,
            f.Store.CareerInitiationRuntimeFingerprint, out var replacement, out _));
        Assert.IsFalse(f.Store.ReplaceWorkspaceDocumentAndAuxiliaryStateAndCheckpoint(f.Id,
            saved.ContentRevision, saved.Document.AuxiliaryStateDigest, replacement!).Success);
        Assert.AreEqual(0, f.Xml().Element("expenses")!.Elements().Count());
    }

    [TestMethod]
    public void Disabled_source_and_unmet_art_or_unknown_prerequisites_never_become_legal_choices()
    {
        using var f = new Fixture();
        f.Edit(x => x.Element("arts")!.RemoveNodes());
        Assert.IsTrue(f.Read(new(Centering, AcceptObligations: true)).Blockers.Contains("initiation_prerequisite_not_met"));
        var data = XDocument.Load(f.MetamagicPath);
        var centering = data.Root!.Element("metamagics")!.Elements("metamagic")
            .Single(x => x.Element("id")!.Value == Centering.ToString("D"));
        centering.Element("required")!.Add(new XElement("unrecognized-rule", "1"));
        data.Save(f.MetamagicPath);
        Assert.IsTrue(f.Read(new(Centering, AcceptObligations: true)).Blockers.Contains("initiation_prerequisite_unsupported"));
        centering.SetElementValue("source", "NOT-ENABLED");
        data.Save(f.MetamagicPath);
        Assert.IsTrue(f.Read(new(Centering, AcceptObligations: true)).Blockers.Contains("initiation_source_disabled"));
    }

    [TestMethod]
    public void House_policy_PP_disable_campaign_authority_and_schooling_balance_are_not_UI_permissions()
    {
        using var f = new Fixture(adept: true, magician: true, configured: true);
        f.SetProfile("careerinitiationpowerpointenabled", "False");
        Assert.IsTrue(f.Read(new(PowerPoint, AcceptObligations: true)).Blockers.Contains("initiation_power_points_policy_disabled"));
        f.Edit(x => x.SetElementValue("nuyen", 1233));
        Assert.IsTrue(f.Read(new(Centering, Schooling: true, AcceptObligations: true)).Blockers.Contains("initiation_insufficient_nuyen"));
        f.Edit(x => x.SetElementValue("campaign", "external policy requires an authority adapter"));
        Assert.IsTrue(f.Read(new(Centering, AcceptObligations: true)).Blockers.Contains("initiation_campaign_authority_unavailable"));
        Assert.AreEqual(0, f.Xml().Element("expenses")!.Elements().Count());
    }

    [TestMethod]
    public void Explicit_cancellation_and_foreign_workspace_cannot_apply_a_reviewed_purchase()
    {
        using var f = new Fixture();
        var plan = f.Plan(Centering);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.AreEqual(CharacterCareerInitiationOutcome.Unavailable, f.Service.Commit(f.Owners.Capture(), plan, cancellation.Token).Outcome);
        var foreign = plan with { Quote = plan.Quote with { Binding = plan.Quote.Binding with { WorkspaceId = "missing-initiation-workspace" } } };
        Assert.AreEqual(CharacterCareerInitiationOutcome.Missing, f.Service.Commit(f.Owners.Capture(), foreign).Outcome);
        Assert.AreEqual(0, f.Xml().Element("expenses")!.Elements().Count());
        Assert.AreEqual("0", f.Xml().Element("initiategrade")!.Value);
    }

    private sealed class Fault : IFileWorkspaceStoreFaultInjector
    {
        public Action<FileWorkspaceStoreFaultStage>? Action;
        public void OnStage(FileWorkspaceStoreFaultStage stage, string targetPath, string tempPath) => Action?.Invoke(stage);
    }

    private sealed class Fixture : IDisposable
    {
        private const string ProfileId = "223a11ff-80e0-428b-89a9-6ef1c243b8b6";
        public string Root { get; } = Directory.CreateTempSubdirectory("chummer-initiation-test-").FullName;
        public string StorePath => Path.Combine(Root, "workspaces");
        public string MetamagicPath => Path.Combine(Root, "Chummer", "data", "metamagic.xml");
        private string SettingsPath => Path.Combine(Root, "Chummer", "data", "settings.xml");
        public CharacterWorkspaceId Id { get; } = new("career-initiation-fixture");
        public CharacterCreationAttributesServiceTests.AllocationOwners Owners { get; } = new();
        public FileWorkspaceStore Store { get; }
        public CharacterCareerInitiationAuthorityResolver Authority { get; }
        public OwnerBoundCharacterCareerInitiationService Service { get; }

        public Fixture(bool adept = false, bool magician = true, bool separate = false, bool configured = false, Fault? fault = null)
        {
            string data = Path.Combine(Root, "Chummer", "data");
            Directory.CreateDirectory(data);
            // Real source/profile rows, not a TestAuthority that blesses caller-provided prices.
            foreach (string name in new[] { "settings.xml", "metamagic.xml", "metatypes.xml", "priorities.xml", "skills.xml", "books.xml" })
                File.Copy(Path.Combine(FindCoreRoot(), "Chummer", "data", name), Path.Combine(data, name));
            SetProfile("mysadeptsecondmagattribute", separate.ToString());
            if (configured)
            {
                SetProfile("karmainitiationflat", "7");
                SetProfile("karmainitiation", "4");
                SetProfile("careerinitiationtraining", "Configured training obligation");
                foreach (var (id, rate) in new[] { ("group", "0.20"), ("ordeal", "0.10"), ("schooling", "0.05") })
                {
                    SetProfile("karmamaginitiation" + id + "percent", rate);
                    SetProfile("careerinitiation" + id + "enabled", "True");
                    SetProfile("careerinitiation" + id + "obligation", id == "schooling" ? "Schooling fixture obligation" : id + " fixture obligation");
                }
                SetProfile("careerinitiationschoolingnuyen", "1234");
            }
            Owners.Set(OwnerScope.LocalSingleUser);
            Store = fault is null ? new(StorePath) : new(StorePath, fault);
            Authority = new(new FileSystemCharacterSourceDataResolver(new FileSystemContentOverlayCatalogService(Root, Root, null)));
            Service = new(Store, Owners, Authority);
            var root = XElement.Parse($"""
                <character><settings>{ProfileId}</settings><created>True</created><magenabled>True</magenabled>
                <adept>{adept}</adept><magician>{magician}</magician><initiategrade>0</initiategrade>
                <karma>100</karma><nuyen>20000</nuyen><magsplitadept>3</magsplitadept><adeptpowerpoints>3</adeptpowerpoints>
                <groupmember>True</groupmember><groupname>Fixture group</groupname>
                <attributes>
                  <attribute><name>MAG</name><base>4</base><karma>0</karma><metatypemin>1</metatypemin><metatypemax>6</metatypemax><metatypeaugmax>9</metatypeaugmax><totalvalue>5</totalvalue></attribute>
                  <attribute><name>MAGAdept</name><base>3</base><karma>0</karma><metatypemin>1</metatypemin><metatypemax>6</metatypemax><metatypeaugmax>9</metatypeaugmax><totalvalue>4</totalvalue></attribute>
                </attributes>
                <arts><art><name>Centering</name></art><art><name>Masking</name></art></arts>
                <qualities/><initiationgrades/><metamagics/><powers/>
                <improvements><improvement><unique>enableattribute</unique><improvedname>MAG</improvedname>
                  <improvementttype>Attribute</improvementttype><improvementsource>Quality</improvementsource>
                <sourcename>fcdeeb08-1c3a-401e-ac33-662c0acf2318</sourcename><enabled>1</enabled>
                <min>0</min><max>0</max><aug>0</aug><augmax>0</augmax><rating>0</rating><val>0</val>
                </improvement></improvements><expenses/><cyberwares/>
                </character>
                """);
            Assert.IsTrue(Store.CreateWorkspaceDocument(Id, new WorkspaceDocument(root.ToString(SaveOptions.DisableFormatting), "sr5")).Success);
            Assert.IsTrue(Store.SaveCheckpoint(Id, 1).Success);
        }

        public void SetProfile(string name, string value)
        {
            var doc = XDocument.Load(SettingsPath);
            var profile = doc.Descendants("setting").Single(x => x.Element("id")?.Value == ProfileId);
            (name is "karmainitiation" or "karmainitiationflat" ? profile.Element("karmacost")! : profile).SetElementValue(name, value);
            doc.Save(SettingsPath);
        }
        public WorkspaceStoredDocument Saved()
        {
            var read = new FileWorkspaceStore(StorePath).Get(Id);
            Assert.IsTrue(read.Success, read.Error);
            return read.Value!;
        }
        public XElement Xml() => XElement.Parse(Saved().Document.Content);
        public CharacterCareerInitiationQuote Read(CharacterCareerInitiationSelection selection)
        {
            var result = Service.Read(Owners.Capture(), Id, selection);
            Assert.IsNotNull(result);
            return result!;
        }
        public CharacterCareerInitiationPlan Plan(Guid benefit)
        {
            var q = Read(new(benefit, AcceptObligations: true));
            Assert.IsTrue(q.CanApply, string.Join(";", q.Blockers));
            return new(Guid.NewGuid(), q, Date);
        }
        public void Edit(Action<XElement> edit)
        {
            var saved = Saved();
            var root = XElement.Parse(saved.Document.Content);
            edit(root);
            Assert.IsTrue(Store.ReplaceWorkspaceDocument(Id, saved.ContentRevision,
                saved.Document with { State = saved.Document.State with { Payload = root.ToString(SaveOptions.DisableFormatting) } }).Success);
            Assert.IsTrue(Store.SaveCheckpoint(Id, saved.ContentRevision + 1).Success);
        }
        public void Dispose() => Directory.Delete(Root, true);
    }

    private static string FindCoreRoot()
    {
        for (DirectoryInfo? current = new(AppDomain.CurrentDomain.BaseDirectory); current is not null; current = current.Parent)
            if (File.Exists(Path.Combine(current.FullName, "Chummer", "data", "settings.xml"))) return current.FullName;
        throw new DirectoryNotFoundException("Canonical Chummer source fixtures unavailable.");
    }
}

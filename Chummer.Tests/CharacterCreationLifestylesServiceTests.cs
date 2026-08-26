using System.Reflection;
using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;
using Chummer.Infrastructure.Workspaces;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public sealed class CharacterCreationLifestylesServiceTests
{
    private static readonly Guid LifestyleId = Guid.Parse("87796157-0366-4154-836a-034326e8e924");
    private static readonly Guid SiblingId = Guid.Parse("11111111-2222-4333-8444-555555555555");
    private static readonly Guid LowSourceId = Guid.Parse("451eef87-d18e-4bee-a972-1ee165b08522");
    private static readonly Guid HighSourceId = Guid.Parse("4a37d519-c9be-4ecc-97bb-e9d78708c374");
    private static readonly Guid QualitySourceId = Guid.Parse("22222222-3333-4444-8555-666666666666");

    [TestMethod]
    public void Rules_project_exact_Chummer5_cost_layers_free_modes_and_lifestyle_points()
    {
        CharacterCreationLifestylesAuthority authority = Authority(includeQuality: true);
        var configuration = Configuration(LifestyleId) with
        {
            StyleId = CharacterCreationLifestyleStyleIds.Advanced,
            Area = 1,
            Roommates = 1,
            SplitCostWithRoommates = true,
            Qualities =
            [
                new CharacterCreationLifestyleQualitySelection(
                    Guid.Parse("33333333-4444-4555-8666-777777777777"),
                    $"lifestyle-quality:{QualitySourceId:D}",
                    string.Empty,
                    UseLifestylePoints: false,
                    IsFree: false,
                    IsBuiltIn: false)
            ]
        };

        Assert.IsTrue(CharacterCreationLifestylesRules.TryProject(
            configuration,
            authority,
            out CharacterCreationLifestyleProjection projection,
            out IReadOnlyList<string> blockers), string.Join(',', blockers));
        // (((2000 * 1.10 aspects) + 100 flat aspect + 100 quality) * 1.10 roommate) / 2.
        // Mirrors CostPreSplit order.
        Assert.AreEqual(1320m, projection.Economics.CostPerIncrement);
        Assert.AreEqual(1320m, projection.Economics.TotalCost);
        Assert.AreEqual(3, projection.Economics.LifestylePointsTotal);
        Assert.AreEqual(2, projection.Economics.LifestylePointsRemaining);
        AssertDigests(projection.LifestyleDigest);
    }

    [TestMethod]
    public void Create_preview_confirm_reopen_and_receipt_lookup_are_atomic_and_typed()
    {
        WithService((store, service, id, directory) =>
        {
            CharacterCreationLifestylesState state = Load(service, id);
            Assert.IsTrue(state.CanEdit, string.Join(',', state.Blockers));
            Assert.AreEqual(10_000m, state.Budget.Total);
            Assert.AreEqual(0m, state.Budget.Used);
            CharacterCreationLifestyleMutation mutation = new(
                CharacterCreationLifestyleMutationKinds.Create,
                LifestyleId,
                Configuration(LifestyleId));
            CharacterCreationLifestylePreview preview = Preview(service, state.Binding, mutation);
            Assert.IsNull(preview.Before);
            Assert.AreEqual(2_000m, preview.After!.Economics.TotalCost);
            Assert.AreEqual(8_000m, preview.BudgetAfter.Remaining);
            Assert.IsTrue(preview.WritePlan.PreservesUntouchedSiblingState);
            Assert.IsTrue(preview.WritePlan.PreservesNestedState);
            Assert.AreEqual(1, preview.WritePlan.Operations.Count);

            var request = new CharacterCreationLifestyleConfirmRequest(
                state.Binding,
                mutation,
                preview.PreviewDigest,
                "android-lifestyle-create-001",
                ExplicitlyConfirmed: true);
            CharacterCreationLifestyleResult<CharacterCreationLifestyleReceipt> applied = service.Confirm(request);
            Assert.AreEqual(CharacterCreationLifestyleOutcomes.Applied, applied.Outcome);
            Assert.AreEqual(1L, applied.Value!.PreviousWorkspaceRevision);
            Assert.AreEqual(2L, applied.Value.WorkspaceRevision);
            Assert.AreEqual(0L, applied.Value.PreviousSavedRevision);
            Assert.AreEqual(2L, applied.Value.SavedRevision);
            Assert.AreEqual(0m, applied.Value.LifestyleCostBefore);
            Assert.AreEqual(2_000m, applied.Value.LifestyleCostAfter);
            AssertDigests(
                applied.Value.IdempotencyKeyDigest,
                applied.Value.CommandDigest,
                applied.Value.ReceiptDigest);

            WorkspaceStoredDocument persisted = store.Get(id).Value!;
            Assert.AreEqual(2L, persisted.ContentRevision);
            Assert.AreEqual(2L, persisted.SavedRevision);
            Assert.AreEqual(1, persisted.Document.AuxiliaryState.CharacterCreationLifestyleReceipts!.Count);
            XElement saved = Lifestyle(XDocument.Parse(persisted.Document.Content), LifestyleId);
            Assert.AreEqual(LowSourceId.ToString("D"), saved.Element("sourceid")!.Value);
            Assert.AreEqual("Low apartment", saved.Element("name")!.Value);
            Assert.AreEqual("2000", saved.Element("cost")!.Value);
            Assert.AreEqual("Standard", saved.Element("type")!.Value);

            var restarted = new CharacterCreationLifestylesService(
                new FileWorkspaceStore(directory),
                new FakeResolver(Authority()));
            CharacterCreationLifestylesState reopened = Load(restarted, id);
            Assert.AreEqual(1, reopened.Lifestyles.Count);
            CharacterCreationLifestyleResult<CharacterCreationLifestyleReceipt> lookup = restarted.LookupReceipt(
                new CharacterCreationLifestyleReceiptLookupRequest(id, "android-lifestyle-create-001"));
            Assert.AreEqual(CharacterCreationLifestyleOutcomes.Available, lookup.Outcome);
            Assert.AreEqual(applied.Value.ReceiptId, lookup.Value!.ReceiptId);
            CharacterCreationLifestyleResult<CharacterCreationLifestyleReceipt> replay = restarted.Confirm(request);
            Assert.AreEqual(CharacterCreationLifestyleOutcomes.Replayed, replay.Outcome);
            Assert.AreEqual(2L, new FileWorkspaceStore(directory).Get(id).Value!.ContentRevision);
        });
    }

    [TestMethod]
    public void Edit_and_delete_preserve_unknown_nested_and_sibling_state()
    {
        WithService((store, service, id, _) =>
        {
            CharacterCreationLifestylesState state = Load(service, id);
            CharacterCreationLifestyleConfiguration edited = state.Lifestyles.Single(
                item => item.Configuration.LifestyleId == LifestyleId).Configuration with
            {
                Name = "Renamed home",
                Increments = 2
            };
            CharacterCreationLifestyleMutation edit = new(
                CharacterCreationLifestyleMutationKinds.Edit,
                LifestyleId,
                edited);
            CharacterCreationLifestylePreview editPreview = Preview(service, state.Binding, edit);
            Assert.AreEqual(4_000m, editPreview.BudgetBefore.Used);
            Assert.AreEqual(6_000m, editPreview.BudgetAfter.Used);
            Assert.AreEqual(
                editPreview.WritePlan.UntouchedSiblingDigestBefore,
                editPreview.WritePlan.UntouchedSiblingDigestAfter);
            Assert.AreEqual(
                editPreview.WritePlan.NestedStateDigestBefore,
                editPreview.WritePlan.NestedStateDigestAfter);
            Assert.AreEqual(CharacterCreationLifestyleOutcomes.Applied, service.Confirm(
                new CharacterCreationLifestyleConfirmRequest(
                    state.Binding,
                    edit,
                    editPreview.PreviewDigest,
                    "android-lifestyle-edit-001",
                    true)).Outcome);
            WorkspaceStoredDocument afterEdit = store.Get(id).Value!;
            XDocument editedDocument = XDocument.Parse(afterEdit.Document.Content);
            Assert.AreEqual("42", Lifestyle(editedDocument, LifestyleId)
                .Element("chummercomplete")!.Element("sentinel")!.Value);
            Assert.AreEqual("Sibling home", Lifestyle(editedDocument, SiblingId).Element("name")!.Value);

            CharacterCreationLifestylesState editState = Load(service, id);
            CharacterCreationLifestyleMutation delete = new(
                CharacterCreationLifestyleMutationKinds.Delete,
                LifestyleId,
                null);
            CharacterCreationLifestylePreview deletePreview = Preview(service, editState.Binding, delete);
            Assert.IsNull(deletePreview.After);
            Assert.AreEqual(2_000m, deletePreview.BudgetAfter.Used);
            WorkspaceStoredDocument beforeDelete = store.Get(id).Value!;
            WorkspaceDocument deleteReplacement = ApplyDelete(beforeDelete.Document, LifestyleId);
            CharacterCreationLifestyleReceiptLedgerEntry deleteEntry = Entry(
                beforeDelete,
                editState.Binding,
                deletePreview,
                LifestyleId,
                '8',
                '9');
            Assert.AreEqual(
                deletePreview.WritePlan.ContentDigestAfter,
                CharacterCreationLifestyleReceiptLedgerIntegrity.ComputeContentDigest(deleteReplacement.Content));
            Assert.IsTrue(CharacterCreationLifestyleReceiptLedgerIntegrity.ElementMatchesProjectionForTests(
                Lifestyle(XDocument.Parse(beforeDelete.Document.Content), LifestyleId)
                    .ToString(SaveOptions.DisableFormatting),
                deletePreview.Before!));
            Assert.AreEqual(
                deletePreview.WritePlan.UntouchedSiblingDigestBefore,
                CharacterCreationLifestyleReceiptLedgerIntegrity.ComputeUntouchedSiblingDigestForTests(
                    beforeDelete.Document.Content,
                    LifestyleId));
            Assert.AreEqual(
                deletePreview.WritePlan.UntouchedSiblingDigestAfter,
                CharacterCreationLifestyleReceiptLedgerIntegrity.ComputeUntouchedSiblingDigestForTests(
                    deleteReplacement.Content,
                    LifestyleId));
            Assert.IsTrue(CharacterCreationLifestyleReceiptLedgerIntegrity.IsValidForCommit(
                id,
                beforeDelete.ContentRevision,
                beforeDelete.SavedRevision,
                deleteEntry));
            Assert.IsTrue(CharacterCreationLifestyleReceiptLedgerIntegrity.HasValidContentTransition(
                deleteEntry,
                beforeDelete.Document,
                deleteReplacement));
            Assert.AreEqual(CharacterCreationLifestyleOutcomes.Applied, service.Confirm(
                new CharacterCreationLifestyleConfirmRequest(
                    editState.Binding,
                    delete,
                    deletePreview.PreviewDigest,
                    "android-lifestyle-delete-001",
                    true)).Outcome);
            XDocument deletedDocument = XDocument.Parse(store.Get(id).Value!.Document.Content);
            Assert.IsFalse(deletedDocument.Root!.Element("lifestyles")!.Elements("lifestyle")
                .Any(item => item.Element("guid")?.Value == LifestyleId.ToString("D")));
            Assert.AreEqual("Sibling home", Lifestyle(deletedDocument, SiblingId).Element("name")!.Value);
        }, Fixture(includeExisting: true));
    }

    [TestMethod]
    public void Budget_source_identity_disabled_source_and_stale_bindings_fail_closed()
    {
        WithService((store, service, id, _) =>
        {
            CharacterCreationLifestylesState state = Load(service, id);
            CharacterCreationLifestyleMutation overspend = new(
                CharacterCreationLifestyleMutationKinds.Create,
                LifestyleId,
                Configuration(LifestyleId) with { Increments = 6 });
            CharacterCreationLifestyleResult<CharacterCreationLifestylePreview> insufficient = service.Preview(
                new CharacterCreationLifestylePreviewRequest(state.Binding, overspend));
            Assert.AreEqual(CharacterCreationLifestyleOutcomes.Blocked, insufficient.Outcome);
            CollectionAssert.Contains(
                insufficient.Blockers.ToArray(),
                CharacterCreationLifestylesBlockers.InsufficientFunds);

            CharacterCreationLifestyleMutation disabled = overspend with
            {
                Configuration = Configuration(LifestyleId) with
                {
                    BaseLifestyleOptionId = $"lifestyle:{HighSourceId:D}"
                }
            };
            CharacterCreationLifestyleResult<CharacterCreationLifestylePreview> sourceBlocked = service.Preview(
                new CharacterCreationLifestylePreviewRequest(state.Binding, disabled));
            CollectionAssert.Contains(
                sourceBlocked.Blockers.ToArray(),
                CharacterCreationLifestylesBlockers.SourceDisabled);

            CharacterCreationLifestyleResult<CharacterCreationLifestylePreview> stale = service.Preview(
                new CharacterCreationLifestylePreviewRequest(
                    state.Binding with { ContentDigest = Sha('e') },
                    new CharacterCreationLifestyleMutation(
                        CharacterCreationLifestyleMutationKinds.Create,
                        LifestyleId,
                        Configuration(LifestyleId))));
            Assert.AreEqual(CharacterCreationLifestyleOutcomes.Conflict, stale.Outcome);
            CollectionAssert.Contains(stale.Blockers.ToArray(), CharacterCreationLifestylesBlockers.StaleContentDigest);
        });

        string invalid = Fixture(includeExisting: true).Replace(
            LowSourceId.ToString("D"),
            "99999999-9999-4999-8999-999999999999",
            StringComparison.Ordinal);
        WithService((_, service, id, _) =>
        {
            CharacterCreationLifestylesState state = Load(service, id);
            Assert.IsFalse(state.CanEdit);
            CollectionAssert.Contains(state.Blockers.ToArray(), CharacterCreationLifestylesBlockers.SourceIdentityMismatch);
        }, invalid);
    }

    [TestMethod]
    public void Explicit_confirmation_duplicate_idempotency_and_different_command_are_enforced()
    {
        WithService((store, service, id, _) =>
        {
            CharacterCreationLifestylesState state = Load(service, id);
            CharacterCreationLifestyleMutation mutation = new(
                CharacterCreationLifestyleMutationKinds.Create,
                LifestyleId,
                Configuration(LifestyleId));
            CharacterCreationLifestylePreview preview = Preview(service, state.Binding, mutation);
            var request = new CharacterCreationLifestyleConfirmRequest(
                state.Binding,
                mutation,
                preview.PreviewDigest,
                "idem-001",
                ExplicitlyConfirmed: false);
            CharacterCreationLifestyleResult<CharacterCreationLifestyleReceipt> unconfirmed = service.Confirm(request);
            CollectionAssert.Contains(
                unconfirmed.Blockers.ToArray(),
                CharacterCreationLifestylesBlockers.ExplicitConfirmationRequired);
            Assert.AreEqual(1L, store.Get(id).Value!.ContentRevision);

            CharacterCreationLifestyleResult<CharacterCreationLifestyleReceipt> applied = service.Confirm(
                request with { ExplicitlyConfirmed = true });
            Assert.AreEqual(CharacterCreationLifestyleOutcomes.Applied, applied.Outcome);
            Assert.AreEqual(CharacterCreationLifestyleOutcomes.Replayed, service.Confirm(
                request with { ExplicitlyConfirmed = true }).Outcome);
            CharacterCreationLifestyleResult<CharacterCreationLifestyleReceipt> conflict = service.Confirm(
                request with
                {
                    Mutation = mutation with
                    {
                        Configuration = Configuration(LifestyleId) with { Name = "Different" }
                    },
                    ExplicitlyConfirmed = true
                });
            Assert.AreEqual(CharacterCreationLifestyleOutcomes.Conflict, conflict.Outcome);
            CollectionAssert.Contains(conflict.Blockers.ToArray(), CharacterCreationLifestylesBlockers.IdempotencyConflict);
        });
    }

    [TestMethod]
    public void File_store_fault_rolls_back_document_revision_checkpoint_and_receipt_lane()
    {
        string directory = CreateStateDirectory();
        try
        {
            var normalStore = new FileWorkspaceStore(directory);
            CharacterWorkspaceId id = new("creation-lifestyle-fault");
            Assert.IsTrue(normalStore.CreateWorkspaceDocument(id, Document(Fixture())).Success);
            var normalService = new CharacterCreationLifestylesService(
                normalStore,
                new FakeResolver(Authority()));
            CharacterCreationLifestylesState state = Load(normalService, id);
            CharacterCreationLifestyleMutation mutation = new(
                CharacterCreationLifestyleMutationKinds.Create,
                LifestyleId,
                Configuration(LifestyleId));
            CharacterCreationLifestylePreview preview = Preview(normalService, state.Binding, mutation);
            string path = Path.Combine(directory, "workspaces", id.Value + ".json");
            byte[] before = File.ReadAllBytes(path);
            var failingStore = new FileWorkspaceStore(
                directory,
                new ThrowingFaultInjector(FileWorkspaceStoreFaultStage.AfterTempFileFlushed));
            var failingService = new CharacterCreationLifestylesService(
                failingStore,
                new FakeResolver(Authority()));

            CharacterCreationLifestyleResult<CharacterCreationLifestyleReceipt> failed = failingService.Confirm(
                new CharacterCreationLifestyleConfirmRequest(
                    state.Binding,
                    mutation,
                    preview.PreviewDigest,
                    "fault-001",
                    true));
            Assert.AreEqual(CharacterCreationLifestyleOutcomes.Unavailable, failed.Outcome);
            CollectionAssert.AreEqual(before, File.ReadAllBytes(path));
            WorkspaceStoredDocument reopened = new FileWorkspaceStore(directory).Get(id).Value!;
            Assert.AreEqual(1L, reopened.ContentRevision);
            Assert.AreEqual(0L, reopened.SavedRevision);
            Assert.IsNull(reopened.Document.AuxiliaryState.CharacterCreationLifestyleReceipts);
            Assert.IsFalse(XDocument.Parse(reopened.Document.Content).Root!.Element("lifestyles")!
                .Elements("lifestyle").Any());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void Career_mode_and_public_XML_or_dictionary_inputs_are_rejected()
    {
        WithService((_, service, id, _) =>
        {
            CharacterCreationLifestylesState state = Load(service, id);
            Assert.IsFalse(state.CanEdit);
            CollectionAssert.Contains(state.Blockers.ToArray(), CharacterCreationLifestylesBlockers.CareerModeRejected);
        }, Fixture().Replace("<created>False</created>", "<created>True</created>", StringComparison.Ordinal));

        Type[] types =
        [
            typeof(CharacterCreationLifestylesLoadRequest),
            typeof(CharacterCreationLifestylePreviewRequest),
            typeof(CharacterCreationLifestyleConfirmRequest),
            typeof(CharacterCreationLifestyleReceiptLookupRequest),
            typeof(CharacterCreationLifestyleMutation),
            typeof(CharacterCreationLifestyleConfiguration)
        ];
        foreach (Type type in types)
        {
            foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                Assert.IsFalse(typeof(XNode).IsAssignableFrom(property.PropertyType), $"{type.Name}.{property.Name}");
                Assert.IsFalse(property.PropertyType.IsGenericType
                    && property.PropertyType.GetGenericTypeDefinition() == typeof(IReadOnlyDictionary<,>),
                    $"{type.Name}.{property.Name}");
            }
        }
        Assert.IsFalse(typeof(CharacterCreationLifestyleConfirmRequest).GetProperties()
            .Any(property => property.PropertyType == typeof(CharacterCreationLifestyleAtomicWritePlan)));
    }

    private static CharacterCreationLifestylesState Load(
        ICharacterCreationLifestylesService service,
        CharacterWorkspaceId id)
    {
        CharacterCreationLifestyleResult<CharacterCreationLifestylesState> result = service.Load(
            new CharacterCreationLifestylesLoadRequest(id));
        Assert.IsNotNull(result.Value);
        return result.Value;
    }

    private static CharacterCreationLifestylePreview Preview(
        ICharacterCreationLifestylesService service,
        CharacterCreationLifestyleBinding binding,
        CharacterCreationLifestyleMutation mutation)
    {
        CharacterCreationLifestyleResult<CharacterCreationLifestylePreview> result = service.Preview(
            new CharacterCreationLifestylePreviewRequest(binding, mutation));
        Assert.IsTrue(result.Success, string.Join(',', result.Blockers));
        return result.Value!;
    }

    private static void WithService(
        Action<FileWorkspaceStore, CharacterCreationLifestylesService, CharacterWorkspaceId, string> action,
        string? xml = null)
    {
        string directory = CreateStateDirectory();
        try
        {
            var store = new FileWorkspaceStore(directory);
            CharacterWorkspaceId id = new("creation-lifestyle-authority");
            Assert.IsTrue(store.CreateWorkspaceDocument(id, Document(xml ?? Fixture())).Success);
            action(
                store,
                new CharacterCreationLifestylesService(store, new FakeResolver(Authority())),
                id,
                directory);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static WorkspaceDocument Document(string xml) =>
        new(xml, "sr5", WorkspaceDocumentFormat.Chum5Xml);

    private static WorkspaceDocument ApplyDelete(WorkspaceDocument document, Guid id)
    {
        XDocument replacement = XDocument.Parse(document.Content, LoadOptions.PreserveWhitespace);
        Lifestyle(replacement, id).Remove();
        return document with
        {
            State = document.State with
            {
                Payload = replacement.ToString(SaveOptions.DisableFormatting)
            }
        };
    }

    private static CharacterCreationLifestyleReceiptLedgerEntry Entry(
        WorkspaceStoredDocument current,
        CharacterCreationLifestyleBinding binding,
        CharacterCreationLifestylePreview preview,
        Guid lifestyleId,
        char idempotencySeed,
        char commandSeed)
    {
        string idempotency = Sha(idempotencySeed);
        string command = Sha(commandSeed);
        long next = current.ContentRevision + 1;
        var candidate = new CharacterCreationLifestyleReceipt(
            CharacterCreationLifestylesSchemas.ReceiptV1,
            "creation-lifestyle-" + command["sha256:".Length..][..24],
            CharacterCreationWizardStepIds.ContactsLifestyles,
            current.Id,
            preview.MutationKind,
            lifestyleId,
            idempotency,
            command,
            current.ContentRevision,
            next,
            current.ContentRevision,
            next,
            current.SavedRevision,
            next,
            preview.WritePlan.ContentDigestBefore,
            preview.WritePlan.ContentDigestAfter,
            binding.SourceDigest,
            binding.RulesDigest,
            binding.RuntimeDigest,
            preview.BudgetBefore.Used,
            preview.BudgetAfter.Used,
            preview.BudgetAfter.Remaining,
            preview.WritePlan,
            string.Empty);
        CharacterCreationLifestyleReceipt receipt = candidate with
        {
            ReceiptDigest = CharacterCreationLifestylesRules.ComputeReceiptDigest(candidate)
        };
        return new CharacterCreationLifestyleReceiptLedgerEntry(idempotency, command, receipt);
    }

    private static CharacterCreationLifestyleConfiguration Configuration(Guid id) => new(
        id,
        $"lifestyle:{LowSourceId:D}",
        "Low apartment",
        CharacterCreationLifestyleStyleIds.Standard,
        CharacterCreationLifestyleIncrementIds.Month,
        1,
        100m,
        0,
        false,
        false,
        0,
        0,
        0,
        0,
        "Vienna",
        "Innere Stadt",
        "First",
        []);

    private static CharacterCreationLifestylesAuthority Authority(bool includeQuality = false)
    {
        CharacterCreationLifestyleCatalogOption low = LifestyleOption(
            LowSourceId,
            "Low",
            2_000m,
            selectable: true);
        CharacterCreationLifestyleCatalogOption high = LifestyleOption(
            HighSourceId,
            "High",
            10_000m,
            selectable: false);
        var qualityCandidate = new CharacterCreationLifestyleQualityCatalogOption(
            $"lifestyle-quality:{QualitySourceId:D}",
            QualitySourceId,
            "Home Security",
            "Positive",
            "HT",
            "140",
            CharacterCreationLifestyleQualityTypes.Positive,
            1,
            100m,
            0m,
            0m,
            0,
            0,
            0,
            0,
            0,
            0,
            [],
            true,
            true,
            [],
            [$"lifestyles.xml#quality:{QualitySourceId:D}"],
            string.Empty);
        CharacterCreationLifestyleQualityCatalogOption quality = qualityCandidate with
        {
            OptionDigest = CharacterCreationLifestylesRules.ComputeQualityOptionDigest(qualityCandidate)
        };
        var candidate = new CharacterCreationLifestylesAuthority(
            CharacterCreationLifestylesSchemas.AuthorityV1,
            "sr5",
            "default.xml",
            [low, high],
            includeQuality ? [quality] : [],
            0,
            false,
            CharacterCreationLifestyleSourceAnchors.All,
            [],
            true,
            Sha('a'),
            Sha('b'),
            Sha('c'),
            Sha('d'),
            string.Empty);
        return candidate with
        {
            AuthorityDigest = CharacterCreationLifestylesRules.ComputeAuthorityDigest(candidate)
        };
    }

    private static CharacterCreationLifestyleCatalogOption LifestyleOption(
        Guid id,
        string name,
        decimal cost,
        bool selectable)
    {
        var candidate = new CharacterCreationLifestyleCatalogOption(
            $"lifestyle:{id:D}",
            id,
            name,
            cost,
            3,
            60m,
            3,
            100m,
            100m,
            100m,
            1,
            3,
            1,
            3,
            1,
            3,
            true,
            CharacterCreationLifestyleIncrementIds.Month,
            "SR5",
            "369",
            [],
            selectable,
            true,
            selectable ? [] : [CharacterCreationLifestylesBlockers.SourceDisabled],
            [$"lifestyles.xml#lifestyle:{id:D}"],
            string.Empty);
        return candidate with
        {
            OptionDigest = CharacterCreationLifestylesRules.ComputeOptionDigest(candidate)
        };
    }

    private static string Fixture(bool includeExisting = false) => $"""
<character>
  <created>False</created>
  <gameedition>SR5</gameedition>
  <settings>default.xml</settings>
  <buildmethod>Priority</buildmethod>
  <startingnuyen>10000</startingnuyen>
  <nuyenbp>0</nuyenbp>
  <improvements />
  <lifestyles>{(includeExisting ? ExistingLifestyle(LifestyleId, "Low apartment") + ExistingLifestyle(SiblingId, "Sibling home") : string.Empty)}</lifestyles>
  <root-sentinel><value>untouched</value></root-sentinel>
</character>
""";

    private static string ExistingLifestyle(Guid id, string name) => $"""
    <lifestyle custom="keep">
      <sourceid>{LowSourceId:D}</sourceid><guid>{id:D}</guid><name>{name}</name>
      <cost>2000</cost><dice>3</dice><lp>3</lp><baselifestyle>Low</baselifestyle><multiplier>60</multiplier>
      <months>1</months><roommates>0</roommates><percentage>100</percentage>
      <area>0</area><comforts>0</comforts><security>0</security><bonuslp>0</bonuslp>
      <trustfund>False</trustfund><splitcostwithroommates>False</splitcostwithroommates>
      <type>Standard</type><increment>Month</increment><city>Vienna</city><district>Innere Stadt</district><borough>First</borough>
      <lifestylequalities /><chummercomplete><sentinel>42</sentinel></chummercomplete>
    </lifestyle>
""";

    private static XElement Lifestyle(XDocument document, Guid id) => document.Root!
        .Element("lifestyles")!.Elements("lifestyle").Single(row =>
            row.Element("guid")?.Value == id.ToString("D"));

    private static string Sha(char value) => "sha256:" + new string(value, 64);

    private static void AssertDigests(params string[] values)
    {
        foreach (string value in values)
        {
            Assert.AreEqual(71, value.Length);
            StringAssert.StartsWith(value, "sha256:");
        }
    }

    private static string CreateStateDirectory()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            "chummer-creation-lifestyles-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class FakeResolver : ICharacterSourceDataResolver
    {
        private readonly CharacterCreationLifestylesAuthority _authority;

        public FakeResolver(CharacterCreationLifestylesAuthority authority)
        {
            _authority = authority;
        }

        public ICharacterSourceDataContext? TryCreateContext(string characterXml)
        {
            _ = characterXml;
            return new FakeContext(_authority);
        }
    }

    private sealed class FakeContext : ICharacterSourceDataContext
    {
        private readonly CharacterCreationLifestylesAuthority _authority;

        public FakeContext(CharacterCreationLifestylesAuthority authority)
        {
            _authority = authority;
        }

        public bool TryResolveCreationLifestylesAuthority(
            out CharacterCreationLifestylesAuthority authority)
        {
            authority = _authority;
            return true;
        }

        public bool TryResolveCyberwareGradeDeviceRating(
            string gradeName,
            string improvementSource,
            out int deviceRating)
        {
            _ = gradeName;
            _ = improvementSource;
            deviceRating = 0;
            return false;
        }

        public bool TryResolveVehicleModBonuses(
            string sourceId,
            string name,
            out CharacterVehicleModSourceBonuses bonuses)
        {
            _ = sourceId;
            _ = name;
            bonuses = CharacterVehicleModSourceBonuses.Empty;
            return false;
        }
    }

    private sealed class ThrowingFaultInjector : IFileWorkspaceStoreFaultInjector
    {
        private readonly FileWorkspaceStoreFaultStage _stage;

        public ThrowingFaultInjector(FileWorkspaceStoreFaultStage stage)
        {
            _stage = stage;
        }

        public void OnStage(FileWorkspaceStoreFaultStage stage, string targetPath, string tempPath)
        {
            _ = targetPath;
            _ = tempPath;
            if (stage == _stage)
                throw new IOException("Injected creation-lifestyle commit failure.");
        }
    }
}

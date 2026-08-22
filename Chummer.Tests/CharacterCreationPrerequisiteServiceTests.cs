using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Rulesets;
using Chummer.Contracts.Workspaces;
using Chummer.Infrastructure.Workspaces;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public sealed class CharacterCreationPrerequisiteServiceTests
{
    [TestMethod]
    public void Priority_preview_exposes_global_karma_and_base_attribute_grant_without_writing()
    {
        WithWorkspace(
            CharacterCreationBuildMethods.Priority,
            CreateAuthority(CharacterCreationBuildMethods.Priority, ["A", "B", "C", "D", "E"]),
            (store, service, id) =>
            {
                CharacterCreationPrerequisiteState state = Load(service, id);
                Assert.AreEqual(25m, state.CreationKarmaBudget.Total);
                Assert.AreEqual(0m, state.CreationKarmaBudget.Used);
                Assert.AreEqual(25m, state.CreationKarmaBudget.Remaining);
                Assert.IsTrue(state.CreationKarmaBudget.IsExact);
                Assert.IsNull(state.PendingDraft);

                CharacterCreationFoundationResult<CharacterCreationPrerequisitePreview> result =
                    service.Preview(PreviewRequest(
                        state.Binding,
                        Assign("A", "B", "C", "D", "E")));

                Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, result.Outcome);
                Assert.IsNotNull(result.Value);
                Assert.AreEqual(16, result.Value.BaseNormalAttributePoints);
                Assert.IsFalse(result.Value.RequiresMetatypeAttributeAdjustment);
                Assert.AreEqual(10, result.Value.SumToTenUsed);
                Assert.IsTrue(result.Value.CanConfirm);
                WorkspaceStoredDocument unchanged = store.Get(id).Value!;
                Assert.AreEqual(1L, unchanged.ContentRevision);
                Assert.AreEqual(0L, unchanged.SavedRevision);
                Assert.IsNull(unchanged.Document.AuxiliaryState.CharacterCreationPrerequisiteDraft);
            });
    }

    [TestMethod]
    public void Heritage_and_talent_children_are_explicit_and_digest_bound()
    {
        WithWorkspace(
            CharacterCreationBuildMethods.Priority,
            CreateAuthority(CharacterCreationBuildMethods.Priority, ["A", "B", "C", "D", "E"]),
            (_, service, id) =>
            {
                CharacterCreationPrerequisiteState state = Load(service, id);
                IReadOnlyDictionary<string, string> ranks = Assign("A", "E", "B", "C", "D");
                CharacterCreationFoundationResult<CharacterCreationPrerequisitePreview> missing =
                    service.Preview(new CharacterCreationPrerequisitePreviewRequest(
                        state.Binding,
                        ranks));
                CharacterCreationFoundationResult<CharacterCreationPrerequisitePreview> wrong =
                    service.Preview(new CharacterCreationPrerequisitePreviewRequest(
                        state.Binding,
                        ranks)
                    {
                        HeritageSelectionId = "human",
                        TalentSelectionId = "forged"
                    });

                Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, missing.Outcome);
                CollectionAssert.Contains(missing.Blockers.ToList(),
                    CharacterCreationPrerequisiteBlockers.HeritageSelectionIncomplete);
                CollectionAssert.Contains(missing.Blockers.ToList(),
                    CharacterCreationPrerequisiteBlockers.TalentSelectionIncomplete);
                Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, wrong.Outcome);
                CollectionAssert.Contains(wrong.Blockers.ToList(),
                    CharacterCreationPrerequisiteBlockers.TalentSelectionInvalid);
            });
    }

    [TestMethod]
    public void Priority_requires_the_profile_multiset_and_preserves_duplicate_rank_arrays()
    {
        CharacterCreationPrerequisiteAuthority authority = CreateAuthority(
            CharacterCreationBuildMethods.Priority,
            ["B", "C", "D", "E", "E"]);
        WithWorkspace(
            CharacterCreationBuildMethods.Priority,
            authority,
            (_, service, id) =>
            {
                CharacterCreationPrerequisiteState state = Load(service, id);
                CharacterCreationPrerequisitePreview valid = service.Preview(
                    PreviewRequest(
                        state.Binding,
                        Assign("E", "B", "E", "D", "C"))).Value!;
                Assert.IsTrue(valid.CanConfirm);

                CharacterCreationFoundationResult<CharacterCreationPrerequisitePreview> invalid =
                    service.Preview(PreviewRequest(
                        state.Binding,
                        Assign("B", "B", "C", "D", "E")));
                Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, invalid.Outcome);
                CollectionAssert.Contains(
                    invalid.Blockers.ToList(),
                    CharacterCreationPrerequisiteBlockers.SelectionInvalid);
            });
    }

    [TestMethod]
    public void Sum_to_ten_accepts_repeated_ranks_only_at_the_exact_weight_total()
    {
        WithWorkspace(
            CharacterCreationBuildMethods.SumToTen,
            CreateAuthority(
                CharacterCreationBuildMethods.SumToTen,
                ["A", "B", "C", "D", "E"]),
            (_, service, id) =>
            {
                CharacterCreationPrerequisiteState state = Load(service, id);
                CharacterCreationPrerequisitePreview repeated = service.Preview(
                    PreviewRequest(
                        state.Binding,
                        Assign("A", "A", "D", "D", "E"))).Value!;
                Assert.AreEqual(10, repeated.SumToTenUsed);
                Assert.IsTrue(repeated.CanConfirm);

                CharacterCreationFoundationResult<CharacterCreationPrerequisitePreview> wrong =
                    service.Preview(PreviewRequest(
                        state.Binding,
                        Assign("A", "A", "E", "E", "E")));
                Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, wrong.Outcome);
                CollectionAssert.Contains(
                    wrong.Blockers.ToList(),
                    CharacterCreationPrerequisiteBlockers.SumToTenMismatch);
            });
    }

    [TestMethod]
    public void Confirm_is_atomic_checkpoints_and_leaves_character_xml_unchanged()
    {
        WithWorkspace(
            CharacterCreationBuildMethods.Priority,
            CreateAuthority(CharacterCreationBuildMethods.Priority, ["A", "B", "C", "D", "E"]),
            (store, service, id) =>
            {
                WorkspaceStoredDocument before = store.Get(id).Value!;
                CharacterCreationPrerequisiteState state = Load(service, id);
                CharacterCreationPrerequisitePreview preview = service.Preview(
                    PreviewRequest(
                        state.Binding,
                        Assign("A", "B", "C", "D", "E"))).Value!;

                CharacterCreationFoundationResult<CharacterCreationPrerequisiteReceipt> result =
                    service.Confirm(ConfirmRequest(
                        preview.Binding,
                        Assign("A", "B", "C", "D", "E"),
                        preview.PreviewDigest,
                        ExplicitlyConfirmed: true));

                Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, result.Outcome);
                Assert.IsNotNull(result.Value);
                Assert.IsFalse(result.Value.CharacterDocumentChanged);
                Assert.AreEqual(2L, result.Value.ContentRevision);
                Assert.AreEqual(2L, result.Value.SavedRevision);
                WorkspaceStoredDocument after = store.Get(id).Value!;
                Assert.AreEqual(before.Document.Content, after.Document.Content);
                Assert.IsNotNull(after.Document.AuxiliaryState.CharacterCreationPrerequisiteDraft);
                CharacterCreationPrerequisiteState resumed = Load(service, id);
                Assert.IsTrue(resumed.CanEnterAttributes);
                Assert.IsFalse(resumed.RequiresMetatypeAttributeAdjustment);
                Assert.AreEqual(16, resumed.BaseNormalAttributePoints);
                Assert.AreEqual(16, resumed.EffectiveNormalAttributePoints);
                Assert.AreEqual(1, resumed.TotalSpecialAttributePoints);

                CharacterCreationFoundationResult<CharacterCreationPrerequisitePreview> duplicate =
                    service.Preview(PreviewRequest(
                        resumed.Binding,
                        Assign("A", "B", "C", "D", "E")));
                Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, duplicate.Outcome);
                CollectionAssert.Contains(
                    duplicate.Blockers.ToList(),
                    CharacterCreationPrerequisiteBlockers.DraftDuplicate);
                Assert.AreEqual(2L, store.Get(id).Value!.ContentRevision);
            });
    }

    [TestMethod]
    public void Stale_binding_tampered_authority_and_legacy_priority_state_fail_closed()
    {
        CharacterCreationPrerequisiteAuthority authority = CreateAuthority(
            CharacterCreationBuildMethods.Priority,
            ["A", "B", "C", "D", "E"]);
        WithWorkspace(
            CharacterCreationBuildMethods.Priority,
            authority,
            (_, service, id) =>
            {
                CharacterCreationPrerequisiteState state = Load(service, id);
                CharacterCreationPrerequisiteBinding stale = state.Binding with
                {
                    AuthorityDigest = Digest(99)
                };
                CharacterCreationFoundationResult<CharacterCreationPrerequisitePreview> result =
                    service.Preview(PreviewRequest(
                        stale,
                        Assign("A", "B", "C", "D", "E")));
                Assert.AreEqual(CharacterCreationFoundationOutcomes.Conflict, result.Outcome);
                CollectionAssert.Contains(
                    result.Blockers.ToList(),
                    CharacterCreationPrerequisiteBlockers.PrioritiesSourceDrift);
            });

        CharacterCreationPrerequisiteAuthority forged = authority with
        {
            CreationKarmaTotal = 26
        };
        WithWorkspace(
            CharacterCreationBuildMethods.Priority,
            forged,
            (_, service, id) =>
            {
                CharacterCreationPrerequisiteState state = Load(service, id);
                CollectionAssert.Contains(
                    state.Blockers.ToList(),
                    CharacterCreationPrerequisiteBlockers.AuthorityUnavailable);
                Assert.IsFalse(state.CreationKarmaBudget.IsExact);
            });

        WithWorkspace(
            CharacterCreationBuildMethods.Priority,
            authority,
            (_, service, id) =>
            {
                CharacterCreationPrerequisiteState state = Load(service, id);
                CollectionAssert.Contains(
                    state.Blockers.ToList(),
                    CharacterCreationPrerequisiteBlockers.LegacyPriorityStateRequiresImport);
            },
            extraXml: "<priorityattributes>A</priorityattributes>");
    }

    [TestMethod]
    public void Confirmation_requires_explicit_consent_and_a_matching_preview_digest()
    {
        WithWorkspace(
            CharacterCreationBuildMethods.Priority,
            CreateAuthority(CharacterCreationBuildMethods.Priority, ["A", "B", "C", "D", "E"]),
            (store, service, id) =>
            {
                CharacterCreationPrerequisiteState state = Load(service, id);
                IReadOnlyDictionary<string, string> selection = Assign("A", "B", "C", "D", "E");
                CharacterCreationPrerequisitePreview preview = service.Preview(
                    PreviewRequest(state.Binding, selection)).Value!;

                CharacterCreationFoundationResult<CharacterCreationPrerequisiteReceipt> noConsent =
                    service.Confirm(ConfirmRequest(
                        preview.Binding,
                        selection,
                        preview.PreviewDigest,
                        ExplicitlyConfirmed: false));
                CharacterCreationFoundationResult<CharacterCreationPrerequisiteReceipt> tampered =
                    service.Confirm(ConfirmRequest(
                        preview.Binding,
                        selection,
                        Digest(88),
                        ExplicitlyConfirmed: true));

                Assert.AreEqual(CharacterCreationFoundationOutcomes.Invalid, noConsent.Outcome);
                Assert.AreEqual(CharacterCreationFoundationOutcomes.Conflict, tampered.Outcome);
                Assert.AreEqual(1L, store.Get(id).Value!.ContentRevision);
            });
    }

    private static CharacterCreationPrerequisiteState Load(
        ICharacterCreationPrerequisiteService service,
        CharacterWorkspaceId id)
    {
        CharacterCreationFoundationResult<CharacterCreationPrerequisiteState> result =
            service.Load(new CharacterCreationPrerequisiteLoadRequest(id));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, result.Outcome);
        Assert.IsNotNull(result.Value);
        return result.Value;
    }

    private static CharacterCreationPrerequisitePreviewRequest PreviewRequest(
        CharacterCreationPrerequisiteBinding binding,
        IReadOnlyDictionary<string, string> assignments) =>
        new(binding, assignments)
        {
            HeritageSelectionId = "human",
            TalentSelectionId = "mundane"
        };

    private static CharacterCreationPrerequisiteConfirmRequest ConfirmRequest(
        CharacterCreationPrerequisiteBinding binding,
        IReadOnlyDictionary<string, string> assignments,
        string previewDigest,
        bool ExplicitlyConfirmed) =>
        new(binding, assignments, previewDigest, ExplicitlyConfirmed)
        {
            HeritageSelectionId = "human",
            TalentSelectionId = "mundane"
        };

    private static void WithWorkspace(
        string buildMethod,
        CharacterCreationPrerequisiteAuthority authority,
        Action<FileWorkspaceStore, ICharacterCreationPrerequisiteService, CharacterWorkspaceId> action,
        string extraXml = "")
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"chummer-prerequisite-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            FileWorkspaceStore store = new(directory);
            CharacterWorkspaceId id = new("prerequisite-runner");
            Assert.IsTrue(store.CreateWorkspaceDocument(
                id,
                new WorkspaceDocument(CharacterXml(buildMethod, extraXml), RulesetDefaults.Sr5)).Success);
            var service = new CharacterCreationPrerequisiteService(
                store,
                new StubCharacterQueries(),
                new StubSourceResolver(authority));
            action(store, service, id);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string CharacterXml(string buildMethod, string extraXml) =>
        $"<character><name>Prerequisite Runner</name><alias>Priority</alias>"
        + $"<buildmethod>{buildMethod}</buildmethod><created>false</created>"
        + $"<karma>25</karma><nuyen>0</nuyen>{extraXml}</character>";

    internal static IReadOnlyDictionary<string, string> Assign(
        string heritage,
        string talent,
        string attributes,
        string skills,
        string resources) => new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [CharacterCreationPriorityCategoryIds.Heritage] = heritage,
        [CharacterCreationPriorityCategoryIds.Talent] = talent,
        [CharacterCreationPriorityCategoryIds.Attributes] = attributes,
        [CharacterCreationPriorityCategoryIds.Skills] = skills,
        [CharacterCreationPriorityCategoryIds.Resources] = resources
    };

    internal static CharacterCreationPrerequisiteAuthority CreateAuthority(
        string buildMethod,
        IReadOnlyList<string> priorityArray)
    {
        Dictionary<string, int> weights = new(StringComparer.Ordinal)
        {
            ["A"] = 4,
            ["B"] = 3,
            ["C"] = 2,
            ["D"] = 1,
            ["E"] = 0
        };
        CharacterCreationPriorityRankWeight[] rankWeights = weights.Select(pair =>
                new CharacterCreationPriorityRankWeight(
                    pair.Key,
                    pair.Value,
                    [$"priorities.xml#weight:{pair.Key}"]))
            .ToArray();
        Dictionary<string, int> attributePoints = new(StringComparer.Ordinal)
        {
            ["A"] = 24,
            ["B"] = 20,
            ["C"] = 16,
            ["D"] = 14,
            ["E"] = 12
        };
        var options = new List<CharacterCreationPriorityOptionProjection>();
        int sequence = 1;
        foreach (string category in CharacterCreationPriorityCategoryIds.Ordered)
        {
            foreach (string rank in priorityArray.Distinct(StringComparer.Ordinal))
            {
                string id = $"00000000-0000-0000-0000-{sequence:000000000000}";
                CharacterCreationPriorityOptionProjection option = new(
                    category,
                    category,
                    rank,
                    id,
                    $"{category}-{rank}",
                    weights[rank],
                    category == CharacterCreationPriorityCategoryIds.Attributes
                        ? attributePoints[rank]
                        : null,
                    Digest(sequence),
                    [$"priorities.xml#priority:{id}"]);
                if (category == CharacterCreationPriorityCategoryIds.Heritage)
                {
                    option = option with
                    {
                        HeritageOptions = [HumanOption(id)]
                    };
                }
                else if (category == CharacterCreationPriorityCategoryIds.Talent)
                {
                    option = option with
                    {
                        TalentOptions = [MundaneOption(id)]
                    };
                }
                options.Add(option);
                sequence++;
            }
        }
        var authority = new CharacterCreationPrerequisiteAuthority(
            CharacterCreationPrerequisiteSchemas.AuthorityV1,
            "223a11ff-80e0-428b-89a9-6ef1c243b8b6",
            buildMethod,
            25,
            priorityArray.ToArray(),
            "Standard",
            10,
            rankWeights,
            options,
            Digest(41),
            Digest(42),
            Digest(43),
            Digest(44),
            ["settings.xml#setting:test", "priorities.xml"],
            [],
            IsAuthoritative: true,
            AuthorityDigest: string.Empty);
        authority = authority with
        {
            RawMetatypesXmlDigest = Digest(45),
            EffectiveMetatypesInputsDigest = Digest(46),
            MaxNumberMaxAttributesCreate = 1,
            KarmaAttribute = 5,
            AlternateMetatypeAttributeKarma = false,
            ReverseAttributePriorityOrder = false
        };
        return authority with
        {
            AuthorityDigest = CharacterCreationPrerequisiteAuthorityDigest.Compute(authority)
        };
    }

    internal static CharacterCreationPriorityHeritageOptionProjection HumanOption(string priorityId) =>
        new(
            "human",
            CharacterCreationPriorityChildKinds.Metatype,
            "a53d885d-a4a4-443d-b6a6-b0a55b0a96c7",
            null,
            "Human",
            null,
            1,
            0,
            false,
            HumanAttributes(),
            Digest(50),
            Digest(51),
            IsEnabled: true,
            Blockers: [],
            SourceAnchorIds: [$"priorities.xml#priority:{priorityId}:heritage:0", "metatypes.xml#metatype:human"]);

    internal static CharacterCreationPriorityTalentOptionProjection MundaneOption(string priorityId) =>
        new(
            "mundane",
            "Mundane",
            "Mundane",
            0,
            null,
            null,
            null,
            [],
            Digest(52),
            IsEnabled: true,
            Blockers: [],
            SourceAnchorIds: [$"priorities.xml#priority:{priorityId}:talent:0"]);

    internal static CharacterCreationMetatypeAttributeProjection[] HumanAttributes() =>
    [
        new("BOD", 1, 6, 10), new("AGI", 1, 6, 10), new("REA", 1, 6, 10),
        new("STR", 1, 6, 10), new("CHA", 1, 6, 10), new("INT", 1, 6, 10),
        new("LOG", 1, 6, 10), new("WIL", 1, 6, 10), new("EDG", 2, 7, 7),
        new("MAG", 1, 6, 6), new("RES", 1, 6, 6), new("ESS", 0, 6, 6),
        new("DEP", 0, 0, 0)
    ];

    private static string Digest(int value) =>
        "sha256:" + value.ToString("x64");

    private sealed class StubCharacterQueries : ICharacterFileQueries
    {
        public CharacterFileSummary ParseSummary(CharacterDocument document)
        {
            XElement root = XDocument.Parse(document.Content).Root!;
            return new CharacterFileSummary(
                root.Element("name")?.Value ?? string.Empty,
                root.Element("alias")?.Value ?? string.Empty,
                root.Element("metatype")?.Value ?? string.Empty,
                root.Element("buildmethod")?.Value ?? string.Empty,
                string.Empty,
                string.Empty,
                25,
                0,
                bool.TryParse(root.Element("created")?.Value, out bool created) && created);
        }

        public CharacterValidationResult Validate(CharacterDocument document) =>
            new(true, []);
    }

    private sealed class StubSourceResolver : ICharacterSourceDataResolver
    {
        private readonly CharacterCreationPrerequisiteAuthority _authority;

        public StubSourceResolver(CharacterCreationPrerequisiteAuthority authority)
        {
            _authority = authority;
        }

        public ICharacterSourceDataContext TryCreateContext(string characterXml) =>
            new StubSourceContext(_authority);
    }

    private sealed class StubSourceContext : ICharacterSourceDataContext
    {
        private readonly CharacterCreationPrerequisiteAuthority _authority;

        public StubSourceContext(CharacterCreationPrerequisiteAuthority authority)
        {
            _authority = authority;
        }

        public bool TryResolveCreationPrerequisiteAuthority(
            out CharacterCreationPrerequisiteAuthority authority)
        {
            authority = _authority;
            return true;
        }

        public bool TryResolveCyberwareGradeDeviceRating(
            string gradeName,
            string improvementSource,
            out int deviceRating)
        {
            deviceRating = 0;
            return false;
        }

        public bool TryResolveVehicleModBonuses(
            string sourceId,
            string name,
            out CharacterVehicleModSourceBonuses bonuses)
        {
            bonuses = CharacterVehicleModSourceBonuses.Empty;
            return false;
        }
    }
}

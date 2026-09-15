using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Application.Explain;
using Chummer.Application.Owners;
using Chummer.Application.Workspaces;
using Chummer.Contracts.BuildGhost;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Rulesets;
using Chummer.Contracts.Workspaces;
using Chummer.Infrastructure.DependencyInjection;
using Chummer.Infrastructure.Explain;
using Chummer.Infrastructure.Files;
using Chummer.Infrastructure.Owners;
using Chummer.Infrastructure.Workspaces;
using Chummer.Infrastructure.Xml;
using Chummer.Rulesets.Hosting;
using Chummer.Rulesets.Sr5;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public sealed class WorkspaceRuleQuestionServiceTests
{
    private static readonly CharacterWorkspaceId WorkspaceId = new("quality-explain");
    private static readonly OwnerScope Owner = new("owner-a");
    private const string SettingsId = "223a11ff-80e0-428b-89a9-6ef1c243b8b6";
    private const string SourceId = "50000000-0000-0000-0000-000000000001";
    private const string SubjectId = "70000000-0000-0000-0000-000000000001";
    private const string SecondSubjectId = "70000000-0000-0000-0000-000000000002";
    private const string QualityName = "Grounded Quality";

    [TestMethod]
    public void Resolve_returns_real_sr5_grouped_level_and_exact_current_binding_without_writes()
    {
        using var fixture = new Fixture();
        WorkspaceStoredDocument stored = fixture.Read();
        Assert.IsTrue(fixture.Codec.Validate(stored.Document.PayloadEnvelope).IsValid);
        var section = (CharacterQualitiesSection)fixture.Codec.ParseSection("qualities", stored.Document.PayloadEnvelope);
        Assert.HasCount(2, section.Qualities);
        Assert.IsNotNull(section.Qualities[0].LevelSemantics);
        Assert.AreEqual(2, section.Qualities[0].LevelSemantics!.Level);
        Assert.AreEqual(3, section.Qualities[0].LevelSemantics!.MaximumLevel);
        Assert.IsNull(section.Qualities[1].LevelSemantics);
        ICharacterSourceDataContext? context = fixture.Sources.TryCreateContext(stored.Document.Content);
        Assert.IsNotNull(context);
        Assert.IsTrue(context.TryResolveCreationSourceProfile(out CharacterCreationSourceProfileAuthority profile));
        Assert.IsTrue(context.TryResolveQualityLevelSource(SourceId, QualityName, out CharacterQualityLevelSource source));
        Assert.IsTrue(source.SourceCitationResolved);
        string before = JsonSerializer.Serialize(stored);
        string[] paths = Directory.GetFiles(fixture.StateRoot, "*.json", SearchOption.AllDirectories);
        byte[][] bytes = paths.Select(File.ReadAllBytes).ToArray();
        DateTime[] timestamps = paths.Select(File.GetLastWriteTimeUtc).ToArray();
        WorkspaceRuleQuestionRequest request = fixture.Request();
        string requestBefore = JsonSerializer.Serialize(request);
        int leasesBefore = fixture.Owners.SuccessfulLeaseAcquisitions;

        WorkspaceRuleQuestionResult result = fixture.CreateService().Resolve(fixture.Owners.Capture(), request);

        AssertResolved(result);
        Assert.AreEqual(2, result.Level);
        Assert.AreEqual(3, result.MaximumLevel);
        WorkspaceRuleQuestionBinding binding = result.Binding!;
        Assert.AreEqual(WorkspaceRuleQuestionSchemas.BindingV1, binding.Schema);
        Assert.AreEqual(Owner.Value, binding.OwnerId);
        Assert.IsFalse(binding.TrustedLocalOwner);
        Assert.AreEqual(fixture.Owners.Capture().AuthorityInstanceId, binding.OwnerAuthorityInstanceId);
        Assert.AreEqual(fixture.Owners.Capture().TransitionRevision, binding.OwnerTransitionRevision);
        Assert.AreEqual(WorkspaceId, binding.WorkspaceId);
        Assert.AreEqual("sr5", binding.RulesetId);
        Assert.AreEqual(2L, binding.ContentRevision);
        Assert.AreEqual(1L, binding.SavedRevision);
        Assert.AreEqual(CharacterCreationBootstrapActivationIntegrity.ComputeDocumentDigest(stored.Document), binding.WorkspaceDocumentDigest);
        Assert.AreEqual(profile.SettingsProfileId, binding.SettingsProfileId);
        Assert.AreEqual(profile.RawProfileInputsDigest, binding.SourceProfileDigest);
        Assert.AreEqual(source.SourceNodeDigest, binding.SourceNodeDigest);
        Assert.AreEqual(request.Intent, binding.Intent);
        Assert.AreEqual(request.SubjectId, binding.SubjectId);
        Assert.AreEqual(request.Locale, binding.Locale);
        Assert.AreEqual(WorkspaceRuleQuestionIntents.QualityLevelRuleId, result.Explanation.RuleId);
        WorkspaceRuleSourceAnchor anchor = result.SourceAnchors.Single();
        Assert.AreEqual("SG", anchor.SourceBook, "Saved SR5/page 111 must not override the active source node.");
        Assert.AreEqual(224, anchor.Page);
        Assert.AreEqual(SourceId, anchor.QualitySourceId);
        Assert.AreEqual(binding.RulesetId, anchor.RulesetId);
        Assert.AreEqual(binding.SettingsProfileId, anchor.SettingsProfileId);
        Assert.AreEqual(binding.SourceNodeDigest, anchor.SourceNodeDigest);
        Assert.AreEqual(binding.SourceProfileDigest, anchor.SourceProfileDigest);
        Assert.IsTrue(anchor.CalculationTrace.Count > 0);
        CollectionAssert.AreEqual(new[] { anchor.AnchorId }, result.Explanation.SourceAnchorIds.ToArray());
        Assert.AreEqual(WorkspaceRuleQuestionSchemas.ExecutingModulesV1, binding.EngineIdentityKind);
        Assert.AreEqual(WorkspaceRuleQuestionIntegrity.ComputeEngineFingerprint(binding.ExecutingModules), binding.EngineFingerprint);
        foreach (Type type in new[] { typeof(WorkspaceRuleQuestionService), typeof(Sr5WorkspaceCodec), typeof(FileSystemCharacterSourceDataResolver) })
        {
            WorkspaceRuleExecutingModule module = binding.ExecutingModules.Single(item => item.ImplementationType == type.FullName);
            Assert.AreEqual(type.Assembly.GetName().Name, module.AssemblyName);
            Assert.AreEqual(type.Module.ModuleVersionId, module.ModuleVersionId);
        }
        Assert.AreEqual(before, JsonSerializer.Serialize(fixture.Read()));
        Assert.AreEqual(requestBefore, JsonSerializer.Serialize(request));
        CollectionAssert.AreEquivalent(paths, Directory.GetFiles(fixture.StateRoot, "*.json", SearchOption.AllDirectories));
        for (int index = 0; index < paths.Length; index++)
        {
            CollectionAssert.AreEqual(bytes[index], File.ReadAllBytes(paths[index]));
            Assert.AreEqual(timestamps[index], File.GetLastWriteTimeUtc(paths[index]));
        }
        Assert.AreEqual(0, fixture.Owners.ActiveLeases);
        Assert.AreEqual(leasesBefore + 1, fixture.Owners.SuccessfulLeaseAcquisitions);
    }

    [TestMethod]
    public void Workspace_with_no_saved_checkpoint_still_resolves_from_its_real_current_document()
    {
        using var fixture = new Fixture(checkpoint: false);
        WorkspaceStoredDocument before = fixture.Read();
        Assert.AreEqual(2L, before.ContentRevision);
        Assert.AreEqual(0L, before.SavedRevision);

        WorkspaceRuleQuestionResult result = fixture.CreateService().Resolve(fixture.Owners.Capture(), fixture.Request());

        AssertResolved(result);
        Assert.AreEqual(2, result.Level);
        Assert.AreEqual(3, result.MaximumLevel);
        Assert.AreEqual(before.ContentRevision, result.Binding!.ContentRevision);
        Assert.AreEqual(0L, result.Binding.SavedRevision);
        Assert.AreEqual(CharacterCreationBootstrapActivationIntegrity.ComputeDocumentDigest(before.Document), result.Binding.WorkspaceDocumentDigest);
        Assert.AreEqual(JsonSerializer.Serialize(before), JsonSerializer.Serialize(fixture.Read()));
    }

    [TestMethod]
    public void Active_source_book_code_uses_the_real_resolvers_case_insensitive_profile_admission()
    {
        using var fixture = new Fixture(sourceMutation: root =>
            root.Element("qualities")!.Element("quality")!.Element("source")!.Value = "sg");
        ICharacterSourceDataContext? context = fixture.Sources.TryCreateContext(fixture.Read().Document.Content);
        Assert.IsNotNull(context);
        Assert.IsTrue(context.TryResolveCreationSourceProfile(out CharacterCreationSourceProfileAuthority profile));
        CollectionAssert.Contains(profile.EnabledSourcebooks.ToArray(), "SG");
        Assert.IsTrue(context.TryResolveQualityLevelSource(SourceId, QualityName, out CharacterQualityLevelSource source));
        Assert.IsTrue(source.SourceCitationResolved);
        Assert.AreEqual("sg", source.SourceBook);

        WorkspaceRuleQuestionResult result = fixture.CreateService().Resolve(fixture.Owners.Capture(), fixture.Request());

        AssertResolved(result);
        Assert.AreEqual(2, result.Level);
        Assert.AreEqual(3, result.MaximumLevel);
        Assert.AreEqual(source.SourceBook, result.SourceAnchors.Single().SourceBook);
        Assert.AreEqual(source.SourceNodeDigest, result.Binding!.SourceNodeDigest);
    }

    [TestMethod]
    public void Throwing_owner_lease_stamp_is_disposed_once_without_workspace_reads()
    {
        using var fixture = new Fixture();
        var authority = new ThrowingStampOwner(fixture.Owners);
        var observedStore = new ObservedStore(fixture.Store, fixture.Owners);

        WorkspaceRuleQuestionResult result = fixture.CreateService(authority, observedStore)
            .Resolve(authority.Capture(), fixture.Request());

        AssertUnresolved(result);
        Assert.AreEqual(1, authority.StampReadCount);
        Assert.AreEqual(1, authority.DisposeCount);
        Assert.AreEqual(1, fixture.Owners.SuccessfulLeaseAcquisitions);
        Assert.AreEqual(0, fixture.Owners.ActiveLeases);
        Assert.AreEqual(0, observedStore.ReadCount);
    }

    [TestMethod]
    [DataRow("Selected", 5, false)]
    [DataRow("Metatype", 0, false)]
    [DataRow("Selected", 5, true)]
    public void Unrelated_ordinary_or_metatype_quality_does_not_invalidate_the_target_level_group(string origin, int bp, bool structuredBonus)
    {
        const string unrelatedSourceId = "50000000-0000-0000-0000-000000000099";
        const string unrelatedGuid = "70000000-0000-0000-0000-000000000099";
        using var fixture = new Fixture(
            characterMutation: root => root.Element("qualities")!.Add(XElement.Parse(
                $"<quality><guid>{unrelatedGuid}</guid><sourceid>{unrelatedSourceId}</sourceid><name>Unrelated Quality</name>"
                + $"<qualitytype>Positive</qualitytype><qualitysource>{origin}</qualitysource><bp>{bp}</bp>"
                + "<extra/><sourcename/><notes/><source>SR5</source><page>80</page>"
                + (structuredBonus ? "<bonus><armor>1</armor></bonus>" : "<bonus/>") + "</quality>")),
            sourceMutation: root => root.Element("qualities")!.Add(XElement.Parse(
                $"<quality><id>{unrelatedSourceId}</id><name>Unrelated Quality</name><category>Positive</category>"
                + "<karma>5</karma><source>SR5</source><page>80</page>"
                + (structuredBonus ? "<bonus><armor>1</armor></bonus>" : "<bonus/>") + "</quality>")));
        var section = (CharacterQualitiesSection)fixture.Codec.ParseSection("qualities", fixture.Read().Document.PayloadEnvelope);
        Assert.HasCount(3, section.Qualities);
        Assert.AreEqual(2, section.Qualities.Single(item => item.Guid == SubjectId).LevelSemantics!.Level);

        WorkspaceRuleQuestionResult result = fixture.CreateService().Resolve(fixture.Owners.Capture(), fixture.Request());

        AssertResolved(result);
        Assert.AreEqual(2, result.Level);
        Assert.AreEqual(3, result.MaximumLevel);
        Assert.AreEqual(SourceId, result.SourceAnchors.Single().QualitySourceId);
    }

    [TestMethod]
    [DataRow("stale-revision")]
    [DataRow("future-revision")]
    [DataRow("zero-revision")]
    [DataRow("request-schema")]
    [DataRow("request-ruleset")]
    [DataRow("unknown-intent")]
    [DataRow("unknown-subject")]
    [DataRow("invalid-subject")]
    [DataRow("missing-workspace")]
    public void Unadmitted_request_returns_no_claims(string mutation)
    {
        using var fixture = new Fixture();
        WorkspaceRuleQuestionRequest request = fixture.Request();
        request = mutation switch
        {
            "stale-revision" => request with { ExpectedContentRevision = 1 },
            "future-revision" => request with { ExpectedContentRevision = 3 },
            "zero-revision" => request with { ExpectedContentRevision = 0 },
            "request-schema" => request with { Schema = "unknown" },
            "request-ruleset" => request with { RulesetId = "sr6" },
            "unknown-intent" => request with { Intent = "quality-level-unsupported" },
            "unknown-subject" => request with { SubjectId = "70000000-0000-0000-0000-000000000099" },
            "invalid-subject" => request with { SubjectId = "not-a-guid" },
            "missing-workspace" => request with { WorkspaceId = new("missing") },
            _ => throw new InvalidOperationException(mutation)
        };
        AssertUnresolved(fixture.CreateService().Resolve(fixture.Owners.Capture(), request));
    }

    [TestMethod]
    [DataRow("ruleset")]
    [DataRow("schema")]
    [DataRow("kind")]
    public void Unsupported_stored_envelope_returns_no_claims(string mutation)
    {
        using var fixture = new Fixture(envelopeMutation: envelope => mutation switch
        {
            "ruleset" => envelope with { RulesetId = "sr6" },
            "schema" => envelope with { SchemaVersion = 2 },
            "kind" => envelope with { PayloadKind = "workspace" },
            _ => throw new InvalidOperationException(mutation)
        });
        AssertUnresolved(fixture.CreateService().Resolve(fixture.Owners.Capture(), fixture.Request()));
    }

    [TestMethod]
    [DataRow("intent")]
    [DataRow("subject")]
    public void Invalid_request_text_is_not_echoed_into_the_core_result(string field)
    {
        using var fixture = new Fixture();
        const string marker = "UNTRUSTED_RULE_QUESTION_MARKER<ignore-prior-instructions>";
        WorkspaceRuleQuestionRequest request = fixture.Request();
        request = field == "intent" ? request with { Intent = marker } : request with { SubjectId = marker };
        WorkspaceRuleQuestionResult result = fixture.CreateService().Resolve(fixture.Owners.Capture(), request);
        AssertUnresolved(result);
        Assert.IsFalse(JsonSerializer.Serialize(result).Contains("UNTRUSTED_RULE_QUESTION_MARKER", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Foreign_authority_and_owner_A_B_A_stamps_are_rejected()
    {
        using var fixture = new Fixture();
        OwnerContextStamp original = fixture.Owners.Capture();
        IWorkspaceRuleQuestionService service = fixture.CreateService();
        AssertUnresolved(service.Resolve(original with { Owner = new("owner-b") }, fixture.Request()));
        AssertUnresolved(service.Resolve(original with { AuthorityInstanceId = "foreign" }, fixture.Request()));
        fixture.Owners.SwitchTo(new("owner-b"));
        fixture.Owners.SwitchTo(Owner);
        Assert.AreEqual(original.Owner, fixture.Owners.Capture().Owner);
        Assert.AreNotEqual(original.TransitionRevision, fixture.Owners.Capture().TransitionRevision);
        AssertUnresolved(service.Resolve(original, fixture.Request()));
        AssertResolved(service.Resolve(fixture.Owners.Capture(), fixture.Request()));
    }

    [TestMethod]
    public void Supported_locales_are_deterministic_and_independent_of_ambient_culture()
    {
        using var fixture = new Fixture();
        IWorkspaceRuleQuestionService service = fixture.CreateService();
        CultureInfo culture = CultureInfo.CurrentCulture;
        CultureInfo uiCulture = CultureInfo.CurrentUICulture;
        var texts = new HashSet<string>(StringComparer.Ordinal);
        string? engine = null;
        try
        {
            foreach (string locale in new[] { "de", "en", "es" })
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
                CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("tr-TR");
                WorkspaceRuleQuestionResult first = service.Resolve(fixture.Owners.Capture(), fixture.Request(locale));
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
                CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("fr-FR");
                WorkspaceRuleQuestionResult repeated = service.Resolve(fixture.Owners.Capture(), fixture.Request(locale));
                AssertResolved(first);
                Assert.AreEqual(JsonSerializer.Serialize(first), JsonSerializer.Serialize(repeated));
                Assert.AreEqual(locale, first.Binding!.Locale);
                Assert.AreEqual(2, first.Level);
                Assert.AreEqual(3, first.MaximumLevel);
                engine ??= first.Binding.EngineFingerprint;
                Assert.AreEqual(engine, first.Binding.EngineFingerprint);
                texts.Add(first.Explanation.Explanation);
            }
            Assert.HasCount(3, texts, "Each supported locale needs its own explanation.");
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = uiCulture;
        }
    }

    [TestMethod]
    [DataRow("de-AT", "de")]
    [DataRow("en-GB", "en")]
    [DataRow("es-ES", "es")]
    public void Regional_locale_preserves_request_binding_and_uses_its_supported_language_family(string locale, string language)
    {
        using var fixture = new Fixture();
        IWorkspaceRuleQuestionService service = fixture.CreateService();
        WorkspaceRuleQuestionResult regional = service.Resolve(fixture.Owners.Capture(), fixture.Request(locale));
        WorkspaceRuleQuestionResult baseLanguage = service.Resolve(fixture.Owners.Capture(), fixture.Request(language));
        AssertResolved(regional);
        AssertResolved(baseLanguage);
        Assert.AreEqual(locale, regional.Binding!.Locale);
        Assert.AreEqual(language, baseLanguage.Binding!.Locale);
        Assert.AreEqual(baseLanguage.Explanation.Explanation, regional.Explanation.Explanation);
        Assert.AreEqual(baseLanguage.Explanation.Question, regional.Explanation.Question);
        Assert.AreEqual(baseLanguage.Binding.EngineFingerprint, regional.Binding.EngineFingerprint);
    }

    [TestMethod]
    public void Unsupported_blank_or_malformed_locale_returns_fixed_safe_fallback_without_relabeling()
    {
        using var fixture = new Fixture();
        IWorkspaceRuleQuestionService service = fixture.CreateService();
        string? fallback = null;
        foreach (string? locale in new string?[] { null, "", " ", "fr-FR", "en_US", "en--US", "-en", "en-", "UNTRUSTED_RULE_QUESTION_LOCALE_MARKER" })
        {
            WorkspaceRuleQuestionRequest request = fixture.Request() with { Locale = locale! };
            WorkspaceRuleQuestionResult result = service.Resolve(fixture.Owners.Capture(), request);
            AssertUnresolved(result);
            fallback ??= result.Explanation.Explanation;
            Assert.AreEqual(fallback, result.Explanation.Explanation);
            Assert.IsFalse(JsonSerializer.Serialize(result).Contains("UNTRUSTED_RULE_QUESTION_LOCALE_MARKER", StringComparison.Ordinal));
        }
    }

    [TestMethod]
    [DataRow("missing-source")]
    [DataRow("missing-page")]
    [DataRow("disabled-book")]
    [DataRow("zero-page")]
    [DataRow("invalid-page")]
    [DataRow("duplicate-page")]
    [DataRow("nested-page")]
    [DataRow("duplicate-source")]
    [DataRow("duplicate-quality")]
    [DataRow("duplicate-source-id")]
    [DataRow("missing-quality")]
    public void Missing_disabled_or_ambiguous_active_citation_returns_no_claims(string mutation)
    {
        using var fixture = new Fixture(sourceMutation: root =>
        {
            XElement quality = root.Element("qualities")!.Element("quality")!;
            switch (mutation)
            {
                case "missing-source": quality.Element("source")!.Remove(); break;
                case "missing-page": quality.Element("page")!.Remove(); break;
                case "disabled-book": quality.Element("source")!.Value = "DISABLED"; break;
                case "zero-page": quality.Element("page")!.Value = "0"; break;
                case "invalid-page": quality.Element("page")!.Value = "224x"; break;
                case "duplicate-page": quality.Add(new XElement("page", "225")); break;
                case "nested-page": quality.Element("page")!.ReplaceNodes(new XElement("nested", "224")); break;
                case "duplicate-source": quality.Add(new XElement("source", "SR5")); break;
                case "duplicate-quality": quality.AddAfterSelf(new XElement(quality)); break;
                case "duplicate-source-id": quality.Add(new XElement("id", SourceId)); break;
                case "missing-quality": quality.Remove(); break;
                default: throw new InvalidOperationException(mutation);
            }
        });
        AssertUnresolved(fixture.CreateService().Resolve(fixture.Owners.Capture(), fixture.Request()));
    }

    [TestMethod]
    [DataRow("duplicate-container")]
    [DataRow("duplicate-settings")]
    [DataRow("missing-settings")]
    [DataRow("nested-settings")]
    [DataRow("invalid-created")]
    [DataRow("missing-created")]
    [DataRow("duplicate-created")]
    [DataRow("invalid-bp")]
    [DataRow("missing-bp")]
    [DataRow("empty-bp")]
    [DataRow("duplicate-bp")]
    [DataRow("nested-bp")]
    [DataRow("nonzero-bp")]
    [DataRow("invalid-guid")]
    [DataRow("duplicate-guid-field")]
    [DataRow("duplicate-saved-guid")]
    [DataRow("missing-source-id")]
    [DataRow("duplicate-source-id")]
    [DataRow("nested-source-id")]
    [DataRow("duplicate-name")]
    [DataRow("nested-name")]
    [DataRow("duplicate-qualitytype")]
    [DataRow("unselected-quality")]
    [DataRow("duplicate-qualitysource")]
    [DataRow("duplicate-extra")]
    [DataRow("nested-extra")]
    [DataRow("duplicate-sourcename")]
    [DataRow("nested-sourcename")]
    [DataRow("duplicate-notes")]
    [DataRow("nested-notes")]
    [DataRow("duplicate-weaponguid")]
    [DataRow("nested-weaponguid")]
    [DataRow("duplicate-bonus")]
    [DataRow("duplicate-firstlevelbonus")]
    [DataRow("duplicate-naturalweapons")]
    public void Invalid_or_ambiguous_saved_scalars_and_containers_cannot_inherit_parser_defaults(string mutation)
    {
        using var fixture = new Fixture(characterMutation: root =>
        {
            XElement container = root.Element("qualities")!;
            XElement first = container.Elements("quality").First();
            XElement second = container.Elements("quality").Last();
            switch (mutation)
            {
                case "duplicate-container": root.Add(new XElement(container)); break;
                case "duplicate-settings": root.Add(new XElement("settings", SettingsId)); break;
                case "missing-settings": root.Element("settings")!.Remove(); break;
                case "nested-settings": root.Element("settings")!.ReplaceNodes(new XElement("value", SettingsId)); break;
                case "invalid-created": root.Element("created")!.Value = "not-a-bool"; break;
                case "missing-created": root.Element("created")!.Remove(); break;
                case "duplicate-created": root.Add(new XElement("created", "True")); break;
                case "invalid-bp": second.Element("bp")!.Value = "not-a-number"; break;
                case "missing-bp": second.Element("bp")!.Remove(); break;
                case "empty-bp": second.Element("bp")!.Value = string.Empty; break;
                case "duplicate-bp": second.Add(new XElement("bp", "5")); break;
                case "nested-bp": second.Element("bp")!.ReplaceNodes(new XElement("value", "0")); break;
                case "nonzero-bp": second.Element("bp")!.Value = "1"; break;
                case "invalid-guid": first.Element("guid")!.Value = "not-a-guid"; break;
                case "duplicate-guid-field": first.Add(new XElement("guid", SubjectId)); break;
                case "duplicate-saved-guid": second.Element("guid")!.Value = SubjectId; break;
                case "missing-source-id": first.Element("sourceid")!.Remove(); break;
                case "duplicate-source-id": second.Add(new XElement("sourceid", SourceId)); break;
                case "nested-source-id": second.Element("sourceid")!.ReplaceNodes(new XElement("value", SourceId)); break;
                case "duplicate-name": second.Add(new XElement("name", QualityName)); break;
                case "nested-name": second.Element("name")!.ReplaceNodes(new XElement("value", QualityName)); break;
                case "duplicate-qualitytype": second.Add(new XElement("qualitytype", "Positive")); break;
                case "unselected-quality": second.Element("qualitysource")!.Value = "Metatype"; break;
                case "duplicate-qualitysource": second.Add(new XElement("qualitysource", "Selected")); break;
                case "duplicate-extra": second.Add(new XElement("extra")); break;
                case "nested-extra": second.Element("extra")!.Add(new XElement("value")); break;
                case "duplicate-sourcename": second.Add(new XElement("sourcename")); break;
                case "nested-sourcename": second.Element("sourcename")!.Add(new XElement("value")); break;
                case "duplicate-notes": second.Add(new XElement("notes", "Hidden semantics")); break;
                case "nested-notes": second.Element("notes")!.Add(new XElement("value")); break;
                case "duplicate-weaponguid": second.Add(new XElement("weaponguid"), new XElement("weaponguid", SubjectId)); break;
                case "nested-weaponguid": second.Add(new XElement("weaponguid", new XElement("value"))); break;
                case "duplicate-bonus": second.Add(new XElement("bonus", new XElement("effect"))); break;
                case "duplicate-firstlevelbonus": second.Add(new XElement("firstlevelbonus"), new XElement("firstlevelbonus", new XElement("effect"))); break;
                case "duplicate-naturalweapons": second.Add(new XElement("naturalweapons"), new XElement("naturalweapons", new XElement("weapon"))); break;
                default: throw new InvalidOperationException(mutation);
            }
        });
        AssertUnresolved(fixture.CreateService().Resolve(fixture.Owners.Capture(), fixture.Request()));
    }

    [TestMethod]
    public void Dtd_and_entity_declarations_are_not_accepted_as_saved_character_authority()
    {
        using var fixture = new Fixture(envelopeMutation: envelope => envelope with
        {
            Payload = "<!DOCTYPE character [<!ENTITY qualityname 'Grounded Quality'>]>"
                + envelope.Payload.Replace("<name>Grounded Quality</name>", "<name>&qualityname;</name>", StringComparison.Ordinal)
        });
        AssertUnresolved(fixture.CreateService().Resolve(fixture.Owners.Capture(), fixture.Request()));
    }

    [TestMethod]
    public void Name_only_owner_accessor_and_foreign_stamps_cannot_read_any_workspace()
    {
        using var fixture = new Fixture();
        var store = new ObservedStore(fixture.Store, fixture.Owners);
        AssertUnresolved(fixture.CreateService(new NameOnlyOwner(), store).Resolve(fixture.Owners.Capture(), fixture.Request()));
        OwnerContextStamp stamp = fixture.Owners.Capture();
        IWorkspaceRuleQuestionService service = fixture.CreateService(store: store);
        AssertUnresolved(service.Resolve(stamp with { Owner = new("owner-b") }, fixture.Request()));
        AssertUnresolved(service.Resolve(stamp with { AuthorityInstanceId = "foreign" }, fixture.Request()));
        AssertUnresolved(service.Resolve(stamp with { TransitionRevision = stamp.TransitionRevision + 1 }, fixture.Request()));
        fixture.Owners.SwitchTo(new("owner-b"));
        fixture.Owners.SwitchTo(Owner);
        AssertUnresolved(service.Resolve(stamp, fixture.Request()));
        Assert.AreEqual(0, store.ReadCount);
        AssertResolved(service.Resolve(fixture.Owners.Capture(), fixture.Request()));
        Assert.IsTrue(store.ReadCount >= 2);
        Assert.AreEqual(0, fixture.Owners.ActiveLeases);
    }

    [TestMethod]
    [DataRow("document")]
    [DataRow("auxiliary")]
    [DataRow("content-revision")]
    [DataRow("saved-revision")]
    [DataRow("workspace-id")]
    public void Final_read_rejects_changed_observations_including_same_revision_document_and_auxiliary(string mutation)
    {
        using var fixture = new Fixture();
        WorkspaceStoredDocument initial = fixture.Read();
        var observed = new ObservedStore(fixture.Store, fixture.Owners)
        {
            OnRead = (count, value) => count == 1 ? value : mutation switch
            {
                "document" => value with { Document = value.Document with { State = value.Document.State with { Payload = value.Document.Content + " " } } },
                "auxiliary" => value with { Document = value.Document with { State = value.Document.State with { AuxiliaryState = new(CharacterCreationSkillsReceipts: []) } } },
                "content-revision" => value with { ContentRevision = value.ContentRevision + 1 },
                "saved-revision" => value with { SavedRevision = value.SavedRevision + 1 },
                "workspace-id" => value with { Id = new("different-workspace") },
                _ => throw new InvalidOperationException(mutation)
            }
        };
        AssertUnresolved(fixture.CreateService(store: observed).Resolve(fixture.Owners.Capture(), fixture.Request()));
        Assert.IsTrue(observed.ReadCount >= 2, "The rejection must follow a reread of the real store.");
        Assert.AreEqual(JsonSerializer.Serialize(initial), JsonSerializer.Serialize(fixture.Read()));
    }

    [TestMethod]
    public void Initial_document_digest_is_captured_before_shared_auxiliary_collection_can_change()
    {
        using var fixture = new Fixture();
        var shared = new List<CharacterCreationSkillsReceipt>();
        WorkspaceStoredDocument? sharedObservation = null;
        string? initialDigest = null;
        var observed = new ObservedStore(fixture.Store, fixture.Owners)
        {
            OnRead = (count, value) =>
            {
                if (count == 1)
                {
                    sharedObservation = value with { Document = value.Document with
                    {
                        State = value.Document.State with { AuxiliaryState = new(CharacterCreationSkillsReceipts: shared) }
                    } };
                    initialDigest = CharacterCreationBootstrapActivationIntegrity.ComputeDocumentDigest(sharedObservation.Document);
                }
                else
                {
                    // Hostile alias mutation: even an invalid later ledger element must
                    // change the original observation, not move both comparison sides.
                    shared.Add(null!);
                }
                return sharedObservation!;
            }
        };
        AssertUnresolved(fixture.CreateService(store: observed).Resolve(fixture.Owners.Capture(), fixture.Request()));
        Assert.IsTrue(observed.ReadCount >= 2);
        Assert.AreNotEqual(initialDigest, CharacterCreationBootstrapActivationIntegrity.ComputeDocumentDigest(sharedObservation!.Document));
        Assert.IsTrue(fixture.Read().Document.AuxiliaryState.IsEmpty);
    }

    [TestMethod]
    public void Final_source_admission_rejects_same_length_source_change_even_when_timestamp_is_restored()
    {
        using var fixture = new Fixture();
        bool injected = false;
        var sources = new ObservedSources(fixture.Sources, fixture.Owners)
        {
            BeforeContextRead = (count, _, _) =>
            {
                if (count == 3)
                {
                    DateTime timestamp = File.GetLastWriteTimeUtc(fixture.QualitiesPath);
                    string before = File.ReadAllText(fixture.QualitiesPath);
                    string changed = before.Replace("<page>224</page>", "<page>225</page>", StringComparison.Ordinal);
                    Assert.AreNotEqual(before, changed);
                    Assert.AreEqual(before.Length, changed.Length);
                    File.WriteAllText(fixture.QualitiesPath, changed);
                    File.SetLastWriteTimeUtc(fixture.QualitiesPath, timestamp);
                    Assert.AreEqual(timestamp, File.GetLastWriteTimeUtc(fixture.QualitiesPath));
                    injected = true;
                }
            }
        };
        AssertUnresolved(fixture.CreateService(sources: sources).Resolve(fixture.Owners.Capture(), fixture.Request()));
        Assert.IsTrue(injected, "Drift must be injected immediately before the same-operation source readmission.");
        Assert.AreEqual(1, sources.OperationCount, "A new source operation abandons the original drift history.");
        Assert.AreEqual(3, sources.ContextReadCount, "Initial and parser admission must precede the final source check.");
        Assert.AreEqual(1, sources.DisposeCount);
    }

    [TestMethod]
    public void Final_source_admission_keeps_observed_drift_poisoned_after_original_bytes_are_restored()
    {
        using var fixture = new Fixture();
        bool poisoned = false;
        var sources = new ObservedSources(fixture.Sources, fixture.Owners)
        {
            BeforeContextRead = (count, xml, operation) =>
            {
                if (count != 3) return;
                byte[] original = File.ReadAllBytes(fixture.QualitiesPath);
                DateTime timestamp = File.GetLastWriteTimeUtc(fixture.QualitiesPath);
                string changed = File.ReadAllText(fixture.QualitiesPath).Replace("<page>224</page>", "<page>225</page>", StringComparison.Ordinal);
                File.WriteAllText(fixture.QualitiesPath, changed);
                Assert.IsNull(operation.TryCreateContext(xml), "The actual operation must observe source drift before restoration.");
                poisoned = true;
                File.WriteAllBytes(fixture.QualitiesPath, original);
                File.SetLastWriteTimeUtc(fixture.QualitiesPath, timestamp);
            }
        };
        AssertUnresolved(fixture.CreateService(sources: sources).Resolve(fixture.Owners.Capture(), fixture.Request()));
        Assert.IsTrue(poisoned);
        Assert.AreEqual(3, sources.ContextReadCount, "Source restoration must challenge the final same-operation admission.");
        Assert.AreEqual(1, sources.OperationCount);
        Assert.AreEqual(1, sources.DisposeCount);
        // A later independent query may admit the restored source. The previous
        // operation alone remains poisoned and cannot revive its in-flight result.
        AssertResolved(fixture.CreateService().Resolve(fixture.Owners.Capture(), fixture.Request()));
    }

    [TestMethod]
    public void Headless_registration_resolves_quality_through_real_default_owner_source_and_codec_without_writes()
    {
        using var fixture = new Fixture();
        using ServiceProvider provider = fixture.CreateHeadlessProvider();
        IOwnerContextAccessor ownerAccessor = provider.GetRequiredService<IOwnerContextAccessor>();
        Assert.IsInstanceOfType<LocalOwnerContextAccessor>(ownerAccessor);
        OwnerContextStamp stamp = ((IOwnerContextLeaseAccessor)ownerAccessor).Capture();
        WorkspaceStoredDocument before = fixture.SeedHeadlessWorkspace(provider, stamp);
        IWorkspaceRuleQuestionService service = provider.GetRequiredService<IWorkspaceRuleQuestionService>();
        Assert.IsInstanceOfType<WorkspaceRuleQuestionService>(service);
        Assert.AreSame(service, provider.GetRequiredService<IWorkspaceRuleQuestionService>());
        ICharacterSourceDataResolver source = provider.GetRequiredService<ICharacterSourceDataResolver>();
        Assert.IsInstanceOfType<FileSystemCharacterSourceDataResolver>(source);
        Assert.AreNotSame(fixture.Sources, source);
        IRulesetWorkspaceCodec codec = provider.GetRequiredService<IRulesetWorkspaceCodecResolver>().Resolve("sr5");
        Assert.IsInstanceOfType<Sr5WorkspaceCodec>(codec);
        Assert.AreNotSame(fixture.Codec, codec);
        string[] paths = Directory.GetFiles(fixture.StateRoot, "*.json", SearchOption.AllDirectories);
        byte[][] bytes = paths.Select(File.ReadAllBytes).ToArray();
        DateTime[] timestamps = paths.Select(File.GetLastWriteTimeUtc).ToArray();

        WorkspaceRuleQuestionResult result = service.Resolve(stamp, fixture.Request());

        AssertResolved(result);
        Assert.AreEqual(2, result.Level);
        Assert.AreEqual(3, result.MaximumLevel);
        Assert.IsTrue(result.Binding!.TrustedLocalOwner);
        Assert.AreEqual(stamp.Owner.Value, result.Binding.OwnerId);
        Assert.AreEqual(stamp.AuthorityInstanceId, result.Binding.OwnerAuthorityInstanceId);
        Assert.AreEqual(stamp.TransitionRevision, result.Binding.OwnerTransitionRevision);
        Assert.AreEqual(before.ContentRevision, result.Binding.ContentRevision);
        Assert.AreEqual(before.SavedRevision, result.Binding.SavedRevision);
        Assert.AreEqual(CharacterCreationBootstrapActivationIntegrity.ComputeDocumentDigest(before.Document), result.Binding.WorkspaceDocumentDigest);
        Assert.AreEqual("SG", result.SourceAnchors.Single().SourceBook);
        Assert.AreEqual(224, result.SourceAnchors.Single().Page);
        Assert.AreEqual(SourceId, result.SourceAnchors.Single().QualitySourceId);
        Assert.AreEqual(JsonSerializer.Serialize(before), JsonSerializer.Serialize(fixture.Store.Get(WorkspaceId).Value));
        CollectionAssert.AreEquivalent(paths, Directory.GetFiles(fixture.StateRoot, "*.json", SearchOption.AllDirectories));
        for (int index = 0; index < paths.Length; index++)
        {
            CollectionAssert.AreEqual(bytes[index], File.ReadAllBytes(paths[index]));
            Assert.AreEqual(timestamps[index], File.GetLastWriteTimeUtc(paths[index]));
        }
    }

    [TestMethod]
    public void Headless_registered_interface_rejects_stale_revision_without_changing_the_real_workspace()
    {
        using var fixture = new Fixture();
        using ServiceProvider provider = fixture.CreateHeadlessProvider();
        IOwnerContextAccessor ownerAccessor = provider.GetRequiredService<IOwnerContextAccessor>();
        Assert.IsInstanceOfType<LocalOwnerContextAccessor>(ownerAccessor);
        OwnerContextStamp stamp = ((IOwnerContextLeaseAccessor)ownerAccessor).Capture();
        WorkspaceStoredDocument before = fixture.SeedHeadlessWorkspace(provider, stamp);
        IWorkspaceRuleQuestionService service = provider.GetRequiredService<IWorkspaceRuleQuestionService>();

        WorkspaceRuleQuestionResult result = service.Resolve(stamp, fixture.Request() with { ExpectedContentRevision = 1 });

        AssertUnresolved(result);
        Assert.AreEqual(JsonSerializer.Serialize(before), JsonSerializer.Serialize(fixture.Store.Get(WorkspaceId).Value));
    }

    [TestMethod]
    public void Reserved_local_owner_name_without_trusted_identity_cannot_read_the_local_workspace()
    {
        using var fixture = new Fixture();
        using ServiceProvider provider = fixture.CreateHeadlessProvider();
        var localAuthority = (IOwnerContextLeaseAccessor)provider.GetRequiredService<IOwnerContextAccessor>();
        OwnerContextStamp localStamp = localAuthority.Capture();
        WorkspaceStoredDocument localBefore = fixture.SeedHeadlessWorkspace(provider, localStamp);
        fixture.Owners.SwitchTo(new OwnerScope(OwnerScope.LocalSingleUser.Value));
        OwnerContextStamp untrustedStamp = fixture.Owners.Capture();
        Assert.IsTrue(untrustedStamp.IsValid);
        Assert.AreEqual(localStamp.Owner.Value, untrustedStamp.Owner.Value);
        Assert.IsTrue(untrustedStamp.Owner.UsesLocalSingleUserValue);
        Assert.IsFalse(untrustedStamp.Owner.IsLocalSingleUser);
        var observedStore = new ObservedStore(fixture.Store, fixture.Owners);

        WorkspaceRuleQuestionResult result = fixture.CreateService(store: observedStore)
            .Resolve(untrustedStamp, fixture.Request());

        AssertUnresolved(result);
        Assert.AreEqual(1, observedStore.ReadCount, "The untrusted owner must stay on the owner-scoped read path.");
        Assert.AreEqual(0, observedStore.LocalReadCount, "Matching the reserved name must never select or fall back to local storage.");
        Assert.AreEqual(0, fixture.Owners.ActiveLeases);
        Assert.AreEqual(JsonSerializer.Serialize(localBefore), JsonSerializer.Serialize(fixture.Store.Get(WorkspaceId).Value));
    }

    [TestMethod]
    [DataRow("de")]
    [DataRow("en")]
    [DataRow("es")]
    public void Registered_provider_admission_accepts_only_current_core_text_without_writes(string locale)
    {
        using var fixture = new Fixture();
        using ServiceProvider provider = fixture.CreateHeadlessProvider();
        var owners = (IOwnerContextLeaseAccessor)provider.GetRequiredService<IOwnerContextAccessor>();
        Assert.IsInstanceOfType<LocalOwnerContextAccessor>(owners);
        OwnerContextStamp stamp = owners.Capture();
        WorkspaceStoredDocument before = fixture.SeedHeadlessWorkspace(provider, stamp);
        WorkspaceRuleQuestionRequest request = fixture.Request(locale);
        WorkspaceRuleQuestionResult current = provider.GetRequiredService<IWorkspaceRuleQuestionService>().Resolve(stamp, request);
        AssertResolved(current);
        Assert.AreEqual(2, current.Level);
        Assert.AreEqual(3, current.MaximumLevel);
        Assert.AreEqual("SG", current.SourceAnchors.Single().SourceBook);
        Assert.AreEqual(224, current.SourceAnchors.Single().Page);
        BuildGhostProviderAnswer answer = CurrentAnswer(current, request);
        string answerBefore = JsonSerializer.Serialize(answer);
        string[] paths = Directory.GetFiles(fixture.StateRoot, "*.json", SearchOption.AllDirectories);
        byte[][] bytes = paths.Select(File.ReadAllBytes).ToArray();
        DateTime[] timestamps = paths.Select(File.GetLastWriteTimeUtc).ToArray();
        IWorkspaceRuleProviderAnswerService validator = provider.GetRequiredService<IWorkspaceRuleProviderAnswerService>();
        Assert.AreSame(validator, provider.GetRequiredService<IWorkspaceRuleProviderAnswerService>());

        BuildGhostProviderValidationResult admitted = validator.Validate(stamp, request, ProviderRequestId, answer);

        Assert.IsTrue(admitted.Accepted, string.Join(", ", admitted.RejectionReasons));
        Assert.AreEqual(current.Explanation.Explanation, admitted.SafeText);
        Assert.HasCount(0, admitted.RejectionReasons);
        Assert.AreEqual(answerBefore, JsonSerializer.Serialize(answer));
        Assert.AreEqual(JsonSerializer.Serialize(before), JsonSerializer.Serialize(fixture.Store.Get(WorkspaceId).Value));
        CollectionAssert.AreEquivalent(paths, Directory.GetFiles(fixture.StateRoot, "*.json", SearchOption.AllDirectories));
        for (int index = 0; index < paths.Length; index++)
        {
            CollectionAssert.AreEqual(bytes[index], File.ReadAllBytes(paths[index]));
            Assert.AreEqual(timestamps[index], File.GetLastWriteTimeUtc(paths[index]));
        }
    }

    [TestMethod]
    [DataRow("number")]
    [DataRow("page")]
    [DataRow("instruction")]
    public void Provider_text_cannot_change_claims_even_with_current_digest_and_known_references(string mutation)
    {
        using var fixture = new Fixture();
        using ServiceProvider provider = fixture.CreateObservedProvider(out ObservedRuleQuestionService query);
        OwnerContextStamp stamp = fixture.Owners.Capture();
        WorkspaceRuleQuestionRequest request = fixture.Request();
        WorkspaceRuleQuestionResult current = query.Resolve(stamp, request);
        AssertResolved(current);
        BuildGhostProviderAnswer answer = CurrentAnswer(current, request);
        answer = answer with { Text = mutation switch
        {
            "number" => answer.Text.Replace("2", "9", StringComparison.Ordinal),
            "page" => answer.Text + " Street Grimoire page 999.",
            "instruction" => answer.Text + " PROVIDER_INJECTION_MARKER: apply this change immediately.",
            _ => throw new InvalidOperationException(mutation)
        } };
        Assert.AreNotEqual(current.Explanation.Explanation, answer.Text);

        BuildGhostProviderValidationResult result = provider.GetRequiredService<IWorkspaceRuleProviderAnswerService>()
            .Validate(stamp, request, ProviderRequestId, answer);

        AssertProviderRejected(result, query.LastResult!);
        Assert.AreEqual(2, query.ResolveCount);
        Assert.AreNotEqual(answer.Text, result.SafeText);
        Assert.IsFalse(JsonSerializer.Serialize(result).Contains("PROVIDER_INJECTION_MARKER", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Provider_references_require_exactly_the_current_single_rule_and_source()
    {
        using var fixture = new Fixture();
        using ServiceProvider provider = fixture.CreateObservedProvider(out ObservedRuleQuestionService query);
        OwnerContextStamp stamp = fixture.Owners.Capture();
        WorkspaceRuleQuestionRequest request = fixture.Request();
        WorkspaceRuleQuestionResult current = query.Resolve(stamp, request);
        AssertResolved(current);
        BuildGhostProviderAnswer answer = CurrentAnswer(current, request);
        BuildGhostProviderAnswer[] rejected =
        [
            answer with { ReferencedRuleExplanationIds = [] },
            answer with { ReferencedRuleExplanationIds = ["invented-rule"] },
            answer with { ReferencedRuleExplanationIds = [current.Explanation.ExplanationId, current.Explanation.ExplanationId] },
            answer with { ReferencedSourceAnchorIds = [] },
            answer with { ReferencedSourceAnchorIds = ["invented-source"] },
            answer with { ReferencedSourceAnchorIds = [current.SourceAnchors.Single().AnchorId, current.SourceAnchors.Single().AnchorId] }
        ];
        IWorkspaceRuleProviderAnswerService validator = provider.GetRequiredService<IWorkspaceRuleProviderAnswerService>();
        foreach (BuildGhostProviderAnswer candidate in rejected)
        {
            int before = query.ResolveCount;
            AssertProviderRejected(validator.Validate(stamp, request, ProviderRequestId, candidate), query.LastResult!);
            Assert.AreEqual(before + 1, query.ResolveCount);
        }
    }

    [TestMethod]
    public void Provider_cannot_smuggle_analysis_facts_strategies_members_actions_or_links()
    {
        using var fixture = new Fixture();
        using ServiceProvider provider = fixture.CreateObservedProvider(out ObservedRuleQuestionService query);
        OwnerContextStamp stamp = fixture.Owners.Capture();
        WorkspaceRuleQuestionRequest request = fixture.Request();
        WorkspaceRuleQuestionResult current = query.Resolve(stamp, request);
        AssertResolved(current);
        BuildGhostProviderAnswer answer = CurrentAnswer(current, request);
        BuildGhostProviderAnswer[] rejected =
        [
            answer with { ReferencedFactIds = ["invented-fact"] },
            answer with { ReferencedStrategyIds = ["invented-strategy"] },
            answer with { ReferencedVariantIds = ["invented-variant"] },
            answer with { ReferencedMemberRefs = ["other-member"] },
            answer with { SuggestedActionIds = ["direct-apply"] },
            answer with { Links = ["https://provider.invalid/apply"] }
        ];
        IWorkspaceRuleProviderAnswerService validator = provider.GetRequiredService<IWorkspaceRuleProviderAnswerService>();
        foreach (BuildGhostProviderAnswer candidate in rejected)
        {
            int before = query.ResolveCount;
            AssertProviderRejected(validator.Validate(stamp, request, ProviderRequestId, candidate), query.LastResult!);
            Assert.AreEqual(before + 1, query.ResolveCount);
        }
    }

    [TestMethod]
    public void Provider_null_collections_are_not_treated_as_empty_authorized_collections()
    {
        using var fixture = new Fixture();
        using ServiceProvider provider = fixture.CreateObservedProvider(out ObservedRuleQuestionService query);
        OwnerContextStamp stamp = fixture.Owners.Capture();
        WorkspaceRuleQuestionRequest request = fixture.Request();
        WorkspaceRuleQuestionResult current = query.Resolve(stamp, request);
        AssertResolved(current);
        BuildGhostProviderAnswer answer = CurrentAnswer(current, request);
        BuildGhostProviderAnswer[] rejected =
        [
            answer with { ReferencedFactIds = null! }, answer with { ReferencedStrategyIds = null! },
            answer with { ReferencedRuleExplanationIds = null! }, answer with { ReferencedVariantIds = null! },
            answer with { ReferencedMemberRefs = null! }, answer with { ReferencedSourceAnchorIds = null! },
            answer with { SuggestedActionIds = null! }, answer with { Links = null! }
        ];
        IWorkspaceRuleProviderAnswerService validator = provider.GetRequiredService<IWorkspaceRuleProviderAnswerService>();
        foreach (BuildGhostProviderAnswer candidate in rejected)
        {
            int before = query.ResolveCount;
            AssertProviderRejected(validator.Validate(stamp, request, ProviderRequestId, candidate), query.LastResult!);
            Assert.AreEqual(before + 1, query.ResolveCount);
            Assert.AreEqual(0, fixture.Owners.ActiveLeases);
        }
    }

    [TestMethod]
    public void Every_provider_envelope_including_null_and_malformed_answers_gets_a_fresh_core_resolution()
    {
        using var fixture = new Fixture();
        using ServiceProvider provider = fixture.CreateObservedProvider(out ObservedRuleQuestionService query);
        OwnerContextStamp stamp = fixture.Owners.Capture();
        WorkspaceRuleQuestionRequest request = fixture.Request();
        WorkspaceRuleQuestionResult current = query.Resolve(stamp, request);
        AssertResolved(current);
        BuildGhostProviderAnswer answer = CurrentAnswer(current, request);
        BuildGhostProviderAnswer?[] rejected =
        [
            null, answer with { Schema = BuildGhostContractVersions.ProviderAnswerV1 },
            answer with { Schema = null! }, answer with { RequestId = "another-request" },
            answer with { RequestId = null! }, answer with { PacketDigest = "not-a-digest" },
            answer with { PacketDigest = null! }, answer with { PacketDigest = new string('0', current.ResultDigest.Length) },
            answer with { Locale = "de" }, answer with { Locale = null! }, answer with { Text = null! }
        ];
        IWorkspaceRuleProviderAnswerService validator = provider.GetRequiredService<IWorkspaceRuleProviderAnswerService>();
        foreach (BuildGhostProviderAnswer? candidate in rejected)
        {
            int before = query.ResolveCount;
            AssertProviderRejected(validator.Validate(stamp, request, ProviderRequestId, candidate), query.LastResult!);
            Assert.AreEqual(before + 1, query.ResolveCount);
        }
        int beforeValid = query.ResolveCount;
        Assert.IsTrue(validator.Validate(stamp, request, ProviderRequestId, answer).Accepted);
        Assert.AreEqual(beforeValid + 1, query.ResolveCount);
        foreach (string badExpectedId in new[] { "", " ", null! })
        {
            int before = query.ResolveCount;
            AssertProviderRejected(validator.Validate(stamp, request, badExpectedId, answer), query.LastResult!);
            Assert.AreEqual(before + 1, query.ResolveCount);
        }
    }

    [TestMethod]
    public void Provider_collection_exception_fails_closed_and_releases_the_admission_lease()
    {
        using var fixture = new Fixture();
        using ServiceProvider provider = fixture.CreateObservedProvider(out ObservedRuleQuestionService query);
        OwnerContextStamp stamp = fixture.Owners.Capture();
        WorkspaceRuleQuestionRequest request = fixture.Request();
        WorkspaceRuleQuestionResult current = query.Resolve(stamp, request);
        AssertResolved(current);
        var hostile = new ThrowingCountList(fixture.Owners);
        BuildGhostProviderAnswer answer = CurrentAnswer(current, request) with { Links = hostile };
        int leasesBefore = fixture.Owners.SuccessfulLeaseAcquisitions;

        BuildGhostProviderValidationResult result = provider.GetRequiredService<IWorkspaceRuleProviderAnswerService>()
            .Validate(stamp, request, ProviderRequestId, answer);

        Assert.IsFalse(result.Accepted);
        Assert.IsTrue(result.RejectionReasons.Count > 0);
        Assert.IsTrue(hostile.CountReads > 0, "The hostile collection must actually reach protocol admission.");
        Assert.AreEqual(1, hostile.ActiveLeasesAtCount, "Provider collection access must remain inside the actual owner lease.");
        Assert.AreEqual(query.LastResult!.Explanation.Explanation, result.SafeText);
        Assert.IsFalse(JsonSerializer.Serialize(result).Contains(ThrowingCountList.ExceptionMarker, StringComparison.Ordinal));
        Assert.AreEqual(2, query.ResolveCount);
        Assert.AreEqual(leasesBefore + 2, fixture.Owners.SuccessfulLeaseAcquisitions);
        Assert.AreEqual(0, fixture.Owners.ActiveLeases);
    }

    [TestMethod]
    [DataRow("character")]
    [DataRow("checkpoint")]
    [DataRow("source-page")]
    public void Old_provider_output_is_rejected_after_real_character_checkpoint_or_source_change(string mutation)
    {
        using var fixture = new Fixture();
        using ServiceProvider provider = fixture.CreateObservedProvider(out ObservedRuleQuestionService query);
        OwnerContextStamp stamp = fixture.Owners.Capture();
        WorkspaceRuleQuestionRequest request = fixture.Request();
        WorkspaceRuleQuestionResult previous = query.Resolve(stamp, request);
        AssertResolved(previous);
        BuildGhostProviderAnswer answer = CurrentAnswer(previous, request);
        if (mutation == "character")
        {
            WorkspaceStoredDocument stored = fixture.Read();
            XElement character = XElement.Parse(stored.Document.Content);
            character.Element("qualities")!.Elements("quality").Single(item => item.Element("guid")!.Value == SecondSubjectId).Remove();
            WorkspaceDocument changed = stored.Document with { State = stored.Document.State with
            {
                Payload = character.ToString(SaveOptions.DisableFormatting)
            } };
            Assert.IsTrue(fixture.Store.ReplaceWorkspaceDocument(Owner, WorkspaceId, 2, changed).Success);
            request = request with { ExpectedContentRevision = 3 };
        }
        else if (mutation == "checkpoint")
            Assert.IsTrue(fixture.Store.SaveCheckpoint(Owner, WorkspaceId, 2).Success);
        else
        {
            string before = File.ReadAllText(fixture.QualitiesPath);
            string after = before.Replace("<page>224</page>", "<page>225</page>", StringComparison.Ordinal);
            Assert.AreNotEqual(before, after);
            File.WriteAllText(fixture.QualitiesPath, after);
        }
        string storedBeforeValidation = JsonSerializer.Serialize(fixture.Read());

        BuildGhostProviderValidationResult result = provider.GetRequiredService<IWorkspaceRuleProviderAnswerService>()
            .Validate(stamp, request, ProviderRequestId, answer);

        WorkspaceRuleQuestionResult current = query.LastResult!;
        AssertResolved(current);
        AssertProviderRejected(result, current);
        Assert.AreEqual(2, query.ResolveCount);
        Assert.AreNotEqual(previous.ResultDigest, current.ResultDigest);
        if (mutation == "character")
        {
            Assert.AreEqual(1, current.Level);
            Assert.AreNotEqual(answer.Text, result.SafeText);
            WorkspaceRuleQuestionRequest stale = request with { ExpectedContentRevision = 2 };
            BuildGhostProviderValidationResult staleResult = provider.GetRequiredService<IWorkspaceRuleProviderAnswerService>()
                .Validate(stamp, stale, ProviderRequestId, answer);
            AssertUnresolved(query.LastResult!);
            AssertProviderRejected(staleResult, query.LastResult!);
            Assert.AreNotEqual(answer.Text, staleResult.SafeText);
        }
        else
        {
            Assert.AreEqual(previous.Explanation.Explanation, current.Explanation.Explanation,
                "Unchanged words must not authorize replay of an old result digest.");
            if (mutation == "checkpoint") Assert.AreEqual(2L, current.Binding!.SavedRevision);
            else
            {
                Assert.AreEqual(225, current.SourceAnchors.Single().Page);
                Assert.AreNotEqual(previous.Binding!.SourceNodeDigest, current.Binding!.SourceNodeDigest);
            }
        }
        Assert.AreEqual(storedBeforeValidation, JsonSerializer.Serialize(fixture.Read()));
    }

    [TestMethod]
    public void Old_provider_output_rejects_changed_auxiliary_read_observation_at_the_same_revision()
    {
        using var fixture = new Fixture();
        bool changed = false;
        // This is explicitly an observation perturbation, not an invented durable
        // auxiliary mutation API. The real store, codec and source still execute.
        var store = new ObservedStore(fixture.Store, fixture.Owners)
        {
            OnRead = (_, value) => !changed ? value : value with { Document = value.Document with
            {
                State = value.Document.State with { AuxiliaryState = new(CharacterCreationSkillsReceipts: []) }
            } }
        };
        using ServiceProvider provider = fixture.CreateObservedProvider(out ObservedRuleQuestionService query, store);
        OwnerContextStamp stamp = fixture.Owners.Capture();
        WorkspaceRuleQuestionRequest request = fixture.Request();
        WorkspaceRuleQuestionResult previous = query.Resolve(stamp, request);
        AssertResolved(previous);
        BuildGhostProviderAnswer answer = CurrentAnswer(previous, request);
        changed = true;

        BuildGhostProviderValidationResult result = provider.GetRequiredService<IWorkspaceRuleProviderAnswerService>()
            .Validate(stamp, request, ProviderRequestId, answer);

        WorkspaceRuleQuestionResult current = query.LastResult!;
        AssertResolved(current);
        Assert.AreEqual(previous.Binding!.ContentRevision, current.Binding!.ContentRevision);
        Assert.AreEqual(previous.Binding.SavedRevision, current.Binding.SavedRevision);
        Assert.AreNotEqual(previous.Binding.WorkspaceDocumentDigest, current.Binding.WorkspaceDocumentDigest);
        Assert.AreNotEqual(previous.ResultDigest, current.ResultDigest);
        AssertProviderRejected(result, current);
        Assert.AreEqual(2, query.ResolveCount);
        Assert.IsTrue(store.ReadCount >= 4);
        Assert.IsTrue(fixture.Read().Document.AuxiliaryState.IsEmpty);
    }

    [TestMethod]
    public void Old_provider_output_cannot_cross_owner_aba_even_when_owner_name_returns()
    {
        using var fixture = new Fixture();
        using ServiceProvider provider = fixture.CreateObservedProvider(out ObservedRuleQuestionService query);
        OwnerContextStamp oldStamp = fixture.Owners.Capture();
        WorkspaceRuleQuestionRequest request = fixture.Request();
        WorkspaceRuleQuestionResult previous = query.Resolve(oldStamp, request);
        AssertResolved(previous);
        BuildGhostProviderAnswer answer = CurrentAnswer(previous, request);
        fixture.Owners.SwitchTo(new("owner-b"));
        fixture.Owners.SwitchTo(Owner);
        IWorkspaceRuleProviderAnswerService validator = provider.GetRequiredService<IWorkspaceRuleProviderAnswerService>();

        BuildGhostProviderValidationResult stale = validator.Validate(oldStamp, request, ProviderRequestId, answer);

        Assert.IsFalse(stale.Accepted);
        AssertUnresolved(query.LastResult!);
        Assert.AreNotEqual(answer.Text, stale.SafeText);
        Assert.AreEqual(2, query.ResolveCount);
        BuildGhostProviderValidationResult freshOwner = validator.Validate(fixture.Owners.Capture(), request, ProviderRequestId, answer);
        AssertResolved(query.LastResult!);
        AssertProviderRejected(freshOwner, query.LastResult!);
        Assert.AreNotEqual(previous.ResultDigest, query.LastResult!.ResultDigest);
        Assert.AreEqual(3, query.ResolveCount);
        Assert.AreEqual(0, fixture.Owners.ActiveLeases);
    }

    [TestMethod]
    public void Owner_aba_after_real_resolution_rejects_with_fixed_fallback_without_reusing_old_core_text()
    {
        using var fixture = new Fixture();
        bool switchAfterResolve = false;
        using ServiceProvider provider = fixture.CreateObservedProvider(out ObservedRuleQuestionService query, afterResolve: () =>
        {
            if (!switchAfterResolve) return;
            switchAfterResolve = false;
            fixture.Owners.SwitchTo(new("owner-b"));
            fixture.Owners.SwitchTo(Owner);
        });
        OwnerContextStamp stamp = fixture.Owners.Capture();
        WorkspaceRuleQuestionRequest request = fixture.Request();
        WorkspaceRuleQuestionResult current = query.Resolve(stamp, request);
        AssertResolved(current);
        BuildGhostProviderAnswer answer = CurrentAnswer(current, request);
        switchAfterResolve = true;
        IWorkspaceRuleProviderAnswerService validator = provider.GetRequiredService<IWorkspaceRuleProviderAnswerService>();

        BuildGhostProviderValidationResult raced = validator.Validate(stamp, request, ProviderRequestId, answer);

        AssertResolved(query.LastResult!);
        Assert.IsFalse(switchAfterResolve, "The real resolver must return before owner drift is injected.");
        Assert.IsFalse(raced.Accepted);
        Assert.IsTrue(raced.RejectionReasons.Count > 0);
        Assert.IsFalse(string.IsNullOrWhiteSpace(raced.SafeText));
        Assert.AreNotEqual(answer.Text, raced.SafeText);
        Assert.AreEqual(2, query.ResolveCount);
        BuildGhostProviderValidationResult stale = validator.Validate(stamp, request, ProviderRequestId, answer);
        Assert.AreEqual(stale.SafeText, raced.SafeText, "Failed actual owner admission must use one fixed generic fallback.");
        Assert.AreEqual(0, fixture.Owners.ActiveLeases);
    }

    [TestMethod]
    public void Provider_admission_throwing_owner_stamp_is_disposed_once_and_never_speaks_prior_core_text()
    {
        using var fixture = new Fixture();
        var authority = new ThrowingStampOwner(fixture.Owners);
        IWorkspaceRuleQuestionService actualQuery = fixture.CreateService();
        using ServiceProvider provider = fixture.CreateHeadlessProvider(services =>
        {
            // The real query uses the normal authority; only the provider's
            // subsequent owner lease has a hostile Stamp accessor.
            services.Replace(ServiceDescriptor.Singleton<IOwnerContextAccessor>(authority));
            services.Replace(ServiceDescriptor.Singleton(actualQuery));
        });
        OwnerContextStamp stamp = fixture.Owners.Capture();
        WorkspaceRuleQuestionRequest request = fixture.Request();
        WorkspaceRuleQuestionResult current = actualQuery.Resolve(stamp, request);
        AssertResolved(current);

        BuildGhostProviderValidationResult result = provider.GetRequiredService<IWorkspaceRuleProviderAnswerService>()
            .Validate(stamp, request, ProviderRequestId, CurrentAnswer(current, request));

        Assert.IsFalse(result.Accepted);
        Assert.IsTrue(result.RejectionReasons.Count > 0);
        Assert.AreNotEqual(current.Explanation.Explanation, result.SafeText);
        Assert.IsFalse(string.IsNullOrWhiteSpace(result.SafeText));
        Assert.AreEqual(1, authority.StampReadCount);
        Assert.AreEqual(1, authority.DisposeCount);
        Assert.AreEqual(0, fixture.Owners.ActiveLeases);
    }

    private const string ProviderRequestId = "workspace-rule-answer-001";

    private static BuildGhostProviderAnswer CurrentAnswer(WorkspaceRuleQuestionResult current, WorkspaceRuleQuestionRequest request) => new(
        WorkspaceRuleQuestionSchemas.ProviderAnswerV1, ProviderRequestId, current.ResultDigest, request.Locale,
        current.Explanation.Explanation, [], [], [current.Explanation.ExplanationId], [], [],
        [current.SourceAnchors.Single().AnchorId], [], []);

    private static void AssertProviderRejected(BuildGhostProviderValidationResult result, WorkspaceRuleQuestionResult current)
    {
        Assert.IsFalse(result.Accepted);
        Assert.IsTrue(result.RejectionReasons.Count > 0);
        Assert.AreEqual(current.Explanation.Explanation, result.SafeText,
            "Only freshly resolved Core text may be spoken; rejected provider text is not a fallback.");
    }

    private static void AssertResolved(WorkspaceRuleQuestionResult result)
    {
        Assert.IsTrue(result.Resolved, result.FailureReason);
        Assert.AreEqual(WorkspaceRuleQuestionSchemas.ResultV1, result.Schema);
        Assert.AreEqual(WorkspaceRuleQuestionStatuses.Resolved, result.Status);
        Assert.AreEqual(BuildGhostContractVersions.RuleExplanationV1, result.Explanation.Schema);
        Assert.AreEqual("resolved", result.Explanation.Status);
        Assert.IsNull(result.Explanation.UncertaintyReason);
        Assert.IsNotNull(result.Binding);
        Assert.IsNotNull(result.Level);
        Assert.IsNotNull(result.MaximumLevel);
        StringAssert.Contains(result.Explanation.Explanation, result.Level.Value.ToString(CultureInfo.InvariantCulture));
        StringAssert.Contains(result.Explanation.Explanation, result.MaximumLevel.Value.ToString(CultureInfo.InvariantCulture));
        Assert.IsFalse(result.Explanation.Explanation.Contains("workspace_rule_question.", StringComparison.Ordinal),
            "A resolved explanation must contain the computed values in text, not an unresolved resource key.");
        Assert.HasCount(1, result.SourceAnchors);
        Assert.IsNull(result.FailureReason);
        Assert.AreEqual(WorkspaceRuleQuestionIntegrity.ComputeResultDigest(result), result.ResultDigest);
    }

    private static void AssertUnresolved(WorkspaceRuleQuestionResult result)
    {
        Assert.IsFalse(result.Resolved);
        Assert.AreEqual(WorkspaceRuleQuestionSchemas.ResultV1, result.Schema);
        Assert.AreEqual(WorkspaceRuleQuestionStatuses.Unresolved, result.Status);
        Assert.AreEqual(BuildGhostContractVersions.RuleExplanationV1, result.Explanation.Schema);
        Assert.AreEqual("bounded-uncertainty", result.Explanation.Status);
        Assert.IsNull(result.Binding);
        Assert.IsNull(result.Level);
        Assert.IsNull(result.MaximumLevel);
        Assert.HasCount(0, result.SourceAnchors);
        Assert.HasCount(0, result.Explanation.SourceAnchorIds);
        Assert.IsNull(result.Explanation.SourceLookupRoute);
        Assert.IsFalse(string.IsNullOrWhiteSpace(result.FailureReason));
        Assert.AreEqual(WorkspaceRuleQuestionIntegrity.ComputeResultDigest(result), result.ResultDigest);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "chummer-rule-question-" + Guid.NewGuid().ToString("N"));
        public string StateRoot { get; }
        public TestOwner Owners { get; } = new();
        public FileWorkspaceStore Store { get; }
        public FileSystemCharacterSourceDataResolver Sources { get; }
        public Sr5WorkspaceCodec Codec { get; }
        public string QualitiesPath { get; }

        public Fixture(Action<XElement>? characterMutation = null, Action<XElement>? sourceMutation = null,
            Func<WorkspacePayloadEnvelope, WorkspacePayloadEnvelope>? envelopeMutation = null, bool checkpoint = true)
        {
            string data = Path.Combine(_root, "data");
            Directory.CreateDirectory(data);
            StateRoot = Path.Combine(_root, "state");
            File.WriteAllText(Path.Combine(data, "settings.xml"), $"<chummer><settings><setting><id>{SettingsId}</id>"
                + "<buildmethod>Priority</buildmethod><buildpoints>25</buildpoints><books><book>SR5</book><book>SG</book></books>"
                + "<customdatadirectorynames/></setting></settings></chummer>");
            File.WriteAllText(Path.Combine(data, "books.xml"), "<chummer><books><book><code>SR5</code><name>Core Rulebook</name></book>"
                + "<book><code>SG</code><name>Street Grimoire</name></book></books></chummer>");
            XElement catalog = XElement.Parse($"<chummer><qualities><quality><id>{SourceId}</id><name>{QualityName}</name>"
                + "<category>Positive</category><limit>3</limit><source>SG</source><page>224</page></quality></qualities></chummer>");
            sourceMutation?.Invoke(catalog);
            QualitiesPath = Path.Combine(data, "qualities.xml");
            File.WriteAllText(QualitiesPath, catalog.ToString(SaveOptions.DisableFormatting));
            Sources = new FileSystemCharacterSourceDataResolver(new FileSystemContentOverlayCatalogService(_root, _root, null));
            Codec = CreateCodec(Sources);
            XElement character = XElement.Parse("<character><name>Grounded Runner</name><alias>Before</alias><metatype>Human</metatype>"
                + $"<settings>{SettingsId}</settings><buildmethod>Priority</buildmethod><createdversion>5.225.0</createdversion>"
                + "<appversion>5.225.0</appversion><created>False</created><karma>0</karma><nuyen>0</nuyen><qualities>"
                + Quality(SubjectId) + Quality(SecondSubjectId) + "</qualities></character>");
            characterMutation?.Invoke(character);
            var envelope = new WorkspacePayloadEnvelope("sr5", Sr5WorkspaceCodec.SchemaVersion, Sr5WorkspaceCodec.Sr5PayloadKind,
                character.ToString(SaveOptions.DisableFormatting));
            envelope = envelopeMutation?.Invoke(envelope) ?? envelope;
            Store = new FileWorkspaceStore(StateRoot);
            Assert.IsTrue(Store.CreateWorkspaceDocument(Owner, WorkspaceId, new WorkspaceDocument(envelope)).Success);
            if (checkpoint)
                Assert.IsTrue(Store.SaveCheckpoint(Owner, WorkspaceId, 1).Success);
            WorkspaceDocument changed = new(envelope with { Payload = envelope.Payload.Replace("<alias>Before</alias>", "<alias>Grounded</alias>", StringComparison.Ordinal) });
            Assert.IsTrue(Store.ReplaceWorkspaceDocument(Owner, WorkspaceId, 1, changed).Success);
            Assert.AreEqual(2L, Read().ContentRevision);
            Assert.AreEqual(checkpoint ? 1L : 0L, Read().SavedRevision);
        }

        private static string Quality(string id) => $"<quality><guid>{id}</guid><sourceid>{SourceId}</sourceid><name>{QualityName}</name>"
            + "<qualitytype>Positive</qualitytype><qualitysource>Selected</qualitysource><bp>0</bp><extra/><sourcename/>"
            + "<notes/><source>SR5</source><page>111</page><bonus/></quality>";

        public WorkspaceStoredDocument Read()
        {
            WorkspaceStoreReadResult result = Store.Get(Owner, WorkspaceId);
            Assert.IsTrue(result.Success, result.Error);
            return result.Value!;
        }

        public WorkspaceRuleQuestionRequest Request(string locale = "en") => new(
            WorkspaceId, 2, "sr5", WorkspaceRuleQuestionIntents.QualityLevel, SubjectId, locale);

        public IWorkspaceRuleQuestionService CreateService(IOwnerContextAccessor? owners = null, IWorkspaceStore? store = null,
            ICharacterSourceDataResolver? sources = null)
        {
            ICharacterSourceDataResolver actualSources = sources ?? Sources;
            return new WorkspaceRuleQuestionService(owners ?? Owners, store ?? Store,
                new RulesetWorkspaceCodecResolver([sources is null ? Codec : CreateCodec(actualSources)]), actualSources);
        }

        private static Sr5WorkspaceCodec CreateCodec(ICharacterSourceDataResolver sources)
        {
            var files = new CharacterFileService();
            return new Sr5WorkspaceCodec(new XmlCharacterFileQueries(files),
                new XmlCharacterSectionQueries(new CharacterSectionService(sources)), new XmlCharacterMetadataCommands(files));
        }

        public ServiceProvider CreateHeadlessProvider(Action<IServiceCollection>? configure = null)
        {
            Assert.IsTrue(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CHUMMER_AMENDS_PATH")),
                "The isolated DI fixture requires no ambient amend path; it must not inspect external source trees.");
            string languageDirectory = Path.Combine(_root, "lang");
            Directory.CreateDirectory(languageDirectory);
            File.WriteAllText(Path.Combine(languageDirectory, "en-us.xml"), "<chummer><strings/></chummer>");
            File.WriteAllText(Path.Combine(_root, "data", "lifemodules.xml"), "<chummer><lifemodules/></chummer>");
            var services = new ServiceCollection();
            services.AddChummerHeadlessCore(_root, _root, requireContentBundle: true);
            // Only the environment-backed store factory is replaced. Owner, overlay,
            // source, codec, XML adapters and query service keep their real registrations.
            services.Replace(ServiceDescriptor.Singleton<IWorkspaceStore>(Store));
            configure?.Invoke(services);
            return services.BuildServiceProvider();
        }

        public ServiceProvider CreateObservedProvider(out ObservedRuleQuestionService observed,
            IWorkspaceStore? store = null, Action? afterResolve = null)
        {
            ServiceProvider provider = CreateHeadlessProvider(services =>
            {
                services.Replace(ServiceDescriptor.Singleton<IOwnerContextAccessor>(Owners));
                if (store is not null) services.Replace(ServiceDescriptor.Singleton(store));
                // Count calls around the actual production query constructor using
                // the registered source/codec. Provider admission stays registered.
                services.Replace(ServiceDescriptor.Singleton<IWorkspaceRuleQuestionService>(resolver =>
                    new ObservedRuleQuestionService(new WorkspaceRuleQuestionService(
                        resolver.GetRequiredService<IOwnerContextAccessor>(), resolver.GetRequiredService<IWorkspaceStore>(),
                        resolver.GetRequiredService<IRulesetWorkspaceCodecResolver>(), resolver.GetRequiredService<ICharacterSourceDataResolver>()),
                        afterResolve)));
            });
            observed = (ObservedRuleQuestionService)provider.GetRequiredService<IWorkspaceRuleQuestionService>();
            return provider;
        }

        public WorkspaceStoredDocument SeedHeadlessWorkspace(IServiceProvider provider, OwnerContextStamp stamp)
        {
            IWorkspaceStore store = provider.GetRequiredService<IWorkspaceStore>();
            Assert.AreSame(Store, store);
            Assert.IsTrue(stamp.Owner.IsLocalSingleUser, "The unchanged production owner selects the local store overload.");
            WorkspaceDocument document = Read().Document;
            WorkspaceDocument initial = document with { State = document.State with
            {
                Payload = document.Content.Replace("<alias>Grounded</alias>", "<alias>Local before</alias>", StringComparison.Ordinal)
            } };
            Assert.IsTrue(store.CreateWorkspaceDocument(WorkspaceId, initial).Success);
            Assert.IsTrue(store.SaveCheckpoint(WorkspaceId, 1).Success);
            Assert.IsTrue(store.ReplaceWorkspaceDocument(WorkspaceId, 1, document).Success);
            WorkspaceStoreReadResult result = store.Get(WorkspaceId);
            Assert.IsTrue(result.Success, result.Error);
            Assert.AreEqual(2L, result.Value!.ContentRevision);
            Assert.AreEqual(1L, result.Value.SavedRevision);
            return result.Value;
        }

        public void Dispose() => Directory.Delete(_root, recursive: true);
    }

    private sealed class ObservedRuleQuestionService(IWorkspaceRuleQuestionService inner, Action? afterResolve)
        : IWorkspaceRuleQuestionService
    {
        public int ResolveCount { get; private set; }
        public WorkspaceRuleQuestionResult? LastResult { get; private set; }
        public WorkspaceRuleQuestionResult Resolve(OwnerContextStamp expectedOwner, WorkspaceRuleQuestionRequest request)
        {
            ResolveCount++;
            LastResult = inner.Resolve(expectedOwner, request);
            afterResolve?.Invoke();
            return LastResult;
        }
    }

    private sealed class ThrowingCountList(TestOwner owner) : IReadOnlyList<string>
    {
        public const string ExceptionMarker = "Hostile collection count.";
        public int CountReads { get; private set; }
        public int ActiveLeasesAtCount { get; private set; }
        public int Count
        {
            get
            {
                CountReads++;
                ActiveLeasesAtCount = owner.ActiveLeases;
                throw new InvalidOperationException(ExceptionMarker);
            }
        }
        public string this[int index] => throw new InvalidOperationException("Hostile collection index.");
        public IEnumerator<string> GetEnumerator() => throw new InvalidOperationException("Hostile collection enumeration.");
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class ThrowingStampOwner(TestOwner inner) : IOwnerContextLeaseAccessor
    {
        public int StampReadCount { get; private set; }
        public int DisposeCount { get; private set; }
        public OwnerScope Current => inner.Current;
        public OwnerContextStamp Capture() => inner.Capture();
        public bool TryAcquire(OwnerContextStamp expected, [NotNullWhen(true)] out IOwnerContextLease? lease)
        {
            lease = null;
            if (!inner.TryAcquire(expected, out IOwnerContextLease? admitted))
                return false;
            lease = new ThrowingLease(this, admitted);
            return true;
        }

        private sealed class ThrowingLease(ThrowingStampOwner owner, IOwnerContextLease admitted) : IOwnerContextLease
        {
            public OwnerContextStamp Stamp
            {
                get
                {
                    owner.StampReadCount++;
                    throw new InvalidOperationException("The admitted authority could not read its lease stamp.");
                }
            }
            public void Dispose()
            {
                owner.DisposeCount++;
                admitted.Dispose();
            }
        }
    }

    private sealed class NameOnlyOwner : IOwnerContextAccessor
    {
        public OwnerScope Current => Owner;
    }

    // Only the read observation is perturbed. Documents come from FileWorkspaceStore;
    // the source resolver, codec, XML adapters, and grouped rules remain production code.
    private sealed class ObservedStore(FileWorkspaceStore inner, TestOwner owner) : IWorkspaceStore
    {
        public int ReadCount { get; private set; }
        public int LocalReadCount { get; private set; }
        public Func<int, WorkspaceStoredDocument, WorkspaceStoredDocument>? OnRead { get; init; }
        public WorkspaceStoreReadResult Get(OwnerScope requestedOwner, CharacterWorkspaceId id)
        {
            Assert.AreEqual(1, owner.ActiveLeases, "Every read must hold the actual owner authority lease.");
            Assert.AreEqual(owner.Current, requestedOwner);
            WorkspaceStoreReadResult result = inner.Get(requestedOwner, id);
            ReadCount++;
            return result.Value is null || OnRead is null ? result : result with { Value = OnRead(ReadCount, result.Value) };
        }
        public WorkspaceStoreReadResult Get(CharacterWorkspaceId id)
        {
            LocalReadCount++;
            throw new AssertFailedException("Unexpected local workspace fallback.");
        }
        public WorkspaceStoreMutationResult CreateWorkspaceDocument(WorkspaceDocument document) => throw Mutation();
        public WorkspaceStoreMutationResult CreateWorkspaceDocument(OwnerScope owner, WorkspaceDocument document) => throw Mutation();
        public IReadOnlyList<WorkspaceStoreEntry> List() => throw new AssertFailedException("Unexpected workspace enumeration.");
        public IReadOnlyList<WorkspaceStoreEntry> List(OwnerScope owner) => throw new AssertFailedException("Unexpected owner enumeration.");
        public WorkspaceStoreMutationResult ReplaceWorkspaceDocument(CharacterWorkspaceId id, long expectedContentRevision, WorkspaceDocument document) => throw Mutation();
        public WorkspaceStoreMutationResult ReplaceWorkspaceDocument(OwnerScope owner, CharacterWorkspaceId id, long expectedContentRevision, WorkspaceDocument document) => throw Mutation();
        public WorkspaceStoreMutationResult SaveCheckpoint(CharacterWorkspaceId id, long expectedContentRevision) => throw Mutation();
        public WorkspaceStoreMutationResult SaveCheckpoint(OwnerScope owner, CharacterWorkspaceId id, long expectedContentRevision) => throw Mutation();
        public WorkspaceStoreMutationResult Delete(CharacterWorkspaceId id, long expectedContentRevision) => throw Mutation();
        public WorkspaceStoreMutationResult Delete(OwnerScope owner, CharacterWorkspaceId id, long expectedContentRevision) => throw Mutation();
        private static AssertFailedException Mutation() => new("Read-only rule resolution attempted a store mutation.");
    }

    private sealed class ObservedSources(FileSystemCharacterSourceDataResolver inner, TestOwner owner)
        : ICharacterSourceDataResolver, ICharacterSourceDataResolverOperationScopeFactory
    {
        public int OperationCount { get; private set; }
        public int ContextReadCount { get; private set; }
        public int DisposeCount { get; private set; }
        public Action<int, string, ICharacterSourceDataResolverOperationScope>? BeforeContextRead { get; init; }
        private void AssertLease() => Assert.AreEqual(1, owner.ActiveLeases);
        public ICharacterSourceDataContext? TryCreateContext(string xml)
        {
            AssertLease();
            return inner.TryCreateContext(xml);
        }
        public ICharacterSourceDataResolverOperationScope CreateOperationScope()
        {
            AssertLease();
            OperationCount++;
            return new Scope(this, inner.CreateOperationScope());
        }
        private sealed class Scope(ObservedSources observed, ICharacterSourceDataResolverOperationScope innerScope)
            : ICharacterSourceDataResolverOperationScope
        {
            private string? _xml;
            public ICharacterSourceDataContext? TryCreateContext(string xml)
            {
                observed.AssertLease();
                _xml ??= xml;
                Assert.AreEqual(_xml, xml, "Final admission must retain the exact original XML cache key.");
                observed.ContextReadCount++;
                observed.BeforeContextRead?.Invoke(observed.ContextReadCount, xml, innerScope);
                return innerScope.TryCreateContext(xml);
            }
            public void Dispose() { observed.DisposeCount++; innerScope.Dispose(); }
        }
    }

    private sealed class TestOwner : IOwnerContextLeaseAccessor
    {
        private readonly object _gate = new();
        private readonly string _authority = Guid.NewGuid().ToString("N");
        private OwnerScope _current = Owner;
        private long _revision = 7;
        public int ActiveLeases { get; private set; }
        public int SuccessfulLeaseAcquisitions { get; private set; }
        public OwnerScope Current { get { lock (_gate) return _current; } }
        public OwnerContextStamp Capture() { lock (_gate) return new(_current, _authority, _revision); }
        public void SwitchTo(OwnerScope owner) { lock (_gate) { Assert.AreEqual(0, ActiveLeases); _current = owner; _revision++; } }

        public bool TryAcquire(OwnerContextStamp expected, [NotNullWhen(true)] out IOwnerContextLease? lease)
        {
            Monitor.Enter(_gate);
            lease = null;
            if (!expected.IsValid || expected != Capture()) { Monitor.Exit(_gate); return false; }
            Assert.AreEqual(0, ActiveLeases);
            ActiveLeases++;
            SuccessfulLeaseAcquisitions++;
            lease = new Lease(this, expected);
            return true;
        }

        private sealed class Lease(TestOwner owner, OwnerContextStamp stamp) : IOwnerContextLease
        {
            private bool _disposed;
            public OwnerContextStamp Stamp => !_disposed ? stamp : throw new ObjectDisposedException(nameof(Lease));
            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                owner.ActiveLeases--;
                Monitor.Exit(owner._gate);
            }
        }
    }
}

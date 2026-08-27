using System.Xml.Linq;
using System.Text.Json;
using Chummer.Application.Characters;
using Chummer.Application.LifeModules;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Api;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Rulesets;
using Chummer.Contracts.LifeModules;
using Chummer.Contracts.Workspaces;
using Chummer.Infrastructure.Files;
using Chummer.Infrastructure.Workspaces;
using Chummer.Infrastructure.Xml;
using Chummer.Infrastructure.DependencyInjection;
using Chummer.Rulesets.Hosting;
using Chummer.Rulesets.Sr5;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Extensions.DependencyInjection;

namespace Chummer.Tests;

[TestClass]
public sealed class CharacterCreationBootstrapServiceTests
{
    private const string CanonicalPrioritySettingsId =
        CharacterCreationBootstrapProfiles.PrioritySettingsProfileId;
    private const string CanonicalSumToTenSettingsId =
        CharacterCreationBootstrapProfiles.SumToTenSettingsProfileId;
    private const string CanonicalKarmaSettingsId =
        CharacterCreationBootstrapProfiles.KarmaSettingsProfileId;
    private const string CanonicalLifeModulesSettingsId =
        CharacterCreationBootstrapProfiles.LifeModulesSettingsProfileId;

    [DataTestMethod]
    [DataRow(CharacterCreationBuildMethods.Priority, CanonicalPrioritySettingsId)]
    [DataRow(CharacterCreationBuildMethods.SumToTen, CanonicalSumToTenSettingsId)]
    [DataRow(CharacterCreationBuildMethods.Karma, CanonicalKarmaSettingsId)]
    [DataRow(CharacterCreationBuildMethods.LifeModules, CanonicalLifeModulesSettingsId)]
    public void Canonical_profile_resolution_is_the_single_source_of_method_tuple_truth(
        string buildMethod,
        string expectedSettingsProfileId)
    {
        Assert.IsTrue(CharacterCreationBootstrapProfiles.TryResolveCanonicalSettingsProfileId(
            buildMethod,
            out string settingsProfileId));
        Assert.AreEqual(expectedSettingsProfileId, settingsProfileId);
        Assert.IsTrue(CharacterCreationBootstrapProfiles.IsExactCanonicalTuple(
            buildMethod,
            settingsProfileId));

        Assert.IsFalse(CharacterCreationBootstrapProfiles.TryResolveCanonicalSettingsProfileId(
            buildMethod.ToLowerInvariant(),
            out string unsupportedProfileId));
        Assert.AreEqual(string.Empty, unsupportedProfileId);
    }

    [TestMethod]
    public void Generic_character_validation_accepts_the_pending_priority_shape_without_trusting_the_marker()
    {
        string xml = MinimalMarkerXml();
        var files = new CharacterFileService();

        CharacterValidationResult validation = files.ValidateXml(xml);
        CharacterFileSummary summary = files.ParseSummaryFromXml(xml);

        Assert.IsTrue(validation.IsValid, string.Join(",", validation.Issues.Select(issue => issue.Code)));
        Assert.IsFalse(validation.Issues.Any(issue =>
            issue.Severity == "Error" && issue.Path == "/character/metatype"));
        Assert.AreEqual(string.Empty, summary.Metatype);
        Assert.IsFalse(summary.Created);

        string unknownMarker = xml.Replace(
            CharacterCreationBootstrapSchemas.MarkerV1,
            "unknown-marker",
            StringComparison.Ordinal);
        CharacterValidationResult unknownValidation = files.ValidateXml(unknownMarker);
        Assert.IsTrue(unknownValidation.IsValid,
            "Generic shape validation is not marker authority; the bootstrap service validates the marker binding.");
    }

    [TestMethod]
    public void Canonical_priority_bootstrap_atomically_binds_and_loads_real_creation_authorities()
    {
        string coreRoot = FindCoreRoot();
        var store = new InMemoryWorkspaceStore();
        FileSystemCharacterSourceDataResolver sourceResolver = CreateSourceResolver(coreRoot);
        ICharacterFileQueries queries = CreateFileQueries();
        CharacterCreationBootstrapService service = CreateService(
            store,
            sourceResolver,
            queries);

        CharacterCreationBootstrapResult<CharacterCreationBootstrapReceipt> created =
            service.Create(CanonicalRequest());

        Assert.AreEqual(CharacterCreationBootstrapOutcomes.Success, created.Outcome,
            string.Join(",", created.Blockers));
        CharacterCreationBootstrapReceipt receipt = created.Value!;
        Assert.IsNotNull(receipt);
        Assert.AreEqual(1, receipt.ContentRevision);
        Assert.AreEqual(0, receipt.SavedRevision);
        Assert.AreEqual(string.Empty, receipt.Summary.Metatype);
        Assert.IsFalse(receipt.Summary.Created);
        Assert.IsTrue(CharacterCreationBootstrapBindingDigest.IsValid(receipt.Binding));
        Assert.IsTrue(CharacterCreationBootstrapReceiptDigest.IsValid(receipt));
        Assert.AreEqual(
            CharacterCreationBootstrapRevisions.InitialContentRevision,
            receipt.Binding.InitialContentRevision);
        Assert.AreEqual(
            CharacterCreationBootstrapRevisions.InitialSavedRevision,
            receipt.Binding.InitialSavedRevision);
        CollectionAssert.AreEqual(
            CharacterCreationBootstrapProfiles.ExpectedSourceAnchorIds(
                CharacterCreationBuildMethods.Priority,
                CanonicalPrioritySettingsId),
            receipt.Binding.SourceAnchorIds.ToArray());
        CollectionAssert.AreEqual(
            receipt.Binding.SourceAnchorIds.ToArray(),
            receipt.SourceAnchorIds.ToArray());
        Assert.IsTrue(CharacterCreationPrerequisiteAuthorityDigest.IsCanonical(
            receipt.Binding.RawProfileInputsDigest));
        Assert.IsTrue(CharacterCreationPrerequisiteAuthorityDigest.IsCanonical(
            receipt.Binding.MetatypeAuthorityDigest));
        Assert.IsTrue(CharacterCreationPrerequisiteAuthorityDigest.IsCanonical(
            receipt.Binding.PrerequisiteAuthorityDigest));
        CollectionAssert.Contains(
            receipt.SourceAnchorIds.ToList(),
            $"settings.xml#setting:{CanonicalPrioritySettingsId}");
        CollectionAssert.Contains(receipt.SourceAnchorIds.ToList(), "metatypes.xml");
        CollectionAssert.Contains(receipt.SourceAnchorIds.ToList(), "priorities.xml");

        WorkspaceStoredDocument workspace = store.Get(receipt.WorkspaceId).Value!;
        Assert.IsNotNull(workspace);
        XDocument xml = XDocument.Parse(workspace.Document.Content);
        Assert.IsNull(xml.Root!.Element("metatype"));
        Assert.HasCount(1, xml.Root.Elements(CharacterCreationBootstrapXml.MarkerElement));
        Assert.IsFalse(xml.Root.Elements().Any(element =>
            element.Name.LocalName.StartsWith("priority", StringComparison.Ordinal)));
        Assert.IsNull(xml.Root.Element("lifemodules"));
        Assert.IsTrue(CreateFileQueries().Validate(
            new CharacterDocument(workspace.Document.Content)).IsValid,
            "The generic file contract accepts the pending typed Priority shape; "
            + "the bootstrap binding remains the authority for its incomplete state.");
        Assert.IsTrue(CharacterCreationBootstrapAuthority.TryValidatePending(
            workspace,
            sourceResolver,
            out IReadOnlyList<string> bootstrapBlockers),
            string.Join(",", bootstrapBlockers));

        var foundation = new CharacterCreationFoundationService(
            store,
            queries,
            sourceResolver,
            new XmlLifeModulesCatalogService(
                Path.Combine(coreRoot, "Chummer", "data", "lifemodules.xml")),
            new UnavailableCharacterCreationFoundationApplyAuthority());
        CharacterCreationFoundationResult<CharacterCreationFoundationState> foundationLoaded =
            foundation.Load(new CharacterCreationFoundationLoadRequest(receipt.WorkspaceId));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, foundationLoaded.Outcome,
            string.Join(",", foundationLoaded.Blockers));
        CharacterCreationFoundationState foundationState = foundationLoaded.Value!;
        Assert.IsNotNull(foundationState);
        Assert.AreEqual(string.Empty, foundationState.CurrentMetatype);
        Assert.IsTrue(foundationState.MetatypeOptions.Count > 0);
        Assert.IsTrue(foundationState.MetatypeOptions.All(option =>
            option.SourceAnchorIds.Count > 0));
        Assert.IsTrue(CharacterCreationPrerequisiteAuthorityDigest.IsCanonical(
            foundationState.Binding.SourceDigest));
        CollectionAssert.DoesNotContain(
            foundationState.AuthorityBlockers.ToList(),
            CharacterCreationFoundationBlockers.CharacterDocumentInvalid);

        var prerequisites = new CharacterCreationPrerequisiteService(
            store,
            queries,
            sourceResolver);
        CharacterCreationFoundationResult<CharacterCreationPrerequisiteState> prerequisiteLoaded =
            prerequisites.Load(new CharacterCreationPrerequisiteLoadRequest(receipt.WorkspaceId));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, prerequisiteLoaded.Outcome,
            string.Join(",", prerequisiteLoaded.Blockers));
        CharacterCreationPrerequisiteState prerequisiteState = prerequisiteLoaded.Value!;
        Assert.IsNotNull(prerequisiteState);
        Assert.IsTrue(prerequisiteState.Authority.IsAuthoritative,
            string.Join(",", prerequisiteState.Authority.Blockers));
        Assert.IsTrue(prerequisiteState.Authority.Options.Count > 0);
        Assert.IsTrue(prerequisiteState.Authority.SourceAnchorIds.Count > 0);
        Assert.IsTrue(CharacterCreationPrerequisiteAuthorityDigest.EqualsFixedTime(
            prerequisiteState.Authority.AuthorityDigest,
            receipt.Binding.PrerequisiteAuthorityDigest));
        CollectionAssert.DoesNotContain(
            prerequisiteState.Blockers.ToList(),
            CharacterCreationPrerequisiteBlockers.CharacterDocumentInvalid);
    }

    [TestMethod]
    public void Activation_bundle_uses_one_source_context_no_store_read_and_matches_individual_loads()
    {
        string coreRoot = FindCoreRoot();
        var store = new CountingWorkspaceStore();
        var sourceResolver = new CountingSourceDataResolver(CreateSourceResolver(coreRoot));
        ICharacterFileQueries queries = CreateFileQueries();
        var lifeModules = new CountingLifeModulesCatalogService(
            new XmlLifeModulesCatalogService(
                Path.Combine(coreRoot, "Chummer", "data", "lifemodules.xml")));
        var applyAuthority = new UnavailableCharacterCreationFoundationApplyAuthority();
        var projector = new CharacterCreationBootstrapActivationProjector(
            store,
            queries,
            lifeModules,
            applyAuthority);
        CharacterCreationBootstrapService service = CreateService(
            store,
            sourceResolver,
            queries,
            projector);

        CharacterCreationBootstrapActivationAttempt attempt = service.CreateActivation(
            CanonicalRequest());

        Assert.AreEqual(CharacterCreationBootstrapOutcomes.Success, attempt.Outcome,
            string.Join(",", attempt.Blockers));
        Assert.IsNotNull(attempt.Receipt);
        Assert.IsNotNull(attempt.Bundle, string.Join(",", attempt.Blockers));
        Assert.AreEqual(1, sourceResolver.ContextCreateCount,
            "Atomic create and every frozen domain projection must share one source context.");
        Assert.AreEqual(1, sourceResolver.SourceProfileResolveCount);
        Assert.AreEqual(1, sourceResolver.MetatypeResolveCount);
        Assert.AreEqual(1, sourceResolver.PrerequisiteResolveCount);
        Assert.AreEqual(1, sourceResolver.QualitiesResolveCount);
        Assert.AreEqual(1, sourceResolver.MagicResolveCount);
        Assert.AreEqual(1, lifeModules.AuthorityReadCount);
        Assert.AreEqual(1, lifeModules.OptionProjectionCount);
        Assert.AreEqual(0, store.ReadCount,
            "The activation bundle must be projected from the atomic create result, not a store reread.");
        Assert.IsTrue(CharacterCreationBootstrapActivationIntegrity.IsValid(attempt.Bundle));
        Assert.IsTrue(service.TryValidateCurrent(attempt.Bundle, out IReadOnlyList<string> freshnessBlockers),
            string.Join(",", freshnessBlockers));
        Assert.HasCount(0, freshnessBlockers);
        Assert.AreEqual(2, sourceResolver.ContextCreateCount,
            "Consumer acceptance performs exactly one fresh source-context capture.");
        Assert.AreEqual(2, sourceResolver.SourceProfileResolveCount);
        Assert.AreEqual(2, sourceResolver.MetatypeResolveCount);
        Assert.AreEqual(2, sourceResolver.PrerequisiteResolveCount);
        Assert.AreEqual(2, sourceResolver.QualitiesResolveCount);
        Assert.AreEqual(2, sourceResolver.MagicResolveCount);
        Assert.AreEqual(2, lifeModules.AuthorityReadCount);
        Assert.AreEqual(2, lifeModules.OptionProjectionCount);
        Assert.IsFalse(service.TryValidateCurrent(attempt.Bundle, out _),
            "Activation authority is one-shot and cannot be replayed.");
        Assert.AreEqual(2, sourceResolver.ContextCreateCount,
            "A rejected replay must not touch source authority.");

        CharacterWorkspaceId workspaceId = attempt.Receipt.WorkspaceId;
        CharacterCreationInitialProjection aggregate = attempt.Bundle.InitialCreation;
        CharacterCreationFoundationResult<CharacterCreationFoundationState> foundation =
            new CharacterCreationFoundationService(
                store, queries, sourceResolver, lifeModules, applyAuthority)
            .Load(new(workspaceId));
        var prerequisites = new CharacterCreationPrerequisiteService(
            store, queries, sourceResolver);
        var attributes = new CharacterCreationAttributesService(store, sourceResolver);
        CharacterCreationFoundationResult<CharacterCreationPrerequisiteState> prerequisite =
            prerequisites.Load(new(workspaceId));
        CharacterCreationFoundationResult<CharacterCreationAttributesState> attribute =
            attributes.Load(new(workspaceId));
        CharacterCreationContactResult<CharacterCreationContactsState> contacts =
            new CharacterCreationContactsService(store).Load(new(workspaceId));
        CharacterCreationFoundationResult<CharacterCreationQualitiesState> qualities =
            new CharacterCreationQualitiesService(
                store, sourceResolver, prerequisites, attributes)
            .Load(new(workspaceId));
        CharacterCreationFoundationResult<CharacterCreationMagicResonanceState> magic =
            new CharacterCreationMagicResonanceService(store, sourceResolver)
            .Load(new(workspaceId));

        AssertJsonEqual(foundation, aggregate.Foundation);
        AssertJsonEqual(prerequisite, aggregate.Prerequisite);
        AssertJsonEqual(attribute, aggregate.Attributes);
        AssertJsonEqual(contacts, aggregate.Contacts);
        AssertJsonEqual(qualities, aggregate.Qualities);
        AssertJsonEqual(magic, aggregate.MagicResonance);
    }

    [TestMethod]
    public void Activation_bundle_rejects_recovery_source_and_aggregate_tampering()
    {
        string coreRoot = FindCoreRoot();
        var store = new CountingWorkspaceStore();
        var sourceResolver = new CountingSourceDataResolver(CreateSourceResolver(coreRoot));
        ICharacterFileQueries queries = CreateFileQueries();
        var projector = new CharacterCreationBootstrapActivationProjector(
            store,
            queries,
            new XmlLifeModulesCatalogService(
                Path.Combine(coreRoot, "Chummer", "data", "lifemodules.xml")),
            new UnavailableCharacterCreationFoundationApplyAuthority());
        CharacterCreationBootstrapService service = CreateService(
            store,
            sourceResolver,
            queries,
            projector);
        CharacterCreationBootstrapActivationAttempt attempt = service.CreateActivation(
            CanonicalRequest());
        CharacterCreationBootstrapActivationBundle bundle = attempt.Bundle!;
        Assert.IsTrue(CharacterCreationBootstrapActivationIntegrity.IsValid(bundle));

        Assert.IsFalse(CharacterCreationBootstrapActivationIntegrity.IsValid(bundle with
        {
            RecoveryBinding = bundle.RecoveryBinding with
            {
                AuxiliaryStateDigest = new string('0', 64)
            }
        }));
        Assert.IsFalse(CharacterCreationBootstrapActivationIntegrity.IsValid(bundle with
        {
            RecoveryBinding = bundle.RecoveryBinding with
            {
                RawProfileInputsDigest = "sha256:" + new string('0', 64)
            }
        }));
        Assert.IsFalse(CharacterCreationBootstrapActivationIntegrity.IsValid(bundle with
        {
            InitialCreation = bundle.InitialCreation with
            {
                Attributes = bundle.InitialCreation.Attributes with
                {
                    Blockers = ["invented-blocker"]
                }
            }
        }));

        CharacterCreationBootstrapActivationBundle overviewTamper = ResignActivation(bundle with
        {
            WorkspaceProjection = bundle.WorkspaceProjection with
            {
                Overview = bundle.WorkspaceProjection.Overview with
                {
                    Profile = bundle.WorkspaceProjection.Overview.Profile with
                    {
                        Name = "Re-signed forgery"
                    }
                }
            }
        });
        Assert.IsFalse(CharacterCreationBootstrapActivationIntegrity.IsValid(overviewTamper));

        CharacterCreationQualitiesState qualities = bundle.InitialCreation.Qualities.Value!;
        CharacterCreationBootstrapActivationBundle qualitiesAuthorityTamper = ResignActivation(
            bundle with
            {
                InitialCreation = bundle.InitialCreation with
                {
                    Qualities = bundle.InitialCreation.Qualities with
                    {
                        Value = qualities with
                        {
                            Authority = qualities.Authority with
                            {
                                SourceDigest = "sha256:" + new string('0', 64)
                            }
                        }
                    }
                }
            });
        Assert.IsFalse(CharacterCreationBootstrapActivationIntegrity.IsValid(
            qualitiesAuthorityTamper));

        Assert.IsTrue(qualities.Authority.Options.Count > 0);
        CharacterCreationQualityCatalogOption firstQuality = qualities.Authority.Options[0];
        CharacterCreationBootstrapActivationBundle qualityOptionTamper = ResignActivation(
            bundle with
            {
                InitialCreation = bundle.InitialCreation with
                {
                    Qualities = bundle.InitialCreation.Qualities with
                    {
                        Value = qualities with
                        {
                            Authority = qualities.Authority with
                            {
                                Options =
                                [
                                    firstQuality with { Name = firstQuality.Name + " forged" },
                                    .. qualities.Authority.Options.Skip(1)
                                ]
                            }
                        }
                    }
                }
            });
        Assert.IsFalse(CharacterCreationBootstrapActivationIntegrity.IsValid(qualityOptionTamper));

        CharacterCreationMagicResonanceState magic =
            bundle.InitialCreation.MagicResonance.Value!;
        Assert.IsTrue(magic.Authority.Traditions.Count > 0);
        CharacterCreationMagicResonanceCatalogOption firstTradition =
            magic.Authority.Traditions[0];
        CharacterCreationBootstrapActivationBundle magicCatalogTamper = ResignActivation(
            bundle with
            {
                InitialCreation = bundle.InitialCreation with
                {
                    MagicResonance = bundle.InitialCreation.MagicResonance with
                    {
                        Value = magic with
                        {
                            Authority = magic.Authority with
                            {
                                Traditions =
                                [
                                    firstTradition with { Name = firstTradition.Name + " forged" },
                                    .. magic.Authority.Traditions.Skip(1)
                                ]
                            }
                        }
                    }
                }
            });
        Assert.IsFalse(CharacterCreationBootstrapActivationIntegrity.IsValid(magicCatalogTamper));

        CharacterCreationAttributesState attributes = bundle.InitialCreation.Attributes.Value!;
        Assert.IsFalse(CharacterCreationBootstrapActivationIntegrity.IsValid(ResignActivation(
            bundle with
            {
                InitialCreation = bundle.InitialCreation with
                {
                    Attributes = bundle.InitialCreation.Attributes with
                    {
                        Value = attributes with { CanEdit = !attributes.CanEdit }
                    }
                }
            })));
        CharacterCreationFoundationState foundation = bundle.InitialCreation.Foundation.Value!;
        Assert.IsFalse(CharacterCreationBootstrapActivationIntegrity.IsValid(ResignActivation(
            bundle with
            {
                InitialCreation = bundle.InitialCreation with
                {
                    Foundation = bundle.InitialCreation.Foundation with
                    {
                        Value = foundation with
                        {
                            ResumeStatus = CharacterCreationFoundationResumeStatuses.PendingDraft
                        }
                    }
                }
            })));
        Assert.IsFalse(CharacterCreationBootstrapActivationIntegrity.IsValid(ResignActivation(
            bundle with
            {
                InitialCreation = bundle.InitialCreation with
                {
                    Foundation = bundle.InitialCreation.Foundation with
                    {
                        Outcome = CharacterCreationFoundationOutcomes.Invalid
                    }
                }
            })));

        CharacterCreationBootstrapBinding documentBinding =
            bundle.WorkspaceProjection.Workspace.Document.AuxiliaryState
                .CharacterCreationBootstrapBinding!;
        CharacterCreationBootstrapBinding hostileDocumentBinding = ResignBinding(
            documentBinding with
            {
                RawProfileInputsDigest = "sha256:" + new string('0', 64)
            });
        WorkspaceDocument hostileDocument = bundle.WorkspaceProjection.Workspace.Document with
        {
            State = bundle.WorkspaceProjection.Workspace.Document.State with
            {
                AuxiliaryState = new WorkspaceDocumentAuxiliaryState(
                    CharacterCreationBootstrapBinding: hostileDocumentBinding)
            }
        };
        Assert.IsFalse(CharacterCreationBootstrapActivationIntegrity.IsValid(ResignActivation(
            bundle with
            {
                WorkspaceProjection = bundle.WorkspaceProjection with
                {
                    Workspace = bundle.WorkspaceProjection.Workspace with
                    {
                        Document = hostileDocument
                    }
                }
            })));
    }

    [TestMethod]
    public void Activation_integrity_returns_false_for_hostile_null_graphs()
    {
        string coreRoot = FindCoreRoot();
        var store = new CountingWorkspaceStore();
        ICharacterFileQueries queries = CreateFileQueries();
        var projector = new CharacterCreationBootstrapActivationProjector(
            store,
            queries,
            new XmlLifeModulesCatalogService(
                Path.Combine(coreRoot, "Chummer", "data", "lifemodules.xml")),
            new UnavailableCharacterCreationFoundationApplyAuthority());
        CharacterCreationBootstrapActivationBundle bundle = CreateService(
            store,
            CreateSourceResolver(coreRoot),
            queries,
            projector).CreateActivation(CanonicalRequest()).Bundle!;

        Assert.IsFalse(CharacterCreationBootstrapActivationIntegrity.IsValid(
            bundle with { WorkspaceProjection = null! }));
        Assert.IsFalse(CharacterCreationBootstrapActivationIntegrity.IsValid(
            bundle with { RecoveryBinding = null! }));
        Assert.IsFalse(CharacterCreationBootstrapActivationIntegrity.IsValid(
            bundle with { InitialCreation = null! }));
        Assert.IsFalse(CharacterCreationBootstrapActivationIntegrity.IsValid(
            bundle with
            {
                InitialCreation = bundle.InitialCreation with { SourceAuthority = null! }
            }));
        Assert.IsFalse(CharacterCreationBootstrapActivationIntegrity.IsValid(
            bundle with
            {
                InitialCreation = bundle.InitialCreation with { Qualities = null! }
            }));
    }

    [TestMethod]
    public void Dependency_injection_aliases_legacy_and_activation_to_the_same_singleton()
    {
        string coreRoot = FindCoreRoot();
        var services = new ServiceCollection();
        services.AddChummerHeadlessCore(coreRoot, coreRoot);
        using ServiceProvider provider = services.BuildServiceProvider();

        ICharacterCreationBootstrapService legacy =
            provider.GetRequiredService<ICharacterCreationBootstrapService>();
        ICharacterCreationBootstrapActivationService activation =
            provider.GetRequiredService<ICharacterCreationBootstrapActivationService>();

        Assert.AreSame((object)legacy, activation);
    }

    [DataTestMethod]
    [DataRow("settings")]
    [DataRow("metatypes")]
    [DataRow("priorities")]
    [DataRow("skills")]
    [DataRow("qualities")]
    [DataRow("traditions")]
    [DataRow("streams")]
    [DataRow("powers")]
    [DataRow("spells")]
    [DataRow("complexforms")]
    public void Consumer_freshness_rejects_each_captured_source_domain_drift(string domain)
    {
        string coreRoot = FindCoreRoot();
        var store = new CountingWorkspaceStore();
        var sourceResolver = new ConsumerDriftSourceDataResolver(
            CreateSourceResolver(coreRoot),
            domain);
        ICharacterFileQueries queries = CreateFileQueries();
        var projector = new CharacterCreationBootstrapActivationProjector(
            store,
            queries,
            new XmlLifeModulesCatalogService(
                Path.Combine(coreRoot, "Chummer", "data", "lifemodules.xml")),
            new UnavailableCharacterCreationFoundationApplyAuthority());
        CharacterCreationBootstrapService service = CreateService(
            store,
            sourceResolver,
            queries,
            projector);
        CharacterCreationBootstrapActivationBundle bundle = service
            .CreateActivation(CanonicalRequest()).Bundle!;
        Assert.IsNotNull(bundle);

        Assert.IsFalse(service.TryValidateCurrent(bundle, out IReadOnlyList<string> blockers));
        CollectionAssert.Contains(
            blockers.ToList(),
            CharacterCreationBootstrapBlockers.ActivationProjectionUnavailable);
        Assert.AreEqual(2, sourceResolver.ContextCreateCount);
    }

    [TestMethod]
    public void Consumer_freshness_rejects_life_modules_source_drift()
    {
        string coreRoot = FindCoreRoot();
        var store = new CountingWorkspaceStore();
        ICharacterFileQueries queries = CreateFileQueries();
        var lifeModules = new DriftingLifeModulesCatalogService(
            new XmlLifeModulesCatalogService(
                Path.Combine(coreRoot, "Chummer", "data", "lifemodules.xml")));
        var projector = new CharacterCreationBootstrapActivationProjector(
            store,
            queries,
            lifeModules,
            new UnavailableCharacterCreationFoundationApplyAuthority());
        CharacterCreationBootstrapService service = CreateService(
            store,
            CreateSourceResolver(coreRoot),
            queries,
            projector);
        CharacterCreationBootstrapActivationBundle bundle = service
            .CreateActivation(CanonicalRequest()).Bundle!;
        Assert.IsNotNull(bundle);

        lifeModules.Drifted = true;
        Assert.IsFalse(service.TryValidateCurrent(bundle, out _));
    }

    [TestMethod]
    public void Consumer_freshness_fails_closed_when_source_authority_drifts_after_creation()
    {
        string coreRoot = FindCoreRoot();
        var store = new CountingWorkspaceStore();
        var sourceResolver = new DriftingSourceDataResolver(CreateSourceResolver(coreRoot));
        ICharacterFileQueries queries = CreateFileQueries();
        var projector = new SourceDriftingProjector(
            new CharacterCreationBootstrapActivationProjector(
                store,
                queries,
                new XmlLifeModulesCatalogService(
                    Path.Combine(coreRoot, "Chummer", "data", "lifemodules.xml")),
                new UnavailableCharacterCreationFoundationApplyAuthority()));

        CharacterCreationBootstrapService service = CreateService(
            store,
            sourceResolver,
            queries,
            projector);
        CharacterCreationBootstrapActivationAttempt attempt = service.CreateActivation(
            CanonicalRequest());

        Assert.AreEqual(CharacterCreationBootstrapOutcomes.Success, attempt.Outcome);
        Assert.IsNotNull(attempt.Receipt,
            "The already-committed workspace receipt must remain available for full fallback loading.");
        Assert.IsNotNull(attempt.Bundle,
            "Projection must use only the immutable initial typed-authority capture.");
        Assert.AreEqual(1, sourceResolver.ContextCreateCount);
        Assert.IsFalse(service.TryValidateCurrent(attempt.Bundle, out _));
        Assert.AreEqual(2, sourceResolver.ContextCreateCount);
        Assert.HasCount(1, store.List());
    }

    [DataTestMethod]
    [DataRow(CharacterCreationBuildMethods.SumToTen, CanonicalSumToTenSettingsId)]
    [DataRow(CharacterCreationBuildMethods.Karma, CanonicalKarmaSettingsId)]
    [DataRow(CharacterCreationBuildMethods.LifeModules, CanonicalLifeModulesSettingsId)]
    public void Non_priority_activation_is_an_explicit_committed_reload_fallback(
        string buildMethod,
        string settingsProfileId)
    {
        string coreRoot = FindCoreRoot();
        var store = new CountingWorkspaceStore();
        ICharacterFileQueries queries = CreateFileQueries();
        var sourceResolver = new CountingSourceDataResolver(CreateSourceResolver(coreRoot));
        var projector = new CharacterCreationBootstrapActivationProjector(
            store,
            queries,
            new XmlLifeModulesCatalogService(
                Path.Combine(coreRoot, "Chummer", "data", "lifemodules.xml")),
            new UnavailableCharacterCreationFoundationApplyAuthority());

        CharacterCreationBootstrapActivationAttempt attempt = CreateService(
            store,
            sourceResolver,
            queries,
            projector).CreateActivation(CanonicalRequest() with
            {
                BuildMethod = buildMethod,
                SettingsProfileId = settingsProfileId
            });

        Assert.AreEqual(CharacterCreationBootstrapOutcomes.Success, attempt.Outcome);
        Assert.IsNotNull(attempt.Receipt);
        Assert.IsNull(attempt.Bundle);
        Assert.IsTrue(attempt.CreatedRequiresReload);
        CollectionAssert.Contains(
            attempt.Blockers.ToList(),
            CharacterCreationBootstrapBlockers.ActivationProjectionUnavailable);
        Assert.HasCount(1, store.List());

        CharacterWorkspaceId workspaceId = attempt.Receipt.WorkspaceId;
        var lifeModules = new XmlLifeModulesCatalogService(
            Path.Combine(coreRoot, "Chummer", "data", "lifemodules.xml"));
        var applyAuthority = new UnavailableCharacterCreationFoundationApplyAuthority();
        CharacterCreationFoundationResult<CharacterCreationFoundationState> foundation =
            new CharacterCreationFoundationService(
                store,
                queries,
                sourceResolver,
                lifeModules,
                applyAuthority).Load(new(workspaceId));
        var prerequisites = new CharacterCreationPrerequisiteService(
            store,
            queries,
            sourceResolver);
        var attributes = new CharacterCreationAttributesService(store, sourceResolver);
        CharacterCreationFoundationResult<CharacterCreationPrerequisiteState> prerequisite =
            prerequisites.Load(new(workspaceId));
        CharacterCreationFoundationResult<CharacterCreationAttributesState> attribute =
            attributes.Load(new(workspaceId));
        CharacterCreationContactResult<CharacterCreationContactsState> contacts =
            new CharacterCreationContactsService(store).Load(new(workspaceId));
        CharacterCreationFoundationResult<CharacterCreationQualitiesState> qualities =
            new CharacterCreationQualitiesService(
                store,
                sourceResolver,
                prerequisites,
                attributes).Load(new(workspaceId));
        CharacterCreationFoundationResult<CharacterCreationMagicResonanceState> magic =
            new CharacterCreationMagicResonanceService(store, sourceResolver)
                .Load(new(workspaceId));

        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, foundation.Outcome);
        Assert.IsNotNull(foundation.Value);
        Assert.AreEqual(buildMethod, foundation.Value.BuildMethod);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, prerequisite.Outcome);
        Assert.IsNotNull(prerequisite.Value);
        Assert.AreEqual(buildMethod, prerequisite.Value.BuildMethod);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, attribute.Outcome);
        Assert.IsNotNull(attribute.Value);
        Assert.AreEqual(CharacterCreationContactOutcomes.Available, contacts.Outcome);
        Assert.IsNotNull(contacts.Value);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, qualities.Outcome);
        Assert.IsNotNull(qualities.Value);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, magic.Outcome);
        Assert.IsNotNull(magic.Value);
    }

    [TestMethod]
    public void Request_tuple_is_exact_and_invalid_variants_never_mutate_the_store()
    {
        string coreRoot = FindCoreRoot();
        var store = new InMemoryWorkspaceStore();
        CharacterCreationBootstrapService service = CreateService(
            store,
            CreateSourceResolver(coreRoot),
            CreateFileQueries());
        CharacterCreationBootstrapRequest canonical = CanonicalRequest();
        CharacterCreationBootstrapRequest[] hostile =
        [
            canonical with { Schema = "unknown" },
            canonical with { Stage = "complete" },
            canonical with { RulesetId = RulesetDefaults.Sr6 },
            canonical with { BuildMethod = "priority" },
            canonical with { BuildMethod = string.Empty },
            canonical with { SettingsProfileId = Guid.Empty.ToString("D") },
            canonical with { SettingsProfileId = CanonicalPrioritySettingsId.ToUpperInvariant() },
            canonical with { Name = string.Empty },
            canonical with { Alias = string.Empty }
        ];

        foreach (CharacterCreationBootstrapRequest request in hostile)
        {
            CharacterCreationBootstrapResult<CharacterCreationBootstrapReceipt> result =
                service.Create(request);
            Assert.AreEqual(CharacterCreationBootstrapOutcomes.Invalid, result.Outcome);
            Assert.IsNull(result.Value);
            Assert.IsTrue(result.Blockers.Count > 0);
        }

        Assert.HasCount(0, store.List());
    }

    [TestMethod]
    public void Every_noncanonical_builtin_sr5_profile_is_rejected_before_resolution()
    {
        string coreRoot = FindCoreRoot();
        var store = new InMemoryWorkspaceStore();
        CharacterCreationBootstrapService service = CreateService(
            store,
            CreateSourceResolver(coreRoot),
            CreateFileQueries());
        XDocument settings = XDocument.Load(
            Path.Combine(coreRoot, "Chummer", "data", "settings.xml"),
            LoadOptions.None);
        XElement[] noncanonical = settings.Root!
            .Element("settings")!
            .Elements("setting")
            .Where(setting =>
            {
                string method = setting.Element("buildmethod")?.Value.Trim() ?? string.Empty;
                string id = setting.Element("id")?.Value.Trim() ?? string.Empty;
                return CharacterCreationBuildMethods.IsSupported(method)
                       && !CharacterCreationBootstrapProfiles.IsExactCanonicalTuple(method, id);
            })
            .ToArray();
        Assert.IsTrue(noncanonical.Length > 0);

        foreach (XElement setting in noncanonical)
        {
            string method = setting.Element("buildmethod")!.Value.Trim();
            string id = setting.Element("id")!.Value.Trim();
            CharacterCreationBootstrapResult<CharacterCreationBootstrapReceipt> result =
                service.Create(CanonicalRequest() with
                {
                    BuildMethod = method,
                    SettingsProfileId = id
                });
            Assert.AreEqual(
                CharacterCreationBootstrapOutcomes.Invalid,
                result.Outcome,
                $"Noncanonical profile {id} ({method}) was accepted.");
            CollectionAssert.Contains(
                result.Blockers.ToList(),
                CharacterCreationBootstrapBlockers.SettingsProfileInvalid);
        }

        Assert.HasCount(0, store.List());
    }

    [TestMethod]
    public void Every_canonical_method_profile_cross_pair_fails_closed()
    {
        string coreRoot = FindCoreRoot();
        var store = new InMemoryWorkspaceStore();
        CharacterCreationBootstrapService service = CreateService(
            store,
            CreateSourceResolver(coreRoot),
            CreateFileQueries());
        (string Method, string Profile)[] tuples =
        [
            (CharacterCreationBuildMethods.Priority, CanonicalPrioritySettingsId),
            (CharacterCreationBuildMethods.SumToTen, CanonicalSumToTenSettingsId),
            (CharacterCreationBuildMethods.Karma, CanonicalKarmaSettingsId),
            (CharacterCreationBuildMethods.LifeModules, CanonicalLifeModulesSettingsId)
        ];

        foreach ((string Method, string Profile) left in tuples)
        foreach ((string Method, string Profile) right in tuples)
        {
            string method = left.Method;
            string profile = right.Profile;
            if (CharacterCreationBootstrapProfiles.IsExactCanonicalTuple(method, profile))
                continue;

            CharacterCreationBootstrapResult<CharacterCreationBootstrapReceipt> result =
                service.Create(CanonicalRequest() with
                {
                    BuildMethod = method,
                    SettingsProfileId = profile
                });
            Assert.AreEqual(CharacterCreationBootstrapOutcomes.Invalid, result.Outcome);
            CollectionAssert.Contains(
                result.Blockers.ToList(),
                CharacterCreationBootstrapBlockers.SettingsProfileInvalid);
        }

        Assert.HasCount(0, store.List());
    }

    [DataTestMethod]
    [DataRow(CharacterCreationBuildMethods.Priority, CanonicalPrioritySettingsId, true)]
    [DataRow(CharacterCreationBuildMethods.SumToTen, CanonicalSumToTenSettingsId, true)]
    [DataRow(CharacterCreationBuildMethods.Karma, CanonicalKarmaSettingsId, false)]
    [DataRow(CharacterCreationBuildMethods.LifeModules, CanonicalLifeModulesSettingsId, false)]
    public void Canonical_sr5_profiles_bind_only_to_their_exact_build_method(
        string buildMethod,
        string settingsProfileId,
        bool hasPrerequisiteAuthority)
    {
        string coreRoot = FindCoreRoot();
        var store = new InMemoryWorkspaceStore();
        CharacterCreationBootstrapService service = CreateService(
            store,
            CreateSourceResolver(coreRoot),
            CreateFileQueries());
        CharacterCreationBootstrapRequest request = CanonicalRequest() with
        {
            BuildMethod = buildMethod,
            SettingsProfileId = settingsProfileId
        };

        CharacterCreationBootstrapResult<CharacterCreationBootstrapReceipt> result =
            service.Create(request);

        Assert.AreEqual(CharacterCreationBootstrapOutcomes.Success, result.Outcome,
            string.Join(",", result.Blockers));
        Assert.AreEqual(buildMethod, result.Value!.Binding.BuildMethod);
        Assert.AreEqual(settingsProfileId, result.Value.Binding.SettingsProfileId);
        Assert.AreEqual(
            hasPrerequisiteAuthority,
            CharacterCreationPrerequisiteAuthorityDigest.IsCanonical(
                result.Value.Binding.PrerequisiteAuthorityDigest));
    }

    [TestMethod]
    public void Marker_without_atomic_binding_is_an_ordinary_invalid_import()
    {
        string coreRoot = FindCoreRoot();
        var store = new InMemoryWorkspaceStore();
        FileSystemCharacterSourceDataResolver resolver = CreateSourceResolver(coreRoot);
        ICharacterFileQueries queries = CreateFileQueries();
        var codec = new Sr5WorkspaceCodec(
            queries,
            new XmlCharacterSectionQueries(new CharacterSectionService(resolver)),
            new XmlCharacterMetadataCommands(new CharacterFileService()));
        var workspaces = new WorkspaceService(
            store,
            new RulesetWorkspaceCodecResolver([codec]),
            new WorkspaceImportRulesetDetector());
        InvalidOperationException importFailure = Assert.ThrowsExactly<InvalidOperationException>(
            () => workspaces.Import(new WorkspaceImportDocument(
                MinimalMarkerXml(),
                RulesetDefaults.Sr5,
                WorkspaceDocumentFormat.NativeXml)));
        StringAssert.Contains(importFailure.Message, "typed, resolver-bound atomic creation service");
        Assert.HasCount(0, store.List());

        CharacterWorkspaceId id = new("ordinary-marker-import");
        Assert.IsTrue(store.CreateWorkspaceDocument(
            id,
            new WorkspaceDocument(MinimalMarkerXml(), RulesetDefaults.Sr5)).Success);

        var foundation = new CharacterCreationFoundationService(
            store,
            queries,
            resolver,
            new XmlLifeModulesCatalogService(
                Path.Combine(coreRoot, "Chummer", "data", "lifemodules.xml")),
            new UnavailableCharacterCreationFoundationApplyAuthority());
        CharacterCreationFoundationResult<CharacterCreationFoundationState> foundationLoaded =
            foundation.Load(new CharacterCreationFoundationLoadRequest(id));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Invalid, foundationLoaded.Outcome);
        CollectionAssert.Contains(
            foundationLoaded.Blockers.ToList(),
            CharacterCreationFoundationBlockers.CharacterDocumentInvalid);

        var prerequisites = new CharacterCreationPrerequisiteService(store, queries, resolver);
        CharacterCreationFoundationResult<CharacterCreationPrerequisiteState> prerequisiteLoaded =
            prerequisites.Load(new CharacterCreationPrerequisiteLoadRequest(id));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Invalid, prerequisiteLoaded.Outcome);
        CollectionAssert.Contains(
            prerequisiteLoaded.Blockers.ToList(),
            CharacterCreationPrerequisiteBlockers.CharacterDocumentInvalid);
    }

    [TestMethod]
    public void Marker_cardinality_selection_and_binding_tamper_fail_closed()
    {
        string coreRoot = FindCoreRoot();
        var store = new InMemoryWorkspaceStore();
        FileSystemCharacterSourceDataResolver resolver = CreateSourceResolver(coreRoot);
        CharacterCreationBootstrapResult<CharacterCreationBootstrapReceipt> created =
            CreateService(store, resolver, CreateFileQueries()).Create(CanonicalRequest());
        CharacterCreationBootstrapReceipt receipt = created.Value!;
        WorkspaceStoredDocument workspace = store.Get(receipt.WorkspaceId).Value!;
        XDocument canonical = XDocument.Parse(workspace.Document.Content);

        XDocument duplicate = XDocument.Parse(workspace.Document.Content);
        duplicate.Root!.Add(new XElement(
            duplicate.Root.Element(CharacterCreationBootstrapXml.MarkerElement)!));
        AssertPrepareBlocked(
            receipt.WorkspaceId,
            workspace.Document with
            {
                State = workspace.Document.State with
                {
                    Payload = duplicate.ToString(SaveOptions.DisableFormatting),
                    AuxiliaryState = WorkspaceDocumentAuxiliaryState.Empty
                }
            },
            resolver,
            CharacterCreationBootstrapBlockers.MarkerDuplicate);

        XDocument wrongMarker = XDocument.Parse(workspace.Document.Content);
        wrongMarker.Root!.Element(CharacterCreationBootstrapXml.MarkerElement)!
            .Element(CharacterCreationBootstrapXml.SchemaElement)!.Value = "unknown-marker";
        AssertPrepareBlocked(
            receipt.WorkspaceId,
            WithoutBinding(workspace.Document, wrongMarker),
            resolver,
            CharacterCreationBootstrapBlockers.MarkerInvalid);

        XDocument duplicateMarkerField = XDocument.Parse(workspace.Document.Content);
        duplicateMarkerField.Root!.Element(CharacterCreationBootstrapXml.MarkerElement)!.Add(
            new XElement(
                CharacterCreationBootstrapXml.SchemaElement,
                CharacterCreationBootstrapSchemas.MarkerV1));
        AssertPrepareBlocked(
            receipt.WorkspaceId,
            WithoutBinding(workspace.Document, duplicateMarkerField),
            resolver,
            CharacterCreationBootstrapBlockers.MarkerInvalid);

        XDocument selected = XDocument.Parse(workspace.Document.Content);
        selected.Root!.AddFirst(new XElement("metatype", "Human"));
        AssertPrepareBlocked(
            receipt.WorkspaceId,
            workspace.Document with
            {
                State = workspace.Document.State with
                {
                    Payload = selected.ToString(SaveOptions.DisableFormatting),
                    AuxiliaryState = WorkspaceDocumentAuxiliaryState.Empty
                }
            },
            resolver,
            CharacterCreationBootstrapBlockers.MetatypeAlreadySelected);

        XDocument emptySelection = XDocument.Parse(workspace.Document.Content);
        emptySelection.Root!.AddFirst(new XElement("metatype"));
        Assert.IsTrue(CharacterCreationBootstrapAuthority.TryPrepareBinding(
            receipt.WorkspaceId,
            WithoutBinding(workspace.Document, emptySelection),
            resolver,
            out CharacterCreationBootstrapBinding emptyMetatypeBinding,
            out _,
            out IReadOnlyList<string> emptyMetatypeBlockers),
            string.Join(",", emptyMetatypeBlockers));
        Assert.IsTrue(CharacterCreationBootstrapBindingDigest.IsValid(emptyMetatypeBinding));

        XDocument createdCharacter = XDocument.Parse(workspace.Document.Content);
        createdCharacter.Root!.Element("created")!.Value = "True";
        AssertPrepareBlocked(
            receipt.WorkspaceId,
            WithoutBinding(workspace.Document, createdCharacter),
            resolver,
            CharacterCreationBootstrapBlockers.CharacterAlreadyCreated);

        XDocument missingBuild = XDocument.Parse(workspace.Document.Content);
        missingBuild.Root!.Element("buildmethod")!.Remove();
        AssertPrepareBlocked(
            receipt.WorkspaceId,
            WithoutBinding(workspace.Document, missingBuild),
            resolver,
            CharacterCreationBootstrapBlockers.CharacterDocumentInvalid);

        XDocument duplicateSettings = XDocument.Parse(workspace.Document.Content);
        duplicateSettings.Root!.Add(new XElement("settings", CanonicalPrioritySettingsId));
        AssertPrepareBlocked(
            receipt.WorkspaceId,
            WithoutBinding(workspace.Document, duplicateSettings),
            resolver,
            CharacterCreationBootstrapBlockers.CharacterDocumentInvalid);

        XDocument noncanonicalProfile = XDocument.Parse(workspace.Document.Content);
        noncanonicalProfile.Root!.Element("settings")!.Value =
            "507eef8e-eba8-41ea-84c4-4282258fe669";
        AssertPrepareBlocked(
            receipt.WorkspaceId,
            WithoutBinding(workspace.Document, noncanonicalProfile),
            resolver,
            CharacterCreationBootstrapBlockers.SettingsProfileInvalid);

        CharacterCreationBootstrapBinding badStage = receipt.Binding with
        {
            Stage = "completed",
            BindingDigest = string.Empty
        };
        badStage = badStage with
        {
            BindingDigest = CharacterCreationBootstrapBindingDigest.Compute(badStage)
        };
        WorkspaceDocument badBindingDocument = workspace.Document with
        {
            State = workspace.Document.State with
            {
                AuxiliaryState = new WorkspaceDocumentAuxiliaryState(
                    CharacterCreationBootstrapBinding: badStage)
            }
        };
        var badBindingWorkspace = workspace with { Document = badBindingDocument };
        Assert.IsFalse(CharacterCreationBootstrapAuthority.TryValidatePending(
            badBindingWorkspace,
            resolver,
            out IReadOnlyList<string> bindingBlockers));
        CollectionAssert.Contains(
            bindingBlockers.ToList(),
            CharacterCreationBootstrapBlockers.BindingInvalid);
        Assert.IsTrue(canonical.Root!.Element("metatype") is null);
    }

    [TestMethod]
    public void Revision_and_complete_anchor_tamper_fail_even_after_structural_redigest()
    {
        string coreRoot = FindCoreRoot();
        var store = new InMemoryWorkspaceStore();
        FileSystemCharacterSourceDataResolver resolver = CreateSourceResolver(coreRoot);
        CharacterCreationBootstrapReceipt receipt = CreateService(
                store,
                resolver,
                CreateFileQueries())
            .Create(CanonicalRequest())
            .Value!;
        WorkspaceStoredDocument workspace = store.Get(receipt.WorkspaceId).Value!;
        Assert.IsTrue(CharacterCreationBootstrapReceiptDigest.IsValid(receipt));

        CharacterCreationBootstrapReceipt receiptRevisionTamper = ResignReceipt(
            receipt with { ContentRevision = receipt.ContentRevision + 1 });
        Assert.IsFalse(CharacterCreationBootstrapReceiptDigest.IsValid(receiptRevisionTamper));

        CharacterCreationBootstrapBinding bindingRevisionTamper = ResignBinding(
            receipt.Binding with
            {
                InitialContentRevision =
                    CharacterCreationBootstrapRevisions.InitialContentRevision + 1
            });
        CharacterCreationBootstrapReceipt fullyRedigestedRevisionTamper = ResignReceipt(
            receipt with
            {
                ContentRevision = bindingRevisionTamper.InitialContentRevision,
                Binding = bindingRevisionTamper
            });
        Assert.IsFalse(CharacterCreationBootstrapBindingDigest.IsValid(bindingRevisionTamper));
        Assert.IsFalse(CharacterCreationBootstrapReceiptDigest.IsValid(
            fullyRedigestedRevisionTamper));
        Assert.IsFalse(CharacterCreationBootstrapStoreIntegrity.IsValidBinding(
            receipt.WorkspaceId,
            bindingRevisionTamper));
        WorkspaceDocument revisionTamperDocument = workspace.Document with
        {
            State = workspace.Document.State with
            {
                AuxiliaryState = new WorkspaceDocumentAuxiliaryState(
                    CharacterCreationBootstrapBinding: bindingRevisionTamper)
            }
        };
        Assert.IsFalse(CharacterCreationBootstrapStoreIntegrity.IsValidInitialState(
            receipt.WorkspaceId,
            revisionTamperDocument));
        var hostileRevisionStore = new InMemoryWorkspaceStore();
        Assert.IsFalse(((ICharacterCreationBootstrapAtomicCreateCapability)hostileRevisionStore)
            .CreateCharacterCreationBootstrapWorkspaceDocument(
                receipt.WorkspaceId,
                revisionTamperDocument)
            .Success);

        CharacterCreationBootstrapBinding savedRevisionTamper = ResignBinding(
            receipt.Binding with
            {
                InitialSavedRevision =
                    CharacterCreationBootstrapRevisions.InitialSavedRevision + 1
            });
        CharacterCreationBootstrapReceipt fullyRedigestedSavedRevisionTamper = ResignReceipt(
            receipt with
            {
                SavedRevision = savedRevisionTamper.InitialSavedRevision,
                Binding = savedRevisionTamper
            });
        Assert.IsFalse(CharacterCreationBootstrapBindingDigest.IsValid(savedRevisionTamper));
        Assert.IsFalse(CharacterCreationBootstrapReceiptDigest.IsValid(
            fullyRedigestedSavedRevisionTamper));

        CharacterCreationBootstrapBinding crossPairBinding = ResignBinding(
            receipt.Binding with { BuildMethod = CharacterCreationBuildMethods.SumToTen });
        WorkspaceDocument crossPairDocument = workspace.Document with
        {
            State = workspace.Document.State with
            {
                AuxiliaryState = new WorkspaceDocumentAuxiliaryState(
                    CharacterCreationBootstrapBinding: crossPairBinding)
            }
        };
        Assert.IsFalse(CharacterCreationBootstrapBindingDigest.IsValid(crossPairBinding));
        Assert.IsFalse(CharacterCreationBootstrapStoreIntegrity.IsValidInitialState(
            receipt.WorkspaceId,
            crossPairDocument));

        string[] missingAnchorSet = receipt.Binding.SourceAnchorIds
            .Where(anchor => !string.Equals(anchor, "skills.xml", StringComparison.Ordinal))
            .ToArray();
        CharacterCreationBootstrapBinding missingAnchorBinding = ResignBinding(
            receipt.Binding with { SourceAnchorIds = missingAnchorSet });
        CharacterCreationBootstrapReceipt fullyRedigestedMissingAnchor = ResignReceipt(
            receipt with
            {
                Binding = missingAnchorBinding,
                SourceAnchorIds = missingAnchorSet
            });
        Assert.IsFalse(CharacterCreationBootstrapBindingDigest.IsValid(missingAnchorBinding));
        Assert.IsFalse(CharacterCreationBootstrapReceiptDigest.IsValid(
            fullyRedigestedMissingAnchor));

        string[] extraAnchorSet = receipt.Binding.SourceAnchorIds
            .Append("unknown.xml")
            .OrderBy(anchor => anchor, StringComparer.Ordinal)
            .ToArray();
        CharacterCreationBootstrapBinding extraAnchorBinding = ResignBinding(
            receipt.Binding with { SourceAnchorIds = extraAnchorSet });
        CharacterCreationBootstrapReceipt fullyRedigestedExtraAnchor = ResignReceipt(
            receipt with
            {
                Binding = extraAnchorBinding,
                SourceAnchorIds = extraAnchorSet
            });
        Assert.IsFalse(CharacterCreationBootstrapBindingDigest.IsValid(extraAnchorBinding));
        Assert.IsFalse(CharacterCreationBootstrapReceiptDigest.IsValid(
            fullyRedigestedExtraAnchor));

        string[] reorderedAnchorSet = receipt.Binding.SourceAnchorIds.Reverse().ToArray();
        CharacterCreationBootstrapBinding reorderedAnchorBinding = ResignBinding(
            receipt.Binding with { SourceAnchorIds = reorderedAnchorSet });
        CharacterCreationBootstrapReceipt fullyRedigestedReorderedAnchors = ResignReceipt(
            receipt with
            {
                Binding = reorderedAnchorBinding,
                SourceAnchorIds = reorderedAnchorSet
            });
        Assert.IsFalse(CharacterCreationBootstrapBindingDigest.IsValid(reorderedAnchorBinding));
        Assert.IsFalse(CharacterCreationBootstrapReceiptDigest.IsValid(
            fullyRedigestedReorderedAnchors));

        WorkspaceDocument extraAnchorDocument = workspace.Document with
        {
            State = workspace.Document.State with
            {
                AuxiliaryState = new WorkspaceDocumentAuxiliaryState(
                    CharacterCreationBootstrapBinding: extraAnchorBinding)
            }
        };
        Assert.IsFalse(CharacterCreationBootstrapStoreIntegrity.IsValidInitialState(
            receipt.WorkspaceId,
            extraAnchorDocument));
        var hostileAnchorStore = new InMemoryWorkspaceStore();
        Assert.IsFalse(((ICharacterCreationBootstrapAtomicCreateCapability)hostileAnchorStore)
            .CreateCharacterCreationBootstrapWorkspaceDocument(
                receipt.WorkspaceId,
                extraAnchorDocument)
            .Success);
        Assert.IsFalse(CharacterCreationBootstrapAuthority.TryValidatePending(
            workspace with { Document = extraAnchorDocument },
            resolver,
            out IReadOnlyList<string> blockers));
        CollectionAssert.Contains(
            blockers.ToList(),
            CharacterCreationBootstrapBlockers.BindingInvalid);
    }

    [TestMethod]
    public void Post_selection_stale_marker_is_rejected_and_generic_auxiliary_cas_cannot_clear_it()
    {
        string coreRoot = FindCoreRoot();
        string workspaceRoot = Path.Combine(
            Path.GetTempPath(),
            $"chummer-bootstrap-lifecycle-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workspaceRoot);
        try
        {
            var store = new FileWorkspaceStore(workspaceRoot);
            FileSystemCharacterSourceDataResolver resolver = CreateSourceResolver(coreRoot);
            CharacterCreationBootstrapResult<CharacterCreationBootstrapReceipt> created =
                CreateService(store, resolver, CreateFileQueries()).Create(CanonicalRequest());
            Assert.AreEqual(CharacterCreationBootstrapOutcomes.Success, created.Outcome,
                string.Join(",", created.Blockers));
            CharacterCreationBootstrapReceipt receipt = created.Value!;
            WorkspaceStoredDocument workspace = store.Get(receipt.WorkspaceId).Value!;

            CharacterWorkspaceId forgedId = new("ordinary-bootstrap-forgery");
            WorkspaceDocument ordinaryCopy = workspace.Document with
            {
                State = workspace.Document.State with
                {
                    AuxiliaryState = WorkspaceDocumentAuxiliaryState.Empty
                }
            };
            WorkspaceStoreMutationResult ordinaryCreated = store.CreateWorkspaceDocument(
                forgedId,
                ordinaryCopy);
            Assert.IsTrue(ordinaryCreated.Success);
            CharacterCreationBootstrapBinding forgedUnsigned = receipt.Binding with
            {
                WorkspaceId = forgedId,
                BindingDigest = string.Empty
            };
            CharacterCreationBootstrapBinding forgedBinding = forgedUnsigned with
            {
                BindingDigest = CharacterCreationBootstrapBindingDigest.Compute(forgedUnsigned)
            };
            WorkspaceDocument forgedReplacement = ordinaryCopy with
            {
                State = ordinaryCopy.State with
                {
                    AuxiliaryState = new WorkspaceDocumentAuxiliaryState(
                        CharacterCreationBootstrapBinding: forgedBinding)
                }
            };
            WorkspaceStoreMutationResult forged =
                ((IWorkspaceAuxiliaryStateAtomicCommitCapability)store)
                .ReplaceWorkspaceDocumentAndAuxiliaryStateAndCheckpoint(
                    forgedId,
                    ordinaryCreated.Entry!.Value.ContentRevision,
                    ordinaryCopy.AuxiliaryStateDigest,
                    forgedReplacement);
            Assert.IsFalse(forged.Success,
                "An ordinary auxiliary-state CAS cannot forge a bootstrap binding.");
            Assert.IsNull(store.Get(forgedId).Value!.Document.AuxiliaryState
                .CharacterCreationBootstrapBinding);

            CharacterWorkspaceId genericBoundId = new("generic-bound-bootstrap");
            Assert.IsFalse(store.CreateWorkspaceDocument(
                genericBoundId,
                workspace.Document).Success,
                "Generic creation cannot persist bootstrap auxiliary authority.");

            XDocument selected = XDocument.Parse(workspace.Document.Content);
            selected.Root!.AddFirst(new XElement("metatype", "Human"));
            WorkspaceDocument stale = workspace.Document with
            {
                State = workspace.Document.State with
                {
                    Payload = selected.ToString(SaveOptions.DisableFormatting)
                }
            };
            Assert.IsFalse(CharacterCreationBootstrapAuthority.TryValidatePending(
                workspace with { Document = stale },
                resolver,
                out IReadOnlyList<string> staleBlockers));
            Assert.IsTrue(staleBlockers.Count > 0);

            selected.Root!.Element(CharacterCreationBootstrapXml.MarkerElement)!.Remove();
            WorkspaceDocument genericCompletion = workspace.Document with
            {
                State = workspace.Document.State with
                {
                    Payload = selected.ToString(SaveOptions.DisableFormatting),
                    AuxiliaryState = WorkspaceDocumentAuxiliaryState.Empty
                }
            };
            WorkspaceStoreMutationResult attempted =
                ((IWorkspaceAuxiliaryStateAtomicCommitCapability)store)
                .ReplaceWorkspaceDocumentAndAuxiliaryStateAndCheckpoint(
                    receipt.WorkspaceId,
                    workspace.ContentRevision,
                    workspace.Document.AuxiliaryStateDigest,
                    genericCompletion);
            Assert.IsFalse(attempted.Success,
                "Only a future resolver-bound finalization authority may clear the pending marker and binding.");
            Assert.IsNotNull(store.Get(receipt.WorkspaceId).Value!.Document.AuxiliaryState
                .CharacterCreationBootstrapBinding);
        }
        finally
        {
            if (Directory.Exists(workspaceRoot))
                Directory.Delete(workspaceRoot, recursive: true);
        }
    }

    private static CharacterCreationBootstrapService CreateService(
        IWorkspaceStore store,
        ICharacterSourceDataResolver sourceResolver,
        ICharacterFileQueries queries,
        ICharacterCreationBootstrapActivationProjector? activationProjector = null)
    {
        var codec = new Sr5WorkspaceCodec(
            queries,
            new XmlCharacterSectionQueries(new CharacterSectionService(sourceResolver)),
            new XmlCharacterMetadataCommands(new CharacterFileService()));
        return new CharacterCreationBootstrapService(
            store,
            new RulesetWorkspaceCodecResolver([codec]),
            queries,
            sourceResolver,
            activationProjector);
    }

    private static void AssertJsonEqual<T>(T expected, T actual)
        => Assert.AreEqual(
            JsonSerializer.Serialize(expected),
            JsonSerializer.Serialize(actual));

    private static CharacterCreationBootstrapRequest CanonicalRequest()
        => new(
            CharacterCreationBootstrapSchemas.RequestV1,
            CharacterCreationBootstrapStages.AwaitingFoundationSelection,
            RulesetDefaults.Sr5,
            "Pending Runner",
            "No Default",
            CharacterCreationBuildMethods.Priority,
            CanonicalPrioritySettingsId);

    private static ICharacterFileQueries CreateFileQueries()
        => new XmlCharacterFileQueries(new CharacterFileService());

    private static FileSystemCharacterSourceDataResolver CreateSourceResolver(string coreRoot)
        => new(new FileSystemContentOverlayCatalogService(coreRoot, coreRoot, null));

    private static void AssertPrepareBlocked(
        CharacterWorkspaceId workspaceId,
        WorkspaceDocument document,
        ICharacterSourceDataResolver resolver,
        string expectedBlocker)
    {
        Assert.IsFalse(CharacterCreationBootstrapAuthority.TryPrepareBinding(
            workspaceId,
            document,
            resolver,
            out _,
            out _,
            out IReadOnlyList<string> blockers));
        CollectionAssert.Contains(blockers.ToList(), expectedBlocker);
    }

    private static WorkspaceDocument WithoutBinding(
        WorkspaceDocument template,
        XDocument character)
        => template with
        {
            State = template.State with
            {
                Payload = character.ToString(SaveOptions.DisableFormatting),
                AuxiliaryState = WorkspaceDocumentAuxiliaryState.Empty
            }
        };

    private static CharacterCreationBootstrapBinding ResignBinding(
        CharacterCreationBootstrapBinding binding)
    {
        CharacterCreationBootstrapBinding unsigned = binding with { BindingDigest = string.Empty };
        return unsigned with
        {
            BindingDigest = CharacterCreationBootstrapBindingDigest.Compute(unsigned)
        };
    }

    private static CharacterCreationBootstrapReceipt ResignReceipt(
        CharacterCreationBootstrapReceipt receipt)
    {
        CharacterCreationBootstrapReceipt unsigned = receipt with { ReceiptDigest = string.Empty };
        return unsigned with
        {
            ReceiptDigest = CharacterCreationBootstrapReceiptDigest.Compute(unsigned)
        };
    }

    private static CharacterCreationBootstrapActivationBundle ResignActivation(
        CharacterCreationBootstrapActivationBundle activation)
    {
        CharacterCreationBootstrapActivationBundle unsigned = activation with
        {
            BundleDigest = string.Empty
        };
        return unsigned with
        {
            BundleDigest = CharacterCreationBootstrapActivationIntegrity.ComputeBundleDigest(
                unsigned)
        };
    }

    private static string MinimalMarkerXml()
        => $"""
           <character>
             <name>Pending Runner</name>
             <alias>No Default</alias>
             <buildmethod>{CharacterCreationBuildMethods.Priority}</buildmethod>
             <createdversion>5.225.0</createdversion>
             <appversion>5.225.0</appversion>
             <karma>0</karma>
             <nuyen>0</nuyen>
             <created>False</created>
             <gameedition>SR5</gameedition>
             <settings>{CanonicalPrioritySettingsId}</settings>
             <{CharacterCreationBootstrapXml.MarkerElement}>
               <{CharacterCreationBootstrapXml.SchemaElement}>{CharacterCreationBootstrapSchemas.MarkerV1}</{CharacterCreationBootstrapXml.SchemaElement}>
               <{CharacterCreationBootstrapXml.StageElement}>{CharacterCreationBootstrapStages.AwaitingFoundationSelection}</{CharacterCreationBootstrapXml.StageElement}>
             </{CharacterCreationBootstrapXml.MarkerElement}>
           </character>
           """;

    private static string FindCoreRoot()
    {
        DirectoryInfo? current = new(AppDomain.CurrentDomain.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Chummer", "data", "settings.xml")))
                return current.FullName;
            current = current.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate canonical Chummer/data/settings.xml.");
    }

    private sealed class CountingSourceDataResolver : ICharacterSourceDataResolver
    {
        private readonly ICharacterSourceDataResolver _inner;

        public CountingSourceDataResolver(ICharacterSourceDataResolver inner)
        {
            _inner = inner;
        }

        public int ContextCreateCount { get; private set; }

        public int SourceProfileResolveCount { get; private set; }

        public int MetatypeResolveCount { get; private set; }

        public int PrerequisiteResolveCount { get; private set; }

        public int QualitiesResolveCount { get; private set; }

        public int MagicResolveCount { get; private set; }

        public ICharacterSourceDataContext? TryCreateContext(string characterXml)
        {
            ContextCreateCount++;
            ICharacterSourceDataContext? context = _inner.TryCreateContext(characterXml);
            return context is null ? null : new CountingSourceDataContext(this, context);
        }

        private sealed class CountingSourceDataContext : ICharacterSourceDataContext
        {
            private readonly CountingSourceDataResolver _owner;
            private readonly ICharacterSourceDataContext _inner;

            public CountingSourceDataContext(
                CountingSourceDataResolver owner,
                ICharacterSourceDataContext inner)
            {
                _owner = owner;
                _inner = inner;
            }

            public bool TryResolveCreationSourceProfile(
                out CharacterCreationSourceProfileAuthority authority)
            {
                _owner.SourceProfileResolveCount++;
                return _inner.TryResolveCreationSourceProfile(out authority);
            }

            public bool TryResolveCreationMetatypeCatalog(
                out CharacterCreationMetatypeCatalogAuthority authority)
            {
                _owner.MetatypeResolveCount++;
                return _inner.TryResolveCreationMetatypeCatalog(out authority);
            }

            public bool TryResolveCreationPrerequisiteAuthority(
                out CharacterCreationPrerequisiteAuthority authority)
            {
                _owner.PrerequisiteResolveCount++;
                return _inner.TryResolveCreationPrerequisiteAuthority(out authority);
            }

            public bool TryResolveCreationQualitiesAuthority(
                out CharacterCreationQualitiesAuthority authority)
            {
                _owner.QualitiesResolveCount++;
                return _inner.TryResolveCreationQualitiesAuthority(out authority);
            }

            public bool TryResolveCreationMagicResonanceAuthority(
                out CharacterCreationMagicResonanceAuthority authority)
            {
                _owner.MagicResolveCount++;
                return _inner.TryResolveCreationMagicResonanceAuthority(out authority);
            }

            public bool TryResolveCyberwareGradeDeviceRating(
                string gradeName,
                string improvementSource,
                out int deviceRating)
                => _inner.TryResolveCyberwareGradeDeviceRating(
                    gradeName,
                    improvementSource,
                    out deviceRating);

            public bool TryResolveVehicleModBonuses(
                string sourceId,
                string name,
                out CharacterVehicleModSourceBonuses bonuses)
                => _inner.TryResolveVehicleModBonuses(sourceId, name, out bonuses);
        }
    }

    private sealed class DriftingSourceDataResolver : ICharacterSourceDataResolver
    {
        private readonly ICharacterSourceDataResolver _inner;

        public DriftingSourceDataResolver(ICharacterSourceDataResolver inner)
        {
            _inner = inner;
        }

        public int ContextCreateCount { get; private set; }

        public ICharacterSourceDataContext? TryCreateContext(string characterXml)
        {
            ContextCreateCount++;
            ICharacterSourceDataContext? context = _inner.TryCreateContext(characterXml);
            return context is null ? null : new DriftingSourceDataContext(context);
        }
    }

    private sealed class ConsumerDriftSourceDataResolver : ICharacterSourceDataResolver
    {
        private readonly ICharacterSourceDataResolver _inner;
        private readonly string _domain;

        public ConsumerDriftSourceDataResolver(
            ICharacterSourceDataResolver inner,
            string domain)
        {
            _inner = inner;
            _domain = domain;
        }

        public int ContextCreateCount { get; private set; }

        public ICharacterSourceDataContext? TryCreateContext(string characterXml)
        {
            ContextCreateCount++;
            ICharacterSourceDataContext? context = _inner.TryCreateContext(characterXml);
            return context is null || ContextCreateCount == 1
                ? context
                : new ConsumerDriftSourceDataContext(context, _domain);
        }
    }

    private sealed class ConsumerDriftSourceDataContext : ICharacterSourceDataContext
    {
        private readonly ICharacterSourceDataContext _inner;
        private readonly string _domain;
        private static readonly string DriftDigest = "sha256:" + new string('0', 64);

        public ConsumerDriftSourceDataContext(
            ICharacterSourceDataContext inner,
            string domain)
        {
            _inner = inner;
            _domain = domain;
        }

        public bool TryResolveCreationSourceProfile(
            out CharacterCreationSourceProfileAuthority authority)
        {
            bool resolved = _inner.TryResolveCreationSourceProfile(out authority);
            if (resolved && _domain == "settings")
                authority = authority with { RawProfileInputsDigest = DriftDigest };
            return resolved;
        }

        public bool TryResolveCreationMetatypeCatalog(
            out CharacterCreationMetatypeCatalogAuthority authority)
        {
            bool resolved = _inner.TryResolveCreationMetatypeCatalog(out authority);
            if (resolved && _domain == "metatypes")
            {
                authority = authority with
                {
                    SourceContext = authority.SourceContext with
                    {
                        RawMetatypesXmlDigest = DriftDigest
                    }
                };
            }
            return resolved;
        }

        public bool TryResolveCreationPrerequisiteAuthority(
            out CharacterCreationPrerequisiteAuthority authority)
        {
            bool resolved = _inner.TryResolveCreationPrerequisiteAuthority(out authority);
            if (resolved && _domain is "priorities" or "skills")
            {
                authority = _domain == "priorities"
                    ? authority with { RawPrioritiesXmlDigest = DriftDigest }
                    : authority with { RawSkillsXmlDigest = DriftDigest };
            }
            return resolved;
        }

        public bool TryResolveCreationQualitiesAuthority(
            out CharacterCreationQualitiesAuthority authority)
        {
            bool resolved = _inner.TryResolveCreationQualitiesAuthority(out authority);
            if (resolved && _domain == "qualities")
                authority = authority with { SourceDigest = DriftDigest };
            return resolved;
        }

        public bool TryResolveCreationMagicResonanceAuthority(
            out CharacterCreationMagicResonanceAuthority authority)
        {
            bool resolved = _inner.TryResolveCreationMagicResonanceAuthority(out authority);
            if (resolved && _domain is "traditions" or "streams" or "powers" or "spells" or "complexforms")
                authority = authority with { SourceInputsDigest = DriftDigest };
            return resolved;
        }

        public bool TryResolveCyberwareGradeDeviceRating(
            string gradeName,
            string improvementSource,
            out int deviceRating)
            => _inner.TryResolveCyberwareGradeDeviceRating(
                gradeName,
                improvementSource,
                out deviceRating);

        public bool TryResolveVehicleModBonuses(
            string sourceId,
            string name,
            out CharacterVehicleModSourceBonuses bonuses)
            => _inner.TryResolveVehicleModBonuses(sourceId, name, out bonuses);
    }

    private sealed class DriftingLifeModulesCatalogService : ILifeModulesCatalogService
    {
        private readonly ILifeModulesCatalogService _inner;

        public DriftingLifeModulesCatalogService(ILifeModulesCatalogService inner)
        {
            _inner = inner;
        }

        public bool Drifted { get; set; }

        public LifeModuleCatalogAuthorityDto GetAuthority()
        {
            LifeModuleCatalogAuthorityDto authority = _inner.GetAuthority();
            return Drifted
                ? authority with { RawXmlDigest = "sha256:" + new string('0', 64) }
                : authority;
        }

        public IReadOnlyList<LifeModuleStageDto> GetStages() => _inner.GetStages();

        public IReadOnlyList<LifeModuleSummaryDto> GetModules(string? stage = null)
            => _inner.GetModules(stage);

        public IReadOnlyList<LifeModuleLegalOptionDto> GetOptionProjections(
            string? stage = null,
            IReadOnlyCollection<string>? enabledSources = null)
            => _inner.GetOptionProjections(stage, enabledSources);
    }

    private sealed class CountingLifeModulesCatalogService : ILifeModulesCatalogService
    {
        private readonly ILifeModulesCatalogService _inner;

        public CountingLifeModulesCatalogService(ILifeModulesCatalogService inner)
        {
            _inner = inner;
        }

        public int AuthorityReadCount { get; private set; }

        public int OptionProjectionCount { get; private set; }

        public LifeModuleCatalogAuthorityDto GetAuthority()
        {
            AuthorityReadCount++;
            return _inner.GetAuthority();
        }

        public IReadOnlyList<LifeModuleStageDto> GetStages() => _inner.GetStages();

        public IReadOnlyList<LifeModuleSummaryDto> GetModules(string? stage = null)
            => _inner.GetModules(stage);

        public IReadOnlyList<LifeModuleLegalOptionDto> GetOptionProjections(
            string? stage = null,
            IReadOnlyCollection<string>? enabledSources = null)
        {
            OptionProjectionCount++;
            return _inner.GetOptionProjections(stage, enabledSources);
        }
    }

    private sealed class DriftingSourceDataContext : ICharacterSourceDataContext
    {
        private readonly ICharacterSourceDataContext _inner;

        public DriftingSourceDataContext(ICharacterSourceDataContext inner)
        {
            _inner = inner;
        }

        public bool Drifted { get; set; }

        public bool TryResolveCreationSourceProfile(
            out CharacterCreationSourceProfileAuthority authority)
        {
            bool resolved = _inner.TryResolveCreationSourceProfile(out authority);
            if (resolved && Drifted)
            {
                authority = authority with
                {
                    RawProfileInputsDigest = "sha256:" + new string('0', 64)
                };
            }
            return resolved;
        }

        public bool TryResolveCreationMetatypeCatalog(
            out CharacterCreationMetatypeCatalogAuthority authority)
            => _inner.TryResolveCreationMetatypeCatalog(out authority);

        public bool TryResolveCreationPrerequisiteAuthority(
            out CharacterCreationPrerequisiteAuthority authority)
            => _inner.TryResolveCreationPrerequisiteAuthority(out authority);

        public bool TryResolveCreationQualitiesAuthority(
            out CharacterCreationQualitiesAuthority authority)
            => _inner.TryResolveCreationQualitiesAuthority(out authority);

        public bool TryResolveCreationMagicResonanceAuthority(
            out CharacterCreationMagicResonanceAuthority authority)
            => _inner.TryResolveCreationMagicResonanceAuthority(out authority);

        public bool TryResolveCyberwareGradeDeviceRating(
            string gradeName,
            string improvementSource,
            out int deviceRating)
            => _inner.TryResolveCyberwareGradeDeviceRating(
                gradeName,
                improvementSource,
                out deviceRating);

        public bool TryResolveVehicleModBonuses(
            string sourceId,
            string name,
            out CharacterVehicleModSourceBonuses bonuses)
            => _inner.TryResolveVehicleModBonuses(sourceId, name, out bonuses);
    }

    private sealed class SourceDriftingProjector : ICharacterCreationBootstrapActivationProjector
    {
        private readonly ICharacterCreationBootstrapActivationProjector _inner;

        public SourceDriftingProjector(ICharacterCreationBootstrapActivationProjector inner)
        {
            _inner = inner;
        }

        public CharacterCreationInitialProjection Project(
            WorkspaceStoredDocument workspace,
            CharacterCreationBootstrapSourceSnapshot sourceSnapshot)
        {
            return _inner.Project(workspace, sourceSnapshot);
        }

        public bool IsCurrent(
            CharacterCreationInitialProjection projection,
            ICharacterSourceDataContext sourceContext,
            string characterXml)
        {
            ((DriftingSourceDataContext)sourceContext).Drifted = true;
            return _inner.IsCurrent(projection, sourceContext, characterXml);
        }
    }

    private sealed class CountingWorkspaceStore :
        IWorkspaceStore,
        ICharacterCreationBootstrapAtomicCreateCapability
    {
        private readonly InMemoryWorkspaceStore _inner = new();

        public int ReadCount { get; private set; }

        public bool SupportsCharacterCreationBootstrapAtomicCreate => true;

        public WorkspaceStoreMutationResult CreateCharacterCreationBootstrapWorkspaceDocument(
            CharacterWorkspaceId id,
            WorkspaceDocument document)
            => _inner.CreateCharacterCreationBootstrapWorkspaceDocument(id, document);

        public WorkspaceStoreMutationResult CreateWorkspaceDocument(WorkspaceDocument document)
            => _inner.CreateWorkspaceDocument(document);

        public WorkspaceStoreMutationResult CreateWorkspaceDocument(
            Chummer.Contracts.Owners.OwnerScope owner,
            WorkspaceDocument document)
            => _inner.CreateWorkspaceDocument(owner, document);

        public WorkspaceStoreMutationResult CreateWorkspaceDocument(
            CharacterWorkspaceId id,
            WorkspaceDocument document)
            => _inner.CreateWorkspaceDocument(id, document);

        public WorkspaceStoreMutationResult CreateWorkspaceDocument(
            Chummer.Contracts.Owners.OwnerScope owner,
            CharacterWorkspaceId id,
            WorkspaceDocument document)
            => _inner.CreateWorkspaceDocument(owner, id, document);

        public IReadOnlyList<WorkspaceStoreEntry> List() => _inner.List();

        public IReadOnlyList<WorkspaceStoreEntry> List(
            Chummer.Contracts.Owners.OwnerScope owner) => _inner.List(owner);

        public WorkspaceStoreReadResult Get(CharacterWorkspaceId id)
        {
            ReadCount++;
            return _inner.Get(id);
        }

        public WorkspaceStoreReadResult Get(
            Chummer.Contracts.Owners.OwnerScope owner,
            CharacterWorkspaceId id)
        {
            ReadCount++;
            return _inner.Get(owner, id);
        }

        public WorkspaceStoreMutationResult ReplaceWorkspaceDocument(
            CharacterWorkspaceId id,
            long expectedContentRevision,
            WorkspaceDocument document)
            => _inner.ReplaceWorkspaceDocument(id, expectedContentRevision, document);

        public WorkspaceStoreMutationResult ReplaceWorkspaceDocument(
            Chummer.Contracts.Owners.OwnerScope owner,
            CharacterWorkspaceId id,
            long expectedContentRevision,
            WorkspaceDocument document)
            => _inner.ReplaceWorkspaceDocument(owner, id, expectedContentRevision, document);

        public WorkspaceStoreMutationResult SaveCheckpoint(
            CharacterWorkspaceId id,
            long expectedContentRevision)
            => _inner.SaveCheckpoint(id, expectedContentRevision);

        public WorkspaceStoreMutationResult SaveCheckpoint(
            Chummer.Contracts.Owners.OwnerScope owner,
            CharacterWorkspaceId id,
            long expectedContentRevision)
            => _inner.SaveCheckpoint(owner, id, expectedContentRevision);

        public WorkspaceStoreMutationResult Delete(
            CharacterWorkspaceId id,
            long expectedContentRevision)
            => _inner.Delete(id, expectedContentRevision);

        public WorkspaceStoreMutationResult Delete(
            Chummer.Contracts.Owners.OwnerScope owner,
            CharacterWorkspaceId id,
            long expectedContentRevision)
            => _inner.Delete(owner, id, expectedContentRevision);
    }
}

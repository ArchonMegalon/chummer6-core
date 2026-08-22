using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Chummer.Contracts.LifeModules;
using Chummer.Infrastructure.Files;
using Chummer.Infrastructure.Xml;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public class LifeModulesServiceTests
{
    [TestMethod]
    public void GetStages_returns_sorted_stage_list()
    {
        (string root, string xmlPath) = CreateTempLifeModulesXml();
        try
        {
            var service = new XmlLifeModulesCatalogService(xmlPath);
            IReadOnlyList<LifeModuleStageDto> stages = service.GetStages();

            Assert.HasCount(2, stages);
            Assert.AreEqual(1, stages[0].Order);
            Assert.AreEqual("Youth", stages[0].Name);
            Assert.AreEqual(2, stages[1].Order);
            Assert.AreEqual("Adult", stages[1].Name);
        }
        finally
        {
            DeleteTempDirectory(root);
        }
    }

    [TestMethod]
    public void GetModules_filters_by_stage_when_specified()
    {
        (string root, string xmlPath) = CreateTempLifeModulesXml();
        try
        {
            var service = new XmlLifeModulesCatalogService(xmlPath);
            IReadOnlyList<LifeModuleSummaryDto> all = service.GetModules();
            IReadOnlyList<LifeModuleSummaryDto> filtered = service.GetModules("Adult");

            Assert.HasCount(2, all);
            Assert.HasCount(1, filtered);
            Assert.AreEqual("Adult", filtered[0].Stage);
            Assert.AreEqual("Corporate Intern", filtered[0].Name);
        }
        finally
        {
            DeleteTempDirectory(root);
        }
    }

    [TestMethod]
    public void GetStages_exposes_required_phases_and_repeatable_real_life_phase()
    {
        (string root, string xmlPath) = CreateProjectionLifeModulesXml();
        try
        {
            var service = new XmlLifeModulesCatalogService(xmlPath);
            IReadOnlyList<LifeModuleStageDto> stages = service.GetStages();

            CollectionAssert.AreEqual(
                new[] { 1, 2, 3, 4, 5 },
                stages.Select(item => item.Order).ToArray());
            CollectionAssert.AreEqual(
                new[] { true, true, true, true, false },
                stages.Select(item => item.IsRequired).ToArray());
            CollectionAssert.AreEqual(
                new[] { false, false, false, false, true },
                stages.Select(item => item.CanRepeat).ToArray());
        }
        finally
        {
            DeleteTempDirectory(root);
        }
    }

    [TestMethod]
    public void GetOptionProjections_projects_nested_versions_requirements_effects_and_follow_ups()
    {
        (string root, string xmlPath) = CreateProjectionLifeModulesXml();
        try
        {
            var service = new XmlLifeModulesCatalogService(xmlPath);
            LifeModuleLegalOptionDto option = AssertExactlyOne(
                service.GetOptionProjections("Nationality", ["RF", "SRC"]));

            Assert.AreEqual("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", option.ModuleId);
            Assert.AreEqual(1, option.StageOrder);
            Assert.AreEqual("Nationality", option.StageId);
            Assert.IsFalse(option.CanRepeat);
            Assert.AreEqual(15m, option.KarmaCost);
            Assert.IsTrue(option.KarmaIsExact);
            Assert.AreEqual("15", option.KarmaRaw);
            Assert.AreEqual("RF", option.Source);
            Assert.AreEqual(66, option.Page);
            Assert.AreEqual("66", option.PageReference);
            Assert.AreEqual("$real base story.", option.StoryTemplate);
            Assert.IsTrue(option.IsEnabled, "A requirement-free version keeps the module structurally selectable.");
            Assert.HasCount(2, option.Versions);
            Assert.HasCount(4, option.Effects);
            Assert.IsTrue(option.Effects.Any(effect =>
                effect.Domain == "attribute"
                && effect.TargetId == "LOG"
                && effect.RawXml.Contains("<attributelevel>", StringComparison.Ordinal)));
            Assert.IsTrue(option.Effects.All(effect =>
                effect.AuthorityBlocker == XmlLifeModulesCatalogService.EffectApplicationAuthorityRequired));

            LifeModuleVersionProjectionDto restricted = option.Versions[0];
            Assert.AreEqual("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb", restricted.VersionId);
            Assert.AreEqual("Elf or Human", restricted.Label);
            Assert.AreEqual("$real chose the restricted path.", restricted.StoryTemplate);
            Assert.AreEqual(20m, restricted.KarmaCost);
            Assert.AreEqual("SRC", restricted.Source);
            Assert.AreEqual(99, restricted.Page);
            Assert.IsFalse(restricted.IsEnabled);
            CollectionAssert.Contains(
                restricted.AuthorityBlockers.ToList(),
                XmlLifeModulesCatalogService.CharacterEligibilityAuthorityRequired);

            LifeModuleRequirementProjectionDto requirement = AssertExactlyOne(restricted.Requirements);
            Assert.AreEqual("oneof", requirement.Operator);
            Assert.AreEqual("metatype", requirement.SubjectKind);
            CollectionAssert.AreEqual(new[] { "Elf", "Human" }, requirement.AcceptedValues.ToArray());
            Assert.IsFalse(requirement.IsMet);
            Assert.IsTrue(requirement.RequiresCharacterAuthority);
            Assert.AreEqual(
                XmlLifeModulesCatalogService.CharacterEligibilityAuthorityRequired,
                requirement.DisableReasonKey);
            Assert.IsTrue(requirement.RawXml.Contains("<oneof>", StringComparison.Ordinal));

            LifeModuleFollowUpPromptDto language = AssertExactlyOne(restricted.FollowUps);
            Assert.AreEqual("single-select", language.InputKind);
            CollectionAssert.AreEqual(
                new[] { "English", "German" },
                language.Options.Select(item => item.SourceValue).ToArray());

            LifeModuleVersionProjectionDto inherited = option.Versions[1];
            Assert.AreEqual("$real base story.", inherited.StoryTemplate);
            Assert.AreEqual(15m, inherited.KarmaCost);
            Assert.AreEqual("RF", inherited.Source);
            Assert.AreEqual(66, inherited.Page);
            Assert.IsTrue(inherited.IsEnabled);
            LifeModuleFollowUpPromptDto city = AssertExactlyOne(inherited.FollowUps);
            Assert.AreEqual("text", city.InputKind);
            Assert.AreEqual("City", city.Label);

            LifeModuleFollowUpPromptDto quality = option.FollowUps.Single(prompt =>
                prompt.Options.Any(item => item.SourceValue == "College Education"));
            Assert.AreEqual("single-select", quality.InputKind);
            CollectionAssert.AreEqual(
                new[] { "College Education", "Technical School Education" },
                quality.Options.Select(item => item.Label).ToArray());
            LifeModuleFollowUpPromptDto pilot = option.FollowUps.Single(prompt =>
                prompt.Options.Any(item => item.SourceValue == "Pilot Ground Craft"));
            CollectionAssert.AreEqual(
                new[] { "Pilot Ground Craft", "Pilot Watercraft" },
                pilot.Options.Select(item => item.SourceValue).ToArray());
            LifeModuleFollowUpPromptDto group = option.FollowUps.Single(prompt =>
                prompt.Options.Any(item => item.SourceValue == "Academic"));
            CollectionAssert.AreEqual(
                new[] { "Academic", "Professional" },
                group.Options.Select(item => item.SourceValue).ToArray());
            Assert.IsTrue(option.FollowUps.Any(prompt =>
                prompt.InputKind == "text" && prompt.Label == "Any"));
        }
        finally
        {
            DeleteTempDirectory(root);
        }
    }

    [TestMethod]
    public void GetOptionProjections_disables_module_and_versions_when_character_authority_is_required()
    {
        (string root, string xmlPath) = CreateProjectionLifeModulesXml();
        try
        {
            var service = new XmlLifeModulesCatalogService(xmlPath);
            LifeModuleLegalOptionDto option = AssertExactlyOne(
                service.GetOptionProjections("Teen Years", ["RF"]));

            Assert.IsFalse(option.IsEnabled);
            Assert.HasCount(1, option.Requirements);
            CollectionAssert.Contains(
                option.AuthorityBlockers.ToList(),
                XmlLifeModulesCatalogService.CharacterEligibilityAuthorityRequired);
            Assert.HasCount(1, option.Versions);
            Assert.IsFalse(option.Versions[0].IsEnabled);
            CollectionAssert.Contains(
                option.Versions[0].AuthorityBlockers.ToList(),
                XmlLifeModulesCatalogService.CharacterEligibilityAuthorityRequired);
        }
        finally
        {
            DeleteTempDirectory(root);
        }
    }

    [TestMethod]
    public void GetOptionProjections_applies_source_filter_without_guessing_missing_sources()
    {
        (string root, string xmlPath) = CreateProjectionLifeModulesXml();
        try
        {
            var service = new XmlLifeModulesCatalogService(xmlPath);

            Assert.HasCount(2, service.GetOptionProjections(enabledSources: ["rf"]));
            Assert.IsEmpty(service.GetOptionProjections(enabledSources: ["DISABLED"]));
            Assert.IsEmpty(service.GetOptionProjections(enabledSources: []));

            LifeModuleLegalOptionDto nationality = AssertExactlyOne(
                service.GetOptionProjections("Nationality", ["RF"]));
            Assert.HasCount(1, nationality.Versions);
            Assert.AreEqual("Open Path", nationality.Versions[0].Label);
        }
        finally
        {
            DeleteTempDirectory(root);
        }
    }

    [TestMethod]
    public void Canonical_catalog_preserves_five_phase_order_and_nested_nationality_versions()
    {
        var service = new XmlLifeModulesCatalogService(FindCanonicalLifeModulesPath());

        IReadOnlyList<LifeModuleStageDto> stages = service.GetStages();
        CollectionAssert.AreEqual(
            new[] { 1, 2, 3, 4, 5 },
            stages.Select(item => item.Order).ToArray());
        Assert.IsTrue(stages.Take(4).All(item => item.IsRequired && !item.CanRepeat));
        Assert.IsTrue(stages[4].CanRepeat);
        Assert.IsFalse(stages[4].IsRequired);

        IReadOnlyList<LifeModuleLegalOptionDto> nationalities =
            service.GetOptionProjections("Nationality", ["RF"]);
        LifeModuleLegalOptionDto ucas = nationalities.Single(item =>
            item.ModuleId == "f35ba316-dd0f-48ab-9f06-d7329305a44e");
        Assert.AreEqual(1, ucas.StageOrder);
        Assert.IsGreaterThan(1, ucas.Versions.Count);
        Assert.IsTrue(ucas.Versions.All(item => item.Source == "RF" && item.Page == 66));
        Assert.IsTrue(ucas.Versions.Any(item =>
            item.StoryTemplate.Contains("born", StringComparison.OrdinalIgnoreCase)));
        Assert.IsTrue(ucas.Effects.Any(item => item.Domain == "active-skill"));
        Assert.IsTrue(ucas.FollowUps.Any(item => item.InputKind == "single-select"));
    }

    [TestMethod]
    public void PathResolver_locates_data_file_from_current_directory()
    {
        string root = Path.Combine(Path.GetTempPath(), "chummer-tests-" + Guid.NewGuid().ToString("N"));
        string dataDir = Path.Combine(root, "data");
        Directory.CreateDirectory(dataDir);
        string xmlPath = Path.Combine(dataDir, "lifemodules.xml");
        File.WriteAllText(xmlPath, "<chummer><stages/><modules/><storybuilder><macros/></storybuilder></chummer>");

        try
        {
            string resolved = LifeModulesCatalogPathResolver.Resolve(baseDirectory: Path.Combine(root, "bin"), currentDirectory: root);
            Assert.AreEqual(xmlPath, resolved);
        }
        finally
        {
            DeleteTempDirectory(root);
        }
    }

    [TestMethod]
    public void PathResolver_prefers_overlay_file_when_available()
    {
        string root = Path.Combine(Path.GetTempPath(), "chummer-tests-" + Guid.NewGuid().ToString("N"));
        string baseDataDir = Path.Combine(root, "data");
        Directory.CreateDirectory(baseDataDir);
        File.WriteAllText(Path.Combine(baseDataDir, "lifemodules.xml"), "<chummer><stages/><modules/></chummer>");

        string amendsRoot = Path.Combine(root, "Docker", "Amends");
        string overlayDataDir = Path.Combine(amendsRoot, "data");
        Directory.CreateDirectory(overlayDataDir);
        string overlayManifestPath = Path.Combine(amendsRoot, "manifest.json");
        string overlayLifeModulesPath = Path.Combine(overlayDataDir, "lifemodules.xml");
        File.WriteAllText(overlayManifestPath, """
{
  "id": "local-test-amend",
  "priority": 100,
  "enabled": true
}
""");
        File.WriteAllText(overlayLifeModulesPath, "<chummer><stages/><modules/><overlay>true</overlay></chummer>");

        try
        {
            var overlays = new FileSystemContentOverlayCatalogService(root, root, amendsRoot);
            string resolved = LifeModulesCatalogPathResolver.Resolve(overlays);
            Assert.AreEqual(overlayLifeModulesPath, resolved);
        }
        finally
        {
            DeleteTempDirectory(root);
        }
    }

    private static (string Root, string XmlPath) CreateTempLifeModulesXml()
    {
        string root = Path.Combine(Path.GetTempPath(), "chummer-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string xmlPath = Path.Combine(root, "lifemodules.xml");
        File.WriteAllText(xmlPath, """
                                   <chummer>
                                     <stages>
                                       <stage order="2">Adult</stage>
                                       <stage order="1">Youth</stage>
                                     </stages>
                                     <modules>
                                       <module>
                                         <id>11111111-1111-1111-1111-111111111111</id>
                                         <stage>Youth</stage>
                                         <name>Street Kid</name>
                                         <karma>5</karma>
                                         <source>RF</source>
                                         <page>12</page>
                                         <story>$real story one.</story>
                                       </module>
                                       <module>
                                         <id>22222222-2222-2222-2222-222222222222</id>
                                         <stage>Adult</stage>
                                         <name>Corporate Intern</name>
                                         <karma>10</karma>
                                         <source>RF</source>
                                         <page>13</page>
                                         <story>$real story two.</story>
                                       </module>
                                     </modules>
                                     <storybuilder>
                                       <macros>
                                         <real>
                                           <random>
                                             <value>Alex</value>
                                           </random>
                                         </real>
                                       </macros>
                                     </storybuilder>
                                   </chummer>
                                   """);
        return (root, xmlPath);
    }

    private static (string Root, string XmlPath) CreateProjectionLifeModulesXml()
    {
        string root = Path.Combine(Path.GetTempPath(), "chummer-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string xmlPath = Path.Combine(root, "lifemodules.xml");
        File.WriteAllText(xmlPath, """
                                   <chummer>
                                     <stages>
                                       <stage order="5">Real Life</stage>
                                       <stage order="3">Teen Years</stage>
                                       <stage order="1">Nationality</stage>
                                       <stage order="4">Further Education</stage>
                                       <stage order="2">Formative Years</stage>
                                     </stages>
                                     <modules>
                                       <module>
                                         <id>aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa</id>
                                         <stage>Nationality</stage>
                                         <category>LifeModule</category>
                                         <name>Nested Nation</name>
                                         <karma>15</karma>
                                         <versions>
                                           <version>
                                             <id>bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb</id>
                                             <name>Elf or Human</name>
                                             <karma>20</karma>
                                             <story>$real chose the restricted path.</story>
                                             <source>SRC</source>
                                             <page>99</page>
                                             <required>
                                               <oneof>
                                                 <metatype>Elf</metatype>
                                                 <metatype>Human</metatype>
                                               </oneof>
                                             </required>
                                             <bonus>
                                               <knowledgeskilllevel>
                                                 <options>
                                                   <english>English</english>
                                                   <german>German</german>
                                                 </options>
                                                 <group>Language</group>
                                               </knowledgeskilllevel>
                                             </bonus>
                                           </version>
                                           <version>
                                             <id>cccccccc-cccc-cccc-cccc-cccccccccccc</id>
                                             <name>Open Path</name>
                                             <bonus>
                                               <knowledgeskilllevel>
                                                 <name>[City]</name>
                                                 <group>Street</group>
                                               </knowledgeskilllevel>
                                             </bonus>
                                           </version>
                                         </versions>
                                         <bonus>
                                           <attributelevel><name>LOG</name></attributelevel>
                                           <selectquality>
                                             <quality>College Education</quality>
                                             <quality>Technical School Education</quality>
                                           </selectquality>
                                           <selectskill limittoskill="Pilot Ground Craft,Pilot Watercraft">
                                             <val>2</val>
                                           </selectskill>
                                           <knowledgeskilllevel>
                                             <name>[Any]</name>
                                             <group>
                                               <option>
                                                 <academic>Academic</academic>
                                                 <professional>Professional</professional>
                                               </option>
                                             </group>
                                           </knowledgeskilllevel>
                                         </bonus>
                                         <story>$real base story.</story>
                                         <source>RF</source>
                                         <page>66</page>
                                       </module>
                                       <module>
                                         <id>dddddddd-dddd-dddd-dddd-dddddddddddd</id>
                                         <stage>Teen Years</stage>
                                         <category>LifeModule</category>
                                         <name>Awakened School</name>
                                         <karma>50</karma>
                                         <required>
                                           <oneof><quality>Magician</quality></oneof>
                                         </required>
                                         <versions>
                                           <version>
                                             <id>eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee</id>
                                             <name>Academy</name>
                                           </version>
                                         </versions>
                                         <source>RF</source>
                                         <page>71</page>
                                       </module>
                                       <module>
                                         <id>ffffffff-ffff-ffff-ffff-ffffffffffff</id>
                                         <stage>Real Life</stage>
                                         <category>LifeModule</category>
                                         <name>Unavailable Source</name>
                                         <karma>100</karma>
                                         <source>OTHER</source>
                                         <page>10</page>
                                       </module>
                                     </modules>
                                     <storybuilder><macros/></storybuilder>
                                   </chummer>
                                   """);
        return (root, xmlPath);
    }

    private static string FindCanonicalLifeModulesPath()
    {
        DirectoryInfo current = new(AppDomain.CurrentDomain.BaseDirectory);
        while (true)
        {
            string candidate = Path.Combine(current.FullName, "Chummer", "data", "lifemodules.xml");
            if (File.Exists(candidate))
                return candidate;

            if (current.Parent == null)
                break;

            current = current.Parent;
        }

        return Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "lifemodules.xml"));
    }

    private static T AssertExactlyOne<T>(IReadOnlyList<T> items)
    {
        Assert.HasCount(1, items);
        return items[0];
    }

    private static void DeleteTempDirectory(string root)
    {
        if (Directory.Exists(root))
            Directory.Delete(root, recursive: true);
    }
}

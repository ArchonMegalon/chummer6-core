using System;
using System.Collections.Generic;
using System.IO;
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

    private static void DeleteTempDirectory(string root)
    {
        if (Directory.Exists(root))
            Directory.Delete(root, recursive: true);
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public class LifeModulesEndToEndTests
{
    private static readonly Regex s_MacroRegex = new(@"\$[A-Za-z0-9_]+(?:\([^)]*\))?", RegexOptions.Compiled);

    private static readonly HashSet<string> s_SystemMacros = new(StringComparer.OrdinalIgnoreCase)
    {
        "DOLLAR",
        "NAME",
        "STREET",
        "REAL",
        "YEAR",
        "METATYPE",
        "METAVARIANT"
    };

    [TestMethod]
    public void LifeModules_FileLayout_HasRequiredTopLevelNodes()
    {
        XDocument doc = LoadLifeModulesDocument();
        XElement root = doc.Root!;

        Assert.AreEqual("chummer", root.Name.LocalName, "Root node must be <chummer>.");
        Assert.IsNotNull(root.Element("stages"), "Missing top-level <stages> node.");
        Assert.IsNotNull(root.Element("modules"), "Missing top-level <modules> node.");
        Assert.IsNotNull(root.Element("storybuilder"), "Missing top-level <storybuilder> node.");
    }

    [TestMethod]
    public void LifeModules_Stages_AreContiguousAndStartAtOne()
    {
        XDocument doc = LoadLifeModulesDocument();
        List<int> orders = doc.Root!
            .Element("stages")!
            .Elements("stage")
            .Select(x => int.Parse(x.Attribute("order")?.Value ?? "-1"))
            .OrderBy(x => x)
            .ToList();

        Assert.IsGreaterThan(0, orders.Count, "No <stage> entries found.");

        for (int i = 0; i < orders.Count; i++)
        {
            int expected = i + 1;
            Assert.AreEqual(expected, orders[i], "Stage ordering has a gap or does not start at 1.");
        }
    }

    [TestMethod]
    public void LifeModules_Modules_BasicRequiredFields_ArePresentAndValid()
    {
        XDocument doc = LoadLifeModulesDocument();
        HashSet<string> stageNames = doc.Root!
            .Element("stages")!
            .Elements("stage")
            .Select(x => (x.Value ?? string.Empty).Trim())
            .Where(x => !string.IsNullOrEmpty(x))
            .ToHashSet(StringComparer.Ordinal);

        List<XElement> modules = doc.Root!
            .Element("modules")!
            .Elements("module")
            .ToList();

        Assert.IsGreaterThan(0, modules.Count, "No <module> entries found in lifemodules.xml.");

        foreach (XElement module in modules)
        {
            string id = (module.Element("id")?.Value ?? string.Empty).Trim();
            Assert.IsTrue(Guid.TryParse(id, out _), $"Module has missing/invalid GUID id: '{id}'.");

            string stage = (module.Element("stage")?.Value ?? string.Empty).Trim();
            CollectionAssert.Contains(stageNames.ToList(), stage, $"Module references unknown stage '{stage}'.");

            string category = (module.Element("category")?.Value ?? string.Empty).Trim();
            Assert.AreEqual("LifeModule", category, "Module category must be 'LifeModule'.");

            string name = (module.Element("name")?.Value ?? string.Empty).Trim();
            Assert.IsGreaterThan(0, name.Length, "Module has empty <name>.");

            string karma = (module.Element("karma")?.Value ?? string.Empty).Trim();
            Assert.IsTrue(int.TryParse(karma, out _), $"Module '{name}' has non-integer <karma>: '{karma}'.");

            // Source/page are intentionally inconsistent in the current data set (module-level, version-level, or blank).
            // We only enforce hard requirements that are stable across all shipped records.
        }
    }

    [TestMethod]
    public void LifeModules_Storybuilder_MacroDefinitions_AreValid()
    {
        XDocument doc = LoadLifeModulesDocument();
        XElement macros = doc.Root!
            .Element("storybuilder")!
            .Element("macros")!;

        Assert.IsNotNull(macros, "Missing /chummer/storybuilder/macros node.");

        foreach (XElement macro in macros.Elements())
        {
            List<XElement> childElements = macro.Elements().ToList();
            if (childElements.Count == 0)
            {
                string directValue = (macro.Value ?? string.Empty).Trim();
                Assert.IsGreaterThan(0, directValue.Length,
                    $"Macro '{macro.Name.LocalName}' must have text value or child nodes.");
                continue;
            }

            XElement mode = childElements[0];
            bool randomOrPersistent = mode.Name.LocalName.Equals("random", StringComparison.OrdinalIgnoreCase)
                                      || mode.Name.LocalName.Equals("persistent", StringComparison.OrdinalIgnoreCase);
            if (randomOrPersistent)
            {
                int options = mode.Elements().Count(x => !x.Name.LocalName.Equals("default", StringComparison.OrdinalIgnoreCase));
                Assert.IsGreaterThan(0, options,
                    $"Macro '{macro.Name.LocalName}' with mode '{mode.Name.LocalName}' must define at least one non-default option.");
            }
            else
            {
                // Keyed-map macros are also used by StoryBuilder via pool lookups (e.g. $MACRO_POOLKEY).
                Assert.IsGreaterThan(0, childElements.Count, $"Macro '{macro.Name.LocalName}' must define at least one keyed child.");
            }
        }
    }

    [TestMethod]
    public void LifeModules_Stories_OnlyUseKnownMacroPrefixes()
    {
        XDocument doc = LoadLifeModulesDocument();

        HashSet<string> userMacros = doc.Root!
            .Element("storybuilder")!
            .Element("macros")!
            .Elements()
            .Select(x => x.Name.LocalName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        IEnumerable<XElement> storyNodes = doc.Root!
            .Element("modules")!
            .Descendants("story");

        foreach (XElement storyNode in storyNodes)
        {
            string text = storyNode.Value ?? string.Empty;
            foreach (Match macroMatch in s_MacroRegex.Matches(text))
            {
                string token = macroMatch.Value.Substring(1);
                int limiterIndex = token.IndexOf('(');
                if (limiterIndex >= 0)
                    token = token.Substring(0, limiterIndex);

                string prefix = token;
                int underscoreIndex = token.IndexOf('_');
                if (underscoreIndex >= 0)
                    prefix = token.Substring(0, underscoreIndex);

                bool known = s_SystemMacros.Contains(prefix) || userMacros.Contains(prefix);
                Assert.IsTrue(known,
                    $"Unknown macro prefix '${prefix}' in story: '{text}'.");
            }
        }
    }

    private static XDocument LoadLifeModulesDocument()
    {
        string path = FindLifeModulesPath();
        Assert.IsTrue(File.Exists(path), "Could not locate lifemodules.xml at: " + path);
        return XDocument.Load(path);
    }

    private static string FindLifeModulesPath()
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
}

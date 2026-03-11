using System.Xml;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public class CoreXmlNodeExtensionsTests
{
    [TestMethod]
    public void IsNullOrInnerTextIsEmpty_returns_true_for_null_node()
    {
        XmlNode node = null;
        Assert.IsTrue(Chummer.Core.XmlNodeExtensions.IsNullOrInnerTextIsEmpty(node));
    }

    [TestMethod]
    public void IsNullOrInnerTextIsEmpty_returns_true_for_whitespace()
    {
        var doc = new XmlDocument();
        var element = doc.CreateElement("test");
        element.InnerText = "  \t\n  ";

        Assert.IsTrue(Chummer.Core.XmlNodeExtensions.IsNullOrInnerTextIsEmpty(element));
    }

    [TestMethod]
    public void IsNullOrInnerTextIsEmpty_returns_false_for_nested_content()
    {
        var doc = new XmlDocument();
        var parent = doc.CreateElement("parent");
        var child = doc.CreateElement("child");
        child.InnerText = "content";
        parent.AppendChild(child);

        Assert.IsFalse(Chummer.Core.XmlNodeExtensions.IsNullOrInnerTextIsEmpty(parent));
    }
}

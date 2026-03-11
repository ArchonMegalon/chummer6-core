using System.Xml;

namespace Chummer.Core;

public static class XmlNodeExtensions
{
    /// <summary>
    /// Checks whether a node is null or contains only empty/whitespace text recursively.
    /// </summary>
    public static bool IsNullOrInnerTextIsEmpty(this XmlNode? xmlNode)
    {
        XmlNode? firstChild = xmlNode?.FirstChild;
        if (firstChild == null)
            return true;

        if (firstChild.NextSibling == null)
        {
            XmlNodeType nodeType = firstChild.NodeType;
            if (nodeType is XmlNodeType.Text or XmlNodeType.CDATA or XmlNodeType.Whitespace or XmlNodeType.SignificantWhitespace)
                return string.IsNullOrWhiteSpace(firstChild.Value);

            return CheckChildren(firstChild);
        }

        return CheckChildren(xmlNode!);

        static bool CheckChildren(XmlNode xmlParentNode)
        {
            for (XmlNode? xmlNodeInner = xmlParentNode.FirstChild; xmlNodeInner != null; xmlNodeInner = xmlNodeInner.NextSibling)
            {
                if (xmlNodeInner.FirstChild == null)
                {
                    XmlNodeType nodeType = xmlNodeInner.NodeType;
                    if (nodeType is XmlNodeType.Text or XmlNodeType.CDATA or XmlNodeType.Whitespace or XmlNodeType.SignificantWhitespace)
                    {
                        if (!string.IsNullOrWhiteSpace(xmlNodeInner.Value))
                            return false;
                    }
                }
                else if (!CheckChildren(xmlNodeInner))
                {
                    return false;
                }
            }

            return true;
        }
    }
}

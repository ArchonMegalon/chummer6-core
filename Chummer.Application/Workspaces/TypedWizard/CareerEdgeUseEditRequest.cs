// Existing typed adapter from the paired Presentation snapshot; Core executes it
// only after store admission. Rules remain in Chummer.Contracts.Characters.
using System.Globalization;
using System.Xml.Linq;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Workspaces.TypedWizard;

internal sealed record CareerEdgeUseEditorState(
    CharacterWorkspaceId WorkspaceId,
    long ContentRevision,
    CharacterCareerEdgeUseState Edge);

internal sealed record CareerEdgeUseEditRequest(
    CharacterWorkspaceId WorkspaceId,
    long ExpectedContentRevision,
    CharacterCareerEdgeUseState ExpectedState,
    CharacterCareerEdgeUseAction Action);

internal static class CareerEdgeUseEditorProjector
{
    public static CareerEdgeUseEditorState Project(
        string xml,
        CharacterWorkspaceId workspaceId,
        long contentRevision)
    {
        if (contentRevision <= 0)
        {
            throw new InvalidOperationException("Dossier revision is unavailable. Reload before editing Edge use.");
        }
        return new CareerEdgeUseEditorState(
            workspaceId,
            contentRevision,
            ProjectState(xml));
    }

    internal static CharacterCareerEdgeUseState ProjectState(string xml)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(xml);
        XDocument document = XDocument.Parse(xml, LoadOptions.PreserveWhitespace);
        XElement root = document.Root is { Name.LocalName: "character" }
            ? document.Root
            : throw new InvalidOperationException("Workspace XML must use <character> as the root node.");
        bool created = ReadRequiredBool(root, "created");
        int edgeUsed = ReadOptionalNonNegativeInt(root, "edgeused");

        XElement[] attributeContainers = root.Elements("attributes").Take(2).ToArray();
        if (attributeContainers.Length != 1)
        {
            throw new InvalidOperationException("The saved runner must have one exact <attributes> collection.");
        }
        XElement[] edgeAttributes = attributeContainers[0]
            .Elements("attribute")
            .Where(attribute => string.Equals(
                ReadRequiredText(attribute, "name"),
                "EDG",
                StringComparison.Ordinal))
            .Take(2)
            .ToArray();
        if (edgeAttributes.Length != 1)
        {
            throw new InvalidOperationException("The saved runner must have one exact EDG attribute identity.");
        }
        int totalEdge = ReadTotalEdge(edgeAttributes[0]);
        if (!CharacterCareerEdgeUseRules.TryProject(
                created,
                edgeUsed,
                totalEdge,
                out CharacterCareerEdgeUseState? state)
            || state is null)
        {
            throw new InvalidOperationException("Edge use is available only for an exact saved Career runner.");
        }
        return state;
    }

    private static int ReadTotalEdge(XElement attribute)
    {
        XElement[] totalValues = attribute.Elements("totalvalue").Take(2).ToArray();
        XElement[] legacyValues = attribute.Elements("value").Take(2).ToArray();
        if (totalValues.Length > 1 || legacyValues.Length > 1)
        {
            throw new InvalidOperationException("The saved EDG attribute has duplicate total values.");
        }
        string value = totalValues.SingleOrDefault()?.Value
            ?? legacyValues.SingleOrDefault()?.Value
            ?? throw new InvalidOperationException("The saved EDG attribute has no exact total value.");
        if (!int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
            || parsed < 0)
        {
            throw new InvalidOperationException("The saved EDG total is invalid.");
        }
        return parsed;
    }

    private static bool ReadRequiredBool(XElement root, string name)
    {
        XElement[] values = root.Elements(name).Take(2).ToArray();
        if (values.Length != 1 || !bool.TryParse(values[0].Value.Trim(), out bool parsed))
        {
            throw new InvalidOperationException($"The saved runner has an invalid or duplicate <{name}> value.");
        }
        return parsed;
    }

    private static int ReadOptionalNonNegativeInt(XElement root, string name)
    {
        XElement[] values = root.Elements(name).Take(2).ToArray();
        if (values.Length == 0)
        {
            return 0;
        }
        if (values.Length != 1
            || !int.TryParse(values[0].Value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
            || parsed < 0)
        {
            throw new InvalidOperationException($"The saved runner has an invalid or duplicate <{name}> value.");
        }
        return parsed;
    }

    private static string ReadRequiredText(XElement parent, string name)
    {
        XElement[] values = parent.Elements(name).Take(2).ToArray();
        if (values.Length != 1 || string.IsNullOrWhiteSpace(values[0].Value))
        {
            throw new InvalidOperationException($"The saved attribute has an invalid or duplicate <{name}> value.");
        }
        return values[0].Value.Trim();
    }
}

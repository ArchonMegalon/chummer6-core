using System.Globalization;
using System.Xml.Linq;
using Chummer.Contracts.Characters;
namespace Chummer.Application.Workspaces.TypedWizard;
internal static class TableMutation
{
    public static string ApplyCareerWeaponFire(
        string xml,
        CareerWeaponFireRequest request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(xml);
        ArgumentNullException.ThrowIfNull(request);
        CareerWeaponFireProjection current = CareerWeaponFireEditorProjector
            .ProjectValue(xml, request.Identity.WeaponId);
        if (current.State.Identity != request.Identity
            || !CharacterWeaponFireRules.TryValidateMutation(
                current.State,
                request.ExpectedNodeRevision,
                request.Mode,
                request.ConfirmedPartial,
                out CharacterWeaponFirePlan plan))
        {
            throw new InvalidOperationException(
                "The selected Weapon ammo, firing mode, confirmation, or local revision changed.");
        }

        if (current.Clip is null)
        {
            throw new InvalidOperationException("The selected Weapon no longer has a saved active clip.");
        }

        if (plan.DeleteAmmoGear)
        {
            if (current.AmmoGear is null)
            {
                throw new InvalidOperationException("The exhausted linked ammo Gear is missing.");
            }
            current.Clip.Remove();
            current.AmmoGear.Remove();
        }
        else
        {
            current.Clip.Elements("count").Single().Value =
                plan.NewAmmoRemaining.ToString(CultureInfo.InvariantCulture);
            if (current.AmmoGear is not null && plan.NewAmmoGearQuantity is decimal quantity)
            {
                current.AmmoGear.Elements("qty").Single().Value =
                    quantity.ToString(CultureInfo.InvariantCulture);
            }
            else if (current.AmmoGear is null && plan.NewAmmoRemaining == 0)
            {
                current.Clip.Remove();
            }
        }

        XElement? clips = current.Weapon.Elements("clips").SingleOrDefault();
        if (clips is not null && !clips.Elements("clip").Any())
        {
            clips.Remove();
        }
        return Serialize(current.Weapon.Document!);
    }
    public static string ApplyCareerEdgeUseEdit(
        string xml,
        CareerEdgeUseEditRequest request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(xml);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.ExpectedState);
        CharacterCareerEdgeUseState current = CareerEdgeUseEditorProjector.ProjectState(xml);
        if (current != request.ExpectedState)
        {
            throw new InvalidOperationException(
                "The runner's used or total Edge changed while the editor was open.");
        }
        int updated = CharacterCareerEdgeUseRules.Apply(current, request.Action);

        XDocument document = XDocument.Parse(xml, LoadOptions.PreserveWhitespace);
        XElement root = document.Root is { Name.LocalName: "character" }
            ? document.Root
            : throw new InvalidOperationException("Workspace XML must use <character> as the root node.");
        XElement edgeUsed = root.Elements("edgeused").SingleOrDefault()
            ?? new XElement("edgeused");
        edgeUsed.Value = updated.ToString(CultureInfo.InvariantCulture);
        if (edgeUsed.Parent is null)
        {
            root.Add(edgeUsed);
        }
        return Serialize(document);
    }
    private static string Serialize(XDocument document)
    {
        using StringWriter writer = new(CultureInfo.InvariantCulture);
        document.Save(writer, SaveOptions.DisableFormatting);
        return writer.ToString();
    }
}

using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

/// <summary>Projection of an already admitted saved runner; never grants damage-edit authority.</summary>
public static class CharacterCurrentConditionMonitorQuery
{
    public static CharacterConditionMonitorSection Project(WorkspaceStoredDocument saved,
        ICharacterSourceDataResolver sources, ICharacterCreationRuleSourceRecovery? recovery = null)
    {
        string xml = saved.Document.Content;
        if (xml.Length > 0 && xml[0] == '\uFEFF') xml = xml[1..];
        using var input = new StringReader(xml);
        using var reader = XmlReader.Create(input, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
        var root = XDocument.Load(reader).Root ?? throw new InvalidDataException("Missing character.");
        if (root.Name != "character" || !Boolean("created")) throw new InvalidDataException("Saved Career runner required.");
        int physical = Integer("physicalcmfilled"), stun = Integer("stuncmfilled"), overflow = Integer("physicalcmoverflow");
        bool core = Boolean("physicalcmiscorecm"), matrix = Boolean("stuncmismatrixcm");
        if (physical < 0 || stun < 0 || overflow < 0) throw new InvalidDataException("Invalid saved damage.");
        if (core || matrix)
        {
            // Alternate tracks remain explicitly labelled, saved display fields.
            // They must never be substituted with ordinary BOD/WIL calculations.
            int physicalTrack = Integer("physicalcm"), stunTrack = Integer("stuncm");
            if (physicalTrack <= 0 || stunTrack <= 0 || stun > stunTrack)
                throw new InvalidDataException("Alternate saved tracks unavailable.");
            return new(physicalTrack, physical, overflow, Integer("physicalcmthresholdoffset"), Text("physicalcmnaturalrecovery") ?? "",
                stunTrack, stun, Integer("stuncmthresholdoffset"), Text("stuncmnaturalrecovery") ?? "", core, matrix, true);
        }

        // The native format stores cumulative physical damage. Its optional root
        // overflow field is not the Windows calculated-values overflow field.
        // Core's existing strict condition parser rejects ambiguous nonzero values.
        var sheet = CharacterSheetDerivedQuery.ParseConditionDisplay9590(saved, sources, recovery);
        if (sheet.UnavailableReason is not null || sheet.AuthorityDigest is null || sheet.Condition is not { } condition
            || condition.PhysicalMaximum <= 0 || condition.StunMaximum <= 0 || stun > condition.StunMaximum)
            throw new InvalidDataException("Current condition calculation unavailable.");
        var bodies = sheet.Attributes.Where(x => x.Name == "BOD").ToArray();
        if (bodies.Length != 1 || bodies[0].Total <= 0)
            throw new InvalidDataException("Current overflow capacity unavailable.");
        int overflowCapacity = checked(bodies[0].Total + condition.OverflowModifier);
        if (overflowCapacity < 0) throw new InvalidDataException("Invalid current overflow capacity.");
        var damaged = condition.WithDamage(Math.Min(physical, condition.PhysicalMaximum), stun);
        // This section's editor contract keeps cumulative filled boxes and an
        // overflow allowance. It must not replace either with current overflow damage.
        return new(condition.PhysicalMaximum, physical, overflowCapacity, damaged.PhysicalThresholdOffset,
            condition.PhysicalRecoveryPool.ToString(CultureInfo.InvariantCulture), condition.StunMaximum, stun,
            damaged.StunThresholdOffset, condition.StunRecoveryPool.ToString(CultureInfo.InvariantCulture), false, false, true);

        string? Text(string name)
        {
            var nodes = root.Elements(name).ToArray();
            if (nodes.Length > 1 || nodes.Any(x => x.HasElements || x.HasAttributes))
                throw new InvalidDataException("Malformed condition scalar.");
            return nodes.SingleOrDefault()?.Value;
        }
        int Integer(string name)
        {
            string? text = Text(name);
            return text is null ? 0 : int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
                ? value : throw new InvalidDataException("Invalid condition number.");
        }
        bool Boolean(string name)
        {
            string? text = Text(name);
            return text is null ? false : bool.TryParse(text, out bool value)
                ? value : throw new InvalidDataException("Invalid condition flag.");
        }
    }
}

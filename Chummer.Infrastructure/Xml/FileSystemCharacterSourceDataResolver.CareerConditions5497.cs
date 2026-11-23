using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Application.Session;

namespace Chummer.Infrastructure.Xml;

public sealed partial class FileSystemCharacterSourceDataResolver
{
    private sealed partial class SourceDataContext : ICurrentCareerConditionSourceCapabilities5497
    {
        private bool UsesCareerCondition5497
            => _sourceInputs.Accounting?.HasCareerConditionSemantics5497 == true
                && _character.Elements("created").ToArray() is [var created]
                && !created.HasAttributes && !created.HasElements
                && bool.TryParse(created.Value, out bool career) && career;

        public bool TryRequiresCareerCondition5497(out bool required)
        {
            using IDisposable scope = _sourceInputs.Enter();
            required = false;
            if (_sourceInputs.HasSourceDrift
                || !TryResolveCareerSettingsProfile(out var settings, out var state)
                || string.IsNullOrWhiteSpace(state)
                || !TryLoadEffectiveDocument(_catalog, "metatypes.xml", out var document)
                || !TryProjectCurrentSourceDocument("metatypes.xml", [("metatypes", "metatype")], ref document)
                || document?.Root is null) return false;
            // Inspect the retained merged source, not its old unavailable catalog
            // projection or the saved improvement. This selects a reader only.
            string id = _character.Element("metatypeguid")?.Value ?? string.Empty;
            string nativeId = _character.Element("metatypeid")?.Value ?? string.Empty;
            if (id.Length != 0 && nativeId.Length != 0 && id != nativeId) return false;
            if (id.Length == 0) id = nativeId;
            if (!Guid.TryParseExact(id, "D", out var parsed) || parsed == Guid.Empty) return false;
            var candidates = document.Root.Elements("metatypes").Elements("metatype")
                .Where(row => row.Element("id")?.Value == id).ToArray();
            if (candidates is not [var metatype]) return false;
            required = XElement.Parse(settings).Elements().Any(e => e.Name.LocalName == SessionPlayVehiclePolicySource5400.OverrideElement)
                || metatype.Elements("bonus").Elements().Any(e => e.Name.LocalName == "conditionmonitor");
            return !_sourceInputs.HasSourceDrift;
        }
    }
}

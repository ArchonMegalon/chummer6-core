using System.Xml.Linq;
using Chummer.Application.Characters;

namespace Chummer.Infrastructure.Xml;

public sealed partial class FileSystemCharacterSourceDataResolver
{
    private sealed partial class SourceDataContext : ICharacterCareerInitiationSourceContext
    {
        public bool TryResolveCareerInitiationSources(out CharacterCareerInitiationSources? sources)
        {
            sources = null;
            using IDisposable scope = _sourceInputs.Enter();
            if (_sourceInputs.HasSourceDrift
                || !TryResolveCareerSettingsProfile(out string settings, out string settingsAuthority)
                || !TryLoadEffectiveDocument(_catalog, "metamagic.xml", out XDocument? document)
                || document?.Root?.Name != "chummer") return false;
            XElement[] containers = document.Root.Elements("metamagics").Take(2).ToArray();
            if (containers.Length != 1 || containers[0].HasAttributes) return false;
            XElement[] rows = containers[0].Elements().Take(4097).ToArray();
            if (_customDirectories.Count > 0
                && !TryEnumerateTargets("metamagic.xml", ["metamagics"], "metamagic", out rows)) return false;
            if (rows.Length > 4096 || rows.Any(x => x.Name != "metamagic" || x.HasAttributes)
                || !TryComputeEffectiveInputDigest(_catalog, "metamagic.xml", out string digest)
                || !TryComputeSelectedCustomDataInputsDigestFor(_customDirectories, "metamagic.xml", out string custom)
                || _sourceInputs.HasSourceDrift) return false;
            var ids = new HashSet<Guid>();
            var result = new List<CharacterCareerInitiationSourceRow>();
            foreach (var row in rows)
            {
                XElement[] sourceIds = row.Elements("id").Take(2).ToArray();
                XElement[] books = row.Elements("source").Take(2).ToArray();
                if (sourceIds.Length != 1 || books.Length != 1 || sourceIds[0].HasElements || books[0].HasElements
                    || !Guid.TryParseExact(sourceIds[0].Value, "D", out Guid id) || id == Guid.Empty || !ids.Add(id))
                    return false;
                result.Add(new(row.ToString(SaveOptions.DisableFormatting), IsEnabledSource(books[0].Value)));
            }
            sources = new(settings, string.Join('\0', "sr5-career-initiation-sources/v1",
                settingsAuthority, digest, custom), result.AsReadOnly());
            return !_sourceInputs.HasSourceDrift;
        }
    }
}

using System.Xml.Linq;

namespace Chummer.Infrastructure.Xml;

public sealed partial class FileSystemCharacterSourceDataResolver
{
    private sealed partial class SourceInputSnapshot
    {
        private bool _captureCurrentGrades5719;
        private string LiveGradeDependencyRecipe5719 => _captureCurrentGrades5719
            ? RetainedCharacterRuleSourceInputs.WithCurrentGradesRecipe5719(LiveDependencyRecipe1244)
            : LiveDependencyRecipe1244;

        // Old retained archives keep their original recipe, even when their XML
        // contains grades. Missing catalog bytes can never be fetched from the host.
        public bool CaptureCurrentGradeInputs5719(XElement character)
            => _retained is not null
                ? RetainedCharacterRuleSourceInputs.HasCurrentGradesRecipe5719(_retained.DependencyRecipe1244)
                : character.Elements().Any(x => x.Name.LocalName is "initiategrade" or "submersiongrade"
                    && (x.HasElements || x.HasAttributes || x.Value is not ("" or "0")))
                    || character.Elements().Any(x => x.Name.LocalName is "initiationgrades" or "metamagics" && x.HasElements);
        public void ObserveCurrentGradeInputs5719() => _captureCurrentGrades5719 = true;
    }

    private sealed partial class SourceDataContext
    {
        private bool CaptureCurrentGradeDependencies5719()
        {
            if (!_sourceInputs.CaptureCurrentGradeInputs5719(_character)) return true;
            // Observe the actual typed consumer's catalog, enabled-book decisions,
            // effective settings and selected amendments as one frozen dependency.
            if (!TryResolveCareerInitiationSources(out var sources) || sources is null || _sourceInputs.HasSourceDrift)
                return false;
            _sourceInputs.ObserveCurrentGradeInputs5719();
            return true;
        }
    }
}

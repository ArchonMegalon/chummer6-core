using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;

namespace Chummer.Infrastructure.Xml;

public sealed partial class FileSystemCharacterSourceDataResolver
{
    private string? _compiledGraphXml;

    private sealed partial class RuleSourceAccounting
    {
        public override ICharacterSourceDataResolver? CreateCompiledGraphRead(CharacterCreationCompiledGraphRead invocation)
        {
            if (invocation is null || !invocation.IsIssuedFor(this, captureDigest, inputs.CharacterXml)
                || CurrentSourceSemantics || !CapturePurchases || !captureCompleteSkillCaps)
                return null;
            try
            {
                RetainedCharacterRuleSourceInputs.ValidateCharacter(invocation.CharacterXml);
                var original = XElement.Parse(inputs.CharacterXml, LoadOptions.None);
                var candidate = XElement.Parse(invocation.CharacterXml, LoadOptions.None);
                if (original.Name != "character" || candidate.Name != original.Name) return null;
                // Mechanics may change; saved profile, edition and source-selection
                // declarations may not. Compare complete nodes, including duplicates.
                string[] selectors = ["settings", "customdatadirectorynames", "gameedition", "buildmethod",
                    CharacterCreationLifestyleMechanicsRules.CapabilityElement,
                    NativeSr5CreationProfiles.KarmaQualityPromptElement];
                foreach (string selector in selectors)
                {
                    var left = original.Elements().Where(node => node.Name.LocalName.Equals(selector, StringComparison.OrdinalIgnoreCase)).ToArray();
                    var right = candidate.Elements().Where(node => node.Name.LocalName.Equals(selector, StringComparison.OrdinalIgnoreCase)).ToArray();
                    if (left.Length != right.Length || left.Where((node, index) => !XNode.DeepEquals(node, right[index])).Any())
                        return null;
                }

                // Frozen input maps are reused, not recopied or exported. Any
                // observations belong to this private read view, never the original
                // execution's accounting or public completion reports.
                var view = new RuleSourceAccounting(inputs, captureDigest, sourcebookSelection800,
                    resolver._creationSourceExecutionSchema);
                if (!view.EnablePurchaseSourceExecution() || !view.EnableCompleteSkillCapSourceExecution()
                    || resolver._sourcebooks800 && !view.EnableCandidateSourcebookSelection()) return null;
                view.resolver._compiledGraphXml = invocation.CharacterXml;
                view.resolver._standaloneSettingsExport5691 = resolver._standaloneSettingsExport5691;
                return new CompiledGraphResolver(view.resolver, inputs);
            }
            catch (Exception error) when (error is IOException or InvalidOperationException or ArgumentException
                or System.Xml.XmlException or OverflowException)
            { return null; }
        }
    }

    // Deliberately exposes no capture/export or resolution interface. Operation
    // wrappers preserve that boundary and the existing disposal/source-read guards.
    private sealed class CompiledGraphResolver(FileSystemCharacterSourceDataResolver resolver,
        RetainedCharacterRuleSourceInputs inputs)
        : ICharacterSourceDataResolver, ICharacterSourceDataResolverOperationScopeFactory
    {
        public ICharacterSourceDataContext? TryCreateContext(string xml)
            => Admit(resolver.TryCreateContext(xml));

        private ICharacterSourceDataContext? Admit(ICharacterSourceDataContext? value)
            => value is SourceDataContext source && source.MatchesRetainedSelection(inputs) ? source : null;

        public ICharacterSourceDataResolver? ForCreationSourceExecution(string? schema)
            => schema == resolver._creationSourceExecutionSchema ? this : null;

        public ICharacterSourceDataResolverOperationScope CreateOperationScope()
            => new CompiledGraphOperation(this, resolver.CreateOperationScope());

        private sealed class CompiledGraphOperation(CompiledGraphResolver owner, ICharacterSourceDataResolverOperationScope scope)
            : ICharacterSourceDataResolverOperationScope, ICharacterCreationRetainedSheetScope8573,
              ICharacterCreationRetainedSheetScopeFactory8573
        {
            public ICharacterSourceDataResolverOperationScope? CreateRetainedCreationSheetScope8573()
            {
                var nested = (scope as ICharacterCreationRetainedSheetScopeFactory8573)?.CreateRetainedCreationSheetScope8573();
                return nested is null ? null : new CompiledGraphOperation(owner, nested);
            }
            public bool IsRetainedCreationSheetScope8573()
                => scope is ICharacterCreationRetainedSheetScope8573 retained && retained.IsRetainedCreationSheetScope8573();
            public ICharacterSourceDataContext? TryCreateContext(string xml) => owner.Admit(scope.TryCreateContext(xml));
            public ICharacterSourceDataResolver? ForCreationSourceExecution(string? schema)
                => owner.ForCreationSourceExecution(schema) is null ? null : this;
            public void Dispose() => scope.Dispose();
        }
    }
}

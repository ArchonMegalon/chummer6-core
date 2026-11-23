using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using P1244 = Chummer.Application.Characters.CharacterCreationSourcebookSelectionProjection;

namespace Chummer.Infrastructure.Xml;

public sealed partial class FileSystemCharacterSourceDataResolver
{
    private sealed partial class SourceInputSnapshot
    {
        private bool _captureLifeModules2040;
        private bool _captureVehicleWorkshop2330;
        // Only the real, successful source consumer selects the new live recipe.
        // Retained contexts always keep the recipe serialized in their own archive.
        public void ObserveVehicleWorkshopInputs2330() => _captureVehicleWorkshop2330 = true;

        // The context supplies the existing resolved build method, never an authority flag.
        public void InitializeCaptureDependencyRecipe2040(string buildMethod)
            => _captureLifeModules2040 = buildMethod == CharacterCreationBuildMethods.LifeModules;

        // A retained resolver ALWAYS uses its serialized recipe. Selecting V4 cannot
        // upgrade an old archive or read the missing dependencies from the machine.
        public string DependencyRecipe1244 => _retained?.DependencyRecipe1244
            ?? (_captureVehicleWorkshop2330
                ? RetainedCharacterRuleSourceInputs.WithVehicleWorkshopRecipe2330(LiveFreeSpriteDependencyRecipe5732)
                : LiveFreeSpriteDependencyRecipe5732);
        public string BaseDependencyRecipe2330
            => RetainedCharacterRuleSourceInputs.BaseDependencyRecipe2193(DependencyRecipe1244);
        public bool HasVehicleWorkshopRecipe2330
            => RetainedCharacterRuleSourceInputs.HasVehicleWorkshopRecipe2330(DependencyRecipe1244);
        private string LiveDependencyRecipe1244 => (_captureLifeModules2040
                ? CreationSourceExecutionSchema == CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV4
                    ? RetainedCharacterRuleSourceInputs.EssenceLifeModuleDependencyRecipe2040
                    : RetainedCharacterRuleSourceInputs.LifeModuleDependencyRecipe2040
                : CreationSourceExecutionSchema == CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV4
                    ? RetainedCharacterRuleSourceInputs.EssenceDependencyRecipe1244
                    : RetainedCharacterRuleSourceInputs.LegacyDependencyRecipe1244)
                + RetainedCharacterRuleSourceInputs.MartialDependencySuffix2421;

        public string BaseDependencyRecipe2193 => RetainedCharacterRuleSourceInputs.BaseDependencyRecipe2193(DependencyRecipe1244);
        public bool HasMartialDependencyRecipe2193 => DependencyRecipe1244.EndsWith(
            RetainedCharacterRuleSourceInputs.MartialDependencySuffix2193, StringComparison.Ordinal)
            || HasMartialKnowledgeRecipe2421;
        public bool HasMartialKnowledgeRecipe2421 => DependencyRecipe1244.EndsWith(
            RetainedCharacterRuleSourceInputs.MartialDependencySuffix2421, StringComparison.Ordinal);

        public bool HasLifeModuleDependencyRecipe2040 => BaseDependencyRecipe2330 is
            RetainedCharacterRuleSourceInputs.LifeModuleDependencyRecipe2040
            or RetainedCharacterRuleSourceInputs.EssenceLifeModuleDependencyRecipe2040;

        public bool HasRetainedEssenceRecipe1244 => _retained?.BaseDependencyRecipe2330 is
            RetainedCharacterRuleSourceInputs.EssenceDependencyRecipe1244
            or RetainedCharacterRuleSourceInputs.EssenceLifeModuleDependencyRecipe2040;

        // Called only after the current context has re-observed its capture recipe.
        // Equality includes character, policy, selection, recipe, local/portable format and every
        // file/existence/inventory observation. A retained mismatch is a refusal,
        // never permission to reseal a subset or return stale character inputs.
        public bool TrySelectCaptureInputs1266(RetainedCharacterRuleSourceInputs observed,
            out RetainedCharacterRuleSourceInputs retained)
        {
            retained = observed;
            if (_retained is not null)
            {
                if (!observed.Export().AsSpan().SequenceEqual(_retained.Export())) return false;
                retained = _retained;
            }
            else if (observed.TryCreatePortable(out var portable)) retained = portable!;
            return true;
        }
    }

    // Internal consumer seam: the caller supplies the real resolver context, not
    // a recipe string, public report, or boolean assertion. The inventory is data,
    // not an execution receipt. Essence must still bind its invocation and result.
    internal static bool TryReadEssenceDependencies1244(ICharacterSourceDataContext source,
        out EssenceDependencyInventory1244? inventory)
    {
        inventory = null;
        return source is SourceDataContext context
            && context.TryReadEssenceDependencies1244(out inventory);
    }

    internal sealed record EssenceDependencyRow1244(string Scope, string Container, string Entry,
        int Index, int NodeIndex, string SourceId, string Name, string Xml, string NodeDigest,
        string IdentityDigest, string SourceBook, P1244.SourceRule Rule, bool Eligible);

    internal sealed class EssenceDependencyInventory1244
    {
        internal EssenceDependencyInventory1244(string policyDigest, string profileInputsDigest,
            string booksInputsDigest, P1244.Result selection, IReadOnlyList<P1244.Scope> scopes,
            IReadOnlyList<EssenceDependencyRow1244> rows)
        {
            PolicyDigest = policyDigest;
            ProfileInputsDigest = profileInputsDigest;
            BooksInputsDigest = booksInputsDigest;
            CatalogueInputsDigest = selection.CatalogueInputsDigest;
            CatalogueXmlDigest = selection.CatalogueXmlDigest;
            EnabledSourcebooks = Array.AsReadOnly(selection.EnabledBooks.Select(book => book.Code).ToArray());
            Scopes = scopes;
            Rows = rows;
            InventoryDigest = CharacterCreationFinalizationDigest.Compute(new
            {
                Recipe, PolicyDigest, ProfileInputsDigest, BooksInputsDigest,
                CatalogueInputsDigest, CatalogueXmlDigest, EnabledSourcebooks, Scopes, Rows
            });
        }

        internal string Recipe => RetainedCharacterRuleSourceInputs.EssenceDependencyRecipe1244;
        internal string PolicyDigest { get; }
        internal string ProfileInputsDigest { get; }
        internal string BooksInputsDigest { get; }
        internal string CatalogueInputsDigest { get; }
        internal string CatalogueXmlDigest { get; }
        internal string InventoryDigest { get; }
        internal IReadOnlyList<string> EnabledSourcebooks { get; }
        internal IReadOnlyList<P1244.Scope> Scopes { get; }
        internal IReadOnlyList<EssenceDependencyRow1244> Rows { get; }

        // Exact root selection for the ware/grade consumer. Descendant source gates
        // remain separately visible in Rows and must be checked when consumed.
        internal bool TryResolveEnabledRoot(string scope, string container, string entry,
            string sourceId, string name, out string? xml)
        {
            xml = null;
            if (string.IsNullOrEmpty(sourceId) && string.IsNullOrEmpty(name)) return false;
            var matches = Rows.Where(row => row.NodeIndex == 0 && row.Scope == scope
                && row.Container == container && row.Entry == entry
                && (sourceId.Length != 0 ? row.SourceId == sourceId : row.Name == name)).Take(2).ToArray();
            if (matches is not [var match] || !match.Eligible) return false;
            xml = match.Xml;
            return true;
        }

        // Bounded consumer gate: compare the complete selected tree, with every
        // source-bearing descendant enabled. No filtered or rewritten tree gains
        // equality. A consumer needing conditional descendant selection must
        // implement that separately using the exact typed Rows.
        internal bool MatchesEnabledTree(string scope, string container, string entry, XElement source)
        {
            string xml = source.ToString(SaveOptions.DisableFormatting);
            var roots = Rows.Where(row => row.NodeIndex == 0 && row.Scope == scope
                && row.Container == container && row.Entry == entry && row.Xml == xml).Take(2).ToArray();
            return roots is [var root] && Rows.Where(row => row.Scope == scope && row.Container == container
                && row.Entry == entry && row.Index == root.Index).All(row => row.Eligible);
        }
    }

    private sealed partial class SourceDataContext
    {
        private EssenceDependencyInventory1244? _essenceDependencies1244;

        private bool CaptureDependencyRecipe1244()
        {
            if (!CaptureCurrentGradeDependencies5719() || !CaptureSpriteDependencies5751()
                || !CaptureFreeSpriteDependencies5732()) return false;
            if (_sourceInputs.HasMartialDependencyRecipe2193 && !CaptureMartialDependencyInputs2193()) return false;
            if (_sourceInputs.HasLifeModuleDependencyRecipe2040
                && _buildMethod != CharacterCreationBuildMethods.LifeModules) return false;
            // Re-run exactly the consumer that enlarged the live snapshot. The same
            // bytes, existence observations and selected-file queries must replay cold.
            if (_sourceInputs.HasVehicleWorkshopRecipe2330 && !TryResolveVehicleWorkshopCatalog(out _)) return false;
            if (_sourceInputs.BaseDependencyRecipe2330 is RetainedCharacterRuleSourceInputs.LegacyDependencyRecipe1244
                or RetainedCharacterRuleSourceInputs.LifeModuleDependencyRecipe2040) return true;
            _ = ReadEssenceDependencyInventory1244();
            return !_sourceInputs.HasSourceDrift;
        }

        internal bool TryReadEssenceDependencies1244(out EssenceDependencyInventory1244? inventory)
        {
            inventory = null;
            if (!_sourceInputs.HasRetainedEssenceRecipe1244) return false;
            try
            {
                using var entered = _sourceInputs.Enter();
                if (!_sourceInputs.TryAdmitReuse(_catalog)) return false;
                var observed = ReadEssenceDependencyInventory1244();
                if (_sourceInputs.HasSourceDrift) return false;
                inventory = observed;
                return true;
            }
            catch (Exception error) when (IsCaptureRejection(error)) { return false; }
        }

        private EssenceDependencyInventory1244 ReadEssenceDependencyInventory1244()
        {
            if (_sourceInputs.BaseDependencyRecipe2330 is not (RetainedCharacterRuleSourceInputs.EssenceDependencyRecipe1244
                    or RetainedCharacterRuleSourceInputs.EssenceLifeModuleDependencyRecipe2040)
                || _sourceInputs.HasSourceDrift) throw RetainedCharacterRuleSourceInputs.Rejected();
            if (_essenceDependencies1244 is not null) return _essenceDependencies1244;
            if (!TryResolveCreationEffectivePolicy(out var policy) || policy?.BooksInputsDigest is null)
                throw RetainedCharacterRuleSourceInputs.Rejected();
            ReadCatalogue800();
            bool previous = _readingSourcebooks800;
            _readingSourcebooks800 = true; // Observe disabled choices too; never prune the closure.
            try
            {
                var scopes = new List<P1244.Scope>();
                var inventory = new List<EssenceDependencyRow1244>();
                long inventoryBytes = 0;
                foreach (string scope in new[] { "cyberware", "bioware" })
                {
                    string file = scope + ".xml";
                    if (!TryLoadEffectiveDocument(_catalog, file, out var document) || document?.Root is null
                        || !TryComputeEffectiveInputDigest(_catalog, file, out var digest))
                        throw RetainedCharacterRuleSourceInputs.Rejected();
                    ValidateFilterShape800(document.Root);
                    var shapes = document.Root.Elements().SelectMany(container => container.Elements()
                        .Where(row => row.Element("id") is not null || row.Element("name") is not null || row.Element("source") is not null)
                        .Select(row => (Container: container.Name.LocalName, Entry: row.Name.LocalName))).ToHashSet();
                    foreach (var directory in _customDirectories)
                    foreach (var path in EnumerateSourceFiles(directory.Path, "*_" + file, SearchOption.AllDirectories))
                    {
                        if (!new[] { "override_", "custom_", "amend_" }.Any(prefix =>
                            Path.GetFileName(path).StartsWith(prefix, StringComparison.OrdinalIgnoreCase))) continue;
                        if (!TryLoadXml(path, out var custom) || custom?.Root is null)
                            throw RetainedCharacterRuleSourceInputs.Rejected();
                        ValidateFilterShape800(custom.Root);
                        foreach (var container in custom.Root.Elements())
                        foreach (var row in container.Elements().Where(row => row.Element("id") is not null
                            || row.Element("name") is not null || row.Element("source") is not null))
                            shapes.Add((container.Name.LocalName, row.Name.LocalName));
                    }
                    var choices = new List<P1244.Choice>();
                    var eligible = new List<string>();
                    var effectiveRows = new List<string>();
                    foreach (var shape in shapes.OrderBy(row => row.Container, StringComparer.Ordinal)
                        .ThenBy(row => row.Entry, StringComparer.Ordinal))
                    {
                        if (!TryEnumerateTargets(file, [shape.Container], shape.Entry, out var rows))
                            throw RetainedCharacterRuleSourceInputs.Rejected();
                        for (int index = 0; index < rows.Length; index++)
                        {
                            string rootXml = rows[index].ToString(SaveOptions.DisableFormatting);
                            effectiveRows.Add(rootXml);
                            int nodeIndex = 0;
                            foreach (var row in rows[index].DescendantsAndSelf().Where(row => row == rows[index]
                                || row.Element("source") is not null))
                            {
                                var gate = SourceGate800(file, row);
                                string xml = row == rows[index] ? rootXml : row.ToString(SaveOptions.DisableFormatting);
                                inventoryBytes += System.Text.Encoding.UTF8.GetByteCount(xml);
                                if (inventoryBytes > RetainedCharacterRuleSourceInputs.MaximumTotalBytes)
                                    throw RetainedCharacterRuleSourceInputs.Rejected();
                                string nodeDigest = CharacterCreationFinalizationDigest.Compute(xml);
                                string identity = CharacterCreationFinalizationDigest.Compute(new
                                    { scope, shape.Container, shape.Entry, index, Node = nodeIndex, nodeDigest });
                                choices.Add(new(identity, nodeDigest, gate.Book, gate.Rule));
                                if (gate.Eligible) eligible.Add(identity);
                                inventory.Add(new(scope, shape.Container, shape.Entry, index, nodeIndex++,
                                    (string?)row.Element("id") ?? "", (string?)row.Element("name") ?? "",
                                    xml, nodeDigest, identity, gate.Book, gate.Rule, gate.Eligible));
                                if (inventory.Count > 100000) throw RetainedCharacterRuleSourceInputs.Rejected();
                            }
                        }
                    }
                    var scopeIdentity = new P1244.ScopeIdentity(scope,
                        CharacterCreationFinalizationDigest.Compute(new { digest, Rows = effectiveRows }));
                    scopes.Add(new(scopeIdentity, Array.AsReadOnly(choices.ToArray()),
                        Array.AsReadOnly(eligible.ToArray()), Array.AsReadOnly(Array.Empty<string>())));
                }
                string probe = CharacterCreationFinalizationDigest.Compute("private-dependency-recipe1244");
                var binding = new P1244.Binding(probe, policy.ProfileInputsDigest, policy.BooksInputsDigest, probe, probe);
                var input = new P1244.Input(binding, _catalogueInputs800!, _catalogueXml800!, _profileBooks800!,
                    _enabledSourcebooks.Order(StringComparer.Ordinal).ToArray(), scopes.Select(s => s.Identity).ToArray(), scopes);
                if (!P1244.TryExecute(input, out var selection, out _) || selection is null || _sourceInputs.HasSourceDrift)
                    throw RetainedCharacterRuleSourceInputs.Rejected();
                return _essenceDependencies1244 = new(policy.PolicyDigest, policy.ProfileInputsDigest,
                    policy.BooksInputsDigest, selection, Array.AsReadOnly(scopes.ToArray()), Array.AsReadOnly(inventory.ToArray()));
            }
            finally { _readingSourcebooks800 = previous; }
        }
    }
}

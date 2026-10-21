using System.Text;
using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Application.Content;
using Chummer.Contracts.Characters;
using P = Chummer.Application.Characters.CharacterCreationSourcebookSelectionProjection;

namespace Chummer.Infrastructure.Xml;

public sealed partial class FileSystemCharacterSourceDataResolver
{
    private bool _sourcebooks800;
    private static readonly byte[] SourcebookEnvelope800 = Encoding.ASCII.GetBytes("CHMR-SOURCEBOOKS-800\0");

    public bool TryCaptureSourcebookRuleSources(string characterXml, out CharacterCreationRuleSourceCapture? capture)
    {
        using var reads = CreateValidatedReadScope();
        return CaptureSourcebookRuleSources800(reads, characterXml, out capture);
    }

    private static bool CaptureSourcebookRuleSources800(ValidatedReadScope reads, string characterXml,
        out CharacterCreationRuleSourceCapture? capture)
    {
        capture = null;
        try
        {
            RetainedCharacterRuleSourceInputs retained;
            using (var operation = new SynchronousOperationScope(reads))
            {
                if (operation.TryCreateContext(characterXml, captureBounded: true) is not SourceDataContext context)
                    return false;
                reads.ValidateOperationBoundary([context]);
                if (!context.TryCaptureSourcebookInputs800(characterXml, out var original)
                    || original is null || !original.TryCreatePortable(out var portable) || portable is null)
                    return false;
                retained = portable;
                reads.ValidateOperationBoundary([context]);
            }
            // Publication is after the original live scope's exit fence.
            if (!VerifySourcebookInputs800(retained, out var authority)) return false;
            capture = new SourcebookCapture800(retained, authority!);
            return true;
        }
        catch (Exception error) when (IsCaptureRejection(error)) { return false; }
    }

    private static bool VerifySourcebookInputs800(RetainedCharacterRuleSourceInputs inputs,
        out CharacterCreationSourcebookSelectionAuthority? authority)
    {
        authority = null;
        if (!inputs.IsPortable) return false;
        var resolver = CreateRetainedResolver(inputs);
        using var reads = resolver.CreateValidatedReadScope();
        using var operation = reads.CreateOperationScope();
        if (operation.TryCreateContext(inputs.CharacterXml) is not SourceDataContext context
            || !context.MatchesRetainedSelection(inputs)
            || !context.TryResolveCreationEffectivePolicy(out var policy) || policy?.PolicyDigest != inputs.PolicyDigest
            || !context.TryCaptureSourcebookInputs800(inputs.CharacterXml, out var observed) || observed is null
            || !observed.ToPortable().Export().AsSpan().SequenceEqual(inputs.Export())) return false;
        authority = context.SourcebookAuthority800();
        return authority.IsValid();
    }

    // Decode the retained inputs without changing their externally committed byte identity.
    // Both private carrier boundaries use this before the full capture replay validation.
    internal static RetainedCharacterRuleSourceInputs ImportCaptureInputs(ReadOnlySpan<byte> bytes, string digest)
    {
        if (!bytes.StartsWith(SourcebookEnvelope800))
            return RetainedCharacterRuleSourceInputs.Import(bytes, digest);
        if (bytes.Length > RetainedCharacterRuleSourceInputs.MaximumArchiveBytes
            || RetainedCharacterRuleSourceInputs.Digest(bytes) != digest)
            throw RetainedCharacterRuleSourceInputs.Rejected();
        var payload = bytes[SourcebookEnvelope800.Length..];
        return RetainedCharacterRuleSourceInputs.Import(payload, RetainedCharacterRuleSourceInputs.Digest(payload));
    }

    private static bool RestoreSourcebooks800(ReadOnlySpan<byte> bytes, string digest,
        out CharacterCreationRuleSourceCapture? capture)
    {
        capture = null;
        try
        {
            if (!bytes.StartsWith(SourcebookEnvelope800)) return false;
            var inputs = ImportCaptureInputs(bytes, digest);
            if (!VerifySourcebookInputs800(inputs, out var authority)) return false;
            var restored = new SourcebookCapture800(inputs, authority!);
            if (!restored.ExportPrivateInputs().AsSpan().SequenceEqual(bytes)) return false;
            capture = restored;
            return true;
        }
        catch (Exception error) when (IsCaptureRejection(error)) { return false; }
    }

    private sealed class SourcebookCapture800 : CharacterCreationRuleSourceCapture
    {
        private readonly RetainedCharacterRuleSourceInputs inputs;
        private readonly byte[] archive;
        public SourcebookCapture800(RetainedCharacterRuleSourceInputs inputs,
            CharacterCreationSourcebookSelectionAuthority authority)
        {
            this.inputs = inputs;
            SourcebookSelection = authority;
            byte[] payload = inputs.Export();
            if (payload.Length > RetainedCharacterRuleSourceInputs.MaximumArchiveBytes - SourcebookEnvelope800.Length)
                throw RetainedCharacterRuleSourceInputs.Rejected();
            archive = new byte[payload.Length + SourcebookEnvelope800.Length];
            SourcebookEnvelope800.CopyTo(archive, 0);
            payload.CopyTo(archive, SourcebookEnvelope800.Length);
            IntegrityDigest = RetainedCharacterRuleSourceInputs.Digest(archive);
        }
        public override string IntegrityDigest { get; }
        public override string EffectivePolicyDigest => inputs.PolicyDigest;
        public override CharacterCreationSourcebookSelectionAuthority SourcebookSelection { get; }
        public override byte[] ExportPrivateInputs() => archive.ToArray();
        public override bool MatchesOriginalCharacter(string characterXml) => inputs.CharacterXml == characterXml;
        // Existing original/history comparisons retain the original resolver semantics.
        // Explicit current/selected execution obtains its filtered resolver below.
        public override ICharacterSourceDataResolver CreateReplayResolver() => CreateRetainedResolver(inputs);
        public override CharacterCreationRuleSourceResolution CreateExecutionResolution() => new RuleSourceAccounting(inputs, IntegrityDigest, sourcebooks800: true);
        public override CharacterCreationRuleSourceResolution? CreateExecutionResolution(string? schema)
            => schema == CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV4
                ? new RuleSourceAccounting(inputs, IntegrityDigest, sourcebooks800: true, sourceExecutionSchema: schema)
                : base.CreateExecutionResolution(schema);
        public override string ToString() => nameof(CharacterCreationRuleSourceCapture);
    }

    private sealed partial class RuleSourceAccounting
    {
        private readonly bool sourcebookSelection800;

        public override bool EnableCandidateSourcebookSelection()
        {
            if (!sourcebookSelection800) return false;
            resolver._sourcebooks800 = true;
            return true;
        }

        public override P.Result? ConsumeSourcebookSelection(CharacterCreationCompletionReviewInputs current,
            CharacterCreationCompletionReviewRequest request)
        {
            if (!sourcebookSelection800 || current.SourcebookSelection is null
                || current.SourcebookSelection != request.SourcebookSelection
                || current.SnapshotDigest != CharacterCreationCompletionReviewRules.ComputeInputsDigest(current)
                || current.SnapshotDigest != request.InputsSnapshotDigest
                || current.RuleSourceCaptureDigest != request.RuleSourceCaptureDigest) return null;
            using var operation = resolver.CreateOperationScope();
            if (operation.TryCreateContext(inputs.CharacterXml) is not SourceDataContext context
                || current.EffectivePolicy is not { } policy || policy.BooksInputsDigest is null) return null;
            var binding = new P.Binding(current.SnapshotDigest, policy.ProfileInputsDigest, policy.BooksInputsDigest,
                request.RuleSourceCaptureDigest!, CharacterCreationFinalizationDigest.Compute(request.Selections));
            // An independent invocation rebuilds the original catalogue and every scope,
            // performs the same source gates used by target consumers, then asks the
            // separate 793 kernel to compare those actual decisions with policy membership.
            var projection = context.ProjectSourcebooks800(binding);
            return context.SourcebookAuthority800() == current.SourcebookSelection ? projection : null;
        }
    }

    private sealed partial class SourceDataContext
    {
        private bool _readingSourcebooks800;
        private string? _catalogueXml800;
        private string? _catalogueInputs800;
        private string? _profileBooks800;
        private HashSet<string>? _catalogueCodes800;
        private P.Scope[]? _sourcebookInventory800;

        private static InvalidOperationException SourcebooksRejected800()
            => new(CharacterCreationSourcebookSelectionAuthority.Invalid);

        private void ReadCatalogue800()
        {
            if (_catalogueCodes800 is not null) return;
            bool previous = _readingSourcebooks800;
            _readingSourcebooks800 = true;
            try
            {
                ValidateCatalogueContributors800();
                if (!TryLoadEffectiveDocument(_catalog, "books.xml", out var raw) || raw?.Root is not { } root
                    || root.Name != "chummer" || root.Elements("books").Count() != 1
                    || root.Elements().Any(node => node.Name != "books")
                    || root.Element("books")!.HasAttributes
                    || root.Element("books")!.Nodes().Any(node => node is XText text && !string.IsNullOrWhiteSpace(text.Value))
                    || root.Attributes().Any(attribute => !attribute.IsNamespaceDeclaration
                        && attribute.Name != XName.Get("schemaLocation", "http://www.w3.org/2001/XMLSchema-instance"))
                    || !TryEnumerateTargets("books.xml", ["books"], "book", out var books)
                    || !TryComputeEffectiveInputDigest(_catalog, "books.xml", out var baseDigest)
                    || !TryResolveTarget(SettingsFile, ["settings"], "setting", _settingsProfileId, string.Empty, out var profile)
                    || profile is null || profile.Elements("books").Count() != 1)
                    throw SourcebooksRejected800();
                // Raw duplicates cannot disappear through target-locator normalization.
                ValidateCatalogueRows800(root.Element("books")!.Elements().ToArray());
                ValidateCatalogueRows800(books);
                _catalogueXml800 = new XElement("chummer", new XElement("books", books.Select(row => new XElement(row))))
                    .ToString(SaveOptions.DisableFormatting);
                _catalogueInputs800 = CharacterCreationFinalizationDigest.Compute(new
                    { Base = baseDigest, Selected = _selectedCustomDataInputsDigest, Catalogue = _catalogueXml800 });
                _profileBooks800 = profile.Element("books")!.ToString(SaveOptions.DisableFormatting);
                _catalogueCodes800 = books.Select(row => row.Element("code")!.Value).ToHashSet(StringComparer.OrdinalIgnoreCase);
                // Use the reused parser to validate the EXACT profile book list before
                // the resolver's case-insensitive set can hide duplicates or whitespace.
                string digest = CharacterCreationFinalizationDigest.Compute("sourcebooks800-admission");
                var probe = new P.Input(new(digest, digest, CharacterCreationFinalizationDigest.Compute(_profileBooks800), digest, digest),
                    _catalogueInputs800, _catalogueXml800, _profileBooks800, _enabledSourcebooks.Order(StringComparer.Ordinal).ToArray(),
                    [new("catalogue", digest)], [new(new("catalogue", digest), [], [], [])]);
                if (!P.TryExecute(probe, out _, out _)) throw SourcebooksRejected800();
            }
            finally { _readingSourcebooks800 = previous; }
        }

        private void ValidateCatalogueContributors800()
        {
            void Validate(string path, bool complete)
            {
                if (!SourceFileExists(path)) return;
                if (!TryLoadXml(path, out var document) || document?.Root is null) throw SourcebooksRejected800();
                var containers = document.Root.Elements("books").ToArray();
                if (containers.Length != 1) throw SourcebooksRejected800();
                var rows = containers[0].Elements().ToArray();
                if (complete) { ValidateCatalogueRows800(rows); return; }
                // Amend fragments may select by name/id and omit code. They still must
                // not hide duplicate catalogue identities in a single contribution.
                foreach (string field in new[] { "id", "code" })
                {
                    if (rows.Any(row => row.Name != "book" || row.Elements(field).Count() > 1)) throw SourcebooksRejected800();
                    var values = rows.Select(row => row.Element(field)?.Value).Where(value => value is not null).ToArray();
                    if (values.Distinct(StringComparer.OrdinalIgnoreCase).Count() != values.Length) throw SourcebooksRejected800();
                }
            }
            Validate(Path.Combine(_catalog.BaseDataPath, "books.xml"), complete: true);
            foreach (var pack in _catalog.Overlays.Where(pack => pack.Enabled)
                .OrderBy(pack => pack.Priority).ThenBy(pack => pack.Id, StringComparer.Ordinal))
            {
                if (string.IsNullOrWhiteSpace(pack.DataPath) || !SourceDirectoryExists(pack.DataPath)) continue;
                if (pack.Mode == ContentOverlayModes.ReplaceFile) Validate(Path.Combine(pack.DataPath, "books.xml"), complete: true);
                else if (pack.Mode == ContentOverlayModes.MergeCatalog)
                {
                    foreach (var path in EnumerateSourceFiles(pack.DataPath, "*.xml", SearchOption.TopDirectoryOnly)
                        .OrderBy(SourcePathOrderKey, StringComparer.Ordinal))
                        if (string.Equals(ResolveCatalogTargetFileName(Path.GetFileName(path)), "books.xml", StringComparison.OrdinalIgnoreCase))
                            Validate(path, complete: false);
                }
                else throw SourcebooksRejected800();
            }
            foreach (var directory in _customDirectories)
            foreach (var path in EnumerateSourceFiles(directory.Path, "*_books.xml", SearchOption.AllDirectories))
                if (new[] { "override_", "custom_", "amend_" }.Any(prefix => Path.GetFileName(path).StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                    Validate(path, complete: false);
        }

        private static void ValidateCatalogueRows800(IReadOnlyList<XElement> rows)
        {
            if (rows.Count is 0 or > 4096) throw SourcebooksRejected800();
            var ids = new HashSet<Guid>();
            var codes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in rows)
            {
                if (row.Name != "book" || row.HasAttributes || row.Elements("id").Count() != 1
                    || row.Elements("code").Count() != 1 || row.Element("id")!.HasElements || row.Element("code")!.HasElements
                    || row.Element("id")!.HasAttributes || row.Element("code")!.HasAttributes
                    || !Guid.TryParseExact(row.Element("id")!.Value, "D", out var id) || id == Guid.Empty
                    || !ids.Add(id)
                    || row.Element("code")!.Value is not { Length: >= 1 and <= 32 } code
                    || code.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-') || !codes.Add(code))
                    throw SourcebooksRejected800();
            }
        }

        private (string Book, P.SourceRule Rule, bool Eligible) SourceGate800(string file, XElement row, bool skillRow = false)
        {
            ReadCatalogue800();
            var sources = row.Elements("source").Take(2).ToArray();
            if (sources.Length > 1 || sources.Any(node => node.HasElements || node.HasAttributes)) throw SourcebooksRejected800();
            string book = sources.Length == 0 ? string.Empty : sources[0].Value.Trim();
            bool ignore = false;
            // Only the existing skill-row parser owns this exception; other XML nodes
            // cannot gain it merely by carrying the same element name.
            if (file == "skills.xml" && skillRow && row.Name == "skill"
                && !TryReadOptionalStrictBoolean(row, "ignoresourcedisabled", false, out ignore))
                throw SourcebooksRejected800();
            if (book.Length != 0 && !_catalogueCodes800!.Contains(book)) throw SourcebooksRejected800();
            var rule = book.Length == 0 ? P.SourceRule.BuiltIn : ignore ? P.SourceRule.IgnoreDisabled : P.SourceRule.Required;
            // Keep exactly IsEnabledSource's source-less and empty-selection semantics.
            return (book, rule, ignore || IsEnabledSource(book));
        }

        private bool FilterSourcebookTarget800(string file, ref XElement? target, bool skillRow = false)
        {
            if (!_sourceInputs.Sourcebooks800 || _readingSourcebooks800 || target is null
                || file == SettingsFile || file == "books.xml") return true;
            if (!SourceGate800(file, target, skillRow).Eligible) { target = null; return true; }
            foreach (var node in target.Descendants().Where(node => node.Element("source") is not null).ToArray())
                if (!SourceGate800(file, node).Eligible) node.Remove();
            return true;
        }

        private bool FilterSourcebookTargets800(string file, ref XElement[] targets, bool skillRows = false)
        {
            if (!_sourceInputs.Sourcebooks800 || _readingSourcebooks800 || file == SettingsFile || file == "books.xml") return true;
            // Skills must retain disabled rows until ProjectSkills performs its duplicate,
            // malformed-row and skip-accounting checks. That parser calls the same source
            // membership predicate, with its original strict ignoresourcedisabled branch.
            if (file == "skills.xml")
            {
                foreach (var row in targets) _ = SourceGate800(file, row, skillRows);
                return true;
            }
            var allowed = new List<XElement>();
            foreach (var row in targets)
            {
                XElement? target = row;
                if (!FilterSourcebookTarget800(file, ref target)) return false;
                if (target is not null) allowed.Add(target);
            }
            targets = allowed.ToArray();
            return true;
        }

        private P.Scope[] ReadSourcebookInventory800()
        {
            if (_sourcebookInventory800 is not null) return _sourcebookInventory800;
            ReadCatalogue800();
            bool previous = _readingSourcebooks800;
            _readingSourcebooks800 = true;
            try
            {
                var scopes = new List<P.Scope>();
                // Exhaustiveness comes from the original policy's fixed source recipes,
                // plus the quality-level dependency actually read by its consumers.
                foreach (string scope in CharacterCreationEffectivePolicyContext.RulesScopes.Where(scope => scope != "profile")
                    .Append("qualitylevels"))
                {
                    string file = scope + ".xml";
                    if (!TryLoadEffectiveDocument(_catalog, file, out var document) || document?.Root is null
                        || !TryComputeEffectiveInputDigest(_catalog, file, out var digest)) throw SourcebooksRejected800();
                    ValidateFilterShape800(document.Root);
                    var shapes = document.Root.Elements().SelectMany(container => container.Elements()
                        .Where(row => row.Element("id") is not null || row.Element("name") is not null || row.Element("source") is not null)
                        .Select(row => (Container: container.Name.LocalName, Entry: row.Name.LocalName))).ToHashSet();
                    // Selected amendments may introduce new entry families. Their bytes and
                    // membership are captured by the same source snapshot as ordinary targets.
                    foreach (var directory in _customDirectories)
                    foreach (var path in EnumerateSourceFiles(directory.Path, "*_" + file, SearchOption.AllDirectories))
                    {
                        string name = Path.GetFileName(path);
                        if (!new[] { "override_", "custom_", "amend_" }.Any(prefix => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))) continue;
                        if (!TryLoadXml(path, out var custom) || custom?.Root is null) throw SourcebooksRejected800();
                        ValidateFilterShape800(custom.Root);
                        foreach (var container in custom.Root.Elements())
                        foreach (var row in container.Elements().Where(row => row.Element("id") is not null || row.Element("name") is not null || row.Element("source") is not null))
                            shapes.Add((container.Name.LocalName, row.Name.LocalName));
                    }
                    var choices = new List<P.Choice>();
                    var eligible = new List<string>();
                    var effectiveRows = new List<string>();
                    foreach (var shape in shapes.OrderBy(row => row.Container, StringComparer.Ordinal).ThenBy(row => row.Entry, StringComparer.Ordinal))
                    {
                        if (!TryEnumerateTargets(file, [shape.Container], shape.Entry, out var rows)) throw SourcebooksRejected800();
                        for (int index = 0; index < rows.Length; index++)
                        {
                            string xml = rows[index].ToString(SaveOptions.DisableFormatting);
                            effectiveRows.Add(xml);
                            int nodeIndex = 0;
                            foreach (var row in rows[index].DescendantsAndSelf().Where(row => row == rows[index] || row.Element("source") is not null))
                            {
                                var gate = SourceGate800(file, row, row == rows[index] && shape.Entry == "skill"
                                    && shape.Container is "skills" or "knowledgeskills");
                                string nodeDigest = CharacterCreationFinalizationDigest.Compute(row.ToString(SaveOptions.DisableFormatting));
                                string identity = CharacterCreationFinalizationDigest.Compute(new { scope, shape.Container, shape.Entry, index, Node = nodeIndex++, nodeDigest });
                                choices.Add(new(identity, nodeDigest, gate.Book, gate.Rule));
                                if (gate.Eligible) eligible.Add(identity);
                                if (choices.Count > 100000) throw SourcebooksRejected800();
                            }
                        }
                    }
                    var identityScope = new P.ScopeIdentity(scope, CharacterCreationFinalizationDigest.Compute(new { digest, Rows = effectiveRows }));
                    scopes.Add(new(identityScope, choices.ToArray(), eligible.ToArray(), []));
                }
                return _sourcebookInventory800 = scopes.ToArray();
            }
            finally { _readingSourcebooks800 = previous; }
        }

        private static void ValidateFilterShape800(XElement root)
        {
            // The supported catalogue shape is root/container/entry. Do not silently
            // omit a new source-bearing wrapper that target enumeration cannot address.
            if (root.Element("source") is not null) throw SourcebooksRejected800();
            foreach (var container in root.Elements())
            {
                if (container.Element("source") is not null) throw SourcebooksRejected800();
                foreach (var row in container.Elements())
                    if (row.Descendants("source").Any() && row.Element("id") is null
                        && row.Element("name") is null && row.Element("source") is null)
                        throw SourcebooksRejected800();
            }
        }

        internal CharacterCreationSourcebookSelectionAuthority SourcebookAuthority800()
        {
            var scopes = ReadSourcebookInventory800();
            return new(CharacterCreationSourcebookSelectionAuthority.SchemaV1, _catalogueInputs800!,
                CharacterCreationFinalizationDigest.Compute(_catalogueXml800!),
                CharacterCreationFinalizationDigest.Compute(scopes.Select(scope => new { scope.Identity, scope.Choices }).ToArray()));
        }

        internal P.Result ProjectSourcebooks800(P.Binding binding)
        {
            using var entered = _sourceInputs.Enter();
            var scopes = ReadSourcebookInventory800();
            var input = new P.Input(binding, _catalogueInputs800!, _catalogueXml800!, _profileBooks800!,
                _enabledSourcebooks.Order(StringComparer.Ordinal).ToArray(), scopes.Select(scope => scope.Identity).ToArray(), scopes);
            if (!P.TryExecute(input, out var result, out _) || result is null) throw SourcebooksRejected800();
            return result;
        }

        internal bool TryCaptureSourcebookInputs800(string characterXml, out RetainedCharacterRuleSourceInputs? retained)
        {
            retained = null;
            using var entered = _sourceInputs.Enter();
            // The legacy method is unchanged. This version adds catalogue and effective
            // filter dependencies, then exports the enlarged observation in a NEW envelope.
            if (!TryCaptureInputs(characterXml, out _) || !TryResolveCreationEffectivePolicy(out var policy) || policy is null)
                return false;
            var authority = SourcebookAuthority800();
            string probe = CharacterCreationFinalizationDigest.Compute("sourcebooks800-capture");
            _ = ProjectSourcebooks800(new(probe, policy.ProfileInputsDigest, policy.BooksInputsDigest!, probe, probe));
            if (!authority.IsValid() || !_sourceInputs.TryAdmitReuse(_catalog)) return false;
            retained = _sourceInputs.Export(_catalog, characterXml, policy.PolicyDigest, _settingsProfileId, RetainedSelection());
            return !_sourceInputs.HasSourceDrift;
        }
    }
}

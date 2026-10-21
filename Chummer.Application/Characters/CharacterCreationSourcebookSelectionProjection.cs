using System.Xml;
using System.Xml.Linq;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

/// <summary>Deterministic selection kernel shared by the retained producer and
/// ordinary/selected consumers. Inputs are reconstructed by the resolver; the
/// serializable result alone is never execution authority.</summary>
public static class CharacterCreationSourcebookSelectionProjection
{
    public const string Schema = "chummer.creation-sourcebook-selection.v1";
    private const int MaximumBooks = 4096;
    private const int MaximumChoices = 100000;
    private const int MaximumXmlCharacters = 4 * 1024 * 1024;

    public enum Failure
    {
        None, InvalidBinding, InvalidCatalogue, InvalidSelection, UnknownBook,
        UncapturedScope, InvalidChoice, ProjectionMismatch, SelectedChoiceUnavailable
    }

    // These are consumer semantics, never options accepted from a candidate/provider.
    // BuiltIn represents an explicitly admitted source-less rule. IgnoreDisabled
    // represents the existing skill parser's ignoresourcedisabled branch only.
    public enum SourceRule { Required, BuiltIn, IgnoreDisabled }

    public sealed record Binding(string InputsSnapshotDigest, string ProfileInputsDigest,
        string BooksInputsDigest, string RuleSourceCaptureDigest, string SelectionsDigest);
    public sealed record ScopeIdentity(string Scope, string InputsDigest);
    public sealed record Choice(string IdentityDigest, string SourceNodeDigest,
        string SourceBook, SourceRule Rule);
    public sealed record Scope(ScopeIdentity Identity, IReadOnlyList<Choice> Choices,
        IReadOnlyList<string> ActualEligibleChoiceIds, IReadOnlyList<string> SelectedChoiceIds);
    public sealed record Input(Binding Binding, string CatalogueInputsDigest,
        string CatalogueXml, string ProfileBooksXml, IReadOnlyList<string> EnabledSourcebooks,
        IReadOnlyList<ScopeIdentity> RequiredScopes, IReadOnlyList<Scope> Scopes);
    public sealed record Book(string Code, string Id);
    public sealed record Decision(string IdentityDigest, string SourceNodeDigest,
        string SourceBook, SourceRule Rule, bool Eligible, bool Selected);
    public sealed record ScopeProjection(ScopeIdentity Identity, IReadOnlyList<Decision> Decisions);
    public sealed record Result(string Schema, Binding Binding, string CatalogueInputsDigest,
        string CatalogueXmlDigest, IReadOnlyList<Book> EnabledBooks,
        IReadOnlyList<ScopeProjection> Scopes, string SelectionDigest, string ProjectionDigest);

    public static bool TryExecute(Input input, out Result? result, out Failure failure)
    {
        result = null;
        failure = Failure.InvalidBinding;
        if (input?.Binding is not { } binding
            || !Digest(binding.InputsSnapshotDigest) || !Digest(binding.ProfileInputsDigest)
            || !Digest(binding.BooksInputsDigest) || !Digest(binding.RuleSourceCaptureDigest)
            || !Digest(binding.SelectionsDigest) || !Digest(input.CatalogueInputsDigest)) return false;
        failure = Failure.InvalidCatalogue;
        if (!TryXml(input.CatalogueXml, out var catalogue)
            || catalogue!.Name != "chummer"
            || catalogue.Elements("books").Count() != 1) return false;
        var containers = catalogue.Elements("books").Single();
        if (!Container(containers, "book")) return false;
        var catalogueBooks = containers.Elements().ToArray();
        if (catalogueBooks.Length is 0 or > MaximumBooks) return false;
        var books = new Dictionary<string, Book>(StringComparer.OrdinalIgnoreCase);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in catalogueBooks)
        {
            if (row.HasAttributes || !Scalar(row, "code", out var code) || !Code(code)
                || !Scalar(row, "id", out var id) || !Guid.TryParseExact(id, "D", out var parsed)
                || parsed == Guid.Empty
                || !ids.Add(parsed.ToString("D")) || !books.TryAdd(code, new(code, parsed.ToString("D")))) return false;
        }

        failure = Failure.InvalidSelection;
        if (!TryXml(input.ProfileBooksXml, out var selection) || selection!.Name != "books"
            || !Container(selection, "book") || input.EnabledSourcebooks is null
            || Hash(selection.ToString(SaveOptions.DisableFormatting)) != binding.BooksInputsDigest)
            return false;
        var enabled = selection.Elements().Select(node => node.Value).ToArray();
        // Match IsEnabledSource: an empty set enables no named sources; explicitly
        // admitted source-less rows still pass. All-books means an enumerated set.
        if (enabled.Length > 256 || selection.Elements().Any(node => node.HasAttributes || node.HasElements
                || node.Nodes().Any(item => item is not XText))
            || enabled.Any(code => !Code(code))
            || enabled.Distinct(StringComparer.OrdinalIgnoreCase).Count() != enabled.Length
            || !enabled.Order(StringComparer.Ordinal).SequenceEqual(input.EnabledSourcebooks)) return false;
        failure = Failure.UnknownBook;
        if (enabled.Any(code => !books.ContainsKey(code))) return false;
        var enabledSet = enabled.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var enabledBooks = enabled.Order(StringComparer.Ordinal)
            .Select(code => new Book(code, books[code].Id)).ToArray();

        failure = Failure.UncapturedScope;
        if (input.RequiredScopes is null || input.Scopes is null
            || input.RequiredScopes.Count is 0 or > 256 || input.Scopes.Count != input.RequiredScopes.Count
            || input.RequiredScopes.Any(scope => scope is null || !ScopeCode(scope.Scope) || !Digest(scope.InputsDigest))
            || input.RequiredScopes.Select(scope => scope.Scope).Distinct(StringComparer.Ordinal).Count() != input.RequiredScopes.Count
            || input.Scopes.Any(scope => scope?.Identity is null)
            || input.Scopes.Select(scope => scope.Identity.Scope).Distinct(StringComparer.Ordinal).Count() != input.Scopes.Count)
            return false;
        var required = input.RequiredScopes.ToDictionary(scope => scope.Scope, StringComparer.Ordinal);
        var projections = new List<ScopeProjection>();
        int count = 0;
        foreach (var scope in input.Scopes.OrderBy(scope => scope.Identity.Scope, StringComparer.Ordinal))
        {
            failure = Failure.UncapturedScope;
            if (!required.TryGetValue(scope.Identity.Scope, out var expected) || expected != scope.Identity) return false;
            failure = Failure.InvalidChoice;
            if (scope.Choices is null || scope.ActualEligibleChoiceIds is null || scope.SelectedChoiceIds is null
                || scope.Choices.Count > MaximumChoices - count
                || !UniqueDigests(scope.ActualEligibleChoiceIds) || !UniqueDigests(scope.SelectedChoiceIds)) return false;
            count += scope.Choices.Count;
            var choiceIds = new HashSet<string>(StringComparer.Ordinal);
            var decisions = new List<Decision>();
            foreach (var choice in scope.Choices)
            {
                if (choice is null || !Digest(choice.IdentityDigest) || !Digest(choice.SourceNodeDigest)
                    || !choiceIds.Add(choice.IdentityDigest) || !Enum.IsDefined(choice.Rule)
                    || (choice.Rule == SourceRule.BuiltIn ? choice.SourceBook != string.Empty : !Code(choice.SourceBook))
                    || choice.Rule == SourceRule.IgnoreDisabled && scope.Identity.Scope != "skills") return false;
                failure = Failure.UnknownBook;
                if (choice.Rule != SourceRule.BuiltIn && !books.ContainsKey(choice.SourceBook)) return false;
                bool eligible = choice.Rule is SourceRule.BuiltIn or SourceRule.IgnoreDisabled
                    || enabledSet.Contains(choice.SourceBook);
                decisions.Add(new(choice.IdentityDigest, choice.SourceNodeDigest, choice.SourceBook, choice.Rule,
                    eligible, false));
                failure = Failure.InvalidChoice;
            }
            var eligibleIds = decisions.Where(choice => choice.Eligible).Select(choice => choice.IdentityDigest)
                .ToHashSet(StringComparer.Ordinal);
            failure = Failure.ProjectionMismatch;
            if (!eligibleIds.SetEquals(scope.ActualEligibleChoiceIds)) return false;
            failure = Failure.SelectedChoiceUnavailable;
            if (scope.SelectedChoiceIds.Any(id => !eligibleIds.Contains(id))) return false;
            var selectedIds = scope.SelectedChoiceIds.ToHashSet(StringComparer.Ordinal);
            projections.Add(new(scope.Identity, Array.AsReadOnly(decisions
                .Select(choice => choice with { Selected = selectedIds.Contains(choice.IdentityDigest) })
                .OrderBy(choice => choice.IdentityDigest, StringComparer.Ordinal).ToArray())));
        }

        string catalogueXmlDigest = Hash(catalogue.ToString(SaveOptions.DisableFormatting));
        string selectionDigest = Hash(new
        {
            Schema, Binding = binding, input.CatalogueInputsDigest, CatalogueXmlDigest = catalogueXmlDigest,
            EnabledBooks = enabledBooks
        });
        string projectionDigest = Hash(new { Schema, SelectionDigest = selectionDigest, Scopes = projections });
        result = new(Schema, binding, input.CatalogueInputsDigest, catalogueXmlDigest,
            Array.AsReadOnly(enabledBooks), Array.AsReadOnly(projections.ToArray()), selectionDigest, projectionDigest);
        failure = Failure.None;
        return true;
    }

    private static string Hash(object value) => CharacterCreationFinalizationDigest.Compute(value);
    private static bool Digest(string? value) => CharacterCreationFinalizationDigest.IsCanonical(value);
    private static bool Code(string? value) => value is { Length: >= 1 and <= 32 }
        && value.All(character => char.IsAsciiLetterOrDigit(character) || character == '-');
    private static bool ScopeCode(string? value) => value is { Length: >= 1 and <= 64 }
        && value.All(character => char.IsAsciiLetterOrDigit(character) || character == '-');
    private static bool UniqueDigests(IReadOnlyList<string> values) => values.Count <= MaximumChoices
        && values.All(Digest) && values.Distinct(StringComparer.Ordinal).Count() == values.Count;
    private static bool Container(XElement node, string child) => !node.HasAttributes
        && node.Elements().All(element => element.Name == child)
        && node.Nodes().All(item => item is XElement || item is XText text && string.IsNullOrWhiteSpace(text.Value));
    private static bool Scalar(XElement node, string name, out string value)
    {
        var children = node.Elements(name).Take(2).ToArray();
        value = children.Length == 1 ? children[0].Value : string.Empty;
        return children.Length == 1 && !children[0].HasElements && !children[0].HasAttributes
            && children[0].Nodes().All(item => item is XText);
    }
    private static bool TryXml(string? source, out XElement? root)
    {
        root = null;
        if (source is null || source.Length > MaximumXmlCharacters) return false;
        try
        {
            using var input = new StringReader(source);
            using var reader = XmlReader.Create(input, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null,
                MaxCharactersInDocument = MaximumXmlCharacters
            });
            root = XDocument.Load(reader, LoadOptions.PreserveWhitespace).Root;
            return root is not null;
        }
        catch (XmlException) { return false; }
    }
}

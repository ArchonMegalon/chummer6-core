using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using D = Chummer.Application.Characters.CharacterCreationSourceOperationDisposition;

namespace Chummer.Infrastructure.Xml;

public sealed partial class FileSystemCharacterSourceDataResolver
{
    private sealed partial class SourceDataContext
    {
        private void ExecuteProfileSourcebooks935(XElement profile, CharacterCreationPrerequisiteAuthority authority,
            CharacterCreationRuleSourceScopeInvocation invocation, SourceScopeLedger ledger)
        {
            if (profile.Elements("books").ToArray() is not [var books]
                || !invocation.TryGetExecutedProfileSourcebooks935(_settingsProfileId, authority.RawProfileInputsDigest,
                    CharacterCreationFinalizationDigest.Compute(books.ToString(SaveOptions.DisableFormatting)),
                    out var projection, out var catalogue) || projection is null || catalogue is null) return;
            // Match the selected subtree and retained catalogue again, without
            // executing another projection or treating inventory as completion.
            // Unknown/duplicate/malformed children cannot hide behind the set.
            var nodes = books.Elements().ToArray();
            if (books.HasAttributes || books.Nodes().Any(node => node is not XElement
                    && !(node is XText text && string.IsNullOrWhiteSpace(text.Value)))
                || nodes.Any(node => node.Name != "book" || node.HasElements || node.HasAttributes
                    || node.Nodes().Any(child => child is not XText))
                || nodes.Select(node => node.Value).Distinct(StringComparer.OrdinalIgnoreCase).Count() != nodes.Length
                || !nodes.Select(node => node.Value).Order(StringComparer.Ordinal)
                    .SequenceEqual(projection.EnabledBooks.Select(book => book.Code))
                || SourcebookAuthority800() != catalogue || _sourceInputs.HasSourceDrift) return;
            ledger.Node(books, D.Compiled, new
            {
                Operation = "ProjectSourcebooks800", Result = projection.EnabledBooks,
                projection.SelectionDigest, projection.ProjectionDigest
            });
            foreach (var node in nodes)
                ledger.Tree(node, D.Compiled, new
                {
                    Operation = "ProjectSourcebooks800",
                    Result = projection.EnabledBooks.Single(book => book.Code == node.Value),
                    projection.SelectionDigest, projection.ProjectionDigest
                });
        }
    }
}

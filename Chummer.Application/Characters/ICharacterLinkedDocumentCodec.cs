using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

/// <summary>
/// Decodes a user-selected Chummer5 runner document without opening arbitrary saved filesystem paths.
/// </summary>
public interface ICharacterLinkedDocumentCodec
{
    bool TryDecode(
        string fileName,
        ReadOnlySpan<byte> content,
        out CharacterLinkedDocument document);
}

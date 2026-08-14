using System.Buffers;
using System.Buffers.Binary;
using System.Xml;
using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using SharpCompress.Compressors.LZMA;

namespace Chummer.Infrastructure.Xml;

public sealed class Chummer5LinkedDocumentCodec : ICharacterLinkedDocumentCodec
{
    public const int MaximumInputBytes = 8 * 1024 * 1024;
    public const int MaximumExpandedBytes = 32 * 1024 * 1024;
    private const int MaximumDictionaryBytes = 128 * 1024 * 1024;
    private const int LzmaHeaderLength = 13;

    public bool TryDecode(
        string fileName,
        ReadOnlySpan<byte> content,
        out CharacterLinkedDocument document)
    {
        document = null!;
        if (string.IsNullOrWhiteSpace(fileName)
            || content.IsEmpty
            || content.Length > MaximumInputBytes)
        {
            return false;
        }

        string extension = Path.GetExtension(fileName);
        try
        {
            byte[] xmlBytes;
            if (string.Equals(extension, ".chum5", StringComparison.OrdinalIgnoreCase))
            {
                xmlBytes = content.ToArray();
            }
            else if (string.Equals(extension, ".chum5lz", StringComparison.OrdinalIgnoreCase))
            {
                if (!TryDecompressLzma(content, out xmlBytes))
                {
                    return false;
                }
            }
            else
            {
                return false;
            }

            try
            {
                return TryParseIdentity(xmlBytes, out document);
            }
            finally
            {
                Array.Clear(xmlBytes);
            }
        }
        catch (Exception exception) when (exception is ArgumentException
                                          or EndOfStreamException
                                          or InvalidDataException
                                          or IOException
                                          or NotSupportedException
                                          or OverflowException
                                          or XmlException)
        {
            document = null!;
            return false;
        }
    }

    private static bool TryDecompressLzma(ReadOnlySpan<byte> content, out byte[] xmlBytes)
    {
        xmlBytes = [];
        if (content.Length <= LzmaHeaderLength)
        {
            return false;
        }

        byte[] properties = content[..5].ToArray();
        int dictionarySize = BinaryPrimitives.ReadInt32LittleEndian(properties.AsSpan(1));
        long declaredOutputSize = BinaryPrimitives.ReadInt64LittleEndian(content.Slice(5, 8));
        if (dictionarySize is <= 0 or > MaximumDictionaryBytes
            || declaredOutputSize < -1
            || declaredOutputSize > MaximumExpandedBytes)
        {
            return false;
        }

        byte[] compressed = content[LzmaHeaderLength..].ToArray();
        try
        {
            using MemoryStream input = new(compressed, writable: false);
            using LzmaStream decoder = LzmaStream.Create(
                properties,
                input,
                input.Length,
                declaredOutputSize,
                leaveOpen: false);
            using MemoryStream output = declaredOutputSize is > 0 and <= int.MaxValue
                ? new MemoryStream((int)declaredOutputSize)
                : new MemoryStream();
            byte[] buffer = ArrayPool<byte>.Shared.Rent(32 * 1024);
            try
            {
                int read;
                while ((read = decoder.Read(buffer, 0, buffer.Length)) > 0)
                {
                    if (output.Length + read > MaximumExpandedBytes)
                    {
                        return false;
                    }
                    output.Write(buffer, 0, read);
                }
            }
            finally
            {
                Array.Clear(buffer);
                ArrayPool<byte>.Shared.Return(buffer, clearArray: false);
            }

            if (declaredOutputSize >= 0 && output.Length != declaredOutputSize)
            {
                return false;
            }

            xmlBytes = output.ToArray();
            return xmlBytes.Length > 0;
        }
        finally
        {
            Array.Clear(properties);
            Array.Clear(compressed);
        }
    }

    private static bool TryParseIdentity(byte[] xmlBytes, out CharacterLinkedDocument document)
    {
        document = null!;
        XmlReaderSettings settings = new()
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = MaximumExpandedBytes,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true
        };
        using MemoryStream input = new(xmlBytes, writable: false);
        using XmlReader reader = XmlReader.Create(input, settings);
        XDocument parsed = XDocument.Load(reader, LoadOptions.None);
        XElement? character = parsed.Root;
        if (character is null
            || !string.Equals(character.Name.LocalName, "character", StringComparison.Ordinal)
            || character.Name.Namespace != XNamespace.None)
        {
            return false;
        }

        string name = ReadValue(character, "name");
        string alias = ReadValue(character, "alias");
        string characterName = FirstNonBlank(alias, name, "Unnamed Character");
        document = new CharacterLinkedDocument(
            CharacterName: characterName,
            Name: name,
            Alias: alias,
            Metatype: ReadValue(character, "metatype"),
            Metavariant: ReadValue(character, "metavariant"),
            Gender: FirstNonBlank(ReadValue(character, "gender"), ReadValue(character, "sex"), string.Empty),
            Age: ReadValue(character, "age"));
        return true;
    }

    private static string ReadValue(XElement element, string name)
        => element.Element(name)?.Value.Trim() ?? string.Empty;

    private static string FirstNonBlank(params string[] values)
        => values.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
}

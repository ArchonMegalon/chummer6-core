#nullable enable annotations

using System;
using System.Text;
using Chummer.Contracts.Characters;
using Chummer.Infrastructure.Xml;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public sealed class Chummer5LinkedDocumentCodecTests
{
    private const string CharacterXml =
        "<character><name>Linked Legal Name</name><alias>Linked Alias</alias>" +
        "<metatype>Elf</metatype><metavariant>Dryad</metavariant>" +
        "<gender>Female</gender><age>31</age></character>";

    private const string LegacyCompatibleLzmaBase64 =
        "XQAAgAD//////////wAeGMkGJENFsJyRWEjo3NimsnmxZ8tFSo8beFz9cRhWKBOwYEqAvrbPEO5DH/EgF5aoMzmSuL61Oh1FEBzBaxUoQMaU9OQbdZh0AyWYgmiUpK9dLCRTL7axjpVOuZzfkIZa1gP9WxtR4od9VnwR6k7ozvlUZf/g3rwA";

    [TestMethod]
    public void Decode_accepts_chum5_and_projects_legacy_link_identity()
    {
        var codec = new Chummer5LinkedDocumentCodec();

        bool decoded = codec.TryDecode(
            "linked.chum5",
            Encoding.UTF8.GetBytes(CharacterXml),
            out CharacterLinkedDocument document);

        Assert.IsTrue(decoded);
        Assert.AreEqual("Linked Alias", document.CharacterName);
        Assert.AreEqual("Linked Legal Name", document.Name);
        Assert.AreEqual("Elf (Dryad)", document.DisplayMetatype);
        Assert.AreEqual("Female", document.Gender);
        Assert.AreEqual("31", document.Age);
    }

    [TestMethod]
    public void Decode_accepts_the_legacy_lzma_alone_chum5lz_envelope()
    {
        var codec = new Chummer5LinkedDocumentCodec();

        bool decoded = codec.TryDecode(
            "linked.chum5lz",
            Convert.FromBase64String(LegacyCompatibleLzmaBase64),
            out CharacterLinkedDocument document);

        Assert.IsTrue(decoded);
        Assert.AreEqual("Linked Alias", document.CharacterName);
        Assert.AreEqual("Elf (Dryad)", document.DisplayMetatype);
    }

    [TestMethod]
    public void Decode_rejects_unsupported_malformed_and_dtd_documents()
    {
        var codec = new Chummer5LinkedDocumentCodec();

        Assert.IsFalse(codec.TryDecode("linked.zip", Encoding.UTF8.GetBytes(CharacterXml), out _));
        Assert.IsFalse(codec.TryDecode("linked.chum5lz", [1, 2, 3], out _));
        Assert.IsFalse(codec.TryDecode(
            "linked.chum5",
            Encoding.UTF8.GetBytes("<!DOCTYPE character [<!ENTITY x SYSTEM 'file:///etc/passwd'>]><character><name>&x;</name></character>"),
            out _));
        Assert.IsFalse(codec.TryDecode("linked.chum5", Encoding.UTF8.GetBytes("<not-character />"), out _));
    }
}

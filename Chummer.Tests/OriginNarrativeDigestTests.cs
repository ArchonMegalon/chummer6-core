using System.Buffers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Chummer.Application.LifeModules;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public sealed class OriginNarrativeDigestTests
{
    // Exercise the actual writer shared by the turn, chapter, choice and effect
    // digests without adding a public hashing API just for these regressions.
    private static readonly Func<Action<Utf8JsonWriter>, string> Compute =
        typeof(LifeModuleOriginDossierService).GetMethod("ComputeDigest",
            BindingFlags.Static | BindingFlags.NonPublic)!
            .CreateDelegate<Func<Action<Utf8JsonWriter>, string>>();

    [TestMethod]
    public void Narrative_digest_preserves_exact_bytes_for_small_large_and_escaped_values()
    {
        foreach (int length in new[] { 0, 1, 255, 4096, 65536, 300000, 1 })
        {
            var write = Payload(new string('x', length) + "Zoë 東京 😀 e\u0301 é <>&\"\\\r\n\t");
            string expected = Legacy(write);
            Assert.AreEqual(expected, Compute(write));
            Parallel.For(0, 4, _ => Assert.AreEqual(expected, Compute(write)));
        }
    }

    [TestMethod]
    public void Narrative_digest_does_not_retain_failed_nested_or_changed_inputs()
    {
        var values = new[] { "before" };
        Action<Utf8JsonWriter> mutable = writer => JsonSerializer.Serialize(writer, values);
        string before = Compute(mutable);
        values[0] = "after";
        Assert.AreNotEqual(before, Compute(mutable));
        Assert.AreEqual(Legacy(mutable), Compute(mutable));
        Assert.ThrowsExactly<InvalidOperationException>(() => Compute(writer =>
        {
            writer.WriteStartArray();
            writer.WriteStringValue(new string('s', 300000));
            throw new InvalidOperationException("Synthetic interrupted digest.");
        }));
        Action<Utf8JsonWriter> nested = writer =>
        {
            writer.WriteStartArray();
            writer.WriteStringValue(Compute(mutable));
            writer.WriteStringValue("outer");
            writer.WriteEndArray();
        };
        Assert.AreEqual(Legacy(nested), Compute(nested));
        Assert.AreEqual(Legacy(mutable), Compute(mutable));
    }

    [TestMethod]
    public void Narrative_digest_reuses_scratch_bytes_for_repeated_effect_validation()
    {
        var write = Payload(new string('x', 1024));
        string expected = Legacy(write);
        Assert.AreEqual(expected, Compute(write));
        long legacy = Allocations(() => Legacy(write));
        long current = Allocations(() => Compute(write));
        Console.WriteLine($"Narrative digest allocations: legacy={legacy}; current={current}.");
        Assert.IsTrue(current < legacy * 0.5,
            $"Repeated narrative digests allocated {current:N0}; original {legacy:N0}.");
        Assert.AreEqual(expected, Compute(write));
    }

    private static Action<Utf8JsonWriter> Payload(string text) => writer =>
    {
        writer.WriteStartObject();
        writer.WriteString("story", text);
        writer.WriteNumber("budgetDelta", 12.50m);
        writer.WriteBoolean("isLegal", true);
        writer.WriteNull("optional");
        writer.WriteStartArray("sourceAnchorIds");
        writer.WriteStringValue("RF:66");
        writer.WriteStringValue("SR5:100");
        writer.WriteEndArray();
        writer.WriteEndObject();
    };

    private static long Allocations(Func<string> compute)
    {
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 256; i++) GC.KeepAlive(compute());
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    // Retained original implementation: persisted receipts must not change.
    private static string Legacy(Action<Utf8JsonWriter> write)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer)) { write(writer); writer.Flush(); }
        return Convert.ToHexString(SHA256.HashData(buffer.WrittenSpan)).ToLowerInvariant();
    }
}

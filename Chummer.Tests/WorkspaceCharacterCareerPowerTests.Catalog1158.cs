using System.Diagnostics;
using System.Xml.Linq;
using Chummer.Contracts.Characters;
using Chummer.Infrastructure.Files;
using Chummer.Infrastructure.Xml;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

public sealed partial class WorkspaceCharacterCareerPowerTests
{
    [TestMethod]
    public void CareerCatalog1158_full_filesystem_catalog_matches_exact_sources_with_less_allocation()
    {
        var resolver = new FileSystemCharacterSourceDataResolver(
            new FileSystemContentOverlayCatalogService(CoreRoot(), CoreRoot(), null));
        // Both paths share the original operation fences. Nested exact reads
        // have the same validation cadence as the pre-optimization catalog.
        using var operation = resolver.CreateOperationScope();
        var context = operation.TryCreateContext(Runner());
        Assert.IsNotNull(context);
        Assert.IsTrue(context.TryResolveCareerSettingsProfile(out var profile, out _));
        var books = XElement.Parse(profile).Element("books")!.Elements("book")
            .Select(node => node.Value.Trim()).ToHashSet(StringComparer.Ordinal);
        var all = XDocument.Load(Path.Combine(CoreRoot(), "Chummer", "data", "powers.xml"))
            .Root!.Element("powers")!.Elements("power").ToArray();
        Assert.IsTrue(all.Length > 0);
        // Every enabled source from the real file, without a name/ID allowlist.
        var ids = all.Where(row => books.Contains(row.Element("source")!.Value))
            .Select(row => Guid.Parse(row.Element("id")!.Value)).OrderBy(id => id).ToArray();
        Assert.IsTrue(ids.Length > 0);
        Assert.IsTrue(context.TryResolveCareerPowerCatalog(out var warm));
        CollectionAssert.AreEqual(ids, warm.Select(row => row.SourceId).ToArray());

        long referenceBefore = GC.GetAllocatedBytesForCurrentThread();
        long referenceStart = Stopwatch.GetTimestamp();
        var reference = new List<CharacterCareerPowerSource>();
        foreach (Guid id in ids)
        {
            Assert.IsTrue(context.TryResolveCareerPowerSource(id, out var exact));
            Assert.IsNotNull(exact);
            reference.Add(exact);
        }
        TimeSpan referenceTime = Stopwatch.GetElapsedTime(referenceStart);
        long referenceBytes = GC.GetAllocatedBytesForCurrentThread() - referenceBefore;

        long catalogBefore = GC.GetAllocatedBytesForCurrentThread();
        long catalogStart = Stopwatch.GetTimestamp();
        Assert.IsTrue(context.TryResolveCareerPowerCatalog(out var actual));
        TimeSpan catalogTime = Stopwatch.GetElapsedTime(catalogStart);
        long catalogBytes = GC.GetAllocatedBytesForCurrentThread() - catalogBefore;

        CollectionAssert.AreEqual(reference.ToArray(), warm.ToArray());
        CollectionAssert.AreEqual(reference.ToArray(), actual.ToArray());
        Assert.IsTrue(catalogBytes < referenceBytes,
            $"Catalog allocated {catalogBytes} bytes; exact source loop allocated {referenceBytes} bytes.");
        Console.WriteLine($"CareerCatalog1158 rows={actual.Count}; exact bytes={referenceBytes}, ms={referenceTime.TotalMilliseconds:F3}; "
            + $"catalog bytes={catalogBytes}, ms={catalogTime.TotalMilliseconds:F3}. No wall-clock threshold.");
    }
}

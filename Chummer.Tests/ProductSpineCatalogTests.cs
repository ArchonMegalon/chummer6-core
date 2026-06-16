using System.Collections.Generic;
using System.Linq;
using Chummer.Contracts.Product;
using Chummer.Contracts.Receipts;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public sealed class ProductSpineCatalogTests
{
    [TestMethod]
    public void Product_spine_catalog_keeps_unique_horizon_ids_and_karma_forge_targets()
    {
        IReadOnlyList<DesktopHorizonWorkbenchEntry> entries = ProductSpineCatalog.ListDesktopHorizons();
        IReadOnlyList<DesktopHorizonRouteOption> targets = ProductSpineCatalog.ListKarmaForgeTargets();

        Assert.IsTrue(entries.Count >= 18);
        Assert.AreEqual(entries.Count, entries.Select(static entry => entry.Id).Distinct().Count());
        CollectionAssert.AreEqual(
            new[] { "karma_forge_packages", "karma_forge_account_packages", "karma_forge_intake" },
            targets.Select(static target => target.Id).ToArray());
        Assert.IsTrue(entries.Any(static entry => entry.Id == "alice" && entry.NativeActions!.Any(static action => action.Id == "ready_for_tonight")));
        Assert.IsTrue(entries.Any(static entry => entry.Id == "runbook_press" && entry.NativeActions!.Any(static action => action.Id == "publication")));
    }

    [TestMethod]
    public void Receipt_envelope_factory_projects_runtime_and_external_webhook_defaults()
    {
        ReceiptEnvelope runtime = ReceiptEnvelopeFactory.Runtime(
            receiptKind: "community_contribution",
            ownerScope: "community.user",
            exposureClass: ReceiptExposureClasses.SignedIn,
            evidenceRef: "rcpt-1",
            reviewState: "verified");
        ReceiptEnvelope webhook = ReceiptEnvelopeFactory.ExternalWebhook(
            receiptKind: "billing_payment",
            ownerScope: "billing.account",
            evidenceRef: "evt-1");

        Assert.AreEqual(ReceiptProvenanceClasses.Runtime, runtime.ProvenanceClass);
        Assert.AreEqual(ReceiptExposureClasses.SignedIn, runtime.ExposureClass);
        Assert.AreEqual(ReceiptLifecycleStates.Verified, runtime.LifecycleState);
        Assert.AreEqual("rcpt-1", runtime.EvidenceRef);
        Assert.AreEqual("verified", runtime.ReviewState);
        Assert.IsTrue(runtime.Reproducible);

        Assert.AreEqual(ReceiptProvenanceClasses.ExternalWebhook, webhook.ProvenanceClass);
        Assert.AreEqual(ReceiptExposureClasses.SignedIn, webhook.ExposureClass);
        Assert.AreEqual(ReceiptLifecycleStates.Verified, webhook.LifecycleState);
        Assert.AreEqual("evt-1", webhook.EvidenceRef);
    }
}

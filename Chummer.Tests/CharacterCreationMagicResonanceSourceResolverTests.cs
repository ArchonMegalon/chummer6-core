#nullable enable annotations

using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Chummer.Infrastructure.Files;
using Chummer.Infrastructure.Xml;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public sealed class CharacterCreationMagicResonanceSourceResolverTests
{
    private const string StandardPrioritySettingsId = "223a11ff-80e0-428b-89a9-6ef1c243b8b6";

    [TestMethod]
    public void Canonical_standard_priority_profile_projects_digest_bound_magic_resonance_authority()
    {
        string root = FindCoreRoot();
        var overlays = new FileSystemContentOverlayCatalogService(root, root, null);
        var resolver = new FileSystemCharacterSourceDataResolver(overlays);
        ICharacterSourceDataContext context = resolver.TryCreateContext(
            $"<character><settings>{StandardPrioritySettingsId}</settings></character>")!;

        Assert.IsNotNull(context);
        Assert.IsTrue(context.TryResolveCreationMagicResonanceAuthority(
            out CharacterCreationMagicResonanceAuthority authority));
        Assert.IsTrue(authority.IsAuthoritative, string.Join(",", authority.Blockers));
        Assert.IsTrue(CharacterCreationMagicResonanceDraftIntegrity.IsValidAuthority(authority));
        Assert.IsTrue(CharacterCreationMagicResonanceDigest.IsCanonical(authority.AuthorityDigest));
        Assert.IsTrue(CharacterCreationMagicResonanceDigest.IsCanonical(authority.SourceInputsDigest));
        Assert.IsTrue(CharacterCreationMagicResonanceDigest.IsCanonical(authority.CustomDataInputsDigest));
        Assert.IsTrue(CharacterCreationMagicResonanceDigest.IsCanonical(authority.GmPolicyDigest));
        Assert.IsTrue(CharacterCreationMagicResonanceDigest.IsCanonical(authority.RuntimeDigest));

        CharacterCreationMagicResonanceTalentOption magician = authority.Talents.Single(item =>
            item.Rank == "C" && item.Kind == CharacterCreationMagicResonanceKinds.Magician);
        Assert.AreEqual(3, magician.Magic);
        Assert.AreEqual(5, magician.SpellBudget);
        Assert.IsTrue(magician.RequiresTradition);
        Assert.IsTrue(magician.AllowsSpells);
        Assert.IsTrue(magician.IsEnabled, string.Join(",", magician.Blockers));

        CharacterCreationMagicResonanceTalentOption adept = authority.Talents.Single(item =>
            item.Rank == "D" && item.Kind == CharacterCreationMagicResonanceKinds.Adept);
        Assert.AreEqual(2m, adept.AdeptPowerPointBudget);
        Assert.IsTrue(adept.AllowsAdeptPowers);

        CharacterCreationMagicResonanceCatalogOption acidStream = authority.Spells.Single(item =>
            item.Name == "Acid Stream");
        Assert.IsTrue(acidStream.IsEnabled, string.Join(",", acidStream.Blockers));
        Assert.AreEqual("Combat", acidStream.Category);
        Assert.AreEqual("SR5", acidStream.SourceBook);

        CharacterCreationMagicResonanceCatalogOption selectablePower = authority.AdeptPowers.Single(item =>
            item.Name == "Adrenaline Boost");
        Assert.IsTrue(selectablePower.IsEnabled, string.Join(",", selectablePower.Blockers));
        Assert.AreEqual(0.25m, selectablePower.PointCost);
        Assert.AreEqual(1, selectablePower.MaximumLevels);

        CharacterCreationMagicResonanceCatalogOption unsupportedPower = authority.AdeptPowers.Single(item =>
            item.Name == "Astral Perception");
        Assert.IsFalse(unsupportedPower.IsEnabled);
        CollectionAssert.Contains(
            unsupportedPower.Blockers.ToList(),
            CharacterCreationMagicResonanceBlockers.OptionSemanticsUnsupported);
    }

    [TestMethod]
    public void Receipt_ledger_rejects_command_tampering_and_binds_every_authority_digest()
    {
        var workspaceId = new Chummer.Contracts.Workspaces.CharacterWorkspaceId(Guid.NewGuid().ToString("D"));
        string digest = CharacterCreationMagicResonanceDigest.ComputeUtf8("bound");
        var receipt = new CharacterCreationMagicResonanceReceipt(
            CharacterCreationMagicResonanceSchemas.ReceiptV1,
            workspaceId,
            PreviousContentRevision: 7,
            ContentRevision: 8,
            SavedRevision: 8,
            DraftRevision: 1,
            DraftDigest: digest,
            PreviewDigest: digest,
            IdempotencyKeyDigest: CharacterCreationMagicResonanceDigest.ComputeUtf8("key"),
            CommandDigest: CharacterCreationMagicResonanceDigest.ComputeUtf8("command"),
            PreviousReceiptDigest: CharacterCreationMagicResonanceDigest.ReceiptLedgerRootDigest,
            AuthorityDigest: digest,
            SourceInputsDigest: digest,
            CustomDataInputsDigest: digest,
            GmPolicyDigest: digest,
            RuntimeDigest: digest,
            TalentKind: CharacterCreationMagicResonanceKinds.Magician,
            AdeptPowerPointsRemaining: 0m,
            SpellsRemaining: 5,
            ComplexFormsRemaining: 0,
            CharacterDocumentChanged: false,
            ReceiptDigest: string.Empty);
        receipt = receipt with
        {
            ReceiptDigest = CharacterCreationMagicResonanceDigest.ComputeReceipt(receipt)
        };

        Assert.IsTrue(CharacterCreationMagicResonanceDraftIntegrity.IsValidReceiptLedger(
            [receipt], workspaceId, persistedContentRevision: 8));
        Assert.IsFalse(CharacterCreationMagicResonanceDraftIntegrity.IsValidReceiptLedger(
            [receipt with { CommandDigest = CharacterCreationMagicResonanceDigest.ComputeUtf8("tampered") }],
            workspaceId,
            persistedContentRevision: 8));
    }

    private static string FindCoreRoot()
    {
        DirectoryInfo? current = new(AppDomain.CurrentDomain.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Chummer", "data", "settings.xml")))
                return current.FullName;
            current = current.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate canonical Chummer/data/settings.xml.");
    }
}

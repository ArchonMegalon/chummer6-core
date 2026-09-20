using System.Xml.Linq;
using System.Text.Json;
using Chummer.Application.Characters;
using Chummer.Application.LifeModules;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Api;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Rulesets;
using Chummer.Contracts.LifeModules;
using Chummer.Contracts.Workspaces;
using Chummer.Infrastructure.Files;
using Chummer.Infrastructure.Workspaces;
using Chummer.Infrastructure.Xml;
using Chummer.Infrastructure.DependencyInjection;
using Chummer.Infrastructure.Owners;
using Chummer.Contracts.Owners;
using Chummer.Rulesets.Hosting;
using Chummer.Rulesets.Sr5;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Extensions.DependencyInjection;

namespace Chummer.Tests;

[TestClass]
public sealed class CharacterCreationBootstrapServiceTests
{
    private const string CanonicalPrioritySettingsId =
        CharacterCreationBootstrapProfiles.PrioritySettingsProfileId;
    private const string CanonicalSumToTenSettingsId =
        CharacterCreationBootstrapProfiles.SumToTenSettingsProfileId;
    private const string CanonicalKarmaSettingsId =
        CharacterCreationBootstrapProfiles.KarmaSettingsProfileId;
    private const string CanonicalLifeModulesSettingsId =
        CharacterCreationBootstrapProfiles.LifeModulesSettingsProfileId;

    private const string HumanId = "a53d885d-a4a4-443d-b6a6-b0a55b0a96c7";
    private const string ElfId = "b3259991-b315-4dbe-ae3c-51f71a1116e2";
    private const string OrkId = "8ed6892f-88e6-42d0-a704-b805778ec13e";
    private const string MagicianId = "0e741331-d776-4be8-abc5-4101228abdef";

    [TestMethod]
    public void Karma_contacts_use_profile_allowance_and_charge_only_overflow_without_mutation()
    {
        using var fixture = new KarmaDiskFixture(includeSkills: true, includeGear: true);
        var before = fixture.Store.Get(fixture.Id).Value!;
        var foundation = CompletionFoundation(fixture, 10m);
        var context = fixture.Resolver.TryCreateContext(before.Document.Content)!;
        Assert.IsTrue(context.TryResolveCreationKarmaContactsPolicy(out var policy));
        Assert.AreEqual("{CHAUnaug} * 3", policy!.ContactPointsExpression);
        Assert.AreEqual(1, policy.GroupContactKarmaMultiplier);
        Assert.IsTrue(context.TryResolveCreationKarmaGrantSources(HumanId, "mundane", out var racial, out var talent));
        var contact = KarmaContact(2, 2);
        var quote = CharacterCreationKarmaContactsRules.Evaluate(policy, foundation, racial, talent, [contact]);
        Assert.IsNotNull(quote);
        Assert.IsTrue(quote.CanSelect, string.Join(",", quote.Blockers));
        Assert.AreEqual(3, quote.ContactPoints);
        Assert.AreEqual(4, quote.ContactPointsUsed);
        Assert.AreEqual(1, quote.KarmaUsed);
        var free = CharacterCreationKarmaContactsRules.Evaluate(policy, foundation, racial, talent, [contact with { Free = true }])!;
        Assert.AreEqual(0, free.KarmaUsed);
        Assert.AreEqual(0, free.ContactPointsUsed);
        var family = CharacterCreationKarmaContactsRules.Evaluate(policy, foundation, racial, talent,
            [contact with { Family = true, Blackmail = true }])!;
        Assert.AreEqual(7, family.Lines[0].PointCost);
        Assert.AreEqual(4, family.KarmaUsed);
        AssertJsonEqual(before, fixture.Store.Get(fixture.Id).Value!);
        AssertJsonEqual(quote, CharacterCreationKarmaContactsRules.Evaluate(policy, foundation, racial, talent, [contact])!);
    }

    [TestMethod]
    public void Karma_contacts_bind_allowance_to_draft_attributes_and_reject_foreign_or_unknown_expressions()
    {
        using var fixture = new KarmaDiskFixture(configureSettings: settings => settings.SetElementValue("contactpointsexpression", "({CHAUnaug} + 1) * 2.1"));
        var state = fixture.Service.Load(fixture.Id).Value!;
        var low = fixture.Service.Preview(state.Binding, HumanId, "mundane", []).Value!.Attributes!;
        var raised = fixture.Service.Preview(state.Binding, HumanId, "mundane", [new("CHA", 1)]).Value!.Attributes!;
        var context = fixture.Resolver.TryCreateContext(fixture.Store.Get(fixture.Id).Value!.Document.Content)!;
        Assert.IsTrue(context.TryResolveCreationKarmaContactsPolicy(out var policy));
        Assert.IsTrue(CharacterCreationKarmaContactsRules.TryContactPoints(policy!, low, out int first));
        Assert.IsTrue(CharacterCreationKarmaContactsRules.TryContactPoints(policy!, raised, out int second));
        Assert.AreEqual(5, first);
        Assert.AreEqual(7, second);
        foreach (string expression in new[] { "{Unknown} * 3", "1 div 0", "-1", "2147483648", "document('private')", "(2", "2)" })
        {
            var invalid = policy! with { ContactPointsExpression = expression, AuthorityDigest = string.Empty };
            invalid = invalid with { AuthorityDigest = CharacterCreationKarmaContactsRules.PolicyDigest(invalid) };
            Assert.IsFalse(CharacterCreationKarmaContactsRules.TryContactPoints(invalid, low, out _), expression);
        }
        var foreign = policy! with { SettingsProfileId = "foreign", AuthorityDigest = string.Empty };
        foreign = foreign with { AuthorityDigest = CharacterCreationKarmaContactsRules.PolicyDigest(foreign) };
        Assert.IsFalse(CharacterCreationKarmaContactsRules.TryContactPoints(foreign, low, out _));
        Assert.IsFalse(CharacterCreationKarmaContactsRules.TryContactPoints(policy! with { ContactPointsExpression = "999" }, low, out _));
    }

    [TestMethod]
    public void Karma_contacts_profile_legacy_shim_and_group_rate_are_explicit_and_source_fenced()
    {
        using var fixture = new KarmaDiskFixture(configureSettings: settings =>
        {
            settings.Elements("contactpointsexpression").Remove();
            settings.SetElementValue("usetotalvalueforcontacts", true);
            settings.SetElementValue("freecontactsmultiplierenabled", true);
            settings.SetElementValue("freekarmacontactsmultiplier", 6);
            settings.Element("karmacost")!.SetElementValue("karmacontact", 2);
        });
        string xml = fixture.Store.Get(fixture.Id).Value!.Document.Content;
        var context = fixture.Resolver.TryCreateContext(xml)!;
        Assert.IsTrue(context.TryResolveCreationKarmaContactsPolicy(out var policy));
        Assert.AreEqual("{CHA} * 6", policy!.ContactPointsExpression);
        Assert.AreEqual(2, policy.GroupContactKarmaMultiplier);
        fixture.EditSettings(settings => settings.SetElementValue("freekarmacontactsmultiplier", 7));
        Assert.IsFalse(context.TryResolveCreationKarmaContactsPolicy(out _));
        Assert.IsTrue(fixture.Resolver.TryCreateContext(xml)!.TryResolveCreationKarmaContactsPolicy(out var fresh));
        Assert.AreNotEqual(policy.AuthorityDigest, fresh!.AuthorityDigest);
        Assert.AreEqual("{CHA} * 7", fresh.ContactPointsExpression);
    }

    [TestMethod]
    public void Karma_contacts_reject_duplicate_malformed_or_structured_settings_without_defaults()
    {
        Action<XElement>[] changes =
        [
            row => row.Add(new XElement("contactpointsexpression", "{CHA} * 999")),
            row => row.Element("contactpointsexpression")!.Add(new XAttribute("unsafe", true)),
            row => row.Element("contactpointsexpression")!.Add(new XElement("value", "3")),
            row => row.SetElementValue("contactpointsexpression", ""),
            row => row.Element("karmacost")!.Add(new XElement("karmacontact", 3)),
            row => row.Element("karmacost")!.SetElementValue("karmacontact", "-1"),
            row => row.Element("karmacost")!.SetElementValue("karmacontact", "1.5")
        ];
        foreach (var change in changes)
        {
            using var fixture = new KarmaDiskFixture(configureSettings: change);
            var context = fixture.Resolver.TryCreateContext(fixture.Store.Get(fixture.Id).Value!.Document.Content)!;
            Assert.IsFalse(context.TryResolveCreationKarmaContactsPolicy(out _));
        }
    }

    [TestMethod]
    public void Karma_contacts_preserve_group_quality_costs_and_do_not_spend_the_free_pool()
    {
        using var fixture = new KarmaDiskFixture(includeSkills: true, includeGear: true, qualityMultiplier: 3,
            configureSettings: settings => settings.Element("karmacost")!.SetElementValue("karmacontact", 2));
        var foundation = CompletionFoundation(fixture, 0m);
        var context = fixture.Resolver.TryCreateContext(fixture.Store.Get(fixture.Id).Value!.Document.Content)!;
        Assert.IsTrue(context.TryResolveCreationKarmaContactsPolicy(out var policy));
        Assert.IsTrue(context.TryResolveCreationKarmaGrantSources(HumanId, "mundane", out var racial, out var talent));
        var group = KarmaContact(3, 1) with { IsGroup = true };
        var quote = CharacterCreationKarmaContactsRules.Evaluate(policy!, foundation, racial, talent, [group])!;
        Assert.IsNotNull(quote);
        Assert.AreEqual(8, quote.GroupContactKarma);
        Assert.AreEqual(8, quote.CombinedQualityCosts.PositiveLimitKarma);
        Assert.AreEqual(8, quote.KarmaUsed, "Do not apply KarmaQuality again to group-contact Karma.");
        Assert.AreEqual(0, quote.ContactPointsUsed);
        Assert.IsNull(CharacterCreationKarmaContactsRules.Evaluate(policy!, foundation, racial, talent, [group with { Loyalty = 2 }]));
        Assert.IsNull(CharacterCreationKarmaContactsRules.Evaluate(policy!, foundation, racial, talent, [group, group]));
        Assert.IsNull(CharacterCreationKarmaContactsRules.Evaluate(policy!, foundation, racial, talent,
            [group with { ContactId = Guid.Empty }]));
    }

    [TestMethod]
    public void Karma_contacts_reject_over_limit_connections_and_mark_excess_shared_karma()
    {
        using var fixture = new KarmaDiskFixture(includeSkills: true, includeGear: true, budget: 1);
        var foundation = CompletionFoundation(fixture, 0m);
        var context = fixture.Resolver.TryCreateContext(fixture.Store.Get(fixture.Id).Value!.Document.Content)!;
        Assert.IsTrue(context.TryResolveCreationKarmaContactsPolicy(out var policy));
        Assert.IsTrue(context.TryResolveCreationKarmaGrantSources(HumanId, "mundane", out var racial, out var talent));
        var contact = KarmaContact(4, 2);
        var quote = CharacterCreationKarmaContactsRules.Evaluate(policy!, foundation, racial, talent, [contact])!;
        Assert.IsFalse(quote.CanSelect);
        Assert.AreEqual(3, quote.KarmaUsed);
        CollectionAssert.Contains(quote.Blockers.ToArray(), CharacterCreationKarmaMetatypeBlockers.BudgetExceeded);
        Assert.IsNull(CharacterCreationKarmaContactsRules.Evaluate(policy!, foundation, racial, talent,
            [contact with { Connection = 8 }]));
        var over = CharacterCreationKarmaContactsRules.Evaluate(policy!, foundation, racial, talent,
            [contact with { Connection = 6, Loyalty = 6 }])!;
        CollectionAssert.Contains(over.Blockers.ToArray(), CharacterCreationKarmaContactsRules.ContactLimitExceeded);
        Assert.IsNull(CharacterCreationKarmaContactsRules.Evaluate(policy!, foundation, racial, talent,
            [contact with { Identity = contact.Identity with { Name = "bad\0name" } }]));
    }

    private static CharacterCreationKarmaContactSelection KarmaContact(int connection, int loyalty)
        => new(Guid.NewGuid(), new("Mara", "Fixer", "Berlin", "", "", "", "", "", "", "", "", "", ""), connection, loyalty);

    [TestMethod]
    public void Karma_contacts_confirm_once_survive_disk_reopen_and_cannot_be_silently_dropped()
    {
        using var fixture = new KarmaDiskFixture(includeSkills: true, includeGear: true, includeLifestyles: true);
        var foundation = CompletionFoundation(fixture, 10m);
        var contact = KarmaContact(2, 2);
        var request = fixture.Request(HumanId, "mundane", [], foundation.Skills!.Selection, 10m, [], [], [contact]);
        var before = fixture.Store.Get(fixture.Id).Value!;
        Assert.IsNull(fixture.Service.Confirm(request with { ExplicitlyConfirmed = false }).Value);
        AssertJsonEqual(before, fixture.Store.Get(fixture.Id).Value!);
        var applied = fixture.Service.Confirm(request);
        Assert.IsNotNull(applied.Value, string.Join(",", applied.Blockers));
        Assert.AreEqual(11m, applied.Value.Decision.Quote.KarmaBudget.Used);
        Assert.IsTrue(fixture.Service.Confirm(request).Value!.Replayed);
        var saved = fixture.Store.Get(fixture.Id).Value!;
        Assert.AreEqual(before.Document.Content, saved.Document.Content, "Pending contacts are not applied to character XML.");
        var store = new FileWorkspaceStore(fixture.StateRoot);
        var service = new CharacterCreationKarmaMetatypeService(store, fixture.Resolver);
        var reopened = service.Open(fixture.Id);
        Assert.IsNotNull(reopened.Value, string.Join(",", reopened.Blockers));
        var open = reopened.Value!;
        Assert.AreEqual(1, open.Quote!.Contacts!.KarmaUsed);
        Assert.AreEqual(contact.ContactId, open.Quote.Contacts.Lines.Single().Selection.ContactId);
        var omitted = service.Preview(open.State.Binding, HumanId, "mundane", [], foundation.Skills.Selection, 10m, [], []);
        CollectionAssert.Contains(omitted.Blockers.ToArray(), CharacterCreationKarmaMetatypeBlockers.ContactsSelectionRequired);
        AssertJsonEqual(saved, store.Get(fixture.Id).Value!);
        var changed = service.Preview(open.State.Binding, HumanId, "mundane", [new("CHA", 1)], foundation.Skills.Selection,
            10m, [], [], [contact]);
        Assert.IsNotNull(changed.Value);
        Assert.AreEqual(6, changed.Value.Contacts!.ContactPoints);
        Assert.AreEqual(0, changed.Value.Contacts.KarmaUsed, "Reprice against the newly selected draft Charisma.");
        var differentPayload = request with { ContactSelections = [contact with { Loyalty = 3 }] };
        Assert.IsNull(service.Confirm(differentPayload).Value, "A reused operation ID must not buy a changed contact.");
    }

    [TestMethod]
    public void Karma_contacts_finalize_into_exact_character_and_archive_without_replaying()
    {
        using var fixture = new KarmaDiskFixture(includeSkills: true, includeGear: true, includeLifestyles: true);
        var foundation = CompletionFoundation(fixture, 10m);
        var contact = KarmaContact(2, 2) with { Identity = KarmaContact(1, 1).Identity with { Name = "Mara <&> Ñ", Notes = "A real contact." } };
        Assert.IsNotNull(fixture.Service.Confirm(fixture.Request(HumanId, "mundane", [], foundation.Skills!.Selection,
            10m, [], [], [contact])).Value);
        var open = fixture.Service.Open(fixture.Id, true, true, true).Value!;
        var reviewed = fixture.Service.ReviewFinalization(open.State.Binding, open.Quote!.QuoteDigest, 4);
        Assert.IsNotNull(reviewed.Value, string.Join(",", reviewed.Blockers));
        var review = reviewed.Value!;
        CollectionAssert.Contains(review.OrderedDeltas.Select(delta => delta.DeltaId).ToArray(), "contacts:karma");
        Assert.AreEqual(1m, review.OrderedDeltas.Single(delta => delta.DeltaId == "contacts:karma").KarmaCost);
        var confirm = new CharacterCreationKarmaFinalizationConfirmRequest(
            new(review.Binding, review.PreviewDigest, review.Plan!.PlanDigest, Guid.NewGuid().ToString("D"), true), 4);
        var final = fixture.Service.ConfirmFinalization(confirm);
        Assert.IsNotNull(final.Value, string.Join(",", final.Blockers));
        var saved = fixture.Store.Get(fixture.Id).Value!;
        var root = XElement.Parse(saved.Document.Content);
        Assert.AreEqual("True", root.Element("created")!.Value);
        Assert.AreEqual("3", root.Element("contactpoints")!.Value);
        var node = root.Element("contacts")!.Elements("contact").Single();
        Assert.AreEqual(contact.ContactId.ToString("D"), node.Element("guid")!.Value);
        Assert.AreEqual(contact.Identity.Name, node.Element("name")!.Value);
        Assert.AreEqual("2", node.Element("loyalty")!.Value);
        AssertJsonEqual(saved, new FileWorkspaceStore(fixture.StateRoot).Get(fixture.Id).Value!);
        Assert.IsNotNull(fixture.Service.ConfirmFinalization(confirm).Value);
        AssertJsonEqual(saved, fixture.Store.Get(fixture.Id).Value!);
    }

    [TestMethod]
    public void Karma_contacts_invalid_choice_keeps_upstream_rows_editable_without_a_saveable_quote()
    {
        using var fixture = new KarmaDiskFixture(includeSkills: true, includeGear: true);
        var foundation = CompletionFoundation(fixture, 0m);
        var contact = KarmaContact(2, 2) with { IsGroup = true };
        var result = fixture.Service.Preview(foundation.Binding, HumanId, "mundane", [],
            foundation.Skills!.Selection, 0m, [], [], [contact]);
        Assert.IsNotNull(result.Value);
        Assert.IsFalse(result.Value.CanSelect);
        Assert.IsFalse(result.Value.KarmaBudget.IsExact);
        Assert.IsNotNull(result.Value.Attributes);
        Assert.IsNotNull(result.Value.Skills);
        Assert.IsNull(result.Value.Contacts);
        CollectionAssert.Contains(result.Blockers.ToArray(), CharacterCreationContactsBlockers.ContactInvalid);
        var request = new CharacterCreationKarmaMetatypeConfirmRequest(foundation.Binding, HumanId,
            result.Value.QuoteDigest, Guid.NewGuid(), true, "mundane", [], foundation.Skills.Selection, 0m, [], [], [contact]);
        var before = fixture.Store.Get(fixture.Id).Value!;
        Assert.IsNull(fixture.Service.Confirm(request).Value);
        AssertJsonEqual(before, fixture.Store.Get(fixture.Id).Value!);
    }

    [TestMethod]
    public void Karma_contacts_saved_quote_rejects_rehashed_budget_tampering_and_source_changes()
    {
        using var fixture = new KarmaDiskFixture(includeSkills: true, includeGear: true);
        var foundation = CompletionFoundation(fixture, 0m);
        var contact = KarmaContact(2, 2);
        var request = fixture.Request(HumanId, "mundane", [], foundation.Skills!.Selection, 0m, [], [], [contact]);
        var applied = fixture.Service.Confirm(request).Value!;
        Assert.IsTrue(CharacterCreationKarmaContactsRules.IsValid(applied.Decision.Quote, [contact]));
        var quote = applied.Decision.Quote;
        var forgedContacts = quote.Contacts! with { KarmaUsed = 0, QuoteDigest = string.Empty };
        forgedContacts = forgedContacts with { QuoteDigest = CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(forgedContacts) };
        var forged = quote with { Contacts = forgedContacts, KarmaBudget = quote.KarmaBudget with { Used = 0, Remaining = 800 }, QuoteDigest = string.Empty };
        forged = forged with { QuoteDigest = CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(forged) };
        Assert.IsFalse(CharacterCreationKarmaContactsRules.IsValid(forged, [contact]));
        foreach (var broken in new[] { quote.Contacts! with { Lines = null! }, quote.Contacts! with { Lines = [null!] } })
            Assert.IsFalse(CharacterCreationKarmaContactsRules.IsValid(quote with { Contacts = broken }, [contact]));
        Assert.IsFalse(CharacterCreationKarmaContactsRules.IsValid(quote with { Binding = null! }, [contact]));
        var before = fixture.Store.Get(fixture.Id).Value!;
        fixture.EditSettings(row => row.SetElementValue("contactpointsexpression", "{CHAUnaug} * 4"));
        Assert.IsNull(fixture.Service.Open(fixture.Id).Value);
        AssertJsonEqual(before, fixture.Store.Get(fixture.Id).Value!);
    }

    [TestMethod]
    public void Karma_contacts_recheck_source_after_flush_and_recover_only_the_durable_result()
    {
        using var fixture = new KarmaDiskFixture(includeSkills: true, includeGear: true);
        var foundation = CompletionFoundation(fixture, 0m);
        var request = fixture.Request(HumanId, "mundane", [], foundation.Skills!.Selection, 0m, [], [], [KarmaContact(2, 2)]);
        var before = fixture.Store.Get(fixture.Id).Value!;
        fixture.Fault.Action = stage =>
        {
            if (stage == FileWorkspaceStoreFaultStage.AfterTempFileFlushed)
                fixture.EditSettings(row => row.SetElementValue("contactpointsexpression", "{CHAUnaug} * 4"));
        };
        Assert.IsNull(fixture.Service.Confirm(request).Value);
        fixture.Fault.Action = null;
        AssertJsonEqual(before, new FileWorkspaceStore(fixture.StateRoot).Get(fixture.Id).Value!);
    }

    [TestMethod]
    public void Karma_contacts_owner_scope_rejects_foreign_local_and_expired_stamps()
    {
        using var fixture = new KarmaDiskFixture(includeSkills: true, includeGear: true);
        using var owner = new RequestOwnerContextAccessor(new("contact-owner"));
        using var stranger = new RequestOwnerContextAccessor(new("contact-stranger"));
        var stamp = owner.Capture();
        var bootstrap = new OwnerBoundCharacterCreationBootstrapService(CreateService(fixture.Store, fixture.Resolver, CreateFileQueries()), owner);
        var id = bootstrap.Create(stamp, KarmaRequest()).Value!.WorkspaceId;
        var service = new OwnerBoundCharacterCreationKarmaMetatypeService(fixture.Store, owner, fixture.Resolver);
        var state = service.Load(stamp, id, true, true, true).Value!;
        var skills = new CharacterCreationKarmaSkillsSelection([NativeEnglish(state.SkillsCatalog!)], []);
        CharacterCreationKarmaContactSelection[] contacts = [KarmaContact(2, 2)];
        var quote = service.Preview(stamp, state.Binding, HumanId, "mundane", [], skills, 0m, [], [], contacts).Value!;
        var request = new CharacterCreationKarmaMetatypeConfirmRequest(quote.Binding, HumanId, quote.QuoteDigest,
            Guid.NewGuid(), true, "mundane", [], skills, 0m, [], [], contacts);
        var foreign = new OwnerBoundCharacterCreationKarmaMetatypeService(fixture.Store, stranger, fixture.Resolver);
        Assert.IsNull(foreign.Confirm(stranger.Capture(), request).Value);
        Assert.IsNull(fixture.Service.Confirm(request).Value);
        Assert.IsNotNull(service.Confirm(stamp, request).Value);
        Assert.AreEqual(contacts[0].ContactId, service.Open(stamp, id).Value!.Quote!.Contacts!.Lines[0].Selection.ContactId);
        owner.Dispose();
        Assert.IsNull(service.Confirm(stamp, request).Value);
        using var newLifetime = new RequestOwnerContextAccessor(new("contact-owner"));
        var returned = new OwnerBoundCharacterCreationKarmaMetatypeService(fixture.Store, newLifetime, fixture.Resolver);
        Assert.IsNull(returned.Confirm(stamp, request).Value, "The same account name does not revive an expired display stamp.");
    }

    [TestMethod]
    public void Karma_contacts_high_places_comes_from_selected_source_quality_not_a_caller_flag()
    {
        using var fixture = new KarmaDiskFixture(includeSkills: true, includeGear: true, configureSettings: row =>
        {
            var books = row.Element("books")!;
            if (!books.Elements("book").Any(book => book.Value == "RF")) books.Add(new XElement("book", "RF"));
        });
        var state = fixture.Service.Load(fixture.Id, true, true, true).Value!;
        var highPlaces = state.QualitiesCatalog!.Options.Single(option => option.Name == "Friends in High Places");
        Assert.IsTrue(highPlaces.IsSelectable);
        var skills = new CharacterCreationKarmaSkillsSelection([NativeEnglish(state.SkillsCatalog!)], []);
        var foundation = fixture.Service.Preview(state.Binding, HumanId, "mundane", [new("CHA", 2)], skills,
            0m, [highPlaces.OptionId], []).Value!;
        Assert.IsNotNull(foundation);
        Assert.IsTrue(foundation.CanSelect, string.Join(",", foundation.Blockers));
        var context = fixture.Resolver.TryCreateContext(fixture.Store.Get(fixture.Id).Value!.Document.Content)!;
        Assert.IsTrue(context.TryResolveCreationKarmaContactsPolicy(out var policy));
        Assert.IsTrue(context.TryResolveCreationKarmaGrantSources(HumanId, "mundane", out var racial, out var talent));
        var ordinary = KarmaContact(2, 2);
        var friend = KarmaContact(8, 1);
        var quote = CharacterCreationKarmaContactsRules.Evaluate(policy!, foundation, racial, talent, [ordinary, friend])!;
        Assert.IsNotNull(quote);
        Assert.IsTrue(quote.CanSelect, string.Join(",", quote.Blockers));
        Assert.AreEqual(9, quote.ContactPoints);
        Assert.AreEqual(4, quote.ContactPointsUsed);
        Assert.AreEqual(12, quote.HighPlacesPoints);
        Assert.AreEqual(9, quote.HighPlacesPointsUsed);
        Assert.AreEqual(0, quote.KarmaUsed);
        var invalid = CharacterCreationKarmaContactsRules.Evaluate(policy!, foundation, racial, talent,
            [friend, friend with { ContactId = Guid.NewGuid() }])!;
        CollectionAssert.Contains(invalid.Blockers.ToArray(), CharacterCreationKarmaContactsRules.ContactLimitExceeded);
        Assert.AreEqual(6, invalid.KarmaUsed, "An invalid high-places selection must still report its overspend.");
        var group = CharacterCreationKarmaContactsRules.Evaluate(policy!, foundation, racial, talent,
            [friend with { IsGroup = true }])!;
        Assert.IsTrue(group.CanSelect);
        Assert.AreEqual(0, group.HighPlacesPointsUsed, "Groups are not funded by either contact allowance.");
        Assert.AreEqual(9, group.KarmaUsed);
    }

    [TestMethod]
    public void Karma_completion_budget_caps_leftovers_before_adding_source_owned_starting_cash_without_mutation()
    {
        using var fixture = new KarmaDiskFixture(includeSkills: true, includeGear: true, includeLifestyles: true);
        var before = fixture.Store.Get(fixture.Id).Value!;
        var foundation = CompletionFoundation(fixture, 10.1m, buyGear: true);
        var context = fixture.Resolver.TryCreateContext(before.Document.Content)!;
        Assert.IsTrue(context.TryResolveCreationKarmaCarryoverPolicy(out var policy));
        Assert.IsTrue(context.TryResolveCreationKarmaDefaultStartingNuyen(out var source));
        Assert.AreEqual(7, policy!.MaximumKarma);
        Assert.AreEqual(5000m, policy.MaximumNuyen);
        Assert.AreEqual("Street", source!.Name);
        Assert.AreEqual(1, source.Dice);
        Assert.AreEqual(20m, source.Multiplier);
        var result = CharacterCreationKarmaFinalizationBudgetRules.Evaluate(policy, source, foundation, 4);
        Assert.IsNotNull(result);
        Assert.AreEqual(0.9m, result.ResourceKarmaRoundingAdjustment);
        Assert.AreEqual(789, result.KarmaBeforeCarryover);
        Assert.AreEqual(7, result.KarmaCarried);
        Assert.AreEqual(782, result.KarmaDiscarded);
        Assert.AreEqual(foundation.Gear!.Budget.RemainingNuyen, result.NuyenBeforeCarryover);
        Assert.AreEqual(5000m, result.NuyenCarried);
        Assert.AreEqual(result.NuyenBeforeCarryover - 5000m, result.NuyenDiscarded);
        Assert.AreEqual(80m, result.LifestyleStartingNuyen);
        Assert.AreEqual(5080m, result.CareerNuyen, "The cap applies before adding lifestyle money.");
        Assert.AreEqual(20200m, foundation.Resources!.NuyenFromKarma, "Starting money cannot fund creation purchases.");
        Assert.IsTrue(CharacterCreationKarmaFinalizationBudgetRules.IsValid(result, foundation));
        AssertJsonEqual(result, CharacterCreationKarmaFinalizationBudgetRules.Evaluate(policy, source, foundation, 4)!);
        AssertJsonEqual(before, fixture.Store.Get(fixture.Id).Value!);
    }

    [TestMethod]
    public void Karma_completion_budget_uses_house_rules_and_zero_limits_not_ui_defaults()
    {
        using var fixture = new KarmaDiskFixture(includeSkills: true, includeGear: true, includeLifestyles: true,
            configureSettings: settings =>
            {
                settings.Element("karmacost")!.SetElementValue("karmacarryover", "0");
                settings.SetElementValue("nuyencarryover", "0");
            });
        fixture.EditLifestyle("Street", row => { row.SetElementValue("dice", "2"); row.SetElementValue("multiplier", "12.5"); });
        var foundation = CompletionFoundation(fixture, 0m);
        var context = fixture.Resolver.TryCreateContext(fixture.Store.Get(fixture.Id).Value!.Document.Content)!;
        Assert.IsTrue(context.TryResolveCreationKarmaCarryoverPolicy(out var policy));
        Assert.IsTrue(context.TryResolveCreationKarmaDefaultStartingNuyen(out var source));
        var quote = CharacterCreationKarmaFinalizationBudgetRules.Evaluate(policy!, source!, foundation, 7);
        Assert.IsNotNull(quote);
        Assert.AreEqual(0, quote.KarmaCarried);
        Assert.AreEqual(800, quote.KarmaDiscarded);
        Assert.AreEqual(0m, quote.NuyenCarried);
        Assert.AreEqual(87.5m, quote.CareerNuyen);
    }

    [TestMethod]
    [DataRow("0.1", 19, "0.9")]
    [DataRow("0.5", 19, "0.5")]
    [DataRow("1", 19, "0")]
    public void Karma_completion_budget_rounds_resource_spend_not_remaining_karma(string investmentText, int expectedKarma, string adjustment)
    {
        using var fixture = new KarmaDiskFixture(budget: 20, includeSkills: true, includeGear: true, includeLifestyles: true,
            configureSettings: settings => settings.Element("karmacost")!.SetElementValue("karmacarryover", "20"));
        decimal investment = decimal.Parse(investmentText, System.Globalization.CultureInfo.InvariantCulture);
        var foundation = CompletionFoundation(fixture, investment);
        var context = fixture.Resolver.TryCreateContext(fixture.Store.Get(fixture.Id).Value!.Document.Content)!;
        Assert.IsTrue(context.TryResolveCreationKarmaCarryoverPolicy(out var policy));
        Assert.IsTrue(context.TryResolveCreationKarmaDefaultStartingNuyen(out var source));
        var result = CharacterCreationKarmaFinalizationBudgetRules.Evaluate(policy!, source!, foundation, 1);
        Assert.IsNotNull(result);
        Assert.AreEqual(expectedKarma, result.KarmaBeforeCarryover);
        Assert.AreEqual(expectedKarma, result.KarmaCarried);
        Assert.AreEqual(decimal.Parse(adjustment, System.Globalization.CultureInfo.InvariantCulture), result.ResourceKarmaRoundingAdjustment);
        Assert.AreEqual(investment * 2000m, foundation.Resources!.NuyenFromKarma);
    }

    [TestMethod]
    public void Karma_completion_budget_rejects_cash_overflow_and_preserves_zero_cash_sources()
    {
        using var fixture = new KarmaDiskFixture(includeSkills: true, includeGear: true, includeLifestyles: true);
        var foundation = CompletionFoundation(fixture, 0m);
        var context = fixture.Resolver.TryCreateContext(fixture.Store.Get(fixture.Id).Value!.Document.Content)!;
        Assert.IsTrue(context.TryResolveCreationKarmaCarryoverPolicy(out var policy));
        Assert.IsTrue(context.TryResolveCreationKarmaDefaultStartingNuyen(out var source));
        var row = XElement.Parse(source!.SourceNodeXml);
        row.SetElementValue("multiplier", decimal.MaxValue.ToString(System.Globalization.CultureInfo.InvariantCulture));
        var overflow = CharacterCreationKarmaFinalizationBudgetRules.ProjectStartingCashSource(row.ToString(),
            source.SettingsProfileId, source.RawProfileInputsDigest, source.SourceInputsDigest)!;
        Assert.IsNull(CharacterCreationKarmaFinalizationBudgetRules.Evaluate(policy!, overflow, foundation, 6));
        row.SetElementValue("multiplier", "0");
        var zero = CharacterCreationKarmaFinalizationBudgetRules.ProjectStartingCashSource(row.ToString(),
            source.SettingsProfileId, source.RawProfileInputsDigest, source.SourceInputsDigest)!;
        Assert.AreEqual(0m, CharacterCreationKarmaFinalizationBudgetRules.Evaluate(policy!, zero, foundation, 6)!.CareerNuyen);
    }

    [TestMethod]
    public void Karma_completion_budget_defaults_apply_only_to_absent_profile_fields()
    {
        using var fixture = new KarmaDiskFixture(configureSettings: settings =>
        {
            settings.Element("karmacost")!.Elements("karmacarryover").Remove();
            settings.Elements("nuyencarryover").Remove();
        });
        var context = fixture.Resolver.TryCreateContext(fixture.Store.Get(fixture.Id).Value!.Document.Content)!;
        Assert.IsTrue(context.TryResolveCreationKarmaCarryoverPolicy(out var policy));
        Assert.AreEqual(7, policy!.MaximumKarma);
        Assert.AreEqual(5000m, policy.MaximumNuyen);
        foreach (var (field, value) in new[] { ("karmacarryover", "-1"), ("karmacarryover", "7.5"),
                     ("karmacarryover", ""), ("karmacarryover", "2147483648"), ("nuyencarryover", "-1"),
                     ("nuyencarryover", "five thousand"), ("nuyencarryover", "1,000"), ("nuyencarryover", "1e4") })
        {
            using var invalid = new KarmaDiskFixture(configureSettings: settings =>
                (field == "karmacarryover" ? settings.Element("karmacost")! : settings).SetElementValue(field, value));
            var malformed = invalid.Resolver.TryCreateContext(invalid.Store.Get(invalid.Id).Value!.Document.Content)!;
            Assert.IsFalse(malformed.TryResolveCreationKarmaCarryoverPolicy(out _), field + ":" + value);
        }
    }

    [TestMethod]
    public void Karma_completion_budget_rejects_duplicate_or_structured_carryover_fields()
    {
        Action<XElement>[] changes =
        [
            settings => settings.Element("karmacost")!.Add(new XElement("karmacarryover", 7)),
            settings => { settings.SetElementValue("nuyencarryover", 5000); settings.Add(new XElement("nuyencarryover", 5000)); },
            settings => settings.Element("karmacost")!.Element("karmacarryover")!.Add(new XAttribute("override", "true")),
            settings => { settings.SetElementValue("nuyencarryover", 5000); settings.Element("nuyencarryover")!.Add(new XElement("value", 5000)); },
            settings => settings.Add(new XElement("karmacost", new XElement("karmacarryover", 7)))
        ];
        foreach (var change in changes)
        {
            using var fixture = new KarmaDiskFixture(configureSettings: change);
            var context = fixture.Resolver.TryCreateContext(fixture.Store.Get(fixture.Id).Value!.Document.Content)!;
            Assert.IsFalse(context.TryResolveCreationKarmaCarryoverPolicy(out _));
        }
    }

    [TestMethod]
    public void Karma_completion_budget_refuses_unbound_or_incomplete_quotes_and_tampered_amounts()
    {
        using var fixture = new KarmaDiskFixture(includeSkills: true, includeGear: true, includeLifestyles: true);
        var foundation = CompletionFoundation(fixture, 10m);
        var context = fixture.Resolver.TryCreateContext(fixture.Store.Get(fixture.Id).Value!.Document.Content)!;
        Assert.IsTrue(context.TryResolveCreationKarmaCarryoverPolicy(out var policy));
        Assert.IsTrue(context.TryResolveCreationKarmaDefaultStartingNuyen(out var source));
        var result = CharacterCreationKarmaFinalizationBudgetRules.Evaluate(policy!, source!, foundation, 6)!;
        Assert.IsNull(CharacterCreationKarmaFinalizationBudgetRules.Evaluate(policy!, source!, foundation, 0));
        Assert.IsNull(CharacterCreationKarmaFinalizationBudgetRules.Evaluate(policy!, source!, foundation, 7));
        Assert.IsNull(CharacterCreationKarmaFinalizationBudgetRules.Evaluate(policy!, source! with { Multiplier = 10000 }, foundation, 6));
        var other = policy! with { RawProfileInputsDigest = CharacterCreationFinalizationDigest.ComputeUtf8("foreign"), AuthorityDigest = string.Empty };
        other = other with { AuthorityDigest = CharacterCreationKarmaFinalizationBudgetRules.PolicyDigest(other) };
        Assert.IsNull(CharacterCreationKarmaFinalizationBudgetRules.Evaluate(other, source!, foundation, 6));
        foreach (var invalid in new[] { foundation with { Skills = null }, foundation with { Gear = null },
                     foundation with { Qualities = null }, foundation with { Resources = null },
                     foundation with { KarmaBudget = foundation.KarmaBudget with { Remaining = 800 } },
                     foundation with { Resources = foundation.Resources! with { NuyenFromKarma = 999999 } } })
        {
            var rehashed = invalid with { QuoteDigest = string.Empty };
            rehashed = rehashed with { QuoteDigest = CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(rehashed) };
            Assert.IsNull(CharacterCreationKarmaFinalizationBudgetRules.Evaluate(policy!, source!, rehashed, 6));
        }
        var forged = result with { CareerNuyen = result.CareerNuyen + 1m, QuoteDigest = string.Empty };
        forged = forged with { QuoteDigest = CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(forged) };
        Assert.IsFalse(CharacterCreationKarmaFinalizationBudgetRules.IsValid(forged, foundation));
    }

    [TestMethod]
    public void Karma_completion_starting_cash_refuses_absent_disabled_ambiguous_and_changed_sources()
    {
        using var missing = new KarmaDiskFixture();
        Assert.IsFalse(missing.Resolver.TryCreateContext(missing.Store.Get(missing.Id).Value!.Document.Content)!
            .TryResolveCreationKarmaDefaultStartingNuyen(out _));
        using var fixture = new KarmaDiskFixture(includeLifestyles: true);
        var document = fixture.Store.Get(fixture.Id).Value!.Document.Content;
        var captured = fixture.Resolver.TryCreateContext(document)!;
        Assert.IsTrue(captured.TryResolveCreationKarmaDefaultStartingNuyen(out _));
        fixture.EditLifestyle("Street", row => row.SetElementValue("multiplier", "21"));
        Assert.IsFalse(captured.TryResolveCreationKarmaDefaultStartingNuyen(out _), "A source context cannot accept drift.");
        var fresh = fixture.Resolver.TryCreateContext(document)!;
        Assert.IsTrue(fresh.TryResolveCreationKarmaDefaultStartingNuyen(out var changed));
        Assert.AreEqual(21m, changed!.Multiplier);
        fixture.EditLifestyle("Street", row => row.SetElementValue("source", "DISABLED"));
        Assert.IsFalse(fixture.Resolver.TryCreateContext(document)!.TryResolveCreationKarmaDefaultStartingNuyen(out _));
        fixture.EditLifestyle("Street", row => { row.SetElementValue("source", "SR5"); row.Add(new XElement("dice", 1)); });
        Assert.IsFalse(fixture.Resolver.TryCreateContext(document)!.TryResolveCreationKarmaDefaultStartingNuyen(out _));
    }

    [TestMethod]
    public void Karma_completion_default_lifestyle_never_overwrites_an_existing_or_malformed_lifestyle()
    {
        using var fixture = new KarmaDiskFixture(includeLifestyles: true);
        var document = XDocument.Parse(fixture.Store.Get(fixture.Id).Value!.Document.Content);
        document.Root!.Elements("lifestyles").Remove();
        document.Root.Add(new XElement("lifestyles", new XElement("lifestyle", new XElement("name", "Low"))));
        Assert.IsFalse(fixture.Resolver.TryCreateContext(document.ToString())!.TryResolveCreationKarmaDefaultStartingNuyen(out _));
        document.Root.Element("lifestyles")!.RemoveNodes();
        document.Root.Add(new XElement("lifestyles"));
        Assert.IsFalse(fixture.Resolver.TryCreateContext(document.ToString())!.TryResolveCreationKarmaDefaultStartingNuyen(out _));
    }

    [TestMethod]
    public void Karma_lifestyles_share_exact_resource_and_gear_budget_without_purchasing_or_rolling_cash()
    {
        using var fixture = new KarmaDiskFixture(includeSkills: true, includeGear: true, includeLifestyles: true);
        var before = fixture.Store.Get(fixture.Id).Value!;
        var foundation = CompletionFoundation(fixture, 10m, buyGear: true);
        Assert.IsTrue(fixture.Resolver.TryCreateContext(before.Document.Content)!
            .TryResolveCreationLifestylesAuthority(out var authority));
        var low = KarmaLifestyle(authority, "Low") with { Increments = 2, City = "Seattle", District = "Redmond" };
        var quote = CharacterCreationKarmaLifestylesRules.Evaluate(authority, foundation.Resources!, foundation.Gear!,
            [low], low.LifestyleId);
        Assert.IsNotNull(quote);
        Assert.IsTrue(quote.CanSelect, string.Join(",", quote.Blockers));
        Assert.AreEqual(4000m, quote.LifestyleNuyenUsed);
        Assert.AreEqual(20000m, quote.Budget.Total);
        Assert.AreEqual(4000m + foundation.Gear!.Budget.BasketCost, quote.Budget.Used);
        Assert.AreEqual(foundation.Gear.Budget.RemainingNuyen - 4000m, quote.Budget.Remaining);
        Assert.AreEqual(foundation.Resources!.QuoteDigest, quote.ResourcesQuoteDigest);
        Assert.AreEqual(foundation.Gear.QuoteDigest, quote.GearQuoteDigest);
        Assert.AreEqual(low.LifestyleId, quote.StartingLifestyleId);
        Assert.AreEqual("Seattle", quote.Lines.Single().Configuration.City);
        Assert.IsTrue(quote.Budget.SourceAnchorIds.Count > 0);
        Assert.IsTrue(CharacterCreationKarmaLifestylesRules.IsValid(quote, foundation.Resources,
            foundation.Gear, [low], low.LifestyleId));
        AssertJsonEqual(before, fixture.Store.Get(fixture.Id).Value!);
    }

    [TestMethod]
    public void Karma_lifestyles_overspend_remains_visible_and_future_cash_cannot_fund_purchase()
    {
        using var fixture = new KarmaDiskFixture(includeSkills: true, includeGear: true, includeLifestyles: true);
        fixture.EditLifestyle("Low", row => row.SetElementValue("multiplier", "1000000"));
        var foundation = CompletionFoundation(fixture, 0.5m);
        Assert.IsTrue(fixture.Resolver.TryCreateContext(fixture.Store.Get(fixture.Id).Value!.Document.Content)!
            .TryResolveCreationLifestylesAuthority(out var authority));
        var low = KarmaLifestyle(authority, "Low");
        var quote = CharacterCreationKarmaLifestylesRules.Evaluate(authority, foundation.Resources!, foundation.Gear!,
            [low], low.LifestyleId);
        Assert.IsNotNull(quote);
        Assert.IsFalse(quote.CanSelect);
        Assert.AreEqual(1000m, quote.Budget.Total);
        Assert.AreEqual(2000m, quote.LifestyleNuyenUsed);
        Assert.AreEqual(-1000m, quote.Budget.Remaining);
        Assert.AreEqual(1000m, quote.Budget.Overspend);
        CollectionAssert.Contains(quote.Blockers.ToArray(), CharacterCreationLifestylesBlockers.InsufficientFunds);
        Assert.IsFalse(CharacterCreationKarmaLifestylesRules.IsValid(quote, foundation.Resources, foundation.Gear,
            [low], low.LifestyleId));
    }

    [TestMethod]
    public void Karma_lifestyles_starting_cash_requires_an_explicit_owned_selection_and_stable_identity()
    {
        using var fixture = new KarmaDiskFixture(includeSkills: true, includeGear: true, includeLifestyles: true);
        var foundation = CompletionFoundation(fixture, 10m);
        Assert.IsTrue(fixture.Resolver.TryCreateContext(fixture.Store.Get(fixture.Id).Value!.Document.Content)!
            .TryResolveCreationLifestylesAuthority(out var authority));
        var low = KarmaLifestyle(authority, "Low");
        var middle = KarmaLifestyle(authority, "Medium");
        var valid = CharacterCreationKarmaLifestylesRules.Evaluate(authority, foundation.Resources!, foundation.Gear!,
            [low, middle], middle.LifestyleId)!;
        Assert.IsTrue(valid.CanSelect);
        Assert.AreEqual(7000m, valid.LifestyleNuyenUsed);
        AssertJsonEqual(valid, CharacterCreationKarmaLifestylesRules.Evaluate(authority, foundation.Resources!,
            foundation.Gear!, [middle, low], middle.LifestyleId)!);
        foreach (Guid? invalid in new Guid?[] { null, Guid.Empty, Guid.NewGuid(), authority.LifestyleOptions.Single(x => x.Name == "High").SourceId })
        {
            var quote = CharacterCreationKarmaLifestylesRules.Evaluate(authority, foundation.Resources!, foundation.Gear!,
                [low], invalid)!;
            Assert.IsFalse(quote.CanSelect);
            CollectionAssert.Contains(quote.Blockers.ToArray(), CharacterCreationKarmaLifestylesRules.StartingLifestyleRequired);
        }
        Assert.IsTrue(CharacterCreationKarmaLifestylesRules.Evaluate(authority, foundation.Resources!, foundation.Gear!, [], null)!.CanSelect);
        Assert.IsFalse(CharacterCreationKarmaLifestylesRules.Evaluate(authority, foundation.Resources!, foundation.Gear!, [], low.LifestyleId)!.CanSelect);
    }

    [TestMethod]
    public void Karma_lifestyles_freeze_nested_choices_reject_duplicate_identities_and_invalid_configuration()
    {
        using var fixture = new KarmaDiskFixture(includeSkills: true, includeGear: true, includeLifestyles: true);
        var foundation = CompletionFoundation(fixture, 10m);
        Assert.IsTrue(fixture.Resolver.TryCreateContext(fixture.Store.Get(fixture.Id).Value!.Document.Content)!
            .TryResolveCreationLifestylesAuthority(out var authority));
        var low = KarmaLifestyle(authority, "Low");
        var quality = new CharacterCreationLifestyleQualitySelection(Guid.NewGuid(), "test-option", "Extra", false, false, false);
        var nested = new[] { quality };
        var original = new[] { low with { Qualities = nested } };
        Assert.IsTrue(CharacterCreationKarmaLifestylesRules.TryFreeze(original, out var frozen));
        nested[0] = quality with { Extra = "Changed after admission" };
        original[0] = low with { Name = "Changed after admission" };
        Assert.AreEqual("Extra", frozen[0].Qualities[0].Extra);
        Assert.AreEqual(low.Name, frozen[0].Name);
        Assert.IsFalse(CharacterCreationKarmaLifestylesRules.TryFreeze([low, low], out _));
        Assert.IsFalse(CharacterCreationKarmaLifestylesRules.TryFreeze([low with { Qualities = [quality, quality] }], out _));
        Assert.IsFalse(CharacterCreationKarmaLifestylesRules.TryFreeze([low with { Qualities = [quality with { InstanceId = low.LifestyleId }] }], out _));
        Assert.IsFalse(CharacterCreationKarmaLifestylesRules.TryFreeze([low with { Qualities = null! }], out _));
        foreach (var invalid in new[] { low with { Increments = 0 }, low with { Percentage = -1 },
            low with { Name = "\0" }, low with { TrustFund = true }, low with { BaseLifestyleOptionId = "missing" } })
            Assert.IsNull(CharacterCreationKarmaLifestylesRules.Evaluate(authority, foundation.Resources!, foundation.Gear!,
                [invalid], invalid.LifestyleId));
    }

    [TestMethod]
    public void Karma_lifestyles_reject_tampered_or_foreign_funding_and_recalculate_changed_source_prices()
    {
        using var fixture = new KarmaDiskFixture(includeSkills: true, includeGear: true, includeLifestyles: true);
        var foundation = CompletionFoundation(fixture, 10m);
        string xml = fixture.Store.Get(fixture.Id).Value!.Document.Content;
        var captured = fixture.Resolver.TryCreateContext(xml)!;
        Assert.IsTrue(captured.TryResolveCreationLifestylesAuthority(out var authority));
        var low = KarmaLifestyle(authority, "Low");
        var quote = CharacterCreationKarmaLifestylesRules.Evaluate(authority, foundation.Resources!, foundation.Gear!, [low], low.LifestyleId)!;
        Assert.IsFalse(CharacterCreationKarmaLifestylesRules.IsValid(quote with { LifestyleNuyenUsed = 0 },
            foundation.Resources, foundation.Gear, [low], low.LifestyleId));
        Assert.IsFalse(CharacterCreationKarmaLifestylesRules.IsValid(quote with { Budget = null! },
            foundation.Resources, foundation.Gear, [low], low.LifestyleId));
        Assert.IsFalse(CharacterCreationKarmaLifestylesRules.IsValid(quote with { Blockers = null! },
            foundation.Resources, foundation.Gear, [low], low.LifestyleId));
        Assert.IsNull(CharacterCreationKarmaLifestylesRules.Evaluate(authority, foundation.Resources! with { NuyenFromKarma = 999999 },
            foundation.Gear!, [low], low.LifestyleId));
        Assert.IsNull(CharacterCreationKarmaLifestylesRules.Evaluate(authority, foundation.Resources!,
            foundation.Gear! with { Budget = foundation.Gear!.Budget with { RemainingNuyen = 999999 } }, [low], low.LifestyleId));
        var foreign = authority with { SettingsProfileId = "other-profile", AuthorityDigest = string.Empty };
        foreign = foreign with { AuthorityDigest = CharacterCreationLifestylesRules.ComputeAuthorityDigest(foreign) };
        Assert.IsNull(CharacterCreationKarmaLifestylesRules.Evaluate(foreign, foundation.Resources!, foundation.Gear!, [low], low.LifestyleId));
        fixture.EditLifestyle("Low", row => row.SetElementValue("cost", "2250"));
        _ = captured.TryResolveCreationLifestylesAuthority(out var drifted);
        Assert.IsFalse(CharacterCreationLifestylesRules.IsValidAuthority(drifted), "Captured contexts must reject source drift.");
        Assert.IsNull(CharacterCreationKarmaLifestylesRules.Evaluate(drifted, foundation.Resources!, foundation.Gear!,
            [low], low.LifestyleId));
        Assert.IsTrue(fixture.Resolver.TryCreateContext(xml)!.TryResolveCreationLifestylesAuthority(out var current));
        var revised = CharacterCreationKarmaLifestylesRules.Evaluate(current, foundation.Resources!, foundation.Gear!, [low], low.LifestyleId)!;
        Assert.AreEqual(2250m, revised.LifestyleNuyenUsed);
        Assert.AreNotEqual(quote.QuoteDigest, revised.QuoteDigest);
        Assert.AreNotEqual(quote.SourceAuthorityDigest, revised.SourceAuthorityDigest);
    }

    [TestMethod]
    public void Karma_lifestyles_cash_resolver_uses_selected_source_and_rejects_missing_disabled_or_drifted_rows()
    {
        using var fixture = new KarmaDiskFixture(includeLifestyles: true);
        var before = fixture.Store.Get(fixture.Id).Value!;
        var context = fixture.Resolver.TryCreateContext(before.Document.Content)!;
        Assert.IsTrue(context.TryResolveCreationLifestylesAuthority(out var authority));
        var low = authority.LifestyleOptions.Single(row => row.Name == "Low");
        Assert.IsTrue(context.TryResolveCreationKarmaStartingNuyen(low.SourceId, out var cash));
        Assert.AreEqual("Low", cash!.Name);
        Assert.AreEqual(3, cash.Dice);
        Assert.AreEqual(60m, cash.Multiplier);
        Assert.AreEqual(low.SourceId.ToString("D"), cash.SourceId);
        Assert.IsTrue(CharacterCreationKarmaFinalizationBudgetRules.IsValidStartingCashSource(cash));
        Assert.IsTrue(context.TryResolveCreationKarmaDefaultStartingNuyen(out var fallback));
        Assert.AreEqual("Street", fallback!.Name);
        Assert.IsFalse(context.TryResolveCreationKarmaStartingNuyen(Guid.Empty, out _));
        Assert.IsFalse(context.TryResolveCreationKarmaStartingNuyen(Guid.NewGuid(), out _));
        fixture.EditLifestyle("Low", row => row.SetElementValue("multiplier", "75"));
        Assert.IsFalse(context.TryResolveCreationKarmaStartingNuyen(low.SourceId, out _));
        Assert.IsTrue(fixture.Resolver.TryCreateContext(before.Document.Content)!
            .TryResolveCreationKarmaStartingNuyen(low.SourceId, out var changed));
        Assert.AreEqual(75m, changed!.Multiplier);
        Assert.AreNotEqual(cash.AuthorityDigest, changed.AuthorityDigest);
        fixture.EditLifestyle("Low", row => row.SetElementValue("source", "DISABLED"));
        Assert.IsFalse(fixture.Resolver.TryCreateContext(before.Document.Content)!
            .TryResolveCreationKarmaStartingNuyen(low.SourceId, out _));
        fixture.EditLifestyle("Low", row => { row.SetElementValue("source", "SR5"); row.Add(new XElement("dice", "3")); });
        Assert.IsFalse(fixture.Resolver.TryCreateContext(before.Document.Content)!
            .TryResolveCreationKarmaStartingNuyen(low.SourceId, out _));
        AssertJsonEqual(before, fixture.Store.Get(fixture.Id).Value!);
    }

    [TestMethod]
    public void Karma_lifestyles_projection_basis_omits_unrelated_qualities_but_binds_the_complete_catalog()
    {
        using var fixture = new KarmaDiskFixture(includeSkills: true, includeGear: true, includeLifestyles: true);
        var foundation = CompletionFoundation(fixture, 10m);
        var authority = fixture.Service.Load(fixture.Id, true, true, true, true).Value!.LifestylesAuthority!;
        var low = KarmaLifestyle(authority, "Low");
        var quote = CharacterCreationKarmaLifestylesRules.Evaluate(authority, foundation.Resources!, foundation.Gear!,
            [low], low.LifestyleId)!;
        Assert.AreEqual(authority.AuthorityDigest, quote.SourceAuthorityDigest);
        Assert.IsTrue(quote.ProjectionAuthority.QualityOptions.Count < authority.QualityOptions.Count);
        Assert.IsTrue(CharacterCreationKarmaLifestylesRules.IsValid(quote, foundation.Resources, foundation.Gear,
            [low], low.LifestyleId));
        var unrelated = authority.QualityOptions.First(row => quote.ProjectionAuthority.QualityOptions.All(kept => kept.OptionId != row.OptionId));
        var changedRow = unrelated with { Name = unrelated.Name + " changed", OptionDigest = string.Empty };
        changedRow = changedRow with { OptionDigest = CharacterCreationLifestylesRules.ComputeQualityOptionDigest(changedRow) };
        var changed = authority with { QualityOptions = authority.QualityOptions.Select(row => row == unrelated ? changedRow : row).ToArray(),
            AuthorityDigest = string.Empty };
        changed = changed with { AuthorityDigest = CharacterCreationLifestylesRules.ComputeAuthorityDigest(changed) };
        var repriced = CharacterCreationKarmaLifestylesRules.Evaluate(changed, foundation.Resources!, foundation.Gear!, [low], low.LifestyleId)!;
        AssertJsonEqual(quote.ProjectionAuthority, repriced.ProjectionAuthority);
        Assert.AreEqual(quote.LifestyleNuyenUsed, repriced.LifestyleNuyenUsed);
        Assert.AreNotEqual(quote.SourceAuthorityDigest, repriced.SourceAuthorityDigest);
        Assert.AreNotEqual(quote.QuoteDigest, repriced.QuoteDigest, "Even an unused catalog change must invalidate the admitted source binding.");
    }

    private static CharacterCreationLifestyleConfiguration KarmaLifestyle(CharacterCreationLifestylesAuthority authority, string name)
    {
        var option = authority.LifestyleOptions.Single(row => row.Name == name);
        return new(Guid.NewGuid(), option.OptionId, name + " home", CharacterCreationLifestyleStyleIds.Standard,
            option.DefaultIncrementId, 1, 100m, 0, false, false, 0, 0, 0, 0, string.Empty, string.Empty, string.Empty, []);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Karma_lifestyles_draft_persists_owned_choices_and_finalization_deducts_them_once(bool freeGrid)
    {
        using var fixture = new KarmaDiskFixture(includeSkills: true, includeGear: true, includeLifestyles: true,
            configureSettings: row => row.SetElementValue("allowfreegrids", freeGrid));
        var initial = fixture.Store.Get(fixture.Id).Value!;
        var foundation = CompletionFoundation(fixture, 10m);
        var state = fixture.Service.Load(fixture.Id, true, true, true, true).Value!;
        var low = KarmaLifestyle(state.LifestylesAuthority!, "Low") with { Name = "Home <&> Ñ", City = "Seattle" };
        var medium = KarmaLifestyle(state.LifestylesAuthority!, "Medium");
        var contact = KarmaContact(2, 2);
        var request = fixture.Request(HumanId, "mundane", [], foundation.Skills!.Selection, 10m, [], [], [contact],
            [low, medium], medium.LifestyleId);
        AssertJsonEqual(initial, fixture.Store.Get(fixture.Id).Value!);
        Assert.IsNull(fixture.Service.Confirm(request with { ExplicitlyConfirmed = false }).Value);
        var committed = fixture.Service.Confirm(request);
        Assert.IsNotNull(committed.Value, string.Join(",", committed.Blockers));
        Assert.AreEqual(7000m, committed.Value.Decision.Quote.Lifestyles!.LifestyleNuyenUsed);
        var saved = fixture.Store.Get(fixture.Id).Value!;
        Assert.AreEqual(initial.Document.Content, saved.Document.Content, "Draft writes must not pre-purchase into legacy XML.");
        Assert.IsTrue(fixture.Service.Confirm(request).Value!.Replayed);
        AssertJsonEqual(saved, fixture.Store.Get(fixture.Id).Value!);
        var coldStore = new FileWorkspaceStore(fixture.StateRoot);
        var cold = new CharacterCreationKarmaMetatypeService(coldStore, fixture.Resolver);
        var opened = cold.Open(fixture.Id);
        Assert.IsNotNull(opened.Value, string.Join(",", opened.Blockers));
        var current = opened.Value.Quote!;
        Assert.AreEqual(medium.LifestyleId, current.Lifestyles!.StartingLifestyleId);
        Assert.IsTrue(current.CanSelect);
        Assert.IsNull(cold.Preview(current.Binding, HumanId, "mundane", [], current.Skills!.Selection,
            10m, [], [], [contact]).Value, "An omitted stage must not discard saved purchases.");
        var finances = cold.PreviewFinalizationBudget(current.Binding, current.QuoteDigest, 4);
        Assert.IsNotNull(finances.Value, string.Join(",", finances.Blockers));
        Assert.AreEqual("Medium", finances.Value.StartingCashSource.Name);
        Assert.AreEqual(13000m, finances.Value.NuyenBeforeCarryover);
        Assert.AreEqual(5400m, finances.Value.CareerNuyen);
        var reviewed = cold.ReviewFinalization(current.Binding, current.QuoteDigest, 4);
        Assert.IsNotNull(reviewed.Value, string.Join(",", reviewed.Blockers));
        var review = reviewed.Value;
        Assert.AreEqual(7000m, review.OrderedDeltas.Sum(delta => delta.NuyenCost));
        var confirm = new CharacterCreationKarmaFinalizationConfirmRequest(
            new(review.Binding, review.PreviewDigest, review.Plan!.PlanDigest, Guid.NewGuid().ToString("D"), true), 4);
        var complete = cold.ConfirmFinalization(confirm);
        Assert.IsNotNull(complete.Value, string.Join(",", complete.Blockers));
        var final = coldStore.Get(fixture.Id).Value!;
        var root = XElement.Parse(final.Document.Content);
        Assert.AreEqual("True", root.Element("created")!.Value);
        Assert.AreEqual("5400", root.Element("nuyen")!.Value);
        var rows = root.Element("lifestyles")!.Elements("lifestyle").ToArray();
        Assert.AreEqual(2, rows.Length);
        // Source data grants Public Grid to Low, and Local + Public Grid to Medium.
        Assert.AreEqual(freeGrid ? 1 : 0, rows.Single(row => row.Element("guid")!.Value == low.LifestyleId.ToString("D"))
            .Descendants("lifestylequality").Count());
        Assert.AreEqual(freeGrid ? 2 : 0, rows.Single(row => row.Element("guid")!.Value == medium.LifestyleId.ToString("D"))
            .Descendants("lifestylequality").Count());
        Assert.AreEqual(low.Name, rows.Single(row => row.Element("guid")!.Value == low.LifestyleId.ToString("D")).Element("name")!.Value);
        Assert.AreEqual(1, root.Element("contacts")!.Elements("contact").Count());
        var reopened = new FileWorkspaceStore(fixture.StateRoot);
        AssertJsonEqual(final, reopened.Get(fixture.Id).Value!);
        AssertJsonEqual(complete.Value, new CharacterCreationKarmaMetatypeService(reopened, fixture.Resolver)
            .ConfirmFinalization(confirm).Value!);
        AssertJsonEqual(final, reopened.Get(fixture.Id).Value!);
    }

    [TestMethod]
    public void Karma_lifestyles_draft_rejects_overspend_stale_sources_and_tampered_history()
    {
        using var fixture = new KarmaDiskFixture(includeSkills: true, includeGear: true, includeLifestyles: true);
        var foundation = CompletionFoundation(fixture, 10m);
        var state = fixture.Service.Load(fixture.Id, true, true, true, true).Value!;
        var low = KarmaLifestyle(state.LifestylesAuthority!, "Low");
        var poor = fixture.Request(HumanId, "mundane", [], foundation.Skills!.Selection, 0.5m, [], [], null, [low], low.LifestyleId);
        Assert.IsNull(fixture.Service.Confirm(poor).Value);
        var request = fixture.Request(HumanId, "mundane", [], foundation.Skills.Selection, 10m, [], [], null, [low], low.LifestyleId);
        var committed = fixture.Service.Confirm(request);
        Assert.IsNotNull(committed.Value, string.Join(",", committed.Blockers));
        var saved = fixture.Store.Get(fixture.Id).Value!;
        var original = committed.Value.Decision.Quote;
        var forged = original with { Lifestyles = original.Lifestyles! with { LifestyleNuyenUsed = 0 } };
        Assert.IsFalse(CharacterCreationKarmaLifestylesRules.IsValidForFoundation(forged, [low], low.LifestyleId));
        Assert.IsFalse(CharacterCreationKarmaLifestylesRules.IsValidForFoundation(original, [], null));
        var open = fixture.Service.Open(fixture.Id).Value!;
        var review = fixture.Service.ReviewFinalization(open.State.Binding, open.Quote!.QuoteDigest, 3).Value!;
        Assert.IsNotNull(review);
        fixture.EditLifestyle("Low", row => row.SetElementValue("cost", "2250"));
        Assert.IsNull(fixture.Service.Open(fixture.Id).Value);
        Assert.IsNull(fixture.Service.ConfirmFinalization(new(new(review.Binding, review.PreviewDigest,
            review.Plan!.PlanDigest, Guid.NewGuid().ToString("D"), true), 3)).Value);
        AssertJsonEqual(saved, fixture.Store.Get(fixture.Id).Value!);
    }

    [TestMethod]
    public void Karma_lifestyles_pending_source_effects_price_trust_fund_and_cannot_be_forged_or_omitted()
    {
        using var fixture = new KarmaDiskFixture(includeSkills: true, includeGear: true, includeLifestyles: true,
            configureSettings: EnableQualityBook);
        // Explicit custom-data fixture with no SIN follow-up. The real source's
        // unresolved prerequisites remain disabled by the existing quality lane.
        const string trustId = "2656bcd7-3fe1-4c34-a4fb-89ebebfbf016";
        fixture.EditQuality(trustId, row => { row.Elements("required").Remove(); row.Elements("forbidden").Remove(); });
        var state = fixture.Service.Load(fixture.Id, true, true, true, true).Value!;
        var quality = state.QualitiesCatalog!.Options.Single(item => item.SourceId.ToString("D") == trustId);
        Assert.IsTrue(quality.IsSelectable, quality.DisableReasonKey);
        var skills = new CharacterCreationKarmaSkillsSelection([NativeEnglish(state.SkillsCatalog!)], []);
        var medium = KarmaLifestyle(state.LifestylesAuthority!, "Medium") with { TrustFund = true };
        var request = fixture.Request(HumanId, "mundane", [], skills, 0m, [quality.OptionId], [], null, [medium], medium.LifestyleId);
        var saved = fixture.Service.Confirm(request);
        Assert.IsNotNull(saved.Value, string.Join(",", saved.Blockers));
        var quote = fixture.Service.Open(fixture.Id).Value!.Quote!;
        Assert.AreEqual(0, quote.Lifestyles!.ProjectionAuthority.TrustFundLevel, "Baseline XML has no pending quality effects.");
        Assert.IsTrue(quote.Lifestyles.Lines[0].Economics.CoveredByTrustFund);
        Assert.AreEqual(0m, quote.Lifestyles.LifestyleNuyenUsed);
        Assert.IsTrue(CharacterCreationKarmaLifestylesRules.IsValidForFoundation(quote, [medium], medium.LifestyleId));
        var removed = fixture.Service.Preview(quote.Binding, HumanId, "mundane", [], skills, 0m, [], [], null, [medium], medium.LifestyleId);
        Assert.IsNotNull(removed.Value);
        Assert.IsFalse(removed.Value.CanSelect, "Removing the quality must not preserve its free lifestyle.");
        var review = fixture.Service.ReviewFinalization(quote.Binding, quote.QuoteDigest, 4);
        Assert.IsNotNull(review.Value, string.Join(",", review.Blockers));
        var final = fixture.Service.ConfirmFinalization(new(new(review.Value.Binding, review.Value.PreviewDigest,
            review.Value.Plan!.PlanDigest, Guid.NewGuid().ToString("D"), true), 4));
        Assert.IsNotNull(final.Value, string.Join(",", final.Blockers));
        var root = XElement.Parse(fixture.Store.Get(fixture.Id).Value!.Document.Content);
        Assert.AreEqual("True", root.Element("lifestyles")!.Element("lifestyle")!.Element("trustfund")!.Value);
        Assert.AreEqual("1", root.Element("improvements")!.Elements("improvement")
            .Single(row => row.Element("improvementttype")!.Value == "TrustFund").Element("val")!.Value);
    }

    [TestMethod]
    public void Karma_lifestyles_effect_admission_preserves_creation_conditions_and_refuses_unsupported_cost_modifiers()
    {
        static XElement Effect(string type, string value, string enabled = "1", string condition = "")
            => new("improvement", new XElement("improvementttype", type), new XElement("val", value),
                new XElement("enabled", enabled), new XElement("condition", condition));
        Assert.IsTrue(CharacterCreationLifestyleImprovementRules.TryResolve(
            [Effect("TrustFund", "1", condition: "create"), Effect("TrustFund", "2", enabled: "0"),
                Effect("LifestyleCost", "-50", condition: "career")], 0, out int level, out _));
        Assert.AreEqual(1, level);
        foreach (var invalid in new[] { new[] { Effect("TrustFund", "1"), Effect("TrustFund", "2") },
            new[] { Effect("TrustFund", "5") }, new[] { Effect("TrustFund", "1.5") },
            new[] { Effect("LifestyleCost", "-50") }, new[] { Effect("BasicLifestyleCost", "-25", condition: "once") } })
            Assert.IsFalse(CharacterCreationLifestyleImprovementRules.TryResolve(invalid, 0, out _, out _));
        Assert.IsFalse(CharacterCreationLifestyleImprovementRules.TryResolve([Effect("TrustFund", "1")], 1, out _, out _));
    }

    [TestMethod]
    public void Karma_lifestyles_owner_admission_and_request_freezing_preserve_the_original_review()
    {
        using var fixture = new KarmaDiskFixture(includeSkills: true, includeGear: true, includeLifestyles: true);
        using var owner = new RequestOwnerContextAccessor(new("lifestyle-owner"));
        using var stranger = new RequestOwnerContextAccessor(new("lifestyle-stranger"));
        var stamp = owner.Capture();
        var bootstrap = new OwnerBoundCharacterCreationBootstrapService(CreateService(fixture.Store, fixture.Resolver, CreateFileQueries()), owner);
        var id = bootstrap.Create(stamp, KarmaRequest()).Value!.WorkspaceId;
        var service = new OwnerBoundCharacterCreationKarmaMetatypeService(fixture.Store, owner, fixture.Resolver);
        var state = service.Load(stamp, id, true, true, true, true).Value!;
        var skills = new CharacterCreationKarmaSkillsSelection([NativeEnglish(state.SkillsCatalog!)], []);
        var low = KarmaLifestyle(state.LifestylesAuthority!, "Low");
        CharacterCreationLifestyleConfiguration[] choices = [low];
        var quote = service.Preview(stamp, state.Binding, HumanId, "mundane", [], skills, 10m, [], [], null, choices, low.LifestyleId).Value!;
        var request = new CharacterCreationKarmaMetatypeConfirmRequest(quote.Binding, HumanId, quote.QuoteDigest,
            Guid.NewGuid(), true, "mundane", [], skills, 10m, [], [], null, choices, low.LifestyleId);
        Assert.IsTrue(CharacterCreationKarmaMetatypeTransaction.TryFreezeRequest(request, out var frozen));
        choices[0] = low with { Name = "Changed after review" };
        Assert.AreEqual(low.Name, frozen.LifestyleSelections![0].Name);
        var foreign = new OwnerBoundCharacterCreationKarmaMetatypeService(fixture.Store, stranger, fixture.Resolver);
        Assert.IsNull(foreign.Confirm(stranger.Capture(), frozen).Value);
        Assert.IsNull(fixture.Service.Confirm(frozen).Value);
        Assert.IsNull(service.Confirm(stamp, request).Value);
        Assert.IsNotNull(service.Confirm(stamp, frozen).Value);
        Assert.AreEqual(low.Name, service.Open(stamp, id).Value!.Quote!.Lifestyles!.Lines[0].Configuration.Name);
        owner.Dispose();
        using var returnedOwner = new RequestOwnerContextAccessor(new("lifestyle-owner"));
        var returned = new OwnerBoundCharacterCreationKarmaMetatypeService(fixture.Store, returnedOwner, fixture.Resolver);
        Assert.IsNull(returned.Confirm(stamp, frozen).Value);
    }

    [TestMethod]
    public void Karma_lifestyles_explicit_edit_and_removal_reprice_the_same_budget_and_restore_street_fallback()
    {
        using var fixture = new KarmaDiskFixture(includeSkills: true, includeGear: true, includeLifestyles: true);
        var foundation = CompletionFoundation(fixture, 10m);
        var state = fixture.Service.Load(fixture.Id, true, true, true, true).Value!;
        var low = KarmaLifestyle(state.LifestylesAuthority!, "Low");
        Assert.IsNotNull(fixture.Service.Confirm(fixture.Request(HumanId, "mundane", [], foundation.Skills!.Selection,
            10m, [], [], null, [low], low.LifestyleId)).Value);
        var edit = fixture.Service.Confirm(fixture.Request(HumanId, "mundane", [], foundation.Skills.Selection,
            10m, [], [], null, [low with { Increments = 2, City = "Berlin" }], low.LifestyleId));
        Assert.IsNotNull(edit.Value, string.Join(",", edit.Blockers));
        Assert.AreEqual(2L, edit.Value.Decision.DraftRevision);
        Assert.AreEqual(4000m, edit.Value.Decision.Quote.Lifestyles!.LifestyleNuyenUsed);
        var removed = fixture.Service.Confirm(fixture.Request(HumanId, "mundane", [], foundation.Skills.Selection,
            10m, [], [], null, [], null));
        Assert.IsNotNull(removed.Value, string.Join(",", removed.Blockers));
        Assert.AreEqual(20000m, removed.Value.Decision.Quote.Lifestyles!.Budget.Remaining);
        var cold = new CharacterCreationKarmaMetatypeService(new FileWorkspaceStore(fixture.StateRoot), fixture.Resolver);
        var current = cold.Open(fixture.Id).Value!.Quote!;
        Assert.AreEqual(0, current.Lifestyles!.Lines.Count);
        Assert.IsNull(current.Lifestyles.StartingLifestyleId);
        Assert.AreEqual("Street", cold.LoadFinalizationStartingCash(current.Binding, current.QuoteDigest).Value!.Name);
        var review = cold.ReviewFinalization(current.Binding, current.QuoteDigest, 4);
        Assert.IsNotNull(review.Value, string.Join(",", review.Blockers));
        Assert.AreEqual(0m, review.Value.OrderedDeltas.Sum(delta => delta.NuyenCost));
        Assert.AreEqual(5080m, review.Value.Plan!.NuyenRemaining);
    }

    private static CharacterCreationKarmaMetatypeQuote CompletionFoundation(KarmaDiskFixture fixture,
        decimal investment, bool buyGear = false, string metatypeId = HumanId)
    {
        var state = fixture.Service.Load(fixture.Id, includeSkills: true, includeQualities: true, includeGear: true).Value!;
        var skills = new CharacterCreationKarmaSkillsSelection([NativeEnglish(state.SkillsCatalog!)], [], null);
        var item = state.GearAuthority!.Options.First(option => option.IsSelectable && option.PackageCost is > 0 and < 1000);
        CharacterCreationGearSelection[] gear = buyGear ? [new(item.OptionId, item.PackageQuantity)] : [];
        var quote = fixture.Service.Preview(state.Binding, metatypeId, "mundane", [], skills, investment, [], gear).Value;
        Assert.IsNotNull(quote);
        Assert.IsTrue(quote.CanSelect, string.Join(",", quote.Blockers));
        return quote;
    }

    [TestMethod]
    [DataRow(HumanId)]
    [DataRow(ElfId)]
    [DataRow(OrkId)]
    public void Karma_completion_projector_composes_the_saved_build_without_writing(string metatypeId)
    {
        bool freeGrid = metatypeId == ElfId;
        using var fixture = new KarmaDiskFixture(includeSkills: true, includeGear: true, includeLifestyles: true,
            configureSettings: settings => settings.SetElementValue("allowfreegrids", freeGrid));
        var state = fixture.Service.Load(fixture.Id, true, true, true).Value!;
        var pistols = state.SkillsCatalog!.ActiveSkills.Single(item => item.Name == "Pistols");
        var skills = new CharacterCreationKarmaSkillsSelection(
            [NativeEnglish(state.SkillsCatalog), new(pistols.SourceSkillId, pistols.Kind, 2)], []);
        var quality = state.QualitiesCatalog!.Options.Single(item => item.Name == "Unsteady Hands");
        var gear = state.GearAuthority!.Options.Single(item => item.Name == "Flashlight, Low-light" && item.IsSelectable);
        Assert.IsNotNull(fixture.Service.Confirm(fixture.Request(metatypeId, "mundane", [new("BOD", 2)],
            skills, 10.1m, [quality.OptionId], [new(gear.OptionId, 2)])).Value);
        var quote = fixture.Service.Open(fixture.Id, true, true, true).Value!.Quote!;
        var before = fixture.Store.Get(fixture.Id).Value!;
        var source = fixture.Resolver.TryCreateContext(before.Document.Content)!;
        Assert.IsTrue(source.TryResolveCreationKarmaCarryoverPolicy(out var policy));
        Assert.IsTrue(source.TryResolveCreationKarmaDefaultStartingNuyen(out var starting));
        Assert.IsTrue(source.TryResolveCreationKarmaGrantSources(metatypeId, "mundane", out var racial, out _));
        Assert.IsTrue(source.TryResolveCreationLifestylesAuthority(out var lifestyles));
        var finances = CharacterCreationKarmaFinalizationBudgetRules.Evaluate(policy!, starting!, quote, 4)!;
        Assert.IsTrue(CharacterCreationKarmaFinalizationProjector.TryProject(before, quote, finances, racial, lifestyles,
            out var xml, out var deltas, out var blockers), string.Join(",", blockers));
        var root = XDocument.Parse(xml).Root!;
        Assert.AreEqual("True", root.Element("created")!.Value);
        Assert.IsNull(root.Element(CharacterCreationBootstrapXml.MarkerElement));
        Assert.AreEqual(metatypeId, root.Element("metatypeid")!.Value);
        Assert.AreEqual("Karma", root.Element("buildmethod")!.Value);
        Assert.AreEqual("2/1/0", root.Element("walk")!.Value);
        var body = root.Element("attributes")!.Elements("attribute").Single(item => item.Element("name")!.Value == "BOD");
        Assert.AreEqual("2", body.Element("karma")!.Value);
        Assert.AreEqual("0", body.Element("base")!.Value);
        Assert.AreEqual(metatypeId == OrkId ? "6" : "3", body.Element("totalvalue")!.Value);
        var skill = root.Element("newskills")!.Element("skills")!.Elements("skill").Single();
        Assert.AreEqual(pistols.SourceSkillId, skill.Element("suid")!.Value);
        Assert.AreEqual("2", skill.Element("karma")!.Value);
        var language = root.Element("newskills")!.Element("knoskills")!.Elements("skill").Single();
        Assert.AreEqual("True", language.Element("isnativelanguage")!.Value);
        Assert.AreEqual(racial.Count + 1, root.Element("qualities")!.Elements("quality").Count());
        Assert.IsTrue(root.Element("qualities")!.Elements("quality").Any(item => item.Element("name")!.Value == "Unsteady Hands"));
        var savedGear = root.Element("gears")!.Elements("gear").Single();
        Assert.AreEqual("2", savedGear.Element("qty")!.Value);
        Assert.AreEqual("Flashlight, Low-light", savedGear.Element("name")!.Value);
        var lifestyle = root.Element("lifestyles")!.Elements("lifestyle").Single();
        Assert.AreEqual("Street", lifestyle.Element("name")!.Value);
        Assert.AreEqual(freeGrid ? 1 : 0, lifestyle.Descendants("lifestylequality").Count());
        if (freeGrid)
        {
            var grid = lifestyle.Descendants("lifestylequality").Single();
            Assert.AreEqual("Grid Subscription", grid.Element("name")!.Value);
            Assert.AreEqual("Public Grid", grid.Element("extra")!.Value);
            Assert.AreEqual("True", grid.Element("isfreegrid")!.Value);
        }
        Assert.AreEqual("7", root.Element("karma")!.Value);
        Assert.AreEqual("5080", root.Element("nuyen")!.Value);
        Assert.AreEqual(quote.KarmaBudget.Used + 0.9m, deltas.Sum(item => item.KarmaCost));
        Assert.AreEqual(50m, deltas.Sum(item => item.NuyenCost));
        CollectionAssert.AreEqual(Enumerable.Range(1, deltas.Length).ToArray(), deltas.Select(item => item.Order).ToArray());
        AssertJsonEqual(before, fixture.Store.Get(fixture.Id).Value!);
        var coldStore = new FileWorkspaceStore(fixture.StateRoot);
        var coldQuote = new CharacterCreationKarmaMetatypeService(coldStore, fixture.Resolver).Open(fixture.Id, true, true, true).Value!.Quote!;
        Assert.IsTrue(CharacterCreationKarmaFinalizationProjector.TryProject(coldStore.Get(fixture.Id).Value!, coldQuote,
            finances, racial, lifestyles, out var coldXml, out var coldDeltas, out _));
        Assert.AreEqual(xml, coldXml);
        AssertJsonEqual(deltas, coldDeltas);
        Assert.IsFalse(CharacterCreationKarmaFinalizationProjector.TryProject(before, quote,
            finances with { CareerNuyen = 999999 }, racial, lifestyles, out var rejected, out var rejectedDeltas, out _));
        Assert.AreEqual(string.Empty, rejected);
        Assert.AreEqual(0, rejectedDeltas.Length);
        Assert.IsFalse(CharacterCreationKarmaFinalizationProjector.TryProject(before with { ContentRevision = before.ContentRevision + 1 },
            quote, finances, racial, lifestyles, out _, out _, out _));
        Assert.IsFalse(CharacterCreationKarmaFinalizationProjector.TryProject(before, quote, finances, racial,
            lifestyles with { SourceDigest = new string('0', 64) }, out _, out _, out _));
    }

    [TestMethod]
    public void Karma_completion_review_uses_saved_current_build_and_does_not_finalize_or_change_history()
    {
        using var fixture = new KarmaDiskFixture(includeSkills: true, includeGear: true, includeLifestyles: true);
        var pending = CompletionFoundation(fixture, 10.1m, buyGear: true);
        Assert.IsNull(fixture.Service.PreviewFinalizationBudget(pending.Binding, pending.QuoteDigest, 4).Value,
            "An unsaved UI quote cannot substitute for the confirmed build.");
        var command = new CharacterCreationKarmaMetatypeConfirmRequest(pending.Binding, HumanId,
            pending.QuoteDigest, Guid.NewGuid(), true, "mundane", [], pending.Skills!.Selection, 10.1m, [],
            pending.Gear!.Lines.Select(line => new CharacterCreationGearSelection(line.OptionId, line.Quantity)).ToArray());
        Assert.IsNotNull(fixture.Service.Confirm(command).Value);
        var current = fixture.Service.Open(fixture.Id, true, true, true).Value!.Quote!;
        var before = fixture.Store.Get(fixture.Id).Value!;
        var counted = new KarmaOpenCountingResolver(fixture.Resolver);
        var service = new CharacterCreationKarmaMetatypeService(fixture.Store, counted);
        var review = service.PreviewFinalizationBudget(current.Binding, current.QuoteDigest, 4);
        Assert.IsNotNull(review.Value, string.Join(",", review.Blockers));
        Assert.AreEqual(1, counted.Contexts, "Admission and completion must share one captured source context.");
        Assert.AreEqual(5080m, review.Value.CareerNuyen);
        Assert.IsNull(service.PreviewFinalizationBudget(pending.Binding, pending.QuoteDigest, 4).Value);
        Assert.IsNull(service.PreviewFinalizationBudget(current.Binding, pending.QuoteDigest, 4).Value);
        var cold = new CharacterCreationKarmaMetatypeService(new FileWorkspaceStore(fixture.StateRoot), fixture.Resolver);
        AssertJsonEqual(review.Value, cold.PreviewFinalizationBudget(current.Binding, current.QuoteDigest, 4).Value!);
        using var foreignOwner = new RequestOwnerContextAccessor(new("foreign-completion-owner"));
        var foreign = new OwnerBoundCharacterCreationKarmaMetatypeService(fixture.Store, foreignOwner, fixture.Resolver);
        Assert.IsNull(foreign.PreviewFinalizationBudget(foreignOwner.Capture(), current.Binding, current.QuoteDigest, 4).Value);
        AssertJsonEqual(before, fixture.Store.Get(fixture.Id).Value!);
        fixture.EditGear(pending.Gear.Lines[0].Name, row => row.SetElementValue("cost", "999"));
        Assert.IsNull(cold.PreviewFinalizationBudget(current.Binding, current.QuoteDigest, 4).Value);
        AssertJsonEqual(before, fixture.Store.Get(fixture.Id).Value!);
    }

    [TestMethod]
    public void Karma_completion_cash_terms_are_read_only_and_review_requires_the_displayed_source()
    {
        using var fixture = new KarmaDiskFixture(includeSkills: true, includeGear: true, includeLifestyles: true);
        _ = CompletionRequest(fixture);
        var current = fixture.Service.Open(fixture.Id, true, true, true).Value!.Quote!;
        var before = fixture.Store.Get(fixture.Id).Value!;
        var terms = fixture.Service.LoadFinalizationStartingCash(current.Binding, current.QuoteDigest);
        Assert.IsNotNull(terms.Value, string.Join(",", terms.Blockers));
        Assert.AreEqual(1, terms.Value.Dice);
        Assert.AreEqual(20m, terms.Value.Multiplier);
        Assert.IsNotNull(fixture.Service.ReviewFinalization(current.Binding, current.QuoteDigest, 4, terms.Value.AuthorityDigest).Value);
        Assert.IsNull(fixture.Service.ReviewFinalization(current.Binding, current.QuoteDigest, 4, "sha256:" + new string('0', 64)).Value);
        Assert.IsNull(fixture.Service.ReviewFinalization(current.Binding, current.QuoteDigest, 999, terms.Value.AuthorityDigest).Value);
        Assert.IsNull(fixture.Service.LoadFinalizationStartingCash(current.Binding with { ContentRevision = current.Binding.ContentRevision + 1 }, current.QuoteDigest).Value);
        AssertJsonEqual(before, fixture.Store.Get(fixture.Id).Value!);
        fixture.EditLifestyle("Street", row => row.SetElementValue("multiplier", "21"));
        Assert.IsNull(fixture.Service.ReviewFinalization(current.Binding, current.QuoteDigest, 4, terms.Value.AuthorityDigest).Value);
        AssertJsonEqual(before, fixture.Store.Get(fixture.Id).Value!);
    }

    private static CharacterCreationKarmaFinalizationConfirmRequest CompletionRequest(KarmaDiskFixture fixture,
        string metatypeId = HumanId)
    {
        var pending = CompletionFoundation(fixture, 10.1m, buyGear: true, metatypeId: metatypeId);
        Assert.IsNotNull(fixture.Service.Confirm(new(pending.Binding, metatypeId, pending.QuoteDigest,
            Guid.NewGuid(), true, "mundane", [], pending.Skills!.Selection, 10.1m, [],
            pending.Gear!.Lines.Select(item => new CharacterCreationGearSelection(item.OptionId, item.Quantity)).ToArray())).Value);
        var current = fixture.Service.Open(fixture.Id, true, true, true).Value!.Quote!;
        var reviewed = fixture.Service.ReviewFinalization(current.Binding, current.QuoteDigest, 4);
        Assert.IsNotNull(reviewed.Value, string.Join(",", reviewed.Blockers));
        var review = reviewed.Value;
        Assert.IsTrue(review.CanConfirm);
        Assert.IsNotNull(review.Plan);
        return new(new(review.Binding, review.PreviewDigest, review.Plan.PlanDigest, Guid.NewGuid().ToString("D"), true), 4);
    }

    [TestMethod]
    [DataRow(HumanId)]
    [DataRow(OrkId)]
    public void Karma_completion_atomic_confirm_archives_history_and_cold_replay_never_spends_twice(string metatypeId)
    {
        using var fixture = new KarmaDiskFixture(includeSkills: true, includeGear: true, includeLifestyles: true);
        var request = CompletionRequest(fixture, metatypeId);
        var before = fixture.Store.Get(fixture.Id).Value!;
        Assert.IsNull(fixture.Service.ConfirmFinalization(request with
            { Confirmation = request.Confirmation with { ExplicitlyConfirmed = false } }).Value);
        Assert.IsNull(fixture.Service.ConfirmFinalization(request with { DiceTotal = 5 }).Value);
        AssertJsonEqual(before, fixture.Store.Get(fixture.Id).Value!);
        var result = fixture.Service.ConfirmFinalization(request);
        Assert.IsNotNull(result.Value, string.Join(",", result.Blockers));
        var coldStore = new FileWorkspaceStore(fixture.StateRoot);
        var afterRead = coldStore.Get(fixture.Id);
        Assert.IsTrue(afterRead.Success, afterRead.Error);
        var after = afterRead.Value!;
        Assert.AreEqual(before.ContentRevision + 1, after.ContentRevision);
        Assert.AreEqual(after.ContentRevision, after.SavedRevision);
        Assert.AreEqual("True", XDocument.Parse(after.Document.Content).Root!.Element("created")!.Value);
        var root = XDocument.Parse(after.Document.Content).Root!;
        Assert.AreEqual(metatypeId, root.Element("metatypeid")!.Value);
        if (metatypeId == OrkId)
        {
            Assert.AreEqual("Ork", root.Element("metatype")!.Value);
            var vision = root.Element("qualities")!.Elements("quality").Single();
            Assert.AreEqual("Low-Light Vision", vision.Element("name")!.Value);
            Assert.AreEqual("Metatype", vision.Element("qualitysource")!.Value);
            Assert.AreEqual("4", root.Element("attributes")!.Elements("attribute")
                .Single(item => item.Element("name")!.Value == "BOD").Element("totalvalue")!.Value);
        }
        Assert.IsNull(after.Document.AuxiliaryState.CharacterCreationKarmaMetatypeDecisions);
        Assert.IsNull(after.Document.AuxiliaryState.CharacterCreationBootstrapBinding);
        AssertJsonEqual(before.Document.AuxiliaryState, after.Document.AuxiliaryState.CharacterCreationFinalizationArchive!.State);
        Assert.AreEqual(result.Value.AuthorityDigest,
            after.Document.AuxiliaryState.CharacterCreationFinalizationArchive.KarmaAuthority!.AuthorityDigest);
        var cold = new CharacterCreationKarmaMetatypeService(coldStore, fixture.Resolver);
        AssertJsonEqual(result.Value, cold.ConfirmFinalization(request).Value!);
        Assert.IsNull(cold.ConfirmFinalization(request with { DiceTotal = 5 }).Value);
        Assert.IsNull(cold.ConfirmFinalization(request with
            { Confirmation = request.Confirmation with { IdempotencyKey = Guid.NewGuid().ToString("D") } }).Value);
        Assert.IsNull(cold.Open(fixture.Id, true, true, true).Value);
        fixture.EditLifestyle("Street", row => row.SetElementValue("multiplier", 99));
        AssertJsonEqual(result.Value, cold.ConfirmFinalization(request).Value!);
        AssertJsonEqual(after, coldStore.Get(fixture.Id).Value!);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Karma_completion_atomic_failure_recovers_only_the_durable_result(bool afterReplace)
    {
        using var fixture = new KarmaDiskFixture(includeSkills: true, includeGear: true, includeLifestyles: true);
        var request = CompletionRequest(fixture);
        var before = fixture.Store.Get(fixture.Id).Value!;
        fixture.Fault.Action = stage =>
        {
            if (stage == (afterReplace ? FileWorkspaceStoreFaultStage.AfterTargetReplaced
                    : FileWorkspaceStoreFaultStage.AfterTempFileFlushed)) throw new IOException("Injected completion failure");
        };
        var result = fixture.Service.ConfirmFinalization(request);
        fixture.Fault.Action = null;
        var coldStore = new FileWorkspaceStore(fixture.StateRoot);
        if (!afterReplace)
        {
            Assert.IsNull(result.Value);
            AssertJsonEqual(before, coldStore.Get(fixture.Id).Value!);
        }
        else Assert.IsNotNull(result.Value, string.Join(",", result.Blockers));
        var cold = new CharacterCreationKarmaMetatypeService(coldStore, fixture.Resolver);
        Assert.IsNotNull(cold.ConfirmFinalization(request).Value);
        Assert.AreEqual(before.ContentRevision + 1, coldStore.Get(fixture.Id).Value!.ContentRevision);
    }

    [TestMethod]
    public void Karma_completion_source_fence_rejects_changes_after_temporary_file_flush()
    {
        using var fixture = new KarmaDiskFixture(includeSkills: true, includeGear: true, includeLifestyles: true);
        var request = CompletionRequest(fixture);
        var before = fixture.Store.Get(fixture.Id).Value!;
        fixture.Fault.Action = stage =>
        {
            if (stage == FileWorkspaceStoreFaultStage.AfterTempFileFlushed)
                fixture.EditLifestyle("Street", row => row.SetElementValue("multiplier", 999));
        };
        Assert.IsNull(fixture.Service.ConfirmFinalization(request).Value);
        fixture.Fault.Action = null;
        AssertJsonEqual(before, new FileWorkspaceStore(fixture.StateRoot).Get(fixture.Id).Value!);
    }

    [TestMethod]
    public void Karma_completion_requires_typed_store_and_rejects_rehashed_archive_forgery()
    {
        using var fixture = new KarmaDiskFixture(includeSkills: true, includeGear: true, includeLifestyles: true);
        var request = CompletionRequest(fixture);
        var before = fixture.Store.Get(fixture.Id).Value!;
        Assert.IsTrue(CharacterCreationKarmaFinalizationTransaction.TryBuild(OwnerScope.LocalSingleUser, before,
            fixture.Resolver, request, out var replacement, out _));
        Assert.IsFalse(fixture.Store.ReplaceWorkspaceDocumentAndAuxiliaryStateAndCheckpoint(fixture.Id,
            before.ContentRevision, before.Document.AuxiliaryStateDigest, replacement!).Success);
        var fake = new CharacterCreationKarmaMetatypeService(new InMemoryWorkspaceStore(), fixture.Resolver);
        CollectionAssert.Contains(fake.ConfirmFinalization(request).Blockers.ToArray(),
            CharacterCreationFinalizationBlockers.AtomicPersistenceRequired);
        Assert.IsNull(fixture.Store.CommitKarmaFinalization(new OwnerScope("local-single-user"), request, fixture.Resolver).Value);
        AssertJsonEqual(before, fixture.Store.Get(fixture.Id).Value!);
        var state = replacement!.AuxiliaryState;
        Assert.IsTrue(WorkspaceAuxiliaryStateIntegrity.IsValidShape(fixture.Id, before.ContentRevision + 1, state));
        var archive = state.CharacterCreationFinalizationArchive!;
        var authority = archive.KarmaAuthority!;
        var changed = authority with { RawCharacterXml = authority.RawCharacterXml.Replace("</character>", "<notes>changed</notes></character>") };
        changed = changed with { AuthorityDigest = CharacterCreationKarmaFinalizationTransaction.AuthorityDigest(changed) };
        var receipt = state.CharacterCreationFinalizationReceipts![0].Receipt with { AuthorityDigest = changed.AuthorityDigest };
        receipt = receipt with { ReceiptDigest = CharacterCreationFinalizationDigest.ComputeReceiptDigest(receipt) };
        var forged = state with
        {
            CharacterCreationFinalizationArchive = archive with { KarmaAuthority = changed },
            CharacterCreationFinalizationReceipts = [new(receipt.IdempotencyKeyDigest, receipt.CommandDigest, receipt)]
        };
        Assert.IsFalse(WorkspaceAuxiliaryStateIntegrity.IsValidShape(fixture.Id, before.ContentRevision + 1, forged));
        Assert.IsFalse(WorkspaceAuxiliaryStateIntegrity.IsValidShape(fixture.Id, before.ContentRevision + 1,
            state with { CharacterCreationFinalizationArchive = archive with { KarmaAuthority = null } }));
        Assert.IsFalse(WorkspaceAuxiliaryStateIntegrity.IsValidShape(fixture.Id, before.ContentRevision + 1,
            state with { CharacterCreationFinalizationArchive = null }));
        Assert.IsNotNull(fixture.Service.ConfirmFinalization(request).Value);
    }

    [TestMethod]
    public async Task Karma_completion_concurrent_confirmations_commit_one_revision()
    {
        using var fixture = new KarmaDiskFixture(includeSkills: true, includeGear: true, includeLifestyles: true);
        var request = CompletionRequest(fixture);
        var other = new CharacterCreationKarmaMetatypeService(new FileWorkspaceStore(fixture.StateRoot), fixture.Resolver);
        var results = await Task.WhenAll(Task.Run(() => fixture.Service.ConfirmFinalization(request)),
            Task.Run(() => other.ConfirmFinalization(request)));
        Assert.IsTrue(results.All(item => item.Value is not null), string.Join(",", results.SelectMany(item => item.Blockers)));
        AssertJsonEqual(results[0].Value!, results[1].Value!);
        Assert.AreEqual(request.Confirmation.Binding.ContentRevision + 1, fixture.Store.Get(fixture.Id).Value!.ContentRevision);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Karma_completion_partial_build_returns_a_blocker_instead_of_throwing(bool metatypeSaved)
    {
        using var fixture = new KarmaDiskFixture(includeSkills: true, includeGear: true, includeLifestyles: true);
        if (metatypeSaved) Assert.IsNotNull(fixture.Service.Confirm(fixture.Request(HumanId)).Value);
        var opened = fixture.Service.Open(fixture.Id, true, true, true).Value!;
        var result = fixture.Service.ReviewFinalization(opened.State.Binding,
            opened.Quote?.QuoteDigest ?? CharacterCreationFinalizationDigest.ComputeUtf8("no-saved-selection"), 4);
        Assert.IsNull(result.Value);
        Assert.IsTrue(result.Blockers.Count > 0);
    }

    [TestMethod]
    public void Karma_completion_owner_lease_rejects_foreign_and_expired_contexts()
    {
        using var fixture = new KarmaDiskFixture(includeSkills: true, includeGear: true, includeLifestyles: true);
        using var owner = new RequestOwnerContextAccessor(new("completion-owner"));
        using var stranger = new RequestOwnerContextAccessor(new("completion-stranger"));
        var stamp = owner.Capture();
        var bootstrap = new OwnerBoundCharacterCreationBootstrapService(CreateService(fixture.Store, fixture.Resolver, CreateFileQueries()), owner);
        var id = bootstrap.Create(stamp, KarmaRequest()).Value!.WorkspaceId;
        var service = new OwnerBoundCharacterCreationKarmaMetatypeService(fixture.Store, owner, fixture.Resolver);
        var foreign = new OwnerBoundCharacterCreationKarmaMetatypeService(fixture.Store, stranger, fixture.Resolver);
        var state = service.Load(stamp, id, true, true, true).Value!;
        var skills = new CharacterCreationKarmaSkillsSelection([NativeEnglish(state.SkillsCatalog!)], []);
        var quote = service.Preview(stamp, state.Binding, HumanId, "mundane", [], skills, 0m, [], []).Value!;
        Assert.IsNotNull(service.Confirm(stamp, new(quote.Binding, HumanId, quote.QuoteDigest,
            Guid.NewGuid(), true, "mundane", [], skills, 0m, [], [])).Value);
        quote = service.Open(stamp, id, true, true, true).Value!.Quote!;
        var review = service.ReviewFinalization(stamp, quote.Binding, quote.QuoteDigest, 4).Value!;
        Assert.IsNotNull(review);
        var request = new CharacterCreationKarmaFinalizationConfirmRequest(
            new(review.Binding, review.PreviewDigest, review.Plan!.PlanDigest, Guid.NewGuid().ToString("D"), true), 4);
        Assert.IsNull(foreign.ConfirmFinalization(stranger.Capture(), request).Value);
        Assert.IsNull(fixture.Service.ConfirmFinalization(request).Value);
        Assert.IsNotNull(service.ConfirmFinalization(stamp, request).Value);
        Assert.IsFalse(fixture.Store.Get(id).Success);
        owner.Dispose();
        Assert.IsNull(service.ConfirmFinalization(stamp, request).Value);
    }

    [TestMethod]
    public void Karma_completion_cold_career_rewards_and_reputation_preserve_the_creation_archive()
    {
        using var fixture = new KarmaDiskFixture(includeSkills: true, includeGear: true, includeLifestyles: true);
        var request = CompletionRequest(fixture);
        var receipt = fixture.Service.ConfirmFinalization(request).Value!;
        Assert.IsNotNull(receipt);
        var coldStore = new FileWorkspaceStore(fixture.StateRoot);
        var before = coldStore.Get(fixture.Id).Value!;
        var reputation = new WorkspaceCharacterCareerReputationService(coldStore, fixture.Resolver);
        var read = reputation.Read(fixture.Id);
        Assert.AreEqual(CharacterCareerReputationOutcome.Available, read.Outcome, read.Error);
        Assert.AreEqual(0, read.Snapshot!.Reputation.Inputs.CareerKarma);
        var review = reputation.Preview(new(fixture.Id, Guid.NewGuid(), CharacterCareerReputationOperation.AdjustManualAwards,
            new(1, null, null), "First Karma-created runner award"));
        Assert.IsNotNull(review.Preview, review.Error);
        var command = review.Preview.Command with { ExplicitlyConfirmed = true };
        Assert.AreEqual(CharacterCareerReputationOutcome.Applied, reputation.Commit(command).Outcome);
        var rewards = new WorkspaceCharacterAfterRunRewardService(new FileWorkspaceStore(fixture.StateRoot));
        var rewardReview = rewards.Preview(new(fixture.Id, Guid.NewGuid(), Guid.NewGuid(),
            8, 12500, new DateTime(2078, 9, 7, 18, 0, 0), "First run"));
        Assert.IsNotNull(rewardReview.Preview, rewardReview.Error);
        Assert.AreEqual(15m, rewardReview.Preview.KarmaAfter);
        Assert.AreEqual(17580m, rewardReview.Preview.NuyenAfter);
        var rewardCommand = rewardReview.Preview.Command with { ExplicitlyConfirmed = true };
        Assert.AreEqual(CharacterAfterRunRewardOutcome.Applied, rewards.Commit(rewardCommand).Outcome);
        var after = new FileWorkspaceStore(fixture.StateRoot).Get(fixture.Id).Value!;
        Assert.AreEqual(before.ContentRevision + 2, after.ContentRevision);
        AssertJsonEqual(before.Document.AuxiliaryState.CharacterCreationFinalizationArchive!,
            after.Document.AuxiliaryState.CharacterCreationFinalizationArchive!);
        Assert.AreEqual(8, reputation.Read(fixture.Id).Snapshot!.Reputation.Inputs.CareerKarma);
        Assert.AreEqual(1, reputation.Read(fixture.Id).Snapshot!.Reputation.Inputs.StreetCred);
        Assert.AreEqual(CharacterCareerReputationOutcome.Replayed, reputation.Commit(command).Outcome);
        Assert.AreEqual(CharacterAfterRunRewardOutcome.Replayed, rewards.Commit(rewardCommand).Outcome);
        AssertJsonEqual(receipt, new CharacterCreationKarmaMetatypeService(coldStore, fixture.Resolver).ConfirmFinalization(request).Value!);
        Assert.AreEqual(after.ContentRevision, coldStore.Get(fixture.Id).Value!.ContentRevision);
    }

    [TestMethod]
    public void Karma_gear_direct_digests_preserve_every_real_catalog_row_and_canonical_edge_case()
    {
        using var fixture = new KarmaDiskFixture(includeGear: true);
        var authority = fixture.Service.Load(fixture.Id, includeGear: true).Value!.GearAuthority!;
        foreach (var option in authority.Options) CheckOption(option);
        CheckAuthority(authority);
        var first = authority.Options[0];
        foreach (decimal cost in new[] { 0m, 0.0000000000000000000000000001m, 1.2300m, decimal.MinValue, decimal.MaxValue })
        {
            var changed = first with
            {
                Name = "ä Español 漢字 \" <>& \\ \n 🐉",
                Category = null!, SourceBook = null!, Page = null!,
                PackageCost = cost, PackageQuantity = int.MinValue, Availability = int.MaxValue,
                PricingIsExact = false, AvailabilityIsExact = false, IsSelectable = false,
                SourceAnchorIds = ["z", "ä", null!, "a"], Blockers = ["later", "first"],
                SourceNodeXml = "<gear>\r\n\t&quote; \\ \u2028 </gear>", OptionDigest = "retained-option-digest"
            };
            CheckOption(changed);
            CheckAuthority(authority with { Options = [changed, first, null!], SourceAnchorIds = null!, Blockers = [null!, "z"] });
            CheckOption(changed with { Blockers = null!, SourceAnchorIds = null!, SourceNodeXml = null! });
        }
        CheckAuthority(authority with { Options = [], Blockers = [], SourceAnchorIds = [] });
        CheckAuthority(authority with { Options = null!, Blockers = null!, RulesetId = null!, ProfileDigest = null! });
        Assert.AreEqual(CharacterCreationGearRules.Compute(new { Schema = "chummer.sr5.creation-gear.source-node.v1", Xml = string.Empty }),
            CharacterCreationGearRules.ComputeSourceNodeDigest(null!));

        static void CheckOption(CharacterCreationGearCatalogOption option)
        {
            Assert.AreEqual(CharacterCreationGearRules.Compute(option with { OptionDigest = string.Empty }),
                CharacterCreationGearRules.ComputeOptionDigest(option), option.OptionId);
            Assert.AreEqual(CharacterCreationGearRules.Compute(new
                { Schema = "chummer.sr5.creation-gear.source-node.v1", Xml = option.SourceNodeXml ?? string.Empty }),
                CharacterCreationGearRules.ComputeSourceNodeDigest(option.SourceNodeXml));
        }
        static void CheckAuthority(CharacterCreationGearAuthority value)
            => Assert.AreEqual(CharacterCreationGearRules.Compute(value with { AuthorityDigest = string.Empty }),
                CharacterCreationGearRules.ComputeAuthorityDigest(value));
    }

    [TestMethod]
    public void Karma_gear_evaluation_still_validates_the_entire_catalog_once_and_never_trusts_a_stale_digest()
    {
        using var fixture = new KarmaDiskFixture(includeGear: true);
        var state = fixture.Service.Load(fixture.Id, includeGear: true).Value!;
        var authority = state.GearAuthority!;
        var resources = fixture.Service.Preview(state.Binding, HumanId, "mundane", [],
            resourceKarmaInvestment: 10m, gearSelections: []).Value!.Resources!;
        Assert.IsNotNull(CharacterCreationKarmaGearRules.Evaluate(authority, resources, []));
        Assert.IsNull(CharacterCreationKarmaGearRules.Evaluate(null!, resources, []));
        Assert.IsNull(CharacterCreationKarmaGearRules.Evaluate(authority with { IsAuthoritative = false }, resources, []));
        Assert.IsNull(CharacterCreationKarmaGearRules.Evaluate(authority with { AuthorityDigest = "stale" }, resources, []));
        var options = authority.Options.ToArray();
        // No selections: even an unused row must be admitted, not skipped.
        options[0] = options[0] with { PackageCost = options[0].PackageCost + 1m };
        var changed = authority with { Options = options };
        changed = changed with { AuthorityDigest = CharacterCreationGearRules.ComputeAuthorityDigest(changed) };
        Assert.IsNull(CharacterCreationKarmaGearRules.Evaluate(changed, resources, []));
        options[0] = options[0] with { OptionDigest = CharacterCreationGearRules.ComputeOptionDigest(options[0]) };
        changed = changed with { AuthorityDigest = CharacterCreationGearRules.ComputeAuthorityDigest(changed) };
        Assert.IsNotNull(CharacterCreationKarmaGearRules.Evaluate(changed, resources, []));
        options[0] = options[0] with { SourceNodeXml = "<gear>changed source</gear>" };
        options[0] = options[0] with { OptionDigest = CharacterCreationGearRules.ComputeOptionDigest(options[0]) };
        changed = changed with { AuthorityDigest = CharacterCreationGearRules.ComputeAuthorityDigest(changed) };
        Assert.IsNull(CharacterCreationKarmaGearRules.Evaluate(changed, resources, []));
    }

    [TestMethod]
    public void Karma_gear_purchases_use_funding_quote_and_survive_cold_reopen_and_exact_replay()
    {
        using var fixture = new KarmaDiskFixture(includeGear: true);
        Assert.IsNotNull(fixture.Service.Confirm(fixture.Request(HumanId, "mundane", [], resources: 10m)).Value);
        var before = fixture.Store.Get(fixture.Id).Value!;
        var state = fixture.Service.Load(fixture.Id, includeGear: true).Value!;
        Assert.IsNotNull(state.GearAuthority);
        var item = state.GearAuthority.Options.First(option => option.IsSelectable && option.PackageCost is > 0 and < 1000);
        CharacterCreationGearSelection[] basket = [new(item.OptionId, item.PackageQuantity * 2)];
        var request = fixture.Request(HumanId, "mundane", [], resources: 10m, gear: basket);
        var quote = fixture.Service.Preview(request.Binding, HumanId, "mundane", [], resourceKarmaInvestment: 10m,
            gearSelections: basket).Value!;
        Assert.IsTrue(quote.CanSelect);
        Assert.AreEqual(10m, quote.KarmaBudget.Used, "Equipment spends nuyen, not a second Karma charge.");
        Assert.AreEqual(20000m, quote.Gear!.Budget.TotalStartingNuyen);
        Assert.AreEqual(item.PackageCost * 2, quote.Gear.Budget.BasketCost);
        Assert.AreEqual(20000m - item.PackageCost * 2, quote.Gear.Budget.RemainingNuyen);
        Assert.AreEqual(quote.Resources!.QuoteDigest, quote.Gear.ResourcesQuoteDigest);
        AssertJsonEqual(before, fixture.Store.Get(fixture.Id).Value!);
        Assert.IsNotNull(fixture.Service.Confirm(request).Value);
        var coldStore = new FileWorkspaceStore(fixture.StateRoot);
        var saved = coldStore.Get(fixture.Id).Value!;
        Assert.AreEqual(before.ContentRevision + 1, saved.ContentRevision);
        Assert.AreEqual(saved.ContentRevision, saved.SavedRevision);
        Assert.AreEqual(before.Document.Content, saved.Document.Content);
        AssertJsonEqual(before.Document.AuxiliaryState.CharacterCreationKarmaMetatypeDecisions![0],
            saved.Document.AuxiliaryState.CharacterCreationKarmaMetatypeDecisions![0]);
        Assert.IsNull(saved.Document.AuxiliaryState.CharacterCreationResourcesDraft);
        Assert.IsNull(saved.Document.AuxiliaryState.CharacterCreationGearDraft);
        var cold = new CharacterCreationKarmaMetatypeService(coldStore, fixture.Resolver);
        var opened = cold.Open(fixture.Id);
        Assert.IsNotNull(opened.Value, string.Join(",", opened.Blockers));
        AssertJsonEqual(quote.Gear, opened.Value.Quote!.Gear);
        Assert.IsTrue(cold.Confirm(request).Value!.Replayed);
        AssertJsonEqual(saved, coldStore.Get(fixture.Id).Value!);
    }

    [TestMethod]
    public void Karma_gear_lower_funding_blocks_persisting_overspend_and_omission_cannot_erase_basket()
    {
        using var fixture = new KarmaDiskFixture(includeGear: true);
        var state = fixture.Service.Load(fixture.Id, includeGear: true).Value!;
        var item = state.GearAuthority!.Options.First(option => option.IsSelectable && option.PackageCost is > 0 and < 1000);
        CharacterCreationGearSelection[] basket = [new(item.OptionId, item.PackageQuantity)];
        Assert.IsNotNull(fixture.Service.Confirm(fixture.Request(HumanId, "mundane", [], resources: 10m, gear: basket)).Value);
        var before = fixture.Store.Get(fixture.Id).Value!;
        var opened = fixture.Service.Open(fixture.Id).Value!;
        var lower = fixture.Service.Preview(opened.State.Binding, HumanId, "mundane", [], resourceKarmaInvestment: 0m,
            gearSelections: basket).Value!;
        Assert.IsFalse(lower.CanSelect);
        CollectionAssert.Contains(lower.Blockers.ToArray(), CharacterCreationGearBlockers.InsufficientFunds);
        Assert.IsNull(fixture.Service.Confirm(new(lower.Binding, HumanId, lower.QuoteDigest, Guid.NewGuid(), true,
            "mundane", [], ResourceKarmaInvestment: 0m, GearSelections: basket)).Value);
        CollectionAssert.Contains(fixture.Service.Preview(opened.State.Binding, HumanId, "mundane", [], resourceKarmaInvestment: 10m)
            .Blockers.ToArray(), CharacterCreationKarmaMetatypeBlockers.GearSelectionRequired);
        AssertJsonEqual(before, fixture.Store.Get(fixture.Id).Value!);
        var empty = fixture.Service.Preview(opened.State.Binding, HumanId, "mundane", [], resourceKarmaInvestment: 0m,
            gearSelections: []).Value!;
        Assert.IsTrue(empty.CanSelect);
        Assert.IsNotNull(fixture.Service.Confirm(new(empty.Binding, HumanId, empty.QuoteDigest, Guid.NewGuid(), true,
            "mundane", [], ResourceKarmaInvestment: 0m, GearSelections: [])).Value);
        Assert.AreEqual(0, fixture.Service.Open(fixture.Id).Value!.Quote!.Gear!.Lines.Count);
    }

    [TestMethod]
    public void Karma_gear_rejects_invalid_baskets_disabled_or_unsupported_sources_and_stale_source_before_commit()
    {
        using var fixture = new KarmaDiskFixture(includeGear: true);
        var state = fixture.Service.Load(fixture.Id, includeGear: true).Value!;
        var item = state.GearAuthority!.Options.First(option => option.IsSelectable && option.PackageCost is > 0 and < 1000);
        var before = fixture.Store.Get(fixture.Id).Value!;
        foreach (CharacterCreationGearSelection[] invalid in new CharacterCreationGearSelection[][]
        {
            [new(item.OptionId, 0)], [new(item.OptionId, -1)], [new(item.OptionId, 1000001)],
            [new(item.OptionId, 1), new(item.OptionId, 1)], [null!], [new("unbound", 1)]
        })
            Assert.IsNull(fixture.Service.Preview(state.Binding, HumanId, "mundane", [], resourceKarmaInvestment: 10m,
                gearSelections: invalid).Value);
        foreach (var denied in state.GearAuthority.Options.Where(option => !option.IsSelectable).Take(3))
        {
            var quote = fixture.Service.Preview(state.Binding, HumanId, "mundane", [], resourceKarmaInvestment: 10m,
                gearSelections: [new(denied.OptionId, 1)]).Value!;
            Assert.IsFalse(quote.CanSelect);
        }
        var request = fixture.Request(HumanId, "mundane", [], resources: 10m, gear: [new(item.OptionId, 1)]);
        fixture.EditGear(item.Name, row => row.SetElementValue("cost", "9999"));
        Assert.IsNull(fixture.Service.Confirm(request).Value);
        AssertJsonEqual(before, fixture.Store.Get(fixture.Id).Value!);
    }

    [TestMethod]
    public void Karma_gear_history_rejects_rehashed_price_quantity_budget_and_funding_tampering()
    {
        using var fixture = new KarmaDiskFixture(includeGear: true);
        var state = fixture.Service.Load(fixture.Id, includeGear: true).Value!;
        var item = state.GearAuthority!.Options.First(option => option.IsSelectable && option.PackageCost is > 0 and < 1000);
        var quote = fixture.Service.Preview(state.Binding, HumanId, "mundane", [], resourceKarmaInvestment: 10m,
            gearSelections: [new(item.OptionId, item.PackageQuantity)]).Value!;
        var gear = quote.Gear!;
        CharacterCreationGearSelection[] basket = [new(item.OptionId, item.PackageQuantity)];
        Assert.IsTrue(CharacterCreationKarmaGearRules.IsValid(gear, quote.Resources, basket));
        var line = gear.Lines.Single() with { PackageCost = 0m, TotalCost = 0m };
        line = line with { LineDigest = CharacterCreationGearRules.ComputeLineDigest(line) };
        foreach (var changed in new[]
        {
            gear with { Lines = [line], Budget = new(20000m, 0m, 20000m, 0m, true, []) },
            gear with { Budget = gear.Budget with { RemainingNuyen = 20000m } },
            gear with { ResourcesQuoteDigest = CharacterCreationGearRules.ComputeUtf8("different funding") },
            gear with { Lines = [gear.Lines[0] with { Quantity = 100 }] }
        })
        {
            var rehashed = changed with { QuoteDigest = CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(
                changed with { QuoteDigest = string.Empty }) };
            Assert.IsFalse(CharacterCreationKarmaGearRules.IsValid(rehashed, quote.Resources, basket));
        }
        Assert.IsFalse(CharacterCreationKarmaGearRules.IsValid(gear with { Blockers = null! }, quote.Resources, basket));
        Assert.IsFalse(CharacterCreationKarmaGearRules.IsValid(gear with { Basis = null! }, quote.Resources, basket));
        Assert.IsFalse(CharacterCreationKarmaGearRules.IsValid(gear, quote.Resources, null));
    }

    [TestMethod]
    public void Karma_gear_source_drift_during_atomic_replace_does_not_commit_or_erase_history()
    {
        using var fixture = new KarmaDiskFixture(includeGear: true);
        Assert.IsNotNull(fixture.Service.Confirm(fixture.Request(HumanId, "mundane", [], resources: 10m)).Value);
        var state = fixture.Service.Load(fixture.Id, includeGear: true).Value!;
        var item = state.GearAuthority!.Options.First(option => option.IsSelectable && option.PackageCost is > 0 and < 1000);
        var request = fixture.Request(HumanId, "mundane", [], resources: 10m, gear: [new(item.OptionId, 1)]);
        var before = fixture.Store.Get(fixture.Id).Value!;
        fixture.Fault.Action = stage =>
        {
            if (stage == FileWorkspaceStoreFaultStage.AfterTempFileFlushed)
                fixture.EditGear(item.Name, row => row.SetElementValue("cost", "9999"));
        };
        Assert.IsNull(fixture.Service.Confirm(request).Value);
        AssertJsonEqual(before, new FileWorkspaceStore(fixture.StateRoot).Get(fixture.Id).Value!);
    }

    [TestMethod]
    public void Karma_gear_command_freezes_caller_basket_and_absent_fields_preserve_previous_history_bytes()
    {
        using var fixture = new KarmaDiskFixture(includeGear: true);
        var previous = fixture.Request(HumanId, "mundane", [], resources: 10m);
        Assert.IsFalse(JsonSerializer.Serialize(previous).Contains("GearSelections", StringComparison.Ordinal));
        Assert.IsFalse(JsonSerializer.Serialize(previous.Binding).Contains("GearAuthorityDigest", StringComparison.Ordinal));
        var state = fixture.Service.Load(fixture.Id, includeGear: true).Value!;
        var item = state.GearAuthority!.Options.First(option => option.IsSelectable && option.PackageCost is > 0 and < 1000);
        CharacterCreationGearSelection[] basket = [new(item.OptionId, 1)];
        var request = fixture.Request(HumanId, "mundane", [], resources: 10m, gear: basket);
        Assert.IsTrue(CharacterCreationKarmaMetatypeTransaction.TryFreezeRequest(request, out var frozen));
        basket[0] = new(item.OptionId, 2);
        Assert.AreEqual(1, frozen.GearSelections!.Single().Quantity);
        Assert.IsNotNull(fixture.Service.Confirm(frozen).Value);
        Assert.AreEqual(1, fixture.Service.Open(fixture.Id).Value!.Quote!.Gear!.Lines.Single().Quantity);
    }

    [TestMethod]
    public void Karma_gear_invalid_attribute_or_funding_changes_keep_the_draft_visible_for_repair()
    {
        using var fixture = new KarmaDiskFixture(includeGear: true, budget: 100);
        var state = fixture.Service.Load(fixture.Id, includeGear: true).Value!;
        foreach (int levels in new[] { 6, 5 })
        {
            var quote = fixture.Service.Preview(state.Binding, HumanId, "mundane", [new("BOD", levels)],
                resourceKarmaInvestment: 10m, gearSelections: []).Value;
            Assert.IsNotNull(quote, "Invalid upstream allocations must remain editable after adding equipment.");
            Assert.IsNotNull(quote.Attributes);
            Assert.IsNotNull(quote.Resources);
            Assert.IsNotNull(quote.Gear);
            Assert.IsFalse(quote.CanSelect);
            Assert.IsFalse(quote.Gear.CanSelect);
            Assert.IsNull(fixture.Service.Confirm(new(quote.Binding, HumanId, quote.QuoteDigest, Guid.NewGuid(), true,
                "mundane", [new("BOD", levels)], ResourceKarmaInvestment: 10m, GearSelections: [])).Value);
        }
    }

    [TestMethod]
    [DataRow("mundane", null, false, false, false)]
    [DataRow("0e741331-d776-4be8-abc5-4101228abdef", null, true, false, false)]
    [DataRow("55247bdc-c313-4614-ae15-5012308096ff", null, false, true, false)]
    [DataRow("9d53e1e4-3f31-40cb-bfbe-4b94f5ba757e", null, true, true, false)]
    [DataRow("c4b35412-bd91-45b4-b428-29da7edd5ff4", null, false, false, true)]
    public void Karma_grants_legacy_projection_keeps_racial_and_purchased_origins_and_compiles_effects(
        string talentId, string? unlock, bool magician, bool adept, bool technomancer)
    {
        using var fixture = new KarmaDiskFixture(includeSkills: true, includeGear: technomancer);
        var before = fixture.Store.Get(fixture.Id).Value!;
        var quote = QuoteKarmaGrants(fixture, talentId, unlock);
        var context = fixture.Resolver.TryCreateContext(before.Document.Content)!;
        Assert.IsTrue(context.TryResolveCreationKarmaGrantSources(ElfId, talentId, out var racial, out var talent));
        Assert.IsTrue(CharacterCreationKarmaGrantsLegacyProjector.TryProject(quote, racial, talent, out var graph, out var changes));
        var root = new XElement("character", graph);
        var racialQuality = root.Element("qualities")!.Elements("quality").Single(row => row.Element("name")!.Value == "Low-Light Vision");
        Assert.AreEqual("Metatype", racialQuality.Element("qualitysource")!.Value);
        Assert.AreEqual("False", racialQuality.Element("contributetolimit")!.Value);
        Assert.AreEqual("4", racialQuality.Element("bp")!.Value, "Keep source BP for later racial removal semantics.");
        Assert.AreEqual(magician ? "True" : "False", root.Element("magician")!.Value);
        Assert.AreEqual(adept ? "True" : "False", root.Element("adept")!.Value);
        Assert.AreEqual(technomancer ? "True" : "False", root.Element("technomancer")!.Value);
        Assert.AreEqual(magician || adept ? "True" : "False", root.Element("magenabled")!.Value);
        Assert.AreEqual(technomancer ? "True" : "False", root.Element("resenabled")!.Value);
        if (talentId != "mundane")
        {
            var saved = root.Element("qualities")!.Elements("quality").Single(row => row.Element("sourceid")!.Value == talentId);
            Assert.AreEqual("Selected", saved.Element("qualitysource")!.Value, "Karma buys this talent; it is not a Priority Heritage grant.");
            Assert.AreEqual(quote.Talent!.KarmaCost, (int)saved.Element("bp")!);
            Assert.IsNotNull(saved.Element("bonus")!.Element("enableattribute"));
            Assert.IsTrue(root.Element("improvements")!.Elements().Any());
            Assert.IsTrue(root.Element("improvements")!.Elements().All(row => row.Element("improvementttype")!.Value is not ("SkillBase" or "SkillGroupBase")));
        }
        var gear = root.Element("gears")!.Elements("gear").ToArray();
        Assert.AreEqual(technomancer ? 1 : 0, gear.Length);
        if (technomancer)
        {
            Assert.AreEqual("Living Persona", gear[0].Element("name")!.Value);
            Assert.AreEqual("0", gear[0].Element("cost")!.Value);
            var savedTalent = root.Element("qualities")!.Elements("quality").Single(row => row.Element("sourceid")!.Value == talentId);
            Assert.AreEqual(savedTalent.Element("guid")!.Value, gear[0].Element("parentid")!.Value);
        }
        Assert.AreEqual((decimal)quote.Talent!.KarmaCost, changes.Sum(change => change.KarmaCost));
        Assert.IsTrue(changes.All(change => change.SourceAnchorIds.Count > 0));
        var reopened = JsonSerializer.Deserialize<CharacterCreationKarmaMetatypeQuote>(JsonSerializer.Serialize(quote))!;
        Assert.IsTrue(CharacterCreationKarmaGrantsLegacyProjector.TryProject(reopened, racial, talent, out var again, out var againChanges));
        Assert.IsTrue(XNode.DeepEquals(root, new XElement("character", again)));
        AssertJsonEqual(changes, againChanges);
        AssertJsonEqual(before, fixture.Store.Get(fixture.Id).Value!);
    }

    [TestMethod]
    public void Karma_grants_legacy_projection_charges_profile_multiplier_once_without_scaling_saved_BP()
    {
        using var fixture = new KarmaDiskFixture(includeSkills: true, qualityMultiplier: 2);
        var quote = QuoteKarmaGrants(fixture, MagicianId);
        var context = fixture.Resolver.TryCreateContext(fixture.Store.Get(fixture.Id).Value!.Document.Content)!;
        Assert.IsTrue(context.TryResolveCreationKarmaGrantSources(ElfId, MagicianId, out var racial, out var talent));
        Assert.IsTrue(CharacterCreationKarmaGrantsLegacyProjector.TryProject(quote, racial, talent, out var graph, out var changes));
        var saved = graph.Single(item => item.Name == "qualities").Elements("quality")
            .Single(row => row.Element("sourceid")!.Value == MagicianId);
        Assert.AreEqual("30", saved.Element("bp")!.Value);
        Assert.AreEqual(60m, changes.Sum(change => change.KarmaCost));
        Assert.AreEqual(quote.Talent!.KarmaCost, changes.Single(change => change.TargetId == MagicianId).KarmaCost);
    }

    [TestMethod]
    public void Karma_grants_legacy_projection_preserves_explicit_aspect_without_priority_skill_grants()
    {
        using var fixture = new KarmaDiskFixture(includeSkills: true);
        var state = fixture.Service.Load(fixture.Id).Value!;
        string id = state.Talents!.Options.Single(item => item.Name == "Aspected Magician").OptionId;
        var quote = QuoteKarmaGrants(fixture, id, "Sorcery");
        var context = fixture.Resolver.TryCreateContext(fixture.Store.Get(fixture.Id).Value!.Document.Content)!;
        Assert.IsTrue(context.TryResolveCreationKarmaGrantSources(ElfId, id, out var racial, out var talent));
        Assert.IsTrue(CharacterCreationKarmaGrantsLegacyProjector.TryProject(quote, racial, talent, out var graph, out _));
        var improvements = graph.Single(item => item.Name == "improvements").Elements().ToArray();
        var special = improvements.Single(item => item.Element("improvementttype")!.Value == "SpecialSkills");
        Assert.AreEqual("Sorcery", special.Element("improvedname")!.Value);
        Assert.IsFalse(improvements.Any(item => item.Element("improvementttype")!.Value is "SkillBase" or "SkillGroupBase"));
    }

    [TestMethod]
    public void Karma_grants_legacy_projection_rejects_missing_racial_foreign_talent_and_rehashed_payment()
    {
        using var fixture = new KarmaDiskFixture(includeSkills: true);
        var quote = QuoteKarmaGrants(fixture, MagicianId);
        var before = fixture.Store.Get(fixture.Id).Value!;
        var context = fixture.Resolver.TryCreateContext(before.Document.Content)!;
        Assert.IsTrue(context.TryResolveCreationKarmaGrantSources(ElfId, MagicianId, out var racial, out var talent));
        Assert.IsFalse(CharacterCreationKarmaGrantsLegacyProjector.TryProject(quote, [], talent, out var graph, out var changes));
        Assert.IsEmpty(graph);
        Assert.IsEmpty(changes);
        Assert.IsFalse(CharacterCreationKarmaGrantsLegacyProjector.TryProject(quote, racial, racial[0], out graph, out changes));
        Assert.IsEmpty(graph);
        Assert.IsEmpty(changes);
        var forged = quote with { Talent = quote.Talent! with { KarmaCost = 0 }, QuoteDigest = string.Empty };
        forged = forged with { QuoteDigest = CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(forged) };
        Assert.IsFalse(CharacterCreationKarmaGrantsLegacyProjector.TryProject(forged, racial, talent, out graph, out changes));
        Assert.IsEmpty(graph);
        Assert.IsEmpty(changes);
        foreach (var malformed in new[]
        {
            quote with { Schema = "unknown" },
            quote with { Metatype = quote.Metatype with { GrantedQualities = [null!] } },
            quote with { Qualities = quote.Qualities! with { Selections = [null!] } },
            quote with { Qualities = null },
            quote with { Skills = null },
            quote with { Attributes = null }
        })
        {
            Assert.IsFalse(CharacterCreationKarmaGrantsLegacyProjector.TryProject(malformed, racial, talent, out graph, out changes));
            Assert.IsEmpty(graph);
            Assert.IsEmpty(changes);
        }
        AssertJsonEqual(before, fixture.Store.Get(fixture.Id).Value!);
    }

    private static CharacterCreationKarmaMetatypeQuote QuoteKarmaGrants(KarmaDiskFixture fixture, string talentId, string? unlock = null)
    {
        var state = fixture.Service.Load(fixture.Id, includeSkills: true, includeQualities: true).Value!;
        var skills = new CharacterCreationKarmaSkillsSelection([NativeEnglish(state.SkillsCatalog!)], [], unlock);
        var quote = fixture.Service.Preview(state.Binding, ElfId, talentId, [], skills, 0, []).Value;
        Assert.IsNotNull(quote);
        Assert.IsTrue(quote.CanSelect, string.Join(",", quote.Blockers));
        return quote;
    }

    [TestMethod]
    [DataRow("a53d885d-a4a4-443d-b6a6-b0a55b0a96c7", "mundane", 0)]
    [DataRow("b3259991-b315-4dbe-ae3c-51f71a1116e2", "mundane", 1)]
    [DataRow(OrkId, "mundane", 1)]
    [DataRow("b3259991-b315-4dbe-ae3c-51f71a1116e2", "0e741331-d776-4be8-abc5-4101228abdef", 1)]
    public void Karma_grant_sources_resolve_racial_and_talent_rows_without_priority_or_gear(
        string metatypeId, string talentId, int racialCount)
    {
        using var fixture = new KarmaDiskFixture();
        var before = fixture.Store.Get(fixture.Id).Value!;
        var context = fixture.Resolver.TryCreateContext(before.Document.Content)!;
        Assert.IsTrue(context.TryResolveCreationKarmaGrantSources(metatypeId, talentId, out var racial, out var talent));
        Assert.AreEqual(racialCount, racial.Count);
        Assert.IsTrue(racial.All(CharacterCreationTalentQualitySourceRules.IsValidSource));
        if (racialCount != 0)
        {
            Assert.AreEqual("Low-Light Vision", racial[0].Name);
            Assert.IsNotNull(XElement.Parse(racial[0].CanonicalSourceXml).Element("hide"),
                "A hidden racial source is retained, not replaced by a similarly named purchasable quality.");
        }
        if (talentId == "mundane") Assert.IsNull(talent);
        else
        {
            Assert.IsNotNull(talent);
            Assert.AreEqual(talentId, talent.SourceId);
            Assert.IsTrue(CharacterCreationTalentQualitySourceRules.IsValidSource(talent));
            Assert.IsTrue(context.TryResolveCreationKarmaTalents(out var catalog));
            var selected = catalog!.Options.Single(item => item.OptionId == talentId);
            Assert.AreEqual(CharacterCreationQualitiesRules.ComputeSourceNodeDigest(selected.SourceNodeXml), selected.SourceNodeDigest);
            Assert.AreEqual(XElement.Parse(selected.SourceNodeXml, LoadOptions.None).ToString(SaveOptions.DisableFormatting),
                talent.CanonicalSourceXml, "Raw quote XML and canonical effect XML retain their own digest formats.");
            Assert.AreEqual("MAG", XElement.Parse(talent.CanonicalSourceXml).Element("bonus")!.Element("enableattribute")!.Element("name")!.Value);
        }
        Assert.IsFalse(context.TryResolveCreationPrerequisiteAuthority(out _));
        AssertJsonEqual(before, fixture.Store.Get(fixture.Id).Value!);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Karma_grant_sources_require_and_retain_the_exact_living_persona_source(bool uppercaseSourceIdentity)
    {
        using var missing = new KarmaDiskFixture();
        var missingContext = missing.Resolver.TryCreateContext(missing.Store.Get(missing.Id).Value!.Document.Content)!;
        Assert.IsTrue(missingContext.TryResolveCreationKarmaTalents(out var talents));
        string technomancer = talents!.Options.Single(item => item.Name == "Technomancer").OptionId;
        Assert.IsFalse(missingContext.TryResolveCreationKarmaGrantSources(ElfId, technomancer, out var partial, out var absent));
        Assert.AreEqual(0, partial.Count);
        Assert.IsNull(absent);
        using var fixture = new KarmaDiskFixture(includeGear: true);
        if (uppercaseSourceIdentity)
            fixture.EditQuality(technomancer, row => row.SetElementValue("id", technomancer.ToUpperInvariant()));
        var before = fixture.Store.Get(fixture.Id).Value!;
        var context = fixture.Resolver.TryCreateContext(before.Document.Content)!;
        Assert.IsTrue(context.TryResolveCreationKarmaGrantSources(ElfId, technomancer, out var racial, out var talent));
        Assert.AreEqual(1, racial.Count);
        Assert.IsNotNull(talent);
        Assert.IsTrue(CharacterCreationTalentQualitySourceRules.IsValidSource(talent));
        var persona = talent.GrantedGearSources!.Single();
        Assert.AreEqual("Living Persona", persona.Name);
        Assert.AreEqual("Commlinks", persona.Category);
        Assert.IsTrue(CharacterCreationTalentQualitySourceRules.IsValidGearSource(persona));
        AssertJsonEqual(before, fixture.Store.Get(fixture.Id).Value!);
        fixture.EditGear("Living Persona", row => row.SetElementValue("source", "DISABLED"));
        var fresh = fixture.Resolver.TryCreateContext(before.Document.Content)!;
        Assert.IsFalse(fresh.TryResolveCreationKarmaGrantSources(ElfId, technomancer, out partial, out absent));
        Assert.AreEqual(0, partial.Count);
        Assert.IsNull(absent);
    }

    [TestMethod]
    [DataRow("disabled-book")]
    [DataRow("wrong-polarity")]
    [DataRow("duplicate-name")]
    public void Karma_grant_sources_reject_missing_ambiguous_or_disabled_racial_semantics(string change)
    {
        using var fixture = new KarmaDiskFixture();
        const string visionId = "8ec5c9bb-aeb9-42f2-a436-a60f764adfe4";
        fixture.EditQuality(visionId, row =>
        {
            if (change == "disabled-book") row.SetElementValue("source", "DISABLED");
            else if (change == "wrong-polarity") row.SetElementValue("category", "Negative");
            else
            {
                var duplicate = new XElement(row);
                duplicate.SetElementValue("id", Guid.NewGuid().ToString("D"));
                row.AddAfterSelf(duplicate);
            }
        });
        var before = fixture.Store.Get(fixture.Id).Value!;
        var context = fixture.Resolver.TryCreateContext(before.Document.Content)!;
        Assert.IsFalse(context.TryResolveCreationKarmaGrantSources(ElfId, "mundane", out var racial, out var talent));
        Assert.AreEqual(0, racial.Count);
        Assert.IsNull(talent);
        AssertJsonEqual(before, fixture.Store.Get(fixture.Id).Value!);
    }

    [TestMethod]
    public void Karma_grant_sources_reject_unknown_choices_and_source_drift_in_an_existing_context()
    {
        using var fixture = new KarmaDiskFixture();
        var before = fixture.Store.Get(fixture.Id).Value!;
        var context = fixture.Resolver.TryCreateContext(before.Document.Content)!;
        Assert.IsFalse(context.TryResolveCreationKarmaGrantSources("unknown", "mundane", out _, out _));
        Assert.IsFalse(context.TryResolveCreationKarmaGrantSources(HumanId, "unknown", out _, out _));
        Assert.IsTrue(context.TryResolveCreationKarmaGrantSources(ElfId, MagicianId, out _, out var first));
        fixture.EditQuality(MagicianId, row => row.SetElementValue("karma", 99));
        Assert.IsFalse(context.TryResolveCreationKarmaGrantSources(ElfId, MagicianId, out var racial, out var talent));
        Assert.AreEqual(0, racial.Count);
        Assert.IsNull(talent);
        var fresh = fixture.Resolver.TryCreateContext(before.Document.Content)!;
        Assert.IsTrue(fresh.TryResolveCreationKarmaGrantSources(ElfId, MagicianId, out _, out var changed));
        Assert.AreNotEqual(first!.SourceNodeDigest, changed!.SourceNodeDigest);
        AssertJsonEqual(before, fixture.Store.Get(fixture.Id).Value!);
    }

    [TestMethod]
    public void Karma_qualities_fund_skills_and_resources_from_the_same_pool_and_cold_reopen()
    {
        using var fixture = new KarmaDiskFixture(budget: 10, includeSkills: true);
        var state = fixture.Service.Load(fixture.Id, includeSkills: true, includeQualities: true).Value!;
        Assert.IsNotNull(state.QualitiesCatalog);
        var source = fixture.Resolver.TryCreateContext(fixture.Store.Get(fixture.Id).Value!.Document.Content)!;
        Assert.IsFalse(source.TryResolveCreationQualitiesAuthority(out _), "Karma must not acquire Priority authority.");
        var negative = state.QualitiesCatalog.Options.Single(item => item.Name == "Unsteady Hands");
        Assert.IsTrue(negative.IsSelectable, negative.DisableReasonKey);
        var pistols = state.SkillsCatalog!.ActiveSkills.Single(item => item.Name == "Pistols");
        var skills = new CharacterCreationKarmaSkillsSelection(
            [NativeEnglish(state.SkillsCatalog), new(pistols.SourceSkillId, pistols.Kind, 3)], []);
        var before = fixture.Store.Get(fixture.Id).Value!;
        var quote = fixture.Service.Preview(state.Binding, HumanId, "mundane", [], skills, 2.5m, [negative.OptionId]).Value!;
        Assert.IsTrue(quote.CanSelect, string.Join(",", quote.Blockers));
        Assert.AreEqual(-7, quote.Qualities!.Costs.NetKarmaSpent);
        Assert.AreEqual(17, quote.Skills!.KarmaAvailable);
        Assert.AreEqual(12m, quote.Skills.KarmaUsed);
        Assert.AreEqual(5m, quote.Resources!.KarmaAvailable);
        Assert.AreEqual(2.5m, quote.KarmaBudget.Remaining);
        AssertJsonEqual(before, fixture.Store.Get(fixture.Id).Value!);
        var request = new CharacterCreationKarmaMetatypeConfirmRequest(quote.Binding, HumanId, quote.QuoteDigest,
            Guid.NewGuid(), true, "mundane", [], skills, 2.5m, [negative.OptionId]);
        Assert.IsNotNull(fixture.Service.Confirm(request).Value);
        var coldStore = new FileWorkspaceStore(fixture.StateRoot);
        var saved = coldStore.Get(fixture.Id).Value!;
        Assert.AreEqual(before.ContentRevision + 1, saved.ContentRevision);
        Assert.AreEqual(saved.ContentRevision, saved.SavedRevision);
        Assert.AreEqual(before.Document.Content, saved.Document.Content, "Qualities remain pending source effects, not legacy character mutations.");
        var cold = new CharacterCreationKarmaMetatypeService(coldStore, fixture.Resolver);
        var opened = cold.Open(fixture.Id);
        Assert.IsNotNull(opened.Value, string.Join(",", opened.Blockers));
        Assert.AreEqual(2.5m, opened.Value.Quote!.KarmaBudget.Remaining);
        Assert.AreEqual(-7, opened.Value.Quote.Qualities!.Selections.Single().KarmaCost);
        Assert.IsTrue(cold.Confirm(request).Value!.Replayed);
        CollectionAssert.Contains(cold.Preview(opened.Value.State.Binding, HumanId, "mundane", [], skills, 2.5m).Blockers.ToArray(),
            CharacterCreationKarmaMetatypeBlockers.QualitiesSelectionRequired);
        var cleared = cold.Preview(opened.Value.State.Binding, HumanId, "mundane", [], skills, 2.5m, []);
        Assert.IsFalse(cleared.Value?.CanSelect == true, "Removing the credit cannot leave the overspent skills/resources approved.");
        AssertJsonEqual(saved, coldStore.Get(fixture.Id).Value!);
    }

    [TestMethod]
    public void Karma_qualities_replace_pending_selection_without_rewriting_old_decisions()
    {
        using var fixture = new KarmaDiskFixture(configureSettings: EnableQualityBook);
        Assert.IsNotNull(fixture.Service.Confirm(fixture.Request(HumanId, "mundane", [], null, 10.5m)).Value);
        var before = fixture.Store.Get(fixture.Id).Value!;
        var catalog = fixture.Service.Load(fixture.Id, includeQualities: true).Value!.QualitiesCatalog!;
        string positive = catalog.Options.Single(item => item.Name == "Overclocker").OptionId;
        string negative = catalog.Options.Single(item => item.Name == "Unsteady Hands").OptionId;
        var request = fixture.Request(HumanId, "mundane", [], null, 10.5m, [positive, negative]);
        Assert.IsNotNull(fixture.Service.Confirm(request).Value);
        var after = fixture.Store.Get(fixture.Id).Value!;
        Assert.AreEqual(8.5m, fixture.Service.Load(fixture.Id).Value!.KarmaBudget.Used);
        AssertJsonEqual(before.Document.AuxiliaryState.CharacterCreationKarmaMetatypeDecisions![0],
            after.Document.AuxiliaryState.CharacterCreationKarmaMetatypeDecisions![0]);
        Assert.IsNotNull(fixture.Service.Confirm(fixture.Request(HumanId, "mundane", [], null, 10.5m, [positive])).Value);
        Assert.AreEqual(15.5m, fixture.Service.Open(fixture.Id).Value!.Quote!.KarmaBudget.Used);
        Assert.IsNotNull(fixture.Service.Confirm(fixture.Request(HumanId, "mundane", [], null, 10.5m, [])).Value);
        Assert.AreEqual(10.5m, fixture.Service.Open(fixture.Id).Value!.Quote!.KarmaBudget.Used);
    }

    [TestMethod]
    public void Karma_qualities_use_profile_multipliers_excess_caps_and_source_effects()
    {
        using var fixture = new KarmaDiskFixture(qualityMultiplier: 2, configureSettings: row =>
        {
            EnableQualityBook(row);
            row.SetElementValue("qualitykarmalimit", 7);
            row.SetElementValue("exceedpositivequalities", true);
            row.SetElementValue("exceedpositivequalitiescostdoubled", true);
            row.SetElementValue("exceednegativequalities", true);
            row.SetElementValue("exceednegativequalitiesnobonus", true);
        });
        var state = fixture.Service.Load(fixture.Id, includeQualities: true).Value!;
        var selected = state.QualitiesCatalog!.Options.Where(item => item.Name is "Overclocker" or "Unsteady Hands").ToArray();
        Assert.AreEqual(2, selected.Length);
        var ids = selected.Select(item => item.OptionId).ToArray();
        var quote = fixture.Service.Preview(state.Binding, HumanId, "mundane", [], null, 0m, ids).Value!;
        Assert.IsTrue(quote.CanSelect, string.Join(",", quote.Blockers));
        Assert.AreEqual(13, quote.Qualities!.Costs.PositiveKarmaSpent);
        Assert.AreEqual(7, quote.Qualities.Costs.NegativeKarmaGranted);
        Assert.AreEqual(794m, quote.KarmaBudget.Remaining);
        Assert.IsTrue(quote.Qualities.Selections.Single(item => item.Name == "Overclocker").SourceNodeXml.Contains("<overclocker", StringComparison.Ordinal));
        Assert.IsNotNull(fixture.Service.Confirm(fixture.Request(HumanId, "mundane", [], null, 0m, ids)).Value);
        Assert.AreEqual(6m, fixture.Service.Open(fixture.Id).Value!.Quote!.KarmaBudget.Used);
    }

    [TestMethod]
    public void Karma_qualities_reject_unresolved_disabled_duplicate_and_invented_selections_without_writing()
    {
        using var fixture = new KarmaDiskFixture(configureSettings: row =>
            row.Element("books")!.Elements("book").Where(book => book.Value == "RF").Remove());
        var state = fixture.Service.Load(fixture.Id, includeQualities: true).Value!;
        var before = fixture.Store.Get(fixture.Id).Value!;
        var catalog = state.QualitiesCatalog!;
        string valid = catalog.Options.Single(item => item.Name == "Unsteady Hands").OptionId;
        var invalid = new[]
        {
            new[] { valid, valid }, new[] { "invented" },
            new[] { catalog.Options.Single(item => item.Name == "Distinctive Style").OptionId },
            new[] { catalog.Options.Single(item => item.Name == "Code of Honor").OptionId },
            new[] { catalog.Options.Single(item => item.Name == "Overclocker").OptionId }
        };
        foreach (var ids in invalid)
            Assert.IsFalse(fixture.Service.Preview(state.Binding, HumanId, "mundane", [], null, null, ids).Value?.CanSelect == true,
                string.Join(",", ids));
        CollectionAssert.Contains(fixture.Service.Preview(state.Binding, HumanId, "mundane", null, null, null, [valid]).Blockers.ToArray(),
            CharacterCreationKarmaMetatypeBlockers.AttributeSelectionRequired);
        var request = fixture.Request(HumanId, "mundane", [], null, null, [valid]);
        Assert.IsNull(fixture.Service.Confirm(request with { ExplicitlyConfirmed = false }).Value);
        AssertJsonEqual(before, fixture.Store.Get(fixture.Id).Value!);
    }

    [TestMethod]
    public void Karma_qualities_apply_caps_and_disable_grant_only_source_choices_in_catalog()
    {
        using var fixture = new KarmaDiskFixture(configureSettings: row =>
        {
            row.SetElementValue("qualitykarmalimit", 4);
            row.SetElementValue("exceedpositivequalities", false);
            row.SetElementValue("exceednegativequalities", false);
        });
        var state = fixture.Service.Load(fixture.Id, includeQualities: true).Value!;
        var negative = state.QualitiesCatalog!.Options.Single(item => item.Name == "Unsteady Hands");
        var positive = state.QualitiesCatalog.Options.Single(item => item.Name == "Overclocker");
        var before = fixture.Store.Get(fixture.Id).Value!;
        foreach (var (option, blocker) in new[] {
            (negative, CharacterCreationQualitiesBlockers.NegativeLimitExceeded),
            (positive, CharacterCreationQualitiesBlockers.PositiveLimitExceeded) })
        {
            var preview = fixture.Service.Preview(state.Binding, HumanId, "mundane", [], null, null, [option.OptionId]);
            Assert.IsNotNull(preview.Value);
            Assert.IsFalse(preview.Value.CanSelect);
            CollectionAssert.Contains(preview.Value.Blockers.ToArray(), blocker);
        }
        fixture.EditQuality(negative.SourceId.ToString("D"), row => row.Add(new XElement("onlyprioritygiven")));
        var changed = fixture.Service.Load(fixture.Id, includeQualities: true).Value!;
        var grantOnly = changed.QualitiesCatalog!.Options.Single(item => item.OptionId == negative.OptionId);
        Assert.IsFalse(grantOnly.IsSelectable);
        Assert.IsNotNull(grantOnly.DisableReasonKey);
        Assert.IsFalse(fixture.Service.Preview(changed.Binding, HumanId, "mundane", [], null, null, [grantOnly.OptionId]).Value?.CanSelect == true);
        AssertJsonEqual(before, fixture.Store.Get(fixture.Id).Value!);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Karma_qualities_source_change_before_or_during_commit_is_rejected(bool duringWrite)
    {
        using var fixture = new KarmaDiskFixture();
        var option = fixture.Service.Load(fixture.Id, includeQualities: true).Value!.QualitiesCatalog!.Options.Single(item => item.Name == "Unsteady Hands");
        var request = fixture.Request(HumanId, "mundane", [], null, null, [option.OptionId]);
        var before = fixture.Store.Get(fixture.Id).Value!;
        void Change() => fixture.EditQuality(option.SourceId.ToString("D"), row => row.SetElementValue("karma", -6));
        if (duringWrite) fixture.Fault.Action = stage => { if (stage == FileWorkspaceStoreFaultStage.AfterTempFileFlushed) Change(); };
        else Change();
        Assert.IsNull(fixture.Service.Confirm(request).Value);
        AssertJsonEqual(before, new FileWorkspaceStore(fixture.StateRoot).Get(fixture.Id).Value!);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Karma_qualities_atomic_recovery_and_frozen_request_prevent_double_credit(bool afterReplace)
    {
        using var fixture = new KarmaDiskFixture();
        string id = fixture.Service.Load(fixture.Id, includeQualities: true).Value!.QualitiesCatalog!.Options.Single(item => item.Name == "Unsteady Hands").OptionId;
        var callerIds = new[] { id };
        var request = fixture.Request(HumanId, "mundane", [], null, null, callerIds);
        var before = fixture.Store.Get(fixture.Id).Value!;
        fixture.Fault.Action = stage =>
        {
            if (stage == FileWorkspaceStoreFaultStage.AfterTempFileFlushed) callerIds[0] = "caller-mutated";
            if (stage == (afterReplace ? FileWorkspaceStoreFaultStage.AfterTargetReplaced : FileWorkspaceStoreFaultStage.AfterTempFileFlushed))
                throw new IOException("Injected quality draft persistence failure");
        };
        fixture.Service.Confirm(request);
        fixture.Fault.Action = null;
        var saved = new FileWorkspaceStore(fixture.StateRoot).Get(fixture.Id).Value!;
        if (afterReplace)
        {
            Assert.AreEqual(before.ContentRevision + 1, saved.ContentRevision);
            Assert.AreEqual(807m, fixture.Service.Open(fixture.Id).Value!.Quote!.KarmaBudget.Remaining);
            Assert.IsTrue(fixture.Service.Confirm(request with { QualityOptionIds = [id] }).Value!.Replayed);
        }
        else AssertJsonEqual(before, saved);
    }

    [TestMethod]
    public void Karma_qualities_rehashed_cost_forgery_fails_intrinsic_source_projection()
    {
        using var fixture = new KarmaDiskFixture();
        var state = fixture.Service.Load(fixture.Id, includeQualities: true).Value!;
        var option = state.QualitiesCatalog!.Options.Single(item => item.Name == "Unsteady Hands");
        var request = fixture.Request(HumanId, "mundane", [], null, null, [option.OptionId]);
        Assert.IsNotNull(fixture.Service.Confirm(request).Value);
        var saved = fixture.Store.Get(fixture.Id).Value!;
        var decision = saved.Document.AuxiliaryState.CharacterCreationKarmaMetatypeDecisions![0];
        var forgedOption = option with { KarmaCost = -700 };
        forgedOption = forgedOption with { OptionDigest = CharacterCreationQualitiesRules.ComputeOptionDigest(forgedOption) };
        var forged = decision.Quote.Qualities! with { Selections = [forgedOption], QuoteDigest = string.Empty };
        forged = forged with { QuoteDigest = CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(forged) };
        var quote = decision.Quote with { Qualities = forged, QuoteDigest = string.Empty };
        quote = quote with { QuoteDigest = CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(quote) };
        decision = decision with { Quote = quote, Command = decision.Command with { QuoteDigest = quote.QuoteDigest } };
        decision = decision with { DecisionDigest = CharacterCreationKarmaMetatypeTransaction.DecisionDigest(decision) };
        Assert.IsFalse(CharacterCreationKarmaMetatypeTransaction.IsValidLedger(fixture.Id, saved.ContentRevision,
            saved.Document.AuxiliaryState with { CharacterCreationKarmaMetatypeDecisions = [decision] }));
        using var old = JsonDocument.Parse(JsonSerializer.Serialize(request with { QualityOptionIds = null }));
        Assert.IsFalse(old.RootElement.TryGetProperty("QualityOptionIds", out _));
        using var oldBinding = JsonDocument.Parse(JsonSerializer.Serialize(request.Binding with
            { QualitiesPolicyDigest = null, QualitiesCatalogDigest = null }));
        Assert.IsFalse(oldBinding.RootElement.TryGetProperty("QualitiesPolicyDigest", out _));
        Assert.IsFalse(oldBinding.RootElement.TryGetProperty("QualitiesCatalogDigest", out _));
        using var oldQuote = JsonDocument.Parse(JsonSerializer.Serialize(quote with { Qualities = null }));
        Assert.IsFalse(oldQuote.RootElement.TryGetProperty("Qualities", out _));
        Assert.IsTrue(CharacterCreationKarmaMetatypeTransaction.IsValidHistory(fixture.Store.Get(fixture.Id).Value!));
    }

    private static void EnableQualityBook(XElement row)
    {
        if (!row.Element("books")!.Elements("book").Any(book => book.Value == "RF"))
            row.Element("books")!.Add(new XElement("book", "RF"));
    }

    [TestMethod]
    public void Karma_qualities_admission_freezes_all_collections_before_reusing_validation()
    {
        using var fixture = new KarmaDiskFixture();
        var state = fixture.Service.Load(fixture.Id, includeQualities: true).Value!;
        var original = state.QualitiesCatalog!;
        var human = state.Options.Single(item => item.OptionId == HumanId);
        var mundane = state.Talents!.Options.Single(item => item.OptionId == "mundane");
        var option = original.Options.Single(item => item.Name == "Unsteady Hands");
        string[] anchors = option.SourceAnchorIds.ToArray();
        string[] policyAnchors = original.Policy.SourceAnchorIds.ToArray();
        var options = original.Options.Select(item => item == option ? item with { SourceAnchorIds = anchors } : item).ToArray();
        var source = original with { Options = options, Policy = original.Policy with { SourceAnchorIds = policyAnchors } };
        var admitted = CharacterCreationKarmaQualitiesRules.AdmittedCatalog.TryCreate(source)!;
        Assert.IsNotNull(admitted);
        AssertJsonEqual(original, admitted.Catalog);
        var expected = CharacterCreationKarmaQualitiesRules.Evaluate(original, human, mundane, [option.OptionId])!;
        AssertJsonEqual(expected, admitted.Evaluate(human, mundane, [option.OptionId])!);

        // A resolver or DTO consumer must not be able to change the validated
        // graph through an aliased option, policy or nested anchor collection.
        anchors[0] = "changed-option-source";
        policyAnchors[0] = "changed-policy-source";
        options[0] = options[0] with { KarmaCost = 9999 };
        AssertJsonEqual(original, admitted.Catalog);
        AssertJsonEqual(expected, admitted.Evaluate(human, mundane, [option.OptionId])!);
        Assert.IsNull(CharacterCreationKarmaQualitiesRules.AdmittedCatalog.TryCreate(source));
        Assert.IsNull(CharacterCreationKarmaQualitiesRules.Evaluate(source, human, mundane, [option.OptionId]),
            "A new public evaluation must validate again, not reuse the previous admission.");

        Assert.ThrowsExactly<NotSupportedException>(() =>
            ((IList<CharacterCreationQualityCatalogOption>)admitted.Catalog.Options)[0] = options[0]);
        Assert.ThrowsExactly<NotSupportedException>(() =>
            ((IList<string>)admitted.Catalog.Policy.SourceAnchorIds)[0] = "forged");
        Assert.ThrowsExactly<NotSupportedException>(() =>
            ((IList<string>)admitted.Catalog.Options.Single(item => item.OptionId == option.OptionId).SourceAnchorIds)[0] = "forged");
    }

    [TestMethod]
    public void Karma_qualities_admission_rejects_unselected_tampering_and_still_projects_selected_source_effects()
    {
        using var fixture = new KarmaDiskFixture();
        var state = fixture.Service.Load(fixture.Id, includeQualities: true).Value!;
        var catalog = state.QualitiesCatalog!;
        var human = state.Options.Single(item => item.OptionId == HumanId);
        var mundane = state.Talents!.Options.Single(item => item.OptionId == "mundane");
        var option = catalog.Options.Single(item => item.Name == "Unsteady Hands");
        var tampered = catalog with { Options = catalog.Options.Select(item => item.OptionId != option.OptionId
            ? item with { KarmaCost = item.KarmaCost + 1 } : item).ToArray() };
        Assert.IsNull(CharacterCreationKarmaQualitiesRules.AdmittedCatalog.TryCreate(tampered));
        Assert.IsNull(CharacterCreationKarmaQualitiesRules.Evaluate(tampered, human, mundane, [option.OptionId]));

        // A coherent hash is not source-effect authority: the selected row must
        // still agree with its original XML, even inside a reused admission.
        var forged = option with { KarmaCost = -700 };
        forged = forged with { OptionDigest = CharacterCreationQualitiesRules.ComputeOptionDigest(forged) };
        var rehashed = catalog with { Options = catalog.Options.Select(item => item.OptionId == option.OptionId ? forged : item).ToArray() };
        rehashed = rehashed with { CatalogDigest = CharacterCreationKarmaQualitiesRules.CatalogDigest(rehashed) };
        var admitted = CharacterCreationKarmaQualitiesRules.AdmittedCatalog.TryCreate(rehashed);
        Assert.IsNotNull(admitted, "This test deliberately supplies internally coherent catalog hashes.");
        Assert.IsNull(admitted.Evaluate(human, mundane, [option.OptionId]));
        Assert.IsNull(admitted.Evaluate(human, mundane, ["invented"]));
        Assert.IsNull(admitted.Evaluate(human, mundane, [option.OptionId, option.OptionId]));
    }

    [TestMethod]
    public void Karma_qualities_service_does_not_retain_catalog_admission_across_public_calls()
    {
        using var fixture = new KarmaDiskFixture();
        var before = fixture.Store.Get(fixture.Id).Value!;
        var first = fixture.Service.Load(fixture.Id, includeQualities: true).Value!;
        var second = fixture.Service.Load(fixture.Id, includeQualities: true).Value!;
        Assert.AreNotSame(first.QualitiesCatalog, second.QualitiesCatalog);
        AssertJsonEqual(first, second);
        var selected = first.QualitiesCatalog!.Options.Single(item => item.Name == "Unsteady Hands");
        string id = selected.OptionId;
        var expected = fixture.Service.Preview(first.Binding, HumanId, "mundane", [], null, null, [id]).Value!;
        Assert.IsTrue(expected.CanSelect);
        fixture.EditQuality(selected.SourceId.ToString("D"), row => row.SetElementValue("karma", -8));
        var changed = fixture.Service.Preview(first.Binding, HumanId, "mundane", [], null, null, [id]);
        Assert.IsNull(changed.Value, "Public preview must not retain an admission after source drift.");
        AssertJsonEqual(before, fixture.Store.Get(fixture.Id).Value!);
    }

    [TestMethod]
    public void Karma_resources_share_budget_and_survive_cold_reopen_without_rewriting_prior_decisions()
    {
        using var fixture = new KarmaDiskFixture(includeSkills: true);
        var skills = GroupAndKnowledge(KarmaSkillsSources(fixture).Catalog);
        CharacterCreationKarmaAttributeAllocation[] attributes = [new("BOD", 2)];
        Assert.IsNotNull(fixture.Service.Confirm(fixture.Request(HumanId, "mundane", attributes, skills)).Value);
        var before = fixture.Store.Get(fixture.Id).Value!;
        var request = fixture.Request(HumanId, "mundane", attributes, skills, 10.5m);
        var quote = fixture.Service.Preview(request.Binding, HumanId, "mundane", attributes, skills, 10.5m).Value!;
        Assert.AreEqual(235m, quote.Resources!.Policy.MaximumKarmaInvestment);
        Assert.AreEqual(21000m, quote.Resources.NuyenFromKarma);
        Assert.AreEqual(688m, quote.Resources.KarmaAvailable);
        Assert.AreEqual(122.5m, quote.KarmaBudget.Used);
        Assert.AreEqual(677.5m, quote.KarmaBudget.Remaining);
        AssertJsonEqual(before, fixture.Store.Get(fixture.Id).Value!);
        Assert.IsNotNull(fixture.Service.Confirm(request).Value);
        var coldStore = new FileWorkspaceStore(fixture.StateRoot);
        var saved = coldStore.Get(fixture.Id).Value!;
        Assert.AreEqual(before.ContentRevision + 1, saved.ContentRevision);
        Assert.AreEqual(saved.ContentRevision, saved.SavedRevision);
        Assert.AreEqual(before.Document.Content, saved.Document.Content, "This remains a pending creation decision, not final wealth.");
        AssertJsonEqual(before.Document.AuxiliaryState.CharacterCreationKarmaMetatypeDecisions![0],
            saved.Document.AuxiliaryState.CharacterCreationKarmaMetatypeDecisions![0]);
        var cold = new CharacterCreationKarmaMetatypeService(coldStore, fixture.Resolver);
        var opened = cold.Open(fixture.Id);
        Assert.IsNotNull(opened.Value, string.Join(",", opened.Blockers));
        Assert.AreEqual(21000m, opened.Value.Quote!.Resources!.NuyenFromKarma);
        Assert.AreEqual(677.5m, opened.Value.Quote.KarmaBudget.Remaining);
        Assert.IsTrue(cold.Confirm(request).Value!.Replayed);
        CollectionAssert.Contains(cold.Preview(opened.Value.State.Binding, HumanId, "mundane", attributes, skills).Blockers.ToArray(),
            CharacterCreationKarmaMetatypeBlockers.ResourcesSelectionRequired);
        AssertJsonEqual(saved, coldStore.Get(fixture.Id).Value!);
        var clear = cold.Preview(opened.Value.State.Binding, HumanId, "mundane", attributes, skills, 0m).Value!;
        Assert.IsTrue(clear.CanSelect);
        Assert.AreEqual(688m, clear.KarmaBudget.Remaining);
        Assert.IsNotNull(cold.Confirm(new(clear.Binding, HumanId, clear.QuoteDigest, Guid.NewGuid(), true,
            "mundane", attributes, skills, 0m)).Value);
    }

    [TestMethod]
    public void Karma_resources_reject_missing_attributes_negative_investment_profile_cap_and_shared_overspend()
    {
        using var fixture = new KarmaDiskFixture(budget: 100);
        var state = fixture.Service.Load(fixture.Id).Value!;
        var before = fixture.Store.Get(fixture.Id).Value!;
        CollectionAssert.Contains(fixture.Service.Preview(state.Binding, HumanId, "mundane", null, null, 1m).Blockers.ToArray(),
            CharacterCreationKarmaMetatypeBlockers.AttributeSelectionRequired);
        CollectionAssert.Contains(fixture.Service.Preview(state.Binding, HumanId, "mundane", [], null, -1m).Blockers.ToArray(),
            CharacterCreationKarmaMetatypeBlockers.ResourceInvestmentInvalid);
        Assert.IsNull(fixture.Service.Preview(state.Binding, HumanId, "mundane", [], null, decimal.MaxValue).Value);
        var expensive = fixture.Service.Preview(state.Binding, HumanId, "mundane", [new("BOD", 2)], null, 76m).Value!;
        CollectionAssert.Contains(expensive.Blockers.ToArray(), CharacterCreationKarmaMetatypeBlockers.BudgetExceeded);
        var overCap = fixture.Service.Preview(state.Binding, HumanId, "mundane", [], null, 235.5m).Value!;
        CollectionAssert.Contains(overCap.Blockers.ToArray(), CharacterCreationKarmaResourcesRules.InvestmentLimitExceeded);
        Assert.IsNull(fixture.Service.Confirm(fixture.Request(HumanId, "mundane", [], null, 235.5m)).Value);
        AssertJsonEqual(before, fixture.Store.Get(fixture.Id).Value!);
    }

    [TestMethod]
    public void Karma_resources_use_profile_expression_attributes_fractional_cap_and_legacy_defaults()
    {
        using var custom = new KarmaDiskFixture(configureSettings: row =>
        {
            row.SetElementValue("chargenkarmatonuyenexpression", "{Karma} * 1500 + {BODUnaug} * 100");
            row.SetElementValue("nuyenmaxbp", "12.5");
        });
        var binding = custom.Service.Load(custom.Id).Value!.Binding;
        var quote = custom.Service.Preview(binding, HumanId, "mundane", [new("BOD", 2)], null, 12.5m).Value!;
        Assert.AreEqual(19050m, quote.Resources!.NuyenFromKarma);
        Assert.AreEqual(12.5m, quote.Resources.Policy.MaximumKarmaInvestment);
        using var legacy = new KarmaDiskFixture(configureSettings: row =>
        {
            row.Elements("chargenkarmatonuyenexpression").Remove();
            row.Elements("nuyenmaxbp").Remove();
            row.Elements("nuyenperbpwftm").Remove();
        });
        var legacyBinding = legacy.Service.Load(legacy.Id).Value!.Binding;
        var legacyQuote = legacy.Service.Preview(legacyBinding, HumanId, "mundane", [], null, 10m).Value!;
        Assert.AreEqual(10m, legacyQuote.Resources!.Policy.MaximumKarmaInvestment);
        Assert.AreEqual(20000m, legacyQuote.Resources.NuyenFromKarma);
        using var unlimited = new KarmaDiskFixture(configureSettings: row => row.SetElementValue("unrestrictednuyen", "True"));
        Assert.AreEqual((decimal)int.MaxValue, unlimited.Service.Load(unlimited.Id).Value!.ResourcesPolicy!.MaximumKarmaInvestment);
    }

    [TestMethod]
    [DataRow("{Karma} / 0")]
    [DataRow("{Unbound}")]
    [DataRow("99999999999999999999999999999999999999999 * {Karma}")]
    [DataRow("-1")]
    public void Karma_resources_invalid_expression_does_not_produce_a_selectable_quote(string expression)
    {
        using var fixture = new KarmaDiskFixture(configureSettings: row => row.SetElementValue("chargenkarmatonuyenexpression", expression));
        var before = fixture.Store.Get(fixture.Id).Value!;
        var result = fixture.Service.Preview(fixture.Service.Load(fixture.Id).Value!.Binding, HumanId, "mundane", [], null, 1m);
        Assert.IsNull(result.Value);
        CollectionAssert.Contains(result.Blockers.ToArray(), CharacterCreationKarmaMetatypeBlockers.ResourceInvestmentInvalid);
        AssertJsonEqual(before, fixture.Store.Get(fixture.Id).Value!);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Karma_resources_changed_source_before_or_during_commit_leaves_workspace_unchanged(bool duringWrite)
    {
        using var fixture = new KarmaDiskFixture();
        var request = fixture.Request(HumanId, "mundane", [], null, 10m);
        var before = fixture.Store.Get(fixture.Id).Value!;
        void Change() => fixture.EditSettings(row => row.SetElementValue("nuyenmaxbp", "5"));
        if (duringWrite) fixture.Fault.Action = stage => { if (stage == FileWorkspaceStoreFaultStage.AfterTempFileFlushed) Change(); };
        else Change();
        Assert.IsNull(fixture.Service.Confirm(request).Value);
        AssertJsonEqual(before, new FileWorkspaceStore(fixture.StateRoot).Get(fixture.Id).Value!);
    }

    [TestMethod]
    public void Karma_resources_remain_editable_when_attributes_exceed_limits_or_leave_no_budget()
    {
        using var fixture = new KarmaDiskFixture(budget: 100);
        var binding = fixture.Service.Load(fixture.Id).Value!.Binding;
        var before = fixture.Store.Get(fixture.Id).Value!;
        foreach (int levels in new[] { 6, 5 })
        {
            var quote = fixture.Service.Preview(binding, HumanId, "mundane", [new("BOD", levels)], null, 10m).Value;
            Assert.IsNotNull(quote, "Keep the current attributes visible so the player can repair the draft.");
            Assert.IsFalse(quote.CanSelect);
            Assert.IsNotNull(quote.Attributes);
            Assert.IsNotNull(quote.Resources);
            Assert.IsNull(fixture.Service.Confirm(new(binding, HumanId, quote.QuoteDigest, Guid.NewGuid(), true,
                "mundane", [new("BOD", levels)], null, 10m)).Value);
        }
        Assert.IsTrue(fixture.Service.Preview(binding, HumanId, "mundane", [], null, 10m).Value!.CanSelect);
        AssertJsonEqual(before, fixture.Store.Get(fixture.Id).Value!);
    }

    [TestMethod]
    [DataRow("nuyenmaxbp", "not-a-number", false)]
    [DataRow("nuyenmaxbp", "12", true)]
    [DataRow("unrestrictednuyen", "maybe", false)]
    [DataRow("chargenkarmatonuyenexpression", "1", true)]
    public void Karma_resources_reject_malformed_or_duplicate_profile_inputs(string field, string value, bool duplicate)
    {
        using var fixture = new KarmaDiskFixture(configureSettings: row =>
        {
            if (duplicate) row.Add(new XElement(field, value));
            else row.SetElementValue(field, value);
        });
        var state = fixture.Service.Load(fixture.Id).Value!;
        Assert.IsNull(state.ResourcesPolicy);
        CollectionAssert.Contains(fixture.Service.Preview(state.Binding, HumanId, "mundane", [], null, 1m).Blockers.ToArray(),
            CharacterCreationKarmaMetatypeBlockers.ResourcesAuthorityRequired);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Karma_resources_atomic_write_failure_is_recoverable_without_duplicate_spending(bool afterReplace)
    {
        using var fixture = new KarmaDiskFixture();
        var request = fixture.Request(HumanId, "mundane", [], null, 10.5m);
        var before = fixture.Store.Get(fixture.Id).Value!;
        fixture.Fault.Action = stage =>
        {
            if (stage == (afterReplace ? FileWorkspaceStoreFaultStage.AfterTargetReplaced : FileWorkspaceStoreFaultStage.AfterTempFileFlushed))
                throw new IOException("Injected resources persistence fault");
        };
        fixture.Service.Confirm(request);
        fixture.Fault.Action = null;
        var cold = new FileWorkspaceStore(fixture.StateRoot).Get(fixture.Id).Value!;
        if (afterReplace)
        {
            Assert.AreEqual(before.ContentRevision + 1, cold.ContentRevision);
            Assert.AreEqual(1, cold.Document.AuxiliaryState.CharacterCreationKarmaMetatypeDecisions!.Count);
            Assert.IsTrue(fixture.Service.Confirm(request).Value!.Replayed);
        }
        else AssertJsonEqual(before, cold);
    }

    [TestMethod]
    public void Karma_resources_rehashed_funding_forgery_is_rejected_and_absent_fields_preserve_old_serialization()
    {
        using var fixture = new KarmaDiskFixture();
        var request = fixture.Request(HumanId, "mundane", [], null, 10m);
        Assert.IsNotNull(fixture.Service.Confirm(request).Value);
        var saved = fixture.Store.Get(fixture.Id).Value!;
        var decision = saved.Document.AuxiliaryState.CharacterCreationKarmaMetatypeDecisions![0];
        var forged = decision.Quote.Resources! with { NuyenFromKarma = 999999m, QuoteDigest = string.Empty };
        forged = forged with { QuoteDigest = CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(forged) };
        var quote = decision.Quote with { Resources = forged, QuoteDigest = string.Empty };
        quote = quote with { QuoteDigest = CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(quote) };
        decision = decision with { Quote = quote, Command = decision.Command with { QuoteDigest = quote.QuoteDigest } };
        decision = decision with { DecisionDigest = CharacterCreationKarmaMetatypeTransaction.DecisionDigest(decision) };
        Assert.IsFalse(CharacterCreationKarmaMetatypeTransaction.IsValidLedger(fixture.Id, saved.ContentRevision,
            saved.Document.AuxiliaryState with { CharacterCreationKarmaMetatypeDecisions = [decision] }));
        using var oldBinding = JsonDocument.Parse(JsonSerializer.Serialize(request.Binding with { ResourcesPolicyDigest = null }));
        using var oldCommand = JsonDocument.Parse(JsonSerializer.Serialize(request with { ResourceKarmaInvestment = null }));
        using var oldQuote = JsonDocument.Parse(JsonSerializer.Serialize(quote with { Resources = null }));
        Assert.IsFalse(oldBinding.RootElement.TryGetProperty("ResourcesPolicyDigest", out _));
        Assert.IsFalse(oldCommand.RootElement.TryGetProperty("ResourceKarmaInvestment", out _));
        Assert.IsFalse(oldQuote.RootElement.TryGetProperty("Resources", out _));
    }

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public void Karma_open_loads_once_and_reissues_saved_review_without_writing(bool saved, bool withSkills)
    {
        using var fixture = new KarmaDiskFixture(includeSkills: withSkills);
        CharacterCreationKarmaSkillsSelection? skills = withSkills
            ? GroupAndKnowledge(KarmaSkillsSources(fixture).Catalog) : null;
        CharacterCreationKarmaAttributeAllocation[]? attributes = withSkills ? [new("BOD", 2)] : null;
        if (saved) Assert.IsNotNull(fixture.Service.Confirm(
            fixture.Request(HumanId, "mundane", attributes, skills)).Value);
        var before = fixture.Store.Get(fixture.Id).Value!;
        var counted = new KarmaOpenCountingResolver(fixture.Resolver);
        var service = new CharacterCreationKarmaMetatypeService(new FileWorkspaceStore(fixture.StateRoot), counted);
        var result = service.Open(fixture.Id);
        Assert.IsNotNull(result.Value, string.Join(",", result.Blockers));
        Assert.AreEqual(1, counted.Contexts, "Open must not load again to produce its review.");
        var opened = result.Value;
        if (saved)
        {
            Assert.IsNotNull(opened.Quote);
            Assert.AreEqual(opened.State.Binding, opened.Quote.Binding);
            Assert.AreEqual(opened.State.SnapshotDigest, opened.Quote.SnapshotDigest);
            Assert.AreNotEqual(opened.State.Selection!.Quote.Binding, opened.Quote.Binding);
            Assert.AreNotEqual(opened.State.Selection.Quote.QuoteDigest, opened.Quote.QuoteDigest);
            AssertJsonEqual(service.Preview(opened.State.Binding, HumanId, "mundane", attributes, skills).Value!, opened.Quote);
        }
        else
        {
            Assert.IsNull(opened.Quote, "Open must not invent a default selection.");
            Assert.IsNull(opened.State.SkillsCatalog, "A blank first step still loads skills lazily.");
        }
        AssertJsonEqual(before, fixture.Store.Get(fixture.Id).Value!);
        if (withSkills)
        {
            fixture.EditSkill("Pistols", row => row.SetElementValue("attribute", "LOG"));
            var drifted = service.Open(fixture.Id);
            Assert.IsNull(drifted.Value, "A saved review cannot outlive changed source inputs.");
            CollectionAssert.Contains(drifted.Blockers.ToArray(), CharacterCreationKarmaMetatypeBlockers.StaleBinding);
            AssertJsonEqual(before, fixture.Store.Get(fixture.Id).Value!);
        }
    }

    private sealed class KarmaOpenCountingResolver(ICharacterSourceDataResolver inner) : ICharacterSourceDataResolver
    {
        public int Contexts { get; private set; }
        public ICharacterSourceDataContext? TryCreateContext(string characterXml)
        { Contexts++; return inner.TryCreateContext(characterXml); }
    }

    [TestMethod]
    public void Karma_skills_confirmation_persists_compact_basis_and_reopens_with_exact_shared_budget()
    {
        using var fixture = new KarmaDiskFixture(includeSkills: true);
        Assert.IsNull(fixture.Service.Load(fixture.Id).Value!.SkillsCatalog, "First metatype page must not load the skills catalog.");
        var (catalog, _, _) = KarmaSkillsSources(fixture);
        var selection = GroupAndKnowledge(catalog);
        var request = fixture.Request(HumanId, "mundane", [new("BOD", 2)], selection);
        var before = fixture.Store.Get(fixture.Id).Value!;
        var result = fixture.Service.Confirm(request);
        Assert.IsNotNull(result.Value, string.Join(",", result.Blockers));
        Assert.IsFalse(result.Value.Replayed);
        var coldStore = new FileWorkspaceStore(fixture.StateRoot);
        var saved = coldStore.Get(fixture.Id).Value!;
        Assert.AreEqual(before.Document.Content, saved.Document.Content, "Pending allocation must not apply XML effects.");
        Assert.AreEqual(before.ContentRevision + 1, saved.ContentRevision);
        Assert.AreEqual(saved.ContentRevision, saved.SavedRevision);
        var cold = new CharacterCreationKarmaMetatypeService(coldStore, fixture.Resolver);
        var loaded = cold.Load(fixture.Id);
        Assert.IsNotNull(loaded.Value, string.Join(",", loaded.Blockers));
        Assert.AreEqual(688m, loaded.Value.KarmaBudget.Remaining); // BOD:25; Firearms:78; knowledge:9.
        var skills = loaded.Value.Selection!.Quote.Skills!;
        Assert.AreEqual(87m, skills.KarmaUsed);
        Assert.AreEqual(775, skills.KarmaAvailable);
        Assert.AreEqual(3, skills.Basis.Catalog.ActiveSkills.Count);
        Assert.AreEqual(2, skills.Basis.Catalog.KnowledgeSkills.Count);
        Assert.AreEqual(1, skills.Basis.Talents.Options.Count);
        Assert.AreEqual(catalog.CatalogDigest, skills.CatalogDigest);
        Assert.AreNotEqual(skills.CatalogDigest, skills.Basis.Catalog.CatalogDigest);
        Assert.AreEqual(skills.Basis.Catalog.CatalogDigest, skills.Access.CatalogDigest,
            "Subset permissions must not advertise the identity of the complete picker catalog.");
        Assert.IsLessThan(128 * 1024, JsonSerializer.SerializeToUtf8Bytes(loaded.Value.Selection).Length);
        Assert.IsTrue(cold.Confirm(request).Value!.Replayed);
        AssertJsonEqual(saved, coldStore.Get(fixture.Id).Value!);
        CollectionAssert.Contains(cold.Preview(loaded.Value.Binding, HumanId, "mundane", []).Blockers.ToArray(),
            CharacterCreationKarmaMetatypeBlockers.SkillsSelectionRequired);
        var clear = cold.Preview(loaded.Value.Binding, HumanId, "mundane", [], new([NativeEnglish(catalog)], [])).Value!;
        Assert.IsTrue(clear.CanSelect);
        Assert.AreEqual(800m, clear.KarmaBudget.Remaining);
        Assert.IsNotNull(cold.Confirm(new(clear.Binding, HumanId, clear.QuoteDigest, Guid.NewGuid(), true,
            "mundane", [], new([NativeEnglish(catalog)], []))).Value);
        Assert.AreEqual(800m, cold.Load(fixture.Id).Value!.KarmaBudget.Remaining);
    }

    [TestMethod]
    public void Karma_skills_legacy_projection_separates_personal_group_and_knowledge_ranks()
    {
        using var fixture = new KarmaDiskFixture(includeSkills: true);
        var before = fixture.Store.Get(fixture.Id).Value!;
        var (catalog, _, _) = KarmaSkillsSources(fixture);
        var selection = GroupAndKnowledge(catalog);
        var quote = QuoteKarmaSkills(fixture, selection)!;
        Assert.IsTrue(TryProjectKarmaSkills(fixture, quote, out var root, out var changes));
        Assert.IsNotNull(root);
        Assert.AreEqual("newskills", root.Name.LocalName);
        Assert.AreEqual("0", root.Element("skillptsmax")!.Value);
        Assert.AreEqual("0", root.Element("skillgrpsmax")!.Value);
        var group = root.Element("groups")!.Elements("group").Single();
        Assert.AreEqual("Firearms", group.Element("name")!.Value);
        Assert.AreEqual("3", group.Element("karma")!.Value);
        Assert.AreEqual("0", group.Element("base")!.Value);
        foreach (var skill in root.Element("skills")!.Elements("skill"))
        {
            Assert.AreEqual("2", skill.Element("karma")!.Value,
                "The three group ranks must not be stored in the personal rank a second time.");
            Assert.AreEqual("0", skill.Element("base")!.Value);
            Assert.AreEqual(5, (int)skill.Element("karma")! + (int)group.Element("karma")!);
        }
        var native = root.Element("knoskills")!.Elements("skill").Single(row => row.Element("name")!.Value == "English");
        Assert.AreEqual("True", native.Element("isnativelanguage")!.Value);
        Assert.AreEqual("0", native.Element("karma")!.Value);
        Assert.AreEqual("0", native.Element("base")!.Value);
        var knowledge = root.Element("knoskills")!.Elements("skill").Single(row => row.Element("name")!.Value == "Sprawl Life");
        Assert.AreEqual("3", knowledge.Element("base")!.Value);
        Assert.AreEqual("2", knowledge.Element("karma")!.Value);
        Assert.AreEqual("False", knowledge.Element("buywithkarma")!.Value);
        Assert.AreEqual(quote.KarmaUsed, changes.Sum(change => change.KarmaCost));
        Assert.IsTrue(changes.All(change => change.SourceAnchorIds.Count > 0));
        Assert.AreEqual(changes.Length, changes.Select(change => change.DeltaId).Distinct().Count());
        AssertJsonEqual(before, fixture.Store.Get(fixture.Id).Value!);

        var roundTripped = JsonSerializer.Deserialize<CharacterCreationKarmaSkillsQuote>(JsonSerializer.Serialize(quote))!;
        Assert.IsTrue(TryProjectKarmaSkills(fixture, roundTripped, out var reopened, out var reopenedChanges));
        Assert.IsTrue(XNode.DeepEquals(root, reopened));
        AssertJsonEqual(changes, reopenedChanges);
    }

    [TestMethod]
    [DataRow("knowledge-point", "False")]
    [DataRow("karma", "True")]
    public void Karma_skills_legacy_projection_keeps_specialization_payment_and_native_language(string payment, string paidWithKarma)
    {
        using var fixture = new KarmaDiskFixture(includeSkills: true);
        var (catalog, _, _) = KarmaSkillsSources(fixture);
        var source = catalog.KnowledgeSkills.Single(skill => skill.Name == "Sprawl Life");
        var spec = source.Specializations.First();
        var allocation = new CharacterCreationKarmaSkillAllocation(source.SourceSkillId, source.Kind, 2, 3,
            SpecializationOptionId: spec.OptionId, SpecializationPayment: payment);
        var quote = QuoteKarmaSkills(fixture, new([NativeEnglish(catalog), allocation], []))!;
        Assert.IsTrue(TryProjectKarmaSkills(fixture, quote, out var root, out var changes));
        var node = root!.Element("knoskills")!.Elements("skill").Single(item => item.Element("name")!.Value == source.Name);
        Assert.AreEqual(paidWithKarma, node.Element("buywithkarma")!.Value);
        var savedSpec = node.Element("specs")!.Element("spec")!;
        Assert.AreEqual(spec.Name, savedSpec.Element("name")!.Value);
        Assert.AreEqual("False", savedSpec.Element("free")!.Value,
            "Knowledge-point purchase is not a free granted specialization.");
        Assert.AreEqual("False", savedSpec.Element("expertise")!.Value);
        Assert.IsTrue(changes.Any(change => change.SourceAnchorIds.Contains(spec.SourceAnchorId)));
        Assert.AreEqual(quote.KarmaUsed, changes.Sum(change => change.KarmaCost));
    }

    [TestMethod]
    public void Karma_skills_legacy_projection_retains_exotic_identity_without_a_fake_specialization()
    {
        using var fixture = new KarmaDiskFixture(includeSkills: true);
        var (catalog, _, _) = KarmaSkillsSources(fixture);
        var source = catalog.ActiveSkills.Single(skill => skill.Name == "Exotic Melee Weapon");
        var identities = source.Specializations.Take(2).ToArray();
        var chosen = identities.Select(item => new CharacterCreationKarmaSkillAllocation(source.SourceSkillId,
            source.Kind, 2, SpecializationOptionId: item.OptionId, SpecializationPayment: "exotic-identity")).ToArray();
        var quote = QuoteKarmaSkills(fixture, new(chosen.Prepend(NativeEnglish(catalog)).ToArray(), []))!;
        Assert.IsTrue(TryProjectKarmaSkills(fixture, quote, out var root, out var changes));
        var rows = root!.Element("skills")!.Elements("skill").ToArray();
        Assert.AreEqual(2, rows.Length);
        Assert.AreNotEqual(rows[0].Element("guid")!.Value, rows[1].Element("guid")!.Value);
        Assert.IsTrue(rows.All(row => row.Element("suid")!.Value == source.SourceSkillId
            && row.Element("specs") is null && row.Element("karma")!.Value == "2"
            && row.Element("buywithkarma")!.Value == "False"));
        CollectionAssert.AreEquivalent(identities.Select(item => item.Name).ToArray(),
            rows.Select(row => row.Element("specific")!.Value).ToArray());
        Assert.AreEqual(quote.KarmaUsed, changes.Sum(change => change.KarmaCost));
    }

    [TestMethod]
    public void Karma_skills_legacy_projection_preserves_group_break_and_disabled_member_movement()
    {
        using var fixture = new KarmaDiskFixture(includeSkills: true);
        var (catalog, _, _) = KarmaSkillsSources(fixture);
        var group = catalog.SkillGroups.Single(item => item.Name == "Athletics");
        var raised = catalog.ActiveSkills.First(skill => skill.SkillGroup == "Athletics" && skill.Name != "Flight");
        var quote = QuoteKarmaSkills(fixture, new([NativeEnglish(catalog), new(raised.SourceSkillId, raised.Kind, 1)],
            [new(group.GroupId, 3)]))!;
        Assert.IsTrue(quote.CanSelect, string.Join(",", quote.Blockers));
        Assert.IsTrue(TryProjectKarmaSkills(fixture, quote, out var root, out _));
        Assert.AreEqual("True", root!.Element("groups")!.Element("group")!.Element("isbroken")!.Value);
        var flight = root.Element("skills")!.Elements("skill").Single(row => row.Element("name")!.Value == "Flight");
        Assert.AreEqual("True", flight.Element("requiresflymovement")!.Value);
        Assert.AreEqual("0", flight.Element("karma")!.Value);
    }

    [TestMethod]
    public void Karma_skills_legacy_projection_rejects_rehashed_cost_rating_source_and_payment_tampering()
    {
        using var fixture = new KarmaDiskFixture(includeSkills: true);
        var (catalog, _, _) = KarmaSkillsSources(fixture);
        var quote = QuoteKarmaSkills(fixture, GroupAndKnowledge(catalog))!;
        var before = fixture.Store.Get(fixture.Id).Value!;
        var skill = quote.Skills.First(item => item.Allocation.Kind == CharacterCreationSkillKinds.Active);
        foreach (var forged in new[]
        {
            quote with { KarmaUsed = quote.KarmaUsed - 1 },
            quote with { Skills = quote.Skills.Select(item => item == skill ? item with { Rating = item.Rating + 1 } : item).ToArray() },
            quote with { Skills = quote.Skills.Select(item => item == skill ? item with { SourceNodeDigest = "sha256:" + new string('0', 64) } : item).ToArray() },
            quote with { Selection = quote.Selection with { TalentUnlock = "invented" } },
            quote with { Blockers = ["blocked"] },
            quote with { Basis = null! }
        })
        {
            var rehashed = forged with { QuoteDigest = CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(
                forged with { QuoteDigest = string.Empty }) };
            Assert.IsFalse(TryProjectKarmaSkills(fixture, rehashed, out var graph, out var deltas));
            Assert.IsNull(graph);
            Assert.AreEqual(0, deltas.Length);
        }
        // A null collection is malformed before it can be hashed/serialized:
        // CanSelect is a DTO getter, so exercise the projector directly.
        Assert.IsFalse(TryProjectKarmaSkills(fixture, quote with { Blockers = null! }, out var missing, out var missingDeltas));
        Assert.IsNull(missing);
        Assert.IsEmpty(missingDeltas);
        AssertJsonEqual(before, fixture.Store.Get(fixture.Id).Value!);
    }

    private static bool TryProjectKarmaSkills(KarmaDiskFixture fixture, CharacterCreationKarmaSkillsQuote quote,
        out XElement? graph, out CharacterCreationFinalizationDelta[] deltas)
    {
        var (_, talents, human) = KarmaSkillsSources(fixture);
        var talent = talents.Options.Single(item => item.OptionId == "mundane");
        var attributes = CharacterCreationKarmaAttributesRules.Evaluate(human, talent,
            fixture.Service.Load(fixture.Id).Value!.AttributePolicy!, [])!;
        return CharacterCreationKarmaSkillsLegacyProjector.TryProject(human, talent, attributes, quote, out graph, out deltas);
    }

    [TestMethod]
    public void Karma_skills_confirmation_rejects_overspend_missing_attributes_and_stale_preview()
    {
        using var fixture = new KarmaDiskFixture(budget: 100, includeSkills: true);
        var (catalog, _, _) = KarmaSkillsSources(fixture);
        var selection = GroupAndKnowledge(catalog);
        var state = fixture.Service.Load(fixture.Id, includeSkills: true).Value!;
        CollectionAssert.Contains(fixture.Service.Preview(state.Binding, HumanId, "mundane", null, selection).Blockers.ToArray(),
            CharacterCreationKarmaMetatypeBlockers.AttributeSelectionRequired);
        var expensive = fixture.Request(HumanId, "mundane", [new("BOD", 2)], selection);
        var before = fixture.Store.Get(fixture.Id).Value!;
        Assert.IsNull(fixture.Service.Confirm(expensive).Value);
        AssertJsonEqual(before, fixture.Store.Get(fixture.Id).Value!);
        var valid = fixture.Request(HumanId, "mundane", [], selection);
        var other = fixture.Request(HumanId, "mundane", [], new([NativeEnglish(catalog)], []));
        Assert.IsNotNull(fixture.Service.Confirm(valid).Value);
        var saved = fixture.Store.Get(fixture.Id).Value!;
        Assert.IsNull(fixture.Service.Confirm(other).Value);
        AssertJsonEqual(saved, fixture.Store.Get(fixture.Id).Value!);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Karma_skills_confirmation_rejects_weapon_source_drift_before_commit(bool duringWrite)
    {
        using var fixture = new KarmaDiskFixture(includeSkills: true);
        var (catalog, _, _) = KarmaSkillsSources(fixture);
        var request = fixture.Request(HumanId, "mundane", [], GroupAndKnowledge(catalog));
        var before = fixture.Store.Get(fixture.Id).Value!;
        void Change() => fixture.EditWeapon("Ares Predator V", row => row.SetElementValue("name", "Changed source"));
        if (duringWrite) fixture.Fault.Action = stage => { if (stage == FileWorkspaceStoreFaultStage.AfterTempFileFlushed) Change(); };
        else Change();
        Assert.IsNull(fixture.Service.Confirm(request).Value);
        fixture.Fault.Action = null;
        AssertJsonEqual(before, new FileWorkspaceStore(fixture.StateRoot).Get(fixture.Id).Value!);
    }

    [TestMethod]
    public void Karma_skills_confirmation_does_not_rebind_saved_choices_after_source_drift()
    {
        using var fixture = new KarmaDiskFixture(includeSkills: true);
        var (catalog, _, _) = KarmaSkillsSources(fixture);
        var request = fixture.Request(HumanId, "mundane", [], GroupAndKnowledge(catalog));
        Assert.IsNotNull(fixture.Service.Confirm(request).Value);
        var saved = fixture.Store.Get(fixture.Id).Value!;
        fixture.EditSkill("Pistols", row => row.SetElementValue("attribute", "LOG"));
        CollectionAssert.Contains(fixture.Service.Load(fixture.Id).Blockers.ToArray(), CharacterCreationKarmaMetatypeBlockers.StaleBinding);
        AssertJsonEqual(saved, fixture.Store.Get(fixture.Id).Value!);
        Assert.IsTrue(fixture.Service.Confirm(request).Value!.Replayed, "An exact receipt retry is not a new source admission.");
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Karma_skills_confirmation_recovers_atomic_io_result_without_replaying_mutation(bool afterReplace)
    {
        using var fixture = new KarmaDiskFixture(includeSkills: true);
        var (catalog, _, _) = KarmaSkillsSources(fixture);
        var request = fixture.Request(HumanId, "mundane", [], GroupAndKnowledge(catalog));
        var before = fixture.Store.Get(fixture.Id).Value!;
        fixture.Fault.Action = stage =>
        {
            if (stage == (afterReplace ? FileWorkspaceStoreFaultStage.AfterTargetReplaced : FileWorkspaceStoreFaultStage.AfterTempFileFlushed))
                throw new IOException("Injected skill commit failure");
        };
        var result = fixture.Service.Confirm(request);
        fixture.Fault.Action = null;
        var cold = new FileWorkspaceStore(fixture.StateRoot).Get(fixture.Id).Value!;
        Assert.AreEqual(afterReplace, result.Value is not null);
        if (afterReplace)
        {
            Assert.IsFalse(result.Value!.Replayed, "The store already knows its atomic rename committed; diagnostic failure is not a retry.");
            var service = new CharacterCreationKarmaMetatypeService(new FileWorkspaceStore(fixture.StateRoot), fixture.Resolver);
            Assert.IsTrue(service.Confirm(request).Value!.Replayed);
            Assert.AreEqual(before.ContentRevision + 1, cold.ContentRevision);
            Assert.AreEqual(1, cold.Document.AuxiliaryState.CharacterCreationKarmaMetatypeDecisions!.Count);
        }
        else AssertJsonEqual(before, cold);
    }

    [TestMethod]
    public async Task Karma_skills_confirmation_concurrent_duplicates_commit_once()
    {
        using var fixture = new KarmaDiskFixture(includeSkills: true);
        var (catalog, _, _) = KarmaSkillsSources(fixture);
        var request = fixture.Request(HumanId, "mundane", [], GroupAndKnowledge(catalog));
        var other = new CharacterCreationKarmaMetatypeService(new FileWorkspaceStore(fixture.StateRoot), fixture.Resolver);
        var results = await Task.WhenAll(Task.Run(() => fixture.Service.Confirm(request)), Task.Run(() => other.Confirm(request)));
        Assert.IsTrue(results.All(result => result.Value is not null), string.Join(",", results.SelectMany(result => result.Blockers)));
        Assert.AreEqual(1, results.Count(result => !result.Value!.Replayed));
        Assert.AreEqual(request.Binding.ContentRevision + 1, fixture.Store.Get(fixture.Id).Value!.ContentRevision);
    }

    [TestMethod]
    public void Karma_skills_confirmation_freezes_collections_and_rejects_forged_rehashed_costs()
    {
        using var fixture = new KarmaDiskFixture(includeSkills: true);
        var (catalog, _, _) = KarmaSkillsSources(fixture);
        var selection = GroupAndKnowledge(catalog);
        var skills = selection.Skills.ToArray();
        var groups = selection.Groups.ToArray();
        var request = fixture.Request(HumanId, "mundane", [], new(skills, groups));
        fixture.Fault.Action = stage =>
        {
            if (stage == FileWorkspaceStoreFaultStage.AfterTempFileFlushed)
            {
                skills[0] = skills[0] with { KarmaLevels = 99 };
                groups[0] = groups[0] with { KarmaLevels = 99 };
            }
        };
        Assert.IsNotNull(fixture.Service.Confirm(request).Value);
        fixture.Fault.Action = null;
        Assert.IsNull(fixture.Service.Confirm(request).Value);
        var saved = fixture.Store.Get(fixture.Id).Value!;
        var decision = saved.Document.AuxiliaryState.CharacterCreationKarmaMetatypeDecisions![0];
        Assert.AreEqual(87m, decision.Quote.Skills!.KarmaUsed);
        var forged = decision.Quote.Skills with { KarmaUsed = 0, QuoteDigest = string.Empty };
        forged = forged with { QuoteDigest = CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(forged) };
        var quote = decision.Quote with { Skills = forged, QuoteDigest = string.Empty,
            KarmaBudget = decision.Quote.KarmaBudget with { Used = 0, Remaining = 800 } };
        quote = quote with { QuoteDigest = CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(quote) };
        decision = decision with { Quote = quote, Command = decision.Command with { QuoteDigest = quote.QuoteDigest } };
        decision = decision with { DecisionDigest = CharacterCreationKarmaMetatypeTransaction.DecisionDigest(decision) };
        Assert.IsFalse(CharacterCreationKarmaMetatypeTransaction.IsValidLedger(fixture.Id, saved.ContentRevision,
            saved.Document.AuxiliaryState with { CharacterCreationKarmaMetatypeDecisions = [decision] }));
    }

    private static CharacterCreationKarmaSkillsSelection GroupAndKnowledge(CharacterCreationSkillsCatalog catalog)
    {
        var group = catalog.SkillGroups.Single(item => item.Name == "Firearms");
        var knowledge = catalog.KnowledgeSkills.Single(item => item.Name == "Sprawl Life");
        return new(group.MemberSkillSourceIds.Select(id => new CharacterCreationKarmaSkillAllocation(id, CharacterCreationSkillKinds.Active, 2))
            .Append(NativeEnglish(catalog)).Append(new(knowledge.SourceSkillId, knowledge.Kind, 2, 3,
                SpecializationOptionId: knowledge.Specializations.First().OptionId, SpecializationPayment: "knowledge-point")).ToArray(),
            [new(group.GroupId, 3)]);
    }

    [TestMethod]
    [DataRow("attributes")]
    [DataRow("talent")]
    public void Karma_skills_confirmation_revisit_revalidates_dependent_spending_without_mutation(string changedStep)
    {
        using var fixture = new KarmaDiskFixture(includeSkills: true);
        var (catalog, _, _) = KarmaSkillsSources(fixture);
        var knowledge = catalog.KnowledgeSkills.Single(skill => skill.Name == "Sprawl Life");
        var spellcasting = catalog.ActiveSkills.Single(skill => skill.Name == "Spellcasting");
        var selection = new CharacterCreationKarmaSkillsSelection([NativeEnglish(catalog),
            new(knowledge.SourceSkillId, knowledge.Kind, 0, 5, SpecializationOptionId: knowledge.Specializations.First().OptionId,
                SpecializationPayment: "knowledge-point"), new(spellcasting.SourceSkillId, spellcasting.Kind, 1)], []);
        CharacterCreationKarmaAttributeAllocation[] attributes = [new("INT", 1), new("LOG", 1)];
        Assert.IsNotNull(fixture.Service.Confirm(fixture.Request(HumanId, MagicianId, attributes, selection)).Value);
        var saved = fixture.Store.Get(fixture.Id).Value!;
        var state = fixture.Service.Load(fixture.Id).Value!;
        string talent = changedStep == "talent" ? "mundane" : MagicianId;
        var nextAttributes = changedStep == "attributes" ? Array.Empty<CharacterCreationKarmaAttributeAllocation>() : attributes;
        var quote = fixture.Service.Preview(state.Binding, HumanId, talent, nextAttributes, selection).Value!;
        Assert.IsNotNull(quote);
        Assert.IsFalse(quote.CanSelect);
        CollectionAssert.Contains(quote.Blockers.ToArray(), changedStep == "attributes"
            ? CharacterCreationSkillsBlockers.KnowledgeBudgetExceeded : CharacterCreationSkillsBlockers.TalentAccessRequired);
        Assert.IsNull(fixture.Service.Confirm(new(quote.Binding, HumanId, quote.QuoteDigest, Guid.NewGuid(), true,
            talent, nextAttributes, selection)).Value);
        AssertJsonEqual(saved, new FileWorkspaceStore(fixture.StateRoot).Get(fixture.Id).Value!);
    }

    [TestMethod]
    [DataRow("knowledge-point", 4, 9)]
    [DataRow("karma", 3, 16)]
    public void Karma_skills_allocation_separates_knowledge_points_and_karma(string payment, int points, int karma)
    {
        using var fixture = new KarmaDiskFixture(includeSkills: true);
        var (catalog, _, _) = KarmaSkillsSources(fixture);
        var sprawl = catalog.KnowledgeSkills.Single(skill => skill.Name == "Sprawl Life");
        var before = fixture.Store.Get(fixture.Id).Value!;
        var allocation = new CharacterCreationKarmaSkillAllocation(sprawl.SourceSkillId, sprawl.Kind,
            2, 3, SpecializationOptionId: sprawl.Specializations.First().OptionId, SpecializationPayment: payment);
        var quote = QuoteKarmaSkills(fixture, new([NativeEnglish(catalog), allocation], []));
        Assert.IsNotNull(quote);
        Assert.IsTrue(quote.CanSelect, string.Join(",", quote.Blockers));
        Assert.AreEqual(4, quote.KnowledgePointsTotal);
        Assert.AreEqual((decimal)points, quote.KnowledgePointsUsed);
        Assert.AreEqual((decimal)karma, quote.KarmaUsed); // Ranks 4 + 5, plus optional Karma spec.
        Assert.AreEqual(5, quote.Skills.Single(skill => skill.Name == "Sprawl Life").Rating);
        Assert.IsNull(quote.Skills.Single(skill => skill.Name == "English").Rating);
        Assert.AreEqual(quote.QuoteDigest, QuoteKarmaSkills(fixture, new([allocation, NativeEnglish(catalog)], []))!.QuoteDigest);
        AssertJsonEqual(before, fixture.Store.Get(fixture.Id).Value!);
    }

    [TestMethod]
    [DataRow(false, 0, 18)]
    [DataRow(true, 0, 16)]
    [DataRow(false, 3, 78)]
    [DataRow(true, 3, 76)]
    public void Karma_skills_allocation_matches_legacy_group_intervals_and_compensation(bool compensate, int groupRanks, int karma)
    {
        using var fixture = new KarmaDiskFixture(includeSkills: true, configureSettings: row =>
            row.SetElementValue("compensateskillgroupkarmadifference", compensate.ToString()));
        var (catalog, _, _) = KarmaSkillsSources(fixture);
        var group = catalog.SkillGroups.Single(item => item.Name == "Firearms");
        var skills = group.MemberSkillSourceIds.Select(id => new CharacterCreationKarmaSkillAllocation(
            id, CharacterCreationSkillKinds.Active, 2)).Prepend(NativeEnglish(catalog)).ToArray();
        var quote = QuoteKarmaSkills(fixture, new(skills, [new(group.GroupId, groupRanks)]));
        Assert.IsNotNull(quote);
        Assert.IsTrue(quote.CanSelect, string.Join(",", quote.Blockers));
        Assert.AreEqual((decimal)karma, quote.KarmaUsed);
        Assert.AreEqual(groupRanks == 0 ? 0 : 60, quote.Groups.Single().KarmaCost);
        Assert.IsFalse(quote.Groups.Single().IsBroken);
        foreach (var skill in quote.Skills.Where(item => item.Allocation.Kind == CharacterCreationSkillKinds.Active))
            Assert.AreEqual(groupRanks + 2, skill.Rating);
        string first = catalog.ActiveSkillSourceOrder.First(id => group.MemberSkillSourceIds.Contains(id));
        Assert.AreEqual(compensate ? 4 : 6, quote.Skills.Single(item => item.Allocation.SourceSkillId == first).KarmaCost);
    }

    [TestMethod]
    public void Karma_skills_allocation_preserves_disabled_member_interval_semantics()
    {
        using var fixture = new KarmaDiskFixture(includeSkills: true);
        var (catalog, _, _) = KarmaSkillsSources(fixture);
        var group = catalog.SkillGroups.Single(item => item.Name == "Athletics");
        var members = catalog.ActiveSkills.Where(skill => skill.SkillGroup == "Athletics" && skill.Name != "Flight")
            .Select(skill => new CharacterCreationKarmaSkillAllocation(skill.SourceSkillId, skill.Kind, 2));
        var quote = QuoteKarmaSkills(fixture, new(members.Prepend(NativeEnglish(catalog)).ToArray(), [new(group.GroupId, 3)]));
        Assert.IsNotNull(quote);
        Assert.IsTrue(quote.CanSelect, string.Join(",", quote.Blockers));
        Assert.AreEqual(114m, quote.KarmaUsed); // Group: 60; enabled skills: (4+5)*2 each.
        var flight = quote.Skills.Single(skill => skill.Name == "Flight");
        Assert.IsFalse(flight.IsEnabled);
        Assert.AreEqual(0, flight.KarmaCost);
        Assert.AreEqual(3, flight.Rating);
    }

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public void Karma_skills_allocation_obeys_group_break_and_strict_house_rules(bool strict, bool specBreaks)
    {
        using var fixture = new KarmaDiskFixture(includeSkills: true, configureSettings: row =>
        {
            row.SetElementValue("breakskillgroupsincreatemode", strict.ToString());
            row.SetElementValue("specializationsbreakskillgroups", specBreaks.ToString());
        });
        var (catalog, _, _) = KarmaSkillsSources(fixture);
        var pistols = catalog.ActiveSkills.Single(skill => skill.Name == "Pistols");
        var group = catalog.SkillGroups.Single(item => item.Name == "Firearms");
        var spec = new CharacterCreationKarmaSkillAllocation(pistols.SourceSkillId, pistols.Kind, 0,
            SpecializationOptionId: pistols.Specializations.First().OptionId, SpecializationPayment: "karma");
        var quote = QuoteKarmaSkills(fixture, new([NativeEnglish(catalog), spec], [new(group.GroupId, 3)]))!;
        Assert.AreEqual(!strict, quote.CanSelect);
        Assert.AreEqual(specBreaks, quote.Groups.Single().IsBroken);
        Assert.AreEqual(37m, quote.KarmaUsed);
        var raised = QuoteKarmaSkills(fixture, new([NativeEnglish(catalog), spec with
        { KarmaLevels = 1, SpecializationOptionId = null, SpecializationPayment = null }], [new(group.GroupId, 3)]))!;
        Assert.AreEqual(!strict, raised.CanSelect);
        Assert.IsTrue(raised.Groups.Single().IsBroken);
        Assert.AreEqual(38m, raised.KarmaUsed);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Karma_skills_allocation_respects_point_specialization_house_rule(bool allow)
    {
        using var fixture = new KarmaDiskFixture(includeSkills: true, configureSettings: row =>
            row.SetElementValue("allowpointbuyspecializationsonkarmaskills", allow.ToString()));
        var (catalog, _, _) = KarmaSkillsSources(fixture);
        var sprawl = catalog.KnowledgeSkills.Single(skill => skill.Name == "Sprawl Life");
        var quote = QuoteKarmaSkills(fixture, new([NativeEnglish(catalog),
            new(sprawl.SourceSkillId, sprawl.Kind, 2, SpecializationOptionId: sprawl.Specializations.First().OptionId,
                SpecializationPayment: "knowledge-point")], []))!;
        Assert.AreEqual(allow, quote.CanSelect);
        Assert.AreEqual(3m, quote.KarmaUsed);
        Assert.AreEqual(1m, quote.KnowledgePointsUsed);
    }

    [TestMethod]
    public void Karma_skills_allocation_supports_distinct_source_bound_exotic_identities_without_spec_surcharge()
    {
        using var fixture = new KarmaDiskFixture(includeSkills: true);
        var (catalog, _, _) = KarmaSkillsSources(fixture);
        var exotic = catalog.ActiveSkills.Single(skill => skill.Name == "Exotic Melee Weapon");
        var whip = exotic.Specializations.Single(spec => spec.Name == "Monofilament Whip");
        Assert.IsTrue(whip.SourceAnchorId.StartsWith("weapons.xml#weapon:", StringComparison.Ordinal));
        var other = exotic.Specializations.First(spec => spec.OptionId != whip.OptionId);
        var first = new CharacterCreationKarmaSkillAllocation(exotic.SourceSkillId, exotic.Kind, 2,
            SpecializationOptionId: whip.OptionId, SpecializationPayment: "exotic-identity");
        var second = first with { SpecializationOptionId = other.OptionId };
        var quote = QuoteKarmaSkills(fixture, new([NativeEnglish(catalog), first, second], []))!;
        Assert.IsTrue(quote.CanSelect, string.Join(",", quote.Blockers));
        Assert.AreEqual(12m, quote.KarmaUsed);
        Assert.AreEqual(2, quote.Skills.Count(skill => skill.Allocation.SourceSkillId == exotic.SourceSkillId));
        Assert.IsTrue(quote.Skills.All(skill => skill.SpecializationKarmaCost == 0));
        Assert.IsNull(QuoteKarmaSkills(fixture, new([NativeEnglish(catalog), first, first], [])));
        Assert.IsFalse(QuoteKarmaSkills(fixture, new([NativeEnglish(catalog), first with { SpecializationPayment = "karma" }], []))!.CanSelect);
        Assert.IsFalse(QuoteKarmaSkills(fixture, new([NativeEnglish(catalog), first with
        { SpecializationOptionId = null, SpecializationPayment = null }], []))!.CanSelect);
    }

    [TestMethod]
    [DataRow("({INTUnaug} + {LOGUnaug}) * 2", 10)]
    [DataRow("{LOGUnaug} div 2", 2)]
    [DataRow("0", 0)]
    [DataRow("-1", -1)]
    [DataRow("1 / 0", -1)]
    [DataRow("{Unknown} * 2", -1)]
    [DataRow("document('file:///etc/passwd')", -1)]
    [DataRow("2147483648", -1)]
    [DataRow("(2", -1)]
    public void Karma_skills_allocation_uses_confirmed_attributes_and_bounded_expression(string expression, int total)
    {
        using var fixture = new KarmaDiskFixture(includeSkills: true, configureSettings: row =>
            row.SetElementValue("knowledgepointsexpression", expression));
        var (catalog, _, _) = KarmaSkillsSources(fixture);
        var quote = QuoteKarmaSkills(fixture, new([NativeEnglish(catalog)], []), attributes: [new("INT", 1), new("LOG", 2)])!;
        Assert.AreEqual(total >= 0, quote.CanSelect);
        if (total >= 0) Assert.AreEqual(total, quote.KnowledgePointsTotal);
        else CollectionAssert.Contains(quote.Blockers.ToArray(), CharacterCreationKarmaSkillsRules.KnowledgeExpressionUnresolved);
    }

    [TestMethod]
    public void Karma_skills_allocation_enforces_native_budgets_caps_access_and_specializations()
    {
        using var fixture = new KarmaDiskFixture(includeSkills: true);
        var (catalog, _, _) = KarmaSkillsSources(fixture);
        var native = NativeEnglish(catalog);
        var pistols = catalog.ActiveSkills.Single(skill => skill.Name == "Pistols");
        var sprawl = catalog.KnowledgeSkills.Single(skill => skill.Name == "Sprawl Life");
        var magic = catalog.ActiveSkills.Single(skill => skill.Name == "Spellcasting");
        var otherLanguage = catalog.KnowledgeSkills.First(skill => skill.CanBeNativeLanguage && skill.SourceSkillId != native.SourceSkillId);
        var invalid = new (CharacterCreationKarmaSkillAllocation[] Skills, string Blocker)[]
        {
            ([], CharacterCreationSkillsBlockers.NativeLanguageRequired),
            ([native, new(otherLanguage.SourceSkillId, otherLanguage.Kind, 0, IsNativeLanguage: true)], CharacterCreationSkillsBlockers.NativeLanguageLimitExceeded),
            ([native with { KnowledgePointLevels = 1 }], CharacterCreationSkillsBlockers.NativeLanguageInvalid),
            ([new(sprawl.SourceSkillId, sprawl.Kind, 0, IsNativeLanguage: true)], CharacterCreationSkillsBlockers.NativeLanguageInvalid),
            ([native, new(sprawl.SourceSkillId, sprawl.Kind, 0, 5)], CharacterCreationSkillsBlockers.KnowledgeBudgetExceeded),
            ([native, new(pistols.SourceSkillId, pistols.Kind, 7)], CharacterCreationSkillsBlockers.RatingInvalid),
            ([native, new(pistols.SourceSkillId, pistols.Kind, 0, 1)], CharacterCreationSkillsBlockers.AllocationInvalid),
            ([native, new(magic.SourceSkillId, magic.Kind, 1)], CharacterCreationSkillsBlockers.TalentAccessRequired),
            ([native, new(pistols.SourceSkillId, pistols.Kind, 1, SpecializationOptionId: "invented", SpecializationPayment: "karma")], CharacterCreationSkillsBlockers.SpecializationInvalid),
            ([native, new(pistols.SourceSkillId, pistols.Kind, 0, SpecializationOptionId: pistols.Specializations.First().OptionId, SpecializationPayment: "karma")], CharacterCreationSkillsBlockers.SpecializationInvalid),
        };
        foreach (var (skills, blocker) in invalid)
        {
            var quote = QuoteKarmaSkills(fixture, new(skills, []))!;
            Assert.IsFalse(quote.CanSelect, blocker);
            CollectionAssert.Contains(quote.Blockers.ToArray(), blocker);
        }
        var budget = QuoteKarmaSkills(fixture, new([native, new(pistols.SourceSkillId, pistols.Kind, 1)], []), karma: 0)!;
        CollectionAssert.Contains(budget.Blockers.ToArray(), CharacterCreationKarmaSkillsRules.KarmaBudgetExceeded);
        Assert.IsTrue(QuoteKarmaSkills(fixture, new([native, new(magic.SourceSkillId, magic.Kind, 1)], []), talent: MagicianId)!.CanSelect);
    }

    [TestMethod]
    public void Karma_skills_allocation_freezes_input_and_rejects_malformed_or_unknown_identities()
    {
        using var fixture = new KarmaDiskFixture(includeSkills: true);
        var (catalog, _, _) = KarmaSkillsSources(fixture);
        var native = NativeEnglish(catalog);
        var items = new[] { native };
        var selection = new CharacterCreationKarmaSkillsSelection(items, []);
        var quote = QuoteKarmaSkills(fixture, selection)!;
        string digest = quote.QuoteDigest;
        items[0] = native with { KarmaLevels = 5 };
        Assert.AreEqual(0, quote.Selection.Skills.Single().KarmaLevels);
        Assert.AreEqual(digest, CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(quote with { QuoteDigest = string.Empty }));
        Assert.IsNull(QuoteKarmaSkills(fixture, new([native, native], [])));
        Assert.IsNull(QuoteKarmaSkills(fixture, new([native with { SourceSkillId = Guid.NewGuid().ToString("D") }], [])));
        Assert.IsNull(QuoteKarmaSkills(fixture, new([native], [new("unknown", 1)])));
        Assert.IsNull(QuoteKarmaSkills(fixture, new([native with { KarmaLevels = -1 }], [])));
        Assert.IsNull(QuoteKarmaSkills(fixture, new([native with { Kind = "invented" }], [])));
        Assert.IsNull(QuoteKarmaSkills(fixture, new([native with { KarmaLevels = int.MaxValue, KnowledgePointLevels = 1 }], [])));
        Assert.IsFalse(CharacterCreationKarmaSkillsRules.TryFreeze(new(Enumerable.Repeat(native, 257).ToArray(), []), out _));
        Assert.IsFalse(CharacterCreationKarmaSkillsRules.TryFreeze(new([], Enumerable.Range(0, 65).Select(i => new CharacterCreationKarmaSkillGroupAllocation(i.ToString(), 0)).ToArray()), out _));
        Assert.IsFalse(CharacterCreationKarmaSkillsRules.TryFreeze(new([], [new("g", 1), new("g", 1)]), out _));
        Assert.IsFalse(CharacterCreationKarmaSkillsRules.TryFreeze(null, out _));
    }

    private static CharacterCreationKarmaSkillAllocation NativeEnglish(CharacterCreationSkillsCatalog catalog)
        => new(catalog.KnowledgeSkills.Single(skill => skill.Name == "English").SourceSkillId,
            CharacterCreationSkillKinds.Knowledge, 0, IsNativeLanguage: true);

    [TestMethod]
    public void Karma_skills_allocation_binds_compensation_to_source_order_not_display_order()
    {
        using var fixture = new KarmaDiskFixture(includeSkills: true, configureSettings: row =>
            row.SetElementValue("compensateskillgroupkarmadifference", "True"));
        fixture.EditSkill("Pistols", row =>
        {
            var parent = row.Parent!;
            row.Remove();
            parent.AddFirst(row);
        });
        var (catalog, _, _) = KarmaSkillsSources(fixture);
        var group = catalog.SkillGroups.Single(item => item.Name == "Firearms");
        string pistols = catalog.ActiveSkills.Single(skill => skill.Name == "Pistols").SourceSkillId;
        Assert.AreEqual(pistols, catalog.ActiveSkillSourceOrder[0]);
        var quote = QuoteKarmaSkills(fixture, new(group.MemberSkillSourceIds.Select(id =>
            new CharacterCreationKarmaSkillAllocation(id, CharacterCreationSkillKinds.Active, 2))
            .Prepend(NativeEnglish(catalog)).ToArray(), []))!;
        Assert.IsTrue(quote.CanSelect);
        Assert.AreEqual(4, quote.Skills.Single(skill => skill.Name == "Pistols").KarmaCost);
        Assert.AreEqual(16m, quote.KarmaUsed);
        var missingOrder = catalog with { ActiveSkillSourceOrder = [] };
        missingOrder = missingOrder with { CatalogDigest = CharacterCreationSkillsCatalogAuthority.ComputeDigest(missingOrder) };
        Assert.IsFalse(CharacterCreationSkillsCatalogAuthority.IsValid(missingOrder));
        var changedOrder = catalog with { ActiveSkillSourceOrder = catalog.ActiveSkillSourceOrder.Reverse().ToArray() };
        Assert.IsFalse(CharacterCreationSkillsCatalogAuthority.IsValid(changedOrder));
        Assert.AreNotEqual(catalog.CatalogDigest, CharacterCreationSkillsCatalogAuthority.ComputeDigest(changedOrder));
    }

    [TestMethod]
    public void Karma_skills_allocation_exotic_options_follow_useskill_and_enabled_books()
    {
        using var fixture = new KarmaDiskFixture(includeSkills: true);
        fixture.EditWeapon("Monofilament Whip", row =>
        {
            row.SetElementValue("category", "Test category");
            row.SetElementValue("useskill", "Exotic Melee Weapon");
        });
        var (before, _, _) = KarmaSkillsSources(fixture);
        Assert.IsTrue(before.ActiveSkills.Single(skill => skill.Name == "Exotic Melee Weapon").Specializations
            .Any(spec => spec.Name == "Monofilament Whip"));
        fixture.EditWeapon("Monofilament Whip", row => row.SetElementValue("source", "disabled-book"));
        var (after, _, _) = KarmaSkillsSources(fixture);
        Assert.IsFalse(after.ActiveSkills.Single(skill => skill.Name == "Exotic Melee Weapon").Specializations
            .Any(spec => spec.Name == "Monofilament Whip"));
        Assert.AreNotEqual(before.CatalogDigest, after.CatalogDigest);
    }

    [TestMethod]
    public void Karma_skills_allocation_rejects_invalid_attributes_policy_and_mixed_profile()
    {
        using var fixture = new KarmaDiskFixture(includeSkills: true);
        var (catalog, talents, human) = KarmaSkillsSources(fixture);
        var context = fixture.Resolver.TryCreateContext(fixture.Store.Get(fixture.Id).Value!.Document.Content)!;
        Assert.IsTrue(context.TryResolveCreationKarmaSkillsPolicy(out var policy));
        Assert.IsNotNull(policy);
        var attributes = CharacterCreationKarmaAttributesRules.Evaluate(human,
            talents.Options.Single(item => item.OptionId == "mundane"), fixture.Service.Load(fixture.Id).Value!.AttributePolicy!, [])!;
        var selection = new CharacterCreationKarmaSkillsSelection([NativeEnglish(catalog)], []);
        CharacterCreationKarmaSkillsQuote? Evaluate(CharacterCreationKarmaSkillsPolicy current, CharacterCreationKarmaAttributesQuote currentAttributes)
            => CharacterCreationKarmaSkillsRules.Evaluate(catalog, current, talents, human, "mundane", currentAttributes, 800, selection);
        Assert.IsNull(Evaluate(policy with { KarmaNewActiveSkill = 0 }, attributes));
        var foreign = policy with { RawProfileInputsDigest = "sha256:" + new string('a', 64) };
        foreign = foreign with { AuthorityDigest = CharacterCreationKarmaSkillsPolicyAuthority.ComputeDigest(foreign) };
        Assert.IsNull(Evaluate(foreign, attributes));
        var changed = attributes with { Attributes = attributes.Attributes.Select(item =>
            item.AttributeId == "INT" ? item with { Current = 6 } : item).ToArray() };
        changed = changed with { QuoteDigest = CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(changed with { QuoteDigest = string.Empty }) };
        Assert.IsNull(Evaluate(policy, changed));
        Assert.IsNull(QuoteKarmaSkills(fixture, selection, attributes: [new("INT", 6)]));
        Assert.IsNull(CharacterCreationKarmaSkillsRules.Evaluate(null!, policy, talents, human, "mundane", attributes, 800, selection));
    }

    [TestMethod]
    [DataRow("unselected-active-row")]
    [DataRow("unselected-group")]
    [DataRow("missing-source-order")]
    public void Karma_skills_allocation_still_admits_the_full_catalog_before_retaining_a_subset(string corruption)
    {
        using var fixture = new KarmaDiskFixture(includeSkills: true);
        var (catalog, talents, human) = KarmaSkillsSources(fixture);
        var context = fixture.Resolver.TryCreateContext(fixture.Store.Get(fixture.Id).Value!.Document.Content)!;
        Assert.IsTrue(context.TryResolveCreationKarmaSkillsPolicy(out var policy));
        Assert.IsNotNull(policy);
        var attributes = CharacterCreationKarmaAttributesRules.Evaluate(human,
            talents.Options.Single(item => item.OptionId == "mundane"),
            fixture.Service.Load(fixture.Id).Value!.AttributePolicy!, [])!;
        // No active skill or group is selected. Checking only the retained
        // native-language subset would incorrectly accept all these forgeries.
        var selection = new CharacterCreationKarmaSkillsSelection([NativeEnglish(catalog)], []);
        CharacterCreationKarmaSkillsQuote? Evaluate(CharacterCreationSkillsCatalog candidate)
            => CharacterCreationKarmaSkillsRules.Evaluate(candidate, policy, talents, human,
                "mundane", attributes, 800, selection);
        var original = Evaluate(catalog);
        Assert.IsNotNull(original);
        Assert.IsTrue(original.CanSelect);
        var altered = corruption switch
        {
            "unselected-active-row" => catalog with
            {
                ActiveSkills = catalog.ActiveSkills.Skip(1).Prepend(catalog.ActiveSkills[0] with
                {
                    RequiresFlyMovement = !catalog.ActiveSkills[0].RequiresFlyMovement
                }).ToArray()
            },
            "unselected-group" => catalog with
            {
                SkillGroups = catalog.SkillGroups.Skip(1).Prepend(catalog.SkillGroups[0] with
                {
                    MemberSkillSourceIds = []
                }).ToArray()
            },
            "missing-source-order" => catalog with { ActiveSkillSourceOrder = [] },
            _ => throw new ArgumentException(corruption)
        };
        altered = altered with { CatalogDigest = CharacterCreationSkillsCatalogAuthority.ComputeDigest(altered) };
        Assert.IsNull(Evaluate(altered));
        Assert.AreEqual(original.QuoteDigest, Evaluate(catalog)!.QuoteDigest,
            "A rejected full catalog must not contaminate a later fresh evaluation.");
    }

    private static CharacterCreationKarmaSkillsQuote? QuoteKarmaSkills(KarmaDiskFixture fixture,
        CharacterCreationKarmaSkillsSelection selection, int karma = 800, string talent = "mundane",
        IReadOnlyList<CharacterCreationKarmaAttributeAllocation>? attributes = null)
    {
        var (catalog, talents, human) = KarmaSkillsSources(fixture);
        var context = fixture.Resolver.TryCreateContext(fixture.Store.Get(fixture.Id).Value!.Document.Content)!;
        Assert.IsTrue(context.TryResolveCreationKarmaSkillsPolicy(out var policy));
        Assert.IsNotNull(policy);
        var attributePolicy = fixture.Service.Load(fixture.Id).Value!.AttributePolicy!;
        var quote = CharacterCreationKarmaAttributesRules.Evaluate(human,
            talents.Options.Single(item => item.OptionId == talent), attributePolicy, attributes ?? []);
        Assert.IsNotNull(quote);
        return CharacterCreationKarmaSkillsRules.Evaluate(catalog, policy, talents, human, talent, quote, karma, selection);
    }

    [TestMethod]
    public void Karma_skills_catalog_keeps_source_identities_without_priority_semantics()
    {
        using var fixture = new KarmaDiskFixture(includeSkills: true, configureSettings: row =>
            row.SetElementValue("breakskillgroupsincreatemode", "True"));
        var context = fixture.Resolver.TryCreateContext(fixture.Store.Get(fixture.Id).Value!.Document.Content)!;
        Assert.IsTrue(context.TryResolveCreationSkillsCatalog(out var catalog));
        Assert.IsNotNull(catalog);
        Assert.IsTrue(CharacterCreationSkillsCatalogAuthority.IsValid(catalog));
        Assert.AreEqual(CanonicalKarmaSettingsId, catalog.SettingsProfileId);
        Assert.IsTrue(catalog.ActiveSkills.Count > 50);
        Assert.IsTrue(catalog.KnowledgeSkills.Count > 50);
        Assert.IsFalse(catalog.SourceAnchorIds.Any(anchor => anchor.StartsWith("priorities.xml", StringComparison.Ordinal)));
        Assert.IsTrue(context.TryResolveCreationSkillsAuthority(out var priority));
        Assert.IsFalse(priority.IsAuthoritative, "Strict house rules are not Standard Priority policy.");
        AssertJsonEqual(priority.ActiveSkills, catalog.ActiveSkills);
        AssertJsonEqual(priority.KnowledgeSkills, catalog.KnowledgeSkills);
        AssertJsonEqual(priority.SkillGroups, catalog.SkillGroups);
        Assert.IsTrue(catalog.KnowledgeSkills.Any(skill => skill.CanBeNativeLanguage));
        var pistols = catalog.ActiveSkills.Single(skill => skill.Name == "Pistols");
        Assert.IsTrue(pistols.Specializations.Any(option => option.SourceAnchorId.StartsWith("weapons.xml#", StringComparison.Ordinal)));
        Assert.AreEqual(catalog.CatalogDigest, CharacterCreationSkillsCatalogAuthority.ComputeDigest(catalog));
    }

    [TestMethod]
    public void Karma_skills_catalog_binds_weapon_specs_and_rejects_existing_context_after_drift()
    {
        using var fixture = new KarmaDiskFixture(includeSkills: true);
        string xml = fixture.Store.Get(fixture.Id).Value!.Document.Content;
        var context = fixture.Resolver.TryCreateContext(xml)!;
        Assert.IsTrue(context.TryResolveCreationSkillsCatalog(out var before));
        var capture = WorkspaceContinuationSourceCapture.TryCapture(context, xml,
            new XmlLifeModulesCatalogService(Path.Combine(FindCoreRoot(), "Chummer", "data", "lifemodules.xml")), out var frozen);
        Assert.IsTrue(capture);
        fixture.EditWeapon("Ares Predator V", row => row.Element("name")!.Value = "Ares Predator V (test source)");
        Assert.IsFalse(context.TryResolveCreationSkillsCatalog(out var stale));
        Assert.IsNull(stale);
        Assert.IsTrue(fixture.Resolver.TryCreateContext(xml)!.TryResolveCreationSkillsCatalog(out var after));
        Assert.IsNotNull(before); Assert.IsNotNull(after);
        Assert.AreEqual(before.SkillsInputsDigest, after.SkillsInputsDigest);
        Assert.AreNotEqual(before.WeaponsInputsDigest, after.WeaponsInputsDigest);
        Assert.AreNotEqual(before.CatalogDigest, after.CatalogDigest);
        Assert.IsTrue(after.ActiveSkills.Single(skill => skill.Name == "Pistols").Specializations
            .Any(option => option.Name == "Ares Predator V (test source)"));
        Assert.IsTrue(frozen!.CreateResolver().TryCreateContext(xml)!.TryResolveCreationSkillsCatalog(out var retained));
        AssertJsonEqual(before, retained);
    }

    [TestMethod]
    public void Karma_skills_catalog_omits_disabled_book_skills_and_rejects_malformed_enabled_rows()
    {
        using var fixture = new KarmaDiskFixture(includeSkills: true);
        string xml = fixture.Store.Get(fixture.Id).Value!.Document.Content;
        fixture.EditSkill("Pistols", row => row.SetElementValue("source", "disabled-book"));
        Assert.IsTrue(fixture.Resolver.TryCreateContext(xml)!.TryResolveCreationSkillsCatalog(out var catalog));
        Assert.IsFalse(catalog!.ActiveSkills.Any(skill => skill.Name == "Pistols"));
        fixture.EditSkill("Pistols", row =>
        {
            row.SetElementValue("source", "SR5");
            row.Add(new XElement("attribute", "AGI"));
        });
        Assert.IsFalse(fixture.Resolver.TryCreateContext(xml)!.TryResolveCreationSkillsCatalog(out var invalid));
        Assert.IsNull(invalid);
    }

    [TestMethod]
    [DataRow("mundane", false, false, false)]
    [DataRow("0e741331-d776-4be8-abc5-4101228abdef", true, true, false)]
    [DataRow("55247bdc-c313-4614-ae15-5012308096ff", false, true, false)]
    [DataRow("9d53e1e4-3f31-40cb-bfbe-4b94f5ba757e", true, true, false)]
    [DataRow("c4b35412-bd91-45b4-b428-29da7edd5ff4", false, false, true)]
    public void Karma_skill_access_uses_talent_source_not_labels_or_priority_grants(
        string talentId, bool spellcasting, bool assensing, bool compiling)
    {
        using var fixture = new KarmaDiskFixture(includeSkills: true);
        var (catalog, talents, human) = KarmaSkillsSources(fixture);
        var access = CharacterCreationKarmaSkillAccessRules.Evaluate(catalog, talents, human, talentId);
        Assert.IsNotNull(access);
        Assert.IsTrue(access.IsReady);
        Assert.IsEmpty(access.RequiredUnlockChoices);
        bool Allowed(string name) => access.AllowedActiveSkillSourceIds.Contains(
            catalog.ActiveSkills.Single(skill => skill.Name == name).SourceSkillId);
        Assert.AreEqual(spellcasting, Allowed("Spellcasting"));
        Assert.AreEqual(assensing, Allowed("Assensing"));
        Assert.AreEqual(compiling, Allowed("Compiling"));
        Assert.IsTrue(Allowed("Pistols"));
        Assert.IsFalse(Allowed("Flight"));
        Assert.IsTrue(access.AllowedSkillGroupIds.Contains(catalog.SkillGroups.Single(group => group.Name == "Athletics").GroupId),
            "An unavailable flight member does not break a group with other enabled members.");
        Assert.AreEqual(access.AccessDigest, CharacterCreationKarmaSkillAccessRules.Evaluate(catalog, talents, human, talentId)!.AccessDigest);
    }

    [TestMethod]
    public void Karma_skill_access_requires_explicit_aspected_group_choice_and_rejects_unused_choice()
    {
        const string aspected = "4adeb2d4-e42e-4b7a-9a5d-3df325ae59a5";
        using var fixture = new KarmaDiskFixture(includeSkills: true);
        var (catalog, talents, human) = KarmaSkillsSources(fixture);
        var missing = CharacterCreationKarmaSkillAccessRules.Evaluate(catalog, talents, human, aspected)!;
        Assert.IsFalse(missing.IsReady);
        CollectionAssert.AreEqual(new[] { "Conjuring", "Enchanting", "Sorcery" }, missing.RequiredUnlockChoices.ToArray());
        CollectionAssert.Contains(missing.Blockers.ToArray(), CharacterCreationKarmaSkillAccess.UnlockRequired);
        Assert.IsEmpty(missing.AllowedActiveSkillSourceIds);
        var chosen = CharacterCreationKarmaSkillAccessRules.Evaluate(catalog, talents, human, aspected, "Sorcery")!;
        Assert.IsTrue(chosen.IsReady);
        Assert.IsTrue(chosen.AllowedActiveSkillSourceIds.Contains(catalog.ActiveSkills.Single(skill => skill.Name == "Spellcasting").SourceSkillId));
        Assert.IsFalse(chosen.AllowedActiveSkillSourceIds.Contains(catalog.ActiveSkills.Single(skill => skill.Name == "Summoning").SourceSkillId));
        Assert.IsTrue(chosen.AllowedActiveSkillSourceIds.Contains(catalog.ActiveSkills.Single(skill => skill.Name == "Assensing").SourceSkillId));
        foreach (string input in new[] { "Magician", " Sorcery", "sorcery", "", "Sorcery,Conjuring" })
        {
            var invalid = CharacterCreationKarmaSkillAccessRules.Evaluate(catalog, talents, human, aspected, input)!;
            Assert.IsFalse(invalid.IsReady);
            Assert.IsEmpty(invalid.AllowedActiveSkillSourceIds);
        }
        Assert.IsFalse(CharacterCreationKarmaSkillAccessRules.Evaluate(catalog, talents, human, "mundane", "Sorcery")!.IsReady);
        Assert.IsFalse(CharacterCreationKarmaSkillAccessRules.Evaluate(catalog, talents, human, MagicianId, "Sorcery")!.IsReady);
    }

    [TestMethod]
    public void Karma_skill_access_rejects_corrupt_catalog_talent_and_foreign_profile()
    {
        using var fixture = new KarmaDiskFixture(includeSkills: true);
        var (catalog, talents, human) = KarmaSkillsSources(fixture);
        var forgedSkill = catalog.ActiveSkills.First() with { RequiresFlyMovement = !catalog.ActiveSkills.First().RequiresFlyMovement };
        var altered = catalog with { ActiveSkills = catalog.ActiveSkills.Skip(1).Prepend(forgedSkill).ToArray() };
        altered = altered with { CatalogDigest = CharacterCreationSkillsCatalogAuthority.ComputeDigest(altered) };
        Assert.IsFalse(CharacterCreationSkillsCatalogAuthority.IsValid(altered));
        Assert.IsNull(CharacterCreationKarmaSkillAccessRules.Evaluate(altered, talents, human, "mundane"));
        var forgedGroup = catalog.SkillGroups.First() with { MemberSkillSourceIds = [] };
        altered = catalog with { SkillGroups = catalog.SkillGroups.Skip(1).Prepend(forgedGroup).ToArray() };
        altered = altered with { CatalogDigest = CharacterCreationSkillsCatalogAuthority.ComputeDigest(altered) };
        Assert.IsFalse(CharacterCreationSkillsCatalogAuthority.IsValid(altered));
        Assert.IsNull(CharacterCreationKarmaSkillAccessRules.Evaluate(catalog, talents with { RawProfileInputsDigest = "different" }, human, MagicianId));
        Assert.IsNull(CharacterCreationKarmaSkillAccessRules.Evaluate(catalog, talents, human, "Magician"));
        var changedTalent = talents.Options.Single(option => option.OptionId == MagicianId) with { SourceNodeXml = "<!DOCTYPE quality><quality />" };
        var corrupt = talents with { Options = talents.Options.Select(option => option.OptionId == MagicianId ? changedTalent : option).ToArray() };
        corrupt = corrupt with { AuthorityDigest = CharacterCreationKarmaTalentAuthority.ComputeDigest(corrupt) };
        Assert.IsNull(CharacterCreationKarmaSkillAccessRules.Evaluate(catalog, corrupt, human, MagicianId));
        changedTalent = talents.Options.Single(option => option.OptionId == MagicianId) with { KarmaCost = 0 };
        corrupt = talents with { Options = talents.Options.Select(option => option.OptionId == MagicianId ? changedTalent : option).ToArray() };
        corrupt = corrupt with { AuthorityDigest = CharacterCreationKarmaTalentAuthority.ComputeDigest(corrupt) };
        Assert.IsNull(CharacterCreationKarmaSkillAccessRules.Evaluate(catalog, corrupt, human, MagicianId));
    }

    private static (CharacterCreationSkillsCatalog Catalog, CharacterCreationKarmaTalentCatalog Talents,
        CharacterCreationMetatypeOptionProjection Human) KarmaSkillsSources(KarmaDiskFixture fixture)
    {
        var context = fixture.Resolver.TryCreateContext(fixture.Store.Get(fixture.Id).Value!.Document.Content)!;
        Assert.IsTrue(context.TryResolveCreationSkillsCatalog(out var catalog));
        Assert.IsNotNull(catalog);
        Assert.IsTrue(context.TryResolveCreationKarmaTalents(out var talents));
        Assert.IsNotNull(talents);
        Assert.IsTrue(context.TryResolveCreationMetatypeCatalog(out var metatypes));
        return (catalog, talents, metatypes.Options.Single(option => option.OptionId == HumanId));
    }

    [TestMethod]
    public void Karma_skill_access_handles_all_movement_domains_without_removing_catalog_rows()
    {
        using var fixture = new KarmaDiskFixture(includeSkills: true);
        var (catalog, talents, human) = KarmaSkillsSources(fixture);
        var onlyFly = human with
        {
            Movement = new(new(0, 0, 0), new(0, 0, 0), new(0, 0, 1))
        };
        var access = CharacterCreationKarmaSkillAccessRules.Evaluate(catalog, talents, onlyFly, "mundane")!;
        Assert.IsTrue(access.IsReady);
        Assert.IsFalse(access.Movement.Ground);
        Assert.IsFalse(access.Movement.Swim);
        Assert.IsTrue(access.Movement.Fly);
        foreach (var skill in catalog.ActiveSkills.Where(skill => skill.RequiresGroundMovement || skill.RequiresSwimMovement))
            Assert.IsFalse(access.AllowedActiveSkillSourceIds.Contains(skill.SourceSkillId));
        Assert.IsTrue(access.AllowedActiveSkillSourceIds.Contains(catalog.ActiveSkills.Single(skill => skill.Name == "Flight").SourceSkillId));
        var special = CharacterCreationKarmaSkillAccessRules.Evaluate(catalog, talents,
            onlyFly with { Movement = onlyFly.Movement with { IsSpecial = true } }, "mundane")!;
        foreach (var skill in catalog.ActiveSkills.Where(skill => skill.RequiresGroundMovement || skill.RequiresSwimMovement || skill.RequiresFlyMovement))
            Assert.IsFalse(special.AllowedActiveSkillSourceIds.Contains(skill.SourceSkillId));
        Assert.AreEqual(4, catalog.SkillGroups.Single(group => group.Name == "Athletics").MemberSkillSourceIds.Count);
        Assert.AreNotEqual(access.AccessDigest, special.AccessDigest);
        Assert.IsNull(CharacterCreationKarmaSkillAccessRules.Evaluate(catalog, talents,
            human with { IsEnabled = false }, "mundane"));
    }

    [TestMethod]
    [DataRow("unknown-unlock")]
    [DataRow("disabled-book")]
    public void Karma_skill_access_does_not_invent_authority_for_unresolved_talent_source(string change)
    {
        using var fixture = new KarmaDiskFixture(includeSkills: true);
        fixture.EditQuality(MagicianId, row =>
        {
            if (change == "unknown-unlock") row.Element("bonus")!.Element("unlockskills")!.Value = "All Skills";
            else row.Element("source")!.Value = "disabled-book";
        });
        var (catalog, talents, human) = KarmaSkillsSources(fixture);
        Assert.IsNull(CharacterCreationKarmaSkillAccessRules.Evaluate(catalog, talents, human, MagicianId));
    }

    [TestMethod]
    public void Karma_skills_policy_uses_selected_profile_without_priority_or_skill_catalog()
    {
        using var fixture = new KarmaDiskFixture();
        var before = fixture.Store.Get(fixture.Id).Value!;
        var context = fixture.Resolver.TryCreateContext(before.Document.Content)!;
        Assert.IsTrue(context.TryResolveCreationKarmaSkillsPolicy(out var policy));
        Assert.IsNotNull(policy);
        Assert.AreEqual(CharacterCreationKarmaSkillsPolicy.SchemaV1, policy.Schema);
        Assert.AreEqual(CanonicalKarmaSettingsId, policy.SettingsProfileId);
        CollectionAssert.AreEqual(new[] { 2, 2, 1, 1, 5, 5, 7, 7 }, new[]
        {
            policy.KarmaNewActiveSkill, policy.KarmaImproveActiveSkill,
            policy.KarmaNewKnowledgeSkill, policy.KarmaImproveKnowledgeSkill,
            policy.KarmaNewSkillGroup, policy.KarmaImproveSkillGroup,
            policy.KarmaSpecialization, policy.KarmaKnowledgeSpecialization
        });
        Assert.AreEqual(6, policy.MaxActiveSkillRatingCreate);
        Assert.AreEqual(6, policy.MaxKnowledgeSkillRatingCreate);
        Assert.AreEqual(6, policy.MaxSkillGroupRatingCreate);
        Assert.AreEqual("({INTUnaug} + {LOGUnaug}) * 2", policy.KnowledgePointsExpression);
        Assert.IsFalse(policy.UsePointsOnBrokenGroups);
        Assert.IsFalse(policy.StrictSkillGroupsInCreateMode);
        Assert.IsTrue(policy.SpecializationsBreakSkillGroups);
        Assert.IsFalse(policy.AllowPointBuySpecializationsOnKarmaSkills);
        Assert.IsFalse(policy.CompensateSkillGroupKarmaDifference);
        Assert.IsTrue(context.TryResolveCreationSourceProfile(out var profile));
        Assert.AreEqual(profile.RawProfileInputsDigest, policy.RawProfileInputsDigest);
        Assert.AreEqual(CharacterCreationKarmaSkillsPolicyAuthority.ComputeDigest(policy), policy.AuthorityDigest);
        CollectionAssert.AreEqual(new[] { $"settings.xml#setting:{CanonicalKarmaSettingsId}" }, policy.SourceAnchorIds.ToArray());
        AssertJsonEqual(before, fixture.Store.Get(fixture.Id).Value!);
    }

    [TestMethod]
    public void Karma_skills_policy_preserves_house_rules_and_rejects_stale_source_context()
    {
        using var fixture = new KarmaDiskFixture();
        string xml = fixture.Store.Get(fixture.Id).Value!.Document.Content;
        var context = fixture.Resolver.TryCreateContext(xml)!;
        Assert.IsTrue(context.TryResolveCreationKarmaSkillsPolicy(out var old));
        fixture.EditSettings(row =>
        {
            string[] costs = ["karmanewactiveskill", "karmaimproveactiveskill", "karmanewknowledgeskill",
                "karmaimproveknowledgeskill", "karmanewskillgroup", "karmaimproveskillgroup",
                "karmaspecialization", "karmaknospecialization"];
            for (int i = 0; i < costs.Length; i++) row.Element("karmacost")!.SetElementValue(costs[i], i);
            row.SetElementValue("maxskillratingcreate", 9);
            row.SetElementValue("maxskillrating", 7);
            row.SetElementValue("maxknowledgeskillratingcreate", 4);
            row.SetElementValue("maxknowledgeskillrating", 12);
            row.SetElementValue("knowledgepointsexpression", "{LOGUnaug} * 3");
            row.SetElementValue("usepointsonbrokengroups", "True");
            row.SetElementValue("breakskillgroupsincreatemode", "True");
            row.SetElementValue("specializationsbreakskillgroups", "False");
            row.SetElementValue("allowpointbuyspecializationsonkarmaskills", "True");
            row.SetElementValue("compensateskillgroupkarmadifference", "True");
        });
        Assert.IsFalse(context.TryResolveCreationKarmaSkillsPolicy(out var stale));
        Assert.IsNull(stale);
        Assert.IsTrue(fixture.Resolver.TryCreateContext(xml)!.TryResolveCreationKarmaSkillsPolicy(out var policy));
        Assert.IsNotNull(policy);
        CollectionAssert.AreEqual(Enumerable.Range(0, 8).ToArray(), new[]
        {
            policy.KarmaNewActiveSkill, policy.KarmaImproveActiveSkill,
            policy.KarmaNewKnowledgeSkill, policy.KarmaImproveKnowledgeSkill,
            policy.KarmaNewSkillGroup, policy.KarmaImproveSkillGroup,
            policy.KarmaSpecialization, policy.KarmaKnowledgeSpecialization
        });
        Assert.AreEqual(7, policy.MaxActiveSkillRatingCreate);
        Assert.AreEqual(7, policy.MaxSkillGroupRatingCreate);
        Assert.AreEqual(4, policy.MaxKnowledgeSkillRatingCreate);
        Assert.AreEqual("{LOGUnaug} * 3", policy.KnowledgePointsExpression);
        Assert.IsTrue(policy.UsePointsOnBrokenGroups);
        Assert.IsTrue(policy.StrictSkillGroupsInCreateMode);
        Assert.IsFalse(policy.SpecializationsBreakSkillGroups);
        Assert.IsTrue(policy.AllowPointBuySpecializationsOnKarmaSkills);
        Assert.IsTrue(policy.CompensateSkillGroupKarmaDifference);
        Assert.AreNotEqual(old!.AuthorityDigest, policy.AuthorityDigest);
        Assert.AreNotEqual(old.RawProfileInputsDigest, policy.RawProfileInputsDigest);
    }

    [TestMethod]
    public void Karma_skills_policy_defaults_only_absent_optional_legacy_settings()
    {
        using var fixture = new KarmaDiskFixture(configureSettings: row =>
        {
            foreach (string name in new[] { "maxskillrating", "maxskillratingcreate", "maxknowledgeskillrating",
                "maxknowledgeskillratingcreate", "usepointsonbrokengroups", "breakskillgroupsincreatemode",
                "specializationsbreakskillgroups", "allowpointbuyspecializationsonkarmaskills", "compensateskillgroupkarmadifference" })
                row.Elements(name).Remove();
            row.SetElementValue("maxskillratingcreate", 14);
        });
        var context = fixture.Resolver.TryCreateContext(fixture.Store.Get(fixture.Id).Value!.Document.Content)!;
        Assert.IsTrue(context.TryResolveCreationKarmaSkillsPolicy(out var policy));
        Assert.AreEqual(14, policy!.MaxActiveSkillRatingCreate, "Do not impose an absent career-cap field.");
        Assert.AreEqual(6, policy.MaxKnowledgeSkillRatingCreate);
        Assert.IsTrue(policy.SpecializationsBreakSkillGroups);
        Assert.IsFalse(policy.StrictSkillGroupsInCreateMode);
        Assert.IsFalse(policy.UsePointsOnBrokenGroups);
        Assert.IsFalse(policy.AllowPointBuySpecializationsOnKarmaSkills);
        Assert.IsFalse(policy.CompensateSkillGroupKarmaDifference);
    }

    [TestMethod]
    [DataRow("missing-cost")]
    [DataRow("duplicate-cost")]
    [DataRow("negative-cost")]
    [DataRow("overflow-cost")]
    [DataRow("nested-cost")]
    [DataRow("attributed-cost")]
    [DataRow("duplicate-cost-container")]
    [DataRow("duplicate-cap")]
    [DataRow("negative-cap")]
    [DataRow("malformed-career-cap")]
    [DataRow("duplicate-career-cap")]
    [DataRow("duplicate-switch")]
    [DataRow("invalid-switch")]
    [DataRow("missing-expression")]
    [DataRow("empty-expression")]
    [DataRow("nested-expression")]
    [DataRow("duplicate-expression")]
    public void Karma_skills_policy_rejects_malformed_settings_without_substituting_defaults(string corruption)
    {
        using var fixture = new KarmaDiskFixture();
        fixture.EditSettings(row =>
        {
            var costs = row.Element("karmacost")!;
            var cost = costs.Element("karmaimproveactiveskill")!;
            switch (corruption)
            {
                case "missing-cost": cost.Remove(); break;
                case "duplicate-cost": costs.Add(new XElement(cost)); break;
                case "negative-cost": cost.Value = "-1"; break;
                case "overflow-cost": cost.Value = "2147483648"; break;
                case "nested-cost": cost.ReplaceNodes(new XElement("value", "2")); break;
                case "attributed-cost": cost.SetAttributeValue("override", "true"); break;
                case "duplicate-cost-container": row.Add(new XElement(costs)); break;
                case "duplicate-cap": row.SetElementValue("maxskillratingcreate", "6"); row.Add(new XElement("maxskillratingcreate", "6")); break;
                case "negative-cap": row.SetElementValue("maxskillratingcreate", "-1"); break;
                case "malformed-career-cap": row.SetElementValue("maxskillrating", "six"); break;
                case "duplicate-career-cap": row.SetElementValue("maxskillrating", "12"); row.Add(new XElement("maxskillrating", "12")); break;
                case "duplicate-switch": row.SetElementValue("specializationsbreakskillgroups", "True"); row.Add(new XElement("specializationsbreakskillgroups", "True")); break;
                case "invalid-switch": row.SetElementValue("compensateskillgroupkarmadifference", "sometimes"); break;
                case "missing-expression": row.Elements("knowledgepointsexpression").Remove(); break;
                case "empty-expression": row.SetElementValue("knowledgepointsexpression", ""); break;
                case "nested-expression": row.Element("knowledgepointsexpression")!.ReplaceNodes(new XElement("expr", "4")); break;
                case "duplicate-expression": row.Add(new XElement(row.Element("knowledgepointsexpression")!)); break;
                default: Assert.Fail("Unknown corruption."); break;
            }
        });
        var context = fixture.Resolver.TryCreateContext(fixture.Store.Get(fixture.Id).Value!.Document.Content)!;
        Assert.IsFalse(context.TryResolveCreationKarmaSkillsPolicy(out var policy));
        Assert.IsNull(policy);
    }

    [TestMethod]
    [DataRow("active", 0, 3, 2, 2, 12)]
    [DataRow("active", 2, 5, 2, 2, 24)]
    [DataRow("active", 0, 3, 5, 2, 15)]
    [DataRow("active", 0, 3, 5, 0, 5)]
    [DataRow("knowledge", 0, 3, 1, 1, 6)]
    [DataRow("knowledge", 2, 5, 1, 1, 12)]
    [DataRow("knowledge", 0, 3, 5, 2, 15)]
    [DataRow("knowledge", 0, 3, 5, 0, 0)]
    [DataRow("group", 0, 1, 7, 5, 7)]
    [DataRow("group", 0, 2, 7, 5, 15)]
    [DataRow("group", 2, 5, 5, 5, 60)]
    [DataRow("group", 0, 6, 5, 5, 105)]
    public void Karma_skill_cost_preserves_distinct_active_knowledge_and_group_semantics(
        string kind, int lower, int upper, int newCost, int improve, int expected)
    {
        Assert.IsTrue(SkillCost(kind, lower, upper, newCost, improve, out int cost));
        Assert.AreEqual(expected, cost);
    }

    [TestMethod]
    public void Karma_skill_cost_bounds_overflow_and_group_split_do_not_lose_karma()
    {
        foreach (string kind in new[] { "active", "knowledge", "group" })
        {
            foreach (var (lower, upper, newCost, improve) in new[]
            {
                (-1, 1, 1, 1), (3, 2, 1, 1), (0, 1, -1, 1), (0, 1, 1, -1),
                (0, int.MaxValue, int.MaxValue, int.MaxValue), (0, 100_000, 1, 1)
            })
            {
                Assert.IsFalse(SkillCost(kind, lower, upper, newCost, improve, out int invalid));
                Assert.AreEqual(0, invalid);
            }
            Assert.IsTrue(SkillCost(kind, int.MaxValue, int.MaxValue, 1, 1, out int none));
            Assert.AreEqual(0, none);
            Assert.IsTrue(SkillCost(kind, int.MaxValue - 1, int.MaxValue, 1, 1, out int large));
            Assert.AreEqual(int.MaxValue, large);
            Assert.IsTrue(SkillCost(kind, 0, int.MaxValue, 0, 0, out int free));
            Assert.AreEqual(0, free);
        }
        // Three members, each with two personal ranks plus three group ranks:
        // the minimum total rating is five, so the group interval is [2,5].
        Assert.IsTrue(CharacterCreationSkillCostRules.TryGroup(2, 5, 5, 5, out int group));
        Assert.IsTrue(CharacterCreationSkillCostRules.TryActive(0, 2, 2, 2, out int personal));
        Assert.AreEqual(78, group + 3 * personal);
    }

    private static bool SkillCost(string kind, int lower, int upper, int newCost, int improve, out int cost)
    {
        return kind switch
        {
            "active" => CharacterCreationSkillCostRules.TryActive(lower, upper, newCost, improve, out cost),
            "knowledge" => CharacterCreationSkillCostRules.TryKnowledge(lower, upper, newCost, improve, out cost),
            "group" => CharacterCreationSkillCostRules.TryGroup(lower, upper, newCost, improve, out cost),
            _ => throw new ArgumentException("Unknown skill cost kind.", nameof(kind))
        };
    }

    [TestMethod]
    public void Karma_attributes_use_racial_minima_without_priority_or_special_grants()
    {
        using var fixture = new KarmaDiskFixture();
        var state = fixture.Service.Load(fixture.Id).Value!;
        var mundane = fixture.Service.Preview(state.Binding, HumanId, CharacterCreationKarmaTalentCatalog.MundaneOptionId,
            [new("BOD", 3), new("EDG", 1)]).Value!;
        Assert.IsTrue(mundane.CanSelect);
        var attributes = mundane.Attributes!.Attributes;
        Assert.AreEqual(4, attributes.Single(item => item.AttributeId == "BOD").Current);
        Assert.AreEqual(45, attributes.Single(item => item.AttributeId == "BOD").KarmaCost);
        Assert.AreEqual(3, attributes.Single(item => item.AttributeId == "EDG").Current);
        Assert.AreEqual(15, attributes.Single(item => item.AttributeId == "EDG").KarmaCost);
        Assert.AreEqual(6, attributes.Single(item => item.AttributeId == "ESS").Current);
        Assert.IsFalse(attributes.Single(item => item.AttributeId == "MAG").IsEnabled);
        Assert.AreEqual(0, attributes.Single(item => item.AttributeId == "MAG").Current);
        Assert.AreEqual(740m, mundane.KarmaBudget.Remaining);
        Assert.IsTrue(attributes.All(item => item.PriorityPointsSpent == 0 && item.PriorityPointCost == 0));
        var magic = fixture.Service.Preview(state.Binding, HumanId, MagicianId, [new("MAG", 1)]).Value!;
        var mag = magic.Attributes!.Attributes.Single(item => item.AttributeId == "MAG");
        Assert.AreEqual(1, mag.Minimum);
        Assert.AreEqual(2, mag.Current);
        Assert.AreEqual(10, mag.KarmaCost);
        Assert.AreEqual(760m, magic.KarmaBudget.Remaining);
        Assert.IsFalse(magic.Attributes.Attributes.Single(item => item.AttributeId == "RES").IsEnabled);
    }

    [TestMethod]
    [DataRow(false, false, 35)]
    [DataRow(false, true, 35)]
    [DataRow(true, false, 25)]
    [DataRow(true, true, 25)]
    public void Karma_attributes_apply_profile_cost_house_rules_without_priority_points(bool alternate, bool reverse, int cost)
    {
        using var fixture = new KarmaDiskFixture(configureSettings: row =>
        {
            row.Element("alternatemetatypeattributekarma")!.Value = alternate.ToString();
            row.Element("reverseattributepriorityorder")!.Value = reverse.ToString();
        });
        var state = fixture.Service.Load(fixture.Id).Value!;
        var quote = fixture.Service.Preview(state.Binding, ElfId, CharacterCreationKarmaTalentCatalog.MundaneOptionId,
            [new("AGI", 2)]).Value!;
        Assert.IsTrue(quote.CanSelect);
        var agility = quote.Attributes!.Attributes.Single(item => item.AttributeId == "AGI");
        Assert.AreEqual(4, agility.Current);
        Assert.AreEqual(cost, agility.KarmaCost);
        Assert.AreEqual(800m - 40 - cost, quote.KarmaBudget.Remaining);
    }

    [TestMethod]
    [DataRow("MAG", 1)]
    [DataRow("RES", 1)]
    [DataRow("DEP", 1)]
    [DataRow("ESS", 1)]
    [DataRow("BOD", 6)]
    [DataRow("BOD", int.MaxValue)]
    public void Karma_attributes_reject_disabled_and_over_cap_purchases_without_writing(string attribute, int levels)
    {
        using var fixture = new KarmaDiskFixture();
        var before = fixture.Store.Get(fixture.Id).Value!;
        var state = fixture.Service.Load(fixture.Id).Value!;
        CharacterCreationKarmaAttributeAllocation[] allocations = [new(attribute, levels)];
        var quote = fixture.Service.Preview(state.Binding, HumanId, CharacterCreationKarmaTalentCatalog.MundaneOptionId,
            allocations).Value!;
        Assert.IsFalse(quote.CanSelect);
        Assert.IsNull(fixture.Service.Confirm(new(quote.Binding, HumanId, quote.QuoteDigest, Guid.NewGuid(), true,
            CharacterCreationKarmaTalentCatalog.MundaneOptionId, allocations)).Value);
        AssertJsonEqual(before, fixture.Store.Get(fixture.Id).Value);
    }

    [TestMethod]
    public void Karma_attributes_reject_unknown_duplicate_negative_and_missing_talent_allocations()
    {
        using var fixture = new KarmaDiskFixture();
        var state = fixture.Service.Load(fixture.Id).Value!;
        foreach (var invalid in new CharacterCreationKarmaAttributeAllocation[][]
        {
            [new("bod", 1)], [new("BOD", -1)], [new("BOD", 1), new("BOD", 2)], [null!]
        })
            Assert.IsNull(fixture.Service.Preview(state.Binding, HumanId, MagicianId, invalid).Value);
        Assert.IsNull(fixture.Service.Preview(state.Binding, HumanId, null, []).Value);
    }

    [TestMethod]
    public void Karma_attributes_apply_normal_maximum_count_but_not_to_edge_or_magic()
    {
        using var fixture = new KarmaDiskFixture();
        var state = fixture.Service.Load(fixture.Id).Value!;
        var blocked = fixture.Service.Preview(state.Binding, HumanId, MagicianId,
            [new("BOD", 5), new("AGI", 5)]).Value!;
        Assert.IsFalse(blocked.CanSelect);
        CollectionAssert.Contains(blocked.Blockers.ToArray(), CharacterCreationAttributesBlockers.MaximumAttributeCountExceeded);
        var valid = fixture.Service.Preview(state.Binding, HumanId, MagicianId,
            [new("BOD", 5), new("EDG", 5), new("MAG", 5)]).Value!;
        Assert.IsTrue(valid.CanSelect, string.Join(",", valid.Blockers));
    }

    [TestMethod]
    public void Karma_attributes_use_the_shared_budget_and_profile_maximum_count()
    {
        using var fixture = new KarmaDiskFixture(budget: 70);
        var state = fixture.Service.Load(fixture.Id).Value!;
        var over = fixture.Service.Preview(state.Binding, HumanId, MagicianId, [new("BOD", 3)]).Value!;
        Assert.IsFalse(over.CanSelect);
        Assert.AreEqual(-5m, over.KarmaBudget.Remaining);
        CollectionAssert.Contains(over.Blockers.ToArray(), CharacterCreationKarmaMetatypeBlockers.BudgetExceeded);
        using var generous = new KarmaDiskFixture(configureSettings: row =>
            row.SetElementValue("maxnumbermaxattributescreate", "2"));
        var generousState = generous.Service.Load(generous.Id).Value!;
        Assert.IsTrue(generous.Service.Preview(generousState.Binding, HumanId, MagicianId,
            [new("BOD", 5), new("AGI", 5)]).Value!.CanSelect);
    }

    [TestMethod]
    public void Karma_attributes_persist_with_foundation_and_require_explicit_replacement()
    {
        using var fixture = new KarmaDiskFixture();
        var before = fixture.Store.Get(fixture.Id).Value!;
        CharacterCreationKarmaAttributeAllocation[] allocations = [new("BOD", 3), new("MAG", 1)];
        var request = fixture.Request(ElfId, MagicianId, allocations);
        Assert.IsNotNull(fixture.Service.Confirm(request).Value);
        var store = new FileWorkspaceStore(fixture.StateRoot);
        var service = new CharacterCreationKarmaMetatypeService(store, fixture.Resolver);
        var state = service.Load(fixture.Id).Value!;
        Assert.AreEqual(675m, state.KarmaBudget.Remaining);
        Assert.AreEqual(4, state.Selection!.Quote.Attributes!.Attributes.Single(item => item.AttributeId == "BOD").Current);
        Assert.AreEqual(before.Document.Content, store.Get(fixture.Id).Value!.Document.Content);
        var copiedRequest = JsonSerializer.Deserialize<CharacterCreationKarmaMetatypeConfirmRequest>(JsonSerializer.Serialize(request))!;
        Assert.IsTrue(service.Confirm(copiedRequest).Value!.Replayed, "Retry identity must not depend on an array reference.");
        Assert.IsNull(service.Preview(state.Binding, HumanId, MagicianId).Value);
        Assert.IsFalse(service.Preview(state.Binding, HumanId, CharacterCreationKarmaTalentCatalog.MundaneOptionId,
            allocations).Value!.CanSelect, "A talent replacement cannot retain a paid but disabled MAG.");
        var reset = service.Preview(state.Binding, HumanId, CharacterCreationKarmaTalentCatalog.MundaneOptionId, []).Value!;
        Assert.IsNotNull(service.Confirm(new(reset.Binding, HumanId, reset.QuoteDigest, Guid.NewGuid(), true,
            CharacterCreationKarmaTalentCatalog.MundaneOptionId, [])).Value);
        Assert.AreEqual(800m, service.Load(fixture.Id).Value!.KarmaBudget.Remaining);
        Assert.IsNull(service.Confirm(request with { AttributeAllocations = [new("BOD", 1)] }).Value);

        // Explicit [] is a reviewed reset, while null means this step was never
        // chosen. Imported history must not erase that distinction either.
        var saved = store.Get(fixture.Id).Value!;
        var ledger = saved.Document.AuxiliaryState.CharacterCreationKarmaMetatypeDecisions!;
        var erasedQuote = ledger[1].Quote with { Attributes = null, QuoteDigest = string.Empty };
        erasedQuote = erasedQuote with { QuoteDigest = CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(erasedQuote) };
        var erasedDecision = ledger[1] with { Quote = erasedQuote,
            Command = ledger[1].Command with { AttributeAllocations = null, QuoteDigest = erasedQuote.QuoteDigest } };
        erasedDecision = erasedDecision with { DecisionDigest = CharacterCreationKarmaMetatypeTransaction.DecisionDigest(erasedDecision) };
        Assert.IsFalse(CharacterCreationKarmaMetatypeTransaction.IsValidLedger(fixture.Id, saved.ContentRevision,
            saved.Document.AuxiliaryState with { CharacterCreationKarmaMetatypeDecisions = [ledger[0], erasedDecision] }));
    }

    [TestMethod]
    public void Karma_attributes_freeze_caller_allocations_before_atomic_storage()
    {
        using var fixture = new KarmaDiskFixture();
        CharacterCreationKarmaAttributeAllocation[] allocations = [new("BOD", 3)];
        var request = fixture.Request(HumanId, MagicianId, allocations);
        fixture.Fault.Action = stage =>
        {
            if (stage == FileWorkspaceStoreFaultStage.AfterTempFileFlushed) allocations[0] = new("BOD", 99);
        };
        Assert.IsNotNull(fixture.Service.Confirm(request).Value);
        fixture.Fault.Action = null;
        var state = fixture.Service.Load(fixture.Id).Value!;
        Assert.AreEqual(725m, state.KarmaBudget.Remaining);
        Assert.AreEqual(3, state.Selection!.Command.AttributeAllocations![0].KarmaLevels);
        Assert.IsNull(fixture.Service.Confirm(request).Value, "An altered command is not an exact retry.");
    }

    [TestMethod]
    public void Karma_attributes_forged_derived_projection_is_not_valid_history_even_with_fresh_hashes()
    {
        using var fixture = new KarmaDiskFixture();
        Assert.IsNotNull(fixture.Service.Confirm(fixture.Request(HumanId, MagicianId, [new("BOD", 3)])).Value);
        var saved = fixture.Store.Get(fixture.Id).Value!;
        var decision = saved.Document.AuxiliaryState.CharacterCreationKarmaMetatypeDecisions![0];
        var attributes = decision.Quote.Attributes!;
        attributes = attributes with { KarmaUsed = 0, QuoteDigest = string.Empty };
        attributes = attributes with { QuoteDigest = CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(attributes) };
        var quote = decision.Quote with { Attributes = attributes, KarmaBudget = decision.Quote.KarmaBudget with
            { Used = 30, Remaining = 770 }, QuoteDigest = string.Empty };
        quote = quote with { QuoteDigest = CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(quote) };
        decision = decision with { Quote = quote, Command = decision.Command with { QuoteDigest = quote.QuoteDigest } };
        decision = decision with { DecisionDigest = CharacterCreationKarmaMetatypeTransaction.DecisionDigest(decision) };
        Assert.IsFalse(CharacterCreationKarmaMetatypeTransaction.IsValidLedger(fixture.Id, saved.ContentRevision,
            saved.Document.AuxiliaryState with { CharacterCreationKarmaMetatypeDecisions = [decision] }));
    }

    [TestMethod]
    public void Karma_attributes_use_profile_multiplier_and_do_not_default_missing_policy()
    {
        using var fixture = new KarmaDiskFixture(configureSettings: row => row.Element("karmacost")!.Element("karmaattribute")!.Value = "7");
        var state = fixture.Service.Load(fixture.Id).Value!;
        Assert.AreEqual(35m, fixture.Service.Preview(state.Binding, HumanId, MagicianId, [new("BOD", 2)]).Value!.Attributes!.KarmaUsed);
        using var missing = new KarmaDiskFixture(configureSettings: row => row.Element("karmacost")!.Element("karmaattribute")!.Remove());
        state = missing.Service.Load(missing.Id).Value!;
        Assert.IsNull(state.AttributePolicy);
        Assert.IsNull(missing.Service.Preview(state.Binding, HumanId, MagicianId, []).Value);
    }

    [TestMethod]
    [DataRow("<quality>")]
    [DataRow("<!DOCTYPE quality [<!ENTITY x 'not allowed'>]><quality>&x;</quality>")]
    [DataRow("<unrelated />")]
    public void Karma_attributes_reject_malformed_embedded_talent_xml_without_throwing(string xml)
    {
        using var fixture = new KarmaDiskFixture();
        var state = fixture.Service.Load(fixture.Id).Value!;
        var talent = state.Talents!.Options.Single(item => item.OptionId == MagicianId) with { SourceNodeXml = xml };
        Assert.IsNull(CharacterCreationKarmaAttributesRules.Evaluate(state.Options.Single(item => item.OptionId == HumanId),
            talent, state.AttributePolicy!, []));
    }

    [TestMethod]
    [DataRow("0e741331-d776-4be8-abc5-4101228abdef", 30, "MAG")]
    [DataRow("55247bdc-c313-4614-ae15-5012308096ff", 20, "MAG")]
    [DataRow("9d53e1e4-3f31-40cb-bfbe-4b94f5ba757e", 35, "MAG")]
    [DataRow("c4b35412-bd91-45b4-b428-29da7edd5ff4", 15, "RES")]
    [DataRow("4adeb2d4-e42e-4b7a-9a5d-3df325ae59a5", 15, "MAG")]
    public void Karma_talent_quotes_real_quality_costs_without_priority_grants(string id, int cost, string attribute)
    {
        using var fixture = new KarmaDiskFixture();
        var before = fixture.Store.Get(fixture.Id).Value!;
        var state = fixture.Service.Load(fixture.Id).Value!;
        Assert.IsNotNull(state.Talents);
        var quote = fixture.Service.Preview(state.Binding, ElfId, id).Value!;
        Assert.IsNotNull(quote, string.Join(",", state.Talents.Options.Single(item => item.OptionId == id).Blockers));
        Assert.IsTrue(quote.CanSelect);
        Assert.AreEqual(cost, quote.Talent!.KarmaCost);
        Assert.AreEqual(attribute, quote.Talent.EnabledAttribute);
        Assert.AreEqual(800m - 40 - cost, quote.KarmaBudget.Remaining);
        Assert.AreEqual(CharacterCreationQualitiesRules.ComputeSourceNodeDigest(quote.Talent.SourceNodeXml), quote.Talent.SourceNodeDigest);
        CollectionAssert.Contains(quote.SourceAnchorIds.ToArray(), "SR5:69");
        AssertJsonEqual(before, fixture.Store.Get(fixture.Id).Value);
        Assert.IsFalse(fixture.Resolver.TryCreateContext(before.Document.Content)!.TryResolveCreationPrerequisiteAuthority(out _),
            "No priorities.xml exists in this fixture.");
    }

    [TestMethod]
    public void Karma_talent_uses_profile_multiplier_and_effective_source_cost()
    {
        using var fixture = new KarmaDiskFixture(qualityMultiplier: 2);
        fixture.EditQuality(MagicianId, row => row.Element("karma")!.Value = "7");
        var state = fixture.Service.Load(fixture.Id).Value!;
        var quote = fixture.Service.Preview(state.Binding, HumanId, MagicianId).Value!;
        Assert.AreEqual(14, quote.Talent!.KarmaCost);
        Assert.AreEqual(786m, quote.KarmaBudget.Remaining);
        fixture.EditQuality(MagicianId, row => row.Element("karma")!.Value = "8");
        Assert.IsNull(fixture.Service.Preview(state.Binding, HumanId, MagicianId).Value);
        Assert.IsNotNull(fixture.Service.Load(fixture.Id).Value, "An unselected talent can be refreshed.");
    }

    [TestMethod]
    [DataRow("negative-cost")]
    [DataRow("duplicate-cost")]
    [DataRow("effect")]
    [DataRow("requirement")]
    [DataRow("disabled-book")]
    [DataRow("quality-limit")]
    public void Karma_talent_does_not_admit_unresolved_or_disabled_sources(string change)
    {
        using var fixture = new KarmaDiskFixture();
        fixture.EditQuality(MagicianId, row =>
        {
            switch (change)
            {
                case "negative-cost": row.Element("karma")!.Value = "-1"; break;
                case "duplicate-cost": row.Add(new XElement("karma", "1")); break;
                case "effect": row.Element("bonus")!.Add(new XElement("selectskill")); break;
                case "requirement": row.Add(new XElement("required", new XElement("quality", "Imaginary"))); break;
                case "disabled-book": row.Element("source")!.Value = "DISABLED"; break;
                case "quality-limit": row.Element("contributetolimit")!.Value = "True"; break;
            }
        });
        var state = fixture.Service.Load(fixture.Id).Value!;
        var option = state.Talents!.Options.Single(item => item.OptionId == MagicianId);
        Assert.IsFalse(option.IsEnabled);
        Assert.IsNull(fixture.Service.Preview(state.Binding, HumanId, MagicianId).Value);
        Assert.IsNotNull(fixture.Service.Preview(state.Binding, HumanId, CharacterCreationKarmaTalentCatalog.MundaneOptionId).Value);
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(-1)]
    [DataRow(int.MaxValue)]
    public void Karma_talent_does_not_guess_invalid_or_overflowing_costs(int multiplier)
    {
        using var fixture = new KarmaDiskFixture(qualityMultiplier: multiplier);
        var state = fixture.Service.Load(fixture.Id).Value!;
        Assert.IsNull(fixture.Service.Preview(state.Binding, HumanId, MagicianId).Value);
    }

    [TestMethod]
    public void Karma_talent_and_metatype_commit_together_and_reopen_without_double_spending()
    {
        using var fixture = new KarmaDiskFixture();
        var before = fixture.Store.Get(fixture.Id).Value!;
        var request = fixture.Request(ElfId, MagicianId);
        Assert.IsNotNull(fixture.Service.Confirm(request).Value);
        var store = new FileWorkspaceStore(fixture.StateRoot);
        var service = new CharacterCreationKarmaMetatypeService(store, fixture.Resolver);
        var state = service.Load(fixture.Id).Value!;
        Assert.AreEqual(730m, state.KarmaBudget.Remaining);
        Assert.AreEqual(MagicianId, state.Selection!.Quote.Talent!.OptionId);
        Assert.AreEqual(before.ContentRevision + 1, store.Get(fixture.Id).Value!.SavedRevision);
        Assert.AreEqual(before.Document.Content, store.Get(fixture.Id).Value!.Document.Content,
            "No free Magic, Resonance, spells or skill grants may be applied by a pending selection.");
        Assert.IsTrue(service.Confirm(request).Value!.Replayed);
        Assert.IsNull(service.Preview(state.Binding, HumanId).Value, "Dropping talent is not an implicit Mundane selection.");
        var mundane = service.Preview(state.Binding, HumanId, CharacterCreationKarmaTalentCatalog.MundaneOptionId).Value!;
        Assert.AreEqual(800m, mundane.KarmaBudget.Remaining);
        Assert.IsNotNull(service.Confirm(new(mundane.Binding, HumanId, mundane.QuoteDigest, Guid.NewGuid(), true,
            CharacterCreationKarmaTalentCatalog.MundaneOptionId)).Value);
        Assert.AreEqual(800m, service.Load(fixture.Id).Value!.KarmaBudget.Remaining);
        Assert.IsNull(service.Confirm(request with { TalentOptionId = CharacterCreationKarmaTalentCatalog.MundaneOptionId }).Value);
    }

    [TestMethod]
    public void Karma_talent_combined_budget_is_enforced_and_source_drift_cannot_commit()
    {
        using var fixture = new KarmaDiskFixture(budget: 69);
        var before = fixture.Store.Get(fixture.Id).Value!;
        var overBudget = fixture.Request(ElfId, MagicianId);
        Assert.IsNull(fixture.Service.Confirm(overBudget).Value);
        AssertJsonEqual(before, fixture.Store.Get(fixture.Id).Value);
        var valid = fixture.Request(HumanId, MagicianId);
        fixture.Fault.Action = stage =>
        {
            if (stage == FileWorkspaceStoreFaultStage.AfterTempFileFlushed)
                fixture.EditQuality(MagicianId, row => row.Element("karma")!.Value = "31");
        };
        Assert.IsNull(fixture.Service.Confirm(valid).Value);
        fixture.Fault.Action = null;
        AssertJsonEqual(before, fixture.Store.Get(fixture.Id).Value);
    }

    [TestMethod]
    public void Karma_talent_unresolved_and_missing_choices_are_not_assumed_mundane()
    {
        using var fixture = new KarmaDiskFixture();
        var state = fixture.Service.Load(fixture.Id).Value!;
        foreach (string id in new[] { "", "Magician", "Mundane", Guid.NewGuid().ToString("D") })
            Assert.IsNull(fixture.Service.Preview(state.Binding, HumanId, id).Value);
        var noTalent = fixture.Service.Preview(state.Binding, HumanId).Value!;
        Assert.IsNull(noTalent.Talent);
        fixture.RemoveQualitySource();
        state = fixture.Service.Load(fixture.Id).Value!;
        Assert.IsNull(state.Talents);
        Assert.IsNull(fixture.Service.Preview(state.Binding, HumanId, CharacterCreationKarmaTalentCatalog.MundaneOptionId).Value);
    }

    [TestMethod]
    public void Karma_talent_respects_metatype_granted_quality_exclusions()
    {
        using var fixture = new KarmaDiskFixture();
        var elf = fixture.Service.Load(fixture.Id).Value!.Options.Single(option => option.OptionId == ElfId);
        Assert.IsNotEmpty(elf.GrantedQualities);
        fixture.EditQuality(MagicianId, row => row.Element("forbidden")!.Element("oneof")!
            .Add(new XElement("quality", elf.GrantedQualities[0].Name)));
        var state = fixture.Service.Load(fixture.Id).Value!;
        Assert.IsNull(fixture.Service.Preview(state.Binding, ElfId, MagicianId).Value);
        Assert.IsNotNull(fixture.Service.Preview(state.Binding, HumanId, MagicianId).Value);
    }

    [TestMethod]
    public void Karma_talent_cannot_hide_drift_after_persistence_or_reuse_a_stale_source_capture()
    {
        using var fixture = new KarmaDiskFixture();
        var xml = fixture.Store.Get(fixture.Id).Value!.Document.Content;
        var context = fixture.Resolver.TryCreateContext(xml)!;
        Assert.IsTrue(context.TryResolveCreationKarmaTalents(out _));
        Assert.IsNotNull(fixture.Service.Confirm(fixture.Request(HumanId, MagicianId)).Value);
        fixture.EditQuality(MagicianId, row => row.Element("karma")!.Value = "31");
        Assert.IsFalse(context.TryResolveCreationKarmaTalents(out _));
        Assert.IsNull(fixture.Service.Load(fixture.Id).Value);
    }

    [TestMethod]
    [DataRow(1, 0, 5, 0)]
    [DataRow(1, 1, 5, 10)]
    [DataRow(1, 5, 5, 100)]
    [DataRow(2, 1, 5, 15)]
    [DataRow(3, 3, 5, 75)]
    [DataRow(0, 1, 5, 5)]
    [DataRow(2, 3, 7, 84)]
    [DataRow(0, 65535, 1, 2147450880)]
    public void Karma_attribute_cost_is_sum_of_new_ratings_with_source_multiplier(int initial, int levels, int multiplier, int expected)
    {
        Assert.IsTrue(CharacterCreationAttributeCostRules.TryCalculate(initial, levels, multiplier, out int cost));
        Assert.AreEqual(expected, cost);
        long sum = 0;
        for (int level = 1; level <= levels; level++) sum += (long)(initial + level) * multiplier;
        Assert.AreEqual((long)expected, sum);
    }

    [TestMethod]
    [DataRow(-1, 1, 5)]
    [DataRow(1, -1, 5)]
    [DataRow(1, 1, 0)]
    [DataRow(1, 1, -1)]
    [DataRow(int.MaxValue, int.MaxValue, int.MaxValue)]
    [DataRow(0, 65536, 1)]
    public void Karma_attribute_cost_rejects_invalid_or_unrepresentable_input(int initial, int levels, int multiplier)
    {
        Assert.IsFalse(CharacterCreationAttributeCostRules.TryCalculate(initial, levels, multiplier, out int cost));
        Assert.AreEqual(0, cost);
    }

    [TestMethod]
    public void Karma_attribute_policy_uses_real_profile_without_any_priority_source()
    {
        using var fixture = new KarmaDiskFixture();
        var state = fixture.Service.Load(fixture.Id).Value!;
        var policy = state.AttributePolicy;
        Assert.IsNotNull(policy, "Minimal fixture has settings/metatypes but no priorities.xml.");
        Assert.AreEqual(CharacterCreationAttributePolicy.SchemaV1, policy.Schema);
        Assert.AreEqual(CharacterCreationBuildMethods.Karma, policy.BuildMethod);
        Assert.AreEqual(CanonicalKarmaSettingsId, policy.SettingsProfileId);
        Assert.AreEqual(5, policy.KarmaAttribute);
        Assert.AreEqual(1, policy.MaxNumberMaxAttributesCreate);
        Assert.IsFalse(policy.AlternateMetatypeAttributeKarma);
        Assert.IsFalse(policy.ReverseAttributePriorityOrder);
        Assert.AreEqual(state.Binding.SourceProfileDigest, policy.RawProfileInputsDigest);
        Assert.AreEqual(policy.AuthorityDigest, CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(
            policy with { AuthorityDigest = string.Empty }));
    }

    [TestMethod]
    public void Karma_attribute_policy_preserves_house_rules_and_rejects_context_drift()
    {
        using var fixture = new KarmaDiskFixture();
        var before = fixture.Store.Get(fixture.Id).Value!;
        var context = fixture.Resolver.TryCreateContext(before.Document.Content)!;
        Assert.IsTrue(context.TryResolveCreationAttributePolicy(out var old));
        fixture.EditSettings(setting =>
        {
            setting.Element("karmacost")!.Element("karmaattribute")!.Value = "7";
            setting.Element("alternatemetatypeattributekarma")!.Value = "True";
            setting.Element("reverseattributepriorityorder")!.Value = "True";
            setting.Elements("maxnumbermaxattributescreate").Remove();
            setting.Add(new XElement("maxnumbermaxattributescreate", "3"));
        });
        Assert.IsFalse(context.TryResolveCreationAttributePolicy(out var stale));
        Assert.IsNull(stale);
        var fresh = fixture.Resolver.TryCreateContext(before.Document.Content)!;
        Assert.IsTrue(fresh.TryResolveCreationAttributePolicy(out var current));
        Assert.IsNotNull(current);
        Assert.AreEqual(7, current.KarmaAttribute);
        Assert.AreEqual(3, current.MaxNumberMaxAttributesCreate);
        Assert.IsTrue(current.AlternateMetatypeAttributeKarma);
        Assert.IsTrue(current.ReverseAttributePriorityOrder);
        Assert.AreNotEqual(old!.AuthorityDigest, current.AuthorityDigest);
        Assert.IsNull(fixture.Service.Load(fixture.Id).Value,
            "A changed profile must not silently rebind a persisted bootstrap.");
        AssertJsonEqual(before, fixture.Store.Get(fixture.Id).Value!);
    }

    [TestMethod]
    [DataRow("missing-cost")]
    [DataRow("duplicate-cost")]
    [DataRow("negative-cost")]
    [DataRow("zero-cost")]
    [DataRow("invalid-cost")]
    [DataRow("duplicate-cap")]
    [DataRow("negative-cap")]
    [DataRow("duplicate-alternate")]
    [DataRow("missing-reverse")]
    public void Karma_attribute_policy_rejects_malformed_settings_instead_of_defaulting(string corruption)
    {
        using var fixture = new KarmaDiskFixture();
        fixture.EditSettings(setting =>
        {
            XElement costs = setting.Element("karmacost")!;
            XElement multiplier = costs.Element("karmaattribute")!;
            switch (corruption)
            {
                case "missing-cost": multiplier.Remove(); break;
                case "duplicate-cost": costs.Add(new XElement(multiplier)); break;
                case "negative-cost": multiplier.Value = "-1"; break;
                case "zero-cost": multiplier.Value = "0"; break;
                case "invalid-cost": multiplier.Value = "five"; break;
                case "duplicate-cap":
                    setting.Elements("maxnumbermaxattributescreate").Remove();
                    setting.Add(new XElement("maxnumbermaxattributescreate", "1"), new XElement("maxnumbermaxattributescreate", "1"));
                    break;
                case "negative-cap":
                    setting.Elements("maxnumbermaxattributescreate").Remove();
                    setting.Add(new XElement("maxnumbermaxattributescreate", "-1"));
                    break;
                case "duplicate-alternate": setting.Add(new XElement(setting.Element("alternatemetatypeattributekarma")!)); break;
                case "missing-reverse": setting.Element("reverseattributepriorityorder")!.Remove(); break;
            }
        });
        var context = fixture.Resolver.TryCreateContext(fixture.Store.Get(fixture.Id).Value!.Document.Content)!;
        Assert.IsFalse(context.TryResolveCreationAttributePolicy(out var policy));
        Assert.IsNull(policy);
    }

    [TestMethod]
    public void Karma_metatype_services_are_registered_in_the_headless_runtime()
    {
        var services = new ServiceCollection();
        services.AddChummerHeadlessCore(FindCoreRoot(), FindCoreRoot());
        using var provider = services.BuildServiceProvider();
        Assert.IsInstanceOfType<CharacterCreationKarmaMetatypeService>(provider.GetRequiredService<ICharacterCreationKarmaMetatypeService>());
        Assert.IsInstanceOfType<OwnerBoundCharacterCreationKarmaMetatypeService>(provider.GetRequiredService<IOwnerBoundCharacterCreationKarmaMetatypeService>());
    }

    [TestMethod]
    [DataRow(ElfId, "Elf", 40)]
    [DataRow(OrkId, "Ork", 50)]
    public void Karma_metatype_confirmation_persists_reopens_and_replaces_cost_without_double_spending(
        string metatypeId, string name, int cost)
    {
        using var fixture = new KarmaDiskFixture();
        var before = fixture.Store.Get(fixture.Id).Value!;
        var request = fixture.Request(metatypeId);
        var applied = fixture.Service.Confirm(request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, applied.Outcome, string.Join(",", applied.Blockers));
        Assert.IsFalse(applied.Value!.Replayed);
        var reopenedStore = new FileWorkspaceStore(fixture.StateRoot);
        var reopened = new CharacterCreationKarmaMetatypeService(reopenedStore, fixture.Resolver);
        var state = reopened.Load(fixture.Id).Value!;
        Assert.IsNotNull(state.Selection);
        Assert.AreEqual(name, state.Selection.Quote.Metatype.Label);
        Assert.AreEqual(800m - cost, state.KarmaBudget.Remaining);
        var saved = reopenedStore.Get(fixture.Id).Value!;
        Assert.AreEqual(before.ContentRevision + 1, saved.ContentRevision);
        Assert.AreEqual(saved.ContentRevision, saved.SavedRevision);
        Assert.AreEqual(before.Document.Content, saved.Document.Content, "Selection must not finalize or grant effects.");
        Assert.IsTrue(reopened.Confirm(request).Value!.Replayed);
        AssertJsonEqual(saved, reopenedStore.Get(fixture.Id).Value!);

        // A new Human selection replaces the reservation; it does not charge
        // for both selections. The old operation can be queried without undoing it.
        var human = reopened.Preview(state.Binding, "a53d885d-a4a4-443d-b6a6-b0a55b0a96c7").Value!;
        Assert.AreEqual(800m, human.KarmaBudget.Remaining);
        Assert.IsNotNull(reopened.Confirm(new(human.Binding, human.Metatype.OptionId,
            human.QuoteDigest, Guid.NewGuid(), true)).Value);
        Assert.IsTrue(reopened.Confirm(request).Value!.Replayed);
        Assert.AreEqual("Human", reopened.Load(fixture.Id).Value!.Selection!.Quote.Metatype.Label);
        Assert.AreEqual(before.ContentRevision + 2, reopenedStore.Get(fixture.Id).Value!.ContentRevision);
        Assert.IsTrue(reopenedStore.ReadContinuation(fixture.Id).Success,
            "The new draft must remain portable as history, without granting imported replay authority.");
    }

    [TestMethod]
    [DataRow("unconfirmed")]
    [DataRow("digest")]
    [DataRow("option")]
    [DataRow("revision")]
    [DataRow("operation")]
    public void Karma_metatype_confirmation_rejects_invalid_command_without_writing(string change)
    {
        using var fixture = new KarmaDiskFixture();
        var request = fixture.Request("b3259991-b315-4dbe-ae3c-51f71a1116e2");
        var before = fixture.Store.Get(fixture.Id).Value!;
        request = change switch
        {
            "unconfirmed" => request with { ExplicitlyConfirmed = false },
            "digest" => request with { QuoteDigest = "sha256:" + new string('0', 64) },
            "option" => request with { MetatypeOptionId = "a53d885d-a4a4-443d-b6a6-b0a55b0a96c7" },
            "revision" => request with { Binding = request.Binding with { ContentRevision = before.ContentRevision + 1 } },
            _ => request with { OperationId = Guid.Empty }
        };
        Assert.IsNull(fixture.Service.Confirm(request).Value);
        AssertJsonEqual(before, fixture.Store.Get(fixture.Id).Value!);
    }

    [TestMethod]
    public void Karma_metatype_confirmation_rejects_stale_preview_and_reused_operation()
    {
        using var fixture = new KarmaDiskFixture();
        var elf = fixture.Request("b3259991-b315-4dbe-ae3c-51f71a1116e2");
        var human = fixture.Request("a53d885d-a4a4-443d-b6a6-b0a55b0a96c7");
        Assert.IsNotNull(fixture.Service.Confirm(elf).Value);
        var saved = fixture.Store.Get(fixture.Id).Value!;
        Assert.IsNull(fixture.Service.Confirm(human).Value);
        var reused = fixture.Service.Confirm(human with { OperationId = elf.OperationId });
        CollectionAssert.Contains(reused.Blockers.ToArray(), CharacterCreationKarmaMetatypeBlockers.IdempotencyConflict);
        AssertJsonEqual(saved, fixture.Store.Get(fixture.Id).Value!);
    }

    [TestMethod]
    public async Task Karma_metatype_confirmation_concurrent_duplicates_commit_once()
    {
        using var fixture = new KarmaDiskFixture();
        var request = fixture.Request("b3259991-b315-4dbe-ae3c-51f71a1116e2");
        var other = new CharacterCreationKarmaMetatypeService(new FileWorkspaceStore(fixture.StateRoot), fixture.Resolver);
        var results = await Task.WhenAll(Task.Run(() => fixture.Service.Confirm(request)),
            Task.Run(() => other.Confirm(request)));
        Assert.IsTrue(results.All(result => result.Value is not null), string.Join(",", results.SelectMany(result => result.Blockers)));
        Assert.AreEqual(1, results.Count(result => !result.Value!.Replayed));
        Assert.AreEqual(request.Binding.ContentRevision + 1, fixture.Store.Get(fixture.Id).Value!.ContentRevision);
    }

    [TestMethod]
    public void Karma_metatype_confirmation_requires_typed_store_not_generic_auxiliary_write()
    {
        using var fixture = new KarmaDiskFixture();
        var request = fixture.Request("b3259991-b315-4dbe-ae3c-51f71a1116e2");
        var before = fixture.Store.Get(fixture.Id).Value!;
        Assert.IsTrue(CharacterCreationKarmaMetatypeTransaction.TryBuild(OwnerScope.LocalSingleUser,
            before, fixture.Resolver, request, out var replacement, out _));
        Assert.IsFalse(fixture.Store.ReplaceWorkspaceDocumentAndAuxiliaryStateAndCheckpoint(fixture.Id,
            before.ContentRevision, before.Document.AuxiliaryStateDigest, replacement!).Success);
        AssertJsonEqual(before, fixture.Store.Get(fixture.Id).Value!);
        Assert.IsNotNull(fixture.Service.Confirm(request).Value);
        var saved = fixture.Store.Get(fixture.Id).Value!;
        Assert.IsFalse(fixture.Store.ReplaceWorkspaceDocumentAndAuxiliaryStateAndCheckpoint(fixture.Id,
            saved.ContentRevision, saved.Document.AuxiliaryStateDigest, before.Document).Success);
        AssertJsonEqual(saved, fixture.Store.Get(fixture.Id).Value!);
        Assert.IsFalse(fixture.Store.CreateCharacterCreationBootstrapWorkspaceDocument(fixture.Id,
            saved.Document).Success, "A bootstrap create cannot import a pre-built selection ledger.");
        var untrustedLocal = fixture.Store.CommitKarmaMetatype(new OwnerScope("local-single-user"), request, fixture.Resolver);
        Assert.IsNull(untrustedLocal.Value, "A caller-supplied owner string is not the trusted local scope.");
        AssertJsonEqual(saved, fixture.Store.Get(fixture.Id).Value!);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Karma_metatype_confirmation_rejects_source_drift_before_atomic_replace(bool duringWrite)
    {
        using var fixture = new KarmaDiskFixture();
        var request = fixture.Request("b3259991-b315-4dbe-ae3c-51f71a1116e2");
        var before = fixture.Store.Get(fixture.Id).Value!;
        int observedWrites = 0;
        if (duringWrite)
            fixture.Fault.Action = stage =>
            {
                if (stage == FileWorkspaceStoreFaultStage.AfterTempFileFlushed)
                {
                    observedWrites++;
                    fixture.SetBudget(799);
                }
            };
        else fixture.SetBudget(799);
        Assert.IsNull(fixture.Service.Confirm(request).Value);
        Assert.AreEqual(duringWrite ? 1 : 0, observedWrites);
        AssertJsonEqual(before, new FileWorkspaceStore(fixture.StateRoot).Get(fixture.Id).Value!);
    }

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(true, false)]
    [DataRow(false, true)]
    [DataRow(true, true)]
    public void Karma_metatype_confirmation_recovers_exact_commit_or_preserves_previous_state_on_io_failure(bool afterReplace, bool withAttributes)
    {
        using var fixture = new KarmaDiskFixture();
        var request = fixture.Request("b3259991-b315-4dbe-ae3c-51f71a1116e2", withAttributes ? MagicianId : null,
            withAttributes ? [new("BOD", 2)] : null);
        var before = fixture.Store.Get(fixture.Id).Value!;
        fixture.Fault.Action = stage =>
        {
            if (stage == (afterReplace ? FileWorkspaceStoreFaultStage.AfterTargetReplaced
                    : FileWorkspaceStoreFaultStage.AfterTempFileFlushed)) throw new IOException("Injected write failure");
        };
        var result = fixture.Service.Confirm(request);
        fixture.Fault.Action = null;
        var reopenedStore = new FileWorkspaceStore(fixture.StateRoot);
        var recovered = new CharacterCreationKarmaMetatypeService(reopenedStore, fixture.Resolver);
        if (afterReplace)
        {
            Assert.IsNotNull(result.Value);
            Assert.IsTrue(recovered.Confirm(request).Value!.Replayed);
        }
        else
        {
            Assert.IsNull(result.Value);
            AssertJsonEqual(before, reopenedStore.Get(fixture.Id).Value!);
            Assert.IsFalse(recovered.Confirm(request).Value!.Replayed);
        }
        Assert.AreEqual(before.ContentRevision + 1, reopenedStore.Get(fixture.Id).Value!.ContentRevision);
    }

    [TestMethod]
    [DataRow(null, false)]
    [DataRow(MagicianId, false)]
    [DataRow(MagicianId, true)]
    public void Karma_metatype_confirmation_is_owner_scoped_and_rejects_expired_owner_stamp(string? talentId, bool withSkills)
    {
        using var fixture = new KarmaDiskFixture(includeSkills: withSkills);
        using var ownerA = new RequestOwnerContextAccessor(new("karma-owner-a"));
        using var ownerB = new RequestOwnerContextAccessor(new("karma-owner-b"));
        var bootstrap = new OwnerBoundCharacterCreationBootstrapService(
            CreateService(fixture.Store, fixture.Resolver, CreateFileQueries()), ownerA);
        var stamp = ownerA.Capture();
        var created = bootstrap.Create(stamp, KarmaRequest());
        Assert.IsNotNull(created.Value, string.Join(",", created.Blockers));
        var id = created.Value.WorkspaceId;
        var serviceA = new OwnerBoundCharacterCreationKarmaMetatypeService(fixture.Store, ownerA, fixture.Resolver);
        var serviceB = new OwnerBoundCharacterCreationKarmaMetatypeService(fixture.Store, ownerB, fixture.Resolver);
        var state = serviceA.Load(stamp, id, includeSkills: withSkills).Value!;
        var skills = withSkills ? GroupAndKnowledge(KarmaSkillsSources(fixture).Catalog) : null;
        CharacterCreationKarmaAttributeAllocation[]? attributes = withSkills ? [] : null;
        var quote = serviceA.Preview(stamp, state.Binding, "b3259991-b315-4dbe-ae3c-51f71a1116e2", talentId, attributes, skills).Value!;
        var request = new CharacterCreationKarmaMetatypeConfirmRequest(quote.Binding, quote.Metatype.OptionId,
            quote.QuoteDigest, Guid.NewGuid(), true, talentId, attributes, skills);
        Assert.IsNull(serviceB.Confirm(ownerB.Capture(), request).Value);
        Assert.IsNull(fixture.Service.Confirm(request).Value);
        Assert.IsNotNull(serviceA.Confirm(stamp, request).Value);
        Assert.IsNotNull(serviceA.Open(stamp, id).Value?.Quote);
        Assert.IsNull(serviceB.Open(ownerB.Capture(), id).Value);
        Assert.IsFalse(fixture.Store.Get(id).Success);
        Assert.AreEqual(2L, fixture.Store.Get(ownerA.Current, id).Value!.ContentRevision);
        ownerA.Dispose();
        Assert.IsNull(serviceA.Open(stamp, id).Value, "An expired owner cannot reopen the wizard.");
        Assert.IsNull(serviceA.Confirm(stamp, request).Value, "An expired owner cannot even replay a local receipt.");
    }

    [TestMethod]
    public void Karma_metatype_confirmation_cannot_spend_more_than_profile_budget()
    {
        using var fixture = new KarmaDiskFixture(budget: 39);
        var request = fixture.Request("b3259991-b315-4dbe-ae3c-51f71a1116e2");
        var before = fixture.Store.Get(fixture.Id).Value!;
        Assert.IsNull(fixture.Service.Confirm(request).Value);
        AssertJsonEqual(before, fixture.Store.Get(fixture.Id).Value!);
        var human = fixture.Request("a53d885d-a4a4-443d-b6a6-b0a55b0a96c7");
        Assert.IsNotNull(fixture.Service.Confirm(human).Value);
        Assert.AreEqual(39m, fixture.Service.Load(fixture.Id).Value!.KarmaBudget.Remaining);
    }

    [TestMethod]
    [DataRow(null, false, false)]
    [DataRow(MagicianId, false, false)]
    [DataRow(MagicianId, true, false)]
    [DataRow(MagicianId, true, true)]
    public void Karma_metatype_confirmation_continuation_preserves_selection_but_not_foreign_replay_authority(string? talentId, bool withAttributes, bool withSkills)
    {
        using var fixture = new KarmaDiskFixture(fullSources: true);
        CharacterCreationKarmaAttributeAllocation[]? allocations = withAttributes ? [new("BOD", 2)] : null;
        CharacterCreationKarmaSkillsSelection? skills = withSkills ? GroupAndKnowledge(KarmaSkillsSources(fixture).Catalog) : null;
        var request = fixture.Request("b3259991-b315-4dbe-ae3c-51f71a1116e2", talentId, allocations, skills);
        Assert.IsNotNull(fixture.Service.Confirm(request).Value);
        var owner = new LocalOwnerContextAccessor();
        var exported = new WorkspaceContinuationExportService(fixture.Store, owner).Export(owner.Capture(), fixture.Id);
        Assert.IsTrue(exported.Success, exported.Error);
        const int limit = 8 * 1024 * 1024;
        var bytes = WorkspaceContinuationCodec.Encode(exported.Value!, limit);
        Assert.IsTrue(WorkspaceContinuationCodec.TryDecodeCandidate(bytes, limit, out _));
        var target = new FileWorkspaceStore(Path.Combine(fixture.StateRoot, "restored"));
        var restorer = new WorkspaceContinuationRestoreService(target, owner, fixture.Resolver, CreateFileQueries(),
            new XmlLifeModulesCatalogService(Path.Combine(FindCoreRoot(), "Chummer", "data", "lifemodules.xml")), limit);
        using var review = restorer.Review(owner.Capture(), bytes);
        Assert.AreEqual(WorkspaceContinuationRestoreOutcome.Available, review.Result.Outcome,
            string.Join(",", review.Result.Blockers ?? []));
        var restored = restorer.Confirm(review, explicitlyConfirmed: true);
        Assert.AreEqual(WorkspaceContinuationRestoreOutcome.Applied, restored.Outcome,
            string.Join(",", restored.Blockers ?? []));
        var service = new CharacterCreationKarmaMetatypeService(target, fixture.Resolver);
        var state = service.Load(fixture.Id).Value!;
        Assert.AreEqual("Elf", state.Selection!.Quote.Metatype.Label);
        Assert.AreEqual((talentId is null ? 760m : 730m) - (withAttributes ? 25m : 0m) - (withSkills ? 87m : 0m), state.KarmaBudget.Remaining);
        Assert.AreEqual(talentId, state.Selection.Quote.Talent?.OptionId);
        CollectionAssert.Contains(service.Confirm(request).Blockers.ToArray(), CharacterCreationKarmaMetatypeBlockers.IdempotencyConflict);
        var next = service.Preview(state.Binding, "a53d885d-a4a4-443d-b6a6-b0a55b0a96c7", talentId, allocations, skills).Value!;
        Assert.IsNotNull(service.Confirm(new(next.Binding, next.Metatype.OptionId, next.QuoteDigest, Guid.NewGuid(), true, talentId, allocations, skills)).Value);
    }

    [TestMethod]
    public void Karma_metatype_confirmation_corrupted_or_reordered_history_is_not_accepted()
    {
        using var fixture = new KarmaDiskFixture();
        Assert.IsNotNull(fixture.Service.Confirm(fixture.Request("b3259991-b315-4dbe-ae3c-51f71a1116e2")).Value);
        Assert.IsNotNull(fixture.Service.Confirm(fixture.Request("a53d885d-a4a4-443d-b6a6-b0a55b0a96c7")).Value);
        var saved = fixture.Store.Get(fixture.Id).Value!;
        var auxiliary = saved.Document.AuxiliaryState;
        var ledger = auxiliary.CharacterCreationKarmaMetatypeDecisions!;
        foreach (var corrupted in new IReadOnlyList<CharacterCreationKarmaMetatypeDecision>[]
        {
            [], [ledger[0], ledger[0]], [ledger[1], ledger[0]],
            [ledger[0], ledger[1] with { CommittedContentRevision = saved.ContentRevision + 1 }],
            [ledger[0], ledger[1] with { DecisionDigest = "sha256:" + new string('0', 64) }]
        })
            Assert.IsFalse(WorkspaceAuxiliaryStateIntegrity.IsValidShape(fixture.Id, saved.ContentRevision,
                auxiliary with { CharacterCreationKarmaMetatypeDecisions = corrupted }));
    }

    private sealed class KarmaFault : IFileWorkspaceStoreFaultInjector
    {
        public Action<FileWorkspaceStoreFaultStage>? Action { get; set; }
        public void OnStage(FileWorkspaceStoreFaultStage stage, string targetPath, string tempPath) => Action?.Invoke(stage);
    }

    private sealed class KarmaDiskFixture : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "chummer-karma-test-" + Guid.NewGuid().ToString("N"));
        public string StateRoot => Path.Combine(_root, "state");
        public KarmaFault Fault { get; } = new();
        public FileWorkspaceStore Store { get; }
        public FileSystemCharacterSourceDataResolver Resolver { get; }
        public CharacterCreationKarmaMetatypeService Service { get; }
        public CharacterWorkspaceId Id { get; }
        public KarmaDiskFixture(int budget = 800, bool fullSources = false, int qualityMultiplier = 1,
            Action<XElement>? configureSettings = null, bool includeSkills = false, bool includeGear = false,
            bool includeLifestyles = false)
        {
            Directory.CreateDirectory(Path.Combine(_root, "data"));
            foreach (string name in new[] { "settings.xml", "metatypes.xml", "qualities.xml" })
                File.Copy(Path.Combine(FindCoreRoot(), "Chummer", "data", name), Path.Combine(_root, "data", name));
            if (includeSkills)
                foreach (string name in new[] { "skills.xml", "weapons.xml" })
                    File.Copy(Path.Combine(FindCoreRoot(), "Chummer", "data", name), Path.Combine(_root, "data", name));
            if (includeGear)
                File.Copy(Path.Combine(FindCoreRoot(), "Chummer", "data", "gear.xml"), Path.Combine(_root, "data", "gear.xml"));
            if (includeLifestyles)
                File.Copy(Path.Combine(FindCoreRoot(), "Chummer", "data", "lifestyles.xml"), Path.Combine(_root, "data", "lifestyles.xml"));
            if (budget != 800) SetBudget(budget);
            if (qualityMultiplier != 1) EditSettings(row => row.Element("karmacost")!.Element("karmaquality")!.Value =
                qualityMultiplier.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (configureSettings is not null) EditSettings(configureSettings);
            Resolver = CreateSourceResolver(fullSources ? FindCoreRoot() : _root);
            Store = new(StateRoot, Fault);
            var created = CreateService(Store, Resolver, CreateFileQueries()).Create(KarmaRequest());
            Assert.IsNotNull(created.Value, string.Join(",", created.Blockers));
            Id = created.Value.WorkspaceId;
            Service = new(Store, Resolver);
        }
        public CharacterCreationKarmaMetatypeConfirmRequest Request(string optionId, string? talentId = null,
            IReadOnlyList<CharacterCreationKarmaAttributeAllocation>? allocations = null,
            CharacterCreationKarmaSkillsSelection? skills = null, decimal? resources = null,
            IReadOnlyList<string>? qualities = null, IReadOnlyList<CharacterCreationGearSelection>? gear = null,
            IReadOnlyList<CharacterCreationKarmaContactSelection>? contacts = null,
            IReadOnlyList<CharacterCreationLifestyleConfiguration>? lifestyles = null, Guid? startingLifestyleId = null)
        {
            var state = Service.Load(Id, includeSkills: skills is not null, includeQualities: qualities is not null,
                includeGear: gear is not null, includeLifestyles: lifestyles is not null);
            Assert.IsNotNull(state.Value, string.Join(",", state.Blockers));
            var quote = Service.Preview(state.Value.Binding, optionId, talentId, allocations, skills, resources, qualities, gear, contacts,
                lifestyles, startingLifestyleId);
            Assert.IsNotNull(quote.Value, string.Join(",", quote.Blockers));
            return new(quote.Value.Binding, optionId, quote.Value.QuoteDigest, Guid.NewGuid(), true, talentId, allocations, skills, resources,
                qualities, gear, contacts, lifestyles, startingLifestyleId);
        }
        public void RemoveQualitySource() => File.Delete(Path.Combine(_root, "data", "qualities.xml"));
        public void EditSkill(string name, Action<XElement> change)
        {
            string path = Path.Combine(_root, "data", "skills.xml");
            var document = XDocument.Load(path);
            change(document.Root!.Element("skills")!.Elements("skill").Single(row => row.Element("name")?.Value == name));
            document.Save(path);
        }
        public void EditWeapon(string name, Action<XElement> change)
        {
            string path = Path.Combine(_root, "data", "weapons.xml");
            var document = XDocument.Load(path);
            change(document.Root!.Element("weapons")!.Elements("weapon").Single(row => row.Element("name")?.Value == name));
            document.Save(path);
        }
        public void EditQuality(string id, Action<XElement> change)
        {
            string path = Path.Combine(_root, "data", "qualities.xml");
            var document = XDocument.Load(path);
            change(document.Descendants("quality").Single(row => row.Element("id")?.Value == id));
            document.Save(path);
        }
        public void EditGear(string name, Action<XElement> change)
        {
            string path = Path.Combine(_root, "data", "gear.xml");
            var document = XDocument.Load(path);
            change(document.Root!.Element("gears")!.Elements("gear").Single(row => row.Element("name")?.Value == name));
            document.Save(path);
        }
        public void SetBudget(int budget)
            => EditSettings(setting => setting.Element("buildpoints")!.Value = budget.ToString(System.Globalization.CultureInfo.InvariantCulture));
        public void EditLifestyle(string name, Action<XElement> change)
        {
            string path = Path.Combine(_root, "data", "lifestyles.xml");
            var document = XDocument.Load(path);
            change(document.Root!.Element("lifestyles")!.Elements("lifestyle").Single(row => row.Element("name")?.Value == name));
            document.Save(path);
        }
        public void EditSettings(Action<XElement> change)
        {
            string path = Path.Combine(_root, "data", "settings.xml");
            var document = XDocument.Load(path);
            change(document.Descendants("setting").Single(node => node.Element("id")?.Value == CanonicalKarmaSettingsId));
            document.Save(path);
        }
        public void Dispose() => Directory.Delete(_root, recursive: true);
    }

    [TestMethod]
    [DataRow("a53d885d-a4a4-443d-b6a6-b0a55b0a96c7", "Human", 0)]
    [DataRow("b3259991-b315-4dbe-ae3c-51f71a1116e2", "Elf", 40)]
    [DataRow(OrkId, "Ork", 50)]
    public void Karma_metatype_quote_uses_real_profile_and_catalog_without_mutation(
        string optionId, string name, int cost)
    {
        var store = new InMemoryWorkspaceStore();
        var resolver = CreateSourceResolver(FindCoreRoot());
        var created = CreateService(store, resolver, CreateFileQueries()).Create(KarmaRequest());
        Assert.AreEqual(CharacterCreationBootstrapOutcomes.Success, created.Outcome);
        var id = created.Value!.WorkspaceId;
        var before = store.Get(id).Value!;
        var service = new CharacterCreationKarmaMetatypeService(store, resolver);
        var loaded = service.Load(id);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, loaded.Outcome,
            string.Join(",", loaded.Blockers));
        var state = loaded.Value!;
        Assert.AreEqual(800m, state.KarmaBudget.Total);
        Assert.AreEqual(CanonicalKarmaSettingsId, state.SettingsProfileId);
        var preview = service.Preview(state.Binding, optionId);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, preview.Outcome,
            string.Join(",", preview.Blockers));
        var quote = preview.Value!;
        Assert.IsTrue(quote.CanSelect);
        Assert.AreEqual(name, quote.Metatype.Label);
        Assert.AreEqual(cost, quote.Metatype.KarmaCost);
        Assert.AreEqual((decimal)cost, quote.KarmaBudget.Used);
        Assert.AreEqual(800m - cost, quote.KarmaBudget.Remaining);
        CollectionAssert.Contains(quote.SourceAnchorIds.ToArray(), $"settings.xml#setting:{CanonicalKarmaSettingsId}");
        Assert.IsTrue(quote.SourceAnchorIds.Count > 1);
        Assert.AreEqual(quote.QuoteDigest, service.Preview(state.Binding, optionId).Value!.QuoteDigest);
        AssertJsonEqual(before, store.Get(id).Value!);
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    [DataRow(4)]
    [DataRow(5)]
    [DataRow(6)]
    public void Karma_metatype_quote_rejects_stale_or_foreign_binding(int changedField)
    {
        var store = new InMemoryWorkspaceStore();
        var resolver = CreateSourceResolver(FindCoreRoot());
        var created = CreateService(store, resolver, CreateFileQueries()).Create(KarmaRequest());
        var service = new CharacterCreationKarmaMetatypeService(store, resolver);
        var original = service.Load(created.Value!.WorkspaceId).Value!.Binding;
        var binding = changedField switch
        {
            0 => original with { ContentRevision = original.ContentRevision + 1 },
            1 => original with { SavedRevision = original.SavedRevision + 1 },
            2 => original with { RawCharacterXmlDigest = "sha256:" + new string('0', 64) },
            3 => original with { AuxiliaryStateDigest = new string('0', 64) },
            4 => original with { BootstrapBindingDigest = "sha256:" + new string('0', 64) },
            5 => original with { SourceProfileDigest = "sha256:" + new string('0', 64) },
            _ => original with { MetatypeAuthorityDigest = "sha256:" + new string('0', 64) }
        };
        var result = service.Preview(binding, "a53d885d-a4a4-443d-b6a6-b0a55b0a96c7");
        Assert.IsNull(result.Value);
        CollectionAssert.Contains(result.Blockers.ToArray(), CharacterCreationKarmaMetatypeBlockers.StaleBinding);
    }

    [TestMethod]
    [DataRow("Human")]
    [DataRow("A53D885D-A4A4-443D-B6A6-B0A55B0A96C7")]
    [DataRow("invented")]
    public void Karma_metatype_quote_rejects_labels_and_invented_option_ids(string optionId)
    {
        var store = new InMemoryWorkspaceStore();
        var resolver = CreateSourceResolver(FindCoreRoot());
        var created = CreateService(store, resolver, CreateFileQueries()).Create(KarmaRequest());
        var service = new CharacterCreationKarmaMetatypeService(store, resolver);
        var binding = service.Load(created.Value!.WorkspaceId).Value!.Binding;
        var result = service.Preview(binding, optionId);
        Assert.IsNull(result.Value);
        CollectionAssert.Contains(result.Blockers.ToArray(), CharacterCreationKarmaMetatypeBlockers.OptionUnavailable);
    }

    [TestMethod]
    [DataRow(CharacterCreationBuildMethods.Priority, CanonicalPrioritySettingsId)]
    [DataRow(CharacterCreationBuildMethods.SumToTen, CanonicalSumToTenSettingsId)]
    [DataRow(CharacterCreationBuildMethods.LifeModules, CanonicalLifeModulesSettingsId)]
    public void Karma_metatype_quote_does_not_reinterpret_other_build_methods(string method, string profile)
    {
        var store = new InMemoryWorkspaceStore();
        var resolver = CreateSourceResolver(FindCoreRoot());
        var created = CreateService(store, resolver, CreateFileQueries()).Create(
            CanonicalRequest() with { BuildMethod = method, SettingsProfileId = profile });
        var result = new CharacterCreationKarmaMetatypeService(store, resolver).Load(created.Value!.WorkspaceId);
        Assert.IsNull(result.Value);
        CollectionAssert.Contains(result.Blockers.ToArray(),
            CharacterCreationKarmaMetatypeBlockers.PendingKarmaBootstrapRequired);
    }

    [TestMethod]
    [DataRow(0, false)]
    [DataRow(39, false)]
    [DataRow(40, true)]
    [DataRow(1000, true)]
    public void Karma_metatype_quote_uses_source_budget_instead_of_a_hardcoded_total(int total, bool canSelect)
    {
        string root = Directory.CreateTempSubdirectory("chummer-karma-metatype-").FullName;
        try
        {
            string data = Path.Combine(root, "data");
            Directory.CreateDirectory(data);
            foreach (string file in new[] { "settings.xml", "metatypes.xml" })
                File.Copy(Path.Combine(FindCoreRoot(), "Chummer", "data", file), Path.Combine(data, file));
            string settingsPath = Path.Combine(data, "settings.xml");
            XDocument settings = XDocument.Load(settingsPath);
            XElement profile = settings.Descendants("setting").Single(item =>
                item.Element("id")?.Value == CanonicalKarmaSettingsId);
            profile.Element("buildpoints")!.Value = total.ToString(System.Globalization.CultureInfo.InvariantCulture);
            settings.Save(settingsPath);

            var store = new InMemoryWorkspaceStore();
            var resolver = CreateSourceResolver(root);
            var created = CreateService(store, resolver, CreateFileQueries()).Create(KarmaRequest());
            Assert.AreEqual(CharacterCreationBootstrapOutcomes.Success, created.Outcome,
                string.Join(",", created.Blockers));
            var service = new CharacterCreationKarmaMetatypeService(store, resolver);
            var state = service.Load(created.Value!.WorkspaceId).Value!;
            var quote = service.Preview(state.Binding, "b3259991-b315-4dbe-ae3c-51f71a1116e2").Value!;
            Assert.AreEqual((decimal)total, quote.KarmaBudget.Total);
            Assert.AreEqual(total - 40m, quote.KarmaBudget.Remaining);
            Assert.AreEqual(canSelect, quote.CanSelect);
            Assert.AreEqual(canSelect ? 0 : 1, quote.Blockers.Count);

            profile.Element("buildpoints")!.Value = (total + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
            settings.Save(settingsPath);
            Assert.IsNull(service.Preview(state.Binding, quote.Metatype.OptionId).Value,
                "A changed source profile invalidates the original bootstrap and quote.");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static CharacterCreationBootstrapRequest KarmaRequest()
        => CanonicalRequest() with
        {
            BuildMethod = CharacterCreationBuildMethods.Karma,
            SettingsProfileId = CanonicalKarmaSettingsId
        };

    [TestMethod]
    public void Karma_metatype_quote_cannot_be_moved_to_a_second_workspace()
    {
        var store = new InMemoryWorkspaceStore();
        var resolver = CreateSourceResolver(FindCoreRoot());
        var bootstrap = CreateService(store, resolver, CreateFileQueries());
        var first = bootstrap.Create(KarmaRequest()).Value!;
        var second = bootstrap.Create(KarmaRequest()).Value!;
        var service = new CharacterCreationKarmaMetatypeService(store, resolver);
        var binding = service.Load(first.WorkspaceId).Value!.Binding;
        Assert.AreEqual(first.Binding.BindingDigest, binding.BootstrapBindingDigest);
        var result = service.Preview(binding with { WorkspaceId = second.WorkspaceId },
            "a53d885d-a4a4-443d-b6a6-b0a55b0a96c7");
        Assert.IsNull(result.Value);
        CollectionAssert.Contains(result.Blockers.ToArray(), CharacterCreationKarmaMetatypeBlockers.StaleBinding);
    }

    [TestMethod]
    [DataRow("<buildpoints>-1</buildpoints>")]
    [DataRow("<buildpoints>800</buildpoints><buildpoints>800</buildpoints>")]
    [DataRow("<buildpoints>unknown</buildpoints>")]
    [DataRow("")]
    public void Karma_metatype_quote_rejects_missing_or_invalid_profile_budget(string budgetXml)
    {
        string root = Directory.CreateTempSubdirectory("chummer-karma-budget-").FullName;
        try
        {
            string data = Path.Combine(root, "data");
            Directory.CreateDirectory(data);
            foreach (string file in new[] { "settings.xml", "metatypes.xml" })
                File.Copy(Path.Combine(FindCoreRoot(), "Chummer", "data", file), Path.Combine(data, file));
            string settingsPath = Path.Combine(data, "settings.xml");
            XDocument settings = XDocument.Load(settingsPath);
            XElement profile = settings.Descendants("setting").Single(item =>
                item.Element("id")?.Value == CanonicalKarmaSettingsId);
            profile.Elements("buildpoints").Remove();
            profile.Add(XElement.Parse("<budget>" + budgetXml + "</budget>").Elements());
            settings.Save(settingsPath);
            var resolver = CreateSourceResolver(root);
            var store = new InMemoryWorkspaceStore();
            var created = CreateService(store, resolver, CreateFileQueries()).Create(KarmaRequest());
            Assert.AreEqual(CharacterCreationBootstrapOutcomes.Success, created.Outcome);
            var before = store.Get(created.Value!.WorkspaceId).Value!;
            var result = new CharacterCreationKarmaMetatypeService(store, resolver).Load(before.Id);
            Assert.IsNull(result.Value);
            CollectionAssert.Contains(result.Blockers.ToArray(),
                CharacterCreationKarmaMetatypeBlockers.BudgetAuthorityRequired);
            AssertJsonEqual(before, store.Get(before.Id).Value!);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    [DataRow(CharacterCreationBuildMethods.Priority, CanonicalPrioritySettingsId)]
    [DataRow(CharacterCreationBuildMethods.SumToTen, CanonicalSumToTenSettingsId)]
    [DataRow(CharacterCreationBuildMethods.Karma, CanonicalKarmaSettingsId)]
    [DataRow(CharacterCreationBuildMethods.LifeModules, CanonicalLifeModulesSettingsId)]
    public void A_new_sr5_runner_owns_explicit_zero_reputation_and_empty_career_history(string method, string settings)
    {
        var store = new InMemoryWorkspaceStore();
        var service = CreateService(store, CreateSourceResolver(FindCoreRoot()), CreateFileQueries());
        var result = service.Create(CanonicalRequest() with { BuildMethod = method, SettingsProfileId = settings });
        Assert.AreEqual(CharacterCreationBootstrapOutcomes.Success, result.Outcome, string.Join(",", result.Blockers));
        var root = XDocument.Parse(store.Get(result.Value!.WorkspaceId).Value!.Document.Content).Root!;
        var codec = new Sr5WorkspaceCodec(CreateFileQueries(),
            new XmlCharacterSectionQueries(new CharacterSectionService()),
            new XmlCharacterMetadataCommands(new CharacterFileService()));
        Assert.IsTrue(codec.Validate(store.Get(result.Value.WorkspaceId).Value!.Document.PayloadEnvelope).IsValid,
            "A successfully bootstrapped runner must be accepted by the normal SR5 workspace loader.");
        foreach (string field in new[] { "streetcred", "notoriety", "publicawareness", "burntstreetcred" })
        {
            Assert.HasCount(1, root.Elements(field).ToArray(), field);
            Assert.AreEqual("0", root.Element(field)!.Value, field);
        }
        foreach (string container in new[] { "expenses", "improvements", "contacts" })
        {
            Assert.HasCount(1, root.Elements(container).ToArray(), container);
            Assert.IsFalse(root.Element(container)!.Nodes().Any(), container);
        }
        Assert.AreEqual("False", root.Element("created")!.Value);
    }

    [DataTestMethod]
    [DataRow(CharacterCreationBuildMethods.Priority, CanonicalPrioritySettingsId)]
    [DataRow(CharacterCreationBuildMethods.SumToTen, CanonicalSumToTenSettingsId)]
    [DataRow(CharacterCreationBuildMethods.Karma, CanonicalKarmaSettingsId)]
    [DataRow(CharacterCreationBuildMethods.LifeModules, CanonicalLifeModulesSettingsId)]
    public void Canonical_profile_resolution_is_the_single_source_of_method_tuple_truth(
        string buildMethod,
        string expectedSettingsProfileId)
    {
        Assert.IsTrue(CharacterCreationBootstrapProfiles.TryResolveCanonicalSettingsProfileId(
            buildMethod,
            out string settingsProfileId));
        Assert.AreEqual(expectedSettingsProfileId, settingsProfileId);
        Assert.IsTrue(CharacterCreationBootstrapProfiles.IsExactCanonicalTuple(
            buildMethod,
            settingsProfileId));

        Assert.IsFalse(CharacterCreationBootstrapProfiles.TryResolveCanonicalSettingsProfileId(
            buildMethod.ToLowerInvariant(),
            out string unsupportedProfileId));
        Assert.AreEqual(string.Empty, unsupportedProfileId);
    }

    [TestMethod]
    public void Generic_character_validation_accepts_the_pending_priority_shape_without_trusting_the_marker()
    {
        string xml = MinimalMarkerXml();
        var files = new CharacterFileService();

        CharacterValidationResult validation = files.ValidateXml(xml);
        CharacterFileSummary summary = files.ParseSummaryFromXml(xml);

        Assert.IsTrue(validation.IsValid, string.Join(",", validation.Issues.Select(issue => issue.Code)));
        Assert.IsFalse(validation.Issues.Any(issue =>
            issue.Severity == "Error" && issue.Path == "/character/metatype"));
        Assert.AreEqual(string.Empty, summary.Metatype);
        Assert.IsFalse(summary.Created);

        string unknownMarker = xml.Replace(
            CharacterCreationBootstrapSchemas.MarkerV1,
            "unknown-marker",
            StringComparison.Ordinal);
        CharacterValidationResult unknownValidation = files.ValidateXml(unknownMarker);
        Assert.IsTrue(unknownValidation.IsValid,
            "Generic shape validation is not marker authority; the bootstrap service validates the marker binding.");
    }

    [TestMethod]
    public void Canonical_priority_bootstrap_atomically_binds_and_loads_real_creation_authorities()
    {
        string coreRoot = FindCoreRoot();
        var store = new InMemoryWorkspaceStore();
        FileSystemCharacterSourceDataResolver sourceResolver = CreateSourceResolver(coreRoot);
        ICharacterFileQueries queries = CreateFileQueries();
        CharacterCreationBootstrapService service = CreateService(
            store,
            sourceResolver,
            queries);

        CharacterCreationBootstrapResult<CharacterCreationBootstrapReceipt> created =
            service.Create(CanonicalRequest());

        Assert.AreEqual(CharacterCreationBootstrapOutcomes.Success, created.Outcome,
            string.Join(",", created.Blockers));
        CharacterCreationBootstrapReceipt receipt = created.Value!;
        Assert.IsNotNull(receipt);
        Assert.AreEqual(1, receipt.ContentRevision);
        Assert.AreEqual(0, receipt.SavedRevision);
        Assert.AreEqual(string.Empty, receipt.Summary.Metatype);
        Assert.IsFalse(receipt.Summary.Created);
        Assert.IsTrue(CharacterCreationBootstrapBindingDigest.IsValid(receipt.Binding));
        Assert.IsTrue(CharacterCreationBootstrapReceiptDigest.IsValid(receipt));
        Assert.AreEqual(
            CharacterCreationBootstrapRevisions.InitialContentRevision,
            receipt.Binding.InitialContentRevision);
        Assert.AreEqual(
            CharacterCreationBootstrapRevisions.InitialSavedRevision,
            receipt.Binding.InitialSavedRevision);
        CollectionAssert.AreEqual(
            CharacterCreationBootstrapProfiles.ExpectedSourceAnchorIds(
                CharacterCreationBuildMethods.Priority,
                CanonicalPrioritySettingsId),
            receipt.Binding.SourceAnchorIds.ToArray());
        CollectionAssert.AreEqual(
            receipt.Binding.SourceAnchorIds.ToArray(),
            receipt.SourceAnchorIds.ToArray());
        Assert.IsTrue(CharacterCreationPrerequisiteAuthorityDigest.IsCanonical(
            receipt.Binding.RawProfileInputsDigest));
        Assert.IsTrue(CharacterCreationPrerequisiteAuthorityDigest.IsCanonical(
            receipt.Binding.MetatypeAuthorityDigest));
        Assert.IsTrue(CharacterCreationPrerequisiteAuthorityDigest.IsCanonical(
            receipt.Binding.PrerequisiteAuthorityDigest));
        CollectionAssert.Contains(
            receipt.SourceAnchorIds.ToList(),
            $"settings.xml#setting:{CanonicalPrioritySettingsId}");
        CollectionAssert.Contains(receipt.SourceAnchorIds.ToList(), "metatypes.xml");
        CollectionAssert.Contains(receipt.SourceAnchorIds.ToList(), "priorities.xml");

        WorkspaceStoredDocument workspace = store.Get(receipt.WorkspaceId).Value!;
        Assert.IsNotNull(workspace);
        XDocument xml = XDocument.Parse(workspace.Document.Content);
        Assert.IsNull(xml.Root!.Element("metatype"));
        Assert.HasCount(1, xml.Root.Elements(CharacterCreationBootstrapXml.MarkerElement));
        Assert.IsFalse(xml.Root.Elements().Any(element =>
            element.Name.LocalName.StartsWith("priority", StringComparison.Ordinal)));
        Assert.IsNull(xml.Root.Element("lifemodules"));
        Assert.IsTrue(CreateFileQueries().Validate(
            new CharacterDocument(workspace.Document.Content)).IsValid,
            "The generic file contract accepts the pending typed Priority shape; "
            + "the bootstrap binding remains the authority for its incomplete state.");
        Assert.IsTrue(CharacterCreationBootstrapAuthority.TryValidatePending(
            workspace,
            sourceResolver,
            out IReadOnlyList<string> bootstrapBlockers),
            string.Join(",", bootstrapBlockers));

        var foundation = new CharacterCreationFoundationService(
            store,
            queries,
            sourceResolver,
            new XmlLifeModulesCatalogService(
                Path.Combine(coreRoot, "Chummer", "data", "lifemodules.xml")),
            new UnavailableCharacterCreationFoundationApplyAuthority());
        CharacterCreationFoundationResult<CharacterCreationFoundationState> foundationLoaded =
            foundation.Load(new CharacterCreationFoundationLoadRequest(receipt.WorkspaceId));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, foundationLoaded.Outcome,
            string.Join(",", foundationLoaded.Blockers));
        CharacterCreationFoundationState foundationState = foundationLoaded.Value!;
        Assert.IsNotNull(foundationState);
        Assert.AreEqual(string.Empty, foundationState.CurrentMetatype);
        Assert.IsTrue(foundationState.MetatypeOptions.Count > 0);
        Assert.IsTrue(foundationState.MetatypeOptions.All(option =>
            option.SourceAnchorIds.Count > 0));
        Assert.IsTrue(CharacterCreationPrerequisiteAuthorityDigest.IsCanonical(
            foundationState.Binding.SourceDigest));
        CollectionAssert.DoesNotContain(
            foundationState.AuthorityBlockers.ToList(),
            CharacterCreationFoundationBlockers.CharacterDocumentInvalid);

        var prerequisites = new CharacterCreationPrerequisiteService(
            store,
            queries,
            sourceResolver);
        CharacterCreationFoundationResult<CharacterCreationPrerequisiteState> prerequisiteLoaded =
            prerequisites.Load(new CharacterCreationPrerequisiteLoadRequest(receipt.WorkspaceId));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, prerequisiteLoaded.Outcome,
            string.Join(",", prerequisiteLoaded.Blockers));
        CharacterCreationPrerequisiteState prerequisiteState = prerequisiteLoaded.Value!;
        Assert.IsNotNull(prerequisiteState);
        Assert.IsTrue(prerequisiteState.Authority.IsAuthoritative,
            string.Join(",", prerequisiteState.Authority.Blockers));
        Assert.IsTrue(prerequisiteState.Authority.Options.Count > 0);
        Assert.IsTrue(prerequisiteState.Authority.SourceAnchorIds.Count > 0);
        Assert.IsTrue(CharacterCreationPrerequisiteAuthorityDigest.EqualsFixedTime(
            prerequisiteState.Authority.AuthorityDigest,
            receipt.Binding.PrerequisiteAuthorityDigest));
        CollectionAssert.DoesNotContain(
            prerequisiteState.Blockers.ToList(),
            CharacterCreationPrerequisiteBlockers.CharacterDocumentInvalid);
    }

    [TestMethod]
    public void Activation_bundle_uses_one_source_context_no_store_read_and_matches_individual_loads()
    {
        string coreRoot = FindCoreRoot();
        var store = new CountingWorkspaceStore();
        var sourceResolver = new CountingSourceDataResolver(CreateSourceResolver(coreRoot));
        ICharacterFileQueries queries = CreateFileQueries();
        var lifeModules = new CountingLifeModulesCatalogService(
            new XmlLifeModulesCatalogService(
                Path.Combine(coreRoot, "Chummer", "data", "lifemodules.xml")));
        var applyAuthority = new UnavailableCharacterCreationFoundationApplyAuthority();
        var projector = new CharacterCreationBootstrapActivationProjector(
            store,
            queries,
            lifeModules,
            applyAuthority);
        CharacterCreationBootstrapService service = CreateService(
            store,
            sourceResolver,
            queries,
            projector);

        CharacterCreationBootstrapActivationAttempt attempt = service.CreateActivation(
            CanonicalRequest());

        Assert.AreEqual(CharacterCreationBootstrapOutcomes.Success, attempt.Outcome,
            string.Join(",", attempt.Blockers));
        Assert.IsNotNull(attempt.Receipt);
        Assert.IsNotNull(attempt.Bundle, string.Join(",", attempt.Blockers));
        Assert.AreEqual(1, sourceResolver.ContextCreateCount,
            "Atomic create and every frozen domain projection must share one source context.");
        Assert.AreEqual(1, sourceResolver.SourceProfileResolveCount);
        Assert.AreEqual(1, sourceResolver.MetatypeResolveCount);
        Assert.AreEqual(1, sourceResolver.PrerequisiteResolveCount);
        Assert.AreEqual(1, sourceResolver.QualitiesResolveCount);
        Assert.AreEqual(1, sourceResolver.MagicResolveCount);
        Assert.AreEqual(1, lifeModules.AuthorityReadCount);
        Assert.AreEqual(1, lifeModules.OptionProjectionCount);
        Assert.AreEqual(0, store.ReadCount,
            "The activation bundle must be projected from the atomic create result, not a store reread.");
        Assert.IsTrue(CharacterCreationBootstrapActivationIntegrity.IsValid(attempt.Bundle));
        Assert.IsTrue(service.TryValidateCurrent(attempt.Bundle, out IReadOnlyList<string> freshnessBlockers),
            string.Join(",", freshnessBlockers));
        Assert.HasCount(0, freshnessBlockers);
        Assert.AreEqual(2, sourceResolver.ContextCreateCount,
            "Consumer acceptance performs exactly one fresh source-context capture.");
        Assert.AreEqual(2, sourceResolver.SourceProfileResolveCount);
        Assert.AreEqual(2, sourceResolver.MetatypeResolveCount);
        Assert.AreEqual(2, sourceResolver.PrerequisiteResolveCount);
        Assert.AreEqual(2, sourceResolver.QualitiesResolveCount);
        Assert.AreEqual(2, sourceResolver.MagicResolveCount);
        Assert.AreEqual(2, lifeModules.AuthorityReadCount);
        Assert.AreEqual(2, lifeModules.OptionProjectionCount);
        Assert.AreEqual(1, store.ReadCount,
            "Consumer acceptance must independently bind to the current persisted workspace.");
        Assert.IsFalse(service.TryValidateCurrent(attempt.Bundle, out _),
            "Activation authority is one-shot and cannot be replayed.");
        Assert.AreEqual(1, store.ReadCount, "Rejected replay must not reread the store.");
        Assert.AreEqual(2, sourceResolver.ContextCreateCount,
            "A rejected replay must not touch source authority.");

        CharacterWorkspaceId workspaceId = attempt.Receipt.WorkspaceId;
        CharacterCreationInitialProjection aggregate = attempt.Bundle.InitialCreation;
        CharacterCreationFoundationResult<CharacterCreationFoundationState> foundation =
            new CharacterCreationFoundationService(
                store, queries, sourceResolver, lifeModules, applyAuthority)
            .Load(new(workspaceId));
        var prerequisites = new CharacterCreationPrerequisiteService(
            store, queries, sourceResolver);
        var attributes = new CharacterCreationAttributesService(store, sourceResolver);
        CharacterCreationFoundationResult<CharacterCreationPrerequisiteState> prerequisite =
            prerequisites.Load(new(workspaceId));
        CharacterCreationFoundationResult<CharacterCreationAttributesState> attribute =
            attributes.Load(new(workspaceId));
        CharacterCreationContactResult<CharacterCreationContactsState> contacts =
            new CharacterCreationContactsService(store).Load(new(workspaceId));
        CharacterCreationFoundationResult<CharacterCreationQualitiesState> qualities =
            new CharacterCreationQualitiesService(
                store, sourceResolver, prerequisites, attributes)
            .Load(new(workspaceId));
        CharacterCreationFoundationResult<CharacterCreationMagicResonanceState> magic =
            new CharacterCreationMagicResonanceService(store, sourceResolver)
            .Load(new(workspaceId));

        AssertJsonEqual(foundation, aggregate.Foundation);
        AssertJsonEqual(prerequisite, aggregate.Prerequisite);
        AssertJsonEqual(attribute, aggregate.Attributes);
        AssertJsonEqual(contacts, aggregate.Contacts);
        AssertJsonEqual(qualities, aggregate.Qualities);
        AssertJsonEqual(magic, aggregate.MagicResonance);
    }

    [TestMethod]
    [DataRow("deleted")]
    [DataRow("revision-advanced")]
    public void Consumer_freshness_rejects_persisted_workspace_changes_before_activation(string change)
    {
        string coreRoot = FindCoreRoot();
        string workspaceRoot = Path.Combine(Path.GetTempPath(), $"chummer-activation-freshness-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workspaceRoot);
        try
        {
            var store = new FileWorkspaceStore(workspaceRoot);
            var resolver = CreateSourceResolver(coreRoot);
            var queries = CreateFileQueries();
            var projector = new CharacterCreationBootstrapActivationProjector(store, queries,
                new XmlLifeModulesCatalogService(Path.Combine(coreRoot, "Chummer", "data", "lifemodules.xml")),
                new UnavailableCharacterCreationFoundationApplyAuthority());
            var service = CreateService(store, resolver, queries, projector);
            var attempt = service.CreateActivation(CanonicalRequest());
            Assert.IsNotNull(attempt.Bundle, string.Join(",", attempt.Blockers));
            var bundle = attempt.Bundle;
            var before = store.Get(bundle.Receipt.WorkspaceId).Value!;
            WorkspaceStoreMutationResult mutation = change switch
            {
                "deleted" => store.Delete(before.Id, before.ContentRevision),
                "revision-advanced" => store.ReplaceWorkspaceDocument(before.Id, before.ContentRevision, before.Document),
                _ => throw new ArgumentOutOfRangeException(nameof(change))
            };
            Assert.IsTrue(mutation.Success, mutation.Error);
            var changed = store.Get(before.Id);

            Assert.IsFalse(service.TryValidateCurrent(bundle, out var blockers),
                "An authentic unconsumed bundle cannot stand in for the current persisted workspace.");
            CollectionAssert.Contains(blockers.ToList(), CharacterCreationBootstrapBlockers.ActivationProjectionUnavailable);
            AssertJsonEqual(changed, store.Get(before.Id));
            Assert.IsFalse(service.TryValidateCurrent(bundle, out _),
                "Rejected activation must not restore a replayable pending capability.");
        }
        finally
        {
            // Only the newly allocated test store; never a supplied repository or user workspace.
            Directory.Delete(workspaceRoot, recursive: true);
        }
    }

    [TestMethod]
    [DataRow("unavailable")]
    [DataRow("missing-value")]
    [DataRow("other-workspace")]
    [DataRow("content-revision")]
    [DataRow("saved-revision")]
    [DataRow("xml-at-same-revision")]
    [DataRow("auxiliary-at-same-revision")]
    [DataRow("null-document")]
    [DataRow("null-state")]
    [DataRow("io-failure")]
    public void Consumer_freshness_rejects_untrusted_current_store_read(string fault)
    {
        string coreRoot = FindCoreRoot();
        var store = new CountingWorkspaceStore();
        var resolver = CreateSourceResolver(coreRoot);
        var queries = CreateFileQueries();
        var projector = new CharacterCreationBootstrapActivationProjector(store, queries,
            new XmlLifeModulesCatalogService(Path.Combine(coreRoot, "Chummer", "data", "lifemodules.xml")),
            new UnavailableCharacterCreationFoundationApplyAuthority());
        var service = CreateService(store, resolver, queries, projector);
        var bundle = service.CreateActivation(CanonicalRequest()).Bundle;
        Assert.IsNotNull(bundle);
        store.RewriteRead = read => fault switch
        {
            "unavailable" => new(WorkspaceOperationOutcome.Unavailable),
            "missing-value" => new(WorkspaceOperationOutcome.Success),
            "other-workspace" => read with { Value = read.Value! with { Id = new("other-workspace") } },
            "content-revision" => read with { Value = read.Value! with { ContentRevision = read.Value.ContentRevision + 1 } },
            "saved-revision" => read with { Value = read.Value! with { SavedRevision = read.Value.SavedRevision + 1 } },
            "xml-at-same-revision" => read with { Value = read.Value! with
                { Document = read.Value.Document with { State = read.Value.Document.State with
                    { Payload = read.Value.Document.Content + " " } } } },
            "auxiliary-at-same-revision" => read with { Value = read.Value! with
                { Document = read.Value.Document with { State = read.Value.Document.State with
                    { AuxiliaryState = WorkspaceDocumentAuxiliaryState.Empty } } } },
            "null-document" => read with { Value = read.Value! with { Document = null! } },
            "null-state" => read with { Value = read.Value! with
                { Document = read.Value.Document with { State = null! } } },
            "io-failure" => throw new IOException("Current store read unavailable."),
            _ => throw new ArgumentOutOfRangeException(nameof(fault))
        };

        Assert.IsFalse(service.TryValidateCurrent(bundle, out var blockers), fault);
        CollectionAssert.Contains(blockers.ToList(), CharacterCreationBootstrapBlockers.ActivationProjectionUnavailable);
        Assert.AreEqual(1, store.ReadCount);
        store.RewriteRead = null;
        Assert.IsFalse(service.TryValidateCurrent(bundle, out _),
            "A failed current-store check must consume the one-shot bundle, not admit a later replay.");
        Assert.AreEqual(1, store.ReadCount);
    }

    [TestMethod]
    public void Activation_bundle_rejects_recovery_source_and_aggregate_tampering()
    {
        string coreRoot = FindCoreRoot();
        var store = new CountingWorkspaceStore();
        var sourceResolver = new CountingSourceDataResolver(CreateSourceResolver(coreRoot));
        ICharacterFileQueries queries = CreateFileQueries();
        var projector = new CharacterCreationBootstrapActivationProjector(
            store,
            queries,
            new XmlLifeModulesCatalogService(
                Path.Combine(coreRoot, "Chummer", "data", "lifemodules.xml")),
            new UnavailableCharacterCreationFoundationApplyAuthority());
        CharacterCreationBootstrapService service = CreateService(
            store,
            sourceResolver,
            queries,
            projector);
        CharacterCreationBootstrapActivationAttempt attempt = service.CreateActivation(
            CanonicalRequest());
        CharacterCreationBootstrapActivationBundle bundle = attempt.Bundle!;
        Assert.IsTrue(CharacterCreationBootstrapActivationIntegrity.IsValid(bundle));

        Assert.IsFalse(CharacterCreationBootstrapActivationIntegrity.IsValid(bundle with
        {
            RecoveryBinding = bundle.RecoveryBinding with
            {
                AuxiliaryStateDigest = new string('0', 64)
            }
        }));
        Assert.IsFalse(CharacterCreationBootstrapActivationIntegrity.IsValid(bundle with
        {
            RecoveryBinding = bundle.RecoveryBinding with
            {
                RawProfileInputsDigest = "sha256:" + new string('0', 64)
            }
        }));
        Assert.IsFalse(CharacterCreationBootstrapActivationIntegrity.IsValid(bundle with
        {
            InitialCreation = bundle.InitialCreation with
            {
                Attributes = bundle.InitialCreation.Attributes with
                {
                    Blockers = ["invented-blocker"]
                }
            }
        }));

        CharacterCreationBootstrapActivationBundle overviewTamper = ResignActivation(bundle with
        {
            WorkspaceProjection = bundle.WorkspaceProjection with
            {
                Overview = bundle.WorkspaceProjection.Overview with
                {
                    Profile = bundle.WorkspaceProjection.Overview.Profile with
                    {
                        Name = "Re-signed forgery"
                    }
                }
            }
        });
        Assert.IsFalse(CharacterCreationBootstrapActivationIntegrity.IsValid(overviewTamper));

        CharacterCreationQualitiesState qualities = bundle.InitialCreation.Qualities.Value!;
        CharacterCreationBootstrapActivationBundle qualitiesAuthorityTamper = ResignActivation(
            bundle with
            {
                InitialCreation = bundle.InitialCreation with
                {
                    Qualities = bundle.InitialCreation.Qualities with
                    {
                        Value = qualities with
                        {
                            Authority = qualities.Authority with
                            {
                                SourceDigest = "sha256:" + new string('0', 64)
                            }
                        }
                    }
                }
            });
        Assert.IsFalse(CharacterCreationBootstrapActivationIntegrity.IsValid(
            qualitiesAuthorityTamper));

        Assert.IsTrue(qualities.Authority.Options.Count > 0);
        CharacterCreationQualityCatalogOption firstQuality = qualities.Authority.Options[0];
        CharacterCreationBootstrapActivationBundle qualityOptionTamper = ResignActivation(
            bundle with
            {
                InitialCreation = bundle.InitialCreation with
                {
                    Qualities = bundle.InitialCreation.Qualities with
                    {
                        Value = qualities with
                        {
                            Authority = qualities.Authority with
                            {
                                Options =
                                [
                                    firstQuality with { Name = firstQuality.Name + " forged" },
                                    .. qualities.Authority.Options.Skip(1)
                                ]
                            }
                        }
                    }
                }
            });
        Assert.IsFalse(CharacterCreationBootstrapActivationIntegrity.IsValid(qualityOptionTamper));

        CharacterCreationMagicResonanceState magic =
            bundle.InitialCreation.MagicResonance.Value!;
        Assert.IsTrue(magic.Authority.Traditions.Count > 0);
        CharacterCreationMagicResonanceCatalogOption firstTradition =
            magic.Authority.Traditions[0];
        CharacterCreationBootstrapActivationBundle magicCatalogTamper = ResignActivation(
            bundle with
            {
                InitialCreation = bundle.InitialCreation with
                {
                    MagicResonance = bundle.InitialCreation.MagicResonance with
                    {
                        Value = magic with
                        {
                            Authority = magic.Authority with
                            {
                                Traditions =
                                [
                                    firstTradition with { Name = firstTradition.Name + " forged" },
                                    .. magic.Authority.Traditions.Skip(1)
                                ]
                            }
                        }
                    }
                }
            });
        Assert.IsFalse(CharacterCreationBootstrapActivationIntegrity.IsValid(magicCatalogTamper));

        CharacterCreationAttributesState attributes = bundle.InitialCreation.Attributes.Value!;
        Assert.IsFalse(CharacterCreationBootstrapActivationIntegrity.IsValid(ResignActivation(
            bundle with
            {
                InitialCreation = bundle.InitialCreation with
                {
                    Attributes = bundle.InitialCreation.Attributes with
                    {
                        Value = attributes with { CanEdit = !attributes.CanEdit }
                    }
                }
            })));
        CharacterCreationFoundationState foundation = bundle.InitialCreation.Foundation.Value!;
        Assert.IsFalse(CharacterCreationBootstrapActivationIntegrity.IsValid(ResignActivation(
            bundle with
            {
                InitialCreation = bundle.InitialCreation with
                {
                    Foundation = bundle.InitialCreation.Foundation with
                    {
                        Value = foundation with
                        {
                            ResumeStatus = CharacterCreationFoundationResumeStatuses.PendingDraft
                        }
                    }
                }
            })));
        Assert.IsFalse(CharacterCreationBootstrapActivationIntegrity.IsValid(ResignActivation(
            bundle with
            {
                InitialCreation = bundle.InitialCreation with
                {
                    Foundation = bundle.InitialCreation.Foundation with
                    {
                        Outcome = CharacterCreationFoundationOutcomes.Invalid
                    }
                }
            })));

        CharacterCreationBootstrapBinding documentBinding =
            bundle.WorkspaceProjection.Workspace.Document.AuxiliaryState
                .CharacterCreationBootstrapBinding!;
        CharacterCreationBootstrapBinding hostileDocumentBinding = ResignBinding(
            documentBinding with
            {
                RawProfileInputsDigest = "sha256:" + new string('0', 64)
            });
        WorkspaceDocument hostileDocument = bundle.WorkspaceProjection.Workspace.Document with
        {
            State = bundle.WorkspaceProjection.Workspace.Document.State with
            {
                AuxiliaryState = new WorkspaceDocumentAuxiliaryState(
                    CharacterCreationBootstrapBinding: hostileDocumentBinding)
            }
        };
        Assert.IsFalse(CharacterCreationBootstrapActivationIntegrity.IsValid(ResignActivation(
            bundle with
            {
                WorkspaceProjection = bundle.WorkspaceProjection with
                {
                    Workspace = bundle.WorkspaceProjection.Workspace with
                    {
                        Document = hostileDocument
                    }
                }
            })));
    }

    [TestMethod]
    public void Activation_integrity_returns_false_for_hostile_null_graphs()
    {
        string coreRoot = FindCoreRoot();
        var store = new CountingWorkspaceStore();
        ICharacterFileQueries queries = CreateFileQueries();
        var projector = new CharacterCreationBootstrapActivationProjector(
            store,
            queries,
            new XmlLifeModulesCatalogService(
                Path.Combine(coreRoot, "Chummer", "data", "lifemodules.xml")),
            new UnavailableCharacterCreationFoundationApplyAuthority());
        CharacterCreationBootstrapActivationBundle bundle = CreateService(
            store,
            CreateSourceResolver(coreRoot),
            queries,
            projector).CreateActivation(CanonicalRequest()).Bundle!;

        Assert.IsFalse(CharacterCreationBootstrapActivationIntegrity.IsValid(
            bundle with { WorkspaceProjection = null! }));
        Assert.IsFalse(CharacterCreationBootstrapActivationIntegrity.IsValid(
            bundle with { RecoveryBinding = null! }));
        Assert.IsFalse(CharacterCreationBootstrapActivationIntegrity.IsValid(
            bundle with { InitialCreation = null! }));
        Assert.IsFalse(CharacterCreationBootstrapActivationIntegrity.IsValid(
            bundle with
            {
                InitialCreation = bundle.InitialCreation with { SourceAuthority = null! }
            }));
        Assert.IsFalse(CharacterCreationBootstrapActivationIntegrity.IsValid(
            bundle with
            {
                InitialCreation = bundle.InitialCreation with { Qualities = null! }
            }));
    }

    [TestMethod]
    public void Dependency_injection_aliases_legacy_and_activation_to_the_same_singleton()
    {
        string coreRoot = FindCoreRoot();
        var services = new ServiceCollection();
        services.AddChummerHeadlessCore(coreRoot, coreRoot);
        using ServiceProvider provider = services.BuildServiceProvider();

        ICharacterCreationBootstrapService legacy =
            provider.GetRequiredService<ICharacterCreationBootstrapService>();
        ICharacterCreationBootstrapActivationService activation =
            provider.GetRequiredService<ICharacterCreationBootstrapActivationService>();

        Assert.AreSame((object)legacy, activation);
    }

    [DataTestMethod]
    [DataRow("settings")]
    [DataRow("metatypes")]
    [DataRow("priorities")]
    [DataRow("skills")]
    [DataRow("qualities")]
    [DataRow("traditions")]
    [DataRow("streams")]
    [DataRow("powers")]
    [DataRow("spells")]
    [DataRow("complexforms")]
    public void Consumer_freshness_rejects_each_captured_source_domain_drift(string domain)
    {
        string coreRoot = FindCoreRoot();
        var store = new CountingWorkspaceStore();
        var sourceResolver = new ConsumerDriftSourceDataResolver(
            CreateSourceResolver(coreRoot),
            domain);
        ICharacterFileQueries queries = CreateFileQueries();
        var projector = new CharacterCreationBootstrapActivationProjector(
            store,
            queries,
            new XmlLifeModulesCatalogService(
                Path.Combine(coreRoot, "Chummer", "data", "lifemodules.xml")),
            new UnavailableCharacterCreationFoundationApplyAuthority());
        CharacterCreationBootstrapService service = CreateService(
            store,
            sourceResolver,
            queries,
            projector);
        CharacterCreationBootstrapActivationBundle bundle = service
            .CreateActivation(CanonicalRequest()).Bundle!;
        Assert.IsNotNull(bundle);

        Assert.IsFalse(service.TryValidateCurrent(bundle, out IReadOnlyList<string> blockers));
        CollectionAssert.Contains(
            blockers.ToList(),
            CharacterCreationBootstrapBlockers.ActivationProjectionUnavailable);
        Assert.AreEqual(2, sourceResolver.ContextCreateCount);
    }

    [TestMethod]
    public void Consumer_freshness_rejects_life_modules_source_drift()
    {
        string coreRoot = FindCoreRoot();
        var store = new CountingWorkspaceStore();
        ICharacterFileQueries queries = CreateFileQueries();
        var lifeModules = new DriftingLifeModulesCatalogService(
            new XmlLifeModulesCatalogService(
                Path.Combine(coreRoot, "Chummer", "data", "lifemodules.xml")));
        var projector = new CharacterCreationBootstrapActivationProjector(
            store,
            queries,
            lifeModules,
            new UnavailableCharacterCreationFoundationApplyAuthority());
        CharacterCreationBootstrapService service = CreateService(
            store,
            CreateSourceResolver(coreRoot),
            queries,
            projector);
        CharacterCreationBootstrapActivationBundle bundle = service
            .CreateActivation(CanonicalRequest()).Bundle!;
        Assert.IsNotNull(bundle);

        lifeModules.Drifted = true;
        Assert.IsFalse(service.TryValidateCurrent(bundle, out _));
    }

    [TestMethod]
    public void Consumer_freshness_fails_closed_when_source_authority_drifts_after_creation()
    {
        string coreRoot = FindCoreRoot();
        var store = new CountingWorkspaceStore();
        var sourceResolver = new DriftingSourceDataResolver(CreateSourceResolver(coreRoot));
        ICharacterFileQueries queries = CreateFileQueries();
        var projector = new SourceDriftingProjector(
            new CharacterCreationBootstrapActivationProjector(
                store,
                queries,
                new XmlLifeModulesCatalogService(
                    Path.Combine(coreRoot, "Chummer", "data", "lifemodules.xml")),
                new UnavailableCharacterCreationFoundationApplyAuthority()));

        CharacterCreationBootstrapService service = CreateService(
            store,
            sourceResolver,
            queries,
            projector);
        CharacterCreationBootstrapActivationAttempt attempt = service.CreateActivation(
            CanonicalRequest());

        Assert.AreEqual(CharacterCreationBootstrapOutcomes.Success, attempt.Outcome);
        Assert.IsNotNull(attempt.Receipt,
            "The already-committed workspace receipt must remain available for full fallback loading.");
        Assert.IsNotNull(attempt.Bundle,
            "Projection must use only the immutable initial typed-authority capture.");
        Assert.AreEqual(1, sourceResolver.ContextCreateCount);
        Assert.IsFalse(service.TryValidateCurrent(attempt.Bundle, out _));
        Assert.AreEqual(2, sourceResolver.ContextCreateCount);
        Assert.HasCount(1, store.List());
    }

    [DataTestMethod]
    [DataRow(CharacterCreationBuildMethods.SumToTen, CanonicalSumToTenSettingsId)]
    [DataRow(CharacterCreationBuildMethods.Karma, CanonicalKarmaSettingsId)]
    [DataRow(CharacterCreationBuildMethods.LifeModules, CanonicalLifeModulesSettingsId)]
    public void Non_priority_activation_is_an_explicit_committed_reload_fallback(
        string buildMethod,
        string settingsProfileId)
    {
        string coreRoot = FindCoreRoot();
        var store = new CountingWorkspaceStore();
        ICharacterFileQueries queries = CreateFileQueries();
        var sourceResolver = new CountingSourceDataResolver(CreateSourceResolver(coreRoot));
        var projector = new CharacterCreationBootstrapActivationProjector(
            store,
            queries,
            new XmlLifeModulesCatalogService(
                Path.Combine(coreRoot, "Chummer", "data", "lifemodules.xml")),
            new UnavailableCharacterCreationFoundationApplyAuthority());

        CharacterCreationBootstrapActivationAttempt attempt = CreateService(
            store,
            sourceResolver,
            queries,
            projector).CreateActivation(CanonicalRequest() with
            {
                BuildMethod = buildMethod,
                SettingsProfileId = settingsProfileId
            });

        Assert.AreEqual(CharacterCreationBootstrapOutcomes.Success, attempt.Outcome);
        Assert.IsNotNull(attempt.Receipt);
        Assert.IsNull(attempt.Bundle);
        Assert.IsTrue(attempt.CreatedRequiresReload);
        CollectionAssert.Contains(
            attempt.Blockers.ToList(),
            CharacterCreationBootstrapBlockers.ActivationProjectionUnavailable);
        Assert.HasCount(1, store.List());

        CharacterWorkspaceId workspaceId = attempt.Receipt.WorkspaceId;
        var lifeModules = new XmlLifeModulesCatalogService(
            Path.Combine(coreRoot, "Chummer", "data", "lifemodules.xml"));
        var applyAuthority = new UnavailableCharacterCreationFoundationApplyAuthority();
        CharacterCreationFoundationResult<CharacterCreationFoundationState> foundation =
            new CharacterCreationFoundationService(
                store,
                queries,
                sourceResolver,
                lifeModules,
                applyAuthority).Load(new(workspaceId));
        var prerequisites = new CharacterCreationPrerequisiteService(
            store,
            queries,
            sourceResolver);
        var attributes = new CharacterCreationAttributesService(store, sourceResolver);
        CharacterCreationFoundationResult<CharacterCreationPrerequisiteState> prerequisite =
            prerequisites.Load(new(workspaceId));
        CharacterCreationFoundationResult<CharacterCreationAttributesState> attribute =
            attributes.Load(new(workspaceId));
        CharacterCreationContactResult<CharacterCreationContactsState> contacts =
            new CharacterCreationContactsService(store).Load(new(workspaceId));
        CharacterCreationFoundationResult<CharacterCreationQualitiesState> qualities =
            new CharacterCreationQualitiesService(
                store,
                sourceResolver,
                prerequisites,
                attributes).Load(new(workspaceId));
        CharacterCreationFoundationResult<CharacterCreationMagicResonanceState> magic =
            new CharacterCreationMagicResonanceService(store, sourceResolver)
                .Load(new(workspaceId));

        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, foundation.Outcome);
        Assert.IsNotNull(foundation.Value);
        Assert.AreEqual(buildMethod, foundation.Value.BuildMethod);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, prerequisite.Outcome);
        Assert.IsNotNull(prerequisite.Value);
        Assert.AreEqual(buildMethod, prerequisite.Value.BuildMethod);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, attribute.Outcome);
        Assert.IsNotNull(attribute.Value);
        Assert.AreEqual(CharacterCreationContactOutcomes.Available, contacts.Outcome);
        Assert.IsNotNull(contacts.Value);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, qualities.Outcome);
        Assert.IsNotNull(qualities.Value);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, magic.Outcome);
        Assert.IsNotNull(magic.Value);
    }

    [TestMethod]
    public void Request_tuple_is_exact_and_invalid_variants_never_mutate_the_store()
    {
        string coreRoot = FindCoreRoot();
        var store = new InMemoryWorkspaceStore();
        CharacterCreationBootstrapService service = CreateService(
            store,
            CreateSourceResolver(coreRoot),
            CreateFileQueries());
        CharacterCreationBootstrapRequest canonical = CanonicalRequest();
        CharacterCreationBootstrapRequest[] hostile =
        [
            canonical with { Schema = "unknown" },
            canonical with { Stage = "complete" },
            canonical with { RulesetId = RulesetDefaults.Sr6 },
            canonical with { BuildMethod = "priority" },
            canonical with { BuildMethod = string.Empty },
            canonical with { SettingsProfileId = Guid.Empty.ToString("D") },
            canonical with { SettingsProfileId = CanonicalPrioritySettingsId.ToUpperInvariant() },
            canonical with { Name = string.Empty },
            canonical with { Alias = string.Empty }
        ];

        foreach (CharacterCreationBootstrapRequest request in hostile)
        {
            CharacterCreationBootstrapResult<CharacterCreationBootstrapReceipt> result =
                service.Create(request);
            Assert.AreEqual(CharacterCreationBootstrapOutcomes.Invalid, result.Outcome);
            Assert.IsNull(result.Value);
            Assert.IsTrue(result.Blockers.Count > 0);
        }

        Assert.HasCount(0, store.List());
    }

    [TestMethod]
    public void Every_noncanonical_builtin_sr5_profile_is_rejected_before_resolution()
    {
        string coreRoot = FindCoreRoot();
        var store = new InMemoryWorkspaceStore();
        CharacterCreationBootstrapService service = CreateService(
            store,
            CreateSourceResolver(coreRoot),
            CreateFileQueries());
        XDocument settings = XDocument.Load(
            Path.Combine(coreRoot, "Chummer", "data", "settings.xml"),
            LoadOptions.None);
        XElement[] noncanonical = settings.Root!
            .Element("settings")!
            .Elements("setting")
            .Where(setting =>
            {
                string method = setting.Element("buildmethod")?.Value.Trim() ?? string.Empty;
                string id = setting.Element("id")?.Value.Trim() ?? string.Empty;
                return CharacterCreationBuildMethods.IsSupported(method)
                       && !CharacterCreationBootstrapProfiles.IsExactCanonicalTuple(method, id);
            })
            .ToArray();
        Assert.IsTrue(noncanonical.Length > 0);

        foreach (XElement setting in noncanonical)
        {
            string method = setting.Element("buildmethod")!.Value.Trim();
            string id = setting.Element("id")!.Value.Trim();
            CharacterCreationBootstrapResult<CharacterCreationBootstrapReceipt> result =
                service.Create(CanonicalRequest() with
                {
                    BuildMethod = method,
                    SettingsProfileId = id
                });
            Assert.AreEqual(
                CharacterCreationBootstrapOutcomes.Invalid,
                result.Outcome,
                $"Noncanonical profile {id} ({method}) was accepted.");
            CollectionAssert.Contains(
                result.Blockers.ToList(),
                CharacterCreationBootstrapBlockers.SettingsProfileInvalid);
        }

        Assert.HasCount(0, store.List());
    }

    [TestMethod]
    public void Every_canonical_method_profile_cross_pair_fails_closed()
    {
        string coreRoot = FindCoreRoot();
        var store = new InMemoryWorkspaceStore();
        CharacterCreationBootstrapService service = CreateService(
            store,
            CreateSourceResolver(coreRoot),
            CreateFileQueries());
        (string Method, string Profile)[] tuples =
        [
            (CharacterCreationBuildMethods.Priority, CanonicalPrioritySettingsId),
            (CharacterCreationBuildMethods.SumToTen, CanonicalSumToTenSettingsId),
            (CharacterCreationBuildMethods.Karma, CanonicalKarmaSettingsId),
            (CharacterCreationBuildMethods.LifeModules, CanonicalLifeModulesSettingsId)
        ];

        foreach ((string Method, string Profile) left in tuples)
        foreach ((string Method, string Profile) right in tuples)
        {
            string method = left.Method;
            string profile = right.Profile;
            if (CharacterCreationBootstrapProfiles.IsExactCanonicalTuple(method, profile))
                continue;

            CharacterCreationBootstrapResult<CharacterCreationBootstrapReceipt> result =
                service.Create(CanonicalRequest() with
                {
                    BuildMethod = method,
                    SettingsProfileId = profile
                });
            Assert.AreEqual(CharacterCreationBootstrapOutcomes.Invalid, result.Outcome);
            CollectionAssert.Contains(
                result.Blockers.ToList(),
                CharacterCreationBootstrapBlockers.SettingsProfileInvalid);
        }

        Assert.HasCount(0, store.List());
    }

    [DataTestMethod]
    [DataRow(CharacterCreationBuildMethods.Priority, CanonicalPrioritySettingsId, true)]
    [DataRow(CharacterCreationBuildMethods.SumToTen, CanonicalSumToTenSettingsId, true)]
    [DataRow(CharacterCreationBuildMethods.Karma, CanonicalKarmaSettingsId, false)]
    [DataRow(CharacterCreationBuildMethods.LifeModules, CanonicalLifeModulesSettingsId, false)]
    public void Canonical_sr5_profiles_bind_only_to_their_exact_build_method(
        string buildMethod,
        string settingsProfileId,
        bool hasPrerequisiteAuthority)
    {
        string coreRoot = FindCoreRoot();
        var store = new InMemoryWorkspaceStore();
        CharacterCreationBootstrapService service = CreateService(
            store,
            CreateSourceResolver(coreRoot),
            CreateFileQueries());
        CharacterCreationBootstrapRequest request = CanonicalRequest() with
        {
            BuildMethod = buildMethod,
            SettingsProfileId = settingsProfileId
        };

        CharacterCreationBootstrapResult<CharacterCreationBootstrapReceipt> result =
            service.Create(request);

        Assert.AreEqual(CharacterCreationBootstrapOutcomes.Success, result.Outcome,
            string.Join(",", result.Blockers));
        Assert.AreEqual(buildMethod, result.Value!.Binding.BuildMethod);
        Assert.AreEqual(settingsProfileId, result.Value.Binding.SettingsProfileId);
        Assert.AreEqual(
            hasPrerequisiteAuthority,
            CharacterCreationPrerequisiteAuthorityDigest.IsCanonical(
                result.Value.Binding.PrerequisiteAuthorityDigest));
    }

    [TestMethod]
    public void Marker_without_atomic_binding_is_an_ordinary_invalid_import()
    {
        string coreRoot = FindCoreRoot();
        var store = new InMemoryWorkspaceStore();
        FileSystemCharacterSourceDataResolver resolver = CreateSourceResolver(coreRoot);
        ICharacterFileQueries queries = CreateFileQueries();
        var codec = new Sr5WorkspaceCodec(
            queries,
            new XmlCharacterSectionQueries(new CharacterSectionService(resolver)),
            new XmlCharacterMetadataCommands(new CharacterFileService()));
        var workspaces = new WorkspaceService(
            store,
            new RulesetWorkspaceCodecResolver([codec]),
            new WorkspaceImportRulesetDetector());
        InvalidOperationException importFailure = Assert.ThrowsExactly<InvalidOperationException>(
            () => workspaces.Import(new WorkspaceImportDocument(
                MinimalMarkerXml(),
                RulesetDefaults.Sr5,
                WorkspaceDocumentFormat.NativeXml)));
        StringAssert.Contains(importFailure.Message, "typed, resolver-bound atomic creation service");
        Assert.HasCount(0, store.List());

        CharacterWorkspaceId id = new("ordinary-marker-import");
        Assert.IsTrue(store.CreateWorkspaceDocument(
            id,
            new WorkspaceDocument(MinimalMarkerXml(), RulesetDefaults.Sr5)).Success);

        var foundation = new CharacterCreationFoundationService(
            store,
            queries,
            resolver,
            new XmlLifeModulesCatalogService(
                Path.Combine(coreRoot, "Chummer", "data", "lifemodules.xml")),
            new UnavailableCharacterCreationFoundationApplyAuthority());
        CharacterCreationFoundationResult<CharacterCreationFoundationState> foundationLoaded =
            foundation.Load(new CharacterCreationFoundationLoadRequest(id));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Invalid, foundationLoaded.Outcome);
        CollectionAssert.Contains(
            foundationLoaded.Blockers.ToList(),
            CharacterCreationFoundationBlockers.CharacterDocumentInvalid);

        var prerequisites = new CharacterCreationPrerequisiteService(store, queries, resolver);
        CharacterCreationFoundationResult<CharacterCreationPrerequisiteState> prerequisiteLoaded =
            prerequisites.Load(new CharacterCreationPrerequisiteLoadRequest(id));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Invalid, prerequisiteLoaded.Outcome);
        CollectionAssert.Contains(
            prerequisiteLoaded.Blockers.ToList(),
            CharacterCreationPrerequisiteBlockers.CharacterDocumentInvalid);
    }

    [TestMethod]
    public void Marker_cardinality_selection_and_binding_tamper_fail_closed()
    {
        string coreRoot = FindCoreRoot();
        var store = new InMemoryWorkspaceStore();
        FileSystemCharacterSourceDataResolver resolver = CreateSourceResolver(coreRoot);
        CharacterCreationBootstrapResult<CharacterCreationBootstrapReceipt> created =
            CreateService(store, resolver, CreateFileQueries()).Create(CanonicalRequest());
        CharacterCreationBootstrapReceipt receipt = created.Value!;
        WorkspaceStoredDocument workspace = store.Get(receipt.WorkspaceId).Value!;
        XDocument canonical = XDocument.Parse(workspace.Document.Content);

        XDocument duplicate = XDocument.Parse(workspace.Document.Content);
        duplicate.Root!.Add(new XElement(
            duplicate.Root.Element(CharacterCreationBootstrapXml.MarkerElement)!));
        AssertPrepareBlocked(
            receipt.WorkspaceId,
            workspace.Document with
            {
                State = workspace.Document.State with
                {
                    Payload = duplicate.ToString(SaveOptions.DisableFormatting),
                    AuxiliaryState = WorkspaceDocumentAuxiliaryState.Empty
                }
            },
            resolver,
            CharacterCreationBootstrapBlockers.MarkerDuplicate);

        XDocument wrongMarker = XDocument.Parse(workspace.Document.Content);
        wrongMarker.Root!.Element(CharacterCreationBootstrapXml.MarkerElement)!
            .Element(CharacterCreationBootstrapXml.SchemaElement)!.Value = "unknown-marker";
        AssertPrepareBlocked(
            receipt.WorkspaceId,
            WithoutBinding(workspace.Document, wrongMarker),
            resolver,
            CharacterCreationBootstrapBlockers.MarkerInvalid);

        XDocument duplicateMarkerField = XDocument.Parse(workspace.Document.Content);
        duplicateMarkerField.Root!.Element(CharacterCreationBootstrapXml.MarkerElement)!.Add(
            new XElement(
                CharacterCreationBootstrapXml.SchemaElement,
                CharacterCreationBootstrapSchemas.MarkerV1));
        AssertPrepareBlocked(
            receipt.WorkspaceId,
            WithoutBinding(workspace.Document, duplicateMarkerField),
            resolver,
            CharacterCreationBootstrapBlockers.MarkerInvalid);

        XDocument selected = XDocument.Parse(workspace.Document.Content);
        selected.Root!.AddFirst(new XElement("metatype", "Human"));
        AssertPrepareBlocked(
            receipt.WorkspaceId,
            workspace.Document with
            {
                State = workspace.Document.State with
                {
                    Payload = selected.ToString(SaveOptions.DisableFormatting),
                    AuxiliaryState = WorkspaceDocumentAuxiliaryState.Empty
                }
            },
            resolver,
            CharacterCreationBootstrapBlockers.MetatypeAlreadySelected);

        XDocument emptySelection = XDocument.Parse(workspace.Document.Content);
        emptySelection.Root!.AddFirst(new XElement("metatype"));
        Assert.IsTrue(CharacterCreationBootstrapAuthority.TryPrepareBinding(
            receipt.WorkspaceId,
            WithoutBinding(workspace.Document, emptySelection),
            resolver,
            out CharacterCreationBootstrapBinding emptyMetatypeBinding,
            out _,
            out IReadOnlyList<string> emptyMetatypeBlockers),
            string.Join(",", emptyMetatypeBlockers));
        Assert.IsTrue(CharacterCreationBootstrapBindingDigest.IsValid(emptyMetatypeBinding));

        XDocument createdCharacter = XDocument.Parse(workspace.Document.Content);
        createdCharacter.Root!.Element("created")!.Value = "True";
        AssertPrepareBlocked(
            receipt.WorkspaceId,
            WithoutBinding(workspace.Document, createdCharacter),
            resolver,
            CharacterCreationBootstrapBlockers.CharacterAlreadyCreated);

        XDocument missingBuild = XDocument.Parse(workspace.Document.Content);
        missingBuild.Root!.Element("buildmethod")!.Remove();
        AssertPrepareBlocked(
            receipt.WorkspaceId,
            WithoutBinding(workspace.Document, missingBuild),
            resolver,
            CharacterCreationBootstrapBlockers.CharacterDocumentInvalid);

        XDocument duplicateSettings = XDocument.Parse(workspace.Document.Content);
        duplicateSettings.Root!.Add(new XElement("settings", CanonicalPrioritySettingsId));
        AssertPrepareBlocked(
            receipt.WorkspaceId,
            WithoutBinding(workspace.Document, duplicateSettings),
            resolver,
            CharacterCreationBootstrapBlockers.CharacterDocumentInvalid);

        XDocument noncanonicalProfile = XDocument.Parse(workspace.Document.Content);
        noncanonicalProfile.Root!.Element("settings")!.Value =
            "507eef8e-eba8-41ea-84c4-4282258fe669";
        AssertPrepareBlocked(
            receipt.WorkspaceId,
            WithoutBinding(workspace.Document, noncanonicalProfile),
            resolver,
            CharacterCreationBootstrapBlockers.SettingsProfileInvalid);

        CharacterCreationBootstrapBinding badStage = receipt.Binding with
        {
            Stage = "completed",
            BindingDigest = string.Empty
        };
        badStage = badStage with
        {
            BindingDigest = CharacterCreationBootstrapBindingDigest.Compute(badStage)
        };
        WorkspaceDocument badBindingDocument = workspace.Document with
        {
            State = workspace.Document.State with
            {
                AuxiliaryState = new WorkspaceDocumentAuxiliaryState(
                    CharacterCreationBootstrapBinding: badStage)
            }
        };
        var badBindingWorkspace = workspace with { Document = badBindingDocument };
        Assert.IsFalse(CharacterCreationBootstrapAuthority.TryValidatePending(
            badBindingWorkspace,
            resolver,
            out IReadOnlyList<string> bindingBlockers));
        CollectionAssert.Contains(
            bindingBlockers.ToList(),
            CharacterCreationBootstrapBlockers.BindingInvalid);
        Assert.IsTrue(canonical.Root!.Element("metatype") is null);
    }

    [TestMethod]
    public void Revision_and_complete_anchor_tamper_fail_even_after_structural_redigest()
    {
        string coreRoot = FindCoreRoot();
        var store = new InMemoryWorkspaceStore();
        FileSystemCharacterSourceDataResolver resolver = CreateSourceResolver(coreRoot);
        CharacterCreationBootstrapReceipt receipt = CreateService(
                store,
                resolver,
                CreateFileQueries())
            .Create(CanonicalRequest())
            .Value!;
        WorkspaceStoredDocument workspace = store.Get(receipt.WorkspaceId).Value!;
        Assert.IsTrue(CharacterCreationBootstrapReceiptDigest.IsValid(receipt));

        CharacterCreationBootstrapReceipt receiptRevisionTamper = ResignReceipt(
            receipt with { ContentRevision = receipt.ContentRevision + 1 });
        Assert.IsFalse(CharacterCreationBootstrapReceiptDigest.IsValid(receiptRevisionTamper));

        CharacterCreationBootstrapBinding bindingRevisionTamper = ResignBinding(
            receipt.Binding with
            {
                InitialContentRevision =
                    CharacterCreationBootstrapRevisions.InitialContentRevision + 1
            });
        CharacterCreationBootstrapReceipt fullyRedigestedRevisionTamper = ResignReceipt(
            receipt with
            {
                ContentRevision = bindingRevisionTamper.InitialContentRevision,
                Binding = bindingRevisionTamper
            });
        Assert.IsFalse(CharacterCreationBootstrapBindingDigest.IsValid(bindingRevisionTamper));
        Assert.IsFalse(CharacterCreationBootstrapReceiptDigest.IsValid(
            fullyRedigestedRevisionTamper));
        Assert.IsFalse(CharacterCreationBootstrapStoreIntegrity.IsValidBinding(
            receipt.WorkspaceId,
            bindingRevisionTamper));
        WorkspaceDocument revisionTamperDocument = workspace.Document with
        {
            State = workspace.Document.State with
            {
                AuxiliaryState = new WorkspaceDocumentAuxiliaryState(
                    CharacterCreationBootstrapBinding: bindingRevisionTamper)
            }
        };
        Assert.IsFalse(CharacterCreationBootstrapStoreIntegrity.IsValidInitialState(
            receipt.WorkspaceId,
            revisionTamperDocument));
        var hostileRevisionStore = new InMemoryWorkspaceStore();
        Assert.IsFalse(((ICharacterCreationBootstrapAtomicCreateCapability)hostileRevisionStore)
            .CreateCharacterCreationBootstrapWorkspaceDocument(
                receipt.WorkspaceId,
                revisionTamperDocument)
            .Success);

        CharacterCreationBootstrapBinding savedRevisionTamper = ResignBinding(
            receipt.Binding with
            {
                InitialSavedRevision =
                    CharacterCreationBootstrapRevisions.InitialSavedRevision + 1
            });
        CharacterCreationBootstrapReceipt fullyRedigestedSavedRevisionTamper = ResignReceipt(
            receipt with
            {
                SavedRevision = savedRevisionTamper.InitialSavedRevision,
                Binding = savedRevisionTamper
            });
        Assert.IsFalse(CharacterCreationBootstrapBindingDigest.IsValid(savedRevisionTamper));
        Assert.IsFalse(CharacterCreationBootstrapReceiptDigest.IsValid(
            fullyRedigestedSavedRevisionTamper));

        CharacterCreationBootstrapBinding crossPairBinding = ResignBinding(
            receipt.Binding with { BuildMethod = CharacterCreationBuildMethods.SumToTen });
        WorkspaceDocument crossPairDocument = workspace.Document with
        {
            State = workspace.Document.State with
            {
                AuxiliaryState = new WorkspaceDocumentAuxiliaryState(
                    CharacterCreationBootstrapBinding: crossPairBinding)
            }
        };
        Assert.IsFalse(CharacterCreationBootstrapBindingDigest.IsValid(crossPairBinding));
        Assert.IsFalse(CharacterCreationBootstrapStoreIntegrity.IsValidInitialState(
            receipt.WorkspaceId,
            crossPairDocument));

        string[] missingAnchorSet = receipt.Binding.SourceAnchorIds
            .Where(anchor => !string.Equals(anchor, "skills.xml", StringComparison.Ordinal))
            .ToArray();
        CharacterCreationBootstrapBinding missingAnchorBinding = ResignBinding(
            receipt.Binding with { SourceAnchorIds = missingAnchorSet });
        CharacterCreationBootstrapReceipt fullyRedigestedMissingAnchor = ResignReceipt(
            receipt with
            {
                Binding = missingAnchorBinding,
                SourceAnchorIds = missingAnchorSet
            });
        Assert.IsFalse(CharacterCreationBootstrapBindingDigest.IsValid(missingAnchorBinding));
        Assert.IsFalse(CharacterCreationBootstrapReceiptDigest.IsValid(
            fullyRedigestedMissingAnchor));

        string[] extraAnchorSet = receipt.Binding.SourceAnchorIds
            .Append("unknown.xml")
            .OrderBy(anchor => anchor, StringComparer.Ordinal)
            .ToArray();
        CharacterCreationBootstrapBinding extraAnchorBinding = ResignBinding(
            receipt.Binding with { SourceAnchorIds = extraAnchorSet });
        CharacterCreationBootstrapReceipt fullyRedigestedExtraAnchor = ResignReceipt(
            receipt with
            {
                Binding = extraAnchorBinding,
                SourceAnchorIds = extraAnchorSet
            });
        Assert.IsFalse(CharacterCreationBootstrapBindingDigest.IsValid(extraAnchorBinding));
        Assert.IsFalse(CharacterCreationBootstrapReceiptDigest.IsValid(
            fullyRedigestedExtraAnchor));

        string[] reorderedAnchorSet = receipt.Binding.SourceAnchorIds.Reverse().ToArray();
        CharacterCreationBootstrapBinding reorderedAnchorBinding = ResignBinding(
            receipt.Binding with { SourceAnchorIds = reorderedAnchorSet });
        CharacterCreationBootstrapReceipt fullyRedigestedReorderedAnchors = ResignReceipt(
            receipt with
            {
                Binding = reorderedAnchorBinding,
                SourceAnchorIds = reorderedAnchorSet
            });
        Assert.IsFalse(CharacterCreationBootstrapBindingDigest.IsValid(reorderedAnchorBinding));
        Assert.IsFalse(CharacterCreationBootstrapReceiptDigest.IsValid(
            fullyRedigestedReorderedAnchors));

        WorkspaceDocument extraAnchorDocument = workspace.Document with
        {
            State = workspace.Document.State with
            {
                AuxiliaryState = new WorkspaceDocumentAuxiliaryState(
                    CharacterCreationBootstrapBinding: extraAnchorBinding)
            }
        };
        Assert.IsFalse(CharacterCreationBootstrapStoreIntegrity.IsValidInitialState(
            receipt.WorkspaceId,
            extraAnchorDocument));
        var hostileAnchorStore = new InMemoryWorkspaceStore();
        Assert.IsFalse(((ICharacterCreationBootstrapAtomicCreateCapability)hostileAnchorStore)
            .CreateCharacterCreationBootstrapWorkspaceDocument(
                receipt.WorkspaceId,
                extraAnchorDocument)
            .Success);
        Assert.IsFalse(CharacterCreationBootstrapAuthority.TryValidatePending(
            workspace with { Document = extraAnchorDocument },
            resolver,
            out IReadOnlyList<string> blockers));
        CollectionAssert.Contains(
            blockers.ToList(),
            CharacterCreationBootstrapBlockers.BindingInvalid);
    }

    [TestMethod]
    public void Post_selection_stale_marker_is_rejected_and_generic_auxiliary_cas_cannot_clear_it()
    {
        string coreRoot = FindCoreRoot();
        string workspaceRoot = Path.Combine(
            Path.GetTempPath(),
            $"chummer-bootstrap-lifecycle-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workspaceRoot);
        try
        {
            var store = new FileWorkspaceStore(workspaceRoot);
            FileSystemCharacterSourceDataResolver resolver = CreateSourceResolver(coreRoot);
            CharacterCreationBootstrapResult<CharacterCreationBootstrapReceipt> created =
                CreateService(store, resolver, CreateFileQueries()).Create(CanonicalRequest());
            Assert.AreEqual(CharacterCreationBootstrapOutcomes.Success, created.Outcome,
                string.Join(",", created.Blockers));
            CharacterCreationBootstrapReceipt receipt = created.Value!;
            WorkspaceStoredDocument workspace = store.Get(receipt.WorkspaceId).Value!;

            CharacterWorkspaceId forgedId = new("ordinary-bootstrap-forgery");
            WorkspaceDocument ordinaryCopy = workspace.Document with
            {
                State = workspace.Document.State with
                {
                    AuxiliaryState = WorkspaceDocumentAuxiliaryState.Empty
                }
            };
            WorkspaceStoreMutationResult ordinaryCreated = store.CreateWorkspaceDocument(
                forgedId,
                ordinaryCopy);
            Assert.IsTrue(ordinaryCreated.Success);
            CharacterCreationBootstrapBinding forgedUnsigned = receipt.Binding with
            {
                WorkspaceId = forgedId,
                BindingDigest = string.Empty
            };
            CharacterCreationBootstrapBinding forgedBinding = forgedUnsigned with
            {
                BindingDigest = CharacterCreationBootstrapBindingDigest.Compute(forgedUnsigned)
            };
            WorkspaceDocument forgedReplacement = ordinaryCopy with
            {
                State = ordinaryCopy.State with
                {
                    AuxiliaryState = new WorkspaceDocumentAuxiliaryState(
                        CharacterCreationBootstrapBinding: forgedBinding)
                }
            };
            WorkspaceStoreMutationResult forged =
                ((IWorkspaceAuxiliaryStateAtomicCommitCapability)store)
                .ReplaceWorkspaceDocumentAndAuxiliaryStateAndCheckpoint(
                    forgedId,
                    ordinaryCreated.Entry!.Value.ContentRevision,
                    ordinaryCopy.AuxiliaryStateDigest,
                    forgedReplacement);
            Assert.IsFalse(forged.Success,
                "An ordinary auxiliary-state CAS cannot forge a bootstrap binding.");
            Assert.IsNull(store.Get(forgedId).Value!.Document.AuxiliaryState
                .CharacterCreationBootstrapBinding);

            CharacterWorkspaceId genericBoundId = new("generic-bound-bootstrap");
            Assert.IsFalse(store.CreateWorkspaceDocument(
                genericBoundId,
                workspace.Document).Success,
                "Generic creation cannot persist bootstrap auxiliary authority.");

            XDocument selected = XDocument.Parse(workspace.Document.Content);
            selected.Root!.AddFirst(new XElement("metatype", "Human"));
            WorkspaceDocument stale = workspace.Document with
            {
                State = workspace.Document.State with
                {
                    Payload = selected.ToString(SaveOptions.DisableFormatting)
                }
            };
            Assert.IsFalse(CharacterCreationBootstrapAuthority.TryValidatePending(
                workspace with { Document = stale },
                resolver,
                out IReadOnlyList<string> staleBlockers));
            Assert.IsTrue(staleBlockers.Count > 0);

            selected.Root!.Element(CharacterCreationBootstrapXml.MarkerElement)!.Remove();
            WorkspaceDocument genericCompletion = workspace.Document with
            {
                State = workspace.Document.State with
                {
                    Payload = selected.ToString(SaveOptions.DisableFormatting),
                    AuxiliaryState = WorkspaceDocumentAuxiliaryState.Empty
                }
            };
            WorkspaceStoreMutationResult attempted =
                ((IWorkspaceAuxiliaryStateAtomicCommitCapability)store)
                .ReplaceWorkspaceDocumentAndAuxiliaryStateAndCheckpoint(
                    receipt.WorkspaceId,
                    workspace.ContentRevision,
                    workspace.Document.AuxiliaryStateDigest,
                    genericCompletion);
            Assert.IsFalse(attempted.Success,
                "Only a future resolver-bound finalization authority may clear the pending marker and binding.");
            Assert.IsNotNull(store.Get(receipt.WorkspaceId).Value!.Document.AuxiliaryState
                .CharacterCreationBootstrapBinding);
        }
        finally
        {
            if (Directory.Exists(workspaceRoot))
                Directory.Delete(workspaceRoot, recursive: true);
        }
    }

    private static CharacterCreationBootstrapService CreateService(
        IWorkspaceStore store,
        ICharacterSourceDataResolver sourceResolver,
        ICharacterFileQueries queries,
        ICharacterCreationBootstrapActivationProjector? activationProjector = null)
    {
        var codec = new Sr5WorkspaceCodec(
            queries,
            new XmlCharacterSectionQueries(new CharacterSectionService(sourceResolver)),
            new XmlCharacterMetadataCommands(new CharacterFileService()));
        return new CharacterCreationBootstrapService(
            store,
            new RulesetWorkspaceCodecResolver([codec]),
            queries,
            sourceResolver,
            activationProjector);
    }

    private static void AssertJsonEqual<T>(T expected, T actual)
        => Assert.AreEqual(
            JsonSerializer.Serialize(expected),
            JsonSerializer.Serialize(actual));

    private static CharacterCreationBootstrapRequest CanonicalRequest()
        => new(
            CharacterCreationBootstrapSchemas.RequestV1,
            CharacterCreationBootstrapStages.AwaitingFoundationSelection,
            RulesetDefaults.Sr5,
            "Pending Runner",
            "No Default",
            CharacterCreationBuildMethods.Priority,
            CanonicalPrioritySettingsId);

    private static ICharacterFileQueries CreateFileQueries()
        => new XmlCharacterFileQueries(new CharacterFileService());

    private static FileSystemCharacterSourceDataResolver CreateSourceResolver(string coreRoot)
        => new(new FileSystemContentOverlayCatalogService(coreRoot, coreRoot, null));

    private static void AssertPrepareBlocked(
        CharacterWorkspaceId workspaceId,
        WorkspaceDocument document,
        ICharacterSourceDataResolver resolver,
        string expectedBlocker)
    {
        Assert.IsFalse(CharacterCreationBootstrapAuthority.TryPrepareBinding(
            workspaceId,
            document,
            resolver,
            out _,
            out _,
            out IReadOnlyList<string> blockers));
        CollectionAssert.Contains(blockers.ToList(), expectedBlocker);
    }

    private static WorkspaceDocument WithoutBinding(
        WorkspaceDocument template,
        XDocument character)
        => template with
        {
            State = template.State with
            {
                Payload = character.ToString(SaveOptions.DisableFormatting),
                AuxiliaryState = WorkspaceDocumentAuxiliaryState.Empty
            }
        };

    private static CharacterCreationBootstrapBinding ResignBinding(
        CharacterCreationBootstrapBinding binding)
    {
        CharacterCreationBootstrapBinding unsigned = binding with { BindingDigest = string.Empty };
        return unsigned with
        {
            BindingDigest = CharacterCreationBootstrapBindingDigest.Compute(unsigned)
        };
    }

    private static CharacterCreationBootstrapReceipt ResignReceipt(
        CharacterCreationBootstrapReceipt receipt)
    {
        CharacterCreationBootstrapReceipt unsigned = receipt with { ReceiptDigest = string.Empty };
        return unsigned with
        {
            ReceiptDigest = CharacterCreationBootstrapReceiptDigest.Compute(unsigned)
        };
    }

    private static CharacterCreationBootstrapActivationBundle ResignActivation(
        CharacterCreationBootstrapActivationBundle activation)
    {
        CharacterCreationBootstrapActivationBundle unsigned = activation with
        {
            BundleDigest = string.Empty
        };
        return unsigned with
        {
            BundleDigest = CharacterCreationBootstrapActivationIntegrity.ComputeBundleDigest(
                unsigned)
        };
    }

    private static string MinimalMarkerXml()
        => $"""
           <character>
             <name>Pending Runner</name>
             <alias>No Default</alias>
             <buildmethod>{CharacterCreationBuildMethods.Priority}</buildmethod>
             <createdversion>5.225.0</createdversion>
             <appversion>5.225.0</appversion>
             <karma>0</karma>
             <nuyen>0</nuyen>
             <created>False</created>
             <gameedition>SR5</gameedition>
             <settings>{CanonicalPrioritySettingsId}</settings>
             <{CharacterCreationBootstrapXml.MarkerElement}>
               <{CharacterCreationBootstrapXml.SchemaElement}>{CharacterCreationBootstrapSchemas.MarkerV1}</{CharacterCreationBootstrapXml.SchemaElement}>
               <{CharacterCreationBootstrapXml.StageElement}>{CharacterCreationBootstrapStages.AwaitingFoundationSelection}</{CharacterCreationBootstrapXml.StageElement}>
             </{CharacterCreationBootstrapXml.MarkerElement}>
           </character>
           """;

    private static string FindCoreRoot()
    {
        DirectoryInfo? current = new(AppDomain.CurrentDomain.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Chummer", "data", "settings.xml")))
                return current.FullName;
            current = current.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate canonical Chummer/data/settings.xml.");
    }

    private sealed class CountingSourceDataResolver : ICharacterSourceDataResolver
    {
        private readonly ICharacterSourceDataResolver _inner;

        public CountingSourceDataResolver(ICharacterSourceDataResolver inner)
        {
            _inner = inner;
        }

        public int ContextCreateCount { get; private set; }

        public int SourceProfileResolveCount { get; private set; }

        public int MetatypeResolveCount { get; private set; }

        public int PrerequisiteResolveCount { get; private set; }

        public int QualitiesResolveCount { get; private set; }

        public int MagicResolveCount { get; private set; }

        public ICharacterSourceDataContext? TryCreateContext(string characterXml)
        {
            ContextCreateCount++;
            ICharacterSourceDataContext? context = _inner.TryCreateContext(characterXml);
            return context is null ? null : new CountingSourceDataContext(this, context);
        }

        private sealed class CountingSourceDataContext : ICharacterSourceDataContext
        {
            private readonly CountingSourceDataResolver _owner;
            private readonly ICharacterSourceDataContext _inner;

            public CountingSourceDataContext(
                CountingSourceDataResolver owner,
                ICharacterSourceDataContext inner)
            {
                _owner = owner;
                _inner = inner;
            }

            public bool TryResolveCreationSourceProfile(
                out CharacterCreationSourceProfileAuthority authority)
            {
                _owner.SourceProfileResolveCount++;
                return _inner.TryResolveCreationSourceProfile(out authority);
            }

            public bool TryResolveCreationMetatypeCatalog(
                out CharacterCreationMetatypeCatalogAuthority authority)
            {
                _owner.MetatypeResolveCount++;
                return _inner.TryResolveCreationMetatypeCatalog(out authority);
            }

            public bool TryResolveCreationPrerequisiteAuthority(
                out CharacterCreationPrerequisiteAuthority authority)
            {
                _owner.PrerequisiteResolveCount++;
                return _inner.TryResolveCreationPrerequisiteAuthority(out authority);
            }

            public bool TryResolveCreationQualitiesAuthority(
                out CharacterCreationQualitiesAuthority authority)
            {
                _owner.QualitiesResolveCount++;
                return _inner.TryResolveCreationQualitiesAuthority(out authority);
            }

            public bool TryResolveCreationMagicResonanceAuthority(
                out CharacterCreationMagicResonanceAuthority authority)
            {
                _owner.MagicResolveCount++;
                return _inner.TryResolveCreationMagicResonanceAuthority(out authority);
            }

            public bool TryResolveCyberwareGradeDeviceRating(
                string gradeName,
                string improvementSource,
                out int deviceRating)
                => _inner.TryResolveCyberwareGradeDeviceRating(
                    gradeName,
                    improvementSource,
                    out deviceRating);

            public bool TryResolveVehicleModBonuses(
                string sourceId,
                string name,
                out CharacterVehicleModSourceBonuses bonuses)
                => _inner.TryResolveVehicleModBonuses(sourceId, name, out bonuses);
        }
    }

    private sealed class DriftingSourceDataResolver : ICharacterSourceDataResolver
    {
        private readonly ICharacterSourceDataResolver _inner;

        public DriftingSourceDataResolver(ICharacterSourceDataResolver inner)
        {
            _inner = inner;
        }

        public int ContextCreateCount { get; private set; }

        public ICharacterSourceDataContext? TryCreateContext(string characterXml)
        {
            ContextCreateCount++;
            ICharacterSourceDataContext? context = _inner.TryCreateContext(characterXml);
            return context is null ? null : new DriftingSourceDataContext(context);
        }
    }

    private sealed class ConsumerDriftSourceDataResolver : ICharacterSourceDataResolver
    {
        private readonly ICharacterSourceDataResolver _inner;
        private readonly string _domain;

        public ConsumerDriftSourceDataResolver(
            ICharacterSourceDataResolver inner,
            string domain)
        {
            _inner = inner;
            _domain = domain;
        }

        public int ContextCreateCount { get; private set; }

        public ICharacterSourceDataContext? TryCreateContext(string characterXml)
        {
            ContextCreateCount++;
            ICharacterSourceDataContext? context = _inner.TryCreateContext(characterXml);
            return context is null || ContextCreateCount == 1
                ? context
                : new ConsumerDriftSourceDataContext(context, _domain);
        }
    }

    private sealed class ConsumerDriftSourceDataContext : ICharacterSourceDataContext
    {
        private readonly ICharacterSourceDataContext _inner;
        private readonly string _domain;
        private static readonly string DriftDigest = "sha256:" + new string('0', 64);

        public ConsumerDriftSourceDataContext(
            ICharacterSourceDataContext inner,
            string domain)
        {
            _inner = inner;
            _domain = domain;
        }

        public bool TryResolveCreationSourceProfile(
            out CharacterCreationSourceProfileAuthority authority)
        {
            bool resolved = _inner.TryResolveCreationSourceProfile(out authority);
            if (resolved && _domain == "settings")
                authority = authority with { RawProfileInputsDigest = DriftDigest };
            return resolved;
        }

        public bool TryResolveCreationMetatypeCatalog(
            out CharacterCreationMetatypeCatalogAuthority authority)
        {
            bool resolved = _inner.TryResolveCreationMetatypeCatalog(out authority);
            if (resolved && _domain == "metatypes")
            {
                authority = authority with
                {
                    SourceContext = authority.SourceContext with
                    {
                        RawMetatypesXmlDigest = DriftDigest
                    }
                };
            }
            return resolved;
        }

        public bool TryResolveCreationPrerequisiteAuthority(
            out CharacterCreationPrerequisiteAuthority authority)
        {
            bool resolved = _inner.TryResolveCreationPrerequisiteAuthority(out authority);
            if (resolved && _domain is "priorities" or "skills")
            {
                authority = _domain == "priorities"
                    ? authority with { RawPrioritiesXmlDigest = DriftDigest }
                    : authority with { RawSkillsXmlDigest = DriftDigest };
            }
            return resolved;
        }

        public bool TryResolveCreationQualitiesAuthority(
            out CharacterCreationQualitiesAuthority authority)
        {
            bool resolved = _inner.TryResolveCreationQualitiesAuthority(out authority);
            if (resolved && _domain == "qualities")
                authority = authority with { SourceDigest = DriftDigest };
            return resolved;
        }

        public bool TryResolveCreationMagicResonanceAuthority(
            out CharacterCreationMagicResonanceAuthority authority)
        {
            bool resolved = _inner.TryResolveCreationMagicResonanceAuthority(out authority);
            if (resolved && _domain is "traditions" or "streams" or "powers" or "spells" or "complexforms")
                authority = authority with { SourceInputsDigest = DriftDigest };
            return resolved;
        }

        public bool TryResolveCyberwareGradeDeviceRating(
            string gradeName,
            string improvementSource,
            out int deviceRating)
            => _inner.TryResolveCyberwareGradeDeviceRating(
                gradeName,
                improvementSource,
                out deviceRating);

        public bool TryResolveVehicleModBonuses(
            string sourceId,
            string name,
            out CharacterVehicleModSourceBonuses bonuses)
            => _inner.TryResolveVehicleModBonuses(sourceId, name, out bonuses);
    }

    private sealed class DriftingLifeModulesCatalogService : ILifeModulesCatalogService
    {
        private readonly ILifeModulesCatalogService _inner;

        public DriftingLifeModulesCatalogService(ILifeModulesCatalogService inner)
        {
            _inner = inner;
        }

        public bool Drifted { get; set; }

        public LifeModuleCatalogAuthorityDto GetAuthority()
        {
            LifeModuleCatalogAuthorityDto authority = _inner.GetAuthority();
            return Drifted
                ? authority with { RawXmlDigest = "sha256:" + new string('0', 64) }
                : authority;
        }

        public IReadOnlyList<LifeModuleStageDto> GetStages() => _inner.GetStages();

        public IReadOnlyList<LifeModuleSummaryDto> GetModules(string? stage = null)
            => _inner.GetModules(stage);

        public IReadOnlyList<LifeModuleLegalOptionDto> GetOptionProjections(
            string? stage = null,
            IReadOnlyCollection<string>? enabledSources = null)
            => _inner.GetOptionProjections(stage, enabledSources);
    }

    private sealed class CountingLifeModulesCatalogService : ILifeModulesCatalogService
    {
        private readonly ILifeModulesCatalogService _inner;

        public CountingLifeModulesCatalogService(ILifeModulesCatalogService inner)
        {
            _inner = inner;
        }

        public int AuthorityReadCount { get; private set; }

        public int OptionProjectionCount { get; private set; }

        public LifeModuleCatalogAuthorityDto GetAuthority()
        {
            AuthorityReadCount++;
            return _inner.GetAuthority();
        }

        public IReadOnlyList<LifeModuleStageDto> GetStages() => _inner.GetStages();

        public IReadOnlyList<LifeModuleSummaryDto> GetModules(string? stage = null)
            => _inner.GetModules(stage);

        public IReadOnlyList<LifeModuleLegalOptionDto> GetOptionProjections(
            string? stage = null,
            IReadOnlyCollection<string>? enabledSources = null)
        {
            OptionProjectionCount++;
            return _inner.GetOptionProjections(stage, enabledSources);
        }
    }

    private sealed class DriftingSourceDataContext : ICharacterSourceDataContext
    {
        private readonly ICharacterSourceDataContext _inner;

        public DriftingSourceDataContext(ICharacterSourceDataContext inner)
        {
            _inner = inner;
        }

        public bool Drifted { get; set; }

        public bool TryResolveCreationSourceProfile(
            out CharacterCreationSourceProfileAuthority authority)
        {
            bool resolved = _inner.TryResolveCreationSourceProfile(out authority);
            if (resolved && Drifted)
            {
                authority = authority with
                {
                    RawProfileInputsDigest = "sha256:" + new string('0', 64)
                };
            }
            return resolved;
        }

        public bool TryResolveCreationMetatypeCatalog(
            out CharacterCreationMetatypeCatalogAuthority authority)
            => _inner.TryResolveCreationMetatypeCatalog(out authority);

        public bool TryResolveCreationPrerequisiteAuthority(
            out CharacterCreationPrerequisiteAuthority authority)
            => _inner.TryResolveCreationPrerequisiteAuthority(out authority);

        public bool TryResolveCreationQualitiesAuthority(
            out CharacterCreationQualitiesAuthority authority)
            => _inner.TryResolveCreationQualitiesAuthority(out authority);

        public bool TryResolveCreationMagicResonanceAuthority(
            out CharacterCreationMagicResonanceAuthority authority)
            => _inner.TryResolveCreationMagicResonanceAuthority(out authority);

        public bool TryResolveCyberwareGradeDeviceRating(
            string gradeName,
            string improvementSource,
            out int deviceRating)
            => _inner.TryResolveCyberwareGradeDeviceRating(
                gradeName,
                improvementSource,
                out deviceRating);

        public bool TryResolveVehicleModBonuses(
            string sourceId,
            string name,
            out CharacterVehicleModSourceBonuses bonuses)
            => _inner.TryResolveVehicleModBonuses(sourceId, name, out bonuses);
    }

    private sealed class SourceDriftingProjector : ICharacterCreationBootstrapActivationProjector
    {
        private readonly ICharacterCreationBootstrapActivationProjector _inner;

        public SourceDriftingProjector(ICharacterCreationBootstrapActivationProjector inner)
        {
            _inner = inner;
        }

        public CharacterCreationInitialProjection Project(
            WorkspaceStoredDocument workspace,
            CharacterCreationBootstrapSourceSnapshot sourceSnapshot)
        {
            return _inner.Project(workspace, sourceSnapshot);
        }

        public bool IsCurrent(
            CharacterCreationInitialProjection projection,
            ICharacterSourceDataContext sourceContext,
            string characterXml)
        {
            ((DriftingSourceDataContext)sourceContext).Drifted = true;
            return _inner.IsCurrent(projection, sourceContext, characterXml);
        }
    }

    private sealed class CountingWorkspaceStore :
        IWorkspaceStore,
        ICharacterCreationBootstrapAtomicCreateCapability
    {
        private readonly InMemoryWorkspaceStore _inner = new();

        public int ReadCount { get; private set; }

        public Func<WorkspaceStoreReadResult, WorkspaceStoreReadResult>? RewriteRead { get; set; }

        public bool SupportsCharacterCreationBootstrapAtomicCreate => true;

        public WorkspaceStoreMutationResult CreateCharacterCreationBootstrapWorkspaceDocument(
            CharacterWorkspaceId id,
            WorkspaceDocument document)
            => _inner.CreateCharacterCreationBootstrapWorkspaceDocument(id, document);

        public WorkspaceStoreMutationResult CreateWorkspaceDocument(WorkspaceDocument document)
            => _inner.CreateWorkspaceDocument(document);

        public WorkspaceStoreMutationResult CreateWorkspaceDocument(
            Chummer.Contracts.Owners.OwnerScope owner,
            WorkspaceDocument document)
            => _inner.CreateWorkspaceDocument(owner, document);

        public WorkspaceStoreMutationResult CreateWorkspaceDocument(
            CharacterWorkspaceId id,
            WorkspaceDocument document)
            => _inner.CreateWorkspaceDocument(id, document);

        public WorkspaceStoreMutationResult CreateWorkspaceDocument(
            Chummer.Contracts.Owners.OwnerScope owner,
            CharacterWorkspaceId id,
            WorkspaceDocument document)
            => _inner.CreateWorkspaceDocument(owner, id, document);

        public IReadOnlyList<WorkspaceStoreEntry> List() => _inner.List();

        public IReadOnlyList<WorkspaceStoreEntry> List(
            Chummer.Contracts.Owners.OwnerScope owner) => _inner.List(owner);

        public WorkspaceStoreReadResult Get(CharacterWorkspaceId id)
        {
            ReadCount++;
            WorkspaceStoreReadResult result = _inner.Get(id);
            return RewriteRead?.Invoke(result) ?? result;
        }

        public WorkspaceStoreReadResult Get(
            Chummer.Contracts.Owners.OwnerScope owner,
            CharacterWorkspaceId id)
        {
            ReadCount++;
            return _inner.Get(owner, id);
        }

        public WorkspaceStoreMutationResult ReplaceWorkspaceDocument(
            CharacterWorkspaceId id,
            long expectedContentRevision,
            WorkspaceDocument document)
            => _inner.ReplaceWorkspaceDocument(id, expectedContentRevision, document);

        public WorkspaceStoreMutationResult ReplaceWorkspaceDocument(
            Chummer.Contracts.Owners.OwnerScope owner,
            CharacterWorkspaceId id,
            long expectedContentRevision,
            WorkspaceDocument document)
            => _inner.ReplaceWorkspaceDocument(owner, id, expectedContentRevision, document);

        public WorkspaceStoreMutationResult SaveCheckpoint(
            CharacterWorkspaceId id,
            long expectedContentRevision)
            => _inner.SaveCheckpoint(id, expectedContentRevision);

        public WorkspaceStoreMutationResult SaveCheckpoint(
            Chummer.Contracts.Owners.OwnerScope owner,
            CharacterWorkspaceId id,
            long expectedContentRevision)
            => _inner.SaveCheckpoint(owner, id, expectedContentRevision);

        public WorkspaceStoreMutationResult Delete(
            CharacterWorkspaceId id,
            long expectedContentRevision)
            => _inner.Delete(id, expectedContentRevision);

        public WorkspaceStoreMutationResult Delete(
            Chummer.Contracts.Owners.OwnerScope owner,
            CharacterWorkspaceId id,
            long expectedContentRevision)
            => _inner.Delete(owner, id, expectedContentRevision);
    }
}

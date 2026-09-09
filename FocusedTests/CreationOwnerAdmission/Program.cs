using System.Text.Json;
using Chummer.Application.Characters;
using Chummer.Application.Owners;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Infrastructure.Workspaces;
using Microsoft.Extensions.DependencyInjection;

string root = Check.Root(args);
(string Name, Action Run)[] cases =
[
    ("bootstrap creates only in admitted local or linked partition", BootstrapPartition),
    ("contacts commits only in admitted partition with identical sibling partitions", ContactsPartition),
    ("local to linked transition rejects original creation stamps", () => Stale(OwnerScope.LocalSingleUser, false)),
    ("A to B to A rejects original creation stamps", () => Stale(CreationFixture.AccountA, true)),
    ("foreign and malformed stamps never enter storage", ForeignAndMalformed),
    ("legacy-only storage cannot advertise scoped creation support", () => Unsupported(false)),
    ("explicit disabled scoped storage fails closed", () => Unsupported(true)),
    ("legacy value-only owner never gains a local lease fallback", ValueOnlyOwner),
    ("same injected owner lease excludes transitions during reads and commit", Exclusion),
    ("store exceptions release the exact owner lease without a write", FailureReleasesLease),
    ("fresh activation validates once under its exact original authority", FreshActivation),
    ("bound and legacy activation registries cannot consume each other's bundles", ActivationIdentity),
    ("committed bootstrap survives unavailable scoped activation projection", CreatedRequiresReload),
    ("old activation bundle rejected after ABA while fresh restart reads remain valid", ActivationAndRestart),
    ("contacts exact original-owner receipt survives restart and a fresh epoch", ReceiptRestart),
    ("explicit legacy entrypoints remain trusted-local", LegacyRemainsLocal),
    ("DI aliases concrete services and respects actual owner override", DependencyIdentity)
];
int selected = Array.IndexOf(args, "--case");
string? filter = selected >= 0 && selected + 1 < args.Length ? args[selected + 1] : null;
int count = 0;
foreach ((string name, Action run) in cases.Where(item => filter is null
             || item.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)))
{
    run();
    count++;
    Console.WriteLine("PASS " + name);
}
Check.That(count != 0, "No selected test cases.");
Console.WriteLine($"PASS {count} Core creation owner-admission cases");

void BootstrapPartition()
{
    foreach (OwnerScope owner in new[] { OwnerScope.LocalSingleUser, CreationFixture.AccountA })
    {
        using var fixture = new CreationFixture(root, owner);
        OwnerContextStamp stamp = fixture.Authority.Capture();
        var result = Bootstrap(fixture.Provider).Create(stamp, CreationFixture.Request());
        Check.That(result.Outcome == CharacterCreationBootstrapOutcomes.Success && result.Value is not null,
            "Owner-bound bootstrap did not create the canonical typed fixture.");
        foreach (OwnerScope partition in CreationFixture.Partitions)
            Check.That(fixture.Read(partition, result.Value!.WorkspaceId).Success == (partition == owner),
                "Bootstrap created or exposed a workspace in the wrong owner partition.");
        Check.That(result.Value!.ContentRevision == 1 && result.Value.SavedRevision == 0,
            "Bootstrap changed the existing revision semantics.");
        NoTransientAuthority(fixture.Snapshot(owner, result.Value.WorkspaceId), stamp);
        Check.That(fixture.Authority.ActiveLeases == 0, "Bootstrap leaked its owner lease.");
    }
}

void ContactsPartition()
{
    using var fixture = new CreationFixture(root, CreationFixture.AccountA);
    fixture.SeedContacts();
    OwnerContextStamp stamp = fixture.Authority.Capture();
    string local = fixture.Snapshot(OwnerScope.LocalSingleUser, CreationFixture.ContactWorkspace);
    string other = fixture.Snapshot(CreationFixture.AccountB, CreationFixture.ContactWorkspace);
    var service = Contacts(fixture.Provider);
    CharacterCreationContactConfirmRequest request = Preview(service, stamp);
    var result = service.Confirm(stamp, request);
    Check.That(result.Outcome == CharacterCreationContactOutcomes.Applied && result.Value is not null,
        "Admitted Contacts edit did not atomically commit.");
    Check.That(fixture.Store.Get(CreationFixture.AccountA, CreationFixture.ContactWorkspace).Value!
            is { ContentRevision: 2, SavedRevision: 2 }, "Contacts checkpoint/revision is not one atomic successor.");
    Check.That(fixture.Snapshot(OwnerScope.LocalSingleUser, CreationFixture.ContactWorkspace) == local
        && fixture.Snapshot(CreationFixture.AccountB, CreationFixture.ContactWorkspace) == other,
        "Identical workspace bytes in local/B were mutated by A's operation.");
    NoTransientAuthority(JsonSerializer.Serialize(result.Value), stamp);
    NoTransientAuthority(fixture.Snapshot(CreationFixture.AccountA, CreationFixture.ContactWorkspace), stamp);
    var replay = service.Confirm(stamp, request);
    Check.That(replay.Outcome == CharacterCreationContactOutcomes.Replayed
        && JsonSerializer.Serialize(replay.Value) == JsonSerializer.Serialize(result.Value),
        "Same-owner idempotent replay changed the original typed receipt.");
}

void Stale(OwnerScope initial, bool aba)
{
    using var fixture = new CreationFixture(root, initial);
    fixture.SeedContacts();
    var store = StoreProxy.Wrap<IAllCreationStore>(fixture.Store, out StoreProxy proxy);
    int domainReads = 0;
    ServiceProvider provider = fixture.AddProvider(store, fixture.Authority, _ => domainReads++);
    var service = Contacts(provider);
    var bootstrap = Bootstrap(provider);
    OwnerContextStamp original = fixture.Authority.Capture();
    CharacterCreationContactConfirmRequest request = Preview(service, original);
    Check.That(service.Confirm(original, request).Value is not null,
        "The stale receipt test must begin with an actual committed receipt.");
    string[] before = CreationFixture.Partitions.Select(owner =>
        fixture.Snapshot(owner, CreationFixture.ContactWorkspace)).ToArray();
    proxy.ResetCounts();
    domainReads = 0;
    fixture.Authority.Transition(aba ? CreationFixture.AccountB : CreationFixture.AccountA);
    if (aba) fixture.Authority.Transition(initial);
    Check.That(fixture.Authority.Capture() != original, "Test transition did not advance original authority.");
    Check.That(bootstrap.Create(original, CreationFixture.Request()).Value is null,
        "A stale creation dialog was admitted to bootstrap.");
    Check.That(bootstrap.CreateActivation(original, CreationFixture.Request()).Receipt is null,
        "A stale creation dialog was admitted through activation bootstrap.");
    Check.That(service.Load(original, new(CreationFixture.ContactWorkspace)).Value is null,
        "A stale Contacts read returned owner-bearing data.");
    Check.That(service.Preview(original, new(request.Binding, request.Edit)).Value is null,
        "A stale Contacts preview was rebound to a new epoch.");
    Check.That(service.Confirm(original, request).Value is null,
        "An original-epoch Contacts confirmation reached a mutation.");
    Check.That(service.LookupReceipt(original, new(CreationFixture.ContactWorkspace, request.IdempotencyKey)).Value is null,
        "A stale read was allowed through receipt lookup.");
    Check.That(proxy.Reads == 0 && proxy.Mutations == 0 && domainReads == 0,
        "Stale authority reached the storage or source authority despite rejection.");
    for (int index = 0; index < before.Length; index++)
        Check.That(fixture.Count(CreationFixture.Partitions[index]) == 1
            && fixture.Snapshot(CreationFixture.Partitions[index], CreationFixture.ContactWorkspace) == before[index],
            "Rejected authority changed a document, receipt, or workspace inventory.");
}

void ForeignAndMalformed()
{
    using var fixture = new CreationFixture(root, CreationFixture.AccountA);
    fixture.SeedContacts();
    var store = StoreProxy.Wrap<IAllCreationStore>(fixture.Store, out StoreProxy proxy);
    int domainReads = 0;
    ServiceProvider provider = fixture.AddProvider(store, fixture.Authority, _ => domainReads++);
    var bootstrap = Bootstrap(provider);
    var contacts = Contacts(provider);
    OwnerContextStamp valid = fixture.Authority.Capture();
    var pending = bootstrap.CreateActivation(valid, CreationFixture.Request());
    Check.That(pending.Bundle is not null, "Foreign-stamp control must have a genuine pending activation.");
    var request = Preview(contacts, valid);
    Check.That(contacts.Confirm(valid, request).Value is not null,
        "Foreign receipt test must begin with an actual committed receipt.");
    OwnerContextStamp[] rejected = [default, valid with { TransitionRevision = -1 },
        valid with { AuthorityInstanceId = "foreign-authority" }, valid with { Owner = CreationFixture.AccountB },
        new ControlledOwner(CreationFixture.AccountA).Capture()];
    proxy.ResetCounts();
    domainReads = 0;
    foreach (OwnerContextStamp stamp in rejected)
    {
        Check.That(bootstrap.Create(stamp, CreationFixture.Request()).Value is null,
            "Foreign or malformed bootstrap authority was admitted.");
        Check.That(bootstrap.CreateActivation(stamp, CreationFixture.Request()).Receipt is null,
            "Foreign or malformed activation create authority was admitted.");
        Check.That(!bootstrap.TryValidateCurrent(stamp, pending.Bundle!, out _),
            "A forged same-owner epoch or malformed authority validated a genuine pending bundle.");
        Check.That(contacts.Load(stamp, new(CreationFixture.ContactWorkspace)).Value is null,
            "Foreign or malformed read authority was admitted.");
        Check.That(contacts.Preview(stamp, new(request.Binding, request.Edit)).Value is null,
            "Foreign or malformed preview authority was admitted.");
        Check.That(contacts.Confirm(stamp, request).Value is null,
            "Foreign or malformed receipt replay authority was admitted.");
        Check.That(contacts.LookupReceipt(stamp, new(CreationFixture.ContactWorkspace, request.IdempotencyKey)).Value is null,
            "Foreign or malformed receipt-read authority was admitted.");
    }
    Check.That(proxy.Reads == 0 && proxy.Mutations == 0 && domainReads == 0,
        "Rejected stamps reached domain sources, the activation projector, or storage.");
    Check.That(bootstrap.TryValidateCurrent(valid, pending.Bundle!, out _),
        "Rejected foreign authority consumed the original authentic pending bundle.");
    Check.That(contacts.LookupReceipt(valid, new(CreationFixture.ContactWorkspace, request.IdempotencyKey)).Value is not null,
        "The positive receipt control did not remain readable under its exact original authority.");
}

void Unsupported(bool explicitFalse)
{
    using var fixture = new CreationFixture(root, CreationFixture.AccountA);
    fixture.SeedContacts();
    OwnerContextStamp stamp = fixture.Authority.Capture();
    CharacterCreationContactConfirmRequest request = Preview(Contacts(fixture.Provider), stamp);
    IWorkspaceStore masked;
    StoreProxy proxy;
    if (explicitFalse)
    {
        masked = StoreProxy.Wrap<IAllCreationStore>(fixture.Store, out proxy);
        proxy.DisableScopedCapabilities = true;
    }
    else masked = StoreProxy.Wrap<ILegacyCreationStore>(fixture.Store, out proxy);
    int domainReads = 0;
    ServiceProvider provider = fixture.AddProvider(masked, fixture.Authority, _ => domainReads++);
    var bootstrap = Bootstrap(provider);
    var contacts = Contacts(provider);
    proxy.ResetCounts();
    Check.That(bootstrap.Create(stamp, CreationFixture.Request()).Outcome == CharacterCreationBootstrapOutcomes.Unavailable,
        "Missing scoped bootstrap support was not explicitly unavailable.");
    Check.That(bootstrap.CreateActivation(stamp, CreationFixture.Request()).Outcome == CharacterCreationBootstrapOutcomes.Unavailable,
        "Activation bypassed missing scoped bootstrap support.");
    Check.That(domainReads == 0 && proxy.Reads == 0 && proxy.Mutations == 0,
        "Missing creation capability reached source evaluation or storage before refusal.");
    var state = contacts.Load(stamp, new(CreationFixture.ContactWorkspace));
    Check.That(state.Value is null || !state.Value.CanEdit,
        "Missing scoped auxiliary capability was advertised as editable owner state.");
    var preview = contacts.Preview(stamp, new(request.Binding, request.Edit));
    Check.That(preview.Value is null || !preview.Value.CanConfirm,
        "Missing scoped auxiliary capability produced a confirmable owner preview.");
    Check.That(contacts.Confirm(stamp, request).Outcome == CharacterCreationContactOutcomes.Unavailable,
        "Missing scoped auxiliary commit support was not explicitly unavailable.");
    Check.That(proxy.Mutations == 0, "Scoped capability absence fell back to a legacy or generic mutation.");
    foreach (OwnerScope owner in CreationFixture.Partitions)
        Check.That(fixture.Count(owner) == 1
            && fixture.Read(owner, CreationFixture.ContactWorkspace).Value!.ContentRevision == 1,
            "Capability rejection changed workspace inventory or revision.");
}

void ValueOnlyOwner()
{
    using var fixture = new CreationFixture(root, CreationFixture.AccountA);
    var masked = StoreProxy.Wrap<IAllCreationStore>(fixture.Store, out StoreProxy proxy);
    ServiceProvider provider = fixture.AddProvider(masked, new LegacyValueOnlyOwner(CreationFixture.AccountA));
    var bootstrap = Bootstrap(provider);
    var contacts = Contacts(provider);
    proxy.ResetCounts();
    Check.That(bootstrap.Create(fixture.Authority.Capture(), CreationFixture.Request()).Outcome
        == CharacterCreationBootstrapOutcomes.Unavailable, "Value-only accessor acquired synthetic mutation authority.");
    Check.That(contacts.Load(fixture.Authority.Capture(), new(CreationFixture.ContactWorkspace)).Value is null,
        "Value-only accessor acquired synthetic read authority.");
    Check.That(proxy.Reads == 0 && proxy.Mutations == 0, "Missing actual lease capability reached storage.");
}

void Exclusion()
{
    using var fixture = new CreationFixture(root, CreationFixture.AccountA);
    fixture.SeedContacts();
    var store = StoreProxy.Wrap<IAllCreationStore>(fixture.Store, out StoreProxy proxy);
    var domainObservations = new HashSet<string>(StringComparer.Ordinal);
    ServiceProvider provider = fixture.AddProvider(store, fixture.Authority, method =>
    {
        domainObservations.Add(method);
        AssertExclusion("Domain read " + method);
    });
    var bootstrap = Bootstrap(provider);
    var contacts = Contacts(provider);
    int observed = 0;
    proxy.BeforeCall = (method, parameters) =>
    {
        if (method.Name is not ("Get" or "CreateCharacterCreationBootstrapWorkspaceDocument"
            or "ReplaceWorkspaceDocumentAndAuxiliaryStateAndCheckpoint")) return;
        observed++;
        AssertExclusion("Storage access " + method.Name);
        Check.That(parameters is { Length: > 0 } && parameters[0] is OwnerScope owner
            && owner == CreationFixture.AccountA, "Companion routed through an ownerless storage method.");
    };
    OwnerContextStamp stamp = fixture.Authority.Capture();
    Check.That(bootstrap.Create(stamp, CreationFixture.Request()).Value is not null, "Exclusion bootstrap failed.");
    var activation = bootstrap.CreateActivation(stamp, CreationFixture.Request());
    Check.That(activation.Bundle is not null
        && bootstrap.TryValidateCurrent(stamp, activation.Bundle, out _), "Exclusion activation failed.");
    var request = Preview(contacts, stamp);
    Check.That(contacts.Confirm(stamp, request).Value is not null, "Exclusion Contacts commit failed.");
    Check.That(contacts.LookupReceipt(stamp, new(CreationFixture.ContactWorkspace, request.IdempotencyKey)).Value is not null,
        "Exclusion receipt lookup did not read a real persisted receipt.");
    Check.That(observed >= 8 && fixture.Authority.ActiveLeases == 0,
        "Required scoped storage calls were not observed, or a lease leaked.");
    Check.That(domainObservations.Contains("SourceResolver.TryCreateContext")
        && domainObservations.Any(value => value.StartsWith("SourceContext.", StringComparison.Ordinal))
        && domainObservations.Contains("ActivationProjector.Project")
        && domainObservations.Contains("ActivationProjector.IsCurrent"),
        "Required real source/activation authority observations did not run under the lease.");
    fixture.Authority.Transition(CreationFixture.AccountB);

    void AssertExclusion(string operation)
    {
        Check.That(fixture.Authority.ActiveLeases == 1
            && fixture.Authority.LeaseThreadId == Environment.CurrentManagedThreadId,
            operation + " escaped its original same-thread owner lease.");
        Check.That(!fixture.Authority.CanEnterTransitionGateFromOtherThread(),
            "Concurrent owner writer was not excluded during " + operation + ".");
    }
}

void FailureReleasesLease()
{
    using var fixture = new CreationFixture(root, CreationFixture.AccountA);
    fixture.SeedContacts();
    var store = StoreProxy.Wrap<IAllCreationStore>(fixture.Store, out StoreProxy proxy);
    ServiceProvider provider = fixture.AddProvider(store, fixture.Authority);
    var bootstrap = Bootstrap(provider);
    var contacts = Contacts(provider);
    OwnerContextStamp stamp = fixture.Authority.Capture();
    int injected = 0;
    proxy.BeforeCall = (method, _) =>
    {
        if (method.Name is not ("Get" or "CreateCharacterCreationBootstrapWorkspaceDocument")) return;
        injected++;
        Check.That(fixture.Authority.ActiveLeases == 1, "Injected failure did not run under the admitted lease.");
        throw new IOException("Deterministic test-only storage failure.");
    };
    foreach (Action operation in new Action[]
    {
        () => { bootstrap.Create(stamp, CreationFixture.Request()); },
        () => { contacts.Load(stamp, new(CreationFixture.ContactWorkspace)); }
    })
    {
        try { operation(); }
        catch (IOException) { }
        Check.That(fixture.Authority.ActiveLeases == 0,
            "A storage exception stranded the owner lease.");
    }
    Check.That(injected == 2, "Both companion storage failure paths must be exercised.");
    Check.That(fixture.Count(CreationFixture.AccountA) == 1
        && fixture.Store.Get(CreationFixture.AccountA, CreationFixture.ContactWorkspace).Value!.ContentRevision == 1,
        "An injected pre-write exception changed the owner workspace.");
    fixture.Authority.Transition(CreationFixture.AccountB);
}

void FreshActivation()
{
    using var fixture = new CreationFixture(root, CreationFixture.AccountA);
    var service = Bootstrap(fixture.Provider);
    foreach (bool afterTransition in new[] { false, true })
    {
        if (afterTransition)
        {
            fixture.Authority.Transition(CreationFixture.AccountB);
            fixture.Authority.Transition(CreationFixture.AccountA);
        }
        OwnerContextStamp original = fixture.Authority.Capture();
        var created = service.CreateActivation(original, CreationFixture.Request());
        Check.That(created.Bundle is not null, "Fresh scoped activation did not produce a bundle.");
        NoTransientAuthority(JsonSerializer.Serialize(created.Bundle), original);
        Check.That(service.TryValidateCurrent(original, created.Bundle!, out var blockers)
            && blockers.Count == 0, "Fresh exact-epoch activation was not admitted.");
        Check.That(!service.TryValidateCurrent(original, created.Bundle!, out _),
            "An already consumed activation bundle was admitted twice.");
        Check.That(fixture.Authority.ActiveLeases == 0, "Activation validation leaked its owner lease.");
    }
    Check.That(fixture.Count(CreationFixture.AccountA) == 2
        && fixture.Count(OwnerScope.LocalSingleUser) == 0,
        "Fresh activation was not confined to its admitted partition.");
}

void ActivationIdentity()
{
    foreach (OwnerScope owner in new[] { OwnerScope.LocalSingleUser, CreationFixture.AccountA })
    {
        using var fixture = new CreationFixture(root, owner);
        var bound = Bootstrap(fixture.Provider);
        var legacy = fixture.Provider.GetRequiredService<ICharacterCreationBootstrapActivationService>();
        OwnerContextStamp original = fixture.Authority.Capture();
        var scoped = bound.CreateActivation(original, CreationFixture.Request());
        var local = legacy.CreateActivation(CreationFixture.Request());
        Check.That(scoped.Bundle is not null && local.Bundle is not null,
            "Activation identity controls need two genuinely created bundles.");
        Check.That(!legacy.TryValidateCurrent(scoped.Bundle!, out _),
            "Legacy validation consumed an owner-bound bundle.");
        Check.That(!bound.TryValidateCurrent(original, local.Bundle!, out _),
            "Owner-bound validation laundered a legacy bundle into its authority.");
        Check.That(bound.TryValidateCurrent(original, scoped.Bundle!, out _)
            && legacy.TryValidateCurrent(local.Bundle!, out _),
            "Rejected cross-authority validation consumed an authentic pending bundle.");
        Check.That(!bound.TryValidateCurrent(original, scoped.Bundle!, out _)
            && !legacy.TryValidateCurrent(local.Bundle!, out _),
            "An exact pending bundle could be consumed twice.");
    }
}

void CreatedRequiresReload()
{
    using var fixture = new CreationFixture(root, CreationFixture.AccountA);
    var store = StoreProxy.Wrap<IBootstrapOnlyCreationStore>(fixture.Store, out StoreProxy proxy);
    ServiceProvider provider = fixture.AddProvider(store, fixture.Authority);
    OwnerContextStamp stamp = fixture.Authority.Capture();
    var created = Bootstrap(provider).CreateActivation(stamp, CreationFixture.Request());
    Check.That(created.Outcome == CharacterCreationBootstrapOutcomes.Success
        && created.Receipt is not null && created.Bundle is null && created.CreatedRequiresReload,
        "A committed create was erased or an unusable activation bundle was advertised.");
    Check.That(created.Blockers.Contains(CharacterCreationBootstrapBlockers.ActivationProjectionUnavailable)
        && proxy.Mutations == 1, "Activation failure hid its cause or retried a committed create.");
    Check.That(fixture.Count(CreationFixture.AccountA) == 1
        && fixture.Count(OwnerScope.LocalSingleUser) == 0 && fixture.Count(CreationFixture.AccountB) == 0,
        "Unavailable activation rolled back, duplicated, or moved the committed workspace.");
    var restarted = fixture.AddProvider(new FileWorkspaceStore(fixture.StateDirectory), fixture.Authority);
    var recovered = Contacts(restarted).Load(stamp, new(created.Receipt!.WorkspaceId));
    Check.That(recovered.Value?.Binding.WorkspaceId == created.Receipt.WorkspaceId,
        "Fresh scoped recovery could not read the successful creation receipt's workspace.");
    Check.That(fixture.Authority.ActiveLeases == 0, "Unavailable activation leaked its owner lease.");
}

void ActivationAndRestart()
{
    using var fixture = new CreationFixture(root, CreationFixture.AccountA);
    var service = Bootstrap(fixture.Provider);
    OwnerContextStamp original = fixture.Authority.Capture();
    var created = service.CreateActivation(original, CreationFixture.Request());
    Check.That(created.Outcome == CharacterCreationBootstrapOutcomes.Success
        && created.Bundle is not null && created.Receipt is not null, "Canonical scoped activation did not produce a bundle.");
    fixture.Authority.Transition(CreationFixture.AccountB);
    fixture.Authority.Transition(CreationFixture.AccountA);
    Check.That(!service.TryValidateCurrent(fixture.Authority.Capture(), created.Bundle!, out _),
        "A fresh same-owner epoch reauthorized an old pending bundle.");
    Check.That(!service.TryValidateCurrent(original, created.Bundle!, out _), "Original stale bundle stamp was admitted.");
    var restartedAuthority = new ControlledOwner(CreationFixture.AccountA);
    var restarted = fixture.AddProvider(new FileWorkspaceStore(fixture.StateDirectory), restartedAuthority);
    Check.That(!Bootstrap(restarted).TryValidateCurrent(restartedAuthority.Capture(), created.Bundle!, out _),
        "Process restart recreated a prior pending activation capability.");
    var read = Contacts(restarted).Load(restartedAuthority.Capture(), new(created.Receipt!.WorkspaceId));
    Check.That(read.Value?.Binding.WorkspaceId == created.Receipt.WorkspaceId,
        "Rejecting an old transient bundle prevented an ordinary freshly authorized stable-owner read.");
    Check.That(fixture.Count(CreationFixture.AccountA) == 1 && fixture.Count(OwnerScope.LocalSingleUser) == 0,
        "Recovery created a second workspace or moved history into local scope.");
}

void ReceiptRestart()
{
    using var fixture = new CreationFixture(root, CreationFixture.AccountA);
    fixture.SeedContacts();
    OwnerContextStamp original = fixture.Authority.Capture();
    var request = Preview(Contacts(fixture.Provider), original);
    var committed = Contacts(fixture.Provider).Confirm(original, request);
    Check.That(committed.Value is not null, "Receipt restart control did not commit.");
    string durable = fixture.Snapshot(CreationFixture.AccountA, CreationFixture.ContactWorkspace);
    var restartedAuthority = new ControlledOwner(CreationFixture.AccountA);
    restartedAuthority.Transition(CreationFixture.AccountB);
    restartedAuthority.Transition(CreationFixture.AccountA);
    var restarted = fixture.AddProvider(new FileWorkspaceStore(fixture.StateDirectory), restartedAuthority);
    var lookup = Contacts(restarted).LookupReceipt(restartedAuthority.Capture(),
        new(CreationFixture.ContactWorkspace, request.IdempotencyKey));
    Check.That(lookup.Value is not null
        && JsonSerializer.Serialize(lookup.Value) == JsonSerializer.Serialize(committed.Value),
        "Fresh same-owner receipt lookup lost or changed historical receipt truth.");
    Check.That(fixture.Snapshot(CreationFixture.AccountA, CreationFixture.ContactWorkspace) == durable,
        "Read-only receipt recovery mutated the document or generated a second receipt.");
}

void LegacyRemainsLocal()
{
    using var fixture = new CreationFixture(root, CreationFixture.AccountA);
    var result = fixture.Provider.GetRequiredService<ICharacterCreationBootstrapService>().Create(CreationFixture.Request());
    Check.That(result.Value is not null && fixture.Store.Get(result.Value.WorkspaceId).Success
        && !fixture.Store.Get(CreationFixture.AccountA, result.Value.WorkspaceId).Success,
        "The documented explicit trusted-local legacy API was silently retargeted.");
}

void DependencyIdentity()
{
    using var fixture = new CreationFixture(root, CreationFixture.AccountA);
    Check.That(ReferenceEquals(fixture.Authority, fixture.Provider.GetRequiredService<IOwnerContextAccessor>())
        && fixture.Provider.GetService<IOwnerContextLeaseAccessor>() is null,
        "DI constructed a second owner authority beside the actual host accessor.");
    Check.That(ReferenceEquals(fixture.Provider.GetRequiredService<CharacterCreationBootstrapService>(),
            fixture.Provider.GetRequiredService<ICharacterCreationBootstrapService>())
        && ReferenceEquals(fixture.Provider.GetRequiredService<CharacterCreationBootstrapService>(),
            fixture.Provider.GetRequiredService<ICharacterCreationBootstrapActivationService>()),
        "Bootstrap legacy and activation aliases stopped sharing the canonical singleton.");
    Check.That(ReferenceEquals(fixture.Provider.GetRequiredService<CharacterCreationContactsService>(),
        fixture.Provider.GetRequiredService<ICharacterCreationContactsService>()), "Contacts alias duplicated the concrete service.");
    Check.That(ReferenceEquals(Bootstrap(fixture.Provider), Bootstrap(fixture.Provider))
        && ReferenceEquals(Contacts(fixture.Provider), Contacts(fixture.Provider)), "Companion registrations are not singletons.");
}

static IOwnerBoundCharacterCreationBootstrapService Bootstrap(IServiceProvider provider)
    => provider.GetRequiredService<IOwnerBoundCharacterCreationBootstrapService>();
static IOwnerBoundCharacterCreationContactsService Contacts(IServiceProvider provider)
    => provider.GetRequiredService<IOwnerBoundCharacterCreationContactsService>();
static CharacterCreationContactConfirmRequest Preview(IOwnerBoundCharacterCreationContactsService service, OwnerContextStamp stamp)
{
    var state = service.Load(stamp, new(CreationFixture.ContactWorkspace)).Value
        ?? throw new InvalidOperationException("Owner-bound Contacts fixture did not load.");
    var edit = new CharacterCreationContactEdit(CreationFixture.ContactId, Free: true);
    var preview = service.Preview(stamp, new(state.Binding, edit)).Value
        ?? throw new InvalidOperationException("Owner-bound Contacts fixture did not preview.");
    Check.That(preview.CanConfirm && preview.Blockers.Count == 0, "Typed Contacts fixture is not confirmable.");
    return new(state.Binding, edit, preview.PreviewDigest, "creation-owner-contact-free", ExplicitlyConfirmed: true);
}
static void NoTransientAuthority(string durableJson, OwnerContextStamp stamp)
    => Check.That(!durableJson.Contains(stamp.AuthorityInstanceId, StringComparison.Ordinal)
        && !durableJson.Contains("TransitionRevision", StringComparison.OrdinalIgnoreCase)
        && !durableJson.Contains("AuthorityInstanceId", StringComparison.OrdinalIgnoreCase),
        "Transient owner authority leaked into durable state or a historical receipt.");

public interface IAllCreationStore : ILegacyCreationStore,
    IOwnerScopedCharacterCreationBootstrapAtomicCreateCapability,
    IOwnerScopedWorkspaceAuxiliaryStateAtomicCommitCapability { }

public interface IBootstrapOnlyCreationStore : ILegacyCreationStore,
    IOwnerScopedCharacterCreationBootstrapAtomicCreateCapability { }

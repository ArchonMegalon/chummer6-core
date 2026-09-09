using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text.Json;
using Chummer.Application.Characters;
using Chummer.Application.Owners;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Rulesets;
using Chummer.Contracts.Workspaces;
using Chummer.Infrastructure.DependencyInjection;
using Chummer.Infrastructure.Workspaces;
using Microsoft.Extensions.DependencyInjection;

internal static class Check
{
    public static void That(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    public static string Root(string[] args)
    {
        int index = Array.IndexOf(args, "--core-root");
        if (index < 0 || index + 1 >= args.Length)
            throw new ArgumentException("Pass --core-root with the exact tested Core source root.");
        string root = Path.GetFullPath(args[index + 1]);
        That(File.Exists(Path.Combine(root, "Chummer.Application", "Chummer.Application.csproj")),
            "The explicit Core source root is invalid.");
        return root;
    }
}

internal sealed class CreationFixture : IDisposable
{
    public static readonly OwnerScope AccountA = new("creation-test-account-a");
    public static readonly OwnerScope AccountB = new("creation-test-account-b");
    public static readonly CharacterWorkspaceId ContactWorkspace = new("creation-owner-contact");
    public static readonly Guid ContactId = Guid.Parse("87796157-0366-4154-836a-034326e8e924");
    public static readonly OwnerScope[] Partitions = [OwnerScope.LocalSingleUser, AccountA, AccountB];
    private readonly List<ServiceProvider> _providers = [];
    private readonly string _coreRoot;
    public string StateDirectory { get; }
    public FileWorkspaceStore Store { get; }
    public ControlledOwner Authority { get; }
    public ServiceProvider Provider { get; }

    public CreationFixture(string coreRoot, OwnerScope owner)
    {
        _coreRoot = coreRoot;
        StateDirectory = Directory.CreateTempSubdirectory("chummer-creation-owner-admission-").FullName;
        Store = new FileWorkspaceStore(StateDirectory);
        Authority = new ControlledOwner(owner);
        Provider = AddProvider(Store, Authority);
    }

    public ServiceProvider AddProvider(
        IWorkspaceStore store,
        IOwnerContextAccessor owner,
        Action<string>? observeDomainRead = null)
    {
        var services = new ServiceCollection();
        services.AddChummerHeadlessCore(_coreRoot, _coreRoot);
        // These are the same actual instances consumed by every companion. Do not
        // register IOwnerContextLeaseAccessor separately from IOwnerContextAccessor.
        services.AddSingleton(store);
        services.AddSingleton(owner);
        if (observeDomainRead is not null)
        {
            DecorateSingleton<ICharacterSourceDataResolver>(services,
                inner => new ObservedSourceResolver(inner, observeDomainRead));
            DecorateSingleton<ICharacterCreationBootstrapActivationProjector>(services,
                inner => new ObservedActivationProjector(inner, observeDomainRead));
        }
        ServiceProvider provider = services.BuildServiceProvider();
        _providers.Add(provider);
        return provider;
    }

    private static void DecorateSingleton<T>(IServiceCollection services, Func<T, T> decorate)
        where T : class
    {
        ServiceDescriptor descriptor = services.Last(item => item.ServiceType == typeof(T));
        Check.That(descriptor.Lifetime == ServiceLifetime.Singleton,
            "Domain observation must preserve the real singleton registration.");
        services.Remove(descriptor);
        services.AddSingleton<T>(provider => decorate(
            descriptor.ImplementationInstance as T
            ?? descriptor.ImplementationFactory?.Invoke(provider) as T
            ?? (T)ActivatorUtilities.CreateInstance(provider, descriptor.ImplementationType!)));
    }

    public static CharacterCreationBootstrapRequest Request() => new(
        CharacterCreationBootstrapSchemas.RequestV1,
        CharacterCreationBootstrapStages.AwaitingFoundationSelection,
        RulesetDefaults.Sr5,
        "Owner-bound pending runner",
        "No implicit partition",
        CharacterCreationBuildMethods.Priority,
        CharacterCreationBootstrapProfiles.PrioritySettingsProfileId);

    public void SeedContacts()
    {
        foreach (OwnerScope partition in Partitions)
            Check.That((partition.IsLocalSingleUser
                    ? Store.CreateWorkspaceDocument(ContactWorkspace, ContactDocument())
                    : Store.CreateWorkspaceDocument(partition, ContactWorkspace, ContactDocument())).Success,
                "Real FileWorkspaceStore contact fixture creation failed.");
    }

    // The store intentionally rejects the reserved local value in its scoped
    // APIs, including the trusted local identity. These controls use the
    // explicit local store seam only for that actual trusted identity.
    public WorkspaceStoreReadResult Read(OwnerScope owner, CharacterWorkspaceId id)
        => owner.IsLocalSingleUser ? Store.Get(id) : Store.Get(owner, id);

    public string Snapshot(OwnerScope owner, CharacterWorkspaceId id)
    {
        WorkspaceStoreReadResult read = Read(owner, id);
        Check.That(read.Success, "Expected exact owner-scoped workspace was not found.");
        return JsonSerializer.Serialize(read.Value);
    }

    public int Count(OwnerScope owner)
        => (owner.IsLocalSingleUser ? Store.List() : Store.List(owner)).Count;

    public void Dispose()
    {
        foreach (ServiceProvider provider in _providers) provider.Dispose();
        // This exact path is a fixture-owned, exclusively created temporary directory.
        Directory.Delete(StateDirectory, recursive: true);
    }

    public static WorkspaceDocument ContactDocument() => new($"""
        <character>
          <created>False</created><gameedition>SR5</gameedition><settings>default.xml</settings>
          <buildmethod>Priority</buildmethod><contactpoints>15</contactpoints><improvements />
          <contacts>
            <contact>
              <guid>{ContactId:D}</guid><name>Fixer</name><role>Broker</role><location>Vienna</location>
              <notes>trusted</notes><extra>Neon</extra><metatype>Human</metatype><gender>Female</gender><age>38</age>
              <contacttype>Professional</contacttype><preferredpayment>Nuyen</preferredpayment>
              <hobbiesvice>Chess</hobbiesvice><personallife>Private</personallife><groupname />
              <connection>3</connection><loyalty>2</loyalty><group>False</group><free>False</free>
              <family>True</family><blackmail>True</blackmail><file /><relative /><type>Contact</type>
              <chummercomplete><sentinel>unchanged</sentinel></chummercomplete>
            </contact>
            <contact>
              <guid>11111111-2222-4333-8444-555555555555</guid><name>Sibling</name>
              <connection>4</connection><loyalty>4</loyalty><group>True</group><free>False</free>
              <family>False</family><blackmail>False</blackmail><type>Contact</type>
            </contact>
          </contacts><root-sentinel>keep</root-sentinel>
        </character>
        """, RulesetDefaults.Sr5, WorkspaceDocumentFormat.Chum5Xml);
}

internal sealed class ControlledOwner(OwnerScope initial) : IOwnerContextLeaseAccessor
{
    private readonly object _gate = new();
    private readonly string _issuer = Guid.NewGuid().ToString("N");
    private OwnerScope _owner = initial;
    private long _revision;
    public int ActiveLeases { get; private set; }
    public int LeaseThreadId { get; private set; }
    public int SuccessfulAdmissions { get; private set; }
    public OwnerScope Current { get { lock (_gate) return _owner; } }

    public OwnerContextStamp Capture()
    {
        lock (_gate) return new(_owner, _issuer, _revision);
    }

    public void Transition(OwnerScope owner)
    {
        lock (_gate)
        {
            Check.That(ActiveLeases == 0, "The test owner cannot transition reentrantly through a held lease.");
            _revision = checked(_revision + 1);
            _owner = owner;
        }
    }

    public bool TryAcquire(OwnerContextStamp expected, [NotNullWhen(true)] out IOwnerContextLease? lease)
    {
        Monitor.Enter(_gate);
        if (!expected.IsValid || expected != new OwnerContextStamp(_owner, _issuer, _revision))
        {
            Monitor.Exit(_gate);
            lease = null;
            return false;
        }
        ActiveLeases++;
        SuccessfulAdmissions++;
        LeaseThreadId = Environment.CurrentManagedThreadId;
        lease = new Lease(this, expected);
        return true;
    }

    public bool CanEnterTransitionGateFromOtherThread()
    {
        // Run only while the tested synchronous service is inside its store call.
        // TryEnter is an immediate lock observation, not a sleep-based race assertion.
        Task<bool> observation = Task.Run(() =>
        {
            bool entered = Monitor.TryEnter(_gate);
            if (entered) Monitor.Exit(_gate);
            return entered;
        });
        Check.That(observation.Wait(TimeSpan.FromSeconds(5)), "Bounded exclusion observation timed out.");
        return observation.Result;
    }

    private sealed class Lease(ControlledOwner authority, OwnerContextStamp stamp) : IOwnerContextLease
    {
        private bool _disposed;
        public OwnerContextStamp Stamp
        {
            get { ObjectDisposedException.ThrowIf(_disposed, this); return stamp; }
        }
        public void Dispose()
        {
            if (_disposed) return;
            Check.That(authority.LeaseThreadId == Environment.CurrentManagedThreadId,
                "The owner lease crossed a thread boundary.");
            _disposed = true;
            authority.ActiveLeases--;
            Monitor.Exit(authority._gate);
        }
    }
}

internal sealed class LegacyValueOnlyOwner(OwnerScope current) : IOwnerContextAccessor
{
    public OwnerScope Current => current;
}

internal sealed class ObservedSourceResolver(
    ICharacterSourceDataResolver inner,
    Action<string> observe) : ICharacterSourceDataResolver
{
    public ICharacterSourceDataContext? TryCreateContext(string characterXml)
    {
        observe("SourceResolver.TryCreateContext");
        ICharacterSourceDataContext? context = inner.TryCreateContext(characterXml);
        return context is null ? null : SourceContextProxy.Wrap(context, observe);
    }
}

public class SourceContextProxy : DispatchProxy
{
    public ICharacterSourceDataContext Inner { get; set; } = null!;
    public Action<string> Observe { get; set; } = null!;

    public static ICharacterSourceDataContext Wrap(ICharacterSourceDataContext inner, Action<string> observe)
    {
        ICharacterSourceDataContext result = Create<ICharacterSourceDataContext, SourceContextProxy>();
        var proxy = (SourceContextProxy)(object)result;
        proxy.Inner = inner;
        proxy.Observe = observe;
        return result;
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        MethodInfo method = targetMethod ?? throw new InvalidOperationException("Missing source context method.");
        Observe("SourceContext." + method.Name);
        try { return method.Invoke(Inner, args); }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
    }
}

internal sealed class ObservedActivationProjector(
    ICharacterCreationBootstrapActivationProjector inner,
    Action<string> observe) : ICharacterCreationBootstrapActivationProjector
{
    public CharacterCreationInitialProjection Project(
        WorkspaceStoredDocument workspace,
        CharacterCreationBootstrapSourceSnapshot sourceSnapshot)
    {
        observe("ActivationProjector.Project");
        return inner.Project(workspace, sourceSnapshot);
    }

    public bool IsCurrent(
        CharacterCreationInitialProjection projection,
        ICharacterSourceDataContext sourceContext,
        string characterXml)
    {
        observe("ActivationProjector.IsCurrent");
        return inner.IsCurrent(projection, sourceContext, characterXml);
    }
}

// Public interface is needed by DispatchProxy's generated test-only implementation.
public interface ILegacyCreationStore : IWorkspaceStore,
    IWorkspaceAuxiliaryStateAtomicCommitCapability,
    ICharacterCreationBootstrapAtomicCreateCapability { }

public class StoreProxy : DispatchProxy
{
    public FileWorkspaceStore Inner { get; set; } = null!;
    public Action<MethodInfo, object?[]?>? BeforeCall { get; set; }
    public bool DisableScopedCapabilities { get; set; }
    public int Reads { get; private set; }
    public int Mutations { get; private set; }
    public void ResetCounts() { Reads = 0; Mutations = 0; }

    public static T Wrap<T>(FileWorkspaceStore inner, out StoreProxy proxy) where T : class
    {
        T result = Create<T, StoreProxy>();
        proxy = (StoreProxy)(object)result;
        proxy.Inner = inner;
        return result;
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        MethodInfo method = targetMethod ?? throw new InvalidOperationException("Missing test proxy method.");
        if (method.Name == "Get" || method.Name == "List") Reads++;
        if (method.Name.StartsWith("Create", StringComparison.Ordinal)
            || method.Name.StartsWith("Replace", StringComparison.Ordinal)
            || method.Name is "Delete" or "SaveCheckpoint") Mutations++;
        if (DisableScopedCapabilities && method.Name.StartsWith("get_SupportsOwnerScoped", StringComparison.Ordinal))
            return false;
        BeforeCall?.Invoke(method, args);
        try { return method.Invoke(Inner, args); }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
    }
}

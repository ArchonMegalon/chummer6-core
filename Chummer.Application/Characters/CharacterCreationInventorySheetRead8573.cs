using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

// Only the concrete retained Creation resolver's synchronous scopes implement
// this seam. Public adapters and imported current-environment reads cannot opt in.
internal interface ICharacterCreationRetainedSheetScope8573
{
    bool IsRetainedCreationSheetScope8573();
}

// Ordinary retained read wrappers do not promise a synchronous lifetime. Only
// their concrete factory may open the checked nested scope for this comparison.
internal interface ICharacterCreationRetainedSheetScopeFactory8573
{
    ICharacterSourceDataResolverOperationScope? CreateRetainedCreationSheetScope8573();
}

// One original-inventory comparison owns this object and disposes it before its
// source scope. Retain only a successful calculation's immutable digest, never
// a mutable sheet, save capability, or observation shared with another operation.
internal sealed class CharacterCreationInventorySheetRead8573 : IDisposable
{
    private readonly ICharacterSourceDataResolver resolver;
    private readonly CharacterCreationPriorityGrantAuthority3563? priorityGrants;
    private readonly int thread = Environment.CurrentManagedThreadId;
    private readonly Dictionary<string, Entry> entries = new(StringComparer.Ordinal);
    private bool retired;
    private bool invalidated;

    private sealed record Entry(ICharacterSourceDataContext Context, string Digest);

    internal CharacterCreationInventorySheetRead8573(ICharacterSourceDataResolver resolver,
        CharacterCreationPriorityGrantAuthority3563? priorityGrants)
    { this.resolver = resolver; this.priorityGrants = priorityGrants; }

    internal bool TryRead(string xml, ICharacterSourceDataResolver requested,
        CharacterCreationPriorityGrantAuthority3563? grants, out string? digest)
    {
        digest = null;
        if (retired || invalidated || Environment.CurrentManagedThreadId != thread
            || !ReferenceEquals(requested, resolver) || !ReferenceEquals(grants, priorityGrants)) return false;
        // No cache is installed for live, imported, or public resolver paths.
        if (priorityGrants?.IsImported5693 == true
            || resolver is not ICharacterCreationRetainedSheetScope8573 scope)
            return TryReadCold(xml, resolver, priorityGrants, out digest);

        invalidated = true; // Failed admission or exceptions permanently retire reuse.
        if (!scope.IsRetainedCreationSheetScope8573())
        {
            bool cold = TryReadCold(xml, resolver, priorityGrants, out digest);
            invalidated = !cold;
            return cold;
        }
        var context = resolver.TryCreateContext(xml);
        if (context is not IRetainedCurrentSourceSnapshot8110 retained
            || !retained.IsCurrentRetainedSourceSnapshot8110()) return false;
        bool hit = entries.TryGetValue(xml, out var entry);
        string? calculated;
        if (hit)
        {
            if (!ReferenceEquals(entry!.Context, context)) return false;
            calculated = entry.Digest;
        }
        else if (!TryReadCold(xml, resolver, priorityGrants, out calculated)) return false;

        // Fresh source membership/byte-owner checks also follow the calculation
        // or hit. The enclosing scope independently rechecks on disposal.
        if (!scope.IsRetainedCreationSheetScope8573()
            || !ReferenceEquals(context, resolver.TryCreateContext(xml))
            || !retained.IsCurrentRetainedSourceSnapshot8110()) return false;
        if (!hit && entries.Count < 2) entries.Add(xml, new(context, calculated!));
        invalidated = false;
        digest = calculated;
        if (hit) CharacterCreationCompletionCost3340.Log.Checkpoint(8573);
        return true;
    }

    internal static bool TryReadCold(string xml, ICharacterSourceDataResolver resolver,
        CharacterCreationPriorityGrantAuthority3563? grants, out string? digest)
    {
        var sheet = CharacterSheetDerivedQuery.ParseWithPriorityGrants3563(xml, resolver, grants);
        digest = sheet.UnavailableReason is null && CharacterCreationFinalizationDigest.IsCanonical(sheet.AuthorityDigest)
            ? sheet.AuthorityDigest : null;
        return digest is not null;
    }

    public void Dispose() { retired = true; entries.Clear(); }
}

using System.Xml.Linq;
using System.Text.Json.Serialization;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

// This observer runs inside the existing compiler. It never interprets a rule,
// supplies an outcome, or survives a failed compilation. Nested invocations
// have separate ledgers, including on asynchronous execution contexts.
internal sealed class CharacterCreationCompiledSourceTrace : IDisposable
{
    private static readonly AsyncLocal<CharacterCreationCompiledSourceTrace?> Current = new();
    private readonly CharacterCreationCompiledSourceTrace? previous;
    private readonly List<CharacterCreationCompiledSourceEffect> effects = [];
    private bool disposed;

    private CharacterCreationCompiledSourceTrace()
    {
        previous = Current.Value;
        Current.Value = this;
    }

    internal static CharacterCreationCompiledSourceTrace Begin() => new();

    internal static void Observe(CharacterCreationMagicResonanceOptionFinalizationSource source,
        XElement consumedSource, XElement compiledEffect, string compilerSeed)
    {
        if (Current.Value is not { disposed: false } trace) return;
        string? scope = source.Identity.Kind switch
        {
            CharacterCreationMagicResonanceKinds.Tradition => "traditions",
            CharacterCreationMagicResonanceKinds.Stream => "streams",
            CharacterCreationMagicResonanceKinds.AdeptPower => "powers",
            CharacterCreationMagicResonanceKinds.Spell => "spells",
            CharacterCreationMagicResonanceKinds.ComplexForm => "complexforms",
            _ => null
        };
        if (scope is null || trace.effects.Count >= 8192
            || trace.effects.Any(row => row.Scope == scope && row.SourceId == source.Identity.SourceId)
            || consumedSource.DescendantsAndSelf().Any(node => node.HasElements
                && node.Nodes().OfType<XText>().Any(text => !string.IsNullOrWhiteSpace(text.Value))))
            throw new InvalidDataException("Ambiguous compiled rule-source occurrence.");
        // OptionSource has validated the complete supported payload. Observe
        // only after ApplyTradition/ApplyOptions emitted its actual saved node.
        trace.effects.Add(new(scope, source,
            consumedSource.ToString(SaveOptions.DisableFormatting),
            compiledEffect.ToString(SaveOptions.DisableFormatting),
            compilerSeed));
    }

    internal IReadOnlyList<CharacterCreationCompiledSourceEffect> Finish()
    {
        if (disposed || !ReferenceEquals(Current.Value, this))
            throw new InvalidOperationException("Source compiler observation is not active.");
        return Array.AsReadOnly(effects.ToArray());
    }

    internal static void ObserveLoadout(IReadOnlyList<CharacterCreationLoadoutLine> lines,
        IReadOnlyList<XElement> weapons, IReadOnlyList<XElement> armor, string compilerSeed)
    {
        if (Current.Value is not { disposed: false } trace) return;
        foreach (var line in lines.OrderBy(row => row.OptionId, StringComparer.Ordinal))
        {
            string scope = line.Kind == CharacterCreationLoadoutKinds.Weapon ? "weapons" : "armor";
            var owners = (line.Kind == CharacterCreationLoadoutKinds.Weapon ? weapons : armor)
                .Where(node => node.Element("sourceid")?.Value == line.SourceId.ToString("D")).ToArray();
            if (!CharacterCreationLoadoutRules.IsValidLine(line) || owners.Length != line.Quantity
                || trace.effects.Count >= 8192
                || trace.effects.Any(row => row.Scope == scope && row.SourceId == line.SourceId.ToString("D")))
                throw new InvalidDataException("Ambiguous compiled loadout source occurrence.");
            // Observe only the actual emitted purchase owners, after the complete
            // canonical projector succeeded. The caller still verifies that no
            // retained or additional owner is omitted from source coverage.
            trace.effects.Add(new(line, owners, compilerSeed));
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        if (!ReferenceEquals(Current.Value, this))
            throw new InvalidOperationException("Source compiler observation scope mismatch.");
        disposed = true;
        Current.Value = previous;
    }
}

// Engine-local, in-process evidence. No public constructor or deserialization
// path can turn copied source/effect hashes into a compiler invocation.
public sealed class CharacterCreationCompiledSourceEffect
{
    internal CharacterCreationCompiledSourceEffect(string scope,
        CharacterCreationMagicResonanceOptionFinalizationSource source,
        string sourceXml, string effectXml, string compilerSeed)
    {
        Scope = scope;
        Source = source with { SourceAnchorIds = Array.AsReadOnly(source.SourceAnchorIds.ToArray()) };
        SourceXml = sourceXml;
        EffectXml = effectXml;
        CompilerSeed = compilerSeed;
    }

    internal CharacterCreationCompiledSourceEffect(CharacterCreationLoadoutLine line,
        IReadOnlyList<XElement> owners, string compilerSeed)
    {
        LoadoutLine = line with { SourceAnchorIds = Array.AsReadOnly(line.SourceAnchorIds.ToArray()) };
        LoadoutInstances = new(line.OptionId, Array.AsReadOnly(owners.Select(node => Guid.Parse(node.Element("guid")!.Value)).ToArray()));
        Scope = line.Kind == CharacterCreationLoadoutKinds.Weapon ? "weapons" : "armor";
        SourceXml = XElement.Parse(line.SourceNodeXml).ToString(SaveOptions.DisableFormatting);
        SourceCategoryXml = XElement.Parse(line.SourceCategoryXml).ToString(SaveOptions.DisableFormatting);
        EffectXml = new XElement(line.Kind == CharacterCreationLoadoutKinds.Weapon ? "weapons" : "armors",
            owners.Select(node => new XElement(node))).ToString(SaveOptions.DisableFormatting);
        CompilerSeed = compilerSeed;
    }

    internal CharacterCreationMagicResonanceOptionFinalizationSource Source { get; } = null!;
    internal CharacterCreationLoadoutLine? LoadoutLine { get; }
    internal CharacterCreationLoadoutInstance? LoadoutInstances { get; }
    internal string CompilerSeed { get; }
    [JsonIgnore] public string Scope { get; }
    [JsonIgnore] public string SourceId => LoadoutLine?.SourceId.ToString("D") ?? Source.Identity.SourceId;
    [JsonIgnore] public string SourceXml { get; }
    [JsonIgnore] public string? SourceCategoryXml { get; }
    [JsonIgnore] public string? SourceCategory => LoadoutLine?.Category;
    [JsonIgnore] public string EffectXml { get; }
    [JsonIgnore] public string ProjectionDigest => LoadoutLine is null ? Source.ProjectionDigest
        : CharacterCreationFinalizationDigest.Compute(new { LoadoutLine, LoadoutInstances, CompilerSeed, SourceXml, SourceCategoryXml, EffectXml });
    public override string ToString() => nameof(CharacterCreationCompiledSourceEffect);
}

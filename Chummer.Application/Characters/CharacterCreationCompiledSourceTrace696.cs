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

    internal CharacterCreationMagicResonanceOptionFinalizationSource Source { get; }
    internal string CompilerSeed { get; }
    [JsonIgnore] public string Scope { get; }
    [JsonIgnore] public string SourceId => Source.Identity.SourceId;
    [JsonIgnore] public string SourceXml { get; }
    [JsonIgnore] public string EffectXml { get; }
    [JsonIgnore] public string ProjectionDigest => Source.ProjectionDigest;
    public override string ToString() => nameof(CharacterCreationCompiledSourceEffect);
}

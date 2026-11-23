using System.Xml.Linq;
using Chummer.Application.Workspaces;

namespace Chummer.Application.Characters;

public static partial class CharacterCurrentRuleEnvironmentConsumers
{
    // Only a synchronous retained learning evaluation constructs this object.
    // It is never returned in a command, persisted, or reused after the store lease.
    internal sealed class LearningAuthority5703
    {
        private readonly CharacterSheetDerivedQuery.ImportedCareerSheet5699 read;
        private readonly ICharacterSourceDataContext source;
        internal CharacterCurrentRuleEnvironmentContext Current => read.Current;
        internal CharacterSheetDerivedQuery.ImportedCareerSheet5699 Retained7552 => read;
        internal XElement Graph { get; }
        internal CharacterCareerMagicEvaluation? Magic { get; }
        internal CharacterCareerResonanceEvaluation? Resonance { get; }
        internal LearningAuthority5703(CharacterSheetDerivedQuery.ImportedCareerSheet5699 read,
            ICharacterSourceDataContext source, XElement graph, CharacterCareerMagicEvaluation? magic,
            CharacterCareerResonanceEvaluation? resonance)
            => (this.read, this.source, Graph, Magic, Resonance) = (read, source, graph, magic, resonance);
        internal bool Matches(WorkspaceStoredDocument saved, ICharacterSourceDataContext candidate)
            => ReferenceEquals(saved, Current.Current) && ReferenceEquals(source, candidate)
                && read.IsCurrent && Current.IsUnchanged;
    }

    internal static LearningAuthority5703? CreateLearningAuthority5703(
        WorkspaceStoredDocument saved, CharacterCurrentRuleEnvironmentContext? current,
        ICharacterSourceDataContext source, bool complexForm)
    {
        if (current is null) return null;
        RequireTalents1949(ReferenceEquals(saved, current.Current) && current.IsUnchanged
            && UsesOwnedPriorityCareer9569(current) && current.VerifyRetainedArchive(),
            "learning-imported-current-context-mismatch");
        var effects = CompileCurrentCareerEffects1960(current, source, []);
        var read = effects?.Imported?.Read;
        RequireTalents1949(read is { IsCurrent: true } && ReferenceEquals(read.Current, current)
            && read.HasPrioritySelection9569, "learning-imported-owner-closure-unavailable");
        var graph = new XElement(effects!.Graph);
        var settings = CurrentTalentsSettings1949(graph, source, out _);
        return new(read!, source, graph,
            complexForm ? null : CurrentMagic5699(current, graph, settings, source, read),
            complexForm ? CurrentResonance5699(current, graph, source, read) : null);
    }
}

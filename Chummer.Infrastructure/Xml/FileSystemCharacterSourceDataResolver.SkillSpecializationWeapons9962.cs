using System.Xml.Linq;
using Chummer.Application.Characters;

namespace Chummer.Infrastructure.Xml;

public sealed partial class FileSystemCharacterSourceDataResolver
{
    private sealed partial class SourceDataContext
    {
        private SkillSpecializationWeapons9962? _skillSpecializationWeapons9962;

        internal IDisposable? BeginSkillSpecializationWeapons9962(CharacterCurrentRuleEnvironmentContext current)
        {
            if (_skillSpecializationWeapons9962 is not null)
                throw new InvalidOperationException("A skill specialization read is already active.");
            // Only the concrete issuer's immutable source and exact synchronous
            // evaluation can lend this scratch. Live/public/historical reads replay.
            if (!_sourceInputs.HasImmutableRetainedInputs7956 || !UsesCurrentCatalogSemantics
                || _readingSourcebooks800 || current.RetainedEvaluation2764 is not { } evaluation
                || !evaluation.Allows(current, this)) return null;
            return _skillSpecializationWeapons9962 = new(this, current, evaluation);
        }

        private XElement[]? ResolveSkillSpecializationWeapons9962()
            => _skillSpecializationWeapons9962 is { } scratch && !_readingSourcebooks800
                ? scratch.Read()
                : TryEnumerateTargets("weapons.xml", ["weapons"], "weapon", out var weapons) ? weapons : null;

        private sealed class SkillSpecializationWeapons9962(
            SourceDataContext source,
            CharacterCurrentRuleEnvironmentContext current,
            CharacterCurrentRuleEnvironmentConsumers.RetainedEvaluation2764 evaluation) : IDisposable
        {
            private const int MaximumRetainedRows = 4096;
            private XElement[]? weapons;
            private bool retired;

            private bool IsCurrent()
                => !retired && ReferenceEquals(source._skillSpecializationWeapons9962, this)
                    && ReferenceEquals(current.RetainedEvaluation2764, evaluation)
                    && evaluation.Allows(current, source);

            internal XElement[]? Read()
            {
                if (!IsCurrent()) { retired = true; weapons = null; return null; }
                if (weapons is not null) return weapons;
                if (!source.TryEnumerateTargets("weapons.xml", ["weapons"], "weapon", out var observed))
                    return null;
                if (!IsCurrent()) { retired = true; weapons = null; return null; }
                // Keep one complete, amended, sourcebook-filtered enumeration.
                // Only the private specialization projector reads these trees;
                // it serializes rows but never mutates or returns them. Every
                // skill still resolves its source and computes all options anew.
                // Large inputs use ordinary enumeration, never a partial catalog.
                if (observed.Length <= MaximumRetainedRows) weapons = observed;
                return observed;
            }

            public void Dispose()
            {
                retired = true;
                weapons = null;
                if (ReferenceEquals(source._skillSpecializationWeapons9962, this))
                    source._skillSpecializationWeapons9962 = null;
            }
        }
    }
}

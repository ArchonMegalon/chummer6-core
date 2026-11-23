using System.Text.Json;
using Chummer.Application.Owners;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

// One synchronous Magic read only. This is neither a commit admission nor a resolver
// registration: public Review and Commit always use their original live authority.
internal sealed class CharacterCareerPowerReadEvaluation
{
    private readonly OwnerContextStamp owner;
    private readonly WorkspaceStoredDocument saved;
    private readonly string runtime;
    private readonly CharacterCareerMagicBinding binding;
    private readonly CapturedSources sources;
    private readonly OwnerManagedCareerPowerAuthorityResolver evaluator;
    private bool completed;

    internal CharacterCareerPowerReadEvaluation(OwnerContextStamp owner, WorkspaceStoredDocument saved,
        string runtime, CharacterCareerMagicBinding binding, ICharacterSourceDataContext retained)
    {
        this.owner = owner;
        this.saved = saved;
        this.runtime = runtime;
        this.binding = binding;
        sources = new(retained);
        evaluator = new(sources);
    }

    internal ICharacterSourceDataContext Context => sources;

    internal bool Matches(OwnerContextStamp currentOwner, WorkspaceStoredDocument current, string currentRuntime) =>
        owner == currentOwner && runtime == currentRuntime
        && CharacterCareerMagicReadBinding.MatchesSaved(binding, currentOwner.Owner, current)
        && CharacterCareerPowerTransaction.IsValidHistory(current)
        && CharacterCareerPowerGovernance.Allows(current);

    internal CharacterCareerPowerReview Review(CharacterCareerPowerCommand command)
    {
        if (completed || command.ReadBinding != binding
            || !CharacterCareerMagicReadBinding.MatchesOwner(binding, owner))
            return new(null, "power_read_binding_changed");
        // OpenDetailed still performs the canonical graph build, command, history,
        // governance, cap, cost, effect and plan checks for every candidate rating.
        using var admission = evaluator.OpenDetailed(owner.Owner, saved, command, runtime, out var blocker);
        return admission is null ? new(null, blocker) : new(admission.Plan, null);
    }

    internal bool Complete(ICharacterSourceDataContext fresh)
    {
        if (completed) return false;
        completed = true;
        // Re-resolve every consumed source answer, including unsuccessful queries.
        // The retained context keeps observed ABA invalidation; the fresh context
        // detects newly added/removed overlay members and selected-profile changes.
        return sources.IsCurrent(fresh);
    }

    private sealed class CapturedSources(ICharacterSourceDataContext retained)
        : ICharacterSourceDataResolver, ICharacterSourceDataContext, ICharacterFinalizedModuleOwnershipSource
    {
        private sealed record Answer<T>(bool Success, T Value);
        private sealed record SourceXml(string Xml, string State);
        private interface ICaptured
        {
            bool Matches(ICharacterSourceDataContext context);
        }
        private sealed class Captured<T>(T value, Func<ICharacterSourceDataContext, T> read) : ICaptured
        {
            internal T Value => value;
            private readonly string signature = JsonSerializer.Serialize(value);
            public bool Matches(ICharacterSourceDataContext context) =>
                signature == JsonSerializer.Serialize(read(context));
        }

        private readonly Dictionary<(string Kind, string Id, string Name), ICaptured> answers = [];

        private T Read<T>((string Kind, string Id, string Name) key, Func<ICharacterSourceDataContext, T> read)
        {
            if (!answers.TryGetValue(key, out var captured))
            {
                captured = new Captured<T>(read(retained), read);
                answers.Add(key, captured);
            }
            return ((Captured<T>)captured).Value;
        }

        internal bool IsCurrent(ICharacterSourceDataContext fresh) =>
            answers.Values.All(answer => answer.Matches(retained))
            && answers.Values.All(answer => answer.Matches(fresh))
            // Dependent reads can invalidate settings/source authority after its
            // earlier replay. Close both contexts against the captured authority last.
            && answers.TryGetValue(("settings", "", ""), out var settings)
            && settings.Matches(retained)
            && settings.Matches(fresh);

        public ICharacterSourceDataContext TryCreateContext(string characterXml) => this;

        public bool TryObserveFinalizedModules(WorkspaceStoredDocument saved,
            out CharacterFinalizedLifeModuleOwnership? ownership)
        {
            string binding = CharacterCreationFinalizationDigest.Compute(new
            {
                saved.Id, saved.ContentRevision, saved.SavedRevision, saved.Document.AuxiliaryStateDigest,
                Character = CharacterCreationFoundationDraftLedgerIntegrity.ComputeRawCharacterXmlDigest(saved.Document.Content)
            });
            var answer = Read(("finalized-modules", saved.Id.Value, binding), context =>
            {
                bool ok = CharacterFinalizedLifeModuleOwnership.TryObserve(saved, context, out var proof);
                return new Answer<CharacterFinalizedLifeModuleOwnership?>(ok, proof);
            });
            ownership = answer.Value;
            return answer.Success;
        }

        public bool TryResolveCareerSettingsProfile(out string settingsXml, out string authorityState)
        {
            var answer = Read(("settings", "", ""), context =>
            {
                bool ok = context.TryResolveCareerSettingsProfile(out var xml, out var state);
                return new Answer<SourceXml>(ok, new(xml, state));
            });
            settingsXml = answer.Value.Xml;
            authorityState = answer.Value.State;
            return answer.Success;
        }

        public bool TryResolveCareerQualityMetatype(out string sourceXml, out string authorityState)
        {
            var answer = Read(("metatype", "", ""), context =>
            {
                bool ok = context.TryResolveCareerQualityMetatype(out var xml, out var state);
                return new Answer<SourceXml>(ok, new(xml, state));
            });
            sourceXml = answer.Value.Xml;
            authorityState = answer.Value.State;
            return answer.Success;
        }

        public bool TryResolveCareerPowerCatalog(out IReadOnlyList<CharacterCareerPowerSource> catalog)
        {
            var answer = Read(("catalog", "", ""), context =>
            {
                bool ok = context.TryResolveCareerPowerCatalog(out var rows);
                return new Answer<IReadOnlyList<CharacterCareerPowerSource>>(ok, Array.AsReadOnly(rows.ToArray()));
            });
            catalog = answer.Value;
            return answer.Success;
        }

        public bool TryResolveCareerPowerGrantSource(string kind, Guid sourceId,
            out CharacterCareerPowerGrantSource? source)
        {
            var answer = Read(("grant:" + kind, sourceId.ToString("D"), ""), context =>
            {
                bool ok = context.TryResolveCareerPowerGrantSource(kind, sourceId, out var row);
                return new Answer<CharacterCareerPowerGrantSource?>(ok, row);
            });
            source = answer.Value;
            return answer.Success;
        }

        public bool TryResolveCyberwareCommerceSource(string sourceId, string name, string improvementSource,
            out CharacterCyberwareCommerceSource source)
        {
            var answer = Read(("ware:" + improvementSource, sourceId, name), context =>
            {
                bool ok = context.TryResolveCyberwareCommerceSource(sourceId, name, improvementSource, out var row);
                return new Answer<CharacterCyberwareCommerceSource>(ok, row with
                {
                    BannedGrades = Array.AsReadOnly(row.BannedGrades.ToArray()),
                    Grades = Array.AsReadOnly(row.Grades.ToArray())
                });
            });
            source = answer.Value;
            return answer.Success;
        }

        // Not consumed by the Power/Magic graph. New dependencies must explicitly
        // join the captured/fenced set rather than silently consult another scope.
        public bool TryResolveCyberwareGradeDeviceRating(string gradeName, string improvementSource, out int deviceRating)
        { deviceRating = 0; return false; }
        public bool TryResolveVehicleModBonuses(string sourceId, string name, out CharacterVehicleModSourceBonuses bonuses)
        { bonuses = CharacterVehicleModSourceBonuses.Empty; return false; }
    }
}

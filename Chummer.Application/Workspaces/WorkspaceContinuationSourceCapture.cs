using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Chummer.Application.Characters;
using Chummer.Application.LifeModules;
using Chummer.Contracts.Characters;
using Chummer.Contracts.LifeModules;

namespace Chummer.Application.Workspaces;

/// <summary>
/// Current, source-only inputs for evaluating one continuation candidate. This
/// capture is neither historical execution evidence nor permission to persist.
/// Every returned DTO is an independent copy; no query can reach live sources.
/// </summary>
internal sealed class WorkspaceContinuationSourceCapture
{
    private readonly string _characterXml;
    private readonly Captured<CharacterCreationSourceProfileAuthority> _profile;
    private readonly Captured<CharacterCreationMetatypeCatalogAuthority> _metatypes;
    private readonly Captured<CharacterCreationPrerequisiteAuthority> _prerequisite;
    private readonly Captured<CharacterCreationSkillsAuthority> _skills;
    private readonly Captured<CharacterCreationQualitiesAuthority> _qualities;
    private readonly Captured<CharacterCreationMagicResonanceAuthority> _magic;
    private readonly Captured<CharacterCreationResourcesAuthority> _resources;
    private readonly Captured<CharacterCreationGearAuthority> _gear;
    private readonly Captured<CharacterCreationLifestylesAuthority> _lifestyles;
    private readonly Captured<ReputationSource> _reputation;
    private readonly FrozenLifeModulesCatalog _lifeModules;

    private WorkspaceContinuationSourceCapture(ICharacterSourceDataContext source,
        string characterXml, ILifeModulesCatalogService lifeModules)
    {
        _characterXml = characterXml;
        _profile = Capture(source.TryResolveCreationSourceProfile, CharacterCreationSourceProfileAuthority.Unavailable);
        _metatypes = Capture(source.TryResolveCreationMetatypeCatalog, CharacterCreationMetatypeCatalogAuthority.Unavailable);
        _prerequisite = Capture(source.TryResolveCreationPrerequisiteAuthority, CharacterCreationPrerequisiteAuthority.Unavailable);
        _skills = Capture(source.TryResolveCreationSkillsAuthority, CharacterCreationSkillsAuthority.Unavailable);
        _qualities = Capture(source.TryResolveCreationQualitiesAuthority, CharacterCreationQualitiesAuthority.Unavailable);
        _magic = Capture(source.TryResolveCreationMagicResonanceAuthority, CharacterCreationMagicResonanceAuthority.Unavailable);
        _resources = Capture(source.TryResolveCreationResourcesAuthority, CharacterCreationResourcesAuthority.Unavailable);
        _gear = Capture(source.TryResolveCreationGearAuthority, CharacterCreationGearAuthority.Unavailable);
        _lifestyles = Capture(source.TryResolveCreationLifestylesAuthority, CharacterCreationLifestylesAuthority.Unavailable);
        _reputation = Capture((out ReputationSource result) =>
        {
            bool resolved = source.TryResolveCareerReputationSettings(out var settings, out string rawRuleState);
            result = new(settings, rawRuleState);
            if (resolved && (settings is null || string.IsNullOrEmpty(rawRuleState)))
                throw new InvalidDataException("Resolved reputation source is incomplete.");
            return resolved;
        }, new ReputationSource(new(false), string.Empty));
        _profile.Read(out var profile);
        _lifeModules = FrozenLifeModulesCatalog.Capture(lifeModules, _profile.Resolved, profile.EnabledSourcebooks);
        Digest = CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(new
        {
            Semantics = "chummer.workspace-continuation-source-capture/v1",
            RawCharacterXmlDigest = CharacterCreationFoundationDraftLedgerIntegrity.ComputeRawCharacterXmlDigest(characterXml),
            Profile = _profile, Metatypes = _metatypes, Prerequisite = _prerequisite,
            Skills = _skills, Qualities = _qualities, MagicResonance = _magic,
            Resources = _resources, Gear = _gear, Lifestyles = _lifestyles,
            Reputation = _reputation, LifeModules = _lifeModules.CapturedState
        });
    }

    public string Digest { get; }
    public ILifeModulesCatalogService LifeModules => _lifeModules;
    public bool LifeModulesResolved => _lifeModules.CapturedState.Resolved;

    public static bool TryCapture(ICharacterSourceDataContext source, string characterXml,
        ILifeModulesCatalogService lifeModules,
        [NotNullWhen(true)] out WorkspaceContinuationSourceCapture? capture)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(characterXml);
        ArgumentNullException.ThrowIfNull(lifeModules);
        capture = null;
        try
        {
            capture = new(source, characterXml, lifeModules);
            return true;
        }
        catch (Exception exception) when (IsSourceFailure(exception))
        {
            return false;
        }
    }

    public ICharacterSourceDataResolver CreateResolver() => new FrozenResolver(this);

    private delegate bool SourceReader<T>(out T value);

    private static Captured<T> Capture<T>(SourceReader<T> read, T unavailable) where T : class
    {
        T value;
        bool resolved;
        try
        {
            resolved = read(out value);
        }
        catch (Exception exception) when (IsSourceFailure(exception))
        {
            return new(false, unavailable);
        }
        if (resolved && value is null)
            throw new InvalidDataException("Resolved continuation source is missing.");
        return new(resolved, value ?? unavailable);
    }

    private static bool IsSourceFailure(Exception exception) => exception is ArgumentException
        or FormatException or IOException or InvalidOperationException or UnauthorizedAccessException
        or System.Xml.XmlException or JsonException or NotSupportedException or OverflowException;

    // Serialize at capture and deserialize at each read. Copying only the outer
    // records would leave arrays/dictionaries shared with sources and consumers.
    private sealed class Captured<T> where T : class
    {
        public Captured(bool resolved, T value)
        {
            Resolved = resolved;
            Value = JsonSerializer.SerializeToElement(value);
            _ = Copy(); // Refuse a capture that its frozen reader cannot materialize.
        }

        public bool Resolved { get; }
        public JsonElement Value { get; }
        public T Copy() => Value.Deserialize<T>()
            ?? throw new InvalidDataException("Captured continuation source is missing.");
        public bool Read(out T value)
        {
            value = Copy();
            return Resolved;
        }
    }

    private sealed record ReputationSource(CharacterCareerReputationSettings Settings, string RawRuleState);

    private sealed class FrozenResolver(WorkspaceContinuationSourceCapture capture) : ICharacterSourceDataResolver
    {
        public ICharacterSourceDataContext? TryCreateContext(string characterXml) =>
            string.Equals(characterXml, capture._characterXml, StringComparison.Ordinal)
                ? new FrozenContext(capture) : null;
    }

    private sealed class FrozenContext(WorkspaceContinuationSourceCapture capture) : ICharacterSourceDataContext
    {
        public bool TryResolveCreationSourceProfile(out CharacterCreationSourceProfileAuthority authority) => capture._profile.Read(out authority);
        public bool TryResolveCreationMetatypeCatalog(out CharacterCreationMetatypeCatalogAuthority authority) => capture._metatypes.Read(out authority);
        public bool TryResolveCreationPrerequisiteAuthority(out CharacterCreationPrerequisiteAuthority authority) => capture._prerequisite.Read(out authority);
        public bool TryResolveCreationSkillsAuthority(out CharacterCreationSkillsAuthority authority) => capture._skills.Read(out authority);
        public bool TryResolveCreationQualitiesAuthority(out CharacterCreationQualitiesAuthority authority) => capture._qualities.Read(out authority);
        public bool TryResolveCreationMagicResonanceAuthority(out CharacterCreationMagicResonanceAuthority authority) => capture._magic.Read(out authority);
        public bool TryResolveCreationResourcesAuthority(out CharacterCreationResourcesAuthority authority) => capture._resources.Read(out authority);
        public bool TryResolveCreationGearAuthority(out CharacterCreationGearAuthority authority) => capture._gear.Read(out authority);
        public bool TryResolveCreationLifestylesAuthority(out CharacterCreationLifestylesAuthority authority) => capture._lifestyles.Read(out authority);

        public bool TryResolveCareerReputationSettings(out CharacterCareerReputationSettings settings, out string rawRuleState)
        {
            bool resolved = capture._reputation.Read(out var value);
            settings = value.Settings;
            rawRuleState = value.RawRuleState;
            return resolved;
        }

        public bool TryIsBookEnabled(string sourceCode, out bool enabled)
        {
            bool resolved = capture._profile.Read(out var profile);
            if (!resolved || profile.EnabledSourcebooks is not { } books || string.IsNullOrWhiteSpace(sourceCode))
            {
                enabled = false;
                return false;
            }
            enabled = books.Contains(sourceCode, StringComparer.OrdinalIgnoreCase);
            return true;
        }

        public bool TryResolveCyberwareGradeDeviceRating(string gradeName, string improvementSource, out int deviceRating)
        {
            deviceRating = 0;
            return false;
        }

        public bool TryResolveVehicleModBonuses(string sourceId, string name, out CharacterVehicleModSourceBonuses bonuses)
        {
            bonuses = CharacterVehicleModSourceBonuses.Empty;
            return false;
        }
    }

    private sealed record LifeModuleStageCapture(string Stage, IReadOnlyList<LifeModuleSummaryDto> Modules,
        IReadOnlyList<LifeModuleLegalOptionDto> Options);

    private sealed record LifeModuleCapture(LifeModuleCatalogAuthorityDto Authority,
        IReadOnlyList<LifeModuleStageDto> Stages, IReadOnlyList<LifeModuleSummaryDto> Modules,
        IReadOnlyList<LifeModuleLegalOptionDto> Options, IReadOnlyList<LifeModuleStageCapture> StageCaptures,
        IReadOnlyList<string> EnabledSources);

    private sealed class FrozenLifeModulesCatalog(Captured<LifeModuleCapture> state) : ILifeModulesCatalogService
    {
        public Captured<LifeModuleCapture> CapturedState => state;

        public static FrozenLifeModulesCatalog Capture(ILifeModulesCatalogService catalog,
            bool profileResolved, IReadOnlyList<string> enabledSources)
        {
            var unavailable = new LifeModuleCapture(new(string.Empty, string.Empty, []), [], [], [], [], []);
            if (!profileResolved)
                return new(new(false, unavailable));
            try
            {
                string[] sources = NormalizeSources(enabledSources);
                LifeModuleCatalogAuthorityDto authority = catalog.GetAuthority();
                LifeModuleStageDto[] stages = catalog.GetStages().ToArray();
                LifeModuleSummaryDto[] modules = catalog.GetModules().ToArray();
                LifeModuleLegalOptionDto[] options = catalog.GetOptionProjections(null, sources).ToArray();
                string[] stageNames = stages.Select(stage => stage.Name)
                    .Concat(modules.Select(module => module.Stage))
                    .Concat(options.Select(option => option.StageId))
                    .Append("Nationality")
                    .Distinct(StringComparer.Ordinal).OrderBy(name => name, StringComparer.Ordinal).ToArray();
                if (stageNames.Any(string.IsNullOrWhiteSpace)
                    || !CharacterCreationFoundationDraftLedgerIntegrity.IsCanonicalDigest(authority.RawXmlDigest))
                    return new(new(false, unavailable));
                LifeModuleStageCapture[] capturedStages = stageNames.Select(stage => new LifeModuleStageCapture(
                    stage, catalog.GetModules(stage).ToArray(),
                    catalog.GetOptionProjections(stage, sources).ToArray())).ToArray();
                return new(new(true, new(authority, stages, modules, options, capturedStages, sources)));
            }
            catch (Exception exception) when (IsSourceFailure(exception))
            {
                return new(new(false, unavailable));
            }
        }

        public LifeModuleCatalogAuthorityDto GetAuthority() => Read().Authority;
        public IReadOnlyList<LifeModuleStageDto> GetStages() => Read().Stages;

        public IReadOnlyList<LifeModuleSummaryDto> GetModules(string? stage = null)
        {
            LifeModuleCapture value = Read();
            return stage is null ? value.Modules : RequireStage(value, stage).Modules;
        }

        public IReadOnlyList<LifeModuleLegalOptionDto> GetOptionProjections(string? stage = null,
            IReadOnlyCollection<string>? enabledSources = null)
        {
            LifeModuleCapture value = Read();
            if (enabledSources is null || !NormalizeSources(enabledSources).SequenceEqual(
                    value.EnabledSources, StringComparer.OrdinalIgnoreCase))
                throw new InvalidOperationException("Life Module sources are outside the continuation capture.");
            return stage is null ? value.Options : RequireStage(value, stage).Options;
        }

        private LifeModuleCapture Read() => state.Resolved ? state.Copy()
            : throw new InvalidOperationException("Life Module continuation sources are unavailable.");

        private static LifeModuleStageCapture RequireStage(LifeModuleCapture value, string stage) =>
            value.StageCaptures.SingleOrDefault(item => string.Equals(item.Stage, stage, StringComparison.Ordinal))
            ?? throw new InvalidOperationException("Life Module stage is outside the continuation capture.");

        private static string[] NormalizeSources(IEnumerable<string> sources) => sources
            .Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray();
    }
}

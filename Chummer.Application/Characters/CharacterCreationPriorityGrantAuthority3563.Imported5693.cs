using System.Globalization;
using System.Xml.Linq;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

internal sealed partial class CharacterCreationPriorityGrantAuthority3563
{
    private readonly ImportedSelection5693? imported5693;

    private CharacterCreationPriorityGrantAuthority3563(ImportedSelection5693 imported)
        : this(null!, default, 0, "", "") => imported5693 = imported;

    internal bool IsImported5693 => imported5693 is not null;

    // Explicit V9/current entry only. Never fall back here after a failed native
    // receipt replay, and never infer a choice from an improvement or rating.
    internal static CharacterCreationPriorityGrantAuthority3563 CaptureImported5693(
        CharacterCurrentRuleEnvironmentContext current)
    {
        try
        {
            var resolution = current.Capture.CreateCurrentEnvironmentResolution(
                current.SemanticsProfile, current.SemanticsFingerprint);
            var context = resolution?.Resolver.TryCreateContext(current.CharacterXml);
            var root = CharacterSheetOwnerGraph.Parse(current.CharacterXml);
            if (context is null || !ImportedSelection5693.TryEvaluate(current, root, context,
                    out _, out _, out _, out _, out _, out string digest)) return Unavailable();
            return new(new ImportedSelection5693(current, digest));
        }
        catch (Exception error) when (Malformed(error)) { return Unavailable(); }
    }

    internal bool TryResolveImportedMinimum5693(XElement root, ICharacterSourceDataContext context,
        bool resonant, out int minimum, out string digest)
    {
        minimum = 0; digest = "";
        if (imported5693 is null || !imported5693.TryResolve(root, context, out _, out _, out _,
                out int? magic, out int? resonance, out digest)) return false;
        if (resonant ? resonance is not > 0 || magic is not null : magic is not > 0 || resonance is not null)
            return false;
        minimum = (resonant ? resonance : magic)!.Value;
        return true;
    }

    private sealed class ImportedSelection5693(CharacterCurrentRuleEnvironmentContext current, string capturedDigest)
    {
        internal bool TryResolve(XElement root, ICharacterSourceDataContext context,
            out CharacterCreationTalentSkillGrants grants, out IReadOnlyList<string> groups,
            out IReadOnlyList<CharacterCreationTalentQualitySource> qualities,
            out int? magic, out int? resonance, out string digest)
        {
            grants = null!; groups = []; qualities = []; magic = null; resonance = null; digest = "";
            try
            {
                return TryEvaluate(current, root, context, out grants, out groups, out qualities,
                    out magic, out resonance, out digest) && digest == capturedDigest;
            }
            catch (Exception error) when (Malformed(error)) { return false; }
        }

        internal static bool TryEvaluate(CharacterCurrentRuleEnvironmentContext current, XElement root,
            ICharacterSourceDataContext context, out CharacterCreationTalentSkillGrants grants,
            out IReadOnlyList<string> groups, out IReadOnlyList<CharacterCreationTalentQualitySource> qualities,
            out int? magic, out int? resonance, out string digest)
        {
            grants = null!; groups = []; qualities = []; magic = null; resonance = null; digest = "";
            var auxiliary = current.Current.Document.AuxiliaryState;
            if (!CurrentCharacterRuleSourceSemantics.IsImportedPriorityCareerProfile5667(
                    current.SemanticsProfile, current.SemanticsFingerprint)
                || current.Stage != CharacterCurrentRuleStage.Career || current.Current.Document.RulesetId != "sr5"
                || !current.VerifyRetainedArchive()
                || auxiliary.CharacterCreationPrerequisiteDraft is not null
                || auxiliary.CharacterCreationFinalizationArchive is not null
                || auxiliary.CharacterCreationFinalizationReceipts is { Count: > 0 }
                || root.Elements(CharacterCreationLifestyleMechanicsRules.CapabilityElement).Any()
                || !TryReadFacts(root, out var facts)
                || !TryReadFacts(CharacterSheetOwnerGraph.Parse(current.CharacterXml), out var original)
                || CharacterCreationFinalizationDigest.Compute(facts) != CharacterCreationFinalizationDigest.Compute(original)
                || !context.TryResolveCreationPrerequisiteAuthority(out var authority)
                || !authority.IsAuthoritative || authority.Blockers.Count != 0
                || authority.Schema != CharacterCreationPrerequisiteSchemas.AuthorityV1
                || authority.BuildMethod != CharacterCreationBuildMethods.Priority
                || authority.SettingsProfileId != facts.Settings
                || authority.AuthorityDigest != CharacterCreationPrerequisiteAuthorityDigest.Compute(authority)) return false;

            // Windows writes a rank letter. Native-written imported files may
            // contain the explicit rank/value pair. Match either exact persisted
            // spelling to one retained option; never reconstruct other ranks.
            var ranks = authority.Options.Where(option => option.CategoryId == CharacterCreationPriorityCategoryIds.Talent
                && (option.Rank == facts.Rank || string.Create(CultureInfo.InvariantCulture,
                    $"{option.Rank},{option.SumToTenValue}") == facts.Rank)).ToArray();
            if (ranks is not [var rank]) return false;
            if (authority.Options.Count(option => option.CategoryId == CharacterCreationPriorityCategoryIds.Skills
                && (option.Rank == facts.SkillRank || string.Create(CultureInfo.InvariantCulture,
                    $"{option.Rank},{option.SumToTenValue}") == facts.SkillRank)) != 1) return false;
            var talents = rank.TalentOptions.Where(option => option.Value == facts.Talent).ToArray();
            if (talents is not [var talent] || !talent.IsEnabled || talent.Blockers.Count != 0 || talent.Depth is not null
                || talent.ActiveSkillGrant is not null && talent.SkillGroupGrant is not null) return false;

            string[] activeIds = [], groupIds = [];
            if (talent.ActiveSkillGrant is { } active)
            {
                if (!facts.HasChoiceContainer) return false;
                var ids = new List<string>();
                foreach (string name in facts.Choices)
                {
                    var matches = active.Options.Where(option => option.CanonicalName == name).ToArray();
                    if (matches is not [var option]) return false;
                    ids.Add(option.SelectionId);
                }
                activeIds = ids.ToArray();
            }
            else if (talent.SkillGroupGrant is { } grouped)
            {
                if (!facts.HasChoiceContainer) return false;
                var ids = new List<string>();
                foreach (string name in facts.Choices)
                {
                    var matches = grouped.Options.Where(option => option.CanonicalName == name).ToArray();
                    if (matches is not [var option]) return false;
                    ids.Add(option.SelectionId);
                }
                groupIds = ids.ToArray();
            }
            else if (facts.Choices.Length != 0) return false;

            var blockers = new List<string>();
            var plan = CharacterCreationPrerequisiteService.ResolveTalentGrantPlan(talent, activeIds, groupIds, blockers);
            if (blockers.Count != 0
                || !context.TryResolveCreationSkillsCatalog(out var catalog)
                || !CharacterCreationSkillsCatalogAuthority.IsValid(catalog)
                || catalog!.SettingsProfileId != authority.SettingsProfileId
                || catalog.RawProfileInputsDigest != authority.RawProfileInputsDigest
                || catalog.SkillsInputsDigest != authority.EffectiveSkillsInputsDigest
                || !context.TryResolveCareerSettingsProfile(out string settingsXml, out _)
                || !CharacterCreationStandardPrioritySkillsRules.TryReadCreationRatingLimit(
                    CharacterSheetOwnerGraph.Parse(settingsXml), "maxskillratingcreate", "maxskillrating", out int maximum)
                || !CharacterCreationStandardPrioritySkillsRules.IsSupportedCreationRatingLimit(maximum)
                || !CharacterCreationTalentSkillGrants.TryResolveImportedPlan5693(plan, catalog, maximum, out grants)
                || !context.TryResolveCreationMagicResonanceAuthority(out var magicAuthority)
                || !CharacterCreationMagicResonanceDraftIntegrity.IsValidAuthority(magicAuthority,
                    CharacterCurrentRuleEnvironmentConsumers.ImportedSettingsFile5691(current, context))
                || !magicAuthority.IsAuthoritative || magicAuthority.Blockers.Count != 0
                || magicAuthority.SettingsProfileId != authority.SettingsProfileId
                || magicAuthority.PrerequisiteAuthorityDigest != authority.AuthorityDigest) return false;
            var magicTalents = magicAuthority.Talents.Where(option => option.Identity.PrioritySourceId == rank.SourceId
                && option.Rank == rank.Rank && option.Identity.TalentSelectionId == talent.SelectionId
                && option.Identity.TalentValue == talent.Value
                && option.SourceNodeDigest == talent.PriorityChildNodeDigest).ToArray();
            if (magicTalents is not [var magicTalent] || !magicTalent.IsEnabled || magicTalent.Blockers.Count != 0
                || !CharacterCreationTalentQualitySourceRules.MatchesTalent(
                    magicTalent.CanonicalSourceXml, magicTalent.GrantedQualitySources)
                || !talent.GrantedQualities.SequenceEqual(
                    magicTalent.GrantedQualitySources!.Select(quality => quality.Reference), StringComparer.Ordinal)) return false;

            // Keep zero-rated group choices: they select unlocks even though they
            // create no SkillGroupBase row. The owner compiler reconciles all
            // actual Heritage owners and every generated/saved effect row.
            groups = plan?.SkillGroups.Select(group => group.CanonicalName).ToArray() ?? [];
            qualities = magicTalent.GrantedQualitySources!;
            magic = talent.Magic; resonance = talent.Resonance;
            digest = CharacterCreationFinalizationDigest.Compute(new
            {
                Schema = "chummer.imported-priority-selection5693/v1", current.BindingDigest,
                current.CaptureDigest, Facts = facts, Prerequisite = authority.AuthorityDigest,
                Skills = CharacterCreationFinalizationDigest.Compute(catalog), Magic = magicAuthority.AuthorityDigest,
                Plan = plan, MagicMinimum = magic, ResonanceMinimum = resonance
            });
            return current.IsUnchanged;
        }

        private sealed record Facts(string Settings, string Rank, string Talent, string SkillRank,
            string Metatype, string Metavariant, bool HasChoiceContainer, string[] Choices);

        private static bool TryReadFacts(XElement root, out Facts facts)
        {
            facts = null!;
            if (root.Name != "character" || root.HasAttributes
                || !Scalar(root, "created", out string created) || !bool.TryParse(created, out bool isCreated) || !isCreated
                || !Scalar(root, "buildmethod", out string method) || method != CharacterCreationBuildMethods.Priority
                || !Scalar(root, "settings", out string settings) || settings.Length == 0
                || !Scalar(root, "priorityspecial", out string rank) || rank.Length == 0
                || !Scalar(root, "prioritytalent", out string talent) || talent.Length == 0
                || !Scalar(root, "metatype", out string metatype) || metatype.Length == 0) return false;
            string variant = "";
            if (root.Elements("metavariant").Any() && !Scalar(root, "metavariant", out variant)) return false;
            var skillNodes = root.Elements("priorityskills").ToArray();
            // Windows Character.Save/SaveAsync deliberately writes these two
            // same-name siblings: a scalar rank and a priorityskill container.
            // An empty container is distinguishable from a nonempty rank.
            var ranks = skillNodes.Where(node => !node.HasElements && !string.IsNullOrWhiteSpace(node.Value)).ToArray();
            var containers = skillNodes.Except(ranks).ToArray();
            if (ranks is not [var skillRank] || skillRank.HasAttributes
                || skillNodes.Length is < 1 or > 2 || containers.Length > 1
                || containers.Any(node => node.HasAttributes || node.Nodes().Any(child => child is not XElement
                    && (child is not XText text || !string.IsNullOrWhiteSpace(text.Value)))
                    || node.Elements().Any(child => child.Name != "priorityskill" || child.HasAttributes
                        || child.HasElements || child.Value.Length == 0 || child.Value != child.Value.Trim()))) return false;
            string[] choices = containers.SelectMany(node => node.Elements("priorityskill")).Select(node => node.Value).ToArray();
            if (choices.Length > CharacterCreationTalentSkillGrantTypes.MaximumPromptSlots
                || choices.Distinct(StringComparer.Ordinal).Count() != choices.Length) return false;
            facts = new(settings, rank, talent, skillRank.Value, metatype, variant, containers.Length == 1, choices);
            return true;
        }

        private static bool Scalar(XElement root, string name, out string value)
        {
            value = "";
            var nodes = root.Elements(name).ToArray();
            if (nodes is not [var node] || node.HasAttributes || node.HasElements
                || node.Nodes().Any(child => child is not XText)) return false;
            value = node.Value;
            return true;
        }
    }
}

using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;
using Chummer.Contracts.Characters;
using D1217 = Chummer.Application.Characters.CharacterCreationSourceOperationDisposition;

namespace Chummer.Application.Characters;

internal sealed partial class CharacterCreationCompleteRuleSourceExecution
{
    // Admission is only CandidateDependent. The original retained 1203 invocation
    // must still execute the exact leaf against fresh sources before coverage.
    private void AddProfileLifecycleCapabilities1217(XElement profile,
        ICharacterSourceDataContext context, CharacterCreationPrerequisiteAuthority prerequisite,
        CharacterCreationPrerequisiteDraft draft, Action<string, string?, object, object?> add)
    {
        if (request.LifecycleResultSchema != CharacterCreationCompletionReviewSchemas.LifecycleResultV1
            || !IsCreationProfile1217(XElement.Parse(Inputs.OriginalCharacter!.RawCharacterXml,
                LoadOptions.PreserveWhitespace), draft.BuildMethod)) return;

        if (Integer1217("minastralinitiativedice", out int minimum)
            && Integer1217("maxastralinitiativedice", out int maximum))
        {
            // Character.AstralInitiativeDice: no MAG/Mundane/inventory shortcut.
            var calculation = new { Operation = "AstralInitiativeDice", prerequisite,
                Minimum = minimum, Maximum = maximum, Dice = Math.Min(minimum, maximum) };
            add("minastralinitiativedice", null, calculation, minimum);
            add("maxastralinitiativedice", null, calculation, maximum);
        }
        if (Boolean1217("autobackstory", out bool automatic))
            add("autobackstory", null, new { Operation = "EnableAutomaticStoryButton", prerequisite,
                draft.BuildMethod, Enabled = draft.BuildMethod == CharacterCreationBuildMethods.LifeModules && automatic }, automatic);

        // These consumers operate on Career expense/quality/PP commands. A
        // canonical creation review is a different lifecycle operation, even
        // when an option is true and the candidate has the corresponding talent.
        foreach (string field in new[] { "dontdoublequalities", "dontdoublequalityrefunds", "mysaddppcareer" })
            if (Boolean1217(field, out bool value))
                add(field, null, new { Operation = "creation-excludes-career-command", prerequisite,
                    Field = field, Created = false, Value = value }, value);
        if (PlainProfileLeaf1203(profile, "nuyenperbpwftp", null) is { } exchange
            && decimal.TryParse(exchange.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal rate)
            && rate > 0)
            add("nuyenperbpwftp", null, new { Operation = "creation-excludes-career-exchange", prerequisite,
                Created = false, Rate = rate }, null);

        if (draft.HeritageSelection is { MetavariantSourceId: null } heritage
            && context.TryResolveCreationMetatypeCatalog(out var metatypes) && metatypes.IsAuthoritative
            && metatypes.SourceContext.SettingsProfileId == prerequisite.SettingsProfileId
            && metatypes.SourceContext.RawProfileInputsDigest == prerequisite.RawProfileInputsDigest
            && metatypes.SourceContext.MetatypeKarmaMultiplier is int multiplier
            && metatypes.Options.Where(option => option.OptionId == heritage.MetatypeSourceId).ToArray() is [var selected]
            && selected.IsEnabled && selected.Blockers.Count == 0)
        {
            // The catalogue's real policy gate and checked BP * multiplier
            // projection execute even for Priority. Do not call this inactive.
            var calculation = new { metatypes.SourceContext, selected.OptionId, selected.BaseKarma, selected.KarmaCost };
            add("metatypecostskarma", null, calculation, true);
            add("metatypecostskarmamultiplier", null, calculation, multiplier);
        }

        if (request.Selections.SkillKarmaPurchases is not null
            && context.TryResolveCreationPrioritySkillKarmaPolicy(out var skills)
            && skills is { Schema: CharacterCreationKarmaSkillsPolicy.PriorityPurchaseSchemaV1, AllowSkillRegrouping: not null }
            && skills.SettingsProfileId == prerequisite.SettingsProfileId
            && skills.RawProfileInputsDigest == prerequisite.RawProfileInputsDigest
            && CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(skills, Inputs.Skills.KarmaPurchasePolicy))
        {
            add("allowskillregrouping", null, skills, skills.AllowSkillRegrouping.Value);
        }

        bool Integer1217(string field, out int value)
        {
            value = 0;
            return PlainProfileLeaf1203(profile, field, null) is { } leaf
                && int.TryParse(leaf.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) && value >= 0;
        }
        bool Boolean1217(string field, out bool value)
        {
            value = false;
            return PlainProfileLeaf1203(profile, field, null) is { } leaf && bool.TryParse(leaf.Value, out value);
        }
    }

    private static bool IsCreationProfile1217(XElement character, string method)
        => method is CharacterCreationBuildMethods.Priority or CharacterCreationBuildMethods.SumToTen
            && character.Name == "character"
            && PlainProfileLeaf1203(character, "created", null) is { } created
            && bool.TryParse(created.Value, out bool career) && !career
            && PlainProfileLeaf1203(character, "gameedition", null)?.Value == "SR5";

    private bool IsExcludedCareerSetting1217(string field, D1217 disposition,
        CharacterCreationRuleSourceScopeInvocation invocation)
        => disposition == D1217.NotSelected
            && field is "nuyenperbpwftp" or "dontdoublequalities" or "dontdoublequalityrefunds" or "mysaddppcareer"
            && invocation.CreationDraft718 is { } draft
            && IsCreationProfile1217(XElement.Parse(Inputs.OriginalCharacter!.RawCharacterXml,
                LoadOptions.PreserveWhitespace), draft.BuildMethod);

    private bool MatchesAstralProfileResult1249(string field, CharacterCreationRuleSourceScopeInvocation invocation,
        IReadOnlyList<CharacterCreationSourceOperation> operations)
    {
        if (field is not ("minastralinitiativedice" or "maxastralinitiativedice")) return true;
        var source = Inputs.Magic.Authority.MysticAdeptPowerPointPolicy;
        if (source is null || !invocation.TryGetExecutedAstralInitiative1249(source.SettingsProfileId,
            CharacterCreationBootstrapProfiles.BindSettingsInputsDigest(source.SettingsInputsDigest, source.SettingsProfileId),
            CharacterCreationFinalizationDigest.Compute(source.CanonicalSourceXml),
            out var native)) return false;
        try
        {
            foreach (var operation in operations)
            {
                using var document = JsonDocument.Parse(operation.OperationResult718!);
                var root = document.RootElement;
                if (!root.TryGetProperty("NativeResult", out var executed)
                    || executed.Deserialize<CharacterAstralInitiativeDiceProjection>() != native
                    || root.GetProperty("Dice").GetInt32() != native!.Dice
                    || root.GetProperty("Minimum").GetInt32() != native.Minimum
                    || root.GetProperty("Maximum").GetInt32() != native.Maximum) return false;
            }
            return true;
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or KeyNotFoundException
            or FormatException or OverflowException)
        { return false; }
    }
}

using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using System.Xml.XPath;
using Chummer.Contracts.Characters;
using static Chummer.Application.Characters.CharacterCreationCompletionFinalizationProjector;
using A1430 = Chummer.Contracts.Characters.CharacterCreationNativeMagicApplicability1430;

namespace Chummer.Application.Characters;

internal static class CharacterCreationNativeMagic1430
{
    internal const string Unavailable = "creation-native-magic1430-unavailable";
    private static readonly string[] AttributeNames = ["BOD", "AGI", "REA", "STR", "CHA", "INT", "LOG", "WIL", "EDG", "MAG", "MAGAdept", "RES", "DEP"];

    internal static CharacterCreationNativeMagicPolicy1430? ReadPolicy(
        CharacterCreationMysticAdeptPowerPointPolicy? source, CharacterCreationPrerequisiteDraft draft)
        => ReadPolicy(source, draft.SettingsProfileId, draft.BuildMethod);

    internal static CharacterCreationNativeMagicPolicy1430? ReadCurrentPolicy(
        CharacterCreationMysticAdeptPowerPointPolicy? source, string expectedSettingsProfileId, string currentBuildMethod,
        CharacterRetainedSettingsFile? retainedFile = null)
        => ReadPolicy(source, expectedSettingsProfileId, currentBuildMethod, retainedFile: retainedFile);

    internal static CharacterCreationNativeMagicPolicy1430? ReadCurrentPolicy(
        CharacterCreationKarmaMagicPolicy? source, CharacterCurrentRuleEnvironmentContext current)
    {
        if (!current.IsUnchanged || !CurrentCharacterRuleSourceSemantics.IsNativeExecutionProfile(
                current.SemanticsProfile, current.SemanticsFingerprint) || source is null)
            return null;
        var graph = XElement.Parse(current.CharacterXml);
        string method = Scalar(graph, "buildmethod");
        if (Scalar(graph, "settings") != source.SettingsProfileId
            || !(method == CharacterCreationBuildMethods.Karma
                ? CharacterCreationKarmaMagicRules.IsValidPolicy(source)
                : method == CharacterCreationBuildMethods.LifeModules
                    && CharacterCreationKarmaMagicRules.IsValidLifeModulePolicy(source))) return null;
        return ReadPolicy(source.PowerPointPolicy, source.SettingsProfileId, method, nativeCurrent: true);
    }

    internal static CharacterCreationNativeMagicProjection1430? EvaluateCurrent(XElement graph,
        CharacterCreationKarmaMagicPolicy source, CharacterCurrentRuleEnvironmentContext current,
        ICharacterSourceDataContext currentSource)
        => EvaluateCore(graph, ReadCurrentPolicy(source, current), null, currentState: true, currentSource: currentSource);

    private static CharacterCreationNativeMagicPolicy1430? ReadPolicy(
        CharacterCreationMysticAdeptPowerPointPolicy? source, string settingsProfileId, string buildMethod,
        bool nativeCurrent = false, CharacterRetainedSettingsFile? retainedFile = null)
    {
        if (!CharacterCreationMysticAdeptPowerPointRules.IsValidPolicy(source, retainedFile)
            || source!.SettingsProfileId != settingsProfileId
            || !(buildMethod is CharacterCreationBuildMethods.Priority or CharacterCreationBuildMethods.SumToTen
                || nativeCurrent && (buildMethod is CharacterCreationBuildMethods.Karma or CharacterCreationBuildMethods.LifeModules))) return null;
        try
        {
            var profile = XElement.Parse(source.CanonicalSourceXml, LoadOptions.PreserveWhitespace);
            if (profile.Name != "setting" || profile.HasAttributes) return null;
            var values = new Dictionary<string, object?>();
            foreach (var definition in CharacterCreationNativeMagicDefinitions1430.All)
            {
                var node = One(profile, definition.Element);
                object? value = null;
                if (node is not null)
                {
                    string text = Text(node);
                    value = definition.Kind switch
                    {
                        CharacterCreationSettingValueKind.Boolean => bool.TryParse(text, out bool flag) ? flag : throw Invalid(),
                        CharacterCreationSettingValueKind.Integer => int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int number)
                            && number >= 0 ? number : throw Invalid(),
                        CharacterCreationSettingValueKind.Expression => ValidExpression(text) ? text : throw Invalid(),
                        _ => throw Invalid()
                    };
                }
                values.Add(definition.Element, value);
            }
            if (values.Values.All(value => value is null)) return null;
            return new((bool?)values["spiritforcebasedontotalmag"], (bool?)values["allowinitiationincreatemode"],
                (bool?)values["ignoreart"], (string?)values["boundspiritexpression"], (string?)values["registeredspriteexpression"],
                (bool?)values["ignorecomplexformlimit"], (bool?)values["freespiritpowerpointsmag"],
                (bool?)values["increasedimprovedabilitymodifier"], (int?)values["dicepenaltysustaining"], source.SettingsProfileId,
                CharacterCreationBootstrapProfiles.BindSettingsInputsDigest(source.SettingsInputsDigest, source.SettingsProfileId),
                CharacterCreationFinalizationDigest.Compute(profile.ToString(SaveOptions.DisableFormatting)));
        }
        catch (Exception error) when (error is InvalidDataException or XmlException or InvalidOperationException or ArgumentException or OverflowException)
        { return null; }
    }

    internal static CharacterCreationNativeMagicProjection1430? Evaluate(XElement graph, string? schema,
        AwakenedGraphInputs inputs, CharacterCreationPrerequisiteDraft draft, CharacterCreationEssenceExecution1222? essence)
        => schema == CharacterCreationCompletionReviewSchemas.LifecycleResultV1
            ? Evaluate(graph, CharacterCreationAstralInitiative1249.Source(inputs), draft, essence) : null;

    internal static CharacterCreationNativeMagicProjection1430? Evaluate(XElement graph,
        CharacterCreationMysticAdeptPowerPointPolicy? source, CharacterCreationPrerequisiteDraft draft,
        CharacterCreationEssenceExecution1222? essence = null)
        => EvaluateCore(graph, ReadPolicy(source, draft), essence, currentState: false);

    internal static CharacterCreationNativeMagicProjection1430? EvaluateCurrent(XElement graph,
        CharacterCreationMysticAdeptPowerPointPolicy? source, string expectedSettingsProfileId,
        string currentBuildMethod, CharacterCreationEssenceExecution1222? essence = null)
        => EvaluateCore(graph, ReadPolicy(source, expectedSettingsProfileId, currentBuildMethod), essence, currentState: true);

    internal static CharacterCreationNativeMagicProjection1430? EvaluateCurrent(XElement graph,
        CharacterCreationMysticAdeptPowerPointPolicy? source, string expectedSettingsProfileId,
        string currentBuildMethod, ICharacterSourceDataContext currentSource, CharacterCreationEssenceExecution1222? essence = null)
        => EvaluateCore(graph, ReadPolicy(source, expectedSettingsProfileId, currentBuildMethod), essence,
            currentState: true, currentSource: currentSource);

    internal static CharacterCreationNativeMagicProjection1430? EvaluateImportedCurrent5699(XElement graph,
        CharacterCreationMysticAdeptPowerPointPolicy? source, CharacterSheetDerivedQuery.ImportedCareerSheet5699 imported,
        CharacterRetainedSettingsFile? retainedFile = null)
    {
        if (!imported.IsCurrent) return null;
        return EvaluateCore(graph, ReadPolicy(source, Scalar(graph, "settings"), Scalar(graph, "buildmethod"),
                retainedFile: retainedFile),
            null, currentState: true, imported: imported);
    }

    private static CharacterCreationNativeMagicProjection1430? EvaluateCore(XElement graph,
        CharacterCreationNativeMagicPolicy1430? policy, CharacterCreationEssenceExecution1222? essence, bool currentState,
        ICharacterSourceDataContext? currentSource = null, CharacterSheetDerivedQuery.ImportedCareerSheet5699? imported = null)
    {
        if (policy is null) return null;
        if (graph.Name != "character" || Scalar(graph, "gameedition") != "SR5") throw Invalid();
        bool created = Boolean(graph, "created"), ignoreRules = Boolean(graph, "ignorerules", false);
        bool magicEnabled = Boolean(graph, "magenabled");
        int? force = null;
        if (policy.SpiritForceBasedOnTotalMagic is bool total)
        {
            int magic = magicEnabled ? Attribute("MAG", total) : 0;
            force = magic <= 0 ? 0 : checked(magic * (created ? 2 : 1));
        }
        bool? initiation = policy.AllowInitiationInCreation is bool allow ? created || ignoreRules || allow : null;
        int? bound = policy.BoundSpiritExpression is { } boundExpression ? Expression(boundExpression) : null;
        int? sprites = policy.RegisteredSpriteExpression is { } spriteExpression ? Expression(spriteExpression) : null;

        // Art requirements are selection predicates, not a global magic exemption.
        // Existing admitted creation source compilers reject art/metamagicart predicates.
        // Retained active art/metamagic graphs must not acquire coverage from that fact.
        var art = A1430.SettingAbsent;
        if (policy.IgnoreArt.HasValue)
        {
            if (Rows(graph, "arts", "art").Length != 0 || Rows(graph, "metamagics", "metamagic").Length != 0
                || graph.Descendants().Any(node => node.Name.LocalName is "art" or "metamagicart")) throw Invalid();
            art = A1430.NoArtRequirementOperation;
        }
        var improved = A1430.SettingAbsent;
        if (policy.IncreasedImprovedAbilityModifier.HasValue)
        {
            // Power.Name sets BoostedSkill only for this exact native name. An active
            // boosted-skill power needs its learned-skill/source execution, not a guess.
            if (Rows(graph, "powers", "power").Any(row => Scalar(row, "name") == "Improved Ability (skill)")) throw Invalid();
            improved = A1430.NoBoostedSkillPower;
        }
        var freeSpirit = A1430.SettingAbsent;
        int? spiritPoints = null;
        decimal? remaining = null;
        if (policy.FreeSpiritPowerPointsMagic is bool useMagic)
        {
            freeSpirit = Scalar(graph, "metatype") == "Free Spirit" && !Boolean(graph, "iscritter", false)
                ? A1430.PlayerFreeSpirit : A1430.NotPlayerFreeSpirit;
            if (freeSpirit == A1430.PlayerFreeSpirit)
            {
                spiritPoints = checked(Attribute(useMagic ? "MAG" : "EDG", true)
                    + Round(DefaultValue("FreeSpiritPowerPoints")));
                decimal spent = Rows(graph, "critterpowers", "critterpower")
                    .Where(row => Boolean(row, "counttowardslimit", true)).Sum(row => Decimal(row, "points"));
                remaining = spiritPoints.Value - spent;
            }
        }
        decimal? formLimit = null;
        int? formCount = null;
        bool? mayAddForm = null;
        if (policy.IgnoreComplexFormLimit is bool ignoreLimit)
        {
            formCount = Rows(graph, "complexforms", "complexform").Length;
            if (currentState && !Boolean(graph, "resenabled", false))
            {
                if (formCount != 0) throw Invalid();
                formLimit = 0; mayAddForm = false;
            }
            else
            {
                formLimit = checked(Attribute("RES", true) * 2m
                    + DefaultValue("ComplexFormLimit"));
                // Native CharacterCreate ignores this setting; CharacterCareer consults it.
                // This is the next-add gate, not retroactive deletion of existing forms.
                mayAddForm = formCount.Value < formLimit.Value || (created ? ignoreLimit : ignoreRules);
            }
        }
        int? penaltyCount = null, penalty = null;
        if (policy.DicePenaltySustaining is int perObject)
            (penaltyCount, penalty) = EvaluateSustainingPenalty(graph, perObject);
        return new(policy, created, CharacterCreationFinalizationDigest.Compute(graph.ToString(SaveOptions.DisableFormatting)),
            force, initiation, bound, sprites, art, improved, freeSpirit, spiritPoints, remaining,
            formLimit, formCount, mayAddForm, penaltyCount, penalty);

        decimal DefaultValue(string type)
        {
            if (imported is not null)
            {
                if (!imported.IsCurrent) throw Invalid();
                return imported.Owners!.Channel(type, "");
            }
            if (currentState && type == "ComplexFormLimit" && currentSource is not null)
            {
                var resonance = CharacterCareerResonanceEvaluator.Evaluate(graph, currentSource);
                if (!resonance.IsSupported) throw Invalid();
                return resonance.LimitModifier;
            }
            if (currentState && Rows(graph, "improvements", "improvement").Any(row =>
                Scalar(row, "improvementttype") == type && DefaultEligible(row))) throw Invalid();
            // All three native consumers call ValueOf with default arguments:
            // empty ImprovedName, no AddToRating, unconditional only. Reuse the
            // reviewed unique/custom sum only after applying those exact filters.
            var filtered = new XElement("character", new XElement("improvements",
                Rows(graph, "improvements", "improvement")
                    .Where(row => Scalar(row, "improvementttype") == type && DefaultEligible(row))
                    .Select(row => new XElement(row))));
            return CharacterCareerReputationProjector.ResolvePhysicalChannel886(filtered, type, created);
        }
        bool DefaultEligible(XElement row) => Integer(row, "enabled", 1) > 0
            && Integer(row, "addtorating", 0) == 0 && Scalar(row, "improvedname", "") == ""
            && Scalar(row, "condition", "") == "";

        int Attribute(string name, bool total)
        {
            if (imported is not null)
            {
                if (!imported.IsCurrent) throw Invalid();
                var value = imported.Sheet.Attributes.SingleOrDefault(attribute => attribute.Name == name);
                return value is null ? throw Invalid() : total ? value.Total : value.Unaugmented ?? throw Invalid();
            }
            var rows = Rows(graph, "attributes", "attribute").Where(row => Scalar(row, "name") == name).ToArray();
            if (rows is not [var row]) throw Invalid();
            if (currentState) return CurrentNaturalAttribute(graph, row);
            int current = Integer(row, "totalvalue");
            if (current < 0) throw Invalid();
            if (total) return current; // Authenticated compiler output, never a public graph receipt.
            if (essence?.Result.Attributes.SingleOrDefault(value => value.Id == name) is { } loss)
            {
                if (essence.ProfileId != policy.SettingsProfileId || essence.ProfileDigest != policy.ProfileInputsDigest
                    || essence.ProfileXmlDigest != policy.ProfileXmlDigest || loss.Current != current) throw Invalid();
                return loss.Value;
            }
            int natural = NaturalAttribute(row);
            // Without a private Essence result the supported compiler must have produced
            // the same unaugmented total. Never infer Value by subtracting an unknown bonus.
            if (natural != current) throw Invalid();
            return natural;
        }
        int Expression(string expression)
        {
            var number = ExpressionNumber(expression, name => Attribute(name, true));
            return number.Literal is { } literal ? Round(literal)
                : Convert.ToInt32(number.Evaluated >= 0 ? Math.Ceiling(number.Evaluated) : Math.Floor(number.Evaluated));
        }
    }

    // Shared native SustainedObject bookkeeping. The caller authenticates linked
    // owners, settings and improvements first; this method grants no mutation.
    internal static (int Count, int Penalty) EvaluateSustainingPenalty(XElement graph, int perObject)
    {
        if (perObject < 0) throw Invalid();
        bool DefaultEligible(XElement row) => Integer(row, "enabled", 1) > 0
            && Integer(row, "addtorating", 0) == 0 && Scalar(row, "improvedname", "") == ""
            && Scalar(row, "condition", "") == "";
        var sustained = Rows(graph, "sustainedobjects", "sustainedobject").Select(row => new
        {
            Type = Scalar(row, "linkedobjecttype"), Linked = Scalar(row, "linkedobject"),
            Self = Boolean(row, "self"), Force = Integer(row, "force")
        }).ToArray();
        if (sustained.Any(row => row.Type is not ("Spell" or "ComplexForm" or "CritterPower") || row.Force < 0)) throw Invalid();
        foreach (var item in sustained)
        {
            var (container, kind) = item.Type switch
            {
                "Spell" => ("spells", "spell"), "ComplexForm" => ("complexforms", "complexform"),
                "CritterPower" => ("critterpowers", "critterpower"), _ => throw Invalid()
            };
            // SustainedObject's native loader resolves a real linked object. A stale
            // reference cannot be promoted into an executed penalty calculation.
            if (!Guid.TryParse(item.Linked, out var id) || id == Guid.Empty
                || Rows(graph, container, kind).Count(row => Scalar(row, "guid") == item.Linked) != 1) throw Invalid();
        }
        var penalized = sustained.Where(row => row.Self && row.Type != "CritterPower").Select(row => row.Force).ToList();
        // The native cache supplies eligible PenaltyFreeSustain improvements. Keep
        // unsupported unique-selection shapes closed rather than invent eligibility.
        var allowances = new SortedDictionary<int, int>();
        foreach (var effect in Rows(graph, "improvements", "improvement")
            .Where(row => Scalar(row, "improvementttype") == "PenaltyFreeSustain"))
        {
            if (!DefaultEligible(effect)) continue;
            if (Scalar(effect, "unique", "") != "") throw Invalid();
            int threshold = Round(Decimal(effect, "val")), count = Integer(effect, "rating");
            if (count <= 0) throw Invalid();
            allowances[threshold] = checked(allowances.GetValueOrDefault(threshold) + count);
        }
        foreach (var allowance in allowances)
        {
            // Native greedily consumes the highest eligible force at each ascending
            // threshold, preserving lower thresholds for subsequent wider grants.
            foreach (int forceValue in penalized.Where(value => value <= allowance.Key)
                .OrderDescending().Take(allowance.Value).ToArray()) penalized.Remove(forceValue);
        }
        return (penalized.Count, checked(penalized.Count * (Boolean(graph, "psyche", false) ? -1 : -perObject)));
    }

    // Exact native Tradition.DrainValue path for a fixed source expression. Source
    // identity, metatype bounds and current provenance are checked by the caller.
    // The current bridge rejects unproven modifiers and never trusts a cached total.
    internal static bool TryEvaluateCurrentDrain(XElement original, XElement traditionSource,
        bool resonance, out int drainPool)
    {
        drainPool = 0;
        try
        {
            if (original.Name != "character" || traditionSource.Name != "tradition") return false;
            string expression = Scalar(traditionSource, "drain");
            if (!ValidExpression(expression) || Rows(original, "improvements", "improvement").Any(row =>
                Scalar(row, "improvementttype") == (resonance ? "FadingResistance" : "DrainResistance"))) return false;
            var number = ExpressionNumber(expression, name =>
            {
                var rows = Rows(original, "attributes", "attribute").Where(row => Scalar(row, "name") == name).ToArray();
                return rows is [var row] ? CurrentNaturalAttribute(original, row) : throw Invalid();
            });
            // Native converts the invariant XPath double to decimal before adding
            // resistance and StandardRound. Here source-proven resistance is zero.
            decimal value = number.Literal ?? Convert.ToDecimal(number.Evaluated);
            drainPool = Round(value); return true;
        }
        catch (Exception error) when (error is InvalidDataException or XmlException or InvalidOperationException
            or ArgumentException or FormatException or OverflowException) { return false; }
    }

    internal static bool TryEvaluateOwnedDrain5699(XElement traditionSource, bool resonance,
        CharacterSheetDerivedQuery.ImportedCareerSheet5699 imported, out int drainPool)
    {
        drainPool = 0;
        return imported.HasPrioritySelection9569
            && TryEvaluateReconciledDrain9569(traditionSource, resonance, imported.Sheet, imported.Owners, out drainPool)
            && imported.IsCurrent;
    }

    internal static bool TryEvaluateOwnedDrain5699(XElement traditionSource, bool resonance,
        CharacterSheetDerivedSection sheet, CharacterSheetOwnerGraph? owners, out int drainPool)
    {
        drainPool = 0;
        // Preserve the old unbound overload's imported-only contract.
        return owners?.ImportedPriority5699 == true
            && TryEvaluateReconciledDrain9569(traditionSource, resonance, sheet, owners, out drainPool);
    }

    private static bool TryEvaluateReconciledDrain9569(XElement traditionSource, bool resonance,
        CharacterSheetDerivedSection sheet, CharacterSheetOwnerGraph? owners, out int drainPool)
    {
        drainPool = 0;
        try
        {
            if (owners is null || sheet.UnavailableReason is not null
                || traditionSource.Name != "tradition") return false;
            string expression = Scalar(traditionSource, "drain");
            if (!ValidExpression(expression)) return false;
            var number = ExpressionNumber(expression, name => sheet.Attributes.Single(a => a.Name == name).Total);
            decimal value = number.Literal ?? Convert.ToDecimal(number.Evaluated);
            drainPool = Round(value + owners.Channel(resonance ? "FadingResistance" : "DrainResistance"));
            return true;
        }
        catch (Exception error) when (error is InvalidDataException or XmlException or InvalidOperationException
            or ArgumentException or FormatException or OverflowException) { return false; }
    }

    private static int CurrentNaturalAttribute(XElement graph, XElement row)
    {
        string name = Scalar(row, "name");
        if (!AttributeNames.Contains(name, StringComparer.Ordinal) || Integer(row, "base") < 0 || Integer(row, "karma") < 0
            || Integer(row, "metatypemin") < 0 || Integer(row, "metatypemax") < Integer(row, "metatypemin")) throw Invalid();
        foreach (var effect in Rows(graph, "improvements", "improvement"))
        {
            string type = Scalar(effect, "improvementttype"), target = Scalar(effect, "improvedname", "");
            if (type is "AttributeKarmaCost" or "AttributeKarmaCostMultiplier" or "AttributePointCost" or "AttributePointCostMultiplier") continue;
            bool enabling = type == "Attribute" && Scalar(effect, "unique", "") == "enableattribute";
            if (!enabling && (type.StartsWith("Attribute", StringComparison.Ordinal) || type == "ReplaceAttribute")
                && (target.Length == 0 || target.StartsWith(name, StringComparison.Ordinal))) throw Invalid();
        }
        int natural = NaturalAttribute(row);
        if (One(row, "totalvalue") is not null && Integer(row, "totalvalue") != natural) throw Invalid();
        return natural;
    }

    private static int NaturalAttribute(XElement row) => Math.Min(
        checked(Integer(row, "base") + Integer(row, "karma") + Integer(row, "metatypemin")), Integer(row, "metatypemax"));

    private static (decimal? Literal, double Evaluated) ExpressionNumber(string expression, Func<string, int> attribute)
    {
        if (TryLiteral(expression, out decimal literal)) return (literal, 0);
        string expanded = Regex.Replace(expression, @"\{([A-Za-z]+)\}", match =>
            attribute(match.Groups[1].Value).ToString(CultureInfo.InvariantCulture));
        try
        {
            if (new XElement("arithmetic").XPathEvaluate(NormalizeExpression(expanded)) is not double value
                || !double.IsFinite(value)) throw Invalid();
            return (null, value);
        }
        catch (Exception error) when (error is XPathException or ArgumentException or FormatException or OverflowException)
        { throw Invalid(); }
    }

    private static bool ValidExpression(string expression)
    {
        if (expression.Length is 0 or > 2048 || string.IsNullOrWhiteSpace(expression)) return false;
        if (TryLiteral(expression, out _)) return true;
        string numeric = NormalizeExpression(Regex.Replace(expression, @"\{([A-Za-z]+)\}", match =>
            AttributeNames.Contains(match.Groups[1].Value, StringComparer.Ordinal) ? "1" : "INVALID"));
        // An arithmetic-only subset: no XPath paths, functions or external data. Syntax
        // admission never evaluates a made-up attribute value or accepts invalid as zero.
        if (!Regex.IsMatch(numeric, @"\A(?:[0-9.\s()+*\-]|div)+\z", RegexOptions.CultureInvariant)) return false;
        try { return XPathExpression.Compile(numeric).ReturnType == XPathResultType.Number; }
        catch (XPathException) { return false; }
    }
    private static string NormalizeExpression(string text) => text.Replace("/", " div ", StringComparison.Ordinal)
        .Replace("\\", " div ", StringComparison.Ordinal).Replace("÷", " div ", StringComparison.Ordinal)
        .Replace(" x ", " * ", StringComparison.Ordinal).Replace('∙', '*').Replace('×', '*')
        .Replace('[', '(').Replace(']', ')').TrimStart('+');
    private static bool TryLiteral(string text, out decimal value)
    {
        value = 0;
        const string processing = "abcdfghijklmnopqrstuvwxyzABCDFGHIJKLMNOPQRSTUVWXYZ()[]{}!=<>&;+*/\\÷×∙";
        if (string.IsNullOrWhiteSpace(text) || text.IndexOfAny(processing.ToCharArray()) >= 0) return false;
        string trimmed = text.Trim();
        int minus = trimmed.LastIndexOf('-');
        return minus < 1 && (minus != 0 || trimmed.Length > 1 && char.IsDigit(trimmed[1]))
            && decimal.TryParse(trimmed, NumberStyles.Any, CultureInfo.InvariantCulture, out value);
    }
    private static int Round(decimal value) => checked((int)(value >= 0 ? decimal.Ceiling(value) : decimal.Floor(value)));
    private static XElement? One(XElement parent, string name)
    {
        var nodes = parent.Elements().Where(node => node.Name.LocalName == name).ToArray();
        if (nodes.Length > 1 || nodes.Length == 1 && nodes[0].Name != name) throw Invalid();
        return nodes.SingleOrDefault();
    }
    private static string Text(XElement node)
        => !node.HasAttributes && node.Nodes().All(child => child.GetType() == typeof(XText)) ? node.Value : throw Invalid();
    private static string Scalar(XElement parent, string name, string? absent = null)
        => One(parent, name) is { } node ? Text(node) : absent ?? throw Invalid();
    private static bool Boolean(XElement parent, string name, bool? absent = null)
        => bool.TryParse(Scalar(parent, name, absent?.ToString()), out bool value) ? value : throw Invalid();
    private static int Integer(XElement parent, string name, int? absent = null)
        => int.TryParse(Scalar(parent, name, absent?.ToString(CultureInfo.InvariantCulture)), NumberStyles.Integer,
            CultureInfo.InvariantCulture, out int value) ? value : throw Invalid();
    private static decimal Decimal(XElement parent, string name)
        => decimal.TryParse(Scalar(parent, name), NumberStyles.Number, CultureInfo.InvariantCulture, out decimal value) ? value : throw Invalid();
    private static XElement[] Rows(XElement parent, string container, string row)
    {
        if (One(parent, container) is not { } node) return [];
        if (node.HasAttributes || node.Nodes().Any(child => child is XElement element ? element.Name != row
            : child.GetType() != typeof(XText) || !string.IsNullOrWhiteSpace(((XText)child).Value))) throw Invalid();
        var rows = node.Elements().ToArray();
        if (rows.Any(child => child.HasAttributes)) throw Invalid();
        return rows;
    }
    private static InvalidDataException Invalid() => new(Unavailable);
}

internal sealed partial class CharacterCreationSelectedEffectsConsumer
{
    private CharacterCreationNativeMagicProjection1430? nativeMagic1430;
    internal CharacterCreationNativeMagicProjection1430? NativeMagic1430(CharacterCreationCompleteAssessment authority)
        => DigestFor(authority) is not null && nativeMagic1430 is { } result
            && result.GraphDigest == CharacterCreationFinalizationDigest.Compute(compiledGraph.ToString(SaveOptions.DisableFormatting)) ? result : null;
}

using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using Chummer.Application.Owners;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

/// <summary>
/// Career-only projection. Unsupported effects/prerequisite vocabulary block the affected
/// choice; derived display totals and Creation admission never confer purchase authority.
/// </summary>
public static partial class CharacterCareerInitiationProjection
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    public static bool TryQuote(OwnerContextStamp owner, WorkspaceStoredDocument saved,
        CharacterCareerInitiationSelection selection, string runtime, CharacterCareerInitiationSources sources,
        out CharacterCareerInitiationQuote? quote)
        => TryQuote(owner, saved, selection, runtime, sources, null, out quote);

    // Only the resolver admission supplies retained source context in production.
    // The original context-free projection remains fail-closed for implants.
    internal static bool TryQuote(OwnerContextStamp owner, WorkspaceStoredDocument saved,
        CharacterCareerInitiationSelection selection, string runtime, CharacterCareerInitiationSources sources,
        ICharacterSourceDataContext? context, out CharacterCareerInitiationQuote? quote,
        CharacterCurrentRuleEnvironmentConsumers.LearningAuthority5703? current = null)
    {
        quote = null;
        try
        {
            if (!owner.IsValid || selection is null || sources is null
                || !CharacterCareerQualityTransaction.IsValidOwner(owner.Owner)
                || saved.Document.RulesetId != "sr5" || saved.Document.Format != WorkspaceDocumentFormat.NativeXml
                || saved.Document.SchemaVersion != 1 || saved.Document.PayloadKind is not ("workspace" or "sr5/chum5-xml")
                || saved.ContentRevision <= 0 || saved.ContentRevision != saved.SavedRevision
                || !CharacterCareerInitiationRules.IsDigest(runtime)
                || saved.Document.AuxiliaryState.CharacterCreationFinalizationArchive?.CompletionAuthority?.Proposal.CampaignAuthority is not null)
                return false;
            if (current is not null && (context is null || !current.Matches(saved, context))) return false;
            XElement root = current is null ? Parse(saved.Document.Content, "character") : new XElement(current.Graph);
            XElement settings = Parse(sources.SettingsXml, "settings");
            if (!Boolean(root, "created")) return false;
            if (Boolean(root, "resenabled", false))
                return TrySubmersionQuote(owner, saved, selection, runtime, sources, context, root, settings, out quote, current);
            if (!Boolean(root, "magenabled", false)) return false;
            var blockers = new List<string>();
            if (selection.Mode != CharacterCareerInitiationMode.GradeAndBenefit || selection.TargetGradeId is not null)
                blockers.Add("initiation_mode_unsupported");
            // No silent substitution for a campaign authority that this source path cannot resolve.
            if (root.Elements().Any(x => x.Name.LocalName.Contains("campaign", StringComparison.OrdinalIgnoreCase)
                    && (x.HasElements || !string.IsNullOrWhiteSpace(x.Value))))
                blockers.Add("initiation_campaign_authority_unavailable");
            if (!Boolean(settings, "careerinitiationenabled", true))
                blockers.Add("initiation_profile_disabled");
            var magicState = current is null ? CharacterCareerMagicEvaluator.Evaluate(root, settings, sourceContext: context)
                : current.Magic ?? throw new InvalidDataException("initiation_imported_magic_unavailable");
            blockers.AddRange(magicState.Blockers);
            if (magicState.Blockers.Contains("magic_saved_grade_inconsistent"))
                blockers.Add("initiation_saved_grade_inconsistent");
            if (magicState.InitiationDisabled) blockers.Add("initiation_disabled");
            bool adept = magicState.IsAdept, magician = magicState.IsMagician;
            bool separate = magicState.SeparateAdeptMagic;
            int grade = Integer(root, "initiategrade");
            int next = checked(grade + 1);
            if (grade < 0 || next > CharacterCareerInitiationRules.MaximumGrade)
                blockers.Add("initiation_grade_invalid");
            string configuredCap = Scalar(settings, "careerinitiationmaximumgrade", "");
            if (configuredCap.Length > 0 && (!int.TryParse(configuredCap, NumberStyles.None, Invariant, out int cap)
                || cap < 0 || next > cap)) blockers.Add("initiation_profile_grade_cap");
            int magic = magicState.Magic;
            int? adeptMagic = magicState.AdeptMagic;
            if (next > magic || separate && next > adeptMagic)
                blockers.Add("initiation_magic_cap");
            int karma = Integer(root, "karma");
            decimal nuyen = Number(root, "nuyen");
            XElement karmaCosts = Container(settings, "karmacost");
            int flat = Integer(karmaCosts, "karmainitiationflat");
            int perGrade = Integer(karmaCosts, "karmainitiation");
            var adjustments = new[]
            {
                Adjustment("group", selection.Group, settings, root),
                Adjustment("ordeal", selection.Ordeal, settings, root),
                Adjustment("schooling", selection.Schooling, settings, root)
            };
            foreach (var adjustment in adjustments.Where(x => x.Selected))
                blockers.AddRange(adjustment.Blockers);
            decimal discount = adjustments.Where(x => x.Selected).Sum(x => x.Discount);
            int cost = CharacterCareerInitiationRules.Cost(flat, perGrade, next, discount);
            decimal nuyenCost = adjustments.Where(x => x.Selected).Sum(x => x.Nuyen);
            if (karma < cost) blockers.Add("initiation_insufficient_karma");
            if (nuyen < nuyenCost) blockers.Add("initiation_insufficient_nuyen");
            if (karma < 0 || nuyen < 0) blockers.Add("initiation_balance_invalid");

            // Training is an acknowledged obligation, not a claim of elapsed time or
            // a training calendar. Effective profile extensions can supply exact terms.
            string training = Scalar(settings, "careerinitiationtraining",
                "Complete initiation training for grade " + next.ToString(Invariant) + " (SR5 325).");
            var obligations = new List<string>();
            if (training.Length > 0) obligations.Add(training);
            obligations.AddRange(adjustments.Where(x => x.Selected && x.Obligation.Length > 0).Select(x => x.Obligation));
            if (obligations.Count > 0 && !selection.AcceptObligations)
                blockers.Add("initiation_obligations_not_accepted");
            if (training.Length > 4096 || obligations.Any(x => x.Length > 4096)) return false;
            bool ignoreArt = Boolean(settings, "ignoreart");
            bool allowPowerPoint = Boolean(settings, "careerinitiationpowerpointenabled", true);
            var benefits = sources.Metamagics.Select(x => Benefit(x, root, adept, magician, grade, ignoreArt, allowPowerPoint)).ToArray();
            if (benefits.Length > 4096 || benefits.Select(x => x.SourceId).Distinct().Count() != benefits.Length) return false;
            var recompilations = RecompileOwnedMetamagics(root, benefits, grade, next, blockers);
            var selected = benefits.SingleOrDefault(x => x.SourceId == selection.BenefitSourceId);
            if (selection.BenefitSourceId == Guid.Empty) blockers.Add("initiation_benefit_choice_required");
            else if (selected is null) blockers.Add("initiation_benefit_unavailable");
            else blockers.AddRange(selected.Blockers);
            var binding = new CharacterCareerInitiationBinding(owner.Owner.NormalizedValue, saved.Id.Value,
                owner.AuthorityInstanceId, owner.TransitionRevision, saved.ContentRevision, saved.SavedRevision,
                CharacterCareerInitiationRules.Hash(saved.Document.Content), saved.Document.AuxiliaryStateDigest, runtime);
            quote = new(binding, selection, grade, next, magic, adeptMagic, separate, karma, nuyen,
                cost, nuyenCost, magicState.EssenceSourceDigest is { } essenceDigest
                    ? CharacterCareerInitiationRules.Digest(new { Sources = sources, EssenceSourceDigest = essenceDigest })
                    : CharacterCareerInitiationRules.Digest(sources), adjustments, benefits,
                obligations.AsReadOnly(), blockers.Distinct(StringComparer.Ordinal).ToArray())
            { RecompiledMetamagics = recompilations.Count == 0 ? null : recompilations };
            return true;
        }
        catch (Exception e) when (e is XmlException or InvalidDataException or FormatException
            or InvalidOperationException or ArgumentException or OverflowException or NullReferenceException)
        { return false; }
    }

    private static CharacterCareerInitiationAdjustment Adjustment(string id, bool selected, XElement settings, XElement root)
    {
        var blockers = new List<string>();
        // Unlike the legacy UI, absent effective settings do not authorize discounts.
        string rate = Scalar(settings, "karmamaginitiation" + id + "percent", "");
        decimal discount = 0, nuyen = 0;
        if (rate.Length == 0) blockers.Add("initiation_" + id + "_rate_unavailable");
        else if (!decimal.TryParse(rate, NumberStyles.AllowDecimalPoint, Invariant, out discount)
            || discount is < 0 or > 1) throw new InvalidDataException();
        string admitted = Scalar(settings, "careerinitiation" + id + "enabled", "");
        if (!bool.TryParse(admitted, out bool enabled) || !enabled)
            blockers.Add("initiation_" + id + "_policy_not_admitted");
        string obligation = Scalar(settings, "careerinitiation" + id + "obligation", "");
        if (obligation.Length == 0) blockers.Add("initiation_" + id + "_obligation_unavailable");
        if (id == "group" && (!Boolean(root, "groupmember", false) || Scalar(root, "groupname", "").Length == 0))
            blockers.Add("initiation_group_membership_required");
        if (id == "schooling")
        {
            string expense = Scalar(settings, "careerinitiationschoolingnuyen", "");
            if (!decimal.TryParse(expense, NumberStyles.AllowDecimalPoint, Invariant, out nuyen) || nuyen < 0)
                blockers.Add("initiation_schooling_expense_unavailable");
        }
        // Explicit profile obligations authorize deferral; no UI flag certifies completion.
        return new(id, selected, discount, nuyen, obligation, blockers.AsReadOnly());
    }

    private static CharacterCareerInitiationBenefit Benefit(CharacterCareerInitiationSourceRow row, XElement root,
        bool adept, bool magician, int grade, bool ignoreArt, bool allowPowerPoint, bool adding = true)
    {
        XElement source = Parse(row.Xml, "metamagic");
        var blockers = new List<string>();
        Guid id = Guid.Parse(Scalar(source, "id"));
        string name = Scalar(source, "name"), book = Scalar(source, "source"), page = Scalar(source, "page");
        if (id == Guid.Empty || name.Length == 0 || name.Length > 512 || book.Length == 0) throw new InvalidDataException();
        if (!row.Enabled) blockers.Add("initiation_source_disabled");
        if (!(adept && Boolean(source, "adept") || magician && Boolean(source, "magician")))
            blockers.Add("initiation_benefit_talent_ineligible");
        string limit = Scalar(source, "limit", "1");
        int count = Container(root, "metamagics").Elements("metamagic")
            .Count(x => Scalar(x, "sourceid", "") == id.ToString("D") || Scalar(x, "name", "") == name);
        if (!limit.Equals("False", StringComparison.OrdinalIgnoreCase))
        {
            if (!int.TryParse(limit, NumberStyles.None, Invariant, out int maximum) || maximum < 1)
                blockers.Add("initiation_benefit_limit_unsupported");
            else if (adding ? count >= maximum : count > maximum) blockers.Add("initiation_benefit_duplicate");
        }
        // A false branch cannot conceal unsupported prerequisite vocabulary.
        foreach (string kind in new[] { "required", "forbidden" })
        {
            var conditions = source.Elements(kind).ToArray();
            if (conditions.Length > 1) throw new InvalidDataException();
            if (conditions.Length == 1)
            {
                bool? result = Requirement(conditions[0], root, grade, ignoreArt, 0);
                if (result is null) blockers.Add("initiation_prerequisite_unsupported");
                else if (kind == "required" ? !result.Value : result.Value)
                    blockers.Add("initiation_prerequisite_not_met");
            }
        }
        var allowed = new HashSet<string>(StringComparer.Ordinal)
        { "id", "name", "adept", "magician", "source", "page", "limit", "required", "forbidden", "bonus", "notes", "translate", "altpage" };
        if (source.Elements().Any(x => !allowed.Contains(x.Name.LocalName) || x.Name.Namespace != XNamespace.None || x.HasAttributes))
            blockers.Add("initiation_source_semantics_unsupported");
        XElement[] bonuses = source.Elements("bonus").ToArray();
        if (bonuses.Length > 1) throw new InvalidDataException();
        if (bonuses.Any(x => x.Nodes().OfType<XText>().Any(t => !string.IsNullOrWhiteSpace(t.Value))))
            blockers.Add("initiation_benefit_effect_unsupported");
        var effects = bonuses.SingleOrDefault()?.Elements().ToArray() ?? [];
        decimal points = 0;
        var benefitKind = CharacterCareerInitiationBenefitKind.Metamagic;
        if (effects.Length == 1 && effects[0].Name == "adeptpowerpoints" && !effects[0].HasElements
            && !effects[0].HasAttributes && decimal.TryParse(effects[0].Value, NumberStyles.AllowDecimalPoint,
                Invariant, out points) && points is > 0 and <= 100)
        {
            benefitKind = CharacterCareerInitiationBenefitKind.PowerPoint;
            if (!adept) blockers.Add("initiation_power_points_require_adept");
            if (!allowPowerPoint) blockers.Add("initiation_power_points_policy_disabled");
        }
        else if (effects.Length > 0)
        {
            points = 0;
            if (!TryCompileMetamagicEffects(source, id, checked(grade + 1), out _))
                blockers.Add("initiation_benefit_effect_unsupported");
        }
        return new(id, name, book, page, row.Xml, benefitKind, points, blockers.AsReadOnly());
    }

    private static IReadOnlyList<CharacterCareerInitiationMetamagicRecompilation> RecompileOwnedMetamagics(
        XElement root, CharacterCareerInitiationBenefit[] benefits,
        int grade, int next, List<string> blockers)
    {
        var result = new List<CharacterCareerInitiationMetamagicRecompilation>();
        var ids = new HashSet<Guid>();
        XElement container = Container(root, "metamagics");
        if (container.Elements().Count() > 4096 || container.Elements().Any(x => x.Name != "metamagic")
            || container.Nodes().OfType<XText>().Any(x => !string.IsNullOrWhiteSpace(x.Value)))
        {
            blockers.Add("initiation_existing_metamagic_shape_unsupported");
            return result;
        }
        foreach (var owned in container.Elements("metamagic"))
        {
            try
            {
                Guid instance = Guid.Parse(Scalar(owned, "guid"));
                if (instance == Guid.Empty || !ids.Add(instance))
                    throw new InvalidDataException("initiation_existing_metamagic_identity_invalid");
                XElement copied = Container(owned, "bonus");
                // Older native saves omit sourceid on effect-free metamagics. They
                // need no recompilation and must not become invented source bindings.
                if (Scalar(owned, "sourceid", "").Length == 0 && !copied.HasElements
                    && string.IsNullOrWhiteSpace(copied.Value) && OwnedEffects(root, instance).Length == 0)
                    continue;
                Guid sourceId = Guid.Parse(Scalar(owned, "sourceid"));
                var benefit = benefits.SingleOrDefault(x => x.SourceId == sourceId);
                if (benefit is null
                    || benefit.Blockers.Any(x => x is "initiation_source_disabled" or "initiation_source_semantics_unsupported"
                        or "initiation_benefit_effect_unsupported" or "initiation_benefit_talent_ineligible")
                    || owned.HasAttributes || Scalar(owned, "improvementsource", "Metamagic") != "Metamagic"
                    || Scalar(owned, "name") != benefit.Name || Scalar(owned, "source") != benefit.Book
                    || Scalar(owned, "extra", "").Length != 0 || Scalar(owned, "forced", "").Length != 0)
                    throw new InvalidDataException("initiation_existing_metamagic_source_unavailable");
                XElement source = Parse(benefit.SourceXml, "metamagic");
                XElement authoritative = Container(source, "bonus");
                if (!MatchesMetamagicBonus(copied, authoritative))
                    throw new InvalidDataException("initiation_existing_metamagic_bonus_mismatch");
                bool dependent = authoritative.DescendantNodes().OfType<XText>()
                    .Any(x => x.Value.Contains("Rating", StringComparison.Ordinal));
                // Fixed PP grants retain their existing serializer and pool semantics.
                if (benefit.Kind == CharacterCareerInitiationBenefitKind.PowerPoint && !dependent) continue;
                if (!TryCompileMetamagicEffects(source, instance, grade, out var before)
                    || !TryCompileMetamagicEffects(source, instance, next, out var after))
                    throw new InvalidDataException("initiation_grade_dependent_effect_unsupported");
                XElement[] actual = OwnedEffects(root, instance);
                if (!EquivalentMetamagicEffects(actual, before))
                    throw new InvalidDataException("initiation_existing_metamagic_effect_mismatch");
                if (!dependent) continue;
                string[] fields = ["guid", "sourceid", "name", "source", "page", "grade", "bonus",
                    "improvementsource", "paidwithkarma", "extra", "forced", "notes", "notesColor"];
                if (owned.Elements().Any(x => x.Name.Namespace != XNamespace.None || !fields.Contains(x.Name.LocalName)
                    || x.HasAttributes || x.Name != "bonus" && x.HasElements)
                    || owned.Elements().GroupBy(x => x.Name).Any(x => x.Count() != 1))
                    throw new InvalidDataException("initiation_existing_metamagic_shape_unsupported");
                int acquired = Integer(owned, "grade");
                if (acquired < 1 || acquired > grade)
                    throw new InvalidDataException("initiation_existing_metamagic_grade_invalid");
                result.Add(new(instance, sourceId, owned.ToString(SaveOptions.DisableFormatting), benefit.SourceXml,
                    actual.Select(x => x.ToString(SaveOptions.DisableFormatting)).ToArray(),
                    after.Select(x => x.ToString(SaveOptions.DisableFormatting)).ToArray()));
            }
            catch (Exception e) when (e is InvalidDataException or FormatException or InvalidOperationException or ArgumentException)
            {
                blockers.Add(e is InvalidDataException && e.Message.StartsWith("initiation_", StringComparison.Ordinal)
                    ? e.Message : "initiation_existing_metamagic_source_unavailable");
                if (owned.Elements("bonus").Any(x => x.ToString().Contains("Rating", StringComparison.Ordinal)))
                    blockers.Add("initiation_grade_dependent_effect_unsupported");
            }
        }
        return result.AsReadOnly();
    }

    // Native readers retain source indentation through XmlReader or discard it
    // through XElement.Parse. Admit only those two exact source representations;
    // never normalize the owned bonus, scalar values, fields, or source identity.
    internal static bool MatchesMetamagicBonus(XElement copied, XElement authoritative)
        => XNode.DeepEquals(copied, authoritative)
            || XNode.DeepEquals(copied, XElement.Parse(authoritative.ToString(SaveOptions.DisableFormatting)));

    // Bind the native metamagic Rating parameter only at understood numeric leaves.
    // The shared compiler still owns effect parsing, type mapping and serialization;
    // this adapter neither evaluates arbitrary expressions nor grants a talent/quality.
    internal static bool TryCompileMetamagicEffects(XElement source, Guid instance, int grade, out XElement[] effects)
    {
        effects = [];
        try
        {
            if (instance == Guid.Empty || grade is < 0 or > CharacterCareerInitiationRules.MaximumGrade) return false;
            XElement bonus = new(Container(source, "bonus"));
            if (bonus.Elements().Count() > 128 || bonus.DescendantsAndSelf().Any(x => x.HasAttributes
                || x.Name.Namespace != XNamespace.None || x.Nodes().OfType<XProcessingInstruction>().Any())) return false;
            foreach (var effect in bonus.Elements())
            {
                XElement numeric;
                if (effect.Name == "specificskill")
                {
                    if (effect.Elements().Any(x => x.Name.LocalName is not ("name" or "bonus" or "condition" or "applytorating"))
                        || Scalar(effect, "condition", "").Length != 0
                        || Boolean(effect, "applytorating", false)) return false;
                    numeric = effect.Elements("bonus").Single();
                }
                else if (effect.Name.LocalName is "pathogencontactresist" or "pathogeningestionresist"
                    or "pathogeninhalationresist" or "pathogeninjectionresist" or "toxincontactresist"
                    or "toxiningestionresist" or "toxininhalationresist" or "toxininjectionresist") numeric = effect;
                else return false;
                if (numeric.HasElements) return false;
                if (numeric.Value == "Rating") numeric.Value = grade.ToString(Invariant);
            }
            string xml = source.ToString(SaveOptions.DisableFormatting);
            string digest = CharacterCareerInitiationRules.Hash(xml);
            // CompileBonus's source carrier is shared with talent bonuses. No quality
            // instance, access flag, choice or generated gear is admitted here.
            var carrier = new CharacterCreationTalentQualitySource(Scalar(source, "id"), "", Scalar(source, "id"),
                Scalar(source, "name"), Scalar(source, "source"), Scalar(source, "page"), digest, digest, xml, digest, []);
            var compiled = new List<XElement>();
            var flags = new HashSet<string>(StringComparer.Ordinal);
            var gear = new List<(XElement Saved, CharacterCreationTalentGearSource Source)>();
            string extra = CharacterCreationAwakenedLegacyProjector.CompileBonus(bonus, "", [], carrier,
                instance.ToString("D"), flags, compiled, gear);
            if (extra.Length != 0 || flags.Count != 0 || gear.Count != 0) return false;
            foreach (var effect in compiled) effect.SetElementValue("improvementsource", "Metamagic");
            if (compiled.Any(x => x.ToString(SaveOptions.DisableFormatting).Length > 65536)) return false;
            effects = compiled.ToArray();
            return true;
        }
        catch (Exception e) when (e is InvalidDataException or FormatException or InvalidOperationException
            or ArgumentException or OverflowException) { return false; }
    }

    internal static XElement[] OwnedEffects(XElement root, Guid instance)
        => Container(root, "improvements").Elements("improvement")
            .Where(x => Guid.TryParse(Scalar(x, "sourcename", ""), out var id) && id == instance).ToArray();

    internal static bool EquivalentMetamagicEffects(IEnumerable<XElement> actual, IEnumerable<XElement> expected)
    {
        // Normalize native boolean/numeric spelling and presentation metadata only.
        // Extra behavioral fields, duplicate leaves, disabled or foreign-source rows fail.
        string Key(XElement original)
        {
            XElement row = new(original);
            string[] numbers = ["min", "max", "aug", "augmax", "val", "rating"];
            string[] booleans = ["custom", "addtorating", "enabled"];
            string[] fields = ["target", "unique", "improvedname", "sourcename", "exclude", "condition",
                "improvementttype", "improvementsource", "customname", "customid", "customgroup"];
            if (row.Name != "improvement" || row.HasAttributes || row.Elements().Any(x => x.HasAttributes || x.HasElements
                || x.Name.Namespace != XNamespace.None) || row.Elements().GroupBy(x => x.Name).Any(x => x.Count() != 1)
                || row.Nodes().OfType<XText>().Any(x => !string.IsNullOrWhiteSpace(x.Value))) throw new InvalidDataException();
            row.Elements().Where(x => x.Name.LocalName is "order" or "notes" or "notesColor").Remove();
            if (row.Elements().Any(x => !numbers.Contains(x.Name.LocalName) && !booleans.Contains(x.Name.LocalName)
                && !fields.Contains(x.Name.LocalName))) throw new InvalidDataException();
            if (row.Element("unique") is null) row.Add(new XElement("unique", ""));
            foreach (string name in numbers) row.SetElementValue(name, Number(row, name).ToString("G29", Invariant));
            foreach (string name in booleans)
            {
                string value = Scalar(row, name);
                row.SetElementValue(name, value is "0" or "1" ? value == "1" : bool.Parse(value));
            }
            row.SetElementValue("sourcename", Guid.Parse(Scalar(row, "sourcename")).ToString("D"));
            return new XElement("improvement", numbers.Concat(booleans).Concat(fields).Order(StringComparer.Ordinal)
                .Select(name => new XElement(name, Scalar(row, name))))
                .ToString(SaveOptions.DisableFormatting);
        }
        return actual.Select(Key).Order(StringComparer.Ordinal).SequenceEqual(expected.Select(Key).Order(StringComparer.Ordinal));
    }

    private static bool? Requirement(XElement node, XElement root, int grade, bool ignoreArt, int depth)
    {
        if (depth > 16 || node.HasAttributes || node.Name.Namespace != XNamespace.None) return null;
        string kind = node.Name.LocalName;
        if (kind is "required" or "forbidden" or "allof" or "oneof")
        {
            var children = node.Elements().Take(129).ToArray();
            if (children.Length is 0 or > 128) return null;
            var values = children.Select(x => Requirement(x, root, grade, ignoreArt, depth + 1)).ToArray();
            if (values.Any(x => !x.HasValue)) return null;
            return kind is "oneof" or "forbidden" ? values.Any(x => x == true) : values.All(x => x == true);
        }
        if (node.HasElements) return null;
        if (kind == "initiategrade")
            return int.TryParse(node.Value, NumberStyles.None, Invariant, out int minimum) ? grade >= minimum : null;
        if (kind == "art" && ignoreArt) return true;
        string? container = kind switch { "art" => "arts", "quality" => "qualities", "metamagic" => "metamagics", _ => null };
        if (container is null) return null;
        return Container(root, container).Elements(kind)
            .Any(x => Scalar(x, "name", "") == node.Value || Scalar(x, "sourceid", "") == node.Value);
    }

    internal static XElement Parse(string xml, string name)
    {
        using var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings
        { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 16 * 1024 * 1024 });
        XElement root = XElement.Load(reader);
        if (root.Name != name || root.HasAttributes) throw new InvalidDataException();
        return root;
    }

    internal static XElement Container(XElement parent, string name)
    {
        var rows = parent.Elements(name).Take(2).ToArray();
        if (rows.Length > 1 || rows.Length == 1 && rows[0].HasAttributes) throw new InvalidDataException();
        return rows.SingleOrDefault() ?? new XElement(name);
    }

    internal static string Scalar(XElement parent, string name, string? fallback = null)
    {
        var rows = parent.Elements(name).Take(2).ToArray();
        if (rows.Length == 0 && fallback is not null) return fallback;
        if (rows.Length != 1 || rows[0].HasAttributes || rows[0].HasElements) throw new InvalidDataException();
        return rows[0].Value;
    }

    internal static decimal Number(XElement parent, string name, decimal? fallback = null)
        => decimal.Parse(Scalar(parent, name, fallback?.ToString(Invariant)), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, Invariant);
    internal static int Integer(XElement parent, string name)
        => int.Parse(Scalar(parent, name), NumberStyles.AllowLeadingSign, Invariant);
    internal static bool Boolean(XElement parent, string name, bool? fallback = null)
        => bool.Parse(Scalar(parent, name, fallback?.ToString()));
}

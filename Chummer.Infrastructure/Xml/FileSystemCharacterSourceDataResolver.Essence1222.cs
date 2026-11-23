using System.Globalization;
using System.Runtime.CompilerServices;
using System.Xml;
using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using E1222 = Chummer.Application.Characters.CharacterCreationEssenceRules1222;

namespace Chummer.Infrastructure.Xml;

public sealed partial class FileSystemCharacterSourceDataResolver
{
    private sealed partial class RuleSourceAccounting
    {
        private sealed record EssenceExecutionReceipt(CharacterCreationEssenceExecution1222 Execution,
            CharacterCreationEssenceSettingEvidence? Settings, string SettingsDigest);
        private readonly ConditionalWeakTable<CharacterCreationEssenceInvocation1222,
            EssenceExecutionReceipt> essenceExecutions1222 = new();

        public override CharacterCreationEssenceExecution1222? ExecuteEssence1222(CharacterCreationEssenceInvocation1222 invocation)
        {
            essenceExecutions1222.Remove(invocation);
            if (inputs.BaseDependencyRecipe2330 != RetainedCharacterRuleSourceInputs.EssenceDependencyRecipe1244
                || !CapturePurchases || !captureCompleteSkillCaps || invocation.CaptureDigest != captureDigest
                || invocation.OriginalXml != inputs.CharacterXml
                || invocation.CapturePolicyDigest != inputs.PolicyDigest) return null;
            try
            {
                using var operation = resolver.CreateOperationScope();
                if (operation.TryCreateContext(inputs.CharacterXml) is not SourceDataContext context
                    || !context.MatchesRetainedSelection(inputs)
                    || !context.TryResolveCreationEffectivePolicy(out var policy) || policy is null
                    || policy.PolicyDigest != inputs.PolicyDigest
                    || !context.TryExecuteEssence1222(invocation, out var execution)) return null;
                essenceExecutions1222.Add(invocation, new(execution!, execution!.SettingsEvidence, CharacterCreationFinalizationDigest.Compute(execution.SettingsEvidence)));
                return execution;
            }
            // The interpreter uses InvalidDataException for validation refusals;
            // it derives from SystemException, not IOException.
            catch (Exception e) when (e is InvalidDataException or IOException or InvalidOperationException or ArgumentException
                or OverflowException or XmlException or FormatException or DivideByZeroException) { return null; }
        }

        public override bool HasExecutedEssence1222(CharacterCreationEssenceInvocation1222 invocation,
            CharacterCreationEssenceExecution1222 execution)
            => invocation.CaptureDigest == captureDigest
                && invocation.CapturePolicyDigest == inputs.PolicyDigest
                && execution.CapturePolicyDigest == invocation.CapturePolicyDigest
                && execution.PolicyDigest == invocation.PolicyDigest
                && essenceExecutions1222.TryGetValue(invocation, out var original)
                && ReferenceEquals(original.Execution, execution)
                && ReferenceEquals(original.Settings, execution.SettingsEvidence)
                && original.SettingsDigest == CharacterCreationFinalizationDigest.Compute(execution.SettingsEvidence)
                && execution.GraphDigest == CharacterCreationFinalizationDigest.Compute(execution.GraphXml);
    }

    private sealed partial class SourceDataContext
    {
        private static void ExecuteEssenceProfile1222(XElement profile,
            CharacterCreationRuleSourceScopeInvocation invocation, SourceScopeLedger ledger)
        {
            ExecuteEssenceSettings(profile, invocation, ledger);
            if (!invocation.TryGetEssence1222(out var execution) || execution is null || !execution.Result.Valid
                || execution.GraphDigest != invocation.GraphDigest) return;
            var leaves = profile.Elements().Where(row => row.Name.LocalName == "essenceformat").ToArray();
            if (leaves is not [var leaf] || leaf.Name != "essenceformat" || leaf.HasAttributes
                || leaf.Nodes().ToArray() is not [XText text] || text.GetType() != typeof(XText)) return;
            ledger.Tree(leaf, CharacterCreationSourceOperationDisposition.Compiled,
                new { Operation = "Finish-character-Essence1222", execution.ExecutionDigest,
                    execution.SourceDigest, execution.Result, invocation.GraphDigest });
        }

        // Additional private dependencies; no original policy source row is removed,
        // reordered or declared inactive. Old captures cannot invent these bytes.
        private bool CaptureEssenceSources1222()
        {
            foreach (string file in new[] { "cyberware.xml", "bioware.xml" })
            {
                foreach (var directory in _customDirectories)
                    _ = EnumerateSourceFiles(directory.Path, "*_" + file, SearchOption.AllDirectories);
                if (!TryComputeEffectiveInputDigest(_catalog, file, out _)
                    || !TryLoadEffectiveDocument(_catalog, file, out var document) || document?.Root is null) return false;
            }
            return !_sourceInputs.HasSourceDrift;
        }

        internal bool TryExecuteEssence1222(CharacterCreationEssenceInvocation1222 invocation,
            out CharacterCreationEssenceExecution1222? execution)
        {
            execution = null;
            using var read = _sourceInputs.Enter();
            if (!_sourceInputs.HasRetainedEssenceRecipe1244 || _sourceInputs.HasSourceDrift
                || invocation.ProfileId != _settingsProfileId
                || !TryResolveCreationEffectivePolicy(out var policy) || policy is null
                || invocation.CapturePolicyDigest != policy.PolicyDigest
                || invocation.ProfileDigest != policy.ProfileInputsDigest) return false;
            // Reconstruct typed dependency data from this exact retained context.
            // No caller flag, public result, or ambient catalogue supplies this boundary.
            if (!TryReadEssenceDependencies1244(out var dependencies1244) || dependencies1244 is null
                || dependencies1244.Recipe != RetainedCharacterRuleSourceInputs.EssenceDependencyRecipe1244
                || dependencies1244.PolicyDigest != invocation.CapturePolicyDigest
                || dependencies1244.ProfileInputsDigest != invocation.ProfileDigest
                || dependencies1244.BooksInputsDigest != policy.BooksInputsDigest) return false;
            var documents = new Dictionary<string, XElement>(StringComparer.Ordinal);
            var inventory = new List<object>();
            // Replay the full retained inventory, including negative/unselected
            // source branches. Supplemental ware bytes belong to this same capture.
            foreach (var source in policy.Sources.Where(s => s.Kind == CharacterCreationPolicySourceKind.Rules))
            {
                string file = source.SourceId == "profile" ? SettingsFile : source.SourceId + ".xml";
                if (!NoOverlayForSource(file)
                    || !TryHasSelectedCustomDataInputFor(_customDirectories, file, out bool custom) || custom
                    || !TryComputeEffectiveInputDigest(_catalog, file, out string digest)
                    || (source.SourceId == "profile" ? BindSelectedProfile(digest, _settingsProfileId) : digest) != source.ContentDigest
                    || !TryLoadEffectiveDocument(_catalog, file, out var document) || document?.Root is null) return false;
                documents.Add(source.SourceId, document.Root);
                inventory.Add(new { source.SourceId, source.Order, source.IdentityDigest, source.ContentDigest, Digest = digest });
            }
            foreach (string scope in new[] { "cyberware", "bioware" })
            {
                if (!NoOverlayForSource(scope + ".xml")
                    || !TryHasSelectedCustomDataInputFor(_customDirectories, scope + ".xml", out bool custom) || custom
                    || !TryComputeEffectiveInputDigest(_catalog, scope + ".xml", out string digest)
                    || !TryLoadEffectiveDocument(_catalog, scope + ".xml", out var document) || document?.Root is null) return false;
                documents.Add(scope, document.Root);
                inventory.Add(new { SourceId = scope, Supplemental = true, Digest = digest });
            }
            XElement profile = documents["profile"].DescendantsAndSelf("setting")
                .Single(row => (string?)row.Element("id") == invocation.ProfileId);
            if (!XNode.DeepEquals(profile, XElement.Parse(invocation.ProfileXml, LoadOptions.PreserveWhitespace))) return false;
            if (!CharacterCreationEssenceSettingDefinitions.TryRead(profile, out var essenceSettings)) return false;
            string format = EssenceEngine1222.Text(profile, "essenceformat");
            if (!TryProfileFormat1202(format, true, out _, out int decimals)) return false;
            var graph = invocation.CreateInterpreterGraph1351();
            if (CharacterCreationFinalizationDigest.Compute(invocation.GraphXml) != invocation.GraphDigest) return false;
            var engine = new EssenceEngine1222(graph, profile, documents, invocation.MetatypeId, invocation.MetavariantId, decimals,
                book => TryIsBookEnabled(book, out bool enabled) && enabled, dependencies1244)
            { PhysicalTalentAttributeEnableRows1291 = invocation.PhysicalTalentAttributeEnableRows1291, NativeStackInvocation1351 = invocation };
            var calculated = engine.Run();
            if (!calculated.Valid || _sourceInputs.HasSourceDrift) return false;
            var settingsEvidence = engine.BindSettings(essenceSettings!, calculated);
            string xml = graph.ToString(SaveOptions.DisableFormatting);
            string sources = CharacterCreationFinalizationDigest.Compute(new
            {
                Recipe = dependencies1244.Recipe, Dependencies = dependencies1244.InventoryDigest,
                Inventory = inventory, Sources = engine.SourceBindings
            });
            string digestResult = CharacterCreationFinalizationDigest.Compute(new
            {
                Operation = "Finish-character-Essence1222", invocation.CaptureDigest, invocation.CapturePolicyDigest, invocation.PolicyDigest,
                invocation.ProfileId, invocation.ProfileDigest, invocation.RequestDigest, invocation.InputsDigest,
                invocation.OwnerRevisionDigest, invocation.GraphDigest, Sources = sources, Format = format,
                engine.Unrounded, engine.MaximumOnly, engine.ShownValue, engine.Acquisition,
                engine.WareTrace, engine.Effects, engine.PrototypeTranshuman, invocation.PhysicalTalentAttributeEnableRows1291,
                Result = calculated, Projected = xml
            });
            if (settingsEvidence is not null)
                digestResult = CharacterCreationFinalizationDigest.Compute(new { Original = digestResult, Settings = settingsEvidence });
            execution = new(xml, CharacterCreationFinalizationDigest.Compute(xml), sources, digestResult, calculated,
                invocation.ProfileId, invocation.ProfileDigest, CharacterCreationFinalizationDigest.Compute(invocation.ProfileXml),
                invocation.OwnerRevisionDigest, format, decimals, engine.Unrounded, engine.MaximumOnly, engine.ShownValue, engine.Acquisition, invocation.CapturePolicyDigest, invocation.PolicyDigest) { SettingsEvidence = settingsEvidence };
            return true;
        }
    }

    // Exact native/source interpreter. It never accepts saved calculated ESS or
    // a report as an authority. All selected sources/grades and active effects
    // are re-resolved from this operation's retained effective documents.
    private sealed partial class EssenceEngine1222(XElement graph, XElement profile,
        IReadOnlyDictionary<string, XElement> documents, string metatypeId, string? metavariantId, int decimals,
        Func<string, bool> enabledBook, EssenceDependencyInventory1244 dependencies1244)
    {
        internal sealed record Effect(string Owner, string Source, string Type, decimal Value, string SourceDigest,
            string Name = "", int Minimum = 0, int Maximum = 0, decimal Augmented = 0, int AugmentedMaximum = 0,
            int Rating = 1, decimal PrototypeAllowance = 0);
        internal readonly List<object> SourceBindings = new();
        internal readonly List<object> WareTrace = new();
        internal readonly List<Effect> Effects = new();
        internal IReadOnlyList<int> PhysicalTalentAttributeEnableRows1291 { get; init; } = [];
        internal bool Unrounded { get; private set; }
        internal bool MaximumOnly { get; private set; }
        internal bool ShownValue { get; private set; }
        internal decimal Acquisition { get; private set; }
        internal decimal PrototypeTranshuman { get; private set; }
        internal CharacterCreationEssenceInvocation1222? NativeStackInvocation1351 { get; init; }
        private readonly Dictionary<XElement, XElement> sources = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<XElement, string> scopes = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<string, decimal> variables = new(StringComparer.Ordinal);
        private readonly Dictionary<XElement, E1222.NonRetroactiveSnapshot> careerSnapshots = new(ReferenceEqualityComparer.Instance);
        private const string Hole = "b57eadaa-7c3b-4b80-8d79-cbbd922c1196";
        private const string AntiHole = "961eac53-0c43-4b19-8741-2872177a3a4c";
        private static readonly IReadOnlyDictionary<string, string> effectTypes = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["essencepenalty"] = "EssencePenalty", ["essencepenaltyt100"] = "EssencePenaltyT100",
            ["essencepenaltymagonlyt100"] = "EssencePenaltyMAGOnlyT100",
            ["essencepenaltyresonlyt100"] = "EssencePenaltyRESOnlyT100", ["essencepenaltydeponlyt100"] = "EssencePenaltyDEPOnlyT100",
            ["specialattburnmultiplier"] = "SpecialAttBurn", ["specialatttotalburnmultiplier"] = "SpecialAttTotalBurnMultiplier",
            ["cyberwareessmultiplier"] = "CyberwareEssCost", ["cyberwaretotalessmultiplier"] = "CyberwareTotalEssMultiplier",
            ["cyberwareessmultipliernonretroactive"] = "CyberwareEssCostNonRetroactive",
            ["cyberwaretotalessmultipliernonretroactive"] = "CyberwareTotalEssMultiplierNonRetroactive",
            ["biowareessmultiplier"] = "BiowareEssCost", ["biowaretotalessmultiplier"] = "BiowareTotalEssMultiplier",
            ["biowareessmultipliernonretroactive"] = "BiowareEssCostNonRetroactive",
            ["biowaretotalessmultipliernonretroactive"] = "BiowareTotalEssMultiplierNonRetroactive",
            ["basicbiowareessmultiplier"] = "BasicBiowareEssCost", ["genetechessmultiplier"] = "GenetechEssMultiplier",
            ["cyborgessence"] = "CyborgEssence", ["prototypetranshuman"] = "PrototypeTranshuman", ["essencemax"] = "EssenceMax",
            ["adeptpowerpoints"] = "AdeptPowerPoints"
        };

        internal E1222.Result Run()
        {
            Require(graph.Name == "character" && !Flag(graph, "created", false));
            _ = Number(graph, "prototypetranshuman", 0m); // Observation only; reject ambiguous shape.
            Require(Text(graph, "metatypecategory", "") is not ("Cyberzombie" or "A.I."));
            Unrounded = ProfileFlag("donotroundessenceinternally");
            MaximumOnly = ProfileFlag("esslossreducesmaximumonly");
            ShownValue = ProfileFlag("specialkarmacostbasedonshownvalue");
            UnclampMinimum = ProfileFlag("unclampattributeminimum");
            foreach (var attribute in Container(graph, "attributes").Elements("attribute"))
            {
                string id = Text(attribute, "name");
                // No stale special total may become a circular ware/effect input.
                // Such source expressions need a separate native dependency solver.
                if (id is not ("ESS" or "MAG" or "MAGAdept" or "RES" or "DEP"))
                    variables.Add(id, Number(attribute, "totalvalue"));
            }
            XElement metatype = Definition("metatypes", "metatype", metatypeId);
            XElement heritage = string.IsNullOrEmpty(metavariantId) || metavariantId == Guid.Empty.ToString("D") ? metatype
                : metatype.Descendants("metavariant").Single(row => Text(row, "id") == metavariantId);
            decimal maximum = Expression(Text(heritage, "essmax", Text(metatype, "essmax")), 0);
            SourceBindings.Add(new { Metatype = metatypeId, Metavariant = metavariantId, Digest = Digest(heritage) });
            BindSources();
            ReadEffects(heritage, "Heritage", metatypeId, 0);
            foreach (var pair in sources)
            {
                var saved = pair.Key;
                if (Active(saved)) ReadEffects(pair.Value, SourceType(saved), Text(saved, "guid"), Integer(saved, "rating", 0));
            }
            // Native eligibility uses the accumulated character scalar, never a
            // saved grant or Any(positive effect). Zero and negative terms count.
            PrototypeTranshuman = Effects.Sum(e => e.PrototypeAllowance);
            VerifySavedEffects();
            // Attribute.Core.cs:481 includes EssenceMax in ESS.MetatypeMaximum.
            maximum = checked(maximum + E1222.Round(Sum("EssenceMax")));
            var specials = new List<E1222.Attribute>();
            foreach (string id in new[] { "MAG", "MAGAdept", "RES", "DEP" })
            {
                bool enabled = id switch
                {
                    "MAG" => Flag(graph, "magenabled", false),
                    "MAGAdept" => Flag(graph, "magenabled", false) && Flag(graph, "adept", false)
                        && Flag(graph, "magician", false) && ProfileFlag("mysadeptsecondmagattribute"),
                    "RES" => Flag(graph, "resenabled", false),
                    _ => Flag(graph, "depenabled", false)
                };
                var rows = Container(graph, "attributes").Elements("attribute").Where(a => Text(a, "name") == id).ToArray();
                Require(rows.Length <= 1 && (!enabled || rows.Length == 1));
                if (rows.Length == 1) specials.Add(AttributeComponents(rows[0], id, enabled));
            }
            // Validate and snapshot all descendants, even where the parent contributes
            // zero Essence. Creation's serialized extras are observations, not inputs.
            foreach (var saved in graph.Descendants("cyberware").Where(row => row.Parent?.Name == "children" || row.Parent?.Name == "cyberwares"))
            {
                _ = Number(saved, "extraessadditivemultiplier", 0m);
                _ = Number(saved, "extraessmultiplicativemultiplier", 1m);
                string prefix = Text(saved, "improvementsource");
                bool forceNone = sources.TryGetValue(saved, out var source) && Text(source, "forcegrade", "") == "None";
                careerSnapshots.Add(saved, E1222.Snapshot(forceNone,
                    Effects.Where(e => e.Type == prefix + "EssCostNonRetroactive").Select(e => e.Value),
                    Effects.Where(e => e.Type == prefix + "TotalEssMultiplierNonRetroactive").Select(e => e.Value)));
            }
            Acquisition = AcquisitionEssence(maximum, specials.Any(a => a.Enabled));
            // Reading inventory must preserve absence for the original-owned graph admission.
            decimal ware = Container(graph, "cyberwares", create: false).Elements("cyberware").Sum(row => Ware(row, null));
            // Native saves use cyberwares/cyberware for both kinds. No separate
            // installed-ware container is silently ignored.
            Require(!graph.Elements("biowares").Any(e => e.HasElements));
            decimal additiveBurn = 1m - Effects.Where(e => e.Type == "SpecialAttBurn").Sum(e => 1m - e.Value / 100m);
            decimal totalBurn = Product("SpecialAttTotalBurnMultiplier");
            executedAttributes = Array.AsReadOnly(specials.Where(row => row.Enabled).ToArray());
            var result = E1222.Calculate(maximum, ware, Sum("EssencePenalty") + Sum("EssencePenaltyT100") / 100m,
                Sum("EssencePenaltyMAGOnlyT100") / 100m, Sum("EssencePenaltyRESOnlyT100") / 100m,
                Sum("EssencePenaltyDEPOnlyT100") / 100m, Acquisition, additiveBurn, totalBurn, decimals,
                Unrounded, MaximumOnly, ShownValue, Effects.Any(e => e.Type == "CyborgEssence"),
                Flag(graph, "adept", false) && Flag(graph, "magician", false) && !ProfileFlag("mysadeptsecondmagattribute"), specials);
            result = result with { NonEssencePowerPoints = Sum("AdeptPowerPoints") };
            if (result.Valid) Apply(result);
            return result;
        }

        private void BindSources()
        {
            if (NativeStackInvocation1351 is { } invocation) Require(invocation.MatchesInterpreterGraph1351(graph));
            else Require(!graph.Descendants().Any(n => n.Name.LocalName == "stackedfocus"));
            var instances = graph.Descendants().Where(row => row.Element("sourceid") is not null
                && !row.Ancestors().Any(a => a.Name.LocalName is "bonus" or "wirelessbonus" or "pairbonus" or "wirelesspairbonus"))
                .ToArray();
            var ids = new HashSet<Guid>();
            foreach (var saved in instances)
            {
                string? scope = saved.Name.LocalName switch
                {
                    "quality" => "qualities", "gear" => "gear", "weapon" => "weapons", "armor" => "armor",
                    "spell" => "spells", "complexform" => "complexforms", "power" => "powers", "lifestyle" => "lifestyles",
                    // The native projector emits a tradition node for both kinds.
                    "tradition" => Text(saved, "traditiontype") switch
                    {
                        "MAG" => "traditions", "RES" => "streams",
                        _ => throw new InvalidDataException("Unsupported native tradition type.")
                    },
                    "cyberware" => Text(saved, "improvementsource") == "Bioware" ? "bioware" : "cyberware",
                    _ => null
                };
                if (scope is null)
                {
                    // Absence of a copied bonus is not proof of absent mechanics
                    // in an unexamined installed catalogue.
                    throw new InvalidDataException("Unsupported source-bearing Essence owner.");
                }
                string id = Text(saved, "sourceid");
                string instance = Text(saved, "guid");
                Require(Guid.TryParse(instance, out var nativeId) && nativeId != Guid.Empty && ids.Add(nativeId));
                if (NativeStackInvocation1351 is { } nativeStack && nativeStack.IsNativeStackOwner1351(graph, saved))
                {
                    // The actual native owner has no catalogue row. Its complete
                    // shape/relationships came from the private native producer;
                    // descendants still traverse retained source/bonus validation.
                    Require(saved.Name == "gear" && saved.Parent?.Parent == graph
                        && saved.Parent?.Name == "gears" && id == Guid.Empty.ToString("D")
                        && Text(saved, "category") == "Stacked Focus");
                    SourceBindings.Add(new { Scope = "native-stacked-focus1351", Instance = instance,
                        ProducerGraph = nativeStack.GraphDigest, Native = Digest(saved) });
                    continue;
                }
                Require(saved.Name != "gear" || Text(saved, "category", "") != "Stacked Focus");
                if (saved.Name == "cyberware") Require(Text(saved, "improvementsource") is "Cyberware" or "Bioware");
                if (saved.Name == "cyberware" && id is Hole or AntiHole)
                {
                    // Sentinels have retained native source rows too. Their early
                    // arithmetic branch does not waive source/grade/flag identity.
                    Require(scope == "cyberware");
                    var sentinel = Definition(scope, "cyberware", id);
                    Require(Text(saved, "ess") == Text(sentinel, "ess")
                        && Text(saved, "category") == Text(sentinel, "category")
                        && Text(saved, "grade") == "None" && Text(sentinel, "forcegrade") == "None"
                        && Flag(saved, "addtoparentess", false) == SourcePresence(sentinel, "addtoparentess"));
                    int sentinelRating = Integer(saved, "rating", 0);
                    Require(sentinelRating >= 0 && sentinelRating <= Integer(sentinel, "rating"));
                    var sentinelGrade = documents[scope].Element("grades")!.Elements("grade").Single(g => Text(g, "name") == "None");
                    RequireDependencyTree1244(scope, "grades", "grade", sentinelGrade);
                    foreach (string field in new[] { "bonus", "wirelessbonus", "pairbonus", "wirelesspairbonus" })
                        Require(!saved.Elements(field).Any(e => e.HasElements));
                    sources.Add(saved, sentinel); scopes.Add(saved, scope);
                    SourceBindings.Add(new { Scope = scope, SourceId = id, Instance = instance, Sentinel = true,
                        Digest = Digest(sentinel), Grade = Digest(sentinelGrade), Native = Digest(saved) });
                    continue;
                }
                string element = scope == "bioware" ? "bioware" : saved.Name.LocalName;
                XElement source = Definition(scope, element, id);
                if (saved.Name == "cyberware")
                {
                    Require(Text(saved, "ess") == Text(source, "ess") && Text(saved, "category") == Text(source, "category"));
                    Require(Text(saved, "improvementsource") is "Cyberware" or "Bioware");
                    Require(Flag(saved, "isgeneware", SourcePresence(source, "isgeneware")) == SourcePresence(source, "isgeneware"));
                    Require(Flag(saved, "addtoparentess", SourcePresence(source, "addtoparentess")) == SourcePresence(source, "addtoparentess"));
                    // Unmodelled modular installation state cannot be silently counted as installed.
                    Require(Text(saved, "plugsintomodularmount", "").Length == 0
                        && Text(source, "plugsintomodularmount", "").Length == 0);
                    int rating = Integer(saved, "rating", 0);
                    Require(rating >= Expression(Text(source, "minrating", "0"), rating)
                        && rating <= Expression(Text(source, "rating", "0"), rating));
                    string gradeName = Text(saved, "grade");
                    var grades = documents[scope].Element("grades")?.Elements("grade")
                        .Where(g => Text(g, "name") == gradeName || Text(g, "id") == gradeName).ToArray() ?? [];
                    Require(grades.Length == 1 && Number(grades[0], "ess") >= 0);
                    RequireDependencyTree1244(scope, "grades", "grade", grades[0]);
                    string force = Text(source, "forcegrade", "");
                    Require(force.Length == 0 || Text(grades[0], "name") == force);
                    Require(Text(saved, "forcegrade", force) == force);
                }
                sources.Add(saved, source); scopes.Add(saved, scope);
                SourceBindings.Add(new { Scope = scope, SourceId = id, Instance = instance, Digest = Digest(source), Native = Digest(saved) });
                foreach (string field in new[] { "bonus", "wirelessbonus", "pairbonus", "wirelesspairbonus" })
                {
                    var native = saved.Elements(field).ToArray(); Require(native.Length <= 1);
                    // A nonempty copied effect subtree must equal the retained source.
                    if (native is [var copied] && copied.HasElements)
                        Require(source.Elements(field).ToArray() is [var retained]
                            && CopiedBonusMatchesSource1313(copied, retained));
                }
            }
            foreach (var ware in graph.Descendants().Where(w => w.Name.LocalName == "cyberware" && !w.Ancestors("bonus").Any()))
            {
                Require(ware.Name == "cyberware");
                // Weapon.Save persists a scalar flag, not an installed ware row.
                // Its owner must already have passed retained source binding.
                if (ware.Parent is { } weapon && weapon.Name == "weapon" && sources.ContainsKey(weapon))
                {
                    Require(bool.TryParse(Text(weapon, "cyberware"), out _));
                    continue;
                }
                Require(ware.Element("sourceid") is not null && (ware.Parent?.Name == "cyberwares" || ware.Parent?.Name == "children"));
            }
        }

        // The native producer parses strings without formatting whitespace, while
        // retained sources use an XmlReader that reports it. Compare only that
        // proven difference: line-breaking XML indentation in element-only content.
        // Never rewrite either operand or use this comparison for source identity.
        private static bool CopiedBonusMatchesSource1313(XElement copied, XElement? retained)
        {
            if (retained is null || copied.Name != retained.Name
                || copied.Attributes().Count() != retained.Attributes().Count()
                || copied.Attributes().Any(a => (string?)retained.Attribute(a.Name) != a.Value)
                || Space(copied) != Space(retained)) return false;

            bool ignoreIndentation = Space(copied) == "default"
                && ElementOnly(copied) && ElementOnly(retained);
            var left = copied.Nodes().Where(n => !ignoreIndentation || !Indentation(n)).ToArray();
            var right = retained.Nodes().Where(n => !ignoreIndentation || !Indentation(n)).ToArray();
            if (left.Length != right.Length) return false;
            for (int i = 0; i < left.Length; i++)
            {
                if (left[i] is XElement child)
                {
                    if (right[i] is not XElement sourceChild
                        || !CopiedBonusMatchesSource1313(child, sourceChild)) return false;
                }
                else if (left[i].NodeType != right[i].NodeType || !XNode.DeepEquals(left[i], right[i]))
                    return false;
            }
            return true;

            static string Space(XElement element) => element.AncestorsAndSelf()
                .Attributes(XNamespace.Xml + "space").FirstOrDefault()?.Value ?? "default";
            static bool ElementOnly(XElement element) => element.HasElements
                && element.Nodes().All(n => n is XElement || Indentation(n));
            static bool Indentation(XNode node) => node.GetType() == typeof(XText)
                && ((XText)node).Value.Any(c => c is '\r' or '\n')
                && ((XText)node).Value.All(c => c is ' ' or '\t' or '\r' or '\n');
        }


        private XElement Definition(string scope, string name, string id)
        {
            Require(documents.ContainsKey(scope));
            var rows = documents[scope].Descendants(name).Where(row => (string?)row.Element("id") == id).ToArray();
            Require(rows.Length == 1);
            if (scope is "cyberware" or "bioware")
                RequireDependencyTree1244(scope, name + "s", name, rows[0]);
            string book = Text(rows[0], "source", "");
            if (book.Length != 0) Require(enabledBook(book));
            return rows[0];
        }

        private void RequireDependencyTree1244(string scope, string container, string entry, XElement source)
        {
            // Both checks are required: exact enabled root identity and the complete
            // retained tree, including every source-bearing descendant. Do not filter it.
            Require(dependencies1244.TryResolveEnabledRoot(scope, container, entry,
                Text(source, "id", ""), Text(source, "name", ""), out var retainedXml)
                && retainedXml == source.ToString(SaveOptions.DisableFormatting)
                && dependencies1244.MatchesEnabledTree(scope, container, entry, source));
        }

        private void ReadEffects(XElement source, string kind, string owner, int rating)
        {
            foreach (string field in new[] { "bonus", "wirelessbonus", "pairbonus", "wirelesspairbonus" })
            {
                var rows = source.Elements().Where(e => e.Name.LocalName == field).ToArray();
                Require(rows.Length <= 1 && rows.All(e => e.Name == field));
                var bonus = rows.SingleOrDefault(); if (bonus is null) continue;
                var relevant = bonus.Descendants().Where(e => effectTypes.ContainsKey(e.Name.LocalName)
                    || e.Name.LocalName is "specificattribute" or "attributelevel" or "attributemaxclamp"
                        or "replaceattributes" or "selectattribute").ToArray();
                if (relevant.Length == 0) continue;
                Require(!bonus.HasAttributes);
                var saved = sources.Keys.SingleOrDefault(row => Text(row, "guid") == owner);
                if (field == "wirelessbonus" && saved is not null && !Flag(saved, "wirelesson", false)) continue;
                // Pair effects need a pair selection, not an assumed multiplier.
                Require(field is "bonus" or "wirelessbonus");
                foreach (var effect in relevant)
                {
                    Require(effect.Parent == bonus && !effect.HasAttributes && effect.Name.NamespaceName.Length == 0);
                    if (effect.Name.LocalName is "specificattribute" or "attributelevel" or "attributemaxclamp")
                    {
                        ReadAttributeEffect(effect, kind, owner, rating);
                        continue;
                    }
                    Require(effectTypes.ContainsKey(effect.Name.LocalName));
                    Require(effect.Parent == bonus && !effect.HasAttributes && !effect.HasElements && effect.Name.NamespaceName.Length == 0);
                    if (effect.Name == "prototypetranshuman")
                    {
                        // AddImprovementCollection.cs:3379 (and async:3522) stores
                        // the expression as ImprovedName, with zero Value and the
                        // producer's rating. The evaluated amount belongs to Character.
                        Require(field == "bonus"); // Wireless owner/suffix is not modelled.
                        int nativeRating = kind switch
                        {
                            "Quality" or "Spell" or "ComplexForm" or "Tradition" => 1,
                            "Cyberware" or "Bioware" or "Gear" or "Armor" => rating,
                            _ => throw new InvalidDataException("Unsupported Prototype Transhuman source rating.")
                        };
                        Effects.Add(new(owner, kind, "PrototypeTranshuman", 0m, Digest(effect),
                            effect.Value, Rating: nativeRating, PrototypeAllowance: Expression(effect.Value, nativeRating)));
                        continue;
                    }
                    decimal value = effect.Name == "cyborgessence" ? 0m : Expression(effect.Value, rating);
                    Effects.Add(new(owner, kind, effectTypes[effect.Name.LocalName], value, Digest(effect)));
                }
            }
        }

        private void VerifySavedEffects()
        {
            var expected = Effects.ToList();
            int occurrence1291 = -1;
            foreach (var saved in Container(graph, "improvements").Elements("improvement"))
            {
                ++occurrence1291;
                string type = Text(saved, "improvementttype");
                string name = Text(saved, "improvedname", "");
                Require(type != "ReplaceAttribute");
                if (PhysicalTalentAttributeEnableRows1291.Contains(occurrence1291))
                {
                    // The private producer attests the complete native shape at
                    // this ordinal in the digest-checked invocation graph. These
                    // redundant channel checks do not authorize other occurrences.
                    Require(type == "Attribute" && (name is "MAG" or "RES" or "DEP")
                        && Text(saved, "improvementsource") == "Quality"
                        && Text(saved, "unique") == "enableattribute"
                        && Number(saved, "val") == 0m && Integer(saved, "rating") == 0);
                    continue;
                }
                if (Text(saved, "improvementsource") is "EssenceLoss" or "EssenceLossChargen") continue;
                bool attribute = (type is "Attribute" or "Attributelevel" or "AttributeMaxClamp") && IsSpecial(name);
                if (!effectTypes.Values.Contains(type) && !attribute)
                {
                    Require(!type.Contains("Ess", StringComparison.Ordinal) && !type.Contains("SpecialAttBurn", StringComparison.Ordinal));
                    continue;
                }
                Require(Flag(saved, "enabled", true) && Text(saved, "condition", "").Length == 0
                    && Text(saved, "unique", "").Length == 0 && !Flag(saved, "custom", false));
                string owner = Text(saved, "sourcename"); string kind = Text(saved, "improvementsource");
                decimal value = Number(saved, "val");
                int savedRating = type == "PrototypeTranshuman" ? Integer(saved, "rating") : Integer(saved, "rating", 1);
                Require(!Flag(saved, "addtorating", false));
                int index = expected.FindIndex(e => e.Owner == owner && e.Source == kind && e.Type == type && e.Value == value
                    && e.Rating == savedRating
                    && e.Name == name && e.Minimum == Integer(saved, "min", 0) && e.Maximum == Integer(saved, "max", 0)
                    && e.Augmented == Number(saved, "aug", 0m) && e.AugmentedMaximum == Integer(saved, "augmax", 0));
                Require(index >= 0); expected.RemoveAt(index);
            }
            // Missing native derived rows are compiled from the actual source;
            // forged or detached saved values above are never substituted.
            foreach (var missing in expected)
            {
                var row = Improvement(missing.Type, missing.Name, missing.Source, missing.Owner,
                    missing.Value, missing.Minimum, missing.Maximum, missing.Augmented);
                row.SetElementValue("augmax", missing.AugmentedMaximum);
                row.SetElementValue("rating", missing.Rating);
                Container(graph, "improvements").Add(row);
            }
        }

        private static bool IsSpecial(string name) => name is "MAG" or "MAGAdept" or "RES" or "DEP"
            or "MAGBase" or "MAGAdeptBase" or "RESBase" or "DEPBase";

        private void ReadAttributeEffect(XElement effect, string kind, string owner, int rating)
        {
            string name = effect.Name == "attributemaxclamp" ? effect.Value : Text(effect, "name");
            if (!IsSpecial(name)) return;
            if (effect.Name == "attributemaxclamp")
            {
                Require(!effect.HasElements);
                Effects.Add(new(owner, kind, "AttributeMaxClamp", 0, Digest(effect), name));
                return;
            }
            Require(effect.Elements().All(e => e.Name.NamespaceName.Length == 0 && !e.HasAttributes && !e.HasElements
                && e.Name.LocalName is "name" or "val" or "min" or "max" or "aug" or "affectbase"));
            if (effect.Name == "attributelevel")
            {
                Require(effect.Elements().All(e => e.Name.LocalName is "name" or "val"));
                Effects.Add(new(owner, kind, "Attributelevel", Integer(effect, "val", 1), Digest(effect), name));
                return;
            }
            if (effect.Elements("affectbase").Any())
            {
                Require(effect.Elements("affectbase").Count() == 1);
                name += "Base"; // Native presence test, not the value of this marker.
            }
            string maxExpression = Text(effect, "max", "0");
            int maximum = maxExpression.EndsWith("-natural", StringComparison.Ordinal)
                ? checked(E1222.Round(Expression(maxExpression[..^8], rating)) - Integer(Container(graph, "attributes")
                    .Elements("attribute").Single(a => Text(a, "name") == name.Replace("Base", "")), "metatypemax"))
                : E1222.Round(Expression(maxExpression, rating));
            Effects.Add(new(owner, kind, "Attribute", 0, Digest(effect), name,
                E1222.Round(Expression(Text(effect, "min", "0"), rating)), maximum,
                Expression(Text(effect, "val", "0"), rating), E1222.Round(Expression(Text(effect, "aug", "0"), rating))));
        }

        private E1222.Attribute AttributeComponents(XElement row, string id, bool enabled)
        {
            var effects = Effects.Where(e => e.Type == "Attribute" && (e.Name == id || e.Name == id + "Base")).ToArray();
            int minimum = Integer(row, "metatypemin"), maximum = Integer(row, "metatypemax");
            return new(id, enabled, minimum, maximum, Integer(row, "metatypeaugmax"),
                Integer(row, "base"), Integer(row, "karma"), UnclampMinimum,
                E1222.Round(Math.Min(Effects.Where(e => e.Type == "Attributelevel" && e.Name == id).Sum(e => e.Value), maximum - minimum)),
                effects.Sum(e => e.Minimum), effects.Sum(e => e.Maximum),
                E1222.Round(effects.Where(e => e.Name == id + "Base").Sum(e => e.Augmented)),
                effects.Where(e => e.Name == id).Sum(e => e.Augmented),
                effects.Where(e => e.Name == id).Sum(e => e.AugmentedMaximum), Effects.Any(e => e.Type == "AttributeMaxClamp" && e.Name == id));
        }

        private decimal AcquisitionEssence(decimal maximum, bool enabled)
        {
            var saved = graph.Elements("essenceatspecialstart").ToArray(); Require(saved.Length <= 1);
            if (!enabled) return decimal.MinValue;
            if (saved.Length == 1)
            {
                decimal acquisition = Number(graph, "essenceatspecialstart");
                if (acquisition != decimal.MinValue)
                { Require(acquisition <= maximum); return acquisition; }
            }
            // Creation's optimal enabling quality: take the enabling source with
            // its own largest penalty first. This is not current/post-ware Essence.
            var penalties = new List<decimal>();
            foreach (var pair in sources.Where(p => p.Key.Name == "quality"))
            {
                var enabledAttributes = pair.Value.Element("bonus")?.Elements("enableattribute")
                    .Select(e => Text(e, "name")).Where(e => e is "MAG" or "RES" or "DEP").ToArray() ?? [];
                if (enabledAttributes.Length == 0) continue;
                string owner = Text(pair.Key, "guid");
                foreach (string attribute in enabledAttributes)
                {
                    decimal penalty = Effects.Where(e => e.Owner == owner && e.Type == "EssencePenalty").Sum(e => e.Value)
                        + Effects.Where(e => e.Owner == owner && (e.Type == "EssencePenaltyT100" || e.Type == "EssencePenalty" + attribute + "OnlyT100")).Sum(e => e.Value) / 100m;
                    if (penalty != 0) penalties.Add(penalty);
                }
            }
            return checked(maximum + (penalties.Count == 0 ? 0 : penalties.Min()));
        }

        private decimal Ware(XElement saved, XElement? inheritedGrade, string? inheritedGradeScope = null)
        {
            string id = Text(saved, "sourceid"); int rating = Integer(saved, "rating", 0);
            bool child = saved.Parent?.Name == "children";
            bool add = id is Hole or AntiHole ? Flag(saved, "addtoparentess", false)
                : SourcePresence(sources[saved], "addtoparentess");
            Require(Flag(saved, "addtoparentess", add) == add);
            // Native exclusion precedes even the hole/anti-hole sentinel branch.
            if (child && !add) { WareTrace.Add(new { SourceId = id, Child = true, AddToParent = false, Essence = 0m }); return 0m; }
            if (id is Hole or AntiHole)
            {
                Require(rating >= 0 && !Container(saved, "children").HasElements);
                decimal hole = rating / (id == Hole ? 100m : -100m);
                WareTrace.Add(new { SourceId = id, Rating = rating, Essence = hole }); return hole;
            }
            var source = sources[saved]; string scope = scopes[saved];
            bool prototype = Flag(saved, "prototypetranshuman", false);
            Require(!prototype || scope == "bioware");
            if (prototype && PrototypeTranshuman > 0m)
            { WareTrace.Add(new { SourceId = id, Prototype = true, Allowance = PrototypeTranshuman, Essence = 0m }); return 0m; }
            string gradeName = Text(saved, "grade");
            XElement grade = inheritedGrade ?? documents[scope].Element("grades")!.Elements("grade")
                .Single(g => Text(g, "name") == gradeName || Text(g, "id") == gradeName);
            string force = Text(source, "forcegrade", "");
            // Native descendant calculation receives the parent's grade even for
            // forcegrade None children. The child's own grade was checked in BindSources.
            if (inheritedGrade is null && force.Length != 0) Require(Text(grade, "name") == force);
            // Native children receive the parent's exact grade object. Its source
            // identity stays in the parent's catalogue even for a mixed-family child.
            string gradeScope = inheritedGradeScope ?? scope;
            RequireDependencyTree1244(gradeScope, "grades", "grade", grade);
            // Creation loading ignores the saved nonretroactive snapshot. Only
            // the live creation effects below participate in this calculation.
            decimal additive = Number(grade, "ess");
            decimal multiplicative = 1m;
            if (Flag(saved, "suite", false)) additive -= 0.1m;
            decimal discount = Number(saved, "essdiscount", 0m);
            Require(discount >= -100 && discount <= 100 && (discount == 0 || ProfileFlag("allowessencediscounts")));
            multiplicative *= 1m - discount / 100m;
            bool gene = SourcePresence(source, "isgeneware");
            Require(Flag(saved, "isgeneware", gene) == gene);
            if (force != "None" || gene)
            {
                string prefix = scope == "bioware" ? "Bioware" : "Cyberware";
                var additiveTypes = gene ? new[] { "GenetechEssMultiplier" }
                    : new[] { prefix + "EssCost", prefix + "EssCostNonRetroactive" };
                foreach (string type in additiveTypes) additive -= Effects.Where(e => e.Type == type).Sum(e => 1m - e.Value / 100m);
                if (!gene)
                {
                    multiplicative *= Product(prefix + "TotalEssMultiplier") * Product(prefix + "TotalEssMultiplierNonRetroactive");
                    if (scope == "bioware" && Text(source, "category") == "Basic")
                        additive -= Effects.Where(e => e.Type == "BasicBiowareEssCost").Sum(e => 1m - e.Value / 100m);
                }
            }
            variables.Remove("Modifier"); // This token belongs only to the post-modifier expression.
            decimal raw = Expression(Text(source, "ess"), rating, Integer(source, "minrating", 0));
            Require(raw >= 0);
            decimal modifier = Math.Max(0, checked(additive * multiplicative));
            string post = Text(profile, "essencemodifierpostexpression", "{Modifier}");
            variables["Modifier"] = modifier;
            if (post.Length != 0) modifier = Expression(post, rating, Integer(source, "minrating", 0));
            decimal own = checked(raw * modifier);
            if (!Unrounded) own = decimal.Round(own, decimals, MidpointRounding.AwayFromZero);
            // Ordinary retained geneware forces None. An exceptional source that
            // permits a nonretroactive Career snapshot can change cost at the
            // transition although creation geneware ignores that live modifier.
            // Do not certify creation totals as Career totals in that case.
            if (gene && force != "None")
            {
                var snapshot = careerSnapshots[saved];
                decimal careerModifier = Math.Max(0m, checked((additive + snapshot.Additive)
                    * multiplicative * snapshot.Multiplicative));
                variables["Modifier"] = careerModifier;
                if (post.Length != 0) careerModifier = Expression(post, rating, Integer(source, "minrating", 0));
                decimal careerOwn = checked(raw * careerModifier);
                if (!Unrounded) careerOwn = decimal.Round(careerOwn, decimals, MidpointRounding.AwayFromZero);
                if (careerOwn != own)
                    throw new InvalidDataException("Geneware Career Essence transition requires distinct acquisition/loss authority.");
            }
            decimal total = own;
            foreach (var nested in Container(saved, "children").Elements())
            { Require(nested.Name == "cyberware"); total += Ware(nested, grade, gradeScope); }
            WareTrace.Add(new { SourceId = id, Instance = Text(saved, "guid"), Source = Digest(source), Grade = Digest(grade),
                Rating = rating, Raw = raw, Additive = additive, Multiplicative = multiplicative, Post = post, Modifier = modifier,
                Own = own, Total = total, Prototype = prototype, PrototypeAllowance = PrototypeTranshuman, Unrounded, Decimals = decimals });
            return total;
        }

        private void Apply(E1222.Result result)
        {
            var improvements = Container(graph, "improvements");
            var existing = improvements.Elements("improvement").Where(e => Text(e, "improvementsource") is "EssenceLoss" or "EssenceLossChargen").ToArray();
            // No double application from already reduced graph output.
            foreach (var old in existing) old.Remove();
            foreach (var loss in result.Attributes)
            {
                var attribute = Container(graph, "attributes").Elements("attribute").Single(a => Text(a, "name") == loss.Id);
                attribute.SetElementValue("totalvalue", Number(loss.Current));
                if (loss.MinimumReduction != 0 || loss.MaximumReduction != 0 || loss.AugmentedReduction != 0)
                    improvements.Add(Improvement("Attribute", loss.Id, "EssenceLossChargen", "", 0,
                        -loss.MinimumReduction, -loss.MaximumReduction, -loss.AugmentedReduction));
            }
            if (result.PowerPointLoss != 0)
                improvements.Add(Improvement("AdeptPowerPoints", "", "EssenceLossChargen", "", -result.PowerPointLoss, 0, 0, 0));
            // Character save/load persists this scalar independently of improvements.
            // Reconcile absent/stale saved observations from source only after success.
            graph.SetElementValue("prototypetranshuman", Number(PrototypeTranshuman));
            graph.SetElementValue("essenceatspecialstart", Number(Acquisition));
            graph.SetElementValue("totaless", Number(result.Essence));
            // CompileSelectedGraph is shared by review/full/selected/recovery.
            // The Career flag and this graph are committed atomically by the
            // existing transaction; there is no store write or partial handoff here.
            foreach (var pair in careerSnapshots)
            {
                // Native Cyberware.Save/Load clears the selected flag without a
                // positive character allowance. Such ware pays ordinary Essence.
                if (PrototypeTranshuman <= 0m && Flag(pair.Key, "prototypetranshuman", false))
                    pair.Key.SetElementValue("prototypetranshuman", "False");
                pair.Key.SetElementValue("extraessadditivemultiplier", Number(pair.Value.Additive));
                pair.Key.SetElementValue("extraessmultiplicativemultiplier", Number(pair.Value.Multiplicative));
            }
        }

        private static XElement Improvement(string type, string name, string source, string owner,
            decimal value, int min, int max, decimal augmented)
            => new("improvement", new XElement("target"), new XElement("improvedname", name), new XElement("sourcename", owner),
                new XElement("improvementttype", type), new XElement("improvementsource", source), new XElement("unique"),
                new XElement("val", Number(value)), new XElement("min", min), new XElement("max", max),
                new XElement("aug", augmented), new XElement("augmax", 0), new XElement("rating", 1),
                new XElement("enabled", "1"), new XElement("custom", "False"), new XElement("customname"),
                new XElement("customid"), new XElement("customgroup"), new XElement("condition"), new XElement("exclude"),
                new XElement("addtorating", "0"), new XElement("order", "0"), new XElement("notes"), new XElement("notesColor", "#000000"));
        private bool ProfileFlag(string name)
        {
            string value = Text(profile, name, "False");
            Require(value == value.Trim() && bool.TryParse(value, out _));
            return bool.Parse(value);
        }
        private decimal Sum(string type) => Effects.Where(e => e.Type == type).Sum(e => e.Value);
        private decimal Product(string type) => Effects.Where(e => e.Type == type).Aggregate(1m, (p, e) => checked(p * e.Value / 100m));
        private bool Active(XElement saved) => saved.Name.LocalName switch
        {
            "gear" or "armor" or "weapon" => Flag(saved, "equipped", true),
            _ => true
        };
        private static string SourceType(XElement saved) => saved.Name.LocalName switch
        {
            "cyberware" => Text(saved, "improvementsource"), "complexform" => "ComplexForm",
            _ => char.ToUpperInvariant(saved.Name.LocalName[0]) + saved.Name.LocalName[1..]
        };
        private decimal Expression(string expression, int rating, int minimum = 0)
        {
            variables["Rating"] = rating; variables["MinRating"] = minimum;
            return CharacterCreationEssenceExpression1222.Evaluate(expression, variables);
        }
        internal static string Text(XElement parent, string name, string? absent = null)
        {
            var nodes = parent.Elements().Where(e => e.Name.LocalName == name).ToArray();
            if (nodes.Length == 0 && absent is not null) return absent;
            Require(nodes is [var leaf] && leaf.Name == name && !leaf.HasAttributes && !leaf.HasElements
                && leaf.Nodes().All(n => n.GetType() == typeof(XText)));
            return nodes[0].Value;
        }
        private static decimal Number(XElement parent, string name, decimal? absent = null)
            => decimal.Parse(Text(parent, name, absent?.ToString(CultureInfo.InvariantCulture)), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
        private static int Integer(XElement parent, string name, int? absent = null)
        { decimal value = Number(parent, name, absent); Require(decimal.Truncate(value) == value); return checked((int)value); }
        private static bool Flag(XElement parent, string name, bool absent)
            => Text(parent, name, absent ? "True" : "False") switch
            { "True" or "true" or "1" => true, "False" or "false" or "0" => false, _ => throw new InvalidDataException("Ambiguous Essence flag.") };
        // Native catalogue flags are enabled by presence unless literally False.
        // Saved/profile booleans above deliberately keep their stricter grammar.
        private static bool SourcePresence(XElement parent, string name)
        {
            var rows = parent.Elements().Where(e => e.Name.LocalName == name).ToArray();
            if (rows.Length == 0) return false;
            Require(rows.Length == 1);
            return Text(parent, name) != "False";
        }
        private static XElement Container(XElement parent, string name, bool create = true)
        {
            var rows = parent.Elements().Where(e => e.Name.LocalName == name).ToArray();
            Require(rows.Length <= 1 && rows.All(e => e.Name == name && !e.HasAttributes));
            if (rows.Length == 1) return rows[0];
            var added = new XElement(name); if (create) parent.Add(added); return added;
        }
        private static string Digest(XElement value) => CharacterCreationFinalizationDigest.Compute(value.ToString(SaveOptions.DisableFormatting));
        private static string Number(decimal value) => value.ToString(CultureInfo.InvariantCulture);
        private static void Require(bool value) { if (!value) throw new InvalidDataException("Source-bound Essence input is not exact."); }
    }
}

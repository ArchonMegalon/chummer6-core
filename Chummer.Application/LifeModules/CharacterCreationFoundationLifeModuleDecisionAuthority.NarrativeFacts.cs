using System.Globalization;
using Chummer.Contracts.Characters;
using Chummer.Contracts.LifeModules;

namespace Chummer.Application.LifeModules;

public sealed partial class CharacterCreationFoundationLifeModuleDecisionAuthority
{
    // Snapshot ONLY when accepting a new decision. Never enrich/reseal historical
    // acceptances: their chapter/source identities may already have authored prose.
    // One bounded fact per occurrence avoids exporting a row per improvement.
    internal static OriginCanonicalNarrativeFact CreateContributionFact(
        string moduleFactId, string decisionId, LifeModuleDecisionAuthorityChoice choice,
        string locale, IReadOnlyList<LifeModuleEffectContribution>? contributions)
    {
        bool de = locale.StartsWith("de", StringComparison.OrdinalIgnoreCase);
        bool es = locale.StartsWith("es", StringComparison.OrdinalIgnoreCase);
        string Text(string en, string german, string spanish) => de ? german : es ? spanish : en;
        string Amount(decimal value) => value.ToString("+0.############################;-0.############################;0",
            CultureInfo.InvariantCulture);
        string unavailable = Text("Mechanical contributions unavailable; do not infer rewards.",
            "Mechanische Beiträge nicht verfügbar; keine Vorteile ableiten.",
            "Aportaciones mecánicas no disponibles; no inferir recompensas.");
        var rows = new List<string>();
        bool unverified = contributions is null;
        foreach (var item in contributions ?? [])
        {
            if (item.CompilationStatus != CharacterCreationFoundationEffectCompilationStatuses.Supported
                || item.Blocker is not null)
            {
                unverified = true;
                continue;
            }
            string? row = item.Kind switch
            {
                "attributelevel" when item.Amount.HasValue => Text("Attribute", "Attribut", "Atributo")
                    + $" {item.TargetName}: {Amount(item.Amount.Value)}",
                "skilllevel" when item.Amount.HasValue => Text("Active skill", "Aktionsfertigkeit", "Habilidad activa")
                    + $" {item.TargetName}: {Amount(item.Amount.Value)}",
                "skillgrouplevel" when item.Amount.HasValue => Text("Skill group", "Fertigkeitsgruppe", "Grupo de habilidades")
                    + $" {item.TargetName}: {Amount(item.Amount.Value)}",
                "knowledgeskilllevel" when item.Amount.HasValue => Text("Knowledge-skill pool", "Wissensfertigkeiten-Pool", "Reserva de conocimientos")
                    + $": {Amount(item.Amount.Value)} " + Text("(not a named skill or language grant)",
                        "(keine bestimmte Wissensfertigkeit oder Sprache)", "(no concede una habilidad o idioma concreto)"),
                "qualitylevel" when item.Amount.HasValue => Text("Quality", "Eigenschaft", "Cualidad")
                    + $" {item.TargetName}: " + Text("level contribution", "Stufenbeitrag", "aportación de nivel")
                    + $" {item.Amount.Value.ToString(CultureInfo.InvariantCulture)} "
                    + Text("(resolved cumulatively, not a final rating)", "(kumulativ aufgelöst, kein Endwert)",
                        "(resolución acumulativa, no es un valor final)"),
                "addqualities" => Text("Quality contribution", "Eigenschaftsbeitrag", "Aportación de cualidad")
                    + $": {item.TargetName}",
                "freepositivequalities" when item.Amount.HasValue => Text("Positive quality budget", "Budget für positive Eigenschaften", "Presupuesto de cualidades positivas")
                    + $": {Amount(item.Amount.Value)}",
                "freenegativequalities" when item.Amount.HasValue => Text("Negative quality budget", "Budget für negative Eigenschaften", "Presupuesto de cualidades negativas")
                    + $": {Amount(item.Amount.Value)}",
                // A stack selection is not another mechanical reward.
                "pushtext" => string.Empty,
                _ => null
            };
            if (row is null) unverified = true;
            else if (row.Length != 0) rows.Add(row);
        }
        string heading = Text("Confirmed module contributions, not final character ratings",
            "Bestätigte Modulbeiträge, keine endgültigen Charakterwerte",
            "Aportaciones confirmadas del módulo, no valores finales del personaje");
        string cost = choice.MechanicsPreview.KarmaIsExact
            ? Text("Confirmed choice cost", "Kosten der bestätigten Auswahl", "Coste de la elección confirmada")
                + $": {choice.MechanicsPreview.KarmaCost.ToString(CultureInfo.InvariantCulture)} Karma. "
            : string.Empty;
        // Partial coverage must not negate the confirmed rows above. Unknown
        // effects still grant nothing; this only distinguishes them from a
        // completely unavailable summary for a newly accepted decision.
        if (unverified) rows.Add(rows.Count == 0 ? unavailable : Text(
            "Other contributions unverified; infer no additional rewards.",
            "Weitere Beiträge nicht bestätigt; keine zusätzlichen Vorteile ableiten.",
            "Otras aportaciones sin verificar; no inferir recompensas adicionales."));
        if (rows.Count == 0)
            rows.Add(Text("No mechanical reward asserted.", "Kein mechanischer Vorteil behauptet.", "No se afirma ninguna recompensa mecánica."));
        string summary = $"{choice.Label} — {heading}. {cost}{string.Join("; ", rows)}";
        // Never silently truncate a reward or imply completeness. The retained
        // review remains available even when the authoring wire cannot fit it.
        if (summary.Length > 2048)
            summary = $"{heading}. {cost}{unavailable}";
        var fact = new OriginCanonicalNarrativeFact(moduleFactId + ":contributions:v1",
            "accepted-life-module-contributions", summary, decisionId,
            choice.SourceAnchorIds.Concat(contributions?.SelectMany(item => item.SourceAnchorIds) ?? [])
                .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(), string.Empty);
        return fact with { FactDigest = Digest(fact) };
    }
}

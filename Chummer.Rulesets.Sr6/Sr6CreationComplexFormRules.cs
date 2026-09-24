using Chummer.Application.Characters;
using Chummer.Contracts.Characters;

namespace Chummer.Rulesets.Sr6;

/// <summary>German 2024 core form identities and creation selection, not execution effects.
/// Names follow the owned German source; no SR5 catalog is used.</summary>
public static class Sr6CreationComplexFormRules
{
    public const string SourceAnchor = "sr6_core_de_2024:p180-185,189-191,201";

    public static IReadOnlyList<Sr6CreationComplexFormOption> Catalog()
    {
        var rows = new List<Sr6CreationComplexFormOption>
        {
            new("editor", "Editor", Page(189)), new("tattletale", "Petze", Page(189)),
            new("puppeteer", "Puppenspieler", Page(189)), new("cleaner", "Reiniger", Page(189)),
            new("resonance-illusion", "Resonanzillusion", Page(190)), new("resonance-channel", "Resonanzkanal", Page(190)),
            new("resonance-cloud", "Resonanznebel", Page(190)), new("resonance-spike", "Resonanzspike", Page(190)),
            new("signal-veil", "Signalschleier", Page(190)), new("signal-storm", "Signalsturm", Page(190)),
            new("mirror-persona", "Spiegelpersona", Page(191)), new("stitches", "Zusammenflicken", Page(191))
        };
        foreach (var (id, name) in new[] { ("attack", "Angriff"), ("sleaze", "Schleicher"),
                     ("data-processing", "Datenverarbeitung"), ("firewall", "Firewall") })
        {
            rows.Add(new("diffusion-" + id, name + "-Senkung", Page(189)));
            rows.Add(new("infusion-" + id, name + "-Steigerung", Page(189)));
        }
        foreach (var (id, name) in new[] { ("baby-monitor", "Babymonitor"), ("edit", "Editieren"),
                     ("configurator", "Konfigurator"), ("browse", "Schmöker"), ("signal-scrub", "Signalreiniger"),
                     ("toolbox", "Toolbox"), ("encryption", "Verschlüsselung"), ("virtual-machine", "Virtuelle Maschine") })
            rows.Add(new("emulate-" + id, "Emulieren: " + name, "sr6_core_de_2024:p184,189"));
        foreach (var (id, name) in new[] { ("track", "Aufspüren"), ("exploit", "Ausnutzen"),
                     ("biofeedback", "Biofeedback"), ("biofeedback-filter", "Biofeedbackfilter"), ("blackout", "Blackout"),
                     ("defuse", "Entschärfen"), ("decryption", "Entschlüsselung"), ("lockdown", "Fessel"),
                     ("fork", "Gabel"), ("armor", "Panzerung"), ("guard", "Splitterschutz"), ("stealth", "Tarnkappe") })
            rows.Add(new("emulate-" + id, "Emulieren: " + name, "sr6_core_de_2024:p185,189"));
        // Overclock is purchased separately per action; never accept an arbitrary
        // program or collapse all its action variants into one learned form.
        foreach (var (id, name) in new[] { ("brute-force", "Brute Force"), ("crack-file", "Datei cracken"),
                     ("edit-file", "Datei editieren"), ("encrypt-file", "Datei verschlüsseln"),
                     ("set-data-bomb", "Datenbombe legen"), ("disarm-data-bomb", "Datenbombe entschärfen"),
                     ("control-device", "Gerät steuern"), ("backdoor-entry", "Hintertür benutzen"),
                     ("trace-icon", "Icon aufspüren"), ("matrix-perception", "Matrixwahrnehmung"),
                     ("matrix-search", "Matrixsuche"), ("data-spike", "Datenspike"),
                     ("probe", "Sondieren"), ("tar-pit", "Teergrube"), ("snoop", "Übertragung abfangen"),
                     ("hide", "Verstecken"), ("spoof-command", "Befehl vortäuschen"),
                     ("format-device", "Gerät formatieren"), ("reboot-device", "Gerät neu starten"),
                     ("jump-in", "Hineinspringen"), ("erase-signature", "Matrixsignatur löschen"),
                     ("check-overwatch", "Overwatch-Wert bestimmen"), ("crash-program", "Programm abstürzen lassen"),
                     ("hash-check", "Prüfsummensuche"), ("jam-signals", "Signal stören") })
            rows.Add(new("emulate-overclock-" + id, "Emulieren: Übertakten (" + name + ")", "sr6_core_de_2024:p180-185,189"));
        foreach (var (id, name, subject) in new (string, string, string?)[] { ("evasion", "Ausweichen", null),
                     ("clearsight", "Clearsight", null), ("electronic-warfare", "Elektronische Kriegsführung", null),
                     ("maneuvering", "Manövrieren", "drone-model"), ("stealth", "Stealth", "drone-model"),
                     ("targeting", "Zielerfassung", "weapon-model") })
            rows.Add(new("emulate-autosoft-" + id, "Emulieren: " + name, "sr6_core_de_2024:p189,201", subject));
        return rows.AsReadOnly();
    }

    public static CharacterCreationFoundationResult<Sr6CreationComplexFormPreview> Evaluate(
        Sr6CreationFoundationPreview foundation, Sr6CreationComplexFormSelection selection)
    {
        if (!Sr6CreationFoundationIntegrity.TryFreezeComplexForms(selection, out var frozen))
            return Fail(Sr6CreationComplexFormBlockers.InvalidSelection);
        if (foundation.Selection.TalentId != "technomancer" || foundation.TalentAllocation is not { } talent)
            return Fail(Sr6CreationComplexFormBlockers.TalentRequired);
        if (frozen!.Choices.Count > talent.ComplexFormLimit)
            return Fail(Sr6CreationComplexFormBlockers.LimitExceeded);
        var catalog = Catalog();
        var values = new List<Sr6CreationComplexFormValue>();
        foreach (var choice in frozen.Choices)
        {
            var option = catalog.SingleOrDefault(row => row.Id == choice.CatalogId);
            if (option is null) return Fail(Sr6CreationComplexFormBlockers.CatalogUnavailable);
            if ((option.SubjectKind is null) != (choice.Subject is null))
                return Fail(Sr6CreationComplexFormBlockers.InvalidSelection);
            values.Add(new(choice, option.SourceName, option.SourceAnchorId, option.SubjectKind is not null));
        }
        int free = Math.Min(values.Count, talent.FreeComplexFormSlots);
        int cost = (values.Count - free) * talent.CharacterPointsPerSpellOrForm;
        if (foundation.PointBuy is { } points && cost > points.PointsRemaining)
            return Fail(Sr6CreationPointBuyBlockers.BudgetExceeded);
        string authority = Sr6CreationFoundationIntegrity.Digest(new
        {
            Schema = "chummer.sr6.creation-complex-forms.v1", SourceAnchor,
            Sr6CreationFoundationRules.CoreSourceSha256, Catalog = catalog,
            TalentAuthority = talent.AuthorityDigest, Selection = frozen, cost, free
        });
        return new(CharacterCreationFoundationOutcomes.Success,
            new(values.ToArray(), talent.ComplexFormLimit, talent.ComplexFormLimit - values.Count, free, cost, authority, [SourceAnchor]), []);
    }

    private static string Page(int number) => "sr6_core_de_2024:p" + number.ToString(System.Globalization.CultureInfo.InvariantCulture);
    private static CharacterCreationFoundationResult<Sr6CreationComplexFormPreview> Fail(string blocker)
        => new(CharacterCreationFoundationOutcomes.Blocked, null, [blocker]);
}

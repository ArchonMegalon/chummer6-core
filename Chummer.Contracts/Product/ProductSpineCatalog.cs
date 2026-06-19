namespace Chummer.Contracts.Product;

public static class ProductSpineCatalog
{
    private static readonly IReadOnlyList<DesktopHorizonWorkbenchEntry> DesktopHorizons =
    [
        new(
            "karma_forge",
            "Karma Forge",
            "Browse package candidates, open the intake lane, and create new rules packages without leaving the desktop settings flow blind.",
            new DesktopHorizonRouteOption("karma_forge_packages", "Browse packages", "/packages", "Open the first-party package browser."),
            new DesktopHorizonRouteOption("karma_forge_account_packages", "My packages", "/account/packages", "Open tracked package summaries on the signed-in account rail."),
            new DesktopHorizonRouteOption("karma_forge_create", "Create package", "/participate/karma-forge#karma-forge-intake", "Open the Karma Forge intake at the create-package anchor.")),
        new(
            "alice",
            "ALICE",
            "Keep build mentor, rules explain, and first-session follow-through on native desks before widening into browser handoff rails.",
            new DesktopHorizonRouteOption("alice_public", "Open ALICE", "/alice", "Open the public ALICE route."),
            new DesktopHorizonRouteOption("alice_account", "Open workbench", "/account/alice", "Open the signed-in ALICE bench."),
            NativeActions:
            [
                new("ready_for_tonight", "Open Ready for Tonight"),
                new("knowledge_fabric", "Open Knowledge Fabric")
            ]),
        new(
            "nexus_pan",
            "NEXUS-PAN",
            "Keep continuity, workspace recovery, and devices/access on adjacent native rails before widening into public continuity routes.",
            new DesktopHorizonRouteOption("nexus_pan_public", "Open public continuity", "/play/continuity", "Open the public continuity lane."),
            SecondaryAction: new DesktopHorizonRouteOption("nexus_pan_account", "Open support rail", "/account/support", "Open the signed-in support rail when continuity recovery needs operator follow-through."),
            NativeActions:
            [
                new("workspace", "Open workspace desk"),
                new("devices_access", "Open devices & access")
            ]),
        new(
            "ready_for_tonight",
            "Ready for Tonight",
            "Open the role verdict surface, the signed-in return lane, and the no-desktop handoff without reducing the product to a fake one-screen wizard.",
            new DesktopHorizonRouteOption("ready_public", "Open Ready", "/ready", "Open the public Ready for Tonight route."),
            new DesktopHorizonRouteOption("ready_account", "Open return lane", "/account/runsites/open", "Open the signed-in return lane for the next safe action."),
            new DesktopHorizonRouteOption("ready_mobile", "Open mobile rail", "/mobile", "Open the mobile and PWA participation rail.")),
        new(
            "jackpoint",
            "Jackpoint",
            "Open dossiers, publication benches, and signed-in briefing routes.",
            new DesktopHorizonRouteOption("jackpoint_public", "Open Jackpoint", "/jackpoint", "Open the public Jackpoint frontdoor."),
            new DesktopHorizonRouteOption("jackpoint_account", "Open desk", "/account/jackpoint", "Open the signed-in Jackpoint desk.")),
        new(
            "knowledge_fabric",
            "Knowledge Fabric",
            "Open grounded rules answers, receipt-backed explain posture, and source-aware follow-through from the desktop shell.",
            new DesktopHorizonRouteOption("knowledge_public", "Open Rules", "/rules", "Open the public Knowledge Fabric rules route."),
            new DesktopHorizonRouteOption("knowledge_receipts", "Open receipts", "/rules/receipts", "Open the rules receipt index."),
            new DesktopHorizonRouteOption("knowledge_studio", "Open Edition Studio", "/edition-studio", "Open the edition and explain studio route.")),
        new(
            "runsite",
            "Runsite",
            "Jump from desktop settings into mission-space prep, workspace digests, and run benches.",
            new DesktopHorizonRouteOption("runsite_public", "Open Runsite", "/runsites", "Open the public Runsite frontdoor."),
            new DesktopHorizonRouteOption("runsite_account", "Open runsites", "/account/runsites", "Open the signed-in Runsite bench.")),
        new(
            "run_control",
            "Run Control",
            "Keep runboard, table pressure, and campaign workspace follow-through on native desks before widening into browser rails.",
            new DesktopHorizonRouteOption("run_control_public", "Open Run Control", "/run-control", "Open the public Run Control route."),
            new DesktopHorizonRouteOption("run_control_account", "Open desk", "/account/run-control", "Open the signed-in run-control desk."),
            NativeActions:
            [
                new("table_pulse", "Open Table Pulse"),
                new("workspace", "Open workspace desk")
            ]),
        new(
            "runbook_press",
            "Runbook Press",
            "Keep publication, workspace, and campaign-book follow-through on native desks before widening into the public publishing frontdoor.",
            new DesktopHorizonRouteOption("runbook_press_public", "Open public Runbook", "/runbook", "Open the public Runbook Press route."),
            SecondaryAction: new DesktopHorizonRouteOption("runbook_press_creator", "Open public creator rail", "/creator", "Open the public creator rail when you need browser-side publishing follow-through."),
            NativeActions:
            [
                new("publication", "Open publication desk"),
                new("workspace", "Open workspace desk")
            ]),
        new(
            "table_pulse",
            "Table Pulse",
            "Split live notification pressure from aftermath follow-through without losing the signed-in rails.",
            new DesktopHorizonRouteOption("table_pulse_public", "Open Table Pulse", "/table-pulse", "Open the public Table Pulse route."),
            new DesktopHorizonRouteOption("table_pulse_live", "Live heat", "/account/ledger/notifications", "Open the live heat and notification rail."),
            new DesktopHorizonRouteOption("table_pulse_aftermath", "Aftermath", "/account/work#aftermath-packages", "Open aftermath packages on the signed-in work rail.")),
        new(
            "black_ledger",
            "Black Ledger",
            "Keep the world board, pulse follow-through, and replay-adjacent desks native before widening into browser validation lanes.",
            new DesktopHorizonRouteOption("black_ledger_public", "Open Ledger", "/ledger", "Open the public Black Ledger route."),
            new DesktopHorizonRouteOption("black_ledger_map", "Open map", "/ledger/map#ledger-map", "Open the command map route."),
            new DesktopHorizonRouteOption("black_ledger_validation", "Validation", "/account/ledger/worldtick/validation", "Open the signed-in validation rail."),
            NativeActions:
            [
                new("table_pulse", "Open Table Pulse"),
                new("ghostwire", "Open Ghostwire")
            ]),
        new(
            "community_hub",
            "Community Hub",
            "Open the open-run network, signed-in community board, and moderated operator rail.",
            new DesktopHorizonRouteOption("community_public", "Open Community", "/community", "Open the public Community Hub route."),
            new DesktopHorizonRouteOption("community_account", "Open board", "/account/community", "Open the signed-in community board."),
            new DesktopHorizonRouteOption("community_open_runs", "Open run venue", "/community/runs/open-run/venue", "Open a public-safe open-run venue posture.")),
        new(
            "creator_os",
            "Creator OS",
            "Keep creator publication, workspace, and publishing-stack follow-through native before widening into the browser creator rail.",
            new DesktopHorizonRouteOption("creator_public", "Open public Creator OS", "/creator", "Open the public Creator OS route."),
            SecondaryAction: new DesktopHorizonRouteOption("creator_account", "Open creator account rail", "/account/creator", "Open the signed-in creator browser rail when native follow-through is not enough."),
            NativeActions:
            [
                new("publication", "Open publication desk"),
                new("workspace", "Open workspace desk")
            ]),
        new(
            "anarchy",
            "Anarchy",
            "Open the rules-light lane and the live play shell from the desktop settings surface.",
            new DesktopHorizonRouteOption("anarchy_public", "Open Anarchy", "/anarchy", "Open the public Anarchy route."),
            new DesktopHorizonRouteOption("anarchy_play", "Open play shell", "/play/anarchy", "Open the live Anarchy play shell."),
            new DesktopHorizonRouteOption("anarchy_ledger", "Open ledger lane", "/ledger/anarchy", "Open the rules-light world lane.")),
        new(
            "ghostwire",
            "Ghostwire",
            "Open replay and after-action follow-through directly from the workbench hub.",
            new DesktopHorizonRouteOption("ghostwire_public", "Open Ghostwire", "/ghostwire", "Open the public Ghostwire route."),
            new DesktopHorizonRouteOption("ghostwire_replay", "After action", "/ghostwire/after-action/replay_timeline.md", "Open the replay timeline packet."),
            new DesktopHorizonRouteOption("ghostwire_report", "Consequence chain", "/ghostwire/after-action/consequence_chain.md", "Open the consequence chain packet.")),
        new(
            "runner_passport",
            "Runner Passport",
            "Open identity-network posture and the signed-in runner passport lane.",
            new DesktopHorizonRouteOption("passport_public", "Open Passport", "/passport", "Open the public Runner Passport route."),
            new DesktopHorizonRouteOption("passport_account", "Open desk", "/account/passport", "Open the signed-in runner passport desk.")),
        new(
            "quicksilver",
            "Quicksilver",
            "Open the command deck and signed-in jump-target bench from the desktop shell.",
            new DesktopHorizonRouteOption("quicksilver_public", "Open Quicksilver", "/quicksilver", "Open the public Quicksilver route."),
            new DesktopHorizonRouteOption("quicksilver_account", "Open deck", "/account/quicksilver", "Open the signed-in Quicksilver deck.")),
        new(
            "local_co_processor",
            "Local Co-Processor",
            "Review capability and policy boundaries before opening the signed-in local acceleration lane.",
            new DesktopHorizonRouteOption("local_coprocessor_public", "Open Local Co-Processor", "/local-co-processor", "Open the public Local Co-Processor route."),
            new DesktopHorizonRouteOption("local_coprocessor_account", "Open desk", "/account/local-co-processor", "Open the signed-in local co-processor desk."))
    ];

    private static readonly IReadOnlyList<DesktopHorizonRouteOption> KarmaForgeTargets =
    [
        new("karma_forge_packages", "Package browser", "/packages", "Browse available public-safe packages."),
        new("karma_forge_account_packages", "Tracked packages", "/account/packages", "Open your signed-in package summary."),
        new("karma_forge_intake", "Karma Forge intake", "/participate/karma-forge#karma-forge-intake", "Open the intake form for a new package candidate.")
    ];

    public static IReadOnlyList<DesktopHorizonWorkbenchEntry> ListDesktopHorizons() => DesktopHorizons;

    public static IReadOnlyList<DesktopHorizonRouteOption> ListKarmaForgeTargets() => KarmaForgeTargets;
}

#nullable enable annotations

using System;
using System.Collections.Generic;
using System.Linq;
using Chummer.Application.Explain;
using Chummer.Application.Journal;
using Chummer.Application.Session;
using Chummer.Contracts;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Journal;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Rulesets;
using Chummer.Contracts.Session;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public sealed class JournalSessionAndExplainHookServiceTests
{
    [TestMethod]
    public void Default_journal_projection_service_filters_orders_and_validates_projection()
    {
        DefaultJournalProjectionService service = new();

        JournalProjection projection = service.BuildProjection(
            scopeKind: " Character ",
            scopeId: " char-1 ",
            notes:
            [
                new NoteDocument(
                    NoteId: "note-b",
                    Owner: OwnerScope.LocalSingleUser,
                    ScopeKind: "character",
                    ScopeId: "char-1",
                    Title: "Second",
                    Blocks:
                    [
                        new NoteBlock("block-z", NoteBlockKinds.Paragraph, "Z", new DateTimeOffset(2026, 5, 20, 10, 1, 0, TimeSpan.Zero)),
                        new NoteBlock("block-a", NoteBlockKinds.Paragraph, "A", new DateTimeOffset(2026, 5, 20, 10, 0, 0, TimeSpan.Zero))
                    ],
                    UpdatedAtUtc: new DateTimeOffset(2026, 5, 20, 12, 0, 0, TimeSpan.Zero)),
                new NoteDocument(
                    NoteId: "note-a",
                    Owner: OwnerScope.LocalSingleUser,
                    ScopeKind: "character",
                    ScopeId: "char-1",
                    Title: "First",
                    Blocks:
                    [
                        new NoteBlock("block-c", NoteBlockKinds.Paragraph, "C", new DateTimeOffset(2026, 5, 20, 9, 0, 0, TimeSpan.Zero))
                    ],
                    UpdatedAtUtc: new DateTimeOffset(2026, 5, 20, 11, 0, 0, TimeSpan.Zero)),
                new NoteDocument(
                    NoteId: "note-other",
                    Owner: OwnerScope.LocalSingleUser,
                    ScopeKind: "session",
                    ScopeId: "other",
                    Title: "Other",
                    Blocks: [],
                    UpdatedAtUtc: new DateTimeOffset(2026, 5, 20, 8, 0, 0, TimeSpan.Zero))
            ],
            ledgerEntries:
            [
                new LedgerEntry("ledger-b", OwnerScope.LocalSingleUser, "character", "char-1", LedgerEntryKinds.Karma, 5m, "karma", "B", new DateTimeOffset(2026, 5, 20, 13, 0, 0, TimeSpan.Zero), NoteId: "missing-note"),
                new LedgerEntry("ledger-a", OwnerScope.LocalSingleUser, "session", "wrong", LedgerEntryKinds.Nuyen, 100m, "nuyen", "A", new DateTimeOffset(2026, 5, 20, 12, 30, 0, TimeSpan.Zero), NoteId: "note-a")
            ],
            timelineEvents:
            [
                new TimelineEvent("event-b", OwnerScope.LocalSingleUser, "character", "char-1", TimelineEventKinds.Session, "B", new DateTimeOffset(2026, 5, 20, 14, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 5, 20, 13, 0, 0, TimeSpan.Zero), NoteId: "note-a", LedgerEntryId: "missing-ledger"),
                new TimelineEvent("event-a", OwnerScope.LocalSingleUser, "character", "char-1", TimelineEventKinds.Note, "A", new DateTimeOffset(2026, 5, 20, 12, 0, 0, TimeSpan.Zero), null, NoteId: "missing-note", LedgerEntryId: "ledger-a"),
                new TimelineEvent("event-other", OwnerScope.LocalSingleUser, "campaign", "other", TimelineEventKinds.Note, "Other", new DateTimeOffset(2026, 5, 20, 10, 0, 0, TimeSpan.Zero))
            ]);

        Assert.AreEqual("character", projection.ScopeKind);
        Assert.AreEqual("char-1", projection.ScopeId);
        CollectionAssert.AreEqual(new[] { "note-a", "note-b" }, projection.Notes.Select(note => note.NoteId).ToArray());
        CollectionAssert.AreEqual(new[] { "block-a", "block-z" }, projection.Notes[1].Blocks.Select(block => block.BlockId).ToArray());
        CollectionAssert.AreEqual(new[] { "ledger-b" }, projection.LedgerEntries.Select(entry => entry.EntryId).ToArray());
        CollectionAssert.AreEqual(new[] { "event-a", "event-b" }, projection.TimelineEvents.Select(entry => entry.EventId).ToArray());

        IReadOnlyList<RulesetCapabilityDiagnostic> diagnostics = service.Validate(projection);

        string[] expectedDiagnosticCodes =
        [
            "journal.ledger.note-missing",
            "journal.timeline.invalid-range",
            "journal.timeline.ledger-missing",
            "journal.timeline.ledger-missing",
            "journal.timeline.note-missing"
        ];
        string[] actualDiagnosticCodes = diagnostics.Select(diagnostic => diagnostic.Code).OrderBy(code => code, StringComparer.Ordinal).ToArray();

        CollectionAssert.AreEqual(
            expectedDiagnosticCodes.OrderBy(code => code, StringComparer.Ordinal).ToArray(),
            actualDiagnosticCodes);
    }

    [TestMethod]
    public void Default_session_overlay_projection_service_replays_sorted_events_and_emits_validator_diagnostics()
    {
        DefaultSessionOverlayProjectionService service = new();

        SessionOverlayProjection projection = service.Replay(
            overlayId: "overlay-1",
            characterId: "char-1",
            runtimeFingerprint: "sha256:test",
            events:
            [
                CreateEvent("event-4", 4, SessionOverlayEventKinds.NoteAdded, new Dictionary<string, object?> { ["note"] = "Second note" }),
                CreateEvent("event-2", 2, SessionOverlayEventKinds.TrackerIncrement, new Dictionary<string, object?> { ["trackerId"] = "edge", ["amount"] = 3 }),
                CreateEvent("event-1", 1, SessionOverlayEventKinds.TrackerIncrement, new Dictionary<string, object?> { ["trackerId"] = "edge", ["amount"] = 2 }),
                CreateEvent("event-3", 3, SessionOverlayEventKinds.TrackerDecrement, new Dictionary<string, object?> { ["trackerId"] = "edge", ["amount"] = 1 }),
                CreateEvent("event-5", 5, SessionOverlayEventKinds.EffectApplied, new Dictionary<string, object?> { ["effectId"] = "buff-1" }),
                CreateEvent("event-6", 6, SessionOverlayEventKinds.EffectRemoved, new Dictionary<string, object?> { ["effectId"] = "buff-1" }),
                CreateEvent("event-7", 7, SessionOverlayEventKinds.PinChanged, new Dictionary<string, object?> { ["actionId"] = "action-1", ["isPinned"] = true }),
                CreateEvent("event-8", 8, SessionOverlayEventKinds.PinChanged, new Dictionary<string, object?> { ["actionId"] = "action-1", ["isPinned"] = false }),
                CreateEvent("event-9", 9, SessionOverlayEventKinds.TrackerIncrement, new Dictionary<string, object?> { ["currentValue"] = 99 }),
                CreateEvent("event-10", 10, SessionOverlayEventKinds.PinChanged, new Dictionary<string, object?>()),
                CreateEvent("event-11", 11, SessionOverlayEventKinds.EffectApplied, new Dictionary<string, object?>()),
                CreateEvent("event-12", 12, SessionOverlayEventKinds.NoteAdded, new Dictionary<string, object?> { ["note"] = "Final note" })
            ]);

        CollectionAssert.AreEqual(
            new[] { "event-1", "event-2", "event-3", "event-4", "event-5", "event-6", "event-7", "event-8", "event-9", "event-10", "event-11", "event-12" },
            projection.AppliedEvents.Select(evt => evt.EventId).ToArray());
        Assert.AreEqual(1, projection.Trackers.Count);
        Assert.AreEqual("edge", projection.Trackers[0].TrackerId);
        Assert.AreEqual(4, projection.Trackers[0].CurrentValue);
        Assert.HasCount(0, projection.ActiveEffects);
        Assert.HasCount(0, projection.PinnedActionIds);
        CollectionAssert.AreEqual(new[] { "Second note", "Final note" }, projection.Notes.ToArray());
        CollectionAssert.AreEqual(
            new[]
            {
                "session.replay.effect.missing-id",
                "session.replay.pin.missing-id",
                "session.replay.tracker.absolute-write-blocked"
            },
            projection.Diagnostics.Select(diagnostic => diagnostic.Code).OrderBy(code => code, StringComparer.Ordinal).ToArray());
    }

    [TestMethod]
    public void Default_explain_hook_composer_normalizes_reference_and_deduplicates_composition()
    {
        DefaultExplainHookComposer service = new();

        ExplainHookReference reference = service.CreateReference(
            targetKind: " Character ",
            targetId: " char-1 ",
            traceId: " trace-1 ",
            subjectId: " subject-1 ",
            capabilityId: " derive.stat ",
            providerId: " provider-x ",
            packId: " pack-y ",
            runtimeFingerprint: " sha256:test ");

        Assert.AreEqual("character:char-1:trace-1", reference.HookId);
        Assert.AreEqual("trace-1", reference.TraceId);
        Assert.AreEqual("subject-1", reference.SubjectId);
        Assert.AreEqual("derive.stat", reference.CapabilityId);
        Assert.AreEqual("provider-x", reference.ProviderId);
        Assert.AreEqual("pack-y", reference.PackId);
        Assert.AreEqual("sha256:test", reference.RuntimeFingerprint);

        ExplainHookComposition composition = service.Compose(
            " composition-1 ",
            [
                new ExplainHookAttachment(" Character ", " char-1 ", reference),
                new ExplainHookAttachment("character", "char-1", reference with { HookId = " character:char-1:trace-1 " }),
                new ExplainHookAttachment(" Session ", " sess-1 ", reference with { HookId = "session:sess-1:trace-2", TraceId = " trace-2 ", SubjectId = " subject-2 " })
            ]);

        Assert.AreEqual("composition-1", composition.CompositionId);
        Assert.AreEqual(2, composition.Attachments.Count);
        CollectionAssert.AreEqual(
            new[] { "character", "session" },
            composition.Attachments.Select(attachment => attachment.TargetKind).ToArray());
        CollectionAssert.AreEqual(
            new[] { "character:char-1:trace-1", "session:sess-1:trace-2" },
            composition.Attachments.Select(attachment => attachment.Explain.HookId).ToArray());
    }

    private static SessionEventEnvelope CreateEvent(
        string eventId,
        long sequence,
        string eventType,
        IReadOnlyDictionary<string, object?> payload)
        => new(
            EventId: eventId,
            OverlayId: "overlay-1",
            BaseCharacterVersion: new CharacterVersionReference("char-1", "v1", "sr5", "sha256:test"),
            DeviceId: "device-1",
            ActorId: "actor-1",
            Sequence: sequence,
            EventType: eventType,
            Payload: payload.ToDictionary(
                static pair => pair.Key,
                static pair => RulesetCapabilityBridge.FromObject(pair.Value),
                StringComparer.Ordinal),
            CreatedAtUtc: new DateTimeOffset(2026, 5, 20, 12, 0, 0, TimeSpan.Zero).AddMinutes(sequence));
}

using System.Collections;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Chummer.Application.Characters;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;
using Chummer.Infrastructure.Workspaces;
using Chummer.Infrastructure.Xml;
using Chummer.Rulesets.Hosting;
using Chummer.Rulesets.Sr5;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public sealed class WorkspaceContinuationCodecTests
{
    // A caller-selected bound for these small transport fixtures, not Core policy.
    private const int MaximumBytes = 64 * 1024;
    private const string UnicodePayload = "<character><name>Zoë 東京 😀 e\u0301 é</name><notes>line 1\r\nline 2\t“quoted”</notes></character>";

    [TestMethod]
    public void Encode_is_deterministic_and_roundtrips_exact_Unicode_without_convenience_projections()
    {
        var expected = Fixture();
        string originalDigest = WorkspaceContinuationSnapshotDigest.Compute(expected.Snapshot);
        byte[] encoded = WorkspaceContinuationCodec.Encode(expected, MaximumBytes);
        CollectionAssert.AreEqual(encoded, WorkspaceContinuationCodec.Encode(expected, MaximumBytes));
        var candidate = Decode(encoded);
        AssertEquivalent(expected, candidate);
        Assert.AreEqual(UnicodePayload, candidate.Snapshot.Workspace.Document.Content);
        Assert.AreEqual(originalDigest, candidate.SnapshotDigest);
        Assert.AreEqual(originalDigest, WorkspaceContinuationSnapshotDigest.Compute(expected.Snapshot));
        CollectionAssert.AreEqual(encoded, WorkspaceContinuationCodec.Encode(candidate, MaximumBytes));

        JsonObject root = JsonNode.Parse(encoded)!.AsObject();
        CollectionAssert.AreEquivalent(new[] { "ContractName", "Snapshot", "SnapshotDigest" }, root.Select(pair => pair.Key).ToArray());
        Assert.AreEqual(WorkspaceContinuationSnapshot.ContractName, root["ContractName"]!.GetValue<string>());
        var document = root["Snapshot"]!["Workspace"]!["Document"]!.AsObject();
        CollectionAssert.AreEquivalent(new[] { "State", "Format" }, document.Select(pair => pair.Key).ToArray());
        Assert.IsFalse(document["State"]!["AuxiliaryState"]!.AsObject().ContainsKey("IsEmpty"));
    }

    [TestMethod]
    public void Property_order_whitespace_and_equivalent_Unicode_escaping_are_accepted()
    {
        var expected = Fixture();
        byte[] canonical = WorkspaceContinuationCodec.Encode(expected, MaximumBytes);
        var reordered = ReverseObjectProperties(JsonNode.Parse(canonical));
        string text = " \n\t" + reordered!.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        }) + "\r\n ";
        Assert.IsTrue(Encoding.UTF8.GetByteCount(text) > text.Length, "The alternate wire form must contain raw multi-byte Unicode.");
        var candidate = Decode(Encoding.UTF8.GetBytes(text));
        AssertEquivalent(expected, candidate);
        CollectionAssert.AreEqual(canonical, WorkspaceContinuationCodec.Encode(candidate, MaximumBytes));
    }

    [TestMethod]
    [DataRow("", "UnexpectedEnvelope")]
    [DataRow("Snapshot", "UnexpectedSnapshot")]
    [DataRow("Snapshot.Workspace", "UnexpectedWorkspace")]
    [DataRow("Snapshot.Workspace.Id", "UnexpectedIdentity")]
    [DataRow("Snapshot.Workspace.Document", "Content")]
    [DataRow("Snapshot.Workspace.Document", "PayloadEnvelope")]
    [DataRow("Snapshot.Workspace.Document", "AuxiliaryStateDigest")]
    [DataRow("Snapshot.Workspace.Document.State", "UnexpectedState")]
    [DataRow("Snapshot.Workspace.Document.State.AuxiliaryState", "UnexpectedReceiptHistory")]
    public void Unknown_or_readonly_projection_fields_are_rejected_at_every_depth(string path, string name)
    {
        JsonObject root = Wire();
        At(root, path)!.AsObject()[name] = "must not be silently discarded";
        AssertRejected(root);
    }

    [TestMethod]
    [DataRow("ContractName")]
    [DataRow("Snapshot.OwnerId")]
    [DataRow("Snapshot.Workspace.SavedRevision")]
    [DataRow("Snapshot.Workspace.Document.State.Payload")]
    [DataRow("Snapshot.Workspace.Document.State.AuxiliaryState.CharacterCreationFoundationDraft")]
    public void Duplicate_properties_with_identical_values_are_not_accepted_by_last_value_wins(string path)
    {
        JsonObject root = Wire();
        string name = path.Split('.')[^1];
        string field = JsonSerializer.Serialize(name) + ":" + (At(root, path)?.ToJsonString() ?? "null");
        string text = root.ToJsonString();
        Assert.IsTrue(text.Contains(field, StringComparison.Ordinal));
        AssertRejected(Encoding.UTF8.GetBytes(text.Replace(field, field + "," + field, StringComparison.Ordinal)));
    }

    [TestMethod]
    public void Escaped_duplicate_names_are_rejected_after_JSON_name_decoding()
    {
        JsonObject root = Wire();
        string text = root.ToJsonString();
        text = text[..^1] + ",\"\\u0053napshot\":" + root["Snapshot"]!.ToJsonString() + "}";
        AssertRejected(Encoding.UTF8.GetBytes(text));
    }

    [TestMethod]
    [DataRow("ContractName")]
    [DataRow("SnapshotDigest")]
    [DataRow("Snapshot")]
    [DataRow("Snapshot.OwnerId")]
    [DataRow("Snapshot.DelegatedGmCharacterEdits")]
    [DataRow("Snapshot.Workspace")]
    [DataRow("Snapshot.Workspace.Id")]
    [DataRow("Snapshot.Workspace.Id.Value")]
    [DataRow("Snapshot.Workspace.LastUpdatedUtc")]
    [DataRow("Snapshot.Workspace.ContentRevision")]
    [DataRow("Snapshot.Workspace.SavedRevision")]
    [DataRow("Snapshot.Workspace.Document")]
    [DataRow("Snapshot.Workspace.Document.Format")]
    [DataRow("Snapshot.Workspace.Document.State")]
    [DataRow("Snapshot.Workspace.Document.State.RulesetId")]
    [DataRow("Snapshot.Workspace.Document.State.SchemaVersion")]
    [DataRow("Snapshot.Workspace.Document.State.PayloadKind")]
    [DataRow("Snapshot.Workspace.Document.State.Payload")]
    [DataRow("Snapshot.Workspace.Document.State.AuxiliaryState")]
    public void Required_fields_cannot_be_missing_or_null_even_when_CLR_defaults_would_match(string path)
    {
        foreach (bool remove in new[] { true, false })
        {
            JsonObject root = Wire();
            var (parent, name) = Parent(root, path);
            if (remove) Assert.IsTrue(parent.Remove(name));
            else parent[name] = null;
            AssertRejected(root, path + (remove ? " missing" : " null"));
        }
    }

    [TestMethod]
    public void Missing_optional_auxiliary_null_field_is_rejected_even_when_defaults_and_digest_match()
    {
        var expected = Fixture();
        JsonObject root = JsonNode.Parse(WorkspaceContinuationCodec.Encode(expected, MaximumBytes))!.AsObject();
        var auxiliary = At(root, "Snapshot.Workspace.Document.State.AuxiliaryState")!.AsObject();
        Assert.IsTrue(auxiliary.ContainsKey("CharacterCreationFoundationDraft"));
        Assert.IsNull(auxiliary["CharacterCreationFoundationDraft"]);
        Assert.IsTrue(auxiliary.Remove("CharacterCreationFoundationDraft"));
        var defaultedAuxiliary = auxiliary.Deserialize<WorkspaceDocumentAuxiliaryState>();
        Assert.IsNotNull(defaultedAuxiliary);
        var defaulted = WithState(expected.Snapshot, expected.Snapshot.Workspace.Document.State with
        {
            AuxiliaryState = defaultedAuxiliary
        });
        Assert.AreEqual(expected.SnapshotDigest, WorkspaceContinuationSnapshotDigest.Compute(defaulted));
        AssertRejected(root);
    }

    [TestMethod]
    [DataRow("unknown-receipt")]
    [DataRow("unknown-operation")]
    [DataRow("duplicate-operation-hash")]
    public void Actual_GM_receipt_nested_fields_cannot_be_ignored_or_shadowed_under_the_original_digest(string corruption)
    {
        var expected = RealGmExport();
        byte[] bytes = WorkspaceContinuationCodec.Encode(expected, MaximumBytes);
        AssertEquivalent(expected, Decode(bytes));
        JsonObject root = JsonNode.Parse(bytes)!.AsObject();
        JsonNode receipt = root["Snapshot"]!["DelegatedGmCharacterEdits"]![0]!;
        JsonNode operation = receipt["Operations"]![0]!;
        if (corruption == "unknown-receipt")
            receipt["UnknownAuthorityHistory"] = "must not disappear";
        if (corruption == "unknown-operation")
            operation["UnknownPrivatePatchValue"] = "must not disappear";
        Assert.AreEqual(expected.SnapshotDigest, root["SnapshotDigest"]!.GetValue<string>());
        if (corruption == "duplicate-operation-hash")
        {
            string field = "\"ValueSha256\":" + operation["ValueSha256"]!.ToJsonString();
            string text = root.ToJsonString();
            Assert.IsTrue(text.Contains(field, StringComparison.Ordinal));
            AssertRejected(Encoding.UTF8.GetBytes(text.Replace(field, field + "," + field, StringComparison.Ordinal)));
        }
        else
            AssertRejected(root);
    }

    [TestMethod]
    [DataRow("content-zero")]
    [DataRow("content-negative")]
    [DataRow("saved-negative")]
    [DataRow("saved-future")]
    [DataRow("schema-zero")]
    [DataRow("schema-negative")]
    [DataRow("unknown-format")]
    [DataRow("owner-empty")]
    [DataRow("owner-case")]
    [DataRow("owner-whitespace")]
    [DataRow("workspace-path")]
    [DataRow("ruleset-case")]
    [DataRow("ruleset-whitespace")]
    [DataRow("payload-empty")]
    [DataRow("kind-empty")]
    public void Invalid_typed_identity_is_rejected_even_with_a_matching_digest(string corruption)
    {
        var snapshot = Fixture().Snapshot;
        var workspace = snapshot.Workspace;
        var state = workspace.Document.State;
        snapshot = corruption switch
        {
            "content-zero" => snapshot with { Workspace = workspace with { ContentRevision = 0, SavedRevision = 0 } },
            "content-negative" => snapshot with { Workspace = workspace with { ContentRevision = -1, SavedRevision = 0 } },
            "saved-negative" => snapshot with { Workspace = workspace with { SavedRevision = -1 } },
            "saved-future" => snapshot with { Workspace = workspace with { SavedRevision = 8 } },
            "schema-zero" => WithState(snapshot, state with { SchemaVersion = 0 }),
            "schema-negative" => WithState(snapshot, state with { SchemaVersion = -1 }),
            "unknown-format" => snapshot with { Workspace = workspace with { Document = workspace.Document with { Format = (WorkspaceDocumentFormat)999 } } },
            "owner-empty" => snapshot with { OwnerId = "" },
            "owner-case" => snapshot with { OwnerId = "Owner-A" },
            "owner-whitespace" => snapshot with { OwnerId = " owner-a " },
            "workspace-path" => snapshot with { Workspace = workspace with { Id = new("../runner") } },
            "ruleset-case" => WithState(snapshot, state with { RulesetId = "SR5" }),
            "ruleset-whitespace" => WithState(snapshot, state with { RulesetId = " sr5 " }),
            "payload-empty" => WithState(snapshot, state with { Payload = "" }),
            "kind-empty" => WithState(snapshot, state with { PayloadKind = "" }),
            _ => throw new AssertFailedException("Unknown corruption case.")
        };
        var invalid = Sign(snapshot);
        Assert.ThrowsExactly<JsonException>(() => WorkspaceContinuationCodec.Encode(invalid, MaximumBytes));
        AssertRejected(UncheckedWire(invalid), corruption);
    }

    [TestMethod]
    [DataRow("contract-version")]
    [DataRow("bad-digest")]
    [DataRow("upper-digest")]
    [DataRow("payload-tamper")]
    [DataRow("owner-tamper")]
    [DataRow("null-ledger-entry")]
    [DataRow("missing-ledger-entry-fields")]
    [DataRow("string-revision")]
    [DataRow("fractional-revision")]
    public void Malformed_or_tampered_wire_data_never_returns_a_candidate(string corruption)
    {
        JsonObject root = Wire();
        switch (corruption)
        {
            case "contract-version": root["ContractName"] = "chummer.workspace-continuation-snapshot/v999"; break;
            case "bad-digest": root["SnapshotDigest"] = new string('0', 64); break;
            case "upper-digest": root["SnapshotDigest"] = root["SnapshotDigest"]!.GetValue<string>().ToUpperInvariant(); break;
            case "payload-tamper": At(root, "Snapshot.Workspace.Document.State")!["Payload"] = "Different content"; break;
            case "owner-tamper": root["Snapshot"]!["OwnerId"] = "owner-b"; break;
            case "null-ledger-entry": root["Snapshot"]!["DelegatedGmCharacterEdits"] = new JsonArray((JsonNode?)null); break;
            case "missing-ledger-entry-fields": root["Snapshot"]!["DelegatedGmCharacterEdits"] = new JsonArray(new JsonObject()); break;
            case "string-revision": root["Snapshot"]!["Workspace"]!["ContentRevision"] = "7"; break;
            case "fractional-revision": root["Snapshot"]!["Workspace"]!["ContentRevision"] = 7.5; break;
        }
        AssertRejected(root, corruption);
    }

    [TestMethod]
    [DataRow("snapshot-null")]
    [DataRow("workspace-null")]
    [DataRow("document-null")]
    [DataRow("state-null")]
    [DataRow("auxiliary-null")]
    [DataRow("ledger-null")]
    [DataRow("ledger-entry-null")]
    [DataRow("digest-mismatch")]
    public void Encode_rejects_incomplete_typed_candidates_instead_of_defaulting_them(string corruption)
    {
        var valid = Fixture();
        var snapshot = valid.Snapshot;
        var workspace = snapshot.Workspace;
        var malformed = corruption switch
        {
            "snapshot-null" => valid with { Snapshot = null! },
            "workspace-null" => valid with { Snapshot = snapshot with { Workspace = null! } },
            "document-null" => valid with { Snapshot = snapshot with { Workspace = workspace with { Document = null! } } },
            "state-null" => valid with { Snapshot = WithState(snapshot, null!) },
            "auxiliary-null" => valid with { Snapshot = WithState(snapshot, workspace.Document.State with { AuxiliaryState = null! }) },
            "ledger-null" => valid with { Snapshot = snapshot with { DelegatedGmCharacterEdits = null! } },
            "ledger-entry-null" => valid with { Snapshot = snapshot with { DelegatedGmCharacterEdits = [null!] } },
            "digest-mismatch" => valid with { SnapshotDigest = new string('0', 64) },
            _ => throw new AssertFailedException("Unknown corruption case.")
        };
        Assert.ThrowsExactly<JsonException>(() => WorkspaceContinuationCodec.Encode(malformed, MaximumBytes));
    }

    [TestMethod]
    public void Positive_future_payload_schema_and_zero_checkpoint_are_lossless_candidates_not_runtime_admission()
    {
        var snapshot = Fixture().Snapshot;
        snapshot = WithState(snapshot, snapshot.Workspace.Document.State with { SchemaVersion = 999 });
        snapshot = snapshot with { Workspace = snapshot.Workspace with { SavedRevision = 0, Document = snapshot.Workspace.Document with { Format = WorkspaceDocumentFormat.Json } } };
        var expected = Sign(snapshot);
        var candidate = Decode(WorkspaceContinuationCodec.Encode(expected, MaximumBytes));
        AssertEquivalent(expected, candidate);
        Assert.AreEqual(999, candidate.Snapshot.Workspace.Document.SchemaVersion);
        Assert.AreEqual(0L, candidate.Snapshot.Workspace.SavedRevision);
        Assert.AreEqual(WorkspaceDocumentFormat.Json, candidate.Snapshot.Workspace.Document.Format);
    }

    [TestMethod]
    public void Caller_byte_limit_is_exact_on_encode_and_decode()
    {
        var expected = Fixture();
        byte[] encoded = WorkspaceContinuationCodec.Encode(expected, MaximumBytes);
        CollectionAssert.AreEqual(encoded, WorkspaceContinuationCodec.Encode(expected, encoded.Length));
        AssertEquivalent(expected, Decode(encoded, encoded.Length));
        Assert.ThrowsExactly<JsonException>(() => WorkspaceContinuationCodec.Encode(expected, encoded.Length - 1));
        Assert.IsFalse(WorkspaceContinuationCodec.TryDecodeCandidate(encoded, encoded.Length - 1, out var candidate));
        Assert.IsNull(candidate);
        byte[] oversizedInvalidJson = Enumerable.Repeat((byte)'[', 257).ToArray();
        Assert.IsFalse(WorkspaceContinuationCodec.TryDecodeCandidate(oversizedInvalidJson, 256, out candidate));
        Assert.IsNull(candidate);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Oversized_payload_is_rejected_before_enumerating_a_throwing_GM_ledger(bool multibyte)
    {
        const int tinyLimit = 128;
        string payload = multibyte ? new string('é', 80) : new string('x', 256);
        Assert.IsTrue(Encoding.UTF8.GetByteCount(payload) > tinyLimit);
        ThrowingLedger ledger = new();
        var snapshot = Fixture().Snapshot;
        snapshot = WithState(snapshot, snapshot.Workspace.Document.State with { Payload = payload }) with
        {
            DelegatedGmCharacterEdits = ledger
        };
        // This has a syntactically valid dummy digest. Computing a real digest
        // would itself enumerate the adversarial ledger before calling Encode.
        var candidate = new WorkspaceContinuationExport(snapshot, new string('a', 64));
        Assert.ThrowsExactly<JsonException>(() => WorkspaceContinuationCodec.Encode(candidate, tinyLimit));
        Assert.AreEqual(0, ledger.EnumerationAttempts);
        Assert.AreEqual(0, ledger.IndexerReads);
    }

    [TestMethod]
    public async Task Alternating_and_concurrent_calls_cannot_reuse_another_calls_cached_byte_limit()
    {
        const int smallLimit = 128;
        const int largeLimit = 8192;
        var expected = Fixture(new string('x', 2048));
        byte[] expectedBytes = WorkspaceContinuationCodec.Encode(expected, largeLimit);
        Assert.IsTrue(expectedBytes.Length > smallLimit);
        Assert.IsTrue(expectedBytes.Length < largeLimit);

        void AssertLargeBudget()
        {
            byte[] bytes = WorkspaceContinuationCodec.Encode(expected, largeLimit);
            CollectionAssert.AreEqual(expectedBytes, bytes);
            AssertEquivalent(expected, Decode(bytes, largeLimit));
        }

        void AssertSmallBudget()
        {
            ThrowingLedger ledger = new();
            var oversized = new WorkspaceContinuationExport(expected.Snapshot with
            {
                DelegatedGmCharacterEdits = ledger
            }, new string('a', 64));
            Assert.ThrowsExactly<JsonException>(() => WorkspaceContinuationCodec.Encode(oversized, smallLimit));
            // A mistakenly reused large-limit converter might eventually hit
            // the small stream cap, but must not get as far as the ledger.
            Assert.AreEqual(0, ledger.EnumerationAttempts);
            Assert.AreEqual(0, ledger.IndexerReads);
        }

        for (int iteration = 0; iteration < 3; iteration++)
        {
            AssertLargeBudget();
            AssertSmallBudget();
            AssertSmallBudget();
            AssertLargeBudget();
        }

        using Barrier startTogether = new(2);
        using CancellationTokenSource aborted = new();
        Task RunConcurrent(Action assertion) => Task.Factory.StartNew(() =>
        {
            try
            {
                for (int iteration = 0; iteration < 8; iteration++)
                {
                    Assert.IsTrue(startTogether.SignalAndWait(TimeSpan.FromSeconds(5), aborted.Token),
                        "Both caller budgets must enter each concurrent round.");
                    assertion();
                }
            }
            catch
            {
                aborted.Cancel();
                throw;
            }
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);

        Task largeCaller = RunConcurrent(AssertLargeBudget);
        Task smallCaller = RunConcurrent(AssertSmallBudget);
        await Task.WhenAll(largeCaller, smallCaller);
    }

    [TestMethod]
    public void Raw_Unicode_input_that_exceeds_the_cap_after_canonical_encoding_is_rejected()
    {
        var expected = Fixture(new string('é', 2048));
        byte[] canonical = WorkspaceContinuationCodec.Encode(expected, MaximumBytes);
        string compact = JsonNode.Parse(canonical)!.ToJsonString(new JsonSerializerOptions
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });
        byte[] raw = Encoding.UTF8.GetBytes(compact);
        Assert.IsTrue(raw.Length < canonical.Length);
        AssertEquivalent(expected, Decode(raw));
        Assert.IsFalse(WorkspaceContinuationCodec.TryDecodeCandidate(raw, raw.Length, out var candidate));
        Assert.IsNull(candidate);
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(-1)]
    public void Nonpositive_caller_limits_are_argument_errors(int limit)
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => WorkspaceContinuationCodec.Encode(Fixture(), limit));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => WorkspaceContinuationCodec.TryDecodeCandidate(ReadOnlyMemory<byte>.Empty, limit, out _));
    }

    [TestMethod]
    public void Invalid_JSON_bytes_and_incomplete_documents_fail_without_throwing_or_returning_partial_state()
    {
        byte[][] malformed =
        [
            [], [0xff, 0xfe], Encoding.UTF8.GetBytes("null"), Encoding.UTF8.GetBytes("[]"),
            Encoding.UTF8.GetBytes("{}"), Encoding.UTF8.GetBytes("{\"Snapshot\":"),
            Encoding.UTF8.GetBytes("/* comment */{}"), Encoding.UTF8.GetBytes("{\"Snapshot\":{},}")
        ];
        foreach (byte[] input in malformed)
            AssertRejected(input);
        byte[] valid = WorkspaceContinuationCodec.Encode(Fixture(), MaximumBytes);
        AssertRejected(valid.Concat(Encoding.UTF8.GetBytes("{}")).ToArray());
    }

    [TestMethod]
    [DataRow(0xd800)]
    [DataRow(0xdc00)]
    public void Encode_does_not_silently_replace_unpaired_UTF16_in_typed_payloads(int codeUnit)
    {
        var expected = Fixture("transport text " + new string((char)codeUnit, 1));
        Assert.ThrowsExactly<JsonException>(() => WorkspaceContinuationCodec.Encode(expected, MaximumBytes));
    }

    [TestMethod]
    [DataRow("D800")]
    [DataRow("DC00")]
    public void Escaped_lone_surrogate_wire_is_rejected_but_literal_replacement_character_is_preserved(string codeUnit)
    {
        const string replacementPayload = "transport text \ufffd";
        var expected = Fixture(replacementPayload);
        string text = Encoding.UTF8.GetString(WorkspaceContinuationCodec.Encode(expected, MaximumBytes));
        string encodedPayload = JsonSerializer.Serialize(replacementPayload);
        Assert.IsTrue(text.Contains(encodedPayload, StringComparison.Ordinal));
        string literalReplacement = text.Replace(encodedPayload, "\"transport text \ufffd\"", StringComparison.Ordinal);
        var valid = Decode(Encoding.UTF8.GetBytes(literalReplacement));
        AssertEquivalent(expected, valid);
        Assert.AreEqual(replacementPayload, valid.Snapshot.Workspace.Document.Content);

        // Keep the U+FFFD digest: a decoder that silently substitutes U+FFFD
        // for the unmatched surrogate would otherwise appear digest-valid.
        string malformed = text.Replace(encodedPayload, "\"transport text \\u" + codeUnit + "\"", StringComparison.Ordinal);
        AssertRejected(Encoding.UTF8.GetBytes(malformed));
    }

    private static WorkspaceContinuationExport RealGmExport()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"chummer-codec-gm-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            OwnerScope owner = new("owner-a");
            CharacterWorkspaceId id = new("codec-gm-runner");
            FileWorkspaceStore store = new(directory);
            WorkspaceDocument document = new("<character><name>Runner One</name><alias>One</alias><notes>Original note</notes><metatype>Human</metatype><buildmethod>Priority</buildmethod><createdversion>1.0</createdversion><appversion>1.0</appversion><karma>0</karma><nuyen>0</nuyen><created>True</created></character>", "sr5");
            Assert.IsTrue(store.CreateWorkspaceDocument(owner, id, document).Success);
            CharacterFileService files = new();
            Sr5WorkspaceCodec codec = new(new XmlCharacterFileQueries(files),
                new XmlCharacterSectionQueries(new CharacterSectionService()), new XmlCharacterMetadataCommands(files));
            var service = new DelegatedGmCharacterEditService(store, new RulesetWorkspaceCodecResolver([codec]),
                new GmAuthorizer(), new GmClock());
            var applied = service.Execute(new("codec-campaign", "gm@example.com", owner, id, 1,
                "codec-gm-edit", "Correct campaign-visible note",
                [new(DelegatedGmCharacterPatchOperationKind.Replace, DelegatedGmCharacterEditContract.ProfileNotesPath, "GM-visible note")]));
            Assert.AreEqual(DelegatedGmCharacterEditOutcome.Applied, applied.Outcome, applied.Error);
            var read = store.ReadContinuation(owner, id);
            Assert.IsTrue(read.Success, read.Error);
            Assert.IsNotNull(read.Value);
            Assert.HasCount(1, read.Value.DelegatedGmCharacterEdits);
            return Sign(read.Value);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    // Campaign admission is the only adapter double. The production service,
    // codec and durable store construct and retain the actual audit receipt.
    private sealed class GmAuthorizer : ICampaignGmCharacterEditAuthorizer
    {
        public CampaignGmCharacterEditAuthorization Authorize(CampaignGmCharacterEditAuthorizationRequest request) => new(
            true, request.CampaignId, request.ActorId, DelegatedGmCharacterEditContract.GameMasterRole,
            DelegatedGmCharacterEditContract.CharacterEditScope, request.CharacterOwner, request.CharacterId,
            "codec-delegation", "campaign-owner@example.com", request.CharacterOwner.NormalizedValue,
            "codec-authority-receipt", 7, GmClock.Now.AddMinutes(-5), GmClock.Now.AddHours(1),
            [DelegatedGmCharacterEditContract.ProfileNotesPath]);
    }

    private sealed class GmClock : TimeProvider
    {
        public static readonly DateTimeOffset Now = new(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class ThrowingLedger : IReadOnlyList<DelegatedGmCharacterEditAuditReceipt>
    {
        public int Count => 1;
        public int EnumerationAttempts { get; private set; }
        public int IndexerReads { get; private set; }
        public DelegatedGmCharacterEditAuditReceipt this[int index]
        {
            get
            {
                IndexerReads++;
                throw new InvalidOperationException("Oversized payload must be rejected before reading ledger entries.");
            }
        }
        public IEnumerator<DelegatedGmCharacterEditAuditReceipt> GetEnumerator()
        {
            EnumerationAttempts++;
            throw new InvalidOperationException("Oversized payload must be rejected before enumerating the ledger.");
        }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private static WorkspaceContinuationExport Fixture(string payload = UnicodePayload)
    {
        // This is content-transport data, not a rule-valid imported character.
        WorkspaceContinuationSnapshot snapshot = new("owner-a",
            new(new("codec-runner"), new WorkspaceDocument(payload, "sr5"),
                new DateTimeOffset(2026, 9, 9, 12, 0, 0, TimeSpan.Zero), 7, 6), []);
        return Sign(snapshot);
    }

    private static WorkspaceContinuationExport Sign(WorkspaceContinuationSnapshot snapshot) =>
        new(snapshot, WorkspaceContinuationSnapshotDigest.Compute(snapshot));

    private static WorkspaceContinuationSnapshot WithState(WorkspaceContinuationSnapshot snapshot, WorkspaceDocumentState state) =>
        snapshot with { Workspace = snapshot.Workspace with { Document = snapshot.Workspace.Document with { State = state } } };

    private static JsonObject Wire() => JsonNode.Parse(WorkspaceContinuationCodec.Encode(Fixture(), MaximumBytes))!.AsObject();

    private static byte[] UncheckedWire(WorkspaceContinuationExport candidate) => JsonSerializer.SerializeToUtf8Bytes(
        new UncheckedEnvelope(WorkspaceContinuationSnapshot.ContractName, candidate.Snapshot, candidate.SnapshotDigest),
        new JsonSerializerOptions { IgnoreReadOnlyProperties = true });

    private sealed record UncheckedEnvelope(string ContractName, WorkspaceContinuationSnapshot Snapshot, string SnapshotDigest);

    private static WorkspaceContinuationExport Decode(byte[] bytes, int maximumBytes = MaximumBytes)
    {
        Assert.IsTrue(WorkspaceContinuationCodec.TryDecodeCandidate(bytes, maximumBytes, out var candidate));
        Assert.IsNotNull(candidate);
        return candidate;
    }

    private static void AssertEquivalent(WorkspaceContinuationExport expected, WorkspaceContinuationExport actual)
    {
        Assert.AreEqual(expected.SnapshotDigest, actual.SnapshotDigest);
        Assert.AreEqual(JsonSerializer.Serialize(expected), JsonSerializer.Serialize(actual));
        Assert.AreEqual(expected.Snapshot.Workspace.Document.AuxiliaryStateDigest, actual.Snapshot.Workspace.Document.AuxiliaryStateDigest);
    }

    private static void AssertRejected(JsonNode node, string? message = null) => AssertRejected(Encoding.UTF8.GetBytes(node.ToJsonString()), message);

    private static void AssertRejected(byte[] bytes, string? message = null)
    {
        Assert.IsFalse(WorkspaceContinuationCodec.TryDecodeCandidate(bytes, MaximumBytes, out var candidate), message);
        Assert.IsNull(candidate, message);
    }

    private static JsonNode? At(JsonNode root, string path)
    {
        JsonNode? current = root;
        if (path.Length == 0) return current;
        foreach (string part in path.Split('.')) current = current![part];
        return current;
    }

    private static (JsonObject Parent, string Name) Parent(JsonNode root, string path)
    {
        int separator = path.LastIndexOf('.');
        return separator < 0 ? (root.AsObject(), path) : (At(root, path[..separator])!.AsObject(), path[(separator + 1)..]);
    }

    private static JsonNode? ReverseObjectProperties(JsonNode? node) => node switch
    {
        JsonObject obj => new JsonObject(obj.Reverse().Select(pair =>
            new KeyValuePair<string, JsonNode?>(pair.Key, ReverseObjectProperties(pair.Value)))),
        JsonArray array => new JsonArray(array.Select(ReverseObjectProperties).ToArray()),
        _ => node?.DeepClone()
    };
}

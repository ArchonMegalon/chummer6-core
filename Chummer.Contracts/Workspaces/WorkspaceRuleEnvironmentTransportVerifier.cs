using System.Buffers.Binary;
using System.Text;
using System.Text.Json;

namespace Chummer.Contracts.Workspaces;

/// <summary>Bounded custody observation of the ORIGINAL V2 framing, not Core admission.
/// Checkpoints are private server state, never wire inputs. Receiver must still import/admit.
/// Only bounded SHA tails and identity/framing observations are retained in this private checkpoint.</summary>
public sealed class WorkspaceRuleEnvironmentTransportVerifier
{
    public WorkspaceRuleEnvironmentHash Archive { get; set; } = new();
    public WorkspaceRuleEnvironmentHash Part { get; set; } = new();
    public WorkspaceRuleEnvironmentJsonObservation Json { get; set; } = new();
    public int Stage { get; set; }
    public byte[] Integer { get; set; } = new byte[4];
    public int IntegerBytes { get; set; }
    public int Remaining { get; set; }
    public string Text { get; set; } = "";
    public string PreviousDigest { get; set; } = "";
    public int Archives { get; set; }
    public int ArchivesRead { get; set; }
    public long ArchiveBytes { get; set; }
    public int Campaigns { get; set; }
    public Dictionary<string, string> Identity { get; set; } = new(StringComparer.Ordinal);
    public string? ContinuationDigest { get; set; }
    public string? EnvironmentDigest { get; set; }
    public string? HistoryDigest { get; set; }

    public void Append(ReadOnlySpan<byte> bytes)
    {
        Archive.Append(bytes);
        Span<byte> one = stackalloc byte[1];
        foreach (byte value in bytes)
        {
            if (Stage is 0 or 2 or 4 or 5 or 7 or 9 or 11 or 13)
            {
                Integer[IntegerBytes++] = value;
                if (IntegerBytes != 4) continue;
                IntegerBytes = 0;
                int length = BinaryPrimitives.ReadInt32LittleEndian(Integer);
                if (length < 0) throw Invalid();
                if (Stage == 4)
                {
                    if (length is < 1 or > WorkspacePrivateRuleSourceClosureBinding.MaximumArchives) throw Invalid();
                    Archives = length; Stage = 5; continue;
                }
                int maximum = Stage switch { 0 => 128, 2 => WorkspacePrivateRuleSourceClosureBinding.MaximumContinuationBytes,
                    5 => 71, 7 => WorkspacePrivateRuleSourceClosureBinding.MaximumBytes, 9 => 1024 * 1024, 11 => 8 * 1024 * 1024, 13 => WorkspaceAuthenticatedHistory.MaximumBytes, _ => 0 };
                if (length == 0 || length > maximum) throw Invalid();
                Remaining = length; Text = ""; Part = new();
                if (Stage is 2 or 9 or 13) Json = new();
                Stage++; continue;
            }
            if (Stage == 15) throw Invalid();
            if (Remaining <= 0) throw Invalid();
            if (Stage is 1 or 6) Text += (char)value;
            if (Stage is 3 or 10 or 14) Json.Append(value);
            if (Stage is 8 or 12 or 14) { one[0] = value; Part.Append(one); }
            if (--Remaining != 0) continue;
            switch (Stage)
            {
                case 1:
                    if (Text != WorkspacePrivateRuleSourceClosureBinding.SchemaV2) throw Invalid();
                    Stage = 2; break;
                case 3:
                    Json.Finish(); Identity = Json.Values;
                    ContinuationDigest = Json.Snapshot.Digest(); Stage = 4; break;
                case 6:
                    if (!WorkspaceRuleEnvironmentTransfer.IsArchiveDigest(Text)
                        || StringComparer.Ordinal.Compare(PreviousDigest, Text) >= 0) throw Invalid();
                    PreviousDigest = Text; Stage = 7; break;
                case 8:
                    if ("sha256:" + Part.Digest() != PreviousDigest) throw Invalid();
                    ArchiveBytes = checked(ArchiveBytes + Part.Length);
                    Stage = ++ArchivesRead == Archives ? 9 : 5; break;
                case 10:
                    Json.Finish(); Campaigns = Json.RootItems;
                    if (Campaigns > WorkspacePrivateRuleSourceClosureBinding.MaximumCampaigns) throw Invalid();
                    Stage = 11; break;
                case 12:
                    EnvironmentDigest = "sha256:" + Part.Digest(); Stage = 13; break;
                case 14:
                    // One optional bounded history JSON block. It remains part of
                    // the ORIGINAL archive digest; Core alone validates canonical
                    // history bytes, observations and owner/origin authentication.
                    Json.Finish(); HistoryDigest = Part.Digest(); Stage = 15; break;
            }
        }
    }
    /// <summary>Validate an authenticated roaming pointer against sealed original custody.
    /// Publication still requires the server's existing owner scope, remote CAS and
    /// durable original-operation transaction. A client-supplied checkpoint is never accepted.</summary>
    public void Complete(WorkspaceRoamingRuleEnvironmentSnapshot snapshot, string authenticatedOwner)
    {
        if (!snapshot.IsValid(authenticatedOwner)) throw Invalid();
        Complete(snapshot.RuleEnvironment);
        if (!Enum.TryParse<WorkspaceDocumentFormat>(snapshot.Format, true, out var format)
            || Identity.GetValueOrDefault("Snapshot.Workspace.Document.Format") != ((int)format).ToString(System.Globalization.CultureInfo.InvariantCulture)
            || Identity.GetValueOrDefault("Snapshot.Workspace.Document.State.RulesetId") != snapshot.RulesetId
            || Identity.GetValueOrDefault("Snapshot.Workspace.Document.State.SchemaVersion") != snapshot.SchemaVersion.ToString(System.Globalization.CultureInfo.InvariantCulture)
            || Identity.GetValueOrDefault("Snapshot.Workspace.Document.State.PayloadKind") != snapshot.PayloadKind
            || !DateTimeOffset.TryParse(Identity.GetValueOrDefault("Snapshot.Workspace.LastUpdatedUtc"),
                System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var updated)
            || !updated.EqualsExact(snapshot.UpdatedAtUtc)) throw Invalid();
    }

    public void Complete(WorkspaceRuleEnvironmentTransfer transfer)
    {
        var b = transfer.Binding;
        if ((Stage != 13 && Stage != 15) || IntegerBytes != 0 || Archive.Length != transfer.ByteLength || "sha256:" + Archive.Digest() != b.ClosureDigest
            || Identity.GetValueOrDefault("ContractName") != WorkspaceContinuationSnapshot.ContractName
            || Identity.GetValueOrDefault("Snapshot.OwnerId") != b.OwnerId
            || Identity.GetValueOrDefault("Snapshot.Workspace.Id.Value") != b.WorkspaceId.Value
            || Identity.GetValueOrDefault("Snapshot.Workspace.ContentRevision") != b.ContentRevision.ToString(System.Globalization.CultureInfo.InvariantCulture)
            || Identity.GetValueOrDefault("Snapshot.Workspace.SavedRevision") != b.SavedRevision.ToString(System.Globalization.CultureInfo.InvariantCulture)
            || Identity.GetValueOrDefault("SnapshotDigest") != b.ContinuationDigest || ContinuationDigest != b.ContinuationDigest
            || Archives != b.ArchiveCount || ArchiveBytes != b.ArchiveBytes || Campaigns != b.CampaignCount
            || EnvironmentDigest != b.CurrentEnvironmentDigest) throw Invalid();
    }
    private static InvalidDataException Invalid() => new("The original private archive identity is invalid.");
}

/// <summary>Streaming JSON framing and exact identity observation. Large irrelevant strings are
/// scanned without buffering. Semantic/canonical validation remains the receiver's Core job.</summary>
public sealed class WorkspaceRuleEnvironmentJsonObservation
{
    public sealed record Frame(bool Array, string Path, int State = 0, string Property = "");
    public List<Frame> Frames { get; set; } = [];
    public Dictionary<string, string> Values { get; set; } = new(StringComparer.Ordinal);
    public string Token { get; set; } = "";
    public bool InString { get; set; }
    public bool Escape { get; set; }
    public bool Scalar { get; set; }
    public bool Property { get; set; }
    public bool Capture { get; set; }
    public string Path { get; set; } = "";
    public bool Started { get; set; }
    public bool Done { get; set; }
    public int RootItems { get; set; }
    public int SnapshotDepth { get; set; }
    public WorkspaceRuleEnvironmentHash Snapshot { get; set; } = new();
    private static bool Relevant(string path) => path is "ContractName" or "SnapshotDigest" or "Snapshot.OwnerId"
        or "Snapshot.Workspace.ContentRevision" or "Snapshot.Workspace.SavedRevision" or "Snapshot.Workspace.Id.Value"
        or "Snapshot.Workspace.LastUpdatedUtc" or "Snapshot.Workspace.Document.Format"
        or "Snapshot.Workspace.Document.State.RulesetId" or "Snapshot.Workspace.Document.State.SchemaVersion"
        or "Snapshot.Workspace.Document.State.PayloadKind";
    private static bool Parent(string path) => path is "" or "Snapshot" or "Snapshot.Workspace" or "Snapshot.Workspace.Id"
        or "Snapshot.Workspace.Document" or "Snapshot.Workspace.Document.State";
    public void Append(byte value)
    {
        bool hashing = SnapshotDepth > 0;
        if (hashing) Hash(value);
        Process(value);
        if (hashing && SnapshotDepth > 0 && Frames.Count < SnapshotDepth)
        { Hash((byte)'}'); SnapshotDepth = 0; }
    }
    private void Hash(byte value) { Span<byte> one = stackalloc byte[1]; one[0] = value; Snapshot.Append(one); }
    private void Process(byte value)
    {
        char c = (char)value;
        if (InString)
        {
            if (Capture)
            {
                if (Token.Length >= 4096) throw Invalid();
                Token += c;
            }
            if (Escape) { Escape = false; return; }
            if (c == '\\') { Escape = true; return; }
            if (c != '"') { if (value < 32) throw Invalid(); return; }
            InString = false;
            string text = Capture ? JsonSerializer.Deserialize<string>(Encoding.Latin1.GetBytes(Token)) ?? throw Invalid() : "";
            if (Property)
            {
                var frame = Frames[^1]; Frames[^1] = frame with { Property = text, State = 1 };
            }
            else { Record(text); EndValue(); }
            Token = ""; return;
        }
        if (Scalar)
        {
            if (c is not (',' or ']' or '}' or ' ' or '\r' or '\n' or '\t'))
            { if (Capture) { if (Token.Length >= 128) throw Invalid(); Token += c; } return; }
            Scalar = false; Record(Token); Token = ""; EndValue();
        }
        if (c is ' ' or '\r' or '\n' or '\t') return;
        if (Done) throw Invalid();
        if (Frames.Count > 0)
        {
            var frame = Frames[^1];
            if (frame.State == 1)
            { if (c != ':') throw Invalid(); Frames[^1] = frame with { State = 2 }; return; }
            if (frame.State == 3)
            {
                if (c == ',') { Frames[^1] = frame with { State = 4 }; return; }
                if (c != (frame.Array ? ']' : '}')) throw Invalid();
                Frames.RemoveAt(Frames.Count - 1); EndValue(); return;
            }
            if (frame.State == 0 && c == (frame.Array ? ']' : '}'))
            { Frames.RemoveAt(Frames.Count - 1); EndValue(); return; }
            if (!frame.Array && frame.State is 0 or 4)
            {
                if (c != '"') throw Invalid();
                Property = true; InString = true; Capture = Parent(frame.Path); Token = Capture ? "\"" : ""; return;
            }
            if (!frame.Array && frame.State != 2) throw Invalid();
            Path = frame.Array || !Parent(frame.Path) ? "*" : (frame.Path.Length == 0 ? frame.Property : frame.Path + "." + frame.Property);
            if (Frames.Count == 1 && frame.Array) RootItems++;
        }
        else { if (Started) throw Invalid(); Started = true; Path = ""; }
        if (c is '{' or '[')
        {
            if (Frames.Count >= 128) throw Invalid();
            Frames.Add(new(c == '[', Path));
            if (Path == "Snapshot")
            {
                if (Snapshot.Length != 0 || c != '{') throw Invalid();
                Snapshot.Append(Encoding.UTF8.GetBytes("{\"ContractName\":\"" + WorkspaceContinuationSnapshot.ContractName + "\",\"Snapshot\":"));
                Hash(value); SnapshotDepth = Frames.Count;
            }
            return;
        }
        Property = false; Capture = Relevant(Path); Token = Capture ? c.ToString() : "";
        if (c == '"') { InString = true; return; }
        if (c is '-' or >= '0' and <= '9' or 't' or 'f' or 'n') { Scalar = true; return; }
        throw Invalid();
    }
    private void Record(string value)
    { if (Capture && !Values.TryAdd(Path, value)) throw Invalid(); }
    private void EndValue()
    {
        if (Frames.Count == 0) { Done = true; return; }
        Frames[^1] = Frames[^1] with { State = 3, Property = "" };
    }
    public void Finish() { if (!Done || InString || Scalar || Frames.Count != 0) throw Invalid(); }
    private static InvalidDataException Invalid() => new("The private archive JSON framing is invalid.");
}

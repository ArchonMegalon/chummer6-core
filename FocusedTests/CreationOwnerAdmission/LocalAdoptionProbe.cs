using System.Text.Json;
using System.Security.Cryptography;
using Chummer.Application.Characters;
using Chummer.Application.LifeModules;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.LifeModules;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;
using Chummer.Infrastructure.Workspaces;
using Microsoft.Extensions.DependencyInjection;

string root = Check.Root(args);
(string Name, Action Run)[] cases =
[
    ("real Life Modules bootstrap survives account handoff and cold reopen", CompleteHandoff),
    ("initial Origin checkpoint survives adoption before any module is accepted", InitialCheckpoint),
    ("missing confirmation and foreign reviews do not claim local data", Confirmation),
    ("account A-B-A rejects the original review", OwnerAba),
    ("changed local runner rejects stale review without losing edits", ChangedSource),
    ("existing account runner is never overwritten", ExistingTarget),
    ("cancellation before claim preserves the local runner", Canceled),
    ("expired review preserves local ownership", Expired),
    ("interrupted durable claim fences local and foreign access and recovers cold", Interrupted),
    ("failure before claim preserves source bytes", BeforeClaimFailure),
    ("post-placement observer failure cannot turn commit into retry", AfterMoveFailure)
];
foreach (var (name, run) in cases) { run(); Console.WriteLine("PASS " + name); }
Console.WriteLine($"PASS {cases.Length} local runner adoption cases");
int fixtureIndex = Array.IndexOf(args, "--saved-runner");
if (fixtureIndex >= 0)
{
    int hashIndex = Array.IndexOf(args, "--saved-runner-sha256");
    Check.That(fixtureIndex + 1 < args.Length && hashIndex >= 0 && hashIndex + 1 < args.Length,
        "A retained runner fixture requires its exact SHA256.");
    string path = Path.GetFullPath(args[fixtureIndex + 1]);
    Check.That((File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0, "Linked fixture paths are not accepted.");
    Check.That(Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))) == args[hashIndex + 1],
        "Retained fixture bytes differ from the declared source.");
    using var f = new CreationFixture(root, CreationFixture.AccountA);
    CharacterWorkspaceId id = new(Path.GetFileNameWithoutExtension(path));
    Check.That(!string.IsNullOrWhiteSpace(id.Value) && id.Value.All(char.IsLetterOrDigit), "Invalid retained runner identity.");
    // Copy only the explicitly named synthetic diagnostic runner into a fresh
    // test-owned store. Never modify the preserved app state or read credentials.
    File.Copy(path, Path.Combine(f.StateDirectory, "workspaces", id.Value + ".json"), overwrite: false);
    var original = f.Store.Get(id).Value;
    Check.That(original?.Document.AuxiliaryState.LifeModuleDecisionAcceptances is { Count: > 0 },
        "Retained runner must include actual accepted Life Modules, not only an empty bootstrap.");
    string before = JsonSerializer.Serialize(original!.Document);
    f.Authority.Transition(OwnerScope.LocalSingleUser);
    var books = new OwnerBoundLifeModuleBookService(f.Store, f.Authority);
    var localBook = books.Load(f.Authority.Capture(), id, original.ContentRevision, original.SavedRevision);
    Check.That(localBook.Value is not null, "Retained native book projection is unavailable before adoption.");
    var interaction = f.Provider.GetRequiredService<IOwnerBoundLifeModuleOriginService>();
    var localDraft = interaction.Start(f.Authority.Capture(), id.Value);
    Check.That(localDraft.Value is not null, "Retained native next decision is unavailable before adoption.");
    var choice = localDraft.Value!.Projection.CurrentTurn.LegalChoices.FirstOrDefault(item =>
        item.IsLegal && item.Blockers.Count == 0 && (item.FollowUps?.All(prompt => !prompt.IsRequired) ?? true));
    Check.That(choice is not null, "The retained native fixture has no answer-free next-module choice.");
    var localPending = interaction.Prepare(f.Authority.Capture(), localDraft.Value, choice!.ChoiceId);
    Check.That(localPending.Value?.PendingPreview is not null, "Local preview fixture is unavailable.");
    f.Authority.Transition(CreationFixture.AccountA);
    var service = Service(f); var review = Review(service, f, id);
    Check.That(service.Confirm(f.Authority.Capture(), review, true).Outcome == WorkspaceLocalAdoptionOutcome.Applied,
        "Actual saved Life Modules runner could not be adopted.");
    var cold = new FileWorkspaceStore(f.StateDirectory);
    Check.That(before == JsonSerializer.Serialize(cold.Get(CreationFixture.AccountA, id).Value!.Document),
        "Actual saved module history changed during adoption.");
    Check.That(cold.Get(id).Outcome == WorkspaceOperationOutcome.Missing
        && cold.Get(CreationFixture.AccountB, id).Outcome == WorkspaceOperationOutcome.Missing,
        "Actual adopted runner leaked through a different owner partition.");
    var accountBook = books.Load(f.Authority.Capture(), id, original.ContentRevision, original.SavedRevision);
    Check.That(accountBook.Value is not null
        && JsonSerializer.Serialize(localBook.Value) == JsonSerializer.Serialize(accountBook.Value),
        "Account adoption changed historical chapter identities or canonical story facts.");
    var next = interaction.Start(f.Authority.Capture(), id.Value);
    Check.That(next.Outcome == LifeModuleOriginDossierOutcomes.Success && next.Value is not null,
        "The adopted runner cannot resume its next Life Modules decision: " + next.Outcome
        + "; " + string.Join(",", next.Blockers));
    Check.That(next.Value!.OwnerId == CreationFixture.AccountA.NormalizedValue,
        "The resumed decision retained local account authority.");
    Check.That(JsonSerializer.Serialize(next.Value.Projection) == JsonSerializer.Serialize(localDraft.Value!.Projection),
        "Adoption rewrote the canonical narrative projection.");
    Check.That(interaction.Restore(f.Authority.Capture(), localDraft.Value).Value is null,
        "A pre-adoption local checkpoint became an account-authorized command.");
    Check.That(interaction.Restore(f.Authority.Capture(), next.Value).Value is not null,
        "The account-bound checkpoint cannot be reopened.");
    var rebound = interaction.AdoptLocalCheckpoint(f.Authority.Capture(), localPending.Value!);
    Check.That(rebound.Value?.OwnerId == CreationFixture.AccountA.NormalizedValue
        && rebound.Value.PendingPreview?.PreviewDigest == localPending.Value!.PendingPreview!.PreviewDigest
        && JsonSerializer.Serialize(rebound.Value.Projection) == JsonSerializer.Serialize(localPending.Value.Projection),
        "An admitted handoff did not preserve the pending choice and its exact review.");
    Check.That(interaction.AdoptLocalCheckpoint(f.Authority.Capture(), localPending.Value! with
        { CheckpointDigest = new string('0', 64) }).Value is null,
        "Adoption resealed an invalid checkpoint.");
    var accountStamp = f.Authority.Capture();
    f.Authority.Transition(CreationFixture.AccountB);
    Check.That(interaction.Restore(f.Authority.Capture(), next.Value).Value is null,
        "A foreign account admitted the adopted draft.");
    Check.That(interaction.AdoptLocalCheckpoint(f.Authority.Capture(), localPending.Value!).Value is null,
        "A foreign account adopted another account's pending choice.");
    Check.That(interaction.Restore(f.Authority.Capture(), next.Value with
        { OwnerId = CreationFixture.AccountB.NormalizedValue }).Value is null,
        "Relabeling a checkpoint granted a foreign account admission.");
    f.Authority.Transition(CreationFixture.AccountA);
    Check.That(interaction.Restore(accountStamp, next.Value).Value is null,
        "An account A-B-A transition revived a stale owner stamp.");
    var prepared = interaction.Prepare(f.Authority.Capture(), next.Value, choice.ChoiceId);
    Check.That(prepared.Value?.PendingPreview is not null,
        "The adopted runner cannot prepare its next choice: " + string.Join(",", prepared.Blockers));
    Check.That(prepared.Value!.CheckpointDigest == rebound.Value!.CheckpointDigest,
        "Rebinding and a fresh account preview disagree about the same next decision.");
    const string decisionKey = "local-adoption-next-native-module";
    var accepted = interaction.Confirm(f.Authority.Capture(), prepared.Value!,
        prepared.Value!.PendingPreview!.PreviewDigest, decisionKey, explicitlyConfirmed: true);
    Check.That(accepted.Value is not null,
        "The adopted runner cannot confirm its next choice: " + string.Join(",", accepted.Blockers));
    var committed = cold.Get(CreationFixture.AccountA, id).Value!;
    Check.That(committed.ContentRevision == original.ContentRevision + 1,
        "The first post-adoption decision did not make exactly one rules edit.");
    Check.That(JsonSerializer.Serialize(committed.Document.AuxiliaryState.LifeModuleDecisionAcceptances!
        .Take(original.Document.AuxiliaryState.LifeModuleDecisionAcceptances!.Count))
        == JsonSerializer.Serialize(original.Document.AuxiliaryState.LifeModuleDecisionAcceptances),
        "The next account-owned decision rewrote historical acceptances.");
    var replay = interaction.Confirm(f.Authority.Capture(), prepared.Value,
        prepared.Value.PendingPreview.PreviewDigest, decisionKey, explicitlyConfirmed: true);
    Check.That(replay.Value is not null && JsonSerializer.Serialize(replay.Value) == JsonSerializer.Serialize(accepted.Value)
        && cold.Get(CreationFixture.AccountA, id).Value!.ContentRevision == committed.ContentRevision,
        "Retry duplicated the accepted post-adoption module.");
    var restarted = f.AddProvider(new FileWorkspaceStore(f.StateDirectory), f.Authority)
        .GetRequiredService<IOwnerBoundLifeModuleOriginService>();
    Check.That(restarted.Restore(f.Authority.Capture(), accepted.Value!.Checkpoint).Value is not null,
        "The post-adoption module cannot reopen through a fresh service and store.");
    Check.That(cold.Get(id).Outcome == WorkspaceOperationOutcome.Missing
        && cold.Get(CreationFixture.AccountB, id).Outcome == WorkspaceOperationOutcome.Missing,
        "A post-adoption mutation revived another owner partition.");
    var snapshot = cold.ReadContinuation(CreationFixture.AccountA, id).Value!;
    const int maximumBytes = 8 * 1024 * 1024;
    byte[] bytes = WorkspaceContinuationCodec.Encode(new(snapshot,
        WorkspaceContinuationSnapshotDigest.Compute(snapshot)), maximumBytes);
    using var otherDevice = new CreationFixture(root, CreationFixture.AccountA);
    var restore = new WorkspaceContinuationRestoreService(otherDevice.Store, otherDevice.Authority,
        otherDevice.Provider.GetRequiredService<ICharacterSourceDataResolver>(),
        otherDevice.Provider.GetRequiredService<ICharacterFileQueries>(),
        otherDevice.Provider.GetRequiredService<ILifeModulesCatalogService>(), maximumBytes);
    using var restoreReview = restore.Review(otherDevice.Authority.Capture(), bytes);
    var restored = restore.Confirm(restoreReview, explicitlyConfirmed: true);
    Check.That(restored.Outcome == WorkspaceContinuationRestoreOutcome.Applied,
        "The adopted runner's complete continuation cannot restore: " + restored.Outcome
        + "; " + string.Join(",", restored.Blockers ?? []));
    var imported = otherDevice.Store.Get(CreationFixture.AccountA, id).Value!;
    Check.That(imported.LocalHistory is { LocalAdoption: null, LastRestore: not null }
        && !imported.CanReplayReceipt(committed.ContentRevision),
        "Portable restore copied local adoption or granted historical mutation replay.");
    var importedOrigin = otherDevice.Provider.GetRequiredService<IOwnerBoundLifeModuleOriginService>();
    Check.That(importedOrigin.Start(otherDevice.Authority.Capture(), id.Value).Value is not null,
        "The adopted runner cannot resume after a legitimate same-account device restore.");
    Check.That(importedOrigin.Confirm(otherDevice.Authority.Capture(), prepared.Value,
        prepared.Value.PendingPreview.PreviewDigest, decisionKey, explicitlyConfirmed: true).Value is null,
        "Same-account restore laundered an imported decision into a local replay.");
    Console.WriteLine("PASS exact retained native Life Modules history adopted and cold-reopened unchanged");
    Console.WriteLine("PASS adopted native runner prepares, confirms once and cold-reopens its next Life Module");
    Console.WriteLine("PASS same-account device restore preserves lineage without importing replay authority");
}

CreationFixture Fixture(out CharacterWorkspaceId id)
{
    var fixture = new CreationFixture(root, OwnerScope.LocalSingleUser);
    var request = CreationFixture.Request() with
    {
        BuildMethod = CharacterCreationBuildMethods.LifeModules,
        SettingsProfileId = CharacterCreationBootstrapProfiles.LifeModulesSettingsProfileId
    };
    var created = fixture.Provider.GetRequiredService<IOwnerBoundCharacterCreationBootstrapService>()
        .Create(fixture.Authority.Capture(), request);
    Check.That(created.Value is not null, "Real Life Modules bootstrap failed: " + created.Outcome);
    id = created.Value!.WorkspaceId;
    fixture.Authority.Transition(CreationFixture.AccountA);
    return fixture;
}

WorkspaceLocalAdoptionService Service(CreationFixture f, FileWorkspaceStore? store = null,
    TimeProvider? time = null) => new(store ?? f.Store, f.Authority, time);

WorkspaceLocalAdoptionReview Review(WorkspaceLocalAdoptionService service, CreationFixture f, CharacterWorkspaceId id)
{
    var review = service.Review(f.Authority.Capture(), id);
    Check.That(review is not null, "Expected a review of the actual unclaimed local runner.");
    return review!;
}

void CompleteHandoff()
{
    using var f = Fixture(out var id);
    var before = f.Store.Get(id).Value!;
    var service = Service(f);
    var review = Review(service, f, id);
    var applied = service.Confirm(f.Authority.Capture(), review, true);
    Check.That(applied.Outcome == WorkspaceLocalAdoptionOutcome.Applied, "Handoff did not commit: " + applied.Outcome);
    var cold = new FileWorkspaceStore(f.StateDirectory);
    Check.That(cold.Get(id).Outcome == WorkspaceOperationOutcome.Missing, "Local copy remained adoptable.");
    Check.That(cold.Get(CreationFixture.AccountB, id).Outcome == WorkspaceOperationOutcome.Missing, "Foreign account read the runner.");
    var after = cold.Get(CreationFixture.AccountA, id).Value!;
    Check.That(after is not null && JsonSerializer.Serialize(before.Document) == JsonSerializer.Serialize(after.Document),
        "XML, auxiliary history or draft state changed during adoption.");
    Check.That(before.ContentRevision == after!.ContentRevision && before.SavedRevision == after.SavedRevision
        && before.LastUpdatedUtc == after.LastUpdatedUtc && before.LocalHistory!.IncarnationId == after.LocalHistory!.IncarnationId,
        "Adoption invented a rules edit or changed the original incarnation/checkpoint.");
    Check.That(cold.ReadContinuation(CreationFixture.AccountA, id).Success, "Account cannot export complete adopted state.");
    Check.That(Service(f, cold).Recover(f.Authority.Capture(), id, review.OperationId).Outcome
        == WorkspaceLocalAdoptionOutcome.Recovered, "Cold recovery did not recognize actual receipt.");
    Check.That(service.Confirm(f.Authority.Capture(), review, true).Outcome == WorkspaceLocalAdoptionOutcome.Rejected,
        "Consumed review was admitted a second time.");
}

void Confirmation()
{
    using var f = Fixture(out var id);
    var service = Service(f); var review = Review(service, f, id);
    Check.That(service.Confirm(f.Authority.Capture(), review, false).Outcome == WorkspaceLocalAdoptionOutcome.Rejected,
        "No confirmation claimed data.");
    Check.That(Service(f).Confirm(f.Authority.Capture(), review, true).Outcome == WorkspaceLocalAdoptionOutcome.Rejected,
        "Foreign issuer admitted review.");
    Check.That(f.Store.Get(id).Success, "Rejection changed local runner.");
}

void InitialCheckpoint()
{
    using var f = Fixture(out var id);
    var origin = f.Provider.GetRequiredService<IOwnerBoundLifeModuleOriginService>();
    f.Authority.Transition(OwnerScope.LocalSingleUser);
    var initial = origin.Start(f.Authority.Capture(), id.Value);
    Check.That(initial.Value is not null, "Initial local Origin checkpoint is unavailable.");
    var choice = initial.Value!.Projection.CurrentTurn.LegalChoices.First(item => item.IsLegal && item.Blockers.Count == 0);
    var answers = choice.FollowUps?.ToDictionary(prompt => prompt.PromptId,
        prompt => prompt.Options.FirstOrDefault(option => option.IsEnabled)?.SourceValue ?? "Denver");
    initial = origin.Prepare(f.Authority.Capture(), initial.Value, choice.ChoiceId, answers);
    Check.That(initial.Value?.PendingPreview is not null, "Initial local choice preview is unavailable.");
    string projection = JsonSerializer.Serialize(initial.Value!.Projection);
    f.Authority.Transition(CreationFixture.AccountA);
    Check.That(origin.AdoptLocalCheckpoint(f.Authority.Capture(), initial.Value).Value is null,
        "A checkpoint alone granted account custody before the runner claim.");
    var service = Service(f);
    var review = Review(service, f, id);
    Check.That(service.Confirm(f.Authority.Capture(), review, true).Outcome == WorkspaceLocalAdoptionOutcome.Applied,
        "Initial runner adoption failed.");
    var adopted = origin.AdoptLocalCheckpoint(f.Authority.Capture(), initial.Value);
    Check.That(adopted.Value is not null && adopted.Value.OwnerId == CreationFixture.AccountA.NormalizedValue,
        "Initial local checkpoint could not be adopted before the first accepted module.");
    Check.That(JsonSerializer.Serialize(adopted.Value!.Projection) == projection,
        "The initial narrative projection changed during adoption.");
    var coldStore = new FileWorkspaceStore(f.StateDirectory);
    var coldOrigin = f.AddProvider(coldStore, f.Authority).GetRequiredService<IOwnerBoundLifeModuleOriginService>();
    Check.That(coldOrigin.Restore(f.Authority.Capture(), adopted.Value).Value is not null,
        "The adopted initial checkpoint did not survive cold reopen.");
    Check.That(coldOrigin.Restore(f.Authority.Capture(), initial.Value).Value is null,
        "A local initial checkpoint became an account command without explicit rebinding.");
    Check.That(adopted.Value.PendingPreview?.PreviewDigest == initial.Value.PendingPreview!.PreviewDigest,
        "The initial reviewed choice changed during adoption.");
    var accepted = coldOrigin.Confirm(f.Authority.Capture(), adopted.Value,
        adopted.Value.PendingPreview!.PreviewDigest, "initial-adoption-choice", true);
    Check.That(accepted.Value is not null,
        "The first reviewed choice cannot be confirmed after adoption: " + string.Join(",", accepted.Blockers));
    Check.That(coldStore.Get(CreationFixture.AccountA, id).Value!.ContentRevision == 2,
        "Initial adopted choice did not commit exactly once.");
}

void OwnerAba()
{
    using var f = Fixture(out var id); var service = Service(f); var stamp = f.Authority.Capture();
    var review = Review(service, f, id);
    f.Authority.Transition(CreationFixture.AccountB); f.Authority.Transition(CreationFixture.AccountA);
    Check.That(service.Confirm(stamp, review, true).Outcome == WorkspaceLocalAdoptionOutcome.Rejected
        && service.Confirm(f.Authority.Capture(), review, true).Outcome == WorkspaceLocalAdoptionOutcome.Rejected,
        "An account transition revived an old review.");
    Check.That(f.Store.Get(id).Success, "ABA mutated local data.");
}

void ChangedSource()
{
    using var f = Fixture(out var id); var service = Service(f); var review = Review(service, f, id);
    var source = f.Store.Get(id).Value!;
    Check.That(f.Store.SaveCheckpoint(id, source.ContentRevision).Success, "Checkpoint fixture failed.");
    Check.That(service.Confirm(f.Authority.Capture(), review, true).Outcome == WorkspaceLocalAdoptionOutcome.Conflict,
        "Changed checkpoint admitted stale handoff.");
    Check.That(f.Store.Get(id).Value!.SavedRevision == source.ContentRevision, "Checkpoint lost.");
}

void ExistingTarget()
{
    using var f = Fixture(out var id); var service = Service(f); var review = Review(service, f, id);
    Check.That(f.Store.CreateWorkspaceDocument(CreationFixture.AccountA, id, CreationFixture.ContactDocument()).Success,
        "Target collision fixture failed.");
    string target = f.Snapshot(CreationFixture.AccountA, id);
    Check.That(service.Confirm(f.Authority.Capture(), review, true).Outcome == WorkspaceLocalAdoptionOutcome.Conflict,
        "Account data overwritten.");
    Check.That(target == f.Snapshot(CreationFixture.AccountA, id) && f.Store.Get(id).Success, "Collision changed data.");
}

void Canceled()
{
    using var f = Fixture(out var id); var service = Service(f); var review = Review(service, f, id);
    using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
    Check.That(service.Confirm(f.Authority.Capture(), review, true, cancellation.Token).Outcome
        == WorkspaceLocalAdoptionOutcome.Canceled && f.Store.Get(id).Success, "Precommit cancellation changed ownership.");
}

void Expired()
{
    using var f = Fixture(out var id); var time = new Clock(); var service = Service(f, time: time);
    var review = Review(service, f, id); time.Now += TimeSpan.FromMinutes(6);
    Check.That(service.Confirm(f.Authority.Capture(), review, true).Outcome == WorkspaceLocalAdoptionOutcome.Rejected
        && f.Store.Get(id).Success, "Expired review changed ownership.");
}

void Interrupted()
{
    using var f = Fixture(out var id);
    string before = JsonSerializer.Serialize(f.Store.Get(id).Value!.Document);
    var faulty = new FileWorkspaceStore(f.StateDirectory, new Fault(FileWorkspaceStoreFaultStage.AfterLocalAdoptionClaimed));
    var service = Service(f, faulty); var review = Review(service, f, id);
    Check.That(service.Confirm(f.Authority.Capture(), review, true).Outcome == WorkspaceLocalAdoptionOutcome.RecoveryRequired,
        "Interrupted placement was presented as uncommitted/retryable.");
    var cold = new FileWorkspaceStore(f.StateDirectory);
    Check.That(cold.Get(id).Outcome == WorkspaceOperationOutcome.Missing && !cold.List().Any(x => x.Id == id)
        && !cold.ReadContinuation(id).Success, "Claimed local data became available again.");
    f.Authority.Transition(CreationFixture.AccountB);
    Check.That(Service(f, cold).Recover(f.Authority.Capture(), id, review.OperationId).Outcome
        == WorkspaceLocalAdoptionOutcome.Rejected, "Foreign account recovered the claim.");
    Check.That(Service(f, cold).Review(f.Authority.Capture(), id) is null, "Foreign account reviewed claimed source.");
    f.Authority.Transition(CreationFixture.AccountA);
    Check.That(Service(f, cold).Recover(f.Authority.Capture(), id, Guid.NewGuid()).Outcome
        == WorkspaceLocalAdoptionOutcome.Rejected, "Unrelated operation recovered the claim.");
    Check.That(Service(f, cold).Recover(f.Authority.Capture(), id, review.OperationId).Outcome
        == WorkspaceLocalAdoptionOutcome.Recovered, "Actual pending claim could not finish after restart.");
    Check.That(before == JsonSerializer.Serialize(cold.Get(CreationFixture.AccountA, id).Value!.Document), "Recovered history changed.");
}

void BeforeClaimFailure()
{
    using var f = Fixture(out var id); string before = f.Snapshot(OwnerScope.LocalSingleUser, id);
    var faulty = new FileWorkspaceStore(f.StateDirectory, new Fault(FileWorkspaceStoreFaultStage.AfterTempFileFlushed));
    var service = Service(f, faulty); var review = Review(service, f, id);
    Check.That(service.Confirm(f.Authority.Capture(), review, true).Outcome == WorkspaceLocalAdoptionOutcome.Unavailable,
        "Failed claim falsely reported committed.");
    Check.That(before == f.Snapshot(OwnerScope.LocalSingleUser, id), "Preclaim I/O failure changed local data.");
}

void AfterMoveFailure()
{
    using var f = Fixture(out var id);
    var faulty = new FileWorkspaceStore(f.StateDirectory, new Fault(FileWorkspaceStoreFaultStage.AfterLocalAdoptionMoved));
    var service = Service(f, faulty); var review = Review(service, f, id);
    Check.That(service.Confirm(f.Authority.Capture(), review, true).Outcome == WorkspaceLocalAdoptionOutcome.Applied
        && f.Store.Get(CreationFixture.AccountA, id).Success, "Post-commit observer failure caused an ambiguous retry.");
}

sealed class Fault(FileWorkspaceStoreFaultStage stage) : IFileWorkspaceStoreFaultInjector
{
    public void OnStage(FileWorkspaceStoreFaultStage observed, string path, string temporaryPath)
    { if (observed == stage) throw new IOException("Synthetic owned-store interruption."); }
}
sealed class Clock : TimeProvider
{
    public DateTimeOffset Now { get; set; } = new(2026, 10, 5, 9, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => Now;
}

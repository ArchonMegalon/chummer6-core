#nullable enable annotations

using System;
using System.Threading;
using System.Threading.Tasks;
using Chummer.Contracts.Presentation;
using Chummer.Contracts.Workspaces;
using Chummer.Presentation.Overview;

namespace Chummer.Tests.Presentation;

internal sealed class FakeCharacterOverviewPresenter : ICharacterOverviewPresenter
{
    public CharacterOverviewState State { get; private set; } = CharacterOverviewState.Empty;

    public event EventHandler? StateChanged;

    public CharacterWorkspaceId? LoadedWorkspaceId { get; private set; }

    public CharacterWorkspaceId? SwitchedWorkspaceId { get; private set; }

    public CharacterWorkspaceId? ClosedWorkspaceId { get; private set; }

    public string? ImportedContent { get; private set; }

    public string? ImportedRulesetId { get; private set; }

    public UpdateWorkspaceMetadata? UpdatedMetadata { get; private set; }

    public string? ExecutedCommandId { get; private set; }

    public string? SelectedTabId { get; private set; }

    public string? HandledUiControlId { get; private set; }

    public string? ExecutedWorkspaceActionId { get; private set; }

    public string? ExecutedDialogActionId { get; private set; }

    public string? UpdatedDialogFieldId { get; private set; }

    public string? UpdatedDialogFieldValue { get; private set; }

    public int CloseDialogCalls { get; private set; }

    public int SaveCalls { get; private set; }

    public int InitializeCalls { get; private set; }

    public Task InitializeAsync(CancellationToken ct)
    {
        InitializeCalls++;
        return Task.CompletedTask;
    }

    public Task ImportAsync(WorkspaceImportDocument document, CancellationToken ct)
    {
        ImportedContent = document.Content;
        ImportedRulesetId = document.RulesetId;
        return Task.CompletedTask;
    }

    public Task LoadAsync(CharacterWorkspaceId id, CancellationToken ct)
    {
        LoadedWorkspaceId = id;
        return Task.CompletedTask;
    }

    public Task SwitchWorkspaceAsync(CharacterWorkspaceId id, CancellationToken ct)
    {
        SwitchedWorkspaceId = id;
        return Task.CompletedTask;
    }

    public Task CloseWorkspaceAsync(CharacterWorkspaceId id, CancellationToken ct)
    {
        ClosedWorkspaceId = id;
        return Task.CompletedTask;
    }

    public Task ExecuteCommandAsync(string commandId, CancellationToken ct)
    {
        ExecutedCommandId = commandId;
        return Task.CompletedTask;
    }

    public Task SelectTabAsync(string tabId, CancellationToken ct)
    {
        SelectedTabId = tabId;
        return Task.CompletedTask;
    }

    public Task HandleUiControlAsync(string controlId, CancellationToken ct)
    {
        HandledUiControlId = controlId;
        return Task.CompletedTask;
    }

    public Task ExecuteWorkspaceActionAsync(WorkspaceSurfaceActionDefinition action, CancellationToken ct)
    {
        ExecutedWorkspaceActionId = action.Id;
        return Task.CompletedTask;
    }

    public Task UpdateDialogFieldAsync(string fieldId, string? value, CancellationToken ct)
    {
        UpdatedDialogFieldId = fieldId;
        UpdatedDialogFieldValue = value;
        return Task.CompletedTask;
    }

    public Task ExecuteDialogActionAsync(string actionId, CancellationToken ct)
    {
        ExecutedDialogActionId = actionId;
        return Task.CompletedTask;
    }

    public Task CloseDialogAsync(CancellationToken ct)
    {
        CloseDialogCalls++;
        return Task.CompletedTask;
    }

    public Task UpdateMetadataAsync(UpdateWorkspaceMetadata command, CancellationToken ct)
    {
        UpdatedMetadata = command;
        return Task.CompletedTask;
    }

    public Task SaveAsync(CancellationToken ct)
    {
        SaveCalls++;
        return Task.CompletedTask;
    }

    public void Publish(CharacterOverviewState state)
    {
        State = state;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }
}

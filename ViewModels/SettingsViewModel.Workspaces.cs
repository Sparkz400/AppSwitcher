namespace AppSwitcher.ViewModels;

public partial class SettingsViewModel
{
    /// <summary>Workspaces with their resolved members; rebuilt when layouts change or the Layouts section opens.</summary>
    public ObservableCollection<WorkspaceCard> WorkspaceCards { get; } = [];

    private IReadOnlyList<WorkspaceChoice> WorkspaceChoices => WorkspaceEditor.Choices(Apps, Groups);

    private void RefreshWorkspaceCards()
    {
        var choices = WorkspaceChoices;
        WorkspaceCards.Clear();
        foreach (var layout in Layouts) WorkspaceCards.Add(WorkspaceEditor.Card(layout, Apps, choices));
    }

    // Layout members are edited in place (like app/group deletion does); the config snapshot drives Save and Undo.
    private void CommitWorkspaceEdit()
    {
        RefreshWorkspaceCards();
        RefreshSaveState();
    }

    [RelayCommand]
    private void RemoveWorkspaceMember(WorkspaceMember? member)
    {
        if (!CanEditApps || member == null) return;
        WorkspaceEditor.Remove(member);
        CommitWorkspaceEdit();
    }

    [RelayCommand]
    private void ReplaceWorkspaceMember(WorkspaceMember? member)
    {
        if (!CanEditApps || member?.Replacement == null) return;
        WorkspaceEditor.Replace(member, member.Replacement);
        CommitWorkspaceEdit();
    }

    [RelayCommand]
    private void AddWorkspaceMember(WorkspaceCard? card)
    {
        if (!CanEditApps || card?.Addition == null) return;
        WorkspaceEditor.Add(card.Layout, card.Addition);
        CommitWorkspaceEdit();
    }
}

using CommunityToolkit.Mvvm.ComponentModel;

namespace Desktop.ViewModels;

public sealed partial class UserNodeSelectionModel : ObservableObject
{
    public UserNodeSelectionModel(UiNode[] all, UiNode? selected = null)
    {
        AllNodes = all;
        SelectedUiNode = selected ?? UiNode.Null;
    }

    [ObservableProperty]
    public partial UiNode[] AllNodes { get; private set; }

    [ObservableProperty]
    public partial UiNode SelectedUiNode { get; set; }
}

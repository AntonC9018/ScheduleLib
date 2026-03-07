using System.Diagnostics;
using Anton.LayeredData;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ScheduleLib.Application.Config;
using ScheduleLib.Helper.Parsing;
using ScheduleLib.Parsing;

namespace Desktop.ViewModels;

public sealed partial class AddUserViewModel : ViewModelBase, IDisposable
{
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddUserWithTypedNameCommand))]
    public partial string UserNameToAdd { get; set; } = "";

    private readonly TreeBuilder _tree;
    private readonly UpdateTreeHelper _updateTreeHelper;
    private readonly EventSubscription _subTreeChanged;
    private readonly EventSubscription<LayerLevel> _layerChangedSub;
    private readonly LayerLevelSelectionViewModel _layerSelection;

    public AddUserViewModel(
        TreeContext t,
        UpdateTreeHelper updateTreeHelper,
        Event treeStructureChanged,
        LayerLevelSelectionViewModel layerSelection)
        : base(t.Dispatcher)
    {
        _tree = t.Tree;
        _updateTreeHelper = updateTreeHelper;
        _layerSelection = layerSelection;
        _subTreeChanged = treeStructureChanged.Sub(() =>
        {
            AddUserWithTypedNameCommand.NotifyCanExecuteChanged();
        });
        _layerChangedSub = _layerSelection.LayerLevelChanged.Sub(level =>
        {
            _ = level;
        });
    }

    public bool CanSelectUserToAdd => true;

    private Name? ParseUserNameToAdd()
    {
        var parser = new Parser(UserNameToAdd);
        Name? name = NameHelper.TryParseName(ref parser);
        return name;
    }

    public bool CanAddUser
    {
        get
        {
            if (ParseUserNameToAdd() is not { } name)
            {
                return false;
            }
            if (_tree.GetAllMarkers().Any(x => x.TeacherName == name))
            {
                return false;
            }
            return true;
        }
    }

    [RelayCommand(CanExecute = nameof(CanAddUser))]
    public void AddUserWithTypedName()
    {
        var name = ParseUserNameToAdd();
        if (name is null)
        {
            Debug.Fail("Parsed name was null");
            return;
        }
        AddUser(name);
    }

    private void AddUser(Name name)
    {
        _updateTreeHelper.ExecTreeAction(() =>
        {
            var layer = _tree.Defaults.CreateUiLayer();
            var val = layer.Builder<TeacherLayerConfig>().Value();
            val.TeacherName = name;
            return new([_tree.BaseNode, layer.Node]);
        });
        _layerSelection.LayerLevel = LayerLevel.UiUser;
        UserNameToAdd = "";
    }

    public void Dispose()
    {
        _subTreeChanged.Dispose();
        _layerChangedSub.Dispose();
    }
}

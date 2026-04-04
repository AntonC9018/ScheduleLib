using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Diagnostics;
using Anton.LayeredData;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Desktop.MvvmEssentials;
using Desktop.ViewModelData;
using ScheduleLib;
using ScheduleLib.Application.Config;
using ScheduleLib.Helper.Parsing;
using ScheduleLib.Parsing;

namespace Desktop.MainWindow;

public sealed partial class AddUserViewModel : ViewModelBase, IDisposable
{
    [ObservableProperty]
    public partial string UserNameToAdd { get; set; } = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddUserWithTypedNameCommand))]
    public partial NameAndScore? SelectedUserName { get; set; } = null;

    private readonly TreeBuilder _tree;
    private readonly UpdateTreeHelper _updateTreeHelper;
    private readonly EventSubscription _subTreeChanged;
    private readonly EventSubscription<LayerLevel> _layerChangedSub;
    private readonly LayerLevelSelectionViewModel _layerSelection;
    private readonly IAllTeacherNamesProvider _teacherNamesProvider;

    private ItemOwner<(NameParser, List<NameParts<string?>>)> _item = new((new(), new()));

    public ObservableCollection<NameAndScore> FilteredUserNames { get; }

    private ImmutableArray<Name> AllUserNames => _teacherNamesProvider.Names.Get();
    private readonly EventSubscription<ImmutableArray<Name>> _teacherNameSub;

    private readonly EventSource<Nothing> _userAddedEventSource;
    public Event UserAdded => _userAddedEventSource;

    public AddUserViewModel(
        TreeContext t,
        UpdateTreeHelper updateTreeHelper,
        Event treeStructureChanged,
        LayerLevelSelectionViewModel layerSelection,
        IAllTeacherNamesProvider teacherNamesProvider)
        : base(t.Dispatcher)
    {
        _userAddedEventSource = t.Dispatcher.CreateEvent();
        FilteredUserNames = new();
        _teacherNameSub = teacherNamesProvider.Names.Changed.Sub(names =>
        {
            _ = names;
            OnUserNameToAddChanged();
        });
        _teacherNamesProvider = teacherNamesProvider;
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
        OnUserNameToAddChanged();
    }

    public bool CanSelectUserToAdd => true;

    public bool CanAddUser
    {
        get
        {
            if (SelectedUserName is not { } it)
            {
                return false;
            }
            if (_tree.GetAllMarkers().Any(x => x.TeacherName == it.Name))
            {
                return false;
            }
            return true;
        }
    }

    [RelayCommand(CanExecute = nameof(CanAddUser))]
    public void AddUserWithTypedName()
    {
        var it = SelectedUserName;
        if (it is null)
        {
            Debug.Fail("Selected name was null");
            return;
        }
        AddUser(it.Name);
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
        _userAddedEventSource.Invoke();
    }

    private void OnUserNameToAddChanged()
    {
        using var x = _item.BorrowHelper();
        var (nameParser, tempArr) = x.Value;
        nameParser.Load(UserNameToAdd.AsMemory());
        tempArr.Clear();

        Parse(nameParser, tempArr);
        UpdateFilteredList();
        return;

        void UpdateFilteredList()
        {
            IEnumerable<Name> matches = AllUserNames;
            FilteredUserNames.Clear();
            if (tempArr.Count != 0)
            {
                var matchesWithScores = matches
                    .Select(x =>
                    {
                        var scoreData = MatchScore.Create(x, tempArr);
                        var scoreInt = scoreData.AsInt();
                        return (Name: x, Score: scoreData, ScoreInt: scoreInt);
                    })
                    .Where(x => x.ScoreInt > 0)
                    .OrderByDescending(x => x.ScoreInt)
                    .ThenBy(x => x.Name, NameAlphabeticComparer.CurrentCultureIgnoreCase);
                foreach (var m in matchesWithScores)
                {
                    // TODO: Try reusing the objects.
                    FilteredUserNames.Add(new()
                    {
                        Name = m.Name,
                        Score = m.Score,
                        ScoreString = m.ScoreInt.ToString(),
                    });
                }
            }
        }
    }

    partial void OnUserNameToAddChanged(string value)
    {
        _ = value;
        Debug.Assert(value == UserNameToAdd);
        OnUserNameToAddChanged();
    }

    public void Dispose()
    {
        _subTreeChanged.Dispose();
        _layerChangedSub.Dispose();
        _teacherNameSub.Dispose();
    }

    internal static void Parse(
        NameParser nameParser,
        List<NameParts<string?>> output)
    {
        var lexer = nameParser.Scope();
        while (!lexer.IsEmpty)
        {
            var namePart = NameHelper.ParseNamePart(ref lexer, out bool isDoubleNameError);
            _ = isDoubleNameError;

            lexer.ConsumeAllConsecutiveThatAreNot(NameTokenType.Word);

            if (namePart == default)
            {
                continue;
            }
            output.Add(namePart);
        }
    }
}

public sealed record class NameAndScore
{
    public required Name Name { get; init; }
    public required MatchScore Score { get; init; }
    public required string ScoreString { get; init; }
    public override string ToString() => Name.ToString();
}

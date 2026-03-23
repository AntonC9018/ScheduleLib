using System.Collections.Immutable;
using System.Diagnostics;
using Anton.LayeredData;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Desktop.MvvmEssentials;
using Desktop.ViewModelData;
using ScheduleLib;
using ScheduleLib.Application.Config;
using ScheduleLib.Application.Core;
using ScheduleLib.Helper;
using ScheduleLib.Helper.Parsing;
using ScheduleLib.Parsing;

namespace Desktop.MainWindow;

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
    private readonly AllTeacherNamesProvider _teacherNamesProvider;

    public bool AutoCompleteName(string? search, Name item)
    {
        if (search is null)
        {
            return false;
        }

        var parts = search.Split(" ");
        var found = new EnumBitArray<NamePart>();
        foreach (var p in parts)
        {
            if (item.FirstName.
        }

        IgnoreDiacriticsAndCaseComparer.Instance.StartsWith(item.
        if
    }

    public IEnumerable<Name> AvailableUserNames => _teacherNamesProvider.Names;

    public AddUserViewModel(
        TreeContext t,
        UpdateTreeHelper updateTreeHelper,
        Event treeStructureChanged,
        LayerLevelSelectionViewModel layerSelection,
        AllTeacherNamesProvider teacherNamesProvider)
        : base(t.Dispatcher)
    {
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

public sealed partial class AllTeacherNamesProvider
{
    public ImmutableArray<Name> Names { get; }

    public AllTeacherNamesProvider(ScheduleProvider scheduleProvider)
    {
        // Known currently ignored issues:
        // 1. constructor may run slowly causing work
        // 2. it assumes the schedule has been loaded already
        // 3. it doesn't track changes to the schedule
        // 4. it currently makes an extra copy of the schedule just to get the names
        var schedule = scheduleProvider.Get();
        Names = schedule
            .EnumerateTeachers()
            .Select(x =>
            {
                var name = x.Item.PersonName.AsNameFields();
                return new Name(name);
            })
            .ToImmutableArray();
    }
}

public enum NamePart
{
    First,
    Last,
    Count,
}

// file readonly ref struct SplitEnumerable
// {
//     private readonly ReadOnlySpan<char> _source;
//     private readonly ReadOnlySpan<char> _splitChar;
//
//     public SplitEnumerable(
//         ReadOnlySpan<char> source,
//         ReadOnlySpan<char> splitChar)
//     {
//         _source = source;
//         _splitChar = splitChar;
//     }
//
//     public MemoryExtensions.SpanSplitEnumerator<char> GetEnumerator() => _source.Split(_splitChar);
//
//     public ref struct SplitEnumerator
//     {
//         private MemoryExtensions.SpanSplitEnumerator<char> _impl;
//         public ReadOnlySpan<char>
//         public SplitEnumerator(SplitEnumerable e) => _e = e;
//     }
//
//     public int Count
//     {
//         get
//         {
//             using var e = GetEnumerator();
//             int i = 0;
//             while (e.MoveNext())
//             {
//                 if (e.Current
//             }
//         }
//     }
// }
//

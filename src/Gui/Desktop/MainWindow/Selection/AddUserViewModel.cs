using System.Collections;
using System.Collections.Immutable;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using Anton.LayeredData;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Desktop.MvvmEssentials;
using Desktop.ViewModelData;
using Microsoft.Extensions.DependencyInjection;
using ScheduleLib;
using ScheduleLib.Application.Config;
using ScheduleLib.Application.Core;
using ScheduleLib.Helper;
using ScheduleLib.Helper.Parsing;
using ScheduleLib.Parsing;
using IDispatcher = Desktop.MvvmEssentials.IDispatcher;

namespace Desktop.MainWindow;

public sealed partial class AddUserViewModel : ViewModelBase, IDisposable
{
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddUserWithTypedNameCommand))]
    public partial string UserNameToAdd { get; set; } = "";

    private readonly TreeBuilder _tree;
    private new readonly TreeEventDispatcher _dispatcher;
    private readonly UpdateTreeHelper _updateTreeHelper;
    private readonly EventSubscription _subTreeChanged;
    private readonly EventSubscription<LayerLevel> _layerChangedSub;
    private readonly LayerLevelSelectionViewModel _layerSelection;
    private readonly AllTeacherNamesProvider _teacherNamesProvider;

    private ItemOwner<(NameParser, List<NameParts<string?>>)> _item = new((new(), new()));
    public UpdateableObservableList<Name> FilteredUserNames { get; }
    private ImmutableArray<Name> AllUserNames => _teacherNamesProvider.Names.Get();
    private readonly EventSubscription<ImmutableArray<Name>> _teacherNameSub;

    public AddUserViewModel(
        TreeContext t,
        UpdateTreeHelper updateTreeHelper,
        Event treeStructureChanged,
        LayerLevelSelectionViewModel layerSelection,
        AllTeacherNamesProvider teacherNamesProvider)
        : base(t.Dispatcher)
    {
        FilteredUserNames = new(t.Dispatcher);
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
        _dispatcher = t.Dispatcher;
        OnUserNameToAddChanged();
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

    private void OnUserNameToAddChanged()
    {
        using var x = _item.BorrowHelper();
        var (nameParser, tempArr) = x.Value;
        tempArr.Clear();

        _dispatcher.StartQueueing();
        try
        {
            Parse();
            UpdateFilteredList();
        }
        finally
        {
            _dispatcher.EndQueueing();
        }

        return;

        void UpdateFilteredList()
        {
            IEnumerable<Name> matches = AllUserNames;
            if (tempArr.Count != 0)
            {
                matches = matches
                    .Select(x => (Name: x, Score: GetMatchScore(x)))
                    .Where(x => x.Score != 0)
                    .OrderByDescending(x => x.Score)
                    .ThenBy(x => x.Name, NameAlphabeticComparer.CurrentCultureIgnoreCase)
                    .Select(x => x.Name);
            }
            FilteredUserNames.Reset(matches);
        }

        void Parse()
        {
            nameParser.Load(UserNameToAdd.AsMemory());
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
                tempArr.Add(namePart);
            }
        }

        int GetMatchScore(Name name)
        {
            var nameFields = name.Fields;
            var foundFields = new EnumBitArray<NameField>();

            int Ret(bool hasExtra)
            {
                var matchedCount = foundFields.SetCount;
                var allMatched = foundFields.AreAllSet;
                var someUnmatched = hasExtra;

                if (matchedCount == 0)
                {
                    return 0;
                }

                if (allMatched && someUnmatched)
                {
                    return 2;
                }

                if (someUnmatched)
                {
                    return 1;
                }

                return matchedCount + 2;
            }

            foreach (var namePart in tempArr)
            {
                // Everything matched, but found even more input.
                if (foundFields.AreAllSet)
                {
                    return Ret(hasExtra: true);
                }

                foreach (var notFoundField in foundFields.Flipped)
                {
                    var field = NameHelper.Field(nameFields, notFoundField);

                    bool matches = namePart.EachEquals(field, (parsed, existing) =>
                    {
                        if (parsed is null)
                        {
                            return true;
                        }
                        if (/*parsed is not null &&*/ existing is null)
                        {
                            return false;
                        }
                        if (IgnoreDiacriticsAndCaseComparer.Instance.StartsWith(existing, parsed))
                        {
                            return true;
                        }
                        return false;
                    });
                    if (matches)
                    {
                        foundFields.Set(notFoundField);
                        break;
                    }
                }
            }

            // Something matching is considered a match?
            return Ret(hasExtra: false);
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
}

public sealed class ScheduleLoading
{
    private ObservableValueSource<Schedule> _names;
    public ObservableValue<Schedule> Names => _names.As();

    public ScheduleLoading(IDispatcher dispatcher)
    {
        _names = dispatcher.CreateObservableValue(Schedule.Empty);
    }

    public async void StartLoading(
        IServiceProvider sp,
        CancellationToken cancellationToken)
    {
        // TODO: Do this better?
        try
        {
            await Task.Run(async () =>
            {
                await sp.InitializeSchedule(cancellationToken);
                Dispatcher.UIThread.Post(() =>
                {
                    _names.Value = sp.GetRequiredService<ScheduleProvider>().Get();
                });
            },
            cancellationToken);
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }
    }
}

public sealed class AllTeacherNamesProvider : IDisposable
{
    private ObservableValueSource<ImmutableArray<Name>> _names;
    public ObservableValue<ImmutableArray<Name>> Names => _names.As();
    private readonly EventSubscription<Schedule> _scheduleUpdatedSub;

    public AllTeacherNamesProvider(
        IDispatcher dispatcher,
        Event<Schedule> scheduleLoaded)
    {
        _names = dispatcher.CreateObservableValue<ImmutableArray<Name>>([]);
        _scheduleUpdatedSub = scheduleLoaded.Sub(s =>
        {
            var x = s
                .EnumerateTeachers()
                .Select(x =>
                {
                    var name = x.Item.PersonName.AsNameFields();
                    return new Name(name);
                });
            _names.Value = [.. x];
        });
    }

    public void Dispose()
    {
        _scheduleUpdatedSub.Dispose();
    }
}

public sealed class UpdateableObservableList<T> : ICollection<T>, INotifyCollectionChanged, INotifyPropertyChanged
{
    public event NotifyCollectionChangedEventHandler? CollectionChanged;
    public event PropertyChangedEventHandler? PropertyChanged;

    private readonly List<T> _items = new();
    // private readonly IDispatcher _dispatcher

    public UpdateableObservableList(IDispatcher dispatcher)
    {

    }

    public void Reset(IEnumerable<T> newItems)
    {
        _items.Clear();
        _items.AddRange(newItems);

        PropertyChanged?.Invoke(this, new(nameof(Count)));
        PropertyChanged?.Invoke(this, new("Item[]"));
        CollectionChanged?.Invoke(this, new(NotifyCollectionChangedAction.Reset));
    }

    public IEnumerator<T> GetEnumerator() => _items.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public void Add(T item) => throw new NotSupportedException();
    public void Clear() => throw new NotSupportedException();
    public bool Contains(T item) => _items.Contains(item);
    public void CopyTo(T[] array, int arrayIndex) => throw new NotSupportedException();
    public bool Remove(T item) => throw new NotSupportedException();
    public int Count => _items.Count;
    public bool IsReadOnly => false;
}

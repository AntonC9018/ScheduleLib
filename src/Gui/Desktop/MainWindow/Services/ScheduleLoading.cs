using System.Collections.Immutable;
using Avalonia.Threading;
using Desktop.MvvmEssentials;
using Microsoft.Extensions.DependencyInjection;
using ScheduleLib;
using ScheduleLib.Application.Core;
using ScheduleLib.Parsing;
using IDispatcher = Desktop.MvvmEssentials.IDispatcher;

namespace Desktop.MainWindow;

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

public interface IAllTeacherNamesProvider
{
    public ObservableValue<ImmutableArray<Name>> Names { get; }
}

public sealed class AllTeacherNamesProvider : IAllTeacherNamesProvider, IDisposable
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


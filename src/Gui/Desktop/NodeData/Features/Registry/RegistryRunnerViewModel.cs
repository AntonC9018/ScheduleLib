using AutoConstructor.Attributes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Desktop.ViewModelData;
using Microsoft.Extensions.DependencyInjection;
using ScheduleLib.Application.Config;
using ScheduleLib.Application.Core;

namespace Desktop.NodeData.Features.Registry;

[AutoConstructor]
public sealed partial class RegistryRunnerViewModel : ObservableObject
{
    private readonly CurrentUserServiceProvider _sp;

    [RelayCommand]
    public async Task Run(CancellationToken cancellationToken)
    {
        var handler = _sp.GetRequiredService<AddLessonsToOnlineRegistryForCurrentTeacherTaskHandler>();
        await handler.Execute(cancellationToken);
    }
}

public sealed partial class CurrentUserServiceProvider : IAsyncDisposable, IServiceProvider
{
    // Might want to recreate the scope before any operation, might want to cache.
    // I'm still not sure what's best to do.
    private AsyncServiceScope _scope;
    private readonly DataStore _data;
    private readonly IServiceProvider _rootSp;

    public CurrentUserServiceProvider(
        DataStore data,
        IServiceProvider rootSp)
    {
        _scope = default;
        _data = data;
        _rootSp = rootSp;
    }

    public object? GetService(Type serviceType)
    {
        // NOTE: Race conditions technically possible depending on use.
        var sp = _scope.ServiceProvider;
        if (sp is null)
        {
            throw new InvalidOperationException("The scope must be initialized before use.");
        }
        var ret = sp.GetService(serviceType);
        return ret;
    }

    public async Task CancelScope()
    {
        if (_scope.ServiceProvider is not null)
        {
            await _scope.DisposeAsync();
        }
    }

    public async Task ResetScope()
    {
        await CancelScope();

        var nodePath = _data.SelectedNodePath.NodePath.Get();
        var markerContainer = nodePath.Leaf.Get(TeacherLayerConfig.Key);
        if (!markerContainer.Exists)
        {
            throw new InvalidOperationException("Cannot reset scope when selected path doesn't contain marker node data.");
        }
        var value = markerContainer.Value.GetValue();
        if (value is null)
        {
            throw new InvalidOperationException("Expected the marker node to have the value directly.");
        }

        var scope = _rootSp.CreateMarkerScope(value);
        _scope = scope;
    }

    public async ValueTask DisposeAsync()
    {
        await CancelScope();
    }
}

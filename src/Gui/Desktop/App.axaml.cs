using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using Desktop.MainWindow;
using Desktop.MvvmEssentials;
using Desktop.NodeData.Editor;
using Desktop.NodeData.Features.Registry;
using Desktop.ViewModelData;
using Desktop.Views;
using Microsoft.Extensions.DependencyInjection;
using ScheduleLib.Application.Core;

namespace Desktop;

public sealed partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public static void AddViewModels(IServiceCollection services)
    {
        services.AddSingleton<TreeEventDispatcher>();
        services.AddSingleton<TreeContext>();
        services.AddSingleton<UiTreeSerializer>();
        services.AddSingleton<IUiTreeOutputProvider, UiTreeOutputProvider>();
        services.AddOptions<UiTreeSerializerSettings>().Configure(x =>
        {
            x.OpenAfterSave = true;
        });

        services.AddTransient<MainWindowViewModel>();

        NodeDataViewModelResolver.Register(services);
        RegistryConfigViewModel.Register(services);

        services.AddSingleton<AllTeacherNamesProvider>(sp =>
        {
            var l = sp.GetRequiredService<ScheduleLoading>();
            var dispatcher = sp.GetRequiredService<TreeEventDispatcher>();
            return new(dispatcher, l.Names.Changed);
        });
        services.AddSingleton<ScheduleLoading>(sp =>
        {
            var dispatcher = sp.GetRequiredService<TreeEventDispatcher>();
            return new(dispatcher);
        });
    }

    public override void OnFrameworkInitializationCompleted()
    {
        DisableAvaloniaDataAnnotationValidation();

        var services = new ServiceCollection();
        AppConfiguration.ConfigureServices(services);

        services.AddView<MainWindowView>();
        services.AddTransient<AddUserView>();

        services.AddTransient<NodeDataEditorView>();
        services.AddTransient<NodeDataVmHostView>();

        services.AddTransient<RegistryConfigView>();

        services.AddSingleton<ViewLocator>();

        AddViewModels(services);

        // services.ValidateViewsAreNotDisposable();
        var serviceProvider = AppConfiguration.BuildServiceProvider(services);
        AppConfiguration.ConfigureLayeredConfig(serviceProvider);

        DataTemplates.Insert(0, serviceProvider.GetRequiredService<ViewLocator>());

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
#pragma warning disable CA2000
            var cts = new CancellationTokenSource();
#pragma warning restore CA2000

            var loading = serviceProvider.GetRequiredService<ScheduleLoading>();
            loading.StartLoading(serviceProvider, cts.Token);

            desktop.ShutdownRequested += (_, _) =>
            {
                serviceProvider.Dispose();
                cts.Dispose();
            };

            var view = serviceProvider.GetRequiredView<MainWindowView>(setViewModel: true);
            desktop.MainWindow = view;
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void DisableAvaloniaDataAnnotationValidation()
    {
        var dataValidationPluginsToRemove =
            BindingPlugins.DataValidators.OfType<DataAnnotationsValidationPlugin>().ToArray();

        foreach (var plugin in dataValidationPluginsToRemove)
        {
            BindingPlugins.DataValidators.Remove(plugin);
        }
    }
}

public static class ServiceProviderExtensions
{
    extension (IServiceCollection services)
    {
        public void ValidateViewsAreNotDisposable()
        {
            var disposableType = typeof(IDisposable);
            var asyncDisposableType = typeof(IAsyncDisposable);

            var offending = services
                .Where(d =>
                {
                    if (!disposableType.IsAssignableFrom(d.ImplementationType) &&
                        !asyncDisposableType.IsAssignableFrom(d.ImplementationType))
                    {
                        return false;
                    }
                    return d.ImplementationType != null
                        && typeof(ViewModelBase).IsAssignableFrom(d.ImplementationType);
                })
                .Select(d => d.ImplementationType!.FullName)
                .ToList();

            if (offending.Count > 0)
            {
                var names = string.Join("\n  ", offending);
                throw new InvalidOperationException(
                    $"The following ViewModels implement a Dispose pattern, which is not allowed:\n  {names}");
            }
        }

        public void AddView<T>() where T : Control
        {
            services.AddTransient<T>();
            // var viewFactory = ActivatorUtilities.CreateFactory<T>([]);
            // var viewModelType = ViewAndViewModelConverter.TypeFromViewToViewModel(typeof(T));
            // Debug.Assert(viewModelType != null);
            //
            // services.AddTransient<T>(sp =>
            // {
            //     var ret = viewFactory(sp, []);
            //     var viewModel = sp.GetRequiredService(viewModelType);
            //     ret.DataContext = viewModel;
            //     return ret;
            // });
        }
    }
    extension (IServiceProvider sp)
    {
        public T GetRequiredView<T>(bool setViewModel = false) where T : Control
        {
            var view = sp.GetRequiredNonDisposable<T>();
            if (setViewModel)
            {
                sp.SetViewModelOnView(view);
            }
            return view;
        }

        public T GetRequiredViewModel<T>() where T : class => sp.GetRequiredNonDisposable<T>();
        public T GetRequiredNonDisposable<T>() where T : class
        {
            if (typeof(T).IsAssignableTo(typeof(IDisposable))
                || typeof(T).IsAssignableTo(typeof(IAsyncDisposable)))
            {
                throw new InvalidOperationException("Tried to instantiate a disposable service from the root scope");
            }
            var service = sp.GetRequiredService<T>();
            if (service is IDisposable or IAsyncDisposable)
            {
                throw new InvalidOperationException("We've got a memory leak!");
            }
            return service;
        }

        public ViewModelBase SetViewModelOnView(Control view)
        {
            var viewModelType = ViewAndViewModelConverter.TypeFromViewToViewModel(view.GetType());
            Debug.Assert(viewModelType != null);
            var viewModel = (ViewModelBase) sp.GetRequiredService(viewModelType);
            view.DataContext = viewModel;
            return viewModel;
        }
    }
}

using System.ComponentModel;
using Anton.LayeredData;
using AutoConstructor.Attributes;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using ScheduleLib.OnlineRegistry;
using ScheduleLib.Scraping.Common;
using ScheduleLib.Scraping.Common.Config;

namespace Desktop.ViewModels;

public sealed partial class RegistryConfigViewModel : ConfigViewModelBase<RegistryConfig>
{
    private readonly ConfigAccessor<RegistryConfig> _helper;
    public RegistryConfigViewModel(ConfigAccessor<RegistryConfig> helper)
    {
        _helper = helper;
    }

    public override void UpdateSelection(NodeDataBuilder<RegistryConfig> builder)
    {
        Credentials.SetBuilder(builder);
    }

    public ObservableCredentials<RegistryConfig> Credentials { get; } = new();

    public ExtraLessonInstanceAction[] ExtraLessonInstanceActions => [
        ExtraLessonInstanceAction.Delete,
        ExtraLessonInstanceAction.LeaveAlone];

    public bool? DryRun
    {
        get
        {
            if (_helper.Config?.CommandProcessingConfig is not { } c)
            {
                return null;
            }
            if (c.HasAnyDryRun(LessonEquationCommandTypes.All))
            {
                return true;
            }
            return false;
        }
        set
        {
            var v = _helper.Config;
            if (v == null)
            {
                throw new InvalidOperationException("Cannot set DryRun when no node selected");
            }
            CommandProcessingConfigBuilder b;
            if (v.CommandProcessingConfig is not { } c)
            {
                b = new CommandProcessingConfigBuilder();
                b.Log().SetAll();
                b.Process().SetAll();
            }
            else
            {
                b = c.Builder();
            }
            b.DryRun().SetAll(value ?? false);
            v.CommandProcessingConfig = b.Build();
        }
    }
}

public sealed class RegistryViewModelFactory : INodeDataViewModelFactory
{
    public NodeDataKey Key => RegistryConfig.Key.Value;

    public NodeDataViewModelResult Create(IServiceProvider sp, ISelectedUserNode selectedUserNode)
    {
        var accessor = ConfigAccessor.Create(selectedUserNode, RegistryConfig.Key);
#pragma warning disable CA2000 // Compiler thinks this leaks disposable
        var vm = ActivatorUtilities.CreateInstance<RegistryConfigViewModel>(sp, accessor);
        var host = new ConfigNodeVmHost<RegistryConfig>(accessor, vm);
#pragma warning restore CA2000
        return NodeDataViewModelResult.Create(host);
    }
}

public sealed class ObservableCredentials<T>
    where T : class, ICredentialsHolder
{
    private NodeDataBuilder<T> _builder;
    public void SetBuilder(NodeDataBuilder<T> builder) => _builder = builder;

    public string Login
    {
        get => Model()?.Login ?? "";
        set => Model()?.Login = value;
        // {
        //     var m = Model;
        //     SetProperty(m.Login, value, m, static (m, v) => m.Login = v);
        // }
    }

    public string Password
    {
        get => Model()?.Password ?? "";
        set => Model()?.Password = value;
        // {
        //     var m = Model;
        //     SetProperty(m.Password, value, m, static (m, v) => m.Password = v);
        // }
    }

    public Credentials? Model()
    {
        if (_builder.IsNull)
        {
            return null;
        }
        var source = _builder.Credentials().Value();
        var x = source.Value ??= new Credentials
        {
            Login = "",
            Password = "",
        };
        return x;
    }
}


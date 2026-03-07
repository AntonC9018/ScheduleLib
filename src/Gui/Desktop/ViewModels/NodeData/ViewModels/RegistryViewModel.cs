using Anton.LayeredData;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using ScheduleLib.OnlineRegistry;
using ScheduleLib.Scraping.Common;
using ScheduleLib.Scraping.Common.Config;

namespace Desktop.ViewModels;

public sealed class RegistryConfigViewModel : NodeDataViewModelBase<RegistryConfig>
{
    private readonly ConfigAccessor<RegistryConfig> _helper;
    public RegistryConfigViewModel(ConfigAccessor<RegistryConfig> helper)
    {
        _helper = helper;
    }

    public static void Register(IServiceCollection services)
    {
        services.AddVmFactory(
            RegistryConfig.Key,
            b => b.VM<RegistryConfigViewModel>().UseUpdateOnDataChange());
    }

    protected override void UpdateSelection(NodeDataBuilder<RegistryConfig> builder)
    {
        Credentials.SetBuilder(builder);
        OnPropertyChanged(nameof(DryRun));
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

public sealed class ObservableCredentials<T> : ObservableObject
    where T : class, ICredentialsHolder
{
    private NodeDataBuilder<T> _builder;
    public void SetBuilder(NodeDataBuilder<T> builder)
    {
        _builder = builder;
        OnPropertyChanged(nameof(Login));
        OnPropertyChanged(nameof(Password));
    }

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
        if (source.Value is not { } x)
        {
            x = new Credentials
            {
                Login = "",
                Password = "",
            };
            source.Value = x;
        }
        return x;
    }
}


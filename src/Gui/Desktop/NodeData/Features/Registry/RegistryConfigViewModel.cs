using CommunityToolkit.Mvvm.ComponentModel;
using Desktop.NodeData.Common;
using Microsoft.Extensions.DependencyInjection;
using ScheduleLib.OnlineRegistry;
using ScheduleLib.Scraping.Common;
using ScheduleLib.Scraping.Common.Config;

namespace Desktop.NodeData.Features.Registry;

public sealed class RegistryConfigViewModel : NodeDataViewModelBase<RegistryConfig>
{
    private readonly NodeDataAccessor<RegistryConfig> _helper;
    public RegistryConfigViewModel(NodeDataAccessor<RegistryConfig> helper)
    {
        _helper = helper;
    }

    public static void Register(IServiceCollection services)
    {
        services.AddVmFactory(
            RegistryConfig.Key,
            b => b.VM<RegistryConfigViewModel>().UseUpdateOnDataChange());
    }

    public override void UpdateSelection()
    {
        Credentials.Set(_helper.ConditionallyEditableData);
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
            if (_helper.Data?.CommandProcessingConfig is not { } c)
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
            var v = _helper.EditableData;
            if (v == null)
            {
                throw new InvalidOperationException("Cannot set DryRun when no node selected");
            }
            CommandProcessingConfigBuilder b;
            if (v.CommandProcessingConfig is { } c)
            {
                b = c.Builder();
            }
            else
            {
                b = new();
                b.Log().SetAll();
                b.Process().SetAll();
            }
            b.DryRun().SetAll(value ?? false);
            v.CommandProcessingConfig = b.Build();
        }
    }
}

public sealed class ObservableCredentials<T> : ObservableObject
    where T : class, ICredentialsHolder
{
    private ConditionallyEditableData<T> _m;
    public void Set(ConditionallyEditableData<T> m)
    {
        _m = m;
        OnPropertyChanged((string?) "");
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
        if (_m.Value is null)
        {
            return null;
        }
        var builder = new CredentialsSourceBuilder(_m.Value);
        var source = builder.Value(overwriteIfAnother: _m.IsEditable);
        if (source is null)
        {
            return null;
        }
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


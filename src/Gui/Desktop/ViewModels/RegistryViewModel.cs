using Anton.LayeredData;
using AutoConstructor.Attributes;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using ScheduleLib.OnlineRegistry;
using ScheduleLib.Scraping.Common;
using ScheduleLib.Scraping.Common.Config;

namespace Desktop.ViewModels;

public sealed class RegistryViewModelFactory : INodeDataViewModelFactory
{
    public NodeDataKey Key => RegistryConfig.Key.Value;

    public ObservableObject Create(IServiceProvider sp, ISelectedUserNode selectedUserNode)
    {
        return ActivatorUtilities.CreateInstance<RegistryViewModel>(sp, selectedUserNode);
    }
}

[AutoConstructor]
public sealed partial class ObservableCredentials : ObservableObject
{
    private readonly NodeDataBuilder<RegistryConfig> _builder;

    public Credentials Model => _builder.Credentials().Value().Value ?? new Credentials
    {
        Login = "",
        Password = "",
    };

    public string Login
    {
        get => Model.Login;
        set
        {
            var m = Model;
            SetProperty(m.Login, value, m, static (m, v) => m.Login = v);
        }
    }

    public string Password
    {
        get => Model.Password;
        set
        {
            var m = Model;
            SetProperty(m.Password, value, m, static (m, v) => m.Password = v);
        }
    }
}

public sealed partial class RegistryViewModel : ViewModelBase
{
    private readonly ISelectedUserNode _selectedUserNode;

    public RegistryViewModel(ISelectedUserNode selectedUserNode)
    {
        _selectedUserNode = selectedUserNode;
        _selectedUserNode.OnSelectedNodeChanged += OnNodeChanged;
    }

    private WrappedNode SelectedNode => _selectedUserNode.SelectedNode;

    private NodeDataBuilder<RegistryConfig> Builder()
    {
        if (SelectedNode.IsNull)
        {
            throw new InvalidOperationException();
        }
        return SelectedNode.Leaf.Builder(RegistryConfig.Key);
    }
    private RegistryConfig? Config
    {
        get
        {
            if (SelectedNode.IsNull)
            {
                return null;
            }
            var ret = Builder().Value();
            return ret;
        }
    }

    [ObservableProperty]
    public partial ObservableCredentials? Credentials { get; private set; }

    public ExtraLessonInstanceAction[] ExtraLessonInstanceActions => [
        ExtraLessonInstanceAction.Delete,
        ExtraLessonInstanceAction.LeaveAlone];

    public bool? DryRun
    {
        get
        {
            if (Config?.CommandProcessingConfig is not { } c)
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
            var v = Config;
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

    public void OnNodeChanged(WrappedNode node)
    {
        if (node.IsNull)
        {
            Credentials = null;
        }
        else
        {
            var builder = Builder();
            Credentials = new(builder);
        }
    }
}

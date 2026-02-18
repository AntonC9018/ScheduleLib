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
        var ret = ActivatorUtilities.CreateInstance<RegistryViewModel>(sp, selectedUserNode);
        return ret;
    }
}

[AutoConstructor]
public sealed partial class ObservableCredentials<T> : ObservableObject
    where T : class, ICredentialsHolder
{
    private readonly NodeDataBuilder<T> _builder;

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

public readonly struct ConfigViewModelHelper<T> : IDisposable
    where T : class
{
    public readonly ISelectedUserNode SelectedUserNode;
    private readonly Action<WrappedNode> _nodeChanged;
    private readonly NodeDataKey<T> _key;

    public ConfigViewModelHelper(
        NodeDataKey<T> key,
        ISelectedUserNode selectedUserNode,
        Action<WrappedNode> onNodeChanged)
    {
        _key = key;
        SelectedUserNode = selectedUserNode;
        _nodeChanged = onNodeChanged;
        selectedUserNode.OnSelectedNodeChanged += _nodeChanged;
    }

    public readonly void Dispose()
    {
        SelectedUserNode.OnSelectedNodeChanged -= _nodeChanged;
    }

    public WrappedNode SelectedNode => SelectedUserNode.SelectedNode;

    public NodeDataBuilder<T> Builder()
    {
        if (SelectedNode.IsNull)
        {
            throw new InvalidOperationException();
        }
        return SelectedNode.Leaf.Builder(_key);
    }
    public T? Config
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
}

public sealed partial class RegistryViewModel : ViewModelBase, IDisposable
{
    private readonly ConfigViewModelHelper<RegistryConfig> _helper;
    public RegistryViewModel(ISelectedUserNode selectedUserNode)
    {
        _helper = new(
            RegistryConfig.Key,
            selectedUserNode,
            node =>
            {
                if (node.IsNull)
                {
                    Credentials = null;
                }
                else
                {
                    var builder = _helper.Builder();
                    Credentials = new(builder);
                }
            });
    }
    public void Dispose() => _helper.Dispose();

    [ObservableProperty]
    public partial ObservableCredentials<RegistryConfig>? Credentials { get; private set; }

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

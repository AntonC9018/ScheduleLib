using CommunityToolkit.Mvvm.ComponentModel;
using Desktop.NodeData.Common;
using ScheduleLib.Scraping.Common;
using ScheduleLib.Scraping.Common.Config;

namespace Desktop.NodeData.Features.Registry;

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
        var source = builder.Value(overwriteIfOther: _m.IsEditable);
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

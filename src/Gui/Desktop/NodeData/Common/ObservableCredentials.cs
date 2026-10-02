using CommunityToolkit.Mvvm.ComponentModel;
using Desktop.NodeData.Common;
using ScheduleLib.Scraping.Common;
using ScheduleLib.Scraping.Common.Config;

namespace Desktop.NodeData.Features.Registry;

public static class ObservableCredentials
{
    public static ViewModelId Id;
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
        get => Read()?.Login ?? "";
        set
        {
            if (Editable() is { } credentials && credentials.Login != value)
            {
                credentials.Login = value;
                OnPropertyChanged();
            }
        }
    }

    public string Password
    {
        get => Read()?.Password ?? "";
        set
        {
            if (Editable() is { } credentials && credentials.Password != value)
            {
                credentials.Password = value;
                OnPropertyChanged();
            }
        }
    }

    private Credentials? Read() => (_m.Value?.Credentials as ValueCredentialsSource)?.Value;

    private Credentials? Editable()
    {
        if (!_m.IsEditable || _m.Value is null)
        {
            return null;
        }
        var builder = new CredentialsSourceBuilder(_m.Value);
        var source = builder.Value(overwriteIfOther: true)!;
        return source.Value ??= new Credentials { Login = "", Password = "" };
    }
}

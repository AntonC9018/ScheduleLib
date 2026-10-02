using CommunityToolkit.Mvvm.ComponentModel;
using Desktop.NodeData.Common;
using ScheduleLib.Scraping.Common;
using ScheduleLib.Scraping.Common.Config;

namespace Desktop.NodeData.Features.Registry;

public static class ObservableCredentials
{
    public static ViewModelId Id { get; } = new(typeof(CredentialsPropertyViewModel));
}

/// <summary>A reusable editor for any CredentialsSource property.</summary>
public sealed class CredentialsPropertyViewModel : ObservableObject, IPropertyEditorViewModel
{
    private PropertyEditorContext? _context;
    private Credentials? Read => (_context?.Value as ValueCredentialsSource)?.Value;
    public string Login
    {
        get => Read?.Login ?? "";
        set => Write(value, Password);
    }
    public string Password
    {
        get => Read?.Password ?? "";
        set => Write(Login, value);
    }
    public void Update(PropertyEditorContext context)
    {
        _context = context;
        OnPropertyChanged(nameof(Login));
        OnPropertyChanged(nameof(Password));
    }
    private void Write(string login, string password)
    {
        if (_context is not { IsEditable: true } context || (Login == login && Password == password)) return;
        // Replace the value through the generated property's guarded setter; don't mutate inherited sources.
        context.SetValue(new ValueCredentialsSource { Value = new Credentials { Login = login, Password = password } });
    }
    public void Dispose() => _context = null;
}

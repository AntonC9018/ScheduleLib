using Desktop.Views;

namespace Desktop.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    public MainWindowViewModel(IServiceProvider sp)
    {
    }

    public string Greeting { get; } = "Welcome to Avalonia!";
}

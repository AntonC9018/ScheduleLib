using Avalonia.Controls;
using Desktop.MvvmEssentials;

namespace Desktop.MainWindow;

public sealed partial class AddUserView : UserControl
{
    private EventSubscription _sub;

    public AddUserView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (!_sub.IsNull)
            {
                _sub.Dispose();
                _sub = default;
            }
            if (DataContext is null)
            {
                return;
            }
            var vm = (AddUserViewModel) DataContext;
            _sub = vm.UserAdded.Sub(() =>
            {
                AutoCompleteBox.Text = "";
                AutoCompleteBox.SelectedItem = null;
            });
        };
    }
}

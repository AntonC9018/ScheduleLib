using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Desktop.MvvmEssentials;

public abstract class ViewModelBase : ObservableObject
{
    protected readonly IDispatcher _dispatcher;

    protected ViewModelBase(IDispatcher dispatcher)
    {
        _dispatcher = dispatcher;
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        var id = new CallerIdentity(this);
        global::Desktop.MvvmEssentials.IDispatcher iDispatcher = _dispatcher;
        global::Desktop.MvvmEssentials.PostArgs<global::System.ComponentModel.PropertyChangedEventArgs> args = new(id, x => base.OnPropertyChanged(x), e);
        iDispatcher.Post<PropertyChangedEventArgs>(args);
    }
    protected override void OnPropertyChanging(PropertyChangingEventArgs e)
    {
        var id = new CallerIdentity(this);
        global::Desktop.MvvmEssentials.IDispatcher iDispatcher = _dispatcher;
        global::Desktop.MvvmEssentials.PostArgs<global::System.ComponentModel.PropertyChangingEventArgs> args = new(id, x => base.OnPropertyChanging(x), e);
        iDispatcher.Post<PropertyChangingEventArgs>(args);
    }
}


using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Desktop.NodeData.Common;

public interface INodeDataViewModel<T> : INotifyPropertyChanged
    where T : class
{
    public void UpdateSelection();
}

public abstract class NodeDataViewModelBase<T> : ObservableObject, IDisposable, INodeDataViewModel<T>
    where T : class
{
    public virtual void Dispose()
    {
        GC.SuppressFinalize(this);
    }

    public virtual void UpdateSelection()
    {
        AllPropertiesChanged();
    }

    protected void AllPropertiesChanged()
    {
        OnPropertyChanged((string?) "");
    }
}

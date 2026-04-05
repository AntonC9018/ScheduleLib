using System.Diagnostics.CodeAnalysis;
using ScheduleLib;

namespace Desktop.NodeData.Features.Registry;

public sealed class TypeComparer<T> : IEqualityComparer<T>
{
    public bool Equals(T? x, T? y)
    {
        if (ComparisonHelper.AtLeastOneIsDefault(
                x,
                y,
                out bool areBothNull))
        {
            return areBothNull;
        }

        return x.GetType() == y.GetType();
    }

    public int GetHashCode([DisallowNull] T obj)
    {
        return obj.GetType().GetHashCode();
    }
}

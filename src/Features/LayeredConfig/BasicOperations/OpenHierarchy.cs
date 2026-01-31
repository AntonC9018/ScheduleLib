using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ScheduleLib;

namespace Anton.LayeredConfig;

public interface IKeyEqualityComparer<in T> : IEqualityComparer<T>
{
}

public sealed class OpenHierarchyKeyEqualityComparer<T> : IKeyEqualityComparer<T>
    where T : class
{
    private readonly IServiceProvider _serviceProvider;

    public OpenHierarchyKeyEqualityComparer(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public bool Equals(T? x, T? y)
    {
        if (ComparisonHelper.NullGuard(x, y, out bool b))
        {
            return b;
        }
        var xtype = x.GetType();
        var ytype = y.GetType();
        if (xtype != ytype)
        {
            return false;
        }

        var ret = KeyEqualityCallHelper.CompareUsingService(_serviceProvider, x, y);
        return ret;
    }

    public int GetHashCode(T obj)
    {
        var ret = KeyEqualityCallHelper.GetHashCodeUsingService(_serviceProvider, obj);
        if (typeof(T).IsInterface)
        {
            ret ^= obj.GetType().GetHashCode();
        }
        return ret;
    }
}

public sealed class KeyEqualityComparer<T, TProperty> : IKeyEqualityComparer<T>
    where TProperty : notnull
{
    private readonly Func<T, TProperty?> _keyGetter;
    private readonly IEqualityComparer<TProperty> _propEquality;

    public KeyEqualityComparer(
        Func<T, TProperty?> keyGetter,
        IEqualityComparer<TProperty>? propEquality = null)
    {
        _keyGetter = keyGetter;
        _propEquality = propEquality ?? EqualityComparer<TProperty>.Default;
    }

    public bool Equals(T? x, T? y)
    {
        if (ComparisonHelper.NullGuard(x, y, out bool b))
        {
            return b;
        }
        var keyx = _keyGetter(x);
        var keyy = _keyGetter(y);
        return _propEquality.Equals(keyx, keyy);
    }

    public int GetHashCode([DisallowNull] T obj)
    {
        var key = _keyGetter(obj);
        if (key is null)
        {
            return 0;
        }
        var ret = _propEquality.GetHashCode(key);
        return ret;
    }
}

public static class KeyEqualityComparer
{
    private static KeyEqualityComparer<T, TProperty> Create<T, TProperty>(
        Func<T, TProperty?> keyGetter,
        IEqualityComparer<TProperty>? propertyComparer = null)

        where TProperty : notnull
    {
        return new(keyGetter, propertyComparer);
    }

    extension(IServiceCollection services)
    {
        public void AddKeyEqualityComparer<T, TProperty>(
            Func<T, TProperty?> keyGetter,
            IEqualityComparer<TProperty>? propertyComparer = null)

            where TProperty : notnull
        {
            var s = Create(keyGetter, propertyComparer);
            services.AddSingleton<IKeyEqualityComparer<T>>(s);
        }

        public void AddOpenHierarchy<TBase>()
            where TBase : class
        {
            services.TryAddSingleton<IKeyEqualityComparer<TBase>, OpenHierarchyKeyEqualityComparer<TBase>>();
            services.AddSingleton<IMerger<TBase>, OpenHierarchyMerger<TBase>>();
            services.AddSingleton<IBasicOperations<TBase>, OpenHierarchyBasicOperations<TBase>>();
        }

        public void SetImmutable<T>()
            where T : class
        {
            services.AddMerger<ImmutableObjectsMerger<T>>();
            services.AddBasicOperations<ImmutableClassBasicOperations<T>>();
        }
    }
}

public sealed class OpenHierarchyBasicOperations<T> : IBasicOperations<T>
    where T : class
{
    private readonly IServiceProvider _sp;

    public OpenHierarchyBasicOperations(IServiceProvider sp)
    {
        _sp = sp;
    }

    public T? Empty() => null;
    public T Copy(T from) => CallCopyHelper.CopyUsingService(_sp, from);
    public T? Reset(T? item) => null;
}

public sealed class ImmutableObjectsMerger<T> : IMerger<T>
{
    public T Merge(T from, T? into) => from;
}

public sealed class OpenHierarchyMerger<T> : IMerger<T>
{
    private readonly IServiceProvider _sp;

    public OpenHierarchyMerger(IServiceProvider sp)
    {
        _sp = sp;
    }

    public T Merge(T from, T? into)
    {
        if (into == null
            // Replace fully when types don't match.
            || from!.GetType() != into.GetType())
        {
            var ret = CallCopyHelper.CopyUsingService(_sp, from);
            return ret;
        }
        // Types match.
        {
            var ret = CallMergerHelper.MergeUsingService(_sp, from, into);
            return ret;
        }
    }
}

// This code is so shit I'm losing my mind.
internal static class KeyEqualityCallHelper
{
    public static bool CompareUsingService(IServiceProvider sp, object x, object y)
    {
        var comparer = GetService(sp, x);
        return Compare(comparer, x, y);
    }

    public static bool Compare(object comparer, object x, object y)
    {
        var comparerType = comparer.GetType();
        var compareDelegate = _compareCallHelper.Get(comparerType);
        return compareDelegate(comparer, x, y);
    }

    public static int GetHashCodeUsingService(IServiceProvider sp, object x)
    {
        var comparer = GetService(sp, x);
        return GetHashCode(comparer, x);
    }

    public static int GetHashCode(object comparer, object x)
    {
        var comparerType = comparer.GetType();
        var compareDelegate = _getHashCodeCallHelper.Get(comparerType);
        return compareDelegate(comparer, x);
    }

    private static object GetService(IServiceProvider sp, object x)
    {
        var fromType = x.GetType();
        var basicOperationsType = typeof(IKeyEqualityComparer<>).MakeGenericType(fromType);
        var comparer = sp.GetRequiredService(basicOperationsType);
        return comparer;
    }

    private delegate bool CompareDelegate(object comparer, object x, object y);

    private static readonly MethodInfo _compareGenericMethod = typeof(KeyEqualityCallHelper)
        .GetMethod(nameof(Compare1), BindingFlags.NonPublic | BindingFlags.Static)!;
    private static bool Compare1<T>(object comparer, object x, object y)
    {
        Debug.Assert(comparer.GetType().IsAssignableTo(typeof(IKeyEqualityComparer<T>)));
        Debug.Assert(x.GetType().IsAssignableTo(typeof(T)));
        Debug.Assert(y.GetType().IsAssignableTo(typeof(T)));
        var comparer1 = (IKeyEqualityComparer<T>) comparer;
        var x1 = (T) x;
        var y1 = (T) y;
        var ret = comparer1.Equals(x1, y1);
        return ret!;
    }

    private static readonly CallHelper<CompareDelegate> _compareCallHelper = new(
        typeof(IKeyEqualityComparer<>),
        _compareGenericMethod);

    private delegate int GetHashCodeDelegate(object comparer, object x);

    private static readonly MethodInfo _getHashCodeGenericMethod = typeof(KeyEqualityCallHelper)
        .GetMethod(nameof(GetHashCode1), BindingFlags.NonPublic | BindingFlags.Static)!;
    private static int GetHashCode1<T>(object comparer, object x)
    {
        Debug.Assert(comparer.GetType().IsAssignableTo(typeof(IKeyEqualityComparer<T>)));
        Debug.Assert(x.GetType().IsAssignableTo(typeof(T)));
        var comparer1 = (IKeyEqualityComparer<T>) comparer;
        var x1 = (T) x;
        var ret = comparer1.GetHashCode(x1);
        return ret!;
    }

    private static readonly CallHelper<GetHashCodeDelegate> _getHashCodeCallHelper = new(
        typeof(IKeyEqualityComparer<>),
        _getHashCodeGenericMethod);
}

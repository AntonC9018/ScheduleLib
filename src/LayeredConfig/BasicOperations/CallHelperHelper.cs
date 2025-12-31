using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;

namespace MainCli.BuilderNew;

internal sealed class CallHelper<TDelegate> where TDelegate : Delegate
{
    private readonly Type _interfaceType;
    private readonly MethodInfo _methodInfo;
    private static readonly ConcurrentDictionary<Type, TDelegate> _copyDelegateCache = new();

    public CallHelper(Type interfaceType, MethodInfo methodInfo)
    {
        Debug.Assert(interfaceType.IsGenericType);
        _interfaceType = interfaceType;
        _methodInfo = methodInfo;
    }

    public TDelegate Get(Type implType)
    {
        var deleg = _copyDelegateCache.GetOrAdd(implType, type =>
        {
            var inter = type.GetInterfaces()
                .FirstOrDefault(i => i.IsGenericType &&
                    i.GetGenericTypeDefinition() == _interfaceType);
            if (inter == null)
            {
                throw new ArgumentException($"Type {type} does not implement {_interfaceType.FullName}<T>", nameof(implType));
            }
            var types = inter.GetGenericArguments();
            if (types.Length != _methodInfo.GetGenericMethodDefinition().GetGenericArguments().Length)
            {
                throw new InvalidOperationException($"Argument count mismatch");
            }
            var genericMethod = _methodInfo.MakeGenericMethod(types);
            return genericMethod.CreateDelegate<TDelegate>();
        });
        return deleg;
    }
}


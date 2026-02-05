namespace Anton.LayeredData.Attributes;

// TODO: Add source generation support
[AttributeUsage(AttributeTargets.Method)]
public sealed class RegisterMethodAttribute : Attribute
{
}

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class KeyAttribute : Attribute
{
}

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property | AttributeTargets.Class)]
public class MergerAttribute : Attribute
{
    public MergerAttribute(Type type)
    {
        Type = type;
    }

    public Type Type { get; set; }
}

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property | AttributeTargets.Class)]
public sealed class MergerAttribute<T> : MergerAttribute
    where T : IMergerBase
{
    public MergerAttribute() : base(typeof(T))
    {
    }
}

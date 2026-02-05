namespace Anton.LayeredData;

public readonly record struct Layer(string Value) : ICreateFromString<Layer>
{
    public static readonly NameRegistry<Layer> Registry = new();
    public static readonly Layer DefaultLayer = Registry.Register("Default");
    public static Layer Unnamed => new("");
    public static Layer Create(string v) => new(v);
}

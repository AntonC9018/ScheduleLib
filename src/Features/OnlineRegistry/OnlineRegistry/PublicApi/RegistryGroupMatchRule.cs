namespace ScheduleLib.OnlineRegistry;

/// <summary>Opt-in matching across parent numbers within one unambiguous intake.</summary>
public sealed class RegistryGroupMatchRule
{
    public required string Faculty { get; set; }

    /// <summary>Canonical schedule subgroup name, after registry aliases are remapped.</summary>
    public required string SubGroup { get; set; }

    public required Language Language { get; set; }
    public bool MatchByLanguage { get; set; }

    /// <summary>Also accept whole-group lessons in a parent taught in the requested language.</summary>
    public bool AllowParentGroup { get; set; }
}

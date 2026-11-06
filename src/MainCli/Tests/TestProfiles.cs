/// <summary>Teacher profiles used by CLI tests.</summary>
public static class TestProfiles
{
    /// <summary>The maintainer's profile; the default for tests that need any teacher.</summary>
    public const string Owner = "Curmanschii Anton";

    /// <summary>
    /// A second teacher: for tests that need a profile other than <see cref="Owner"/>,
    /// or lessons in the 2025 semester 2 year III fixture, which the owner does not teach.
    /// </summary>
    public const string OtherTeacher = "Nartea Nichita";
}

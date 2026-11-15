using ScheduleLib.Builders;

namespace ScheduleLib.ParserTests;

public sealed class SubGroupAliasMappingTests
{
    [Fact]
    public void AliasSelectionReturnsACopyableValueThatWritesToItsOwner()
    {
        var remappings = new SubGroupNameRemappings();
        var mapping = remappings.ForAlias("AG");
        var copy = mapping;
        copy.To(new Specialization("Graph theory"));
        Assert.Equal(expected: "Graph theory", actual: remappings["AG"]);
        Assert.Equal(expected: "Graph theory", actual: remappings.Remap("AG".AsSpan()).Value);
        Assert.Throws<ArgumentException>(() => mapping.To(new Alternative("Another target")));
    }

    [Fact]
    public void BothDestinationFormsValidateBeforeAddingAnAlias()
    {
        var remappings = new SubGroupNameRemappings();
        var specialization = remappings.ForAlias("invalid-specialization");
        var alternative = remappings.ForAlias("invalid-alternative");
        Assert.Throws<ArgumentException>(() => specialization.To(Specialization.All));
        Assert.Throws<ArgumentException>(() => alternative.To(Alternative.All));
        Assert.Empty(remappings);
        var valid = remappings.ForAlias("optional");
        valid.To(new Alternative("Option A"));
        Assert.Equal(expected: "Option A", actual: remappings["optional"]);
    }

    [Fact]
    public void DefaultSelectionCannotWriteToAnUnselectedOwner()
    {
        var mapping = default(SubGroupNameRemappings.AliasMapping);
        Assert.Throws<UnselectedSubGroupAliasException>(() => mapping.To(new Specialization("target")));
    }
}

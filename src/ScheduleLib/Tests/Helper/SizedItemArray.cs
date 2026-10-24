using ScheduleLib.Helper;

namespace ScheduleLib.Tests;

public sealed class SizedItemArrayTests
{
    [Fact]
    public void AddAndFind_Basic()
    {
        var array = new SizedItemArray<string>();
        array.Add(new("A", 3));
        array.Add(new("B", 2));

        Assert.Equal(expected: "A", actual: array.Find(0));
        Assert.Equal(expected: "A", actual: array.Find(2));
        Assert.Equal(expected: "B", actual: array.Find(3));
    }

    [Fact]
    public void Find_Throws_OnInvalidIndex()
    {
        var array = new SizedItemArray<string>();
        array.Add(new("A", 2));

        Assert.Throws<ArgumentOutOfRangeException>(() => array.Find(2));
    }

    [Fact]
    public void FindPosition_Basic()
    {
        var array = new SizedItemArray<string>();
        array.Add(new("X", 3));
        array.Add(new("Y", 2));

        Assert.Equal(expected: 0, actual: array.FindPosition("X"));
        Assert.Equal(expected: 3, actual: array.FindPosition("Y"));
        Assert.Null(array.FindPosition("Z"));
    }

    [Fact]
    public void Replace_Basic()
    {
        var array = new SizedItemArray<string>();
        array.Add(new("Old", 2));

        bool replaced = array.Replace(oldItem: "Old", newItem: "New");

        Assert.True(replaced);
        Assert.Equal(expected: "New", actual: array.Find(0));
    }

    [Fact]
    public void ReplaceItem_Fails_WhenIndexInvalid()
    {
        var array = new SizedItemArray<string>();
        array.Add(new("A", 2));

        Assert.False(array.ReplaceItem(2, "B"));
        Assert.False(array.ReplaceItem(3, "B"));
    }

    [Fact]
    public void ReplaceAt_Fully()
    {
        var array = new SizedItemArray<string>();
        array.Add(new("A", 3));

        var result = array.ReplaceAt(0, new("B", 3));

        Assert.Equal(expected: 3, actual: array.TotalSize);
        Assert.Equal(expected: ReplaceItemStatus.FullyReplaced, actual: result);
        Assert.Equal(expected: "B", actual: array.Find(0));
        Assert.Null(array.FindPosition("A"));
    }

    [Fact]
    public void ReplaceAt_Partly()
    {
        var array = new SizedItemArray<string>();
        array.Add(new("A", 5));

        var result = array.ReplaceAt(0, new("B", 2));

        Assert.Equal(expected: 5, actual: array.TotalSize);
        Assert.Equal(expected: ReplaceItemStatus.PartlyReplaced, actual: result);
        Assert.Equal(expected: "B", actual: array.Find(0));
        Assert.Equal(expected: "A", actual: array.Find(2)); // remainder of A
    }

    [Fact]
    public void ReplaceAt_Spliced()
    {
        var array = new SizedItemArray<string>();
        array.Add(new("A", 6));

        var result = array.ReplaceAt(2, new("B", 2));

        Assert.Equal(expected: 6, actual: array.TotalSize);
        Assert.Equal(expected: ReplaceItemStatus.Spliced, actual: result);
        Assert.Equal(expected: "A", actual: array.Find(0));
        Assert.Equal(expected: "B", actual: array.Find(2));
        Assert.Equal(expected: "A", actual: array.Find(4));
    }

    [Fact]
    public void ReplaceAt_MoreItems()
    {
        var array = new SizedItemArray<string>();
        array.Add(new("A", 3));
        array.Add(new("B", 6));
        array.Add(new("C", 6));

        var result = array.ReplaceAt(5, new("D", 1));

        Assert.Equal(expected: 15, actual: array.TotalSize);
        Assert.Equal(expected: ReplaceItemStatus.Spliced, actual: result);
        Assert.Equal(expected: "A", actual: array.Find(0));
        Assert.Equal(expected: "B", actual: array.Find(3));
        Assert.Equal(expected: "D", actual: array.Find(5));
        Assert.Equal(expected: "B", actual: array.Find(6));
        Assert.Equal(expected: "C", actual: array.Find(9));
    }

    [Fact]
    public void ReplaceAtRange_Basic()
    {
        var array = new SizedItemArray<string>();
        array.Add(new("A", 3));
        array.Add(new("B", 6));
        array.Add(new("C", 6));

        var result = array.ReplaceAtRange(3, [
            new("D", 1),
            new("E", 2),
        ]);
        Assert.Equal(expected: ReplaceItemStatus.PartlyReplaced, actual: result);
        Assert.Equal(expected: 15, actual: array.TotalSize);
        Assert.Equal(expected: "A", actual: array.Find(0));
        Assert.Equal(expected: "D", actual: array.Find(3));
        Assert.Equal(expected: "E", actual: array.Find(4));
        Assert.Equal(expected: "B", actual: array.Find(6));
        Assert.Equal(expected: "C", actual: array.Find(9));
    }
}

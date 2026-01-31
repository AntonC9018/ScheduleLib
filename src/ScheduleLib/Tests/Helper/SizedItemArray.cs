using ScheduleLib.Helper;

namespace ScheduleLib.Tests;

public class SizedItemArrayTests
{
    [Fact]
    public void AddAndFind_Basic()
    {
        var array = new SizedItemArray<string>();
        array.Add(new("A", 3));
        array.Add(new("B", 2));

        Assert.Equal("A", array.Find(0));
        Assert.Equal("A", array.Find(2));
        Assert.Equal("B", array.Find(3));
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

        Assert.Equal(0, array.FindPosition("X"));
        Assert.Equal(3, array.FindPosition("Y"));
        Assert.Null(array.FindPosition("Z"));
    }

    [Fact]
    public void Replace_Basic()
    {
        var array = new SizedItemArray<string>();
        array.Add(new("Old", 2));

        bool replaced = array.Replace("Old", "New");

        Assert.True(replaced);
        Assert.Equal("New", array.Find(0));
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

        Assert.Equal(3, array.TotalSize);
        Assert.Equal(ReplaceItemStatus.FullyReplaced, result);
        Assert.Equal("B", array.Find(0));
        Assert.Null(array.FindPosition("A"));
    }

    [Fact]
    public void ReplaceAt_Partly()
    {
        var array = new SizedItemArray<string>();
        array.Add(new("A", 5));

        var result = array.ReplaceAt(0, new("B", 2));

        Assert.Equal(5, array.TotalSize);
        Assert.Equal(ReplaceItemStatus.PartlyReplaced, result);
        Assert.Equal("B", array.Find(0));
        Assert.Equal("A", array.Find(2)); // remainder of A
    }

    [Fact]
    public void ReplaceAt_Spliced()
    {
        var array = new SizedItemArray<string>();
        array.Add(new("A", 6));

        var result = array.ReplaceAt(2, new("B", 2));

        Assert.Equal(6, array.TotalSize);
        Assert.Equal(ReplaceItemStatus.Spliced, result);
        Assert.Equal("A", array.Find(0));
        Assert.Equal("B", array.Find(2));
        Assert.Equal("A", array.Find(4));
    }

    [Fact]
    public void ReplaceAt_MoreItems()
    {
        var array = new SizedItemArray<string>();
        array.Add(new("A", 3));
        array.Add(new("B", 6));
        array.Add(new("C", 6));

        var result = array.ReplaceAt(5, new("D", 1));

        Assert.Equal(15, array.TotalSize);
        Assert.Equal(ReplaceItemStatus.Spliced, result);
        Assert.Equal("A", array.Find(0));
        Assert.Equal("B", array.Find(3));
        Assert.Equal("D", array.Find(5));
        Assert.Equal("B", array.Find(6));
        Assert.Equal("C", array.Find(9));
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
        Assert.Equal(ReplaceItemStatus.PartlyReplaced, result);
        Assert.Equal(15, array.TotalSize);
        Assert.Equal("A", array.Find(0));
        Assert.Equal("D", array.Find(3));
        Assert.Equal("E", array.Find(4));
        Assert.Equal("B", array.Find(6));
        Assert.Equal("C", array.Find(9));
    }
}

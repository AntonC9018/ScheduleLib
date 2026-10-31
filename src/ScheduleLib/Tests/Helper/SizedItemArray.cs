using ScheduleLib.Helper;

namespace ScheduleLib.Tests;

public sealed class SizedItemArrayTests
{
    [Fact]
    public void AddAndFind_Basic()
    {
        var array = new SizedItemArray<string>();

        SizedItem<string> CalculateIt()
        {
            SizedItem<string> it = new("A", 3);
            return it;
        }

        array.Add(CalculateIt());

        SizedItem<string> CalculateIt2()
        {
            SizedItem<string> it2 = new("B", 2);
            return it2;
        }

        array.Add(CalculateIt2());

        string expected2 = "A";
        string actual2 = array.Find(0);
        Assert.Equal(expected: expected2, actual: actual2);
        string expected3 = "A";
        string actual3 = array.Find(2);
        Assert.Equal(expected: expected3, actual: actual3);
        string expected4 = "B";
        string actual4 = array.Find(3);
        Assert.Equal(expected: expected4, actual: actual4);
    }

    [Fact]
    public void Find_Throws_OnInvalidIndex()
    {
        var array = new SizedItemArray<string>();

        SizedItem<string> CalculateIt()
        {
            SizedItem<string> it = new("A", 2);
            return it;
        }

        array.Add(CalculateIt());

        Assert.Throws<ArgumentOutOfRangeException>(() => array.Find(2));
    }

    [Fact]
    public void FindPosition_Basic()
    {
        var array = new SizedItemArray<string>();

        SizedItem<string> CalculateIt()
        {
            SizedItem<string> it = new("X", 3);
            return it;
        }

        array.Add(CalculateIt());

        SizedItem<string> CalculateIt2()
        {
            SizedItem<string> it2 = new("Y", 2);
            return it2;
        }

        array.Add(CalculateIt2());

        int? expected2 = 0;
        int? actual2 = array.FindPosition("X");
        Assert.Equal(expected: expected2, actual: actual2);
        int? expected3 = 3;
        int? actual3 = array.FindPosition("Y");
        Assert.Equal(expected: expected3, actual: actual3);
        int? value = array.FindPosition("Z");
        Assert.Null(value);
    }

    [Fact]
    public void Replace_Basic()
    {
        var array = new SizedItemArray<string>();

        SizedItem<string> CalculateIt()
        {
            SizedItem<string> it = new("Old", 2);
            return it;
        }

        array.Add(CalculateIt());

        bool replaced = array.Replace(oldItem: "Old", newItem: "New");

        Assert.True(replaced);
        string expected2 = "New";
        string actual2 = array.Find(0);
        Assert.Equal(expected: expected2, actual: actual2);
    }

    [Fact]
    public void ReplaceItem_Fails_WhenIndexInvalid()
    {
        var array = new SizedItemArray<string>();

        SizedItem<string> CalculateIt()
        {
            SizedItem<string> it = new("A", 2);
            return it;
        }

        array.Add(CalculateIt());

        bool condition = array.ReplaceItem(2, "B");
        Assert.False(condition);
        bool condition2 = array.ReplaceItem(3, "B");
        Assert.False(condition2);
    }

    [Fact]
    public void ReplaceAt_Fully()
    {
        var array = new SizedItemArray<string>();

        SizedItem<string> CalculateIt()
        {
            SizedItem<string> it = new("A", 3);
            return it;
        }

        array.Add(CalculateIt());

        SizedItem<string> CalculateItem()
        {
            SizedItem<string> item = new("B", 3);
            return item;
        }

        var result = array.ReplaceAt(0, CalculateItem());

        Assert.Equal(expected: 3, actual: array.TotalSize);
        Assert.Equal(expected: ReplaceItemStatus.FullyReplaced, actual: result);
        string expected2 = "B";
        string actual2 = array.Find(0);
        Assert.Equal(expected: expected2, actual: actual2);
        int? value = array.FindPosition("A");
        Assert.Null(value);
    }

    [Fact]
    public void ReplaceAt_Partly()
    {
        var array = new SizedItemArray<string>();

        SizedItem<string> CalculateIt()
        {
            SizedItem<string> it = new("A", 5);
            return it;
        }

        array.Add(CalculateIt());

        SizedItem<string> CalculateItem()
        {
            SizedItem<string> item = new("B", 2);
            return item;
        }

        var result = array.ReplaceAt(0, CalculateItem());

        Assert.Equal(expected: 5, actual: array.TotalSize);
        Assert.Equal(expected: ReplaceItemStatus.PartlyReplaced, actual: result);
        string expected2 = "B";
        string actual2 = array.Find(0);
        Assert.Equal(expected: expected2, actual: actual2);
        string expected3 = "A";
        string actual3 = array.Find(2);
        Assert.Equal(expected: expected3, actual: actual3); // remainder of A
    }

    [Fact]
    public void ReplaceAt_Spliced()
    {
        var array = new SizedItemArray<string>();

        SizedItem<string> CalculateIt()
        {
            SizedItem<string> it = new("A", 6);
            return it;
        }

        array.Add(CalculateIt());

        SizedItem<string> CalculateItem()
        {
            SizedItem<string> item = new("B", 2);
            return item;
        }

        var result = array.ReplaceAt(2, CalculateItem());

        Assert.Equal(expected: 6, actual: array.TotalSize);
        Assert.Equal(expected: ReplaceItemStatus.Spliced, actual: result);
        string expected2 = "A";
        string actual2 = array.Find(0);
        Assert.Equal(expected: expected2, actual: actual2);
        string expected3 = "B";
        string actual3 = array.Find(2);
        Assert.Equal(expected: expected3, actual: actual3);
        string expected4 = "A";
        string actual4 = array.Find(4);
        Assert.Equal(expected: expected4, actual: actual4);
    }

    [Fact]
    public void ReplaceAt_MoreItems()
    {
        var array = new SizedItemArray<string>();

        SizedItem<string> CalculateIt()
        {
            SizedItem<string> it = new("A", 3);
            return it;
        }

        array.Add(CalculateIt());

        SizedItem<string> CalculateIt2()
        {
            SizedItem<string> it2 = new("B", 6);
            return it2;
        }

        array.Add(CalculateIt2());

        SizedItem<string> CalculateIt3()
        {
            SizedItem<string> it3 = new("C", 6);
            return it3;
        }

        array.Add(CalculateIt3());

        SizedItem<string> CalculateItem()
        {
            SizedItem<string> item = new("D", 1);
            return item;
        }

        var result = array.ReplaceAt(5, CalculateItem());

        Assert.Equal(expected: 15, actual: array.TotalSize);
        Assert.Equal(expected: ReplaceItemStatus.Spliced, actual: result);
        string expected2 = "A";
        string actual2 = array.Find(0);
        Assert.Equal(expected: expected2, actual: actual2);
        string expected3 = "B";
        string actual3 = array.Find(3);
        Assert.Equal(expected: expected3, actual: actual3);
        string expected4 = "D";
        string actual4 = array.Find(5);
        Assert.Equal(expected: expected4, actual: actual4);
        string expected5 = "B";
        string actual5 = array.Find(6);
        Assert.Equal(expected: expected5, actual: actual5);
        string expected6 = "C";
        string actual6 = array.Find(9);
        Assert.Equal(expected: expected6, actual: actual6);
    }

    [Fact]
    public void ReplaceAtRange_Basic()
    {
        var array = new SizedItemArray<string>();

        SizedItem<string> CalculateIt()
        {
            SizedItem<string> it = new("A", 3);
            return it;
        }

        array.Add(CalculateIt());

        SizedItem<string> CalculateIt2()
        {
            SizedItem<string> it2 = new("B", 6);
            return it2;
        }

        array.Add(CalculateIt2());

        SizedItem<string> CalculateIt3()
        {
            SizedItem<string> it3 = new("C", 6);
            return it3;
        }

        array.Add(CalculateIt3());

        var result = array.ReplaceAtRange(
        3,
        [
            new("D", 1),
            new("E", 2),
        ]);
        Assert.Equal(expected: ReplaceItemStatus.PartlyReplaced, actual: result);
        Assert.Equal(expected: 15, actual: array.TotalSize);
        string expected2 = "A";
        string actual2 = array.Find(0);
        Assert.Equal(expected: expected2, actual: actual2);
        string expected3 = "D";
        string actual3 = array.Find(3);
        Assert.Equal(expected: expected3, actual: actual3);
        string expected4 = "E";
        string actual4 = array.Find(4);
        Assert.Equal(expected: expected4, actual: actual4);
        string expected5 = "B";
        string actual5 = array.Find(6);
        Assert.Equal(expected: expected5, actual: actual5);
        string expected6 = "C";
        string actual6 = array.Find(9);
        Assert.Equal(expected: expected6, actual: actual6);
    }
}

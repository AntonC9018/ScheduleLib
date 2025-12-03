using ScheduleLib.Helper;

namespace ScheduleLib.Tests;

public enum TestDay
{
    Monday,
    Tuesday,
    Wednesday,
    Thursday,
    Friday,
    Saturday,
    Sunday,
}

public sealed class BitArrayForEnumTests
{
    [Fact]
    public void SetAndIsSet()
    {
        var bitArray = new BitArrayForEnum<TestDay>();
        Assert.False(bitArray.IsSet(TestDay.Monday));

        bitArray.Set(TestDay.Monday);
        Assert.True(bitArray.IsSet(TestDay.Monday));
        Assert.False(bitArray.IsSet(TestDay.Tuesday));
    }

    [Fact]
    public void Clear()
    {
        var bitArray = new BitArrayForEnum<TestDay>();
        bitArray.Set(TestDay.Monday);
        bitArray.Set(TestDay.Wednesday);

        Assert.True(bitArray.IsSet(TestDay.Monday));
        bitArray.Clear(TestDay.Monday);
        Assert.False(bitArray.IsSet(TestDay.Monday));
        Assert.True(bitArray.IsSet(TestDay.Wednesday));
    }

    [Fact]
    public void AllSet()
    {
        var bitArray = BitArrayForEnum<TestDay>.AllSet;
        Assert.True(bitArray.AreAllSet);
        Assert.Equal(7, bitArray.SetCount);

        foreach (var day in new AllEnumEnumerable<TestDay>())
        {
            Assert.True(bitArray.IsSet(day));
        }
    }

    [Fact]
    public void WithSetImmutable()
    {
        var bitArray = new BitArrayForEnum<TestDay>();
        var modified = bitArray.WithSet(TestDay.Tuesday);

        Assert.False(bitArray.IsSet(TestDay.Tuesday));
        Assert.True(modified.IsSet(TestDay.Tuesday));
    }

    [Fact]
    public void WithClearImmutable()
    {
        var bitArray = BitArrayForEnum<TestDay>.AllSet;
        var modified = bitArray.WithClear(TestDay.Friday);

        Assert.True(bitArray.IsSet(TestDay.Friday));
        Assert.False(modified.IsSet(TestDay.Friday));
    }

    [Fact]
    public void Flipped()
    {
        var bitArray = new BitArrayForEnum<TestDay>();
        bitArray.Set(TestDay.Monday);
        bitArray.Set(TestDay.Wednesday);

        var flipped = bitArray.Flipped;
        Assert.False(flipped.IsSet(TestDay.Monday));
        Assert.True(flipped.IsSet(TestDay.Tuesday));
        Assert.False(flipped.IsSet(TestDay.Wednesday));
        Assert.True(flipped.IsSet(TestDay.Thursday));
    }

    [Fact]
    public void Intersect()
    {
        var bitArray1 = new BitArrayForEnum<TestDay>();
        bitArray1.Set(TestDay.Monday);
        bitArray1.Set(TestDay.Wednesday);
        bitArray1.Set(TestDay.Friday);

        var bitArray2 = new BitArrayForEnum<TestDay>();
        bitArray2.Set(TestDay.Wednesday);
        bitArray2.Set(TestDay.Friday);
        bitArray2.Set(TestDay.Sunday);

        var result = bitArray1.Intersect(bitArray2);
        Assert.False(result.IsSet(TestDay.Monday));
        Assert.True(result.IsSet(TestDay.Wednesday));
        Assert.True(result.IsSet(TestDay.Friday));
        Assert.False(result.IsSet(TestDay.Sunday));
    }

    [Fact]
    public void GetFirstSet()
    {
        var bitArray = new BitArrayForEnum<TestDay>();
        Assert.Null(bitArray.GetFirstSet());

        bitArray.Set(TestDay.Wednesday);
        bitArray.Set(TestDay.Friday);
        Assert.Equal(TestDay.Wednesday, bitArray.GetFirstSet());
    }

    [Fact]
    public void SetValuesEnumeration()
    {
        var bitArray = new BitArrayForEnum<TestDay>();
        bitArray.Set(TestDay.Tuesday);
        bitArray.Set(TestDay.Thursday);
        bitArray.Set(TestDay.Saturday);

        var values = bitArray.SetValues().ToList();
        Assert.Equal(3, values.Count);
        Assert.Equal(TestDay.Tuesday, values[0]);
        Assert.Equal(TestDay.Thursday, values[1]);
        Assert.Equal(TestDay.Saturday, values[2]);
    }

    [Fact]
    public void SetValuesEmpty()
    {
        var bitArray = new BitArrayForEnum<TestDay>();
        var values = bitArray.SetValues().ToList();
        Assert.Empty(values);
    }

    [Fact]
    public void ClearAll()
    {
        var bitArray = BitArrayForEnum<TestDay>.AllSet;
        Assert.True(bitArray.AreAllSet);

        bitArray.ClearAll();
        Assert.True(bitArray.AreNoneSet);
        Assert.Equal(0, bitArray.SetCount);
    }

    [Fact]
    public void AreNoneSet()
    {
        var bitArray = new BitArrayForEnum<TestDay>();
        Assert.True(bitArray.AreNoneSet);

        bitArray.Set(TestDay.Monday);
        Assert.False(bitArray.AreNoneSet);
    }

    [Fact]
    public void SetCount()
    {
        var bitArray = new BitArrayForEnum<TestDay>();
        Assert.Equal(0, bitArray.SetCount);

        bitArray.Set(TestDay.Monday);
        Assert.Equal(1, bitArray.SetCount);

        bitArray.Set(TestDay.Wednesday);
        bitArray.Set(TestDay.Friday);
        Assert.Equal(3, bitArray.SetCount);
    }
}

namespace ScheduleLib.Tests;

public sealed class BitArray
{
    [Fact]
    public void GetSetAfter()
    {
        var bitArray = BitArray32.Empty(10);
        bitArray.Set(0);
        bitArray.Set(2);
        bitArray.Set(4);
        bitArray.Set(6);
        Assert.Equal(expected: 2, actual: bitArray.GetSetAfter(0));
        Assert.Equal(expected: 4, actual: bitArray.GetSetAfter(2));
        Assert.Equal(expected: 6, actual: bitArray.GetSetAfter(4));
        Assert.Equal(expected: -1, actual: bitArray.GetSetAfter(6));
    }

    [Fact]
    public void GetUnsetAfter()
    {
        var bitArray = BitArray32.AllSet(10);
        bitArray.Set(0, false);
        bitArray.Set(2, false);
        bitArray.Set(4, false);
        bitArray.Set(6, false);
        Assert.Equal(expected: 2, actual: bitArray.GetUnsetAfter(0));
        Assert.Equal(expected: 4, actual: bitArray.GetUnsetAfter(2));
        Assert.Equal(expected: 6, actual: bitArray.GetUnsetAfter(4));
        Assert.Equal(expected: -1, actual: bitArray.GetUnsetAfter(6));
    }

    [Fact]
    public void GetUnsetEmpty()
    {
        var bitArray = BitArray32.AllSet(0);
        Assert.Equal(expected: -1, actual: bitArray.GetUnsetAfter(-1));
    }

    [Fact]
    public void SetIndicesLowToHigh()
    {
        var bitArray = BitArray32.Empty(8);
        bitArray.Set(5);
        bitArray.Set(7);
        using var e = bitArray.SetBitIndicesLowToHigh.GetEnumerator();
        Assert.True(e.MoveNext());
        Assert.Equal(expected: 5, actual: e.Current);
        Assert.True(e.MoveNext());
        Assert.Equal(expected: 7, actual: e.Current);
    }

    [Fact]
    public void UnsetIndicesLowToHigh()
    {
        var bitArray = BitArray32.AllSet(8);
        bitArray.Set(5, false);
        bitArray.Set(7, false);
        using var e = bitArray.UnsetBitIndicesLowToHigh.GetEnumerator();
        Assert.True(e.MoveNext());
        Assert.Equal(expected: 5, actual: e.Current);
        Assert.True(e.MoveNext());
        Assert.Equal(expected: 7, actual: e.Current);
    }

    [Fact]
    public void SetIndicesHighToLow()
    {
        var bitArray = BitArray32.Empty(8);
        bitArray.Set(5);
        bitArray.Set(7);
        using var e = bitArray.SetBitIndicesHighToLow.GetEnumerator();
        Assert.True(e.MoveNext());
        Assert.Equal(expected: 7, actual: e.Current);
        Assert.True(e.MoveNext());
        Assert.Equal(expected: 5, actual: e.Current);
    }

    [Fact]
    public void SetArray()
    {
        var bitArray = BitArray32.Empty(10);
        var setArr = UnsizedBitArray32.GetMask(3).ShiftedLeft(5).WithFixedSize(10);
        bitArray.SetArray(setArr);
        Assert.Collection(bitArray.SetBitIndicesLowToHigh,
            f1 => Assert.Equal(expected: 5, actual: f1),
            f2 => Assert.Equal(expected: 6, actual: f2),
            f3 => Assert.Equal(expected: 7, actual: f3));
    }

    [Fact]
    public void ClearArray()
    {
        var bitArray = BitArray32.AllSet(10);
        var clearArr = UnsizedBitArray32.GetMask(8).ShiftedLeft(1).WithFixedSize(10);
        bitArray.ClearArray(clearArr);
        Assert.Collection(bitArray.SetBitIndicesLowToHigh,
            f1 => Assert.Equal(expected: 0, actual: f1),
            f2 => Assert.Equal(expected: 9, actual: f2));
    }

    [Fact(Skip = "DEBUG")]
    public void ChecksArrayOps1()
    {
        Assert.ThrowsAny<Exception>(() =>
        {
            var otherArr = UnsizedBitArray32.GetMask(8).ShiftedLeft(8).WithFixedSize(10);
            _ = otherArr;
        });
    }

    [Fact]
    public void RemoveTest()
    {
        var arr = BitArray32.Empty(10);
        arr.Set(0);
        arr.Set(1);
        arr.Set(3);
        arr.Set(5);

        var mask = BitArray32.Empty(10);
        mask.Set(3);
        mask.Set(0);
        mask.Set(2);

        arr = arr.Remove(mask);

        Assert.Equal(expected: arr.SetBitIndicesLowToHigh, actual: [1, 5]);
    }
}

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
        Assert.Equal(2, bitArray.GetSetAfter(0));
        Assert.Equal(4, bitArray.GetSetAfter(2));
        Assert.Equal(6, bitArray.GetSetAfter(4));
        Assert.Equal(-1, bitArray.GetSetAfter(6));
    }

    [Fact]
    public void GetUnsetAfter()
    {
        var bitArray = BitArray32.AllSet(10);
        bitArray.Set(0, false);
        bitArray.Set(2, false);
        bitArray.Set(4, false);
        bitArray.Set(6, false);
        Assert.Equal(2, bitArray.GetUnsetAfter(0));
        Assert.Equal(4, bitArray.GetUnsetAfter(2));
        Assert.Equal(6, bitArray.GetUnsetAfter(4));
        Assert.Equal(-1, bitArray.GetUnsetAfter(6));
    }

    [Fact]
    public void GetUnsetEmpty()
    {
        var bitArray = BitArray32.AllSet(0);
        Assert.Equal(-1, bitArray.GetUnsetAfter(-1));
    }

    [Fact]
    public void SetIndicesLowToHigh()
    {
        var bitArray = BitArray32.Empty(8);
        bitArray.Set(5);
        bitArray.Set(7);
        using var e = bitArray.SetBitIndicesLowToHigh.GetEnumerator();
        Assert.True(e.MoveNext());
        Assert.Equal(5, e.Current);
        Assert.True(e.MoveNext());
        Assert.Equal(7, e.Current);
    }

    [Fact]
    public void UnsetIndicesLowToHigh()
    {
        var bitArray = BitArray32.AllSet(8);
        bitArray.Set(5, false);
        bitArray.Set(7, false);
        using var e = bitArray.UnsetBitIndicesLowToHigh.GetEnumerator();
        Assert.True(e.MoveNext());
        Assert.Equal(5, e.Current);
        Assert.True(e.MoveNext());
        Assert.Equal(7, e.Current);
    }

    [Fact]
    public void SetIndicesHighToLow()
    {
        var bitArray = BitArray32.Empty(8);
        bitArray.Set(5);
        bitArray.Set(7);
        using var e = bitArray.SetBitIndicesHighToLow.GetEnumerator();
        Assert.True(e.MoveNext());
        Assert.Equal(7, e.Current);
        Assert.True(e.MoveNext());
        Assert.Equal(5, e.Current);
    }

    [Fact]
    public void SetArray()
    {
        var bitArray = BitArray32.Empty(10);
        var setArr = UnsizedBitArray32.GetMask(3).ShiftedLeft(5).WithFixedSize(10);
        bitArray.SetArray(setArr);
        Assert.Collection(bitArray.SetBitIndicesLowToHigh,
            f1 => Assert.Equal(5, f1),
            f2 => Assert.Equal(6, f2),
            f3 => Assert.Equal(7, f3));
    }

    [Fact]
    public void ClearArray()
    {
        var bitArray = BitArray32.AllSet(10);
        var clearArr = UnsizedBitArray32.GetMask(8).ShiftedLeft(1).WithFixedSize(10);
        bitArray.ClearArray(clearArr);
        Assert.Collection(bitArray.SetBitIndicesLowToHigh,
            f1 => Assert.Equal(0, f1),
            f2 => Assert.Equal(9, f2));
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
}

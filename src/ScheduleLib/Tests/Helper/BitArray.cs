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
        int expected2 = 2;
        int actual2 = bitArray.GetSetAfter(0);
        Assert.Equal(expected: expected2, actual: actual2);
        int expected3 = 4;
        int actual3 = bitArray.GetSetAfter(2);
        Assert.Equal(expected: expected3, actual: actual3);
        int expected4 = 6;
        int actual4 = bitArray.GetSetAfter(4);
        Assert.Equal(expected: expected4, actual: actual4);
        int expected5 = -1;
        int actual5 = bitArray.GetSetAfter(6);
        Assert.Equal(expected: expected5, actual: actual5);
    }

    [Fact]
    public void GetUnsetAfter()
    {
        var bitArray = BitArray32.AllSet(10);
        bitArray.Set(0, false);
        bitArray.Set(2, false);
        bitArray.Set(4, false);
        bitArray.Set(6, false);
        int expected2 = 2;
        int actual2 = bitArray.GetUnsetAfter(0);
        Assert.Equal(expected: expected2, actual: actual2);
        int expected3 = 4;
        int actual3 = bitArray.GetUnsetAfter(2);
        Assert.Equal(expected: expected3, actual: actual3);
        int expected4 = 6;
        int actual4 = bitArray.GetUnsetAfter(4);
        Assert.Equal(expected: expected4, actual: actual4);
        int expected5 = -1;
        int actual5 = bitArray.GetUnsetAfter(6);
        Assert.Equal(expected: expected5, actual: actual5);
    }

    [Fact]
    public void GetUnsetEmpty()
    {
        var bitArray = BitArray32.AllSet(0);
        int expected2 = -1;
        int actual2 = bitArray.GetUnsetAfter(-1);
        Assert.Equal(expected: expected2, actual: actual2);
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
        var setArr = UnsizedBitArray32.GetMask(3)
            .ShiftedLeft(5)
            .WithFixedSize(10);
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
        var clearArr = UnsizedBitArray32.GetMask(8)
            .ShiftedLeft(1)
            .WithFixedSize(10);
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
            var otherArr = UnsizedBitArray32.GetMask(8)
                .ShiftedLeft(8)
                .WithFixedSize(10);
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

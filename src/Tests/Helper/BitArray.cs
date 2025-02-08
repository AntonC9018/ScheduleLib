namespace App.Tests;

public class BitArray
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
}

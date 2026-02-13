namespace ScheduleLib.Tests;

public sealed class BitArrayRef
{
    #if DEBUG // It's implemented with Debug.Assert, might want to change that.
    [Fact]
    public void BreaksForInvalidOffset()
    {
        Assert.ThrowsAny<Exception>(() =>
        {
            var bitArray = BitArray32.Empty(10);
            var arrRef = bitArray.PortionRef(offset: 6, len: 5);
            _ = arrRef;
        });
    }

    [Fact]
    public void BreaksForInvalidLen()
    {
        Assert.ThrowsAny<Exception>(() =>
        {
            var bitArray = BitArray32.Empty(10);
            var arrRef = bitArray.PortionRef(offset: 0, len: 20);
            _ = arrRef;
        });
    }
    #endif

    [Fact]
    public void SetIndex()
    {
        var bitArray = BitArray32.Empty(10);
        var arrRef = bitArray.PortionRef(offset: 2, len: 3);
        arrRef.Set(0);
        arrRef.Set(2);
        Assert.True(bitArray.IsSet(2));
        Assert.True(bitArray.IsSet(4));
    }

    [Fact]
    public void SetArray()
    {
        var bitArray = BitArray32.Empty(10);
        var arrRef = bitArray.PortionRef(offset: 2, len: 5);
        arrRef.SetArray(UnsizedBitArray32.GetMask(3));
        Assert.True(bitArray.IsSet(2));
        Assert.True(bitArray.IsSet(3));
        Assert.True(bitArray.IsSet(4));
    }
}

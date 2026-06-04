using ScheduleLib.Helper;

namespace ScheduleLib.Tests;

public sealed class EnumBitArrayRef
{
    #if DEBUG // It's implemented with Debug.Assert, might want to change that.
    [Fact]
    public void BreaksForInvalidOffset()
    {
        Assert.ThrowsAny<Exception>(() =>
        {
            var bitArray = BitArray64.Empty(10);
            var arrRef = bitArray.EnumPortionRef<TestDay>(offset: 5);
            _ = arrRef;
        });
    }

    [Fact]
    public void BreaksForInvalidLen()
    {
        Assert.ThrowsAny<Exception>(() =>
        {
            var bitArray = BitArray64.Empty(5);
            var arrRef = bitArray.EnumPortionRef<TestDay>(offset: 0);
            _ = arrRef;
        });
    }
    #endif

    [Fact]
    public void SetIndex()
    {
        var bitArray = BitArray64.Empty(10);
        var arrRef = bitArray.EnumPortionRef<TestDay>(offset: 2);
        arrRef.Set(TestDay.Friday);
        Assert.True(bitArray.IsSet(2 + (int) TestDay.Friday));
    }

    [Fact]
    public void SetArray()
    {
        var bitArray = BitArray64.Empty(10);
        var arrRef = bitArray.EnumPortionRef<TestDay>(offset: 2);
        var mask = new EnumBitArray<TestDay>();
        mask.Set(TestDay.Friday);
        arrRef.SetArray(mask);
        Assert.True(bitArray.IsSet(2 + (int) TestDay.Friday));
    }
}

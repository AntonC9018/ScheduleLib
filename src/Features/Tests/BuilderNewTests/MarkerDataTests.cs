using Anton.LayeredData.Retrieval;
using Anton.LayeredData.TreeEnumeration;
using ScheduleLib.Application.Config;

namespace Anton.LayeredData.Tests;

public sealed class MarkerDataTests
{
    private sealed class TestMarkerDataHelper : MarkerDataHelperBase<TeacherLayerConfig>
    {
        public override TeacherLayerConfig GetMarkerData(IServiceProvider sp) => throw new NotSupportedException();
        public override NodePath? GetCurrentPath(TeacherLayerConfig marker) => null;
    }

    [Fact]
    public void DefaultMarkerDataThrowsMismatchWithNullActualType()
    {
        IMarkerDataHelper helper = new TestMarkerDataHelper();

        var exception = Assert.Throws<MarkerConfigTypeMismatchException>(() => helper.GetCurrentPath(default));

        Assert.Equal(expected: typeof(TeacherLayerConfig), actual: exception.ExpectedType);
        Assert.Null(exception.ActualType);
        Assert.Contains(expectedSubstring: "null", actualString: exception.Message);
    }

    [Fact]
    public void MismatchedMarkerDataPreservesRuntimeActualType()
    {
        IMarkerDataHelper helper = new TestMarkerDataHelper();
        var data = new MarkerData("unexpected");

        var exception = Assert.Throws<MarkerConfigTypeMismatchException>(() => helper.GetCurrentPath(data));

        Assert.Equal(expected: typeof(TeacherLayerConfig), actual: exception.ExpectedType);
        Assert.Equal(expected: typeof(string), actual: exception.ActualType);
        Assert.Contains(expectedSubstring: nameof(String), actualString: exception.Message);
    }
}

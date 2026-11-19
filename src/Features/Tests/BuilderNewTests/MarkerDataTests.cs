using System.Reflection;
using System.Runtime.CompilerServices;
using Anton.LayeredData.Retrieval;
using Anton.LayeredData.TreeEnumeration;
using ScheduleLib.Application.Config;
using ScheduleLib.Parsing;

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

    /// <summary>A marker shared through the settings tree cannot be changed by whoever reads it.</summary>
    [Theory]
    [InlineData(typeof(TeacherLayerConfig))]
    [InlineData(typeof(Name))]
    public void MarkerStateIsInitOnly(Type type)
    {
        static bool IsMutable(PropertyInfo property)
        {
            if (property.SetMethod is not { IsPublic: true } setter)
            {
                return false;
            }

            var modifiers = setter.ReturnParameter.GetRequiredCustomModifiers();
            if (modifiers.Contains(typeof(IsExternalInit)))
            {
                return false;
            }

            return true;
        }

        var mutable = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(IsMutable)
            .Select(x => x.Name);

        Assert.Empty(mutable);
    }
}

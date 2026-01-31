using ScheduleLib.Builders;

namespace ScheduleLib.ParserTests;

public sealed class TeacherNameTests
{
    private void Test(string input, TeacherBuilderModel.NameModel expected)
    {
        var name = TeacherNameHelper.ParseName(input);
        Assert.Equal(expected, name);
    }

    [Fact]
    public void LastName()
    {
        var model = new TeacherBuilderModel.NameModel();
        model.LastName[0] = "Lastname";
        Test("Lastname", model);
    }

    [Fact]
    public void RegularFirstLast()
    {
        var model = new TeacherBuilderModel.NameModel();
        model.LastName[0] = "Last";
        model.FirstName[0].Full = "First";
        Test("First Last", model);
    }

    [Fact]
    public void ShortFirstName()
    {
        var model = new TeacherBuilderModel.NameModel();
        model.LastName[0] = "Last";
        model.FirstName[0].Short = "F.";
        Test("F. Last", model);
    }

    [Fact]
    public void DoubleFirstName()
    {
        var model = new TeacherBuilderModel.NameModel();
        model.LastName[0] = "Last";
        model.FirstName[0].Full = "Firsta";
        model.FirstName[1].Full = "Firstb";
        Test("Firsta-Firstb Last", model);
    }

    [Fact]
    public void ShortLast_NotAllowed()
    {
        Assert.Throws<ArgumentException>(() =>
        {
            var name = TeacherNameHelper.ParseName("First L.");
            _ = name;
        });
    }

    [Fact]
    public void CanMixShortAndFullInDoubleNames()
    {
        // "F.-Firstb Last"
        var model = new TeacherBuilderModel.NameModel();
        model.LastName[0] = "Last";
        model.FirstName[0].Short = "F.";
        model.FirstName[1].Full = "Firstb";
        Test("F.-Firstb Last", model);
    }

    [Fact]
    public void DoubleLastName_SimpleShortName()
    {
        var model = new TeacherBuilderModel.NameModel();
        model.LastName[0] = "Lasta";
        model.LastName[1] = "Lastb";
        model.FirstName[0].Full = "First";
        Test("First Lasta-Lastb", model);
    }

    [Fact]
    public void ShortFirstName_DoubleLastName()
    {
        var model = new TeacherBuilderModel.NameModel();
        model.LastName[0] = "Lasta";
        model.LastName[1] = "Lastb";
        model.FirstName[0].Short = "F.";
        Test("F. Lasta-Lastb", model);
    }

    [Fact]
    public void DoubleShortenedLastName_NotAllowed()
    {
        Assert.Throws<ArgumentException>(() =>
        {
            var name = TeacherNameHelper.ParseName("First L.-Lastb");
            _ = name;
        });
    }
}

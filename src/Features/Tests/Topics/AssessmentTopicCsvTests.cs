using ScheduleLib.Application.Core.Topics;

namespace ScheduleLib.Topics.Tests;

public sealed class AssessmentTopicCsvTests
{
    [Fact]
    public async Task AssessmentRowsAreExcludedWithoutChangingRegularTopicOrder()
    {
        using var input =
            new StringReader(
                "name,lessonType\nSynthetic first,Curs\nAtestare 1,Curs\nSynthetic second,Curs\nAtestări,Curs\n");
        var names = new List<string>();
        await foreach (var topic in LessonTopicCsvSerializer.Deserialize(input,
                           new(),
                           CancellationToken.None))
        {
            names.Add(topic.Name);
        }

        Assert.Equal(expected: new[]
            {
                "Synthetic first",
                "Synthetic second",
            },
            actual: names);
    }
}

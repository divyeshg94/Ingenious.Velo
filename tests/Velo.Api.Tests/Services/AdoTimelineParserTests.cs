using FluentAssertions;
using Velo.Api.Services;
using Xunit;

namespace Velo.Api.Tests.Services;

public class AdoTimelineParserTests
{
    [Fact]
    public void ExtractStageNames_ReturnsNull_WhenJsonIsNull()
        => AdoTimelineParser.ExtractStageNames(null).Should().BeNull();

    [Fact]
    public void ExtractStageNames_ReturnsNull_WhenJsonIsEmpty()
        => AdoTimelineParser.ExtractStageNames("   ").Should().BeNull();

    [Fact]
    public void ExtractStageNames_ReturnsNull_WhenJsonIsMalformed()
        => AdoTimelineParser.ExtractStageNames("{not json}").Should().BeNull();

    [Fact]
    public void ExtractStageNames_ReturnsNull_WhenRecordsArrayMissing()
        => AdoTimelineParser.ExtractStageNames("{\"foo\":\"bar\"}").Should().BeNull();

    [Fact]
    public void ExtractStageNames_ReturnsNull_WhenNoStageTypeRecords()
    {
        var json = """
        {
          "records": [
            { "type": "Job", "name": "JobA", "order": 1 },
            { "type": "Task", "name": "TaskA", "order": 2 }
          ]
        }
        """;
        AdoTimelineParser.ExtractStageNames(json).Should().BeNull();
    }

    [Fact]
    public void ExtractStageNames_JoinsStagesInOrder()
    {
        var json = """
        {
          "records": [
            { "type": "Stage", "name": "Deploy to Prod", "order": 3 },
            { "type": "Stage", "name": "Build", "order": 1 },
            { "type": "Job",   "name": "Compile",        "order": 1 },
            { "type": "Stage", "name": "Deploy to UAT",  "order": 2 }
          ]
        }
        """;
        var result = AdoTimelineParser.ExtractStageNames(json);
        result.Should().Be("Build → Deploy to UAT → Deploy to Prod");
    }

    [Fact]
    public void ExtractStageNames_SkipsStagesWithMissingOrBlankName()
    {
        var json = """
        {
          "records": [
            { "type": "Stage", "name": "Build", "order": 1 },
            { "type": "Stage", "order": 2 },
            { "type": "Stage", "name": "   ", "order": 3 },
            { "type": "Stage", "name": "Deploy", "order": 4 }
          ]
        }
        """;
        AdoTimelineParser.ExtractStageNames(json).Should().Be("Build → Deploy");
    }

    [Fact]
    public void ExtractStageNames_TruncatesAt200Chars()
    {
        var longName = new string('A', 80);
        var json = $$"""
        {
          "records": [
            { "type": "Stage", "name": "{{longName}}", "order": 1 },
            { "type": "Stage", "name": "{{longName}}", "order": 2 },
            { "type": "Stage", "name": "{{longName}}", "order": 3 }
          ]
        }
        """;
        var result = AdoTimelineParser.ExtractStageNames(json);
        result.Should().NotBeNull();
        result!.Length.Should().Be(200);
    }

    [Fact]
    public void ExtractStageNames_TreatsStageTypeCaseInsensitively()
    {
        var json = """
        {
          "records": [
            { "type": "stage", "name": "Build", "order": 1 },
            { "type": "STAGE", "name": "Deploy", "order": 2 }
          ]
        }
        """;
        AdoTimelineParser.ExtractStageNames(json).Should().Be("Build → Deploy");
    }

    [Fact]
    public void ExtractStageNames_PutsRecordsWithoutOrderAtEnd()
    {
        var json = """
        {
          "records": [
            { "type": "Stage", "name": "NoOrderStage" },
            { "type": "Stage", "name": "First", "order": 1 }
          ]
        }
        """;
        AdoTimelineParser.ExtractStageNames(json).Should().Be("First → NoOrderStage");
    }
}

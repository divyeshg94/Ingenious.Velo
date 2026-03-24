using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Velo.Api.Controllers;
using Velo.Api.Interface;
using Velo.Shared.Models;

namespace Velo.Api.Tests.Controllers;

public class PipelinesControllerTests
{
    private readonly Mock<IPipelineService> _pipelineServiceMock = new();
    private readonly PipelinesController _sut;

    public PipelinesControllerTests()
    {
        _sut = new PipelinesController(_pipelineServiceMock.Object);
    }

    [Fact]
    public async Task GetPipelines_ReturnsOk_WithRuns()
    {
        var runs = new List<PipelineRunDto>
        {
            new() { Id = Guid.NewGuid(), PipelineName = "CI", Result = "succeeded" },
            new() { Id = Guid.NewGuid(), PipelineName = "Deploy", Result = "failed" }
        };
        _pipelineServiceMock
            .Setup(s => s.GetRunsAsync("proj1", 1, 50, It.IsAny<CancellationToken>()))
            .ReturnsAsync(runs);

        var result = await _sut.GetPipelines("proj1");

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().BeEquivalentTo(runs);
    }

    [Fact]
    public async Task GetPipelines_ReturnsOk_WithEmptyList_WhenNoRuns()
    {
        _pipelineServiceMock
            .Setup(s => s.GetRunsAsync("proj1", 1, 50, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PipelineRunDto>());

        var result = await _sut.GetPipelines("proj1");

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.As<IEnumerable<PipelineRunDto>>().Should().BeEmpty();
    }

    [Fact]
    public async Task GetPipelines_PropagatesException_WhenServiceThrows()
    {
        _pipelineServiceMock
            .Setup(s => s.GetRunsAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Service error"));

        var act = async () => await _sut.GetPipelines("proj1");

        await act.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task GetPipelines_UsesDefaultPageAndPageSize()
    {
        int capturedPage = 0, capturedPageSize = 0;
        _pipelineServiceMock
            .Setup(s => s.GetRunsAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback<string, int, int, CancellationToken>((_, p, ps, _) => { capturedPage = p; capturedPageSize = ps; })
            .ReturnsAsync(new List<PipelineRunDto>());

        await _sut.GetPipelines("proj1");

        capturedPage.Should().Be(1);
        capturedPageSize.Should().Be(50);
    }

    [Fact]
    public async Task GetPipelines_ForwardsCustomPageAndPageSize()
    {
        int capturedPage = 0, capturedPageSize = 0;
        _pipelineServiceMock
            .Setup(s => s.GetRunsAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback<string, int, int, CancellationToken>((_, p, ps, _) => { capturedPage = p; capturedPageSize = ps; })
            .ReturnsAsync(new List<PipelineRunDto>());

        await _sut.GetPipelines("proj1", page: 3, pageSize: 25);

        capturedPage.Should().Be(3);
        capturedPageSize.Should().Be(25);
    }
}

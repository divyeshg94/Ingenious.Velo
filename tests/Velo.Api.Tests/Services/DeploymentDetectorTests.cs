using FluentAssertions;
using Velo.Api.Services;

namespace Velo.Api.Tests.Services;

public class DeploymentDetectorTests
{
    [Theory]
    [InlineData("Deploy to UAT")]
    [InlineData("deploy-prod")]
    [InlineData("Release Train")]
    [InlineData("ci-release")]
    [InlineData("Prod Push")]
    [InlineData("production-rollout")]
    [InlineData("Publish Package")]
    [InlineData("publish-website")]
    [InlineData("Canary Rollout")]
    [InlineData("Canary Deploy")]
    [InlineData("Promote to staging")]
    [InlineData("API-CD")]           // whole-word "cd"
    [InlineData("CD pipeline")]      // whole-word "cd"
    [InlineData("backend cd")]       // whole-word "cd"
    public void IsDeployment_ReturnsTrue_ForKnownDeploymentKeywords(string pipelineName)
    {
        DeploymentDetector.IsDeployment(pipelineName).Should().BeTrue(
            $"'{pipelineName}' should be classified as a deployment pipeline");
    }

    [Theory]
    [InlineData("Build")]
    [InlineData("CI")]
    [InlineData("Unit Tests")]
    [InlineData("lint-and-format")]
    [InlineData("code-coverage")]    // contains "code" but not "cd" as whole word
    [InlineData("ascend-pipeline")]  // contains "cd" inside "ascend" but not as whole word
    [InlineData("encoded-data")]     // contains "cd" inside "encoded" but not as whole word
    [InlineData("scd")]              // "cd" inside "scd" but not whole word
    [InlineData("cdc")]              // "cd" inside "cdc" but not whole word
    public void IsDeployment_ReturnsFalse_ForNonDeploymentNames(string pipelineName)
    {
        DeploymentDetector.IsDeployment(pipelineName).Should().BeFalse(
            $"'{pipelineName}' should not be classified as a deployment pipeline");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void IsDeployment_ReturnsFalse_ForNullOrWhitespace(string? pipelineName)
    {
        DeploymentDetector.IsDeployment(pipelineName).Should().BeFalse();
    }

    [Fact]
    public void IsDeployment_UsesStageName_WhenPipelineNameIsCI()
    {
        // Common pattern: a build pipeline whose later stages deploy.
        DeploymentDetector.IsDeployment("ci-build", "Deploy to Prod").Should().BeTrue();
    }

    [Fact]
    public void IsDeployment_IgnoresStageName_WhenPipelineNameAlreadyMatches()
    {
        DeploymentDetector.IsDeployment("Deploy Production", stageName: null).Should().BeTrue();
    }

    [Fact]
    public void IsDeployment_IsCaseInsensitive()
    {
        DeploymentDetector.IsDeployment("DEPLOY-PRODUCTION").Should().BeTrue();
        DeploymentDetector.IsDeployment("rELeAsE").Should().BeTrue();
    }

    [Fact]
    public void IsDeployment_DoesNotFalseMatch_OnSubstring()
    {
        // None of these contain any deployment keyword as a substring match.
        DeploymentDetector.IsDeployment("frontend-build").Should().BeFalse();
        DeploymentDetector.IsDeployment("schema-migration-tests").Should().BeFalse();
    }
}

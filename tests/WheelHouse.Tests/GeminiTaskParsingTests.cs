using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using WheelHouse.Core;
using WheelHouse.Infrastructure.Agents;
using Xunit;

namespace WheelHouse.Tests;

public class GeminiTaskParsingTests
{
    private class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly string _responseJson;

        public MockHttpMessageHandler(string responseJson)
        {
            _responseJson = responseJson;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_responseJson)
            };
            return Task.FromResult(response);
        }
    }

    [Fact]
    public async Task GenerateTasksAsync_Parses_Risk_And_SkillTags_Correctly()
    {
        // Arrange
        var mockResponse = new
        {
            candidates = new[]
            {
                new
                {
                    content = new
                    {
                        parts = new[]
                        {
                            new
                            {
                                text = @"
[
  {
    ""title"": ""Task 1"",
    ""description"": ""Description 1"",
    ""verificationCommand"": ""dotnet build"",
    ""risk"": ""High"",
    ""skillTags"": [""csharp"", ""database""]
  },
  {
    ""title"": ""Task 2"",
    ""description"": ""Description 2"",
    ""verificationCommand"": null,
    ""risk"": ""Medium"",
    ""skillTags"": [""typescript""]
  },
  {
    ""title"": ""Task 3"",
    ""description"": ""Description 3"",
    ""verificationCommand"": ""dotnet test"",
    ""risk"": ""Low"",
    ""skillTags"": []
  }
]"
                            }
                        }
                    }
                }
            }
        };

        var json = JsonSerializer.Serialize(mockResponse);
        var handler = new MockHttpMessageHandler(json);
        var httpClient = new HttpClient(handler);

        var options = new GeminiOptions { ApiKey = "fake-key" };
        var service = new GeminiService(httpClient, options, NullLogger<GeminiService>.Instance);

        // Act
        var tasks = await service.GenerateTasksAsync("Some dummy plan");

        // Assert
        Assert.Equal(3, tasks.Count);

        Assert.Equal("Task 1", tasks[0].Title);
        Assert.Equal("Description 1", tasks[0].Description);
        Assert.Equal("dotnet build", tasks[0].VerificationCommand);
        Assert.Equal(RiskLevel.High, tasks[0].Risk);
        Assert.Equal("csharp,database", tasks[0].SkillTags);

        Assert.Equal("Task 2", tasks[1].Title);
        Assert.Equal("Description 2", tasks[1].Description);
        Assert.Null(tasks[1].VerificationCommand);
        Assert.Equal(RiskLevel.Medium, tasks[1].Risk);
        Assert.Equal("typescript", tasks[1].SkillTags);

        Assert.Equal("Task 3", tasks[2].Title);
        Assert.Equal("Description 3", tasks[2].Description);
        Assert.Equal("dotnet test", tasks[2].VerificationCommand);
        Assert.Equal(RiskLevel.Low, tasks[2].Risk);
        Assert.Null(tasks[2].SkillTags);
    }
}

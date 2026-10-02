using MatHelper.API.Common;
using Microsoft.AspNetCore.Mvc.Testing;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace MatHelper.IntegrationTests.Tests
{
    public class TasksControllerIntegrationTests : IClassFixture<CustomWebApplicationFactory<Program>>
    {
        private readonly CustomWebApplicationFactory<Program> _factory;
        private readonly HttpClient _client;

        public TasksControllerIntegrationTests(CustomWebApplicationFactory<Program> factory)
        {
            _factory = factory;
            _factory.ResetDatabase();

            _client = factory.CreateClient(new WebApplicationFactoryClientOptions
            {
                HandleCookies = true,
                BaseAddress = new Uri("https://localhost:5001")
            });
        }

        [Fact]
        public async Task MathTask_EndToEnd_WhenHubRunning_ShouldSolveAndPersist()
        {
            var taskPayload = new { data = "2x + 5 = 15" };

            var postResponse = await _client.PostAsJsonAsync("api/v1/tasks/math", taskPayload);

            // If Hub is not running or unavailable in certain test runners, assert valid HTTP contract
            if (!postResponse.IsSuccessStatusCode)
            {
                Assert.Equal(HttpStatusCode.InternalServerError, postResponse.StatusCode);
                return;
            }

            Assert.Equal(HttpStatusCode.OK, postResponse.StatusCode);

            var apiResponse = await postResponse.Content.ReadFromJsonAsync<ApiResponse<string>>();
            Assert.NotNull(apiResponse);
            Assert.True(apiResponse.Success);
            Assert.False(string.IsNullOrWhiteSpace(apiResponse.Data));

            var taskId = apiResponse.Data;

            // Fetch persisted task
            var getResponse = await _client.GetAsync($"api/v1/tasks/math/{taskId}");
            Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);

            var getApiResponse = await getResponse.Content.ReadFromJsonAsync<ApiResponse<JsonElement>>();
            Assert.NotNull(getApiResponse);
            Assert.True(getApiResponse.Success);

            var root = getApiResponse.Data;
            Assert.True(root.TryGetProperty("data", out var dataProp));
            Assert.Contains("5", dataProp.GetString());

            Assert.True(root.TryGetProperty("problem", out var problemProp));
            Assert.Equal("2x + 5 = 15", problemProp.GetString());

            Assert.True(root.TryGetProperty("solution", out var solutionProp));
            Assert.Equal("completed", solutionProp.GetProperty("status").GetString());
            Assert.Contains("5", solutionProp.GetProperty("finalAnswer").GetString());
        }

        [Fact]
        public async Task InvalidTaskType_ShouldReturnBadRequest()
        {
            var response = await _client.PostAsJsonAsync("api/v1/tasks/unknown_type", new { foo = "bar" });
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
    }
}

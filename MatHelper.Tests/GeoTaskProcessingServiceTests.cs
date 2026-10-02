using Grpc.Core;
using MatHelper.BLL.Services;
using MatHelper.CORE.Enums;
using MatHelper.DAL.Interfaces;
using MatHelper.DAL.Models;
using Microsoft.Extensions.Logging;
using Moq;
using SolutionHub;
using System.Text.Json;
using Xunit;

namespace MatHelper.Tests
{
    public class GeoTaskProcessingServiceTests
    {
        private readonly Mock<ITaskRequestRepository> _taskRequestRepoMock = new();
        private readonly Mock<ITaskRatingRepository> _taskRatingRepoMock = new();
        private readonly Mock<ILogger<GeoTaskProcessingService>> _loggerMock = new();
        private readonly Mock<SolutionHub.SolutionHub.SolutionHubClient> _solutionHubMock = new();
        private readonly GeoTaskProcessingService _service;

        public GeoTaskProcessingServiceTests()
        {
            _service = new GeoTaskProcessingService(
                _solutionHubMock.Object,
                _taskRequestRepoMock.Object,
                _taskRatingRepoMock.Object,
                _loggerMock.Object
            );
        }

        private static AsyncUnaryCall<SolveProblemResponse> CreateGrpcCall(SolveProblemResponse response)
        {
            return new AsyncUnaryCall<SolveProblemResponse>(
                Task.FromResult(response),
                Task.FromResult(new Metadata()),
                () => Status.DefaultSuccess,
                () => new Metadata(),
                () => { });
        }

        private static string ValidGeometryResultJson() =>
            JsonSerializer.Serialize(new
            {
                problemType = "geometry",
                status = "completed",
                problemStatement = "Given triangle ABC with AB = BC = 5, AC = 6",
                inputFacts = new
                {
                    figures = new[]
                    {
                        new { id = "triangle_1", type = "triangle", vertices = new[] { "A", "B", "C" } }
                    },
                    lengths = new Dictionary<string, double> { ["AB"] = 5.0, ["BC"] = 5.0, ["AC"] = 6.0 },
                    angles = new Dictionary<string, double>()
                },
                target = new
                {
                    descriptions = new[] { "Altitude BH", "Area S" },
                    variables = new[] { "BH", "S" }
                },
                derivedFacts = new
                {
                    auxiliaryConstructions = new[]
                    {
                        new { type = "altitude", label = "BH", fromVertex = "B", toSegment = "AC", footPoint = "H" }
                    },
                    lengths = new Dictionary<string, double> { ["BH"] = 4.0 },
                    angles = new Dictionary<string, double>(),
                    metrics = new Dictionary<string, double> { ["area"] = 12.0, ["perimeter"] = 16.0 }
                },
                steps = new[]
                {
                    new
                    {
                        stepNumber = 1,
                        title = "Pythagorean Theorem",
                        explanation = "In right triangle ABH, BH = sqrt(AB^2 - AH^2)",
                        latexFormula = "BH = 4"
                    }
                },
                finalAnswer = "Altitude BH = 4, Area S = 12, Perimeter P = 16",
                latexAnswer = "BH = 4,\\; S = 12"
            });

        [Fact]
        public async Task CanProcessRequestAsync_ShouldAllow_WhenUserIdProvided()
        {
            var result = await _service.CanProcessRequestAsync("127.0.0.1", Guid.NewGuid());

            Assert.True(result.Allowed);
            Assert.Null(result.RetryAfter);
        }

        [Fact]
        public async Task CanProcessRequestAsync_ShouldAllow_WhenNoLogs()
        {
            _taskRequestRepoMock.Setup(r => r.GetLastRequestByIpAsync(It.IsAny<string>(), SubjectType.Geometry))
                .ReturnsAsync((TaskRequestLog)null!);

            var result = await _service.CanProcessRequestAsync("127.0.0.1", null);

            Assert.True(result.Allowed);
        }

        [Fact]
        public async Task CanProcessRequestAsync_ShouldAllow_WhenMoreThan24h()
        {
            _taskRequestRepoMock.Setup(r => r.GetLastRequestByIpAsync(It.IsAny<string>(), SubjectType.Geometry))
                .ReturnsAsync(new TaskRequestLog { RequestTime = DateTime.UtcNow.AddHours(-25), TaskId = new Guid().ToString(), Subject = "Geometry" });

            var result = await _service.CanProcessRequestAsync("127.0.0.1", null);

            Assert.True(result.Allowed);
        }

        [Fact]
        public async Task CanProcessRequestAsync_ShouldBlock_WhenLessThan24h()
        {
            _taskRequestRepoMock.Setup(r => r.GetLastRequestByIpAsync(It.IsAny<string>(), SubjectType.Geometry))
                .ReturnsAsync(new TaskRequestLog { RequestTime = DateTime.UtcNow, TaskId = new Guid().ToString(), Subject = "Geometry" });

            var result = await _service.CanProcessRequestAsync("127.0.0.1", null);

            Assert.False(result.Allowed);
            Assert.NotNull(result.RetryAfter);
        }

        [Fact]
        public async Task ProcessTaskAsync_Success_ShouldCallHub_AndPersistStructuredResult()
        {
            SolveProblemRequest? capturedRequest = null;
            _solutionHubMock
                .Setup(s => s.SolveProblemAsync(It.IsAny<SolveProblemRequest>(), null, null, default))
                .Callback<SolveProblemRequest, Metadata, DateTime?, CancellationToken>((req, _, _, _) => capturedRequest = req)
                .Returns(CreateGrpcCall(new SolveProblemResponse
                {
                    TaskId = "geo-task-1",
                    Status = "completed",
                    Result = ValidGeometryResultJson(),
                    Success = true
                }));

            var userId = Guid.NewGuid();
            var canvasJson = """
            {
                "triangle_1": {
                    "points": [{"label": "A"}, {"label": "B"}, {"label": "C"}],
                    "lines": {"AB": 5.0, "BC": 5.0, "AC": 6.0},
                    "angles": {}
                }
            }
            """;
            var json = JsonDocument.Parse(canvasJson).RootElement;

            var taskId = await _service.ProcessTaskAsync(json, "127.0.0.1", userId);

            Assert.False(string.IsNullOrEmpty(taskId));

            // Verify gRPC call parameters
            _solutionHubMock.Verify(s => s.SolveProblemAsync(It.IsAny<SolveProblemRequest>(), null, null, default), Times.Once);
            Assert.NotNull(capturedRequest);
            Assert.Equal(taskId, capturedRequest.TaskId);
            Assert.Equal("geometry", capturedRequest.ProblemType);
            Assert.Equal(userId.ToString(), capturedRequest.UserId);

            // Payload assertion: deserialize payload and verify geometric facts are preserved
            using var payloadDoc = JsonDocument.Parse(capturedRequest.Payload);
            var payloadRoot = payloadDoc.RootElement;
            Assert.True(payloadRoot.TryGetProperty("triangle_1", out var triProp));
            Assert.True(triProp.TryGetProperty("lines", out var linesProp));
            Assert.Equal(5.0, linesProp.GetProperty("AB").GetDouble());
            Assert.Equal(6.0, linesProp.GetProperty("AC").GetDouble());

            // Verify file persistence
            var filePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "Tasks", "Geo", $"{taskId}.json");
            Assert.True(File.Exists(filePath));

            var content = await File.ReadAllTextAsync(filePath);
            using var fileDoc = JsonDocument.Parse(content);
            var root = fileDoc.RootElement;

            // Original task canvas remains intact
            Assert.True(root.TryGetProperty("task", out var savedTask));
            Assert.True(savedTask.TryGetProperty("triangle_1", out _));

            // Given section is preserved
            Assert.True(root.TryGetProperty("given", out var savedGiven));
            Assert.Contains("triangle", savedGiven.GetString() ?? "");

            // Structured solution is saved
            Assert.True(root.TryGetProperty("solution", out var savedSolution));
            Assert.Equal("completed", savedSolution.GetProperty("status").GetString());

            // Answer is extracted from finalAnswer
            Assert.True(root.TryGetProperty("answer", out var savedAnswer));
            Assert.Equal("Altitude BH = 4, Area S = 12, Perimeter P = 16", savedAnswer.GetString());

            _taskRequestRepoMock.Verify(r => r.AddRequestAsync(It.Is<TaskRequestLog>(t => t.TaskId == taskId)), Times.Once);

            File.Delete(filePath);
        }

        [Fact]
        public async Task ProcessTaskAsync_HubFails_ShouldThrow_AndNotPersistFile()
        {
            _solutionHubMock
                .Setup(s => s.SolveProblemAsync(It.IsAny<SolveProblemRequest>(), null, null, default))
                .Returns(CreateGrpcCall(new SolveProblemResponse
                {
                    TaskId = "fail-task",
                    Status = "error",
                    Result = "",
                    Success = false
                }));

            var json = JsonDocument.Parse("{\"triangle_1\": {}}").RootElement;

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                _service.ProcessTaskAsync(json, "127.0.0.1", null));

            _taskRequestRepoMock.Verify(r => r.AddRequestAsync(It.IsAny<TaskRequestLog>()), Times.Never);
        }

        [Fact]
        public async Task ProcessTaskAsync_EmptyResult_ShouldThrow()
        {
            _solutionHubMock
                .Setup(s => s.SolveProblemAsync(It.IsAny<SolveProblemRequest>(), null, null, default))
                .Returns(CreateGrpcCall(new SolveProblemResponse
                {
                    TaskId = "empty-task",
                    Status = "completed",
                    Result = "   ",
                    Success = true
                }));

            var json = JsonDocument.Parse("{\"triangle_1\": {}}").RootElement;

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                _service.ProcessTaskAsync(json, "127.0.0.1", null));
        }

        [Fact]
        public async Task ProcessTaskAsync_InvalidJsonResult_ShouldThrow()
        {
            _solutionHubMock
                .Setup(s => s.SolveProblemAsync(It.IsAny<SolveProblemRequest>(), null, null, default))
                .Returns(CreateGrpcCall(new SolveProblemResponse
                {
                    TaskId = "invalid-task",
                    Status = "completed",
                    Result = "malformed response",
                    Success = true
                }));

            var json = JsonDocument.Parse("{\"triangle_1\": {}}").RootElement;

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                _service.ProcessTaskAsync(json, "127.0.0.1", null));
        }

        [Fact]
        public async Task ProcessTaskAsync_GrpcThrows_ShouldThrow()
        {
            _solutionHubMock
                .Setup(s => s.SolveProblemAsync(It.IsAny<SolveProblemRequest>(), null, null, default))
                .Throws(new RpcException(new Status(StatusCode.DeadlineExceeded, "Request timed out")));

            var json = JsonDocument.Parse("{\"triangle_1\": {}}").RootElement;

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                _service.ProcessTaskAsync(json, "127.0.0.1", null));

            Assert.Contains("SolutionHub", ex.Message);
        }

        [Fact]
        public async Task GetTaskAsync_ShouldThrow_WhenFileNotFound()
        {
            await Assert.ThrowsAsync<FileNotFoundException>(() => _service.GetTaskAsync("nonexistent"));
        }

        [Fact]
        public async Task GetTaskAsync_ShouldReturnJson_WhenFileExists()
        {
            var taskId = Guid.NewGuid().ToString();
            var folder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "Tasks", "Geo");
            Directory.CreateDirectory(folder);
            var filePath = Path.Combine(folder, $"{taskId}.json");
            await File.WriteAllTextAsync(filePath, "{\"test\":123}");

            var json = await _service.GetTaskAsync(taskId);

            Assert.Equal(123, json.GetProperty("test").GetInt32());

            File.Delete(filePath);
        }

        [Fact]
        public async Task RateTaskAsync_ShouldSaveRating()
        {
            await _service.RateTaskAsync("task1", true, Guid.NewGuid());

            _taskRatingRepoMock.Verify(r => r.AddRatingAsync(It.Is<TaskRating>(tr => tr.TaskId == "task1" && tr.IsCorrect)), Times.Once);
        }

        [Fact]
        public async Task GetTaskCreatorUserIdAsync_ShouldReturnUserId_WhenFound()
        {
            _taskRequestRepoMock.Setup(r => r.GetRequestByTaskIdAsync("task1"))
                .ReturnsAsync(new TaskRequestLog { TaskId = "task1", UserId = "123", Subject = "Geometry" });

            var userId = await _service.GetTaskCreatorUserIdAsync("task1");

            Assert.Equal("123", userId);
        }

        [Fact]
        public async Task GetTaskCreatorUserIdAsync_ShouldReturnNull_WhenNotFound()
        {
            _taskRequestRepoMock.Setup(r => r.GetRequestByTaskIdAsync("task1"))
                .ReturnsAsync((TaskRequestLog)null!);

            var userId = await _service.GetTaskCreatorUserIdAsync("task1");

            Assert.Null(userId);
        }
    }
}

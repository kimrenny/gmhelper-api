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

namespace MatHelper.Tests.BLL
{
    public class MathTaskProcessingServiceTests
    {
        private readonly Mock<SolutionHub.SolutionHub.SolutionHubClient> _solutionHubMock = new();
        private readonly Mock<ITaskRequestRepository> _taskRequestRepositoryMock;
        private readonly Mock<ITaskRatingRepository> _taskRatingRepositoryMock;
        private readonly Mock<ILogger<MathTaskProcessingService>> _loggerMock;
        private readonly MathTaskProcessingService _service;

        public MathTaskProcessingServiceTests()
        {
            _taskRequestRepositoryMock = new Mock<ITaskRequestRepository>();
            _taskRatingRepositoryMock = new Mock<ITaskRatingRepository>();
            _loggerMock = new Mock<ILogger<MathTaskProcessingService>>();
            _service = new MathTaskProcessingService(
                _solutionHubMock.Object,
                _taskRequestRepositoryMock.Object,
                _taskRatingRepositoryMock.Object,
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

        private static string ValidMathResultJson() =>
            JsonSerializer.Serialize(new
            {
                problemType = "math",
                status = "completed",
                problem = "2x + 5 = 15",
                latexProblem = "2x + 5 = 15",
                steps = new[]
                {
                    new
                    {
                        stepNumber = 1,
                        title = "Subtract 5",
                        explanation = "Subtract 5 from both sides",
                        latexFormula = "2x = 10"
                    }
                },
                finalAnswer = "x = 5",
                latexAnswer = "x = 5",
                compositeLatex = "2x + 5 = 15 \\\\ 2x = 10 \\\\ x = 5"
            });

        [Fact]
        public async Task CanProcessRequestAsync_UserLoggedIn_ShouldAllow()
        {
            var result = await _service.CanProcessRequestAsync("127.0.0.1", Guid.NewGuid());
            Assert.True(result.Allowed);
            Assert.Null(result.RetryAfter);
        }

        [Fact]
        public async Task CanProcessRequestAsync_NoPreviousRequest_ShouldAllow()
        {
            _taskRequestRepositoryMock
                .Setup(r => r.GetLastRequestByIpAsync("127.0.0.1", SubjectType.Math))
                .ReturnsAsync((TaskRequestLog?)null);

            var result = await _service.CanProcessRequestAsync("127.0.0.1", null);

            Assert.True(result.Allowed);
            Assert.Null(result.RetryAfter);
        }

        [Fact]
        public async Task CanProcessRequestAsync_LastRequestOlderThan24h_ShouldAllow()
        {
            _taskRequestRepositoryMock
                .Setup(r => r.GetLastRequestByIpAsync("127.0.0.1", SubjectType.Math))
                .ReturnsAsync(new TaskRequestLog { RequestTime = DateTime.UtcNow.AddHours(-25), TaskId = new Guid().ToString(), Subject = "Math" });

            var result = await _service.CanProcessRequestAsync("127.0.0.1", null);

            Assert.True(result.Allowed);
            Assert.Null(result.RetryAfter);
        }

        [Fact]
        public async Task CanProcessRequestAsync_LastRequestWithin24h_ShouldBlock()
        {
            _taskRequestRepositoryMock
                .Setup(r => r.GetLastRequestByIpAsync("127.0.0.1", SubjectType.Math))
                .ReturnsAsync(new TaskRequestLog { RequestTime = DateTime.UtcNow.AddHours(-1), TaskId = new Guid().ToString(), Subject = "Math" });

            var result = await _service.CanProcessRequestAsync("127.0.0.1", null);

            Assert.False(result.Allowed);
            Assert.NotNull(result.RetryAfter);
            Assert.True(result.RetryAfter.Value.TotalHours <= 24);
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
                    TaskId = "ignored",
                    Status = "completed",
                    Result = ValidMathResultJson(),
                    Success = true
                }));

            var userId = Guid.NewGuid();
            var json = JsonDocument.Parse("{\"data\": \"2x + 5 = 15\"}").RootElement;

            var taskId = await _service.ProcessTaskAsync(json, "127.0.0.1", userId);

            Assert.False(string.IsNullOrWhiteSpace(taskId));

            // Verify gRPC call parameters
            _solutionHubMock.Verify(s => s.SolveProblemAsync(It.IsAny<SolveProblemRequest>(), null, null, default), Times.Once);
            Assert.NotNull(capturedRequest);
            Assert.Equal(taskId, capturedRequest.TaskId);
            Assert.Equal("math", capturedRequest.ProblemType);
            Assert.Equal(userId.ToString(), capturedRequest.UserId);
            Assert.Contains("2x + 5 = 15", capturedRequest.Payload);

            // Verify file persistence
            var filePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "Tasks", "Math", $"{taskId}.json");
            Assert.True(File.Exists(filePath));

            var content = await File.ReadAllTextAsync(filePath);
            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;

            // 'data' must contain compositeLatex for KaTeX rendering
            Assert.True(root.TryGetProperty("data", out var dataProp));
            Assert.Equal("2x + 5 = 15 \\\\ 2x = 10 \\\\ x = 5", dataProp.GetString());

            // 'problem' must contain the original equation
            Assert.True(root.TryGetProperty("problem", out var problemProp));
            Assert.Equal("2x + 5 = 15", problemProp.GetString());

            // Structured solution must be present and contain complete Hub result
            Assert.True(root.TryGetProperty("solution", out var solutionProp));
            Assert.Equal("completed", solutionProp.GetProperty("status").GetString());
            Assert.Equal("x = 5", solutionProp.GetProperty("finalAnswer").GetString());
            Assert.Equal("x = 5", solutionProp.GetProperty("latexAnswer").GetString());
            Assert.Equal("2x + 5 = 15 \\\\ 2x = 10 \\\\ x = 5", solutionProp.GetProperty("compositeLatex").GetString());

            _taskRequestRepositoryMock.Verify(r => r.AddRequestAsync(It.IsAny<TaskRequestLog>()), Times.Once);

            File.Delete(filePath);
        }

        [Fact]
        public async Task ProcessTaskAsync_MissingCompositeLatex_ShouldThrow_AndNotPersist()
        {
            var resultWithoutCompositeLatex = JsonSerializer.Serialize(new
            {
                problemType = "math",
                status = "completed",
                problem = "2x + 5 = 15",
                latexProblem = "2x + 5 = 15",
                steps = Array.Empty<object>(),
                finalAnswer = "x = 5",
                latexAnswer = "x = 5"
            });

            _solutionHubMock
                .Setup(s => s.SolveProblemAsync(It.IsAny<SolveProblemRequest>(), null, null, default))
                .Returns(CreateGrpcCall(new SolveProblemResponse
                {
                    TaskId = "missing-latex-task",
                    Status = "completed",
                    Result = resultWithoutCompositeLatex,
                    Success = true
                }));

            var json = JsonDocument.Parse("{\"data\": \"2x + 5 = 15\"}").RootElement;

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                _service.ProcessTaskAsync(json, "127.0.0.1", null));

            Assert.Contains("compositeLatex", ex.Message);
            _taskRequestRepositoryMock.Verify(r => r.AddRequestAsync(It.IsAny<TaskRequestLog>()), Times.Never);
        }

        [Fact]
        public async Task ProcessTaskAsync_EmptyCompositeLatex_ShouldThrow_AndNotPersist()
        {
            var resultWithEmptyCompositeLatex = JsonSerializer.Serialize(new
            {
                problemType = "math",
                status = "completed",
                problem = "2x + 5 = 15",
                latexProblem = "2x + 5 = 15",
                steps = Array.Empty<object>(),
                finalAnswer = "x = 5",
                latexAnswer = "x = 5",
                compositeLatex = "   "
            });

            _solutionHubMock
                .Setup(s => s.SolveProblemAsync(It.IsAny<SolveProblemRequest>(), null, null, default))
                .Returns(CreateGrpcCall(new SolveProblemResponse
                {
                    TaskId = "empty-latex-task",
                    Status = "completed",
                    Result = resultWithEmptyCompositeLatex,
                    Success = true
                }));

            var json = JsonDocument.Parse("{\"data\": \"2x + 5 = 15\"}").RootElement;

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                _service.ProcessTaskAsync(json, "127.0.0.1", null));

            Assert.Contains("compositeLatex", ex.Message);
            _taskRequestRepositoryMock.Verify(r => r.AddRequestAsync(It.IsAny<TaskRequestLog>()), Times.Never);
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

            var json = JsonDocument.Parse("{\"data\": \"bad problem\"}").RootElement;

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                _service.ProcessTaskAsync(json, "127.0.0.1", null));

            _taskRequestRepositoryMock.Verify(r => r.AddRequestAsync(It.IsAny<TaskRequestLog>()), Times.Never);
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

            var json = JsonDocument.Parse("{\"data\": \"2+2\"}").RootElement;

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
                    TaskId = "invalid-json-task",
                    Status = "completed",
                    Result = "not a valid json object",
                    Success = true
                }));

            var json = JsonDocument.Parse("{\"data\": \"2+2\"}").RootElement;

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                _service.ProcessTaskAsync(json, "127.0.0.1", null));
        }

        [Fact]
        public async Task ProcessTaskAsync_GrpcThrows_ShouldThrow()
        {
            _solutionHubMock
                .Setup(s => s.SolveProblemAsync(It.IsAny<SolveProblemRequest>(), null, null, default))
                .Throws(new RpcException(new Status(StatusCode.Unavailable, "Hub is unreachable")));

            var json = JsonDocument.Parse("{\"data\": \"2+2\"}").RootElement;

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                _service.ProcessTaskAsync(json, "127.0.0.1", null));

            Assert.Contains("SolutionHub", ex.Message);
        }

        [Fact]
        public async Task GetTaskAsync_ShouldReturnTask()
        {
            var json = "{\"number\": 42}";
            var id = Guid.NewGuid().ToString();
            var folderPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "Tasks", "Math");
            Directory.CreateDirectory(folderPath);
            var filePath = Path.Combine(folderPath, $"{id}.json");
            await File.WriteAllTextAsync(filePath, json);

            var result = await _service.GetTaskAsync(id);

            Assert.Equal(42, result.GetProperty("number").GetInt32());

            File.Delete(filePath);
        }

        [Fact]
        public async Task GetTaskAsync_ShouldThrow_WhenFileNotFound()
        {
            await Assert.ThrowsAsync<FileNotFoundException>(() => _service.GetTaskAsync("nonexistent"));
        }

        [Fact]
        public async Task RateTaskAsync_ShouldAddRating()
        {
            await _service.RateTaskAsync("task123", true, Guid.NewGuid());
            _taskRatingRepositoryMock.Verify(r => r.AddRatingAsync(It.IsAny<TaskRating>()), Times.Once);
        }

        [Fact]
        public async Task GetTaskCreatorUserIdAsync_ShouldReturnUserId()
        {
            var expectedUserId = Guid.NewGuid().ToString();
            _taskRequestRepositoryMock
                .Setup(r => r.GetRequestByTaskIdAsync("task123"))
                .ReturnsAsync(new TaskRequestLog { UserId = expectedUserId, TaskId = new Guid().ToString(), Subject = "Math" });

            var result = await _service.GetTaskCreatorUserIdAsync("task123");

            Assert.Equal(expectedUserId, result);
        }
    }
}

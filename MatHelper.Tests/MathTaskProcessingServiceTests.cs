using MatHelper.BLL.Services;
using MatHelper.DAL.Interfaces;
using MatHelper.DAL.Models;
using Microsoft.Extensions.Logging;
using SolutionHub;
using System.Text.Json;
using MatHelper.CORE.Enums;
using Xunit;
using Moq;

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
                .ReturnsAsync(new TaskRequestLog {RequestTime = DateTime.UtcNow.AddHours(-25), TaskId = new Guid().ToString(), Subject = "Math" });

            var result = await _service.CanProcessRequestAsync("127.0.0.1", null);

            Assert.True(result.Allowed);
            Assert.Null(result.RetryAfter);
        }

        [Fact]
        public async Task CanProcessRequestAsync_LastRequestWithin24h_ShouldBlock()
        {
            _taskRequestRepositoryMock
                .Setup(r => r.GetLastRequestByIpAsync("127.0.0.1", SubjectType.Math))
                .ReturnsAsync(new TaskRequestLog {RequestTime = DateTime.UtcNow.AddHours(-1), TaskId = new Guid().ToString(), Subject = "Math" });

            var result = await _service.CanProcessRequestAsync("127.0.0.1", null);

            Assert.False(result.Allowed);
            Assert.NotNull(result.RetryAfter);
            Assert.True(result.RetryAfter.Value.TotalHours <= 24);
        }

        [Fact]
        public async Task ProcessTaskAsync_ShouldCreateFile_AndAddRequestLog()
        {
            var json = JsonDocument.Parse("{\"example\": 123}").RootElement;

            var taskId = await _service.ProcessTaskAsync(json, "127.0.0.1", Guid.NewGuid());

            Assert.False(string.IsNullOrWhiteSpace(taskId));
            var filePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "Tasks", "Math", $"{taskId}.json");
            Assert.True(File.Exists(filePath));

            var content = await File.ReadAllTextAsync(filePath);
            Assert.Contains("example", content);

            _taskRequestRepositoryMock.Verify(r => r.AddRequestAsync(It.IsAny<TaskRequestLog>()), Times.Once);

            File.Delete(filePath);
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
                .ReturnsAsync(new TaskRequestLog {UserId = expectedUserId, TaskId = new Guid().ToString(), Subject = "Math" });

            var result = await _service.GetTaskCreatorUserIdAsync("task123");

            Assert.Equal(expectedUserId, result);
        }
    }
}

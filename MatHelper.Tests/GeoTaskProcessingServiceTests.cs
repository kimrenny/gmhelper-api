using MatHelper.BLL.Services;
using MatHelper.CORE.Enums;
using MatHelper.CORE.Models;
using MatHelper.DAL.Interfaces;
using MatHelper.DAL.Models;
using SolutionHub;
using Microsoft.Extensions.Logging;
using Moq;
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
        public async Task ProcessTaskAsync_ShouldSaveFile_AndLogRequest()
        {
            var json = JsonDocument.Parse("{\"rectangle_1\": {}}").RootElement;

            var taskId = await _service.ProcessTaskAsync(json, "127.0.0.1", null);

            Assert.False(string.IsNullOrEmpty(taskId));
            _taskRequestRepoMock.Verify(r => r.AddRequestAsync(It.Is<TaskRequestLog>(t => t.TaskId == taskId)), Times.Once);

            var filePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "Tasks", "Geo", $"{taskId}.json");
            Assert.True(File.Exists(filePath));

            File.Delete(filePath);
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

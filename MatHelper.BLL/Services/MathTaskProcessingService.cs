using MatHelper.DAL.Models;
using MatHelper.DAL.Interfaces;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using MatHelper.CORE.Enums;
using MatHelper.BLL.Interfaces;
using Grpc.Net.Client;
using SolutionHub;

namespace MatHelper.BLL.Services
{
    public class MathTaskProcessingService : IMathTaskProcessingService
    {
        private readonly SolutionHub.SolutionHub.SolutionHubClient _solutionHubClient;

        private readonly ITaskRequestRepository _taskRequestRepository;
        private readonly ITaskRatingRepository _taskRatingRepository;
        private readonly ILogger<MathTaskProcessingService> _logger;

        private const ushort AnonymousRequestLimitHours = 24;

        private const string WebRootFolderName = "wwwroot";
        private const string TasksFolderName = "Tasks";
        private const string MathTaskFolderName = "Math";
        private const string JsonFileExtension = ".json";

        public MathTaskProcessingService(SolutionHub.SolutionHub.SolutionHubClient solutionHubClient, ITaskRequestRepository taskRequestRepository, ITaskRatingRepository taskRatingRepository, ILogger<MathTaskProcessingService> logger)
        {
            _solutionHubClient = solutionHubClient;
            _taskRequestRepository = taskRequestRepository;
            _taskRatingRepository = taskRatingRepository;
            _logger = logger;
        }

        public async Task<(bool Allowed, TimeSpan? RetryAfter)> CanProcessRequestAsync(string ip, Guid? userId)
        {
            if (userId != null)
                return (true, null);

            var latest = await _taskRequestRepository.GetLastRequestByIpAsync(ip, SubjectType.Math);

            if (latest == null)
                return (true, null);

            var now = DateTime.UtcNow;
            var diff = now - latest.RequestTime;

            if (diff.TotalHours >= AnonymousRequestLimitHours)
                return (true, null);

            return (false, TimeSpan.FromHours(AnonymousRequestLimitHours) - diff);
        }

        public async Task<string> ProcessTaskAsync(JsonElement taskData, string ip, Guid? userId)
        {
            string taskId = Guid.NewGuid().ToString();
            string folderPath = Path.Combine(Directory.GetCurrentDirectory(), WebRootFolderName, TasksFolderName, MathTaskFolderName);

            if (!Directory.Exists(folderPath))
            {
                Directory.CreateDirectory(folderPath);
                _logger.LogInformation("Created task directory at: {FolderPath}", folderPath);
            }

            string filePath = Path.Combine(folderPath, $"{taskId}.json");

            try
            {
                var request = new SolveProblemRequest
                {
                    TaskId = taskId,
                    ProblemType = "math",
                    Payload = JsonSerializer.Serialize(taskData),
                    UserId = userId?.ToString() ?? ""
                };

                var response = await _solutionHubClient.SolveProblemAsync(request);

                _logger.LogInformation("Task sent to SolutionHub. TaskId: {TaskId}, Success: {Success}, Result: {Result}", taskId, response.Success, response.Result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send task to SolutionHub. TaskId: {TaskId}", taskId);
            }

            var solution = await GenerateSolutionAsync(taskData);

            await File.WriteAllTextAsync(filePath, JsonSerializer.Serialize(taskData));
            _logger.LogInformation("Saved task JSON to file: {FilePath}", filePath);

            var log = new TaskRequestLog
            {
                TaskId = taskId,
                Subject = SubjectType.Math.ToString(),
                IpAddress = ip,
                RequestTime = DateTime.UtcNow,
                UserId = userId.ToString()
            };

            await _taskRequestRepository.AddRequestAsync(log);

            _logger.LogInformation("Task log saved. TaskId: {TaskId}, IP: {Ip}, UserId: {UserId}", taskId, ip, userId.ToString() ?? "Anonymous");

            return taskId;
        }

        public async Task<JsonElement> GetTaskAsync(string id)
        {
            var filePath = Path.Combine(Directory.GetCurrentDirectory(), WebRootFolderName, TasksFolderName, MathTaskFolderName, $"{id}{JsonFileExtension}");

            if (!File.Exists(filePath))
                throw new FileNotFoundException();

            var jsonString = await File.ReadAllTextAsync(filePath);
            using var doc = JsonDocument.Parse(jsonString);
            return doc.RootElement.Clone();
        }

        public async Task RateTaskAsync(string taskId, bool isCorrect, Guid? userId)
        {
            var rating = new TaskRating 
            { 
                TaskId = taskId, 
                Subject = SubjectType.Math.ToString(),
                IsCorrect = isCorrect, 
                UserId = userId.ToString(), 
                CreatedAt = DateTime.UtcNow 
            };

            await _taskRatingRepository.AddRatingAsync(rating);
        }

        public async Task<string?> GetTaskCreatorUserIdAsync(string taskid)
        {
            var requestLog = await _taskRequestRepository.GetRequestByTaskIdAsync(taskid);
            return requestLog?.UserId;
        }

        private Task<object> GenerateSolutionAsync(JsonElement taskData) 
        {
            return Task.FromResult<Object>(new { });
        }
    }
}
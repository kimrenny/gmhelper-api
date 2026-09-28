using MatHelper.BLL.Interfaces;
using MatHelper.CORE.Models;
using MatHelper.DAL.Models;
using MatHelper.DAL.Interfaces;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using MatHelper.CORE.Enums;
using Grpc.Net.Client;
using SolutionHub;

namespace MatHelper.BLL.Services
{
    public class GeoTaskProcessingService : IGeoTaskProcessingService
    {
        private readonly SolutionHub.SolutionHub.SolutionHubClient _solutionHubClient;

        private readonly ITaskRequestRepository _taskRequestRepository;
        private readonly ITaskRatingRepository _taskRatingRepository;
        private readonly ILogger<GeoTaskProcessingService> _logger;

        private const ushort AnonymousRequestLimitHours = 24;

        private const string WebRootFolderName = "wwwroot";
        private const string TasksFolderName = "Tasks";
        private const string GeoTaskFolderName = "Geo";
        private const string JsonFileExtension = ".json";

        public GeoTaskProcessingService(SolutionHub.SolutionHub.SolutionHubClient solutionHubClient, ITaskRequestRepository taskRequestRepository, ITaskRatingRepository taskRatingRepository, ILogger<GeoTaskProcessingService> logger)
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

            var latest = await _taskRequestRepository.GetLastRequestByIpAsync(ip, SubjectType.Geometry);

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
            string folderPath = Path.Combine(Directory.GetCurrentDirectory(), WebRootFolderName, TasksFolderName, GeoTaskFolderName);

            if (!Directory.Exists(folderPath))
            {
                Directory.CreateDirectory(folderPath);
                _logger.LogInformation("Created task directory at: {FolderPath}", folderPath);
            }

            string filePath = Path.Combine(folderPath, $"{taskId}{JsonFileExtension}");

            var given = await GenerateGivenSectionAsync(taskData);
            var solution = await GenerateSolutionAsync(taskData);
            var answer = await GenerateAnswerAsync(taskData);

            var task = new
            {
                task = taskData,
                given,
                solution,
                answer
            };

            try
            {
                var request = new SolveProblemRequest
                {
                    TaskId = taskId,
                    ProblemType = "geo",
                    Payload = JsonSerializer.Serialize(task),
                    UserId = userId?.ToString() ?? ""
                };

                var response = await _solutionHubClient.SolveProblemAsync(request);

                _logger.LogInformation("Task sent to SolutionHub. TaskId: {TaskId}, Success: {Success}, Result: {Result}", taskId, response.Success, response.Result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send task to SolutionHub. TaskId: {TaskId}", taskId);
            }

            await File.WriteAllTextAsync(filePath, JsonSerializer.Serialize(task));
            _logger.LogInformation("Saved task JSON to file: {FilePath}", filePath);

            var log = new TaskRequestLog
            {
                TaskId = taskId,
                Subject = SubjectType.Geometry.ToString(),
                IpAddress = ip,
                RequestTime = DateTime.UtcNow,
                UserId = userId.ToString()
            };

            await _taskRequestRepository.AddRequestAsync(log);

            // await Task.Delay(3000);

            _logger.LogInformation("Task log saved. TaskId: {TaskId}, IP: {Ip}, UserId: {UserId}", taskId, ip, userId.ToString() ?? "Anonymous");

            return taskId;
        }

        public async Task<JsonElement> GetTaskAsync(string id)
        {
            var filePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "Tasks", "Geo", $"{id}{JsonFileExtension}");

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
                Subject = SubjectType.Geometry.ToString(),
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
        private Task<string> GenerateGivenSectionAsync(JsonElement taskData)
        {
            var result = new List<string>();

            foreach(var figureProp in taskData.EnumerateObject())
            {
                string figureId = figureProp.Name;
                var figure = figureProp.Value;

                var typePrefix = figureId.Split('_')[0].ToLower();

                if(typePrefix == "pencil") 
                {
                    continue; 
                }

                string figureName = figure.TryGetProperty("points", out var points) && points.ValueKind == JsonValueKind.Array
                    ? string.Join("", points.EnumerateArray()
                            .Where(p => p.TryGetProperty("label", out var label) && label.ValueKind == JsonValueKind.String)
                            .Select(p => p.GetProperty("label").GetString()))
                    : figureId;

                string figureType = typePrefix switch
                {
                    "rectangle" when AllSidesEqual(figure) => "square",
                    _ => typePrefix
                };

                if (!String.IsNullOrWhiteSpace(figureName))
                {
                    result.Add($"{figureName} – {figureType}");
                }
                else
                {
                    result.Add(char.ToUpper(figureType[0]) + figureType.Substring(1));

                }

                if (figure.TryGetProperty("lines", out var lines) && lines.ValueKind == JsonValueKind.Object)
                {
                    var groupedLines = lines.EnumerateObject()
                        .Where(l => l.Value.ValueKind == JsonValueKind.Number)
                        .GroupBy(l => l.Value.GetDouble())
                        .OrderByDescending(g => g.Count());

                    foreach (var group in groupedLines)
                    {
                        var names = string.Join(" = ", group.Select(g => g.Name));
                        var value = group.Key;
                        result.Add($"{names} = {value}");
                    }
                }

                if (figure.TryGetProperty("angles", out var angles) && angles.ValueKind == JsonValueKind.Object && angles.EnumerateObject().Any())
                {
                    var groupedAngles = angles.EnumerateObject()
                        .GroupBy(a => a.Value.GetDouble())
                        .OrderByDescending(g => g.Count());

                    foreach (var group in groupedAngles)
                    {
                        var names = string.Join(" = ", group.Select(g => $"∠{g.Name}"));
                        var value = group.Key;
                        result.Add($"{names} = {value}°");
                    }
                }

                result.Add("");
            }

            return Task.FromResult(string.Join("\n", result));
        }

        private Task<object> GenerateSolutionAsync(JsonElement taskData) 
        {
            return Task.FromResult<Object>(new { });
        }

        private Task<string> GenerateAnswerAsync(JsonElement taskData)
        {
            return Task.FromResult("...");
        }

        private bool AllSidesEqual(JsonElement figure)
        {
            if (!figure.TryGetProperty("lines", out var lines) || lines.ValueKind != JsonValueKind.Object)
                return false;

            var lengths = lines.EnumerateObject()
                .Where(l => l.Value.ValueKind == JsonValueKind.Number)
                .Select(p => p.Value.GetDouble())
                .Distinct()
                .ToList();
            return lengths.Count == 1;
        }
    }
}
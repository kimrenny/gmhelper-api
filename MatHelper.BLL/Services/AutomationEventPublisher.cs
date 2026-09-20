using MatHelper.BLL.Interfaces;
using MatHelper.CORE.Exceptions;
using MatHelper.CORE.Models;
using MatHelper.CORE.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MatHelper.BLL.Services
{
    /// <summary>
    /// HTTP implementation of IAutomationEventPublisher that publishes events to gmhelper-notify-api.
    /// </summary>
    public class AutomationEventPublisher : IAutomationEventPublisher
    {
        private readonly HttpClient _httpClient;
        private readonly ITokenGeneratorService _tokenGeneratorService;
        private readonly NotifyApiOptions _options;
        private readonly ILogger<AutomationEventPublisher> _logger;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        public AutomationEventPublisher(
            HttpClient httpClient,
            ITokenGeneratorService tokenGeneratorService,
            IOptions<NotifyApiOptions> options,
            ILogger<AutomationEventPublisher> logger)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _tokenGeneratorService = tokenGeneratorService ?? throw new ArgumentNullException(nameof(tokenGeneratorService));
            _options = options?.Value ?? new NotifyApiOptions();
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task PublishAsync(AutomationEvent automationEvent, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(automationEvent);

            if (string.IsNullOrWhiteSpace(automationEvent.Id))
                throw new ArgumentException("Event ID cannot be empty.", nameof(automationEvent));
            if (string.IsNullOrWhiteSpace(automationEvent.Type))
                throw new ArgumentException("Event Type cannot be empty.", nameof(automationEvent));
            if (string.IsNullOrWhiteSpace(automationEvent.UserId))
                throw new ArgumentException("Event UserID cannot be empty.", nameof(automationEvent));

            // Ensure event timestamp is strictly UTC
            if (automationEvent.OccurredAt.Kind != DateTimeKind.Utc)
            {
                automationEvent.OccurredAt = automationEvent.OccurredAt.ToUniversalTime();
            }

            var serviceToken = _tokenGeneratorService.GenerateServiceToken("gmhelper-api");
            var jsonPayload = JsonSerializer.Serialize(automationEvent, JsonOptions);

            int maxRetries = Math.Max(0, _options.MaxRetries);
            int attempt = 0;

            while (true)
            {
                attempt++;
                cancellationToken.ThrowIfCancellationRequested();

                using var request = new HttpRequestMessage(HttpMethod.Post, "api/v1/internal/automation/events")
                {
                    Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json")
                };
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", serviceToken);

                HttpResponseMessage? response = null;
                try
                {
                    response = await _httpClient.SendAsync(request, cancellationToken);

                    if (response.IsSuccessStatusCode)
                    {
                        _logger.LogInformation(
                            "Successfully published automation event {EventId} of type {EventType} for user {UserId}",
                            automationEvent.Id, automationEvent.Type, automationEvent.UserId);
                        return;
                    }

                    var statusCode = response.StatusCode;
                    var isTransient = (int)statusCode >= 500;

                    if (!isTransient || attempt > maxRetries)
                    {
                        _logger.LogError(
                            "Failed to publish automation event {EventId} of type {EventType}. Status: {StatusCode}, Attempts: {Attempt}",
                            automationEvent.Id, automationEvent.Type, (int)statusCode, attempt);

                        throw new AutomationEventPublishException(
                            $"Failed to publish automation event {automationEvent.Id}. Notify API returned HTTP {(int)statusCode} ({statusCode}).",
                            statusCode: statusCode,
                            isTransient: isTransient);
                    }

                    _logger.LogWarning(
                        "Transient HTTP {StatusCode} received when publishing automation event {EventId}. Retrying attempt {Attempt}/{MaxRetries}...",
                        (int)statusCode, automationEvent.Id, attempt, maxRetries);
                }
                catch (Exception ex) when (ex is not AutomationEventPublishException && ex is not OperationCanceledException)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        throw new OperationCanceledException("Automation event publishing was cancelled.", ex, cancellationToken);
                    }

                    if (attempt > maxRetries)
                    {
                        _logger.LogError(ex,
                            "Failed to publish automation event {EventId} after {Attempt} attempts due to network or connection error.",
                            automationEvent.Id, attempt);

                        throw new AutomationEventPublishException(
                            $"Failed to publish automation event {automationEvent.Id} due to network error: {ex.Message}",
                            isTransient: true,
                            innerException: ex);
                    }

                    _logger.LogWarning(ex,
                        "Transient error publishing automation event {EventId}. Retrying attempt {Attempt}/{MaxRetries}...",
                        automationEvent.Id, attempt, maxRetries);
                }
                finally
                {
                    response?.Dispose();
                }

                var delayMs = _options.RetryDelayMilliseconds * attempt;
                if (delayMs > 0)
                {
                    await Task.Delay(delayMs, cancellationToken);
                }
            }
        }
    }
}

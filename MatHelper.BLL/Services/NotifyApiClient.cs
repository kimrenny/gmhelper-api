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
    /// HTTP implementation of INotifyApiClient that sends direct email notifications to gmhelper-notify-api.
    /// </summary>
    public class NotifyApiClient : INotifyApiClient
    {
        private readonly HttpClient _httpClient;
        private readonly ITokenGeneratorService _tokenGeneratorService;
        private readonly NotifyApiOptions _options;
        private readonly ILogger<NotifyApiClient> _logger;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        public NotifyApiClient(
            HttpClient httpClient,
            ITokenGeneratorService tokenGeneratorService,
            IOptions<NotifyApiOptions> options,
            ILogger<NotifyApiClient> logger)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _tokenGeneratorService = tokenGeneratorService ?? throw new ArgumentNullException(nameof(tokenGeneratorService));
            _options = options?.Value ?? new NotifyApiOptions();
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task SendNotificationAsync(SendNotificationRequest request, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);

            if (string.IsNullOrWhiteSpace(request.TemplateKey))
                throw new ArgumentException("TemplateKey cannot be empty.", nameof(request));
            if (string.IsNullOrWhiteSpace(request.RecipientEmail))
                throw new ArgumentException("RecipientEmail cannot be empty.", nameof(request));

            var serviceToken = _tokenGeneratorService.GenerateServiceToken("gmhelper-api");
            var jsonPayload = JsonSerializer.Serialize(request, JsonOptions);

            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "api/v1/internal/notifications/send")
            {
                Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json")
            };
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", serviceToken);

            HttpResponseMessage? response = null;
            try
            {
                response = await _httpClient.SendAsync(httpRequest, cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation(
                        "Successfully enqueued notification for template {TemplateKey} to {RecipientEmail}",
                        request.TemplateKey, request.RecipientEmail);
                    return;
                }

                var statusCode = response.StatusCode;
                _logger.LogError(
                    "Failed to enqueue notification for template {TemplateKey} to {RecipientEmail}. Status: {StatusCode}",
                    request.TemplateKey, request.RecipientEmail, (int)statusCode);

                throw new NotifyApiException(
                    $"Failed to send notification for template {request.TemplateKey}. Notify API returned HTTP {(int)statusCode} ({statusCode}).",
                    statusCode: statusCode);
            }
            catch (Exception ex) when (ex is not NotifyApiException && ex is not OperationCanceledException)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    throw new OperationCanceledException("Notification request was cancelled.", ex, cancellationToken);
                }

                _logger.LogError(ex,
                    "Failed to send notification for template {TemplateKey} to {RecipientEmail} due to network or connection error.",
                    request.TemplateKey, request.RecipientEmail);

                throw new NotifyApiException(
                    $"Failed to send notification for template {request.TemplateKey} due to network error: {ex.Message}",
                    innerException: ex);
            }
            finally
            {
                response?.Dispose();
            }
        }
    }
}

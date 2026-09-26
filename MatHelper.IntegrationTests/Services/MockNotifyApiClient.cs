using MatHelper.BLL.Interfaces;
using MatHelper.CORE.Models;
using Microsoft.Extensions.Logging;

namespace MatHelper.IntegrationTests.Services
{
    public class MockNotifyApiClient : INotifyApiClient
    {
        private readonly ILogger<MockNotifyApiClient> _logger;
        public List<SendNotificationRequest> SentNotifications { get; } = new();

        public MockNotifyApiClient(ILogger<MockNotifyApiClient> logger)
        {
            _logger = logger;
        }

        public Task SendNotificationAsync(SendNotificationRequest request, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("[MOCK NOTIFY API] Sending notification template {TemplateKey} to {RecipientEmail}", request.TemplateKey, request.RecipientEmail);
            SentNotifications.Add(request);
            return Task.CompletedTask;
        }
    }
}

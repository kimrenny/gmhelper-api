using MatHelper.BLL.Services;
using MatHelper.CORE.Models;
using MatHelper.CORE.Options;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace MatHelper.Tests.Services
{
    public class AutomationEventPublisherIntegrationTests
    {
        [Fact]
        public async Task PublishAsync_LiveServiceToService_PublishesToRunningNotifyApi()
        {
            var notifyBaseUrl = Environment.GetEnvironmentVariable("NOTIFY_API_BASE_URL") ?? "http://localhost:8080";

            // Check if notify-api is reachable before executing live integration test
            using var probeClient = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            try
            {
                var healthResp = await probeClient.GetAsync($"{notifyBaseUrl.TrimEnd('/')}/health");
                if (!healthResp.IsSuccessStatusCode)
                {
                    return; // Skip if notify-api is not healthy
                }
            }
            catch
            {
                return; // Skip if notify-api is not reachable in current test runner
            }

            var jwtOptions = new JwtOptions
            {
                SecretKey = Environment.GetEnvironmentVariable("JWT_SECRET_KEY") ?? "c283114c605c4c78adfefef864c575266d8ce3c7a23445eda7afd6c28671e8c3",
                Issuer = Environment.GetEnvironmentVariable("JWT_ISSUER") ?? "GMHelperAPI",
                Audience = Environment.GetEnvironmentVariable("JWT_AUDIENCE") ?? "GMHelperClient"
            };

            var tokenGenerator = new TokenGeneratorService(jwtOptions, NullLogger<TokenService>.Instance);
            var notifyOptions = Options.Create(new NotifyApiOptions
            {
                BaseUrl = notifyBaseUrl,
                TimeoutSeconds = 10,
                MaxRetries = 1
            });

            using var httpClient = new HttpClient
            {
                BaseAddress = new Uri(notifyBaseUrl.TrimEnd('/') + "/")
            };

            var publisher = new AutomationEventPublisher(
                httpClient,
                tokenGenerator,
                notifyOptions,
                NullLogger<AutomationEventPublisher>.Instance);

            var eventId = Guid.NewGuid().ToString();
            var userId = Guid.NewGuid().ToString();
            var evt = new AutomationEvent
            {
                Id = eventId,
                Type = "user.registered",
                UserId = userId,
                OccurredAt = DateTime.UtcNow,
                User = new InternalUserDto
                {
                    Id = Guid.Parse(userId),
                    Username = "e2e_publisher_user",
                    Email = "e2e_publisher@example.com",
                    Role = "User",
                    Language = "EN",
                    IsActive = true,
                    IsBlocked = false,
                    RegistrationDate = DateTime.UtcNow
                },
                Data = new Dictionary<string, object>
                {
                    { "source", "dotnet_e2e_integration_test" }
                }
            };

            // This sends a real HTTP request with service JWT to POST http://localhost:8080/api/v1/internal/automation/events
            var exception = await Record.ExceptionAsync(() => publisher.PublishAsync(evt));

            Assert.Null(exception);
        }
    }
}

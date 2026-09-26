using MatHelper.BLL.Services;
using MatHelper.CORE.Models;
using MatHelper.CORE.Options;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace MatHelper.Tests.Services
{
    public class NotifyApiClientIntegrationTests
    {
        [Fact]
        public async Task SendNotificationAsync_LiveServiceToService_DispatchesToRunningNotifyApi()
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
                TimeoutSeconds = 10
            });

            using var httpClient = new HttpClient
            {
                BaseAddress = new Uri(notifyBaseUrl.TrimEnd('/') + "/")
            };

            var client = new NotifyApiClient(
                httpClient,
                tokenGenerator,
                notifyOptions,
                NullLogger<NotifyApiClient>.Instance);

            var request = new SendNotificationRequest
            {
                TemplateKey = "auth.register_code",
                Locale = "en",
                RecipientEmail = "integration_test@example.com",
                Variables = new Dictionary<string, object>
                {
                    { "mainLink", "https://gmhelper.com/" },
                    { "code", "123456" }
                }
            };

            var exception = await Record.ExceptionAsync(() => client.SendNotificationAsync(request));
            Assert.Null(exception);
        }
    }
}

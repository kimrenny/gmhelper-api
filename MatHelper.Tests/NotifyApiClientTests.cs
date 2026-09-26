using MatHelper.BLL.Interfaces;
using MatHelper.BLL.Services;
using MatHelper.CORE.Exceptions;
using MatHelper.CORE.Models;
using MatHelper.CORE.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Xunit;

namespace MatHelper.Tests.Services
{
    public class NotifyApiClientTests
    {
        private readonly Mock<ITokenGeneratorService> _tokenGeneratorMock;
        private readonly Mock<ILogger<NotifyApiClient>> _loggerMock;
        private readonly NotifyApiOptions _options;

        public NotifyApiClientTests()
        {
            _tokenGeneratorMock = new Mock<ITokenGeneratorService>();
            _tokenGeneratorMock
                .Setup(t => t.GenerateServiceToken(It.IsAny<string>()))
                .Returns("mock-service-token-abc");

            _loggerMock = new Mock<ILogger<NotifyApiClient>>();
            _options = new NotifyApiOptions
            {
                BaseUrl = "http://localhost:8080/",
                TimeoutSeconds = 5
            };
        }

        private (NotifyApiClient Client, Mock<HttpMessageHandler> HandlerMock) CreateClientWithHandler(
            Func<HttpRequestMessage, HttpResponseMessage> handlerFunc)
        {
            var handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);
            handlerMock
                .Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync((HttpRequestMessage req, CancellationToken ct) => handlerFunc(req));

            var httpClient = new HttpClient(handlerMock.Object)
            {
                BaseAddress = new Uri(_options.BaseUrl)
            };

            var client = new NotifyApiClient(
                httpClient,
                _tokenGeneratorMock.Object,
                Options.Create(_options),
                _loggerMock.Object);

            return (client, handlerMock);
        }

        [Fact]
        public async Task SendNotificationAsync_Successful202Response_SendsPostWithBearerAndCorrectJson()
        {
            HttpRequestMessage? capturedRequest = null;
            string? capturedBody = null;

            var (client, _) = CreateClientWithHandler(req =>
            {
                capturedRequest = req;
                capturedBody = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
                return new HttpResponseMessage(HttpStatusCode.Accepted)
                {
                    Content = new StringContent("{\"id\":\"notif-123\",\"deliveryStatus\":\"pending\"}")
                };
            });

            var request = new SendNotificationRequest
            {
                TemplateKey = "auth.password_recovery",
                Locale = "ru",
                ExternalUserId = "usr-guid-999",
                RecipientEmail = "user@example.com",
                RecipientName = "Alex",
                Variables = new Dictionary<string, object>
                {
                    { "mainLink", "https://gmhelper.com/" },
                    { "recoveryLink", "https://gmhelper.com/recover?token=secret123" }
                }
            };

            await client.SendNotificationAsync(request);

            Assert.NotNull(capturedRequest);
            Assert.Equal(HttpMethod.Post, capturedRequest.Method);
            Assert.Equal("http://localhost:8080/api/v1/internal/notifications/send", capturedRequest.RequestUri?.ToString());
            Assert.Equal("Bearer", capturedRequest.Headers.Authorization?.Scheme);
            Assert.Equal("mock-service-token-abc", capturedRequest.Headers.Authorization?.Parameter);

            Assert.NotNull(capturedBody);
            using var doc = JsonDocument.Parse(capturedBody);
            var root = doc.RootElement;
            Assert.Equal("auth.password_recovery", root.GetProperty("templateKey").GetString());
            Assert.Equal("ru", root.GetProperty("locale").GetString());
            Assert.Equal("usr-guid-999", root.GetProperty("externalUserId").GetString());
            Assert.Equal("user@example.com", root.GetProperty("recipientEmail").GetString());
            Assert.Equal("Alex", root.GetProperty("recipientName").GetString());
            Assert.True(root.TryGetProperty("variables", out var vars));
            Assert.Equal("https://gmhelper.com/", vars.GetProperty("mainLink").GetString());
            Assert.Equal("https://gmhelper.com/recover?token=secret123", vars.GetProperty("recoveryLink").GetString());

            _tokenGeneratorMock.Verify(t => t.GenerateServiceToken("gmhelper-api"), Times.Once);
        }

        [Fact]
        public async Task SendNotificationAsync_NullFieldsAreOmittedFromJson()
        {
            string? capturedBody = null;

            var (client, _) = CreateClientWithHandler(req =>
            {
                capturedBody = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
                return new HttpResponseMessage(HttpStatusCode.Accepted);
            });

            var request = new SendNotificationRequest
            {
                TemplateKey = "auth.register_code",
                Locale = "en",
                RecipientEmail = "reg@example.com",
                ExternalUserId = null,
                RecipientName = null,
                Variables = new Dictionary<string, object>
                {
                    { "code", "123456" }
                }
            };

            await client.SendNotificationAsync(request);

            Assert.NotNull(capturedBody);
            using var doc = JsonDocument.Parse(capturedBody);
            var root = doc.RootElement;
            Assert.False(root.TryGetProperty("externalUserId", out _));
            Assert.False(root.TryGetProperty("recipientName", out _));
        }

        [Theory]
        [InlineData(HttpStatusCode.BadRequest)]
        [InlineData(HttpStatusCode.Unauthorized)]
        [InlineData(HttpStatusCode.Forbidden)]
        [InlineData(HttpStatusCode.NotFound)]
        [InlineData(HttpStatusCode.InternalServerError)]
        public async Task SendNotificationAsync_NonSuccessStatusCode_ThrowsNotifyApiException(HttpStatusCode statusCode)
        {
            var (client, _) = CreateClientWithHandler(req => new HttpResponseMessage(statusCode)
            {
                Content = new StringContent("{\"error\":\"failed\"}")
            });

            var request = new SendNotificationRequest
            {
                TemplateKey = "auth.register_code",
                RecipientEmail = "user@example.com"
            };

            var ex = await Assert.ThrowsAsync<NotifyApiException>(() => client.SendNotificationAsync(request));
            Assert.Equal(statusCode, ex.StatusCode);
            Assert.Contains(((int)statusCode).ToString(), ex.Message);
        }

        [Fact]
        public async Task SendNotificationAsync_ConnectionFailure_ThrowsNotifyApiException()
        {
            var (client, _) = CreateClientWithHandler(req => throw new HttpRequestException("Connection refused"));

            var request = new SendNotificationRequest
            {
                TemplateKey = "auth.register_code",
                RecipientEmail = "user@example.com"
            };

            var ex = await Assert.ThrowsAsync<NotifyApiException>(() => client.SendNotificationAsync(request));
            Assert.NotNull(ex.InnerException);
            Assert.IsType<HttpRequestException>(ex.InnerException);
        }

        [Fact]
        public async Task SendNotificationAsync_Cancellation_ThrowsOperationCanceledException()
        {
            var (client, _) = CreateClientWithHandler(req => throw new TaskCanceledException());

            using var cts = new CancellationTokenSource();
            cts.Cancel();

            var request = new SendNotificationRequest
            {
                TemplateKey = "auth.register_code",
                RecipientEmail = "user@example.com"
            };

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.SendNotificationAsync(request, cts.Token));
        }

        [Fact]
        public async Task SendNotificationAsync_InvalidArguments_ThrowsArgumentException()
        {
            var (client, _) = CreateClientWithHandler(req => new HttpResponseMessage(HttpStatusCode.Accepted));

            await Assert.ThrowsAsync<ArgumentNullException>(() => client.SendNotificationAsync(null!));

            await Assert.ThrowsAsync<ArgumentException>(() => client.SendNotificationAsync(new SendNotificationRequest
            {
                TemplateKey = "",
                RecipientEmail = "user@example.com"
            }));

            await Assert.ThrowsAsync<ArgumentException>(() => client.SendNotificationAsync(new SendNotificationRequest
            {
                TemplateKey = "auth.welcome",
                RecipientEmail = ""
            }));
        }
    }
}

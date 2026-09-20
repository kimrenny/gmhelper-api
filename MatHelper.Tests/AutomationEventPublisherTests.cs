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
    public class AutomationEventPublisherTests
    {
        private readonly Mock<ITokenGeneratorService> _tokenGeneratorMock;
        private readonly Mock<ILogger<AutomationEventPublisher>> _loggerMock;
        private readonly NotifyApiOptions _options;

        public AutomationEventPublisherTests()
        {
            _tokenGeneratorMock = new Mock<ITokenGeneratorService>();
            _tokenGeneratorMock
                .Setup(t => t.GenerateServiceToken(It.IsAny<string>()))
                .Returns("mock-service-token-xyz");

            _loggerMock = new Mock<ILogger<AutomationEventPublisher>>();
            _options = new NotifyApiOptions
            {
                BaseUrl = "http://localhost:8080/",
                TimeoutSeconds = 5,
                MaxRetries = 2,
                RetryDelayMilliseconds = 1 // Fast for unit tests
            };
        }

        private (AutomationEventPublisher Publisher, Mock<HttpMessageHandler> HandlerMock) CreatePublisher(
            HttpResponseMessage responseMessage)
        {
            return CreatePublisherWithHandler(req => responseMessage);
        }

        private (AutomationEventPublisher Publisher, Mock<HttpMessageHandler> HandlerMock) CreatePublisherWithHandler(
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

            var publisher = new AutomationEventPublisher(
                httpClient,
                _tokenGeneratorMock.Object,
                Options.Create(_options),
                _loggerMock.Object);

            return (publisher, handlerMock);
        }

        [Fact]
        public async Task PublishAsync_SuccessfulRequest_SendsPostWithServiceBearerAndCorrectPayload()
        {
            HttpRequestMessage? capturedRequest = null;
            string? capturedBody = null;

            var (publisher, _) = CreatePublisherWithHandler(req =>
            {
                capturedRequest = req;
                capturedBody = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"eventId\":\"evt-1\",\"eventType\":\"user.registered\",\"results\":[]}")
                };
            });

            var now = DateTime.UtcNow;
            var evt = new AutomationEvent
            {
                Id = "evt-123",
                Type = "user.registered",
                UserId = "usr-456",
                OccurredAt = now,
                User = new InternalUserDto
                {
                    Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                    Username = "alex",
                    Email = "alex@example.com",
                    Role = "User",
                    Language = "EN",
                    IsActive = true,
                    IsBlocked = false,
                    RegistrationDate = now
                },
                Data = new Dictionary<string, object>
                {
                    { "source", "web_app" }
                }
            };

            await publisher.PublishAsync(evt);

            Assert.NotNull(capturedRequest);
            Assert.Equal(HttpMethod.Post, capturedRequest.Method);
            Assert.Equal("/api/v1/internal/automation/events", capturedRequest.RequestUri?.AbsolutePath);
            Assert.Equal("Bearer", capturedRequest.Headers.Authorization?.Scheme);
            Assert.Equal("mock-service-token-xyz", capturedRequest.Headers.Authorization?.Parameter);


            Assert.NotNull(capturedBody);
            using var doc = JsonDocument.Parse(capturedBody);
            var root = doc.RootElement;
            Assert.Equal("evt-123", root.GetProperty("id").GetString());
            Assert.Equal("user.registered", root.GetProperty("type").GetString());
            Assert.Equal("usr-456", root.GetProperty("userId").GetString());
            Assert.Equal("alex", root.GetProperty("user").GetProperty("username").GetString());
            Assert.Equal("web_app", root.GetProperty("data").GetProperty("source").GetString());
        }

        [Fact]
        public async Task PublishAsync_OmitsNullUserAndData_WhenNotProvided()
        {
            string? capturedBody = null;

            var (publisher, _) = CreatePublisherWithHandler(req =>
            {
                capturedBody = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
                return new HttpResponseMessage(HttpStatusCode.OK);
            });

            var evt = new AutomationEvent
            {
                Id = "evt-minimal",
                Type = "email.confirmed",
                UserId = "usr-min-1",
                OccurredAt = DateTime.UtcNow
            };

            await publisher.PublishAsync(evt);

            Assert.NotNull(capturedBody);
            using var doc = JsonDocument.Parse(capturedBody);
            var root = doc.RootElement;
            Assert.Equal("evt-minimal", root.GetProperty("id").GetString());
            Assert.Equal("email.confirmed", root.GetProperty("type").GetString());
            Assert.Equal("usr-min-1", root.GetProperty("userId").GetString());
            Assert.False(root.TryGetProperty("user", out _));
            Assert.False(root.TryGetProperty("data", out _));
        }

        [Fact]
        public async Task PublishAsync_PreservesEventIdAndTimestamp_AcrossInvocations()
        {
            var eventId = "stable-uuid-" + Guid.NewGuid();
            var timestamp = new DateTime(2026, 9, 19, 21, 0, 0, DateTimeKind.Utc);

            string? capturedBody = null;
            var (publisher, _) = CreatePublisherWithHandler(req =>
            {
                capturedBody = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
                return new HttpResponseMessage(HttpStatusCode.OK);
            });

            var evt = new AutomationEvent
            {
                Id = eventId,
                Type = "password.changed",
                UserId = "usr-stable",
                OccurredAt = timestamp
            };

            await publisher.PublishAsync(evt);

            Assert.NotNull(capturedBody);
            using var doc = JsonDocument.Parse(capturedBody);
            Assert.Equal(eventId, doc.RootElement.GetProperty("id").GetString());
            Assert.Equal(timestamp, doc.RootElement.GetProperty("occurredAt").GetDateTime().ToUniversalTime());
        }

        [Fact]
        public async Task PublishAsync_Surfaces4xxClientError_WithoutRetrying()
        {
            int requestCount = 0;
            var (publisher, _) = CreatePublisherWithHandler(req =>
            {
                requestCount++;
                return new HttpResponseMessage(HttpStatusCode.BadRequest)
                {
                    Content = new StringContent("{\"error\":{\"code\":\"BAD_REQUEST\",\"message\":\"event id is required\"}}")
                };
            });

            var evt = new AutomationEvent
            {
                Id = "evt-400",
                Type = "user.registered",
                UserId = "usr-1"
            };

            var ex = await Assert.ThrowsAsync<AutomationEventPublishException>(() => publisher.PublishAsync(evt));

            Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
            Assert.False(ex.IsTransient);
            Assert.Equal(1, requestCount); // Exactly 1 attempt, NO retries on 4xx
        }

        [Fact]
        public async Task PublishAsync_RetriesOn5xx_AndSucceedsOnSubsequentAttempt()
        {
            int requestCount = 0;
            var (publisher, _) = CreatePublisherWithHandler(req =>
            {
                requestCount++;
                if (requestCount == 1)
                {
                    return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
                }
                return new HttpResponseMessage(HttpStatusCode.OK);
            });

            var evt = new AutomationEvent
            {
                Id = "evt-retry-ok",
                Type = "user.registered",
                UserId = "usr-1"
            };

            await publisher.PublishAsync(evt);

            Assert.Equal(2, requestCount); // Succeeded on attempt 2
        }

        [Fact]
        public async Task PublishAsync_RetriesOn5xx_AndThrowsWhenMaxRetriesExhausted()
        {
            int requestCount = 0;
            var (publisher, _) = CreatePublisherWithHandler(req =>
            {
                requestCount++;
                return new HttpResponseMessage(HttpStatusCode.InternalServerError);
            });

            var evt = new AutomationEvent
            {
                Id = "evt-500-exhaust",
                Type = "user.registered",
                UserId = "usr-1"
            };

            var ex = await Assert.ThrowsAsync<AutomationEventPublishException>(() => publisher.PublishAsync(evt));

            Assert.Equal(HttpStatusCode.InternalServerError, ex.StatusCode);
            Assert.True(ex.IsTransient);
            Assert.Equal(3, requestCount); // Initial attempt + 2 retries = 3 attempts total
        }

        [Fact]
        public async Task PublishAsync_RetriesOnNetworkError_AndThrowsWhenMaxRetriesExhausted()
        {
            int requestCount = 0;
            var handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);
            handlerMock
                .Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>())
                .Returns(() =>
                {
                    requestCount++;
                    throw new HttpRequestException("Connection refused");
                });

            var httpClient = new HttpClient(handlerMock.Object)
            {
                BaseAddress = new Uri(_options.BaseUrl)
            };

            var publisher = new AutomationEventPublisher(
                httpClient,
                _tokenGeneratorMock.Object,
                Options.Create(_options),
                _loggerMock.Object);

            var evt = new AutomationEvent
            {
                Id = "evt-net-err",
                Type = "user.registered",
                UserId = "usr-1"
            };

            var ex = await Assert.ThrowsAsync<AutomationEventPublishException>(() => publisher.PublishAsync(evt));

            Assert.True(ex.IsTransient);
            Assert.NotNull(ex.InnerException);
            Assert.Equal(3, requestCount); // Initial attempt + 2 retries = 3 attempts total
        }

        [Fact]
        public async Task PublishAsync_RespectsCancellationToken()
        {
            var (publisher, _) = CreatePublisher(new HttpResponseMessage(HttpStatusCode.OK));

            using var cts = new CancellationTokenSource();
            cts.Cancel(); // Pre-cancel

            var evt = new AutomationEvent
            {
                Id = "evt-cancel",
                Type = "user.registered",
                UserId = "usr-1"
            };

            await Assert.ThrowsAsync<OperationCanceledException>(() => publisher.PublishAsync(evt, cts.Token));
        }

        [Fact]
        public async Task PublishAsync_ThrowsArgumentValidationErrors_OnMissingRequiredFields()
        {
            var (publisher, _) = CreatePublisher(new HttpResponseMessage(HttpStatusCode.OK));

            await Assert.ThrowsAsync<ArgumentNullException>(() => publisher.PublishAsync(null!));

            await Assert.ThrowsAsync<ArgumentException>(() => publisher.PublishAsync(new AutomationEvent
            {
                Id = "",
                Type = "user.registered",
                UserId = "u-1"
            }));

            await Assert.ThrowsAsync<ArgumentException>(() => publisher.PublishAsync(new AutomationEvent
            {
                Id = "evt-1",
                Type = "   ",
                UserId = "u-1"
            }));

            await Assert.ThrowsAsync<ArgumentException>(() => publisher.PublishAsync(new AutomationEvent
            {
                Id = "evt-1",
                Type = "user.registered",
                UserId = ""
            }));
        }
    }
}

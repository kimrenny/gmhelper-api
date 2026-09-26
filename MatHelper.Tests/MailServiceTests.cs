using MatHelper.BLL.Interfaces;
using MatHelper.BLL.Services;
using MatHelper.CORE.Enums;
using MatHelper.CORE.Exceptions;
using MatHelper.CORE.Models;
using MatHelper.DAL.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using System.Net;
using Xunit;

namespace MatHelper.Tests.BLL
{
    public class MailServiceTests
    {
        private readonly Mock<INotifyApiClient> _notifyApiClientMock;
        private readonly Mock<IUserRepository> _userRepoMock;
        private readonly Mock<IUserManagementService> _userManagementMock;
        private readonly Mock<ILogger<MailService>> _loggerMock;
        private readonly IConfiguration _configuration;
        private readonly MailService _service;

        public MailServiceTests()
        {
            var inMemorySettings = new Dictionary<string, string>
            {
                { "ClientApp:BaseUrl", "https://test-client.com" }
            };
            _configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(inMemorySettings!)
                .Build();

            _notifyApiClientMock = new Mock<INotifyApiClient>();
            _userRepoMock = new Mock<IUserRepository>();
            _userManagementMock = new Mock<IUserManagementService>();
            _loggerMock = new Mock<ILogger<MailService>>();

            _service = new MailService(
                _notifyApiClientMock.Object,
                _userRepoMock.Object,
                _userManagementMock.Object,
                _configuration,
                _loggerMock.Object);
        }

        [Fact]
        public async Task SendRegistrationCodeEmailAsync_DispatchesCorrectPayloadToNotifyApi()
        {
            SendNotificationRequest? capturedRequest = null;
            _notifyApiClientMock
                .Setup(c => c.SendNotificationAsync(It.IsAny<SendNotificationRequest>(), It.IsAny<CancellationToken>()))
                .Callback<SendNotificationRequest, CancellationToken>((req, _) => capturedRequest = req)
                .Returns(Task.CompletedTask);

            await _service.SendRegistrationCodeEmailAsync("newuser@example.com", "987654");

            Assert.NotNull(capturedRequest);
            Assert.Equal("auth.register_code", capturedRequest.TemplateKey);
            Assert.Equal("en", capturedRequest.Locale);
            Assert.Equal("newuser@example.com", capturedRequest.RecipientEmail);
            Assert.Null(capturedRequest.ExternalUserId);
            Assert.Null(capturedRequest.RecipientName);
            Assert.NotNull(capturedRequest.Variables);
            Assert.Equal("https://test-client.com/", capturedRequest.Variables["mainLink"]);
            Assert.Equal("987654", capturedRequest.Variables["code"]);
        }

        [Fact]
        public async Task SendConfirmationEmailAsync_DispatchesCorrectPayloadToNotifyApi()
        {
            var userId = Guid.NewGuid();
            var user = new User
            {
                Id = userId,
                Email = "welcome@example.com",
                Username = "welcome_user",
                PasswordHash = "hash",
                Role = "User",
                RegistrationDate = DateTime.UtcNow,
                Language = LanguageType.RU
            };

            _userRepoMock
                .Setup(r => r.GetUserByEmailAsync("welcome@example.com"))
                .ReturnsAsync(user);

            _userManagementMock
                .Setup(m => m.GetUserLanguageByEmail("welcome@example.com"))
                .ReturnsAsync("ru");

            SendNotificationRequest? capturedRequest = null;
            _notifyApiClientMock
                .Setup(c => c.SendNotificationAsync(It.IsAny<SendNotificationRequest>(), It.IsAny<CancellationToken>()))
                .Callback<SendNotificationRequest, CancellationToken>((req, _) => capturedRequest = req)
                .Returns(Task.CompletedTask);

            await _service.SendConfirmationEmailAsync("welcome@example.com");

            Assert.NotNull(capturedRequest);
            Assert.Equal("auth.welcome", capturedRequest.TemplateKey);
            Assert.Equal("ru", capturedRequest.Locale);
            Assert.Equal(userId.ToString(), capturedRequest.ExternalUserId);
            Assert.Equal("welcome@example.com", capturedRequest.RecipientEmail);
            Assert.Equal("welcome_user", capturedRequest.RecipientName);
            Assert.NotNull(capturedRequest.Variables);
            Assert.Equal("https://test-client.com/", capturedRequest.Variables["mainLink"]);
        }

        [Fact]
        public async Task SendPasswordRecoveryEmailAsync_DispatchesCorrectPayloadToNotifyApi()
        {
            var userId = Guid.NewGuid();
            var user = new User
            {
                Id = userId,
                Email = "recover@example.com",
                Username = "recover_user",
                PasswordHash = "hash",
                Role = "User",
                RegistrationDate = DateTime.UtcNow,
                Language = LanguageType.UA
            };

            _userRepoMock
                .Setup(r => r.GetUserByEmailAsync("recover@example.com"))
                .ReturnsAsync(user);

            _userManagementMock
                .Setup(m => m.GetUserLanguageByEmail("recover@example.com"))
                .ReturnsAsync("ua"); // Should normalize "ua" to "uk"

            SendNotificationRequest? capturedRequest = null;
            _notifyApiClientMock
                .Setup(c => c.SendNotificationAsync(It.IsAny<SendNotificationRequest>(), It.IsAny<CancellationToken>()))
                .Callback<SendNotificationRequest, CancellationToken>((req, _) => capturedRequest = req)
                .Returns(Task.CompletedTask);

            await _service.SendPasswordRecoveryEmailAsync("recover@example.com", "sec-token-12345");

            Assert.NotNull(capturedRequest);
            Assert.Equal("auth.password_recovery", capturedRequest.TemplateKey);
            Assert.Equal("uk", capturedRequest.Locale);
            Assert.Equal(userId.ToString(), capturedRequest.ExternalUserId);
            Assert.Equal("recover@example.com", capturedRequest.RecipientEmail);
            Assert.Equal("recover_user", capturedRequest.RecipientName);
            Assert.NotNull(capturedRequest.Variables);
            Assert.Equal("https://test-client.com/", capturedRequest.Variables["mainLink"]);
            Assert.Equal("https://test-client.com/recover?token=sec-token-12345", capturedRequest.Variables["recoveryLink"]);
        }

        [Fact]
        public async Task SendIpConfirmationCodeEmailAsync_DispatchesCorrectPayloadToNotifyApi()
        {
            var userId = Guid.NewGuid();
            var user = new User
            {
                Id = userId,
                Email = "ipcheck@example.com",
                Username = "ip_user",
                PasswordHash = "hash",
                Role = "User",
                RegistrationDate = DateTime.UtcNow,
                Language = LanguageType.DE
            };

            _userRepoMock
                .Setup(r => r.GetUserByEmailAsync("ipcheck@example.com"))
                .ReturnsAsync(user);

            _userManagementMock
                .Setup(m => m.GetUserLanguageByEmail("ipcheck@example.com"))
                .ReturnsAsync("de");

            SendNotificationRequest? capturedRequest = null;
            _notifyApiClientMock
                .Setup(c => c.SendNotificationAsync(It.IsAny<SendNotificationRequest>(), It.IsAny<CancellationToken>()))
                .Callback<SendNotificationRequest, CancellationToken>((req, _) => capturedRequest = req)
                .Returns(Task.CompletedTask);

            await _service.SendIpConfirmationCodeEmailAsync("ipcheck@example.com", "445566");

            Assert.NotNull(capturedRequest);
            Assert.Equal("auth.ip_confirmation", capturedRequest.TemplateKey);
            Assert.Equal("de", capturedRequest.Locale);
            Assert.Equal(userId.ToString(), capturedRequest.ExternalUserId);
            Assert.Equal("ipcheck@example.com", capturedRequest.RecipientEmail);
            Assert.Equal("ip_user", capturedRequest.RecipientName);
            Assert.NotNull(capturedRequest.Variables);
            Assert.Equal("https://test-client.com/", capturedRequest.Variables["mainLink"]);
            Assert.Equal("445566", capturedRequest.Variables["code"]);
        }

        [Fact]
        public async Task SendRegistrationCodeEmailAsync_Throws_WhenClientBaseUrlMissing()
        {
            var configWithoutUrl = new ConfigurationBuilder().Build();
            var service = new MailService(
                _notifyApiClientMock.Object,
                _userRepoMock.Object,
                _userManagementMock.Object,
                configWithoutUrl,
                _loggerMock.Object);

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.SendRegistrationCodeEmailAsync("test@example.com", "123456"));

            Assert.Contains("Client application base URL is not configured", ex.Message);
        }

        [Fact]
        public async Task NotifyApiFailure_PropagatesExceptionWithoutSwallowing()
        {
            _notifyApiClientMock
                .Setup(c => c.SendNotificationAsync(It.IsAny<SendNotificationRequest>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new NotifyApiException("Notify API returned HTTP 500", HttpStatusCode.InternalServerError));

            var ex = await Assert.ThrowsAsync<NotifyApiException>(() =>
                _service.SendRegistrationCodeEmailAsync("test@example.com", "123456"));

            Assert.Equal(HttpStatusCode.InternalServerError, ex.StatusCode);
        }

        [Theory]
        [InlineData("valid@example.com")]
        [InlineData("user.name+tag@domain.co.uk")]
        public void ValidateEmailFormatAsync_ValidEmails_ReturnsTrue(string email)
        {
            var result = _service.ValidateEmailFormatAsync(email);
            Assert.True(result);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("notanemail")]
        [InlineData("@nodomain.com")]
        [InlineData("user@")]
        [InlineData("user@nodot")]
        public void ValidateEmailFormatAsync_InvalidEmails_ThrowsArgumentException(string email)
        {
            Assert.Throws<ArgumentException>(() => _service.ValidateEmailFormatAsync(email));
        }
    }
}

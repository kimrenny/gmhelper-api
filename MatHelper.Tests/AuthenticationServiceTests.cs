using MatHelper.BLL.Interfaces;
using MatHelper.BLL.Services;
using MatHelper.CORE.Enums;
using MatHelper.CORE.Models;
using MatHelper.DAL.Interfaces;
using MatHelper.DAL.Models;
using Microsoft.Extensions.Logging;
using Moq;

namespace MatHelper.Tests
{
    public class AuthenticationServiceTests
    {
        private readonly Mock<IUserRepository> _userRepoMock = new();
        private readonly Mock<IAppTwoFactorSessionRepository> _twoFactorSessionRepoMock = new();
        private readonly Mock<ITwoFactorService> _twoFactorMock = new();
        private readonly Mock<IEmailLoginCodeRepository> _emailLoginCodeRepoMock = new();
        private readonly Mock<IAuthLogRepository> _authLogRepoMock = new();
        private readonly Mock<IMailService> _mailServiceMock = new();
        private readonly Mock<ISecurityService> _securityServiceMock = new();
        private readonly Mock<ILoginAttemptService> _loginAttemptServiceMock = new();
        private readonly Mock<ILogger<AuthenticationService>> _loggerMock = new();
        private readonly Mock<IRegistrationService> _registrationServiceMock = new();
        private readonly Mock<ISecurityPolicyService> _securityPolicyMock = new();
        private readonly Mock<IEmailAuthService> _emailAuthServiceMock = new();
        private readonly Mock<ILoginService> _loginServiceMock = new();
        private readonly Mock<IRecoveryService> _recoveryServiceMock = new();
        private readonly Mock<ITwoFactorAuthService> _twoFactorAuthServiceMock = new();
        private readonly Mock<ITokenService> _tokenServiceMock = new();
        private readonly Mock<ICacheService> _cacheMock = new();
        private readonly Mock<IAutomationEventPublisher> _automationEventPublisherMock = new();

        private AuthenticationService CreateService() => new(
            _userRepoMock.Object,
            _twoFactorSessionRepoMock.Object,
            _twoFactorMock.Object,
            _emailLoginCodeRepoMock.Object,
            _authLogRepoMock.Object,
            _mailServiceMock.Object,
            _securityServiceMock.Object,
            _loginAttemptServiceMock.Object,
            _registrationServiceMock.Object,
            _securityPolicyMock.Object,
            _emailAuthServiceMock.Object,
            _loginServiceMock.Object,
            _recoveryServiceMock.Object,
            _twoFactorAuthServiceMock.Object,
            _tokenServiceMock.Object,
            _cacheMock.Object,
            _automationEventPublisherMock.Object,
            _loggerMock.Object
        );

        [Fact]
        public async Task RegisterUserAsync_ShouldThrow_WhenEmailIsEmpty()
        {
            var service = CreateService();

            var userDto = new UserDto
            {
                Email = "",
                UserName = "test",
                Password = "pass",
                Token = ""
            };

            await Assert.ThrowsAsync<ArgumentException>(() =>
                service.RegisterUserAsync(userDto, new DeviceInfo(), "127.0.0.1"));
        }

        [Fact]
        public async Task RegisterUserAsync_ShouldThrow_WhenUsernameIsEmpty()
        {
            var service = CreateService();

            var userDto = new UserDto
            {
                Email = "test@test.com",
                UserName = "",
                Password = "pass",
                Token = ""
            };

            await Assert.ThrowsAsync<ArgumentException>(() =>
                service.RegisterUserAsync(userDto, new DeviceInfo(), "127.0.0.1"));
        }

        [Fact]
        public async Task SendRecoverPasswordLinkAsync_ShouldCallRecoveryService()
        {
            var service = CreateService();

            var email = "test@test.com";

            _recoveryServiceMock
                .Setup(x => x.SendRecoveryEmailAsync(email))
                .ReturnsAsync(true);

            var result = await service.SendRecoverPasswordLinkAsync(email);

            Assert.True(result);

            _recoveryServiceMock.Verify(
                x => x.SendRecoveryEmailAsync(email),
                Times.Once);
        }

        [Fact]
        public async Task RecoverPassword_ShouldIncrementCache_WhenSuccess()
        {
            var service = CreateService();

            _recoveryServiceMock
                .Setup(x => x.ResetPasswordAsync("token", "pass"))
                .ReturnsAsync(RecoverPasswordResult.Success);

            var result = await service.RecoverPassword("token", "pass");

            Assert.Equal(RecoverPasswordResult.Success, result);

            _cacheMock.Verify(
                x => x.IncrementVersionAsync("tokens:admin:version"),
                Times.Once);

            _cacheMock.Verify(
                x => x.IncrementVersionAsync("tokens:dashboard"),
                Times.Once);
        }

        [Fact]
        public async Task RegisterUserAsync_ShouldPublishUserRegisteredEvent_WhenRegistrationSucceeds()
        {
            var service = CreateService();

            var userDto = new UserDto
            {
                Email = "test@example.com",
                UserName = "testuser",
                Password = "Password123!",
                Token = "valid_token"
            };

            var userId = Guid.NewGuid();
            var registrationDate = DateTime.UtcNow.AddMinutes(-1);
            var user = new User
            {
                Id = userId,
                Username = userDto.UserName,
                Email = userDto.Email,
                Role = "User",
                Language = LanguageType.EN,
                IsActive = true,
                IsBlocked = false,
                RegistrationDate = registrationDate,
                PasswordHash = "hashed_secret_password"
            };

            _emailAuthServiceMock
                .Setup(x => x.ConfirmEmailAsync(userDto.Email, userDto.Token))
                .ReturnsAsync(ConfirmTokenResult.Success);

            _registrationServiceMock
                .Setup(x => x.EnsureEmailAndUsernameUniqueAsync(userDto.Email, userDto.UserName))
                .Returns(Task.CompletedTask);

            _securityServiceMock
                .Setup(x => x.HashPassword(userDto.Password))
                .Returns("hashed_secret_password");

            _registrationServiceMock
                .Setup(x => x.BuildNewUserAsync(userDto, "hashed_secret_password"))
                .ReturnsAsync(user);

            _registrationServiceMock
                .Setup(x => x.CreateInactiveInitialSessionAsync(user, It.IsAny<DeviceInfo>(), It.IsAny<string>()))
                .Returns(Task.CompletedTask);

            AutomationEvent? capturedEvent = null;
            _automationEventPublisherMock
                .Setup(x => x.PublishAsync(It.IsAny<AutomationEvent>(), It.IsAny<CancellationToken>()))
                .Callback<AutomationEvent, CancellationToken>((e, _) => capturedEvent = e)
                .Returns(Task.CompletedTask);

            var result = await service.RegisterUserAsync(userDto, new DeviceInfo(), "127.0.0.1");

            Assert.Equal(ConfirmTokenResult.Success, result);
            _userRepoMock.Verify(x => x.AddUserAsync(user), Times.Once);
            _userRepoMock.Verify(x => x.SaveChangesAsync(), Times.Once);
            _automationEventPublisherMock.Verify(x => x.PublishAsync(It.IsAny<AutomationEvent>(), It.IsAny<CancellationToken>()), Times.Once);

            Assert.NotNull(capturedEvent);
            Assert.Equal("user.registered", capturedEvent.Type);
            Assert.False(string.IsNullOrWhiteSpace(capturedEvent.Id));
            Assert.True(Guid.TryParse(capturedEvent.Id, out _));
            Assert.NotEqual(userId.ToString(), capturedEvent.Id);
            Assert.Equal(userId.ToString(), capturedEvent.UserId);
            Assert.Equal(DateTimeKind.Utc, capturedEvent.OccurredAt.Kind);

            Assert.NotNull(capturedEvent.User);
            Assert.Equal(userId, capturedEvent.User.Id);
            Assert.Equal("testuser", capturedEvent.User.Username);
            Assert.Equal("test@example.com", capturedEvent.User.Email);
            Assert.Equal("User", capturedEvent.User.Role);
            Assert.Equal("EN", capturedEvent.User.Language);
            Assert.True(capturedEvent.User.IsActive);
            Assert.False(capturedEvent.User.IsBlocked);
            Assert.Equal(registrationDate, capturedEvent.User.RegistrationDate);

            Assert.NotNull(capturedEvent.Data);
            Assert.Equal("registration_flow", capturedEvent.Data["source"]);
        }

        [Fact]
        public async Task RegisterUserAsync_ShouldNotPublishEvent_WhenConfirmationFails()
        {
            var service = CreateService();

            var userDto = new UserDto
            {
                Email = "test@example.com",
                UserName = "testuser",
                Password = "Password123!",
                Token = "invalid_token"
            };

            _emailAuthServiceMock
                .Setup(x => x.ConfirmEmailAsync(userDto.Email, userDto.Token))
                .ReturnsAsync(ConfirmTokenResult.TokenNotFound);

            var result = await service.RegisterUserAsync(userDto, new DeviceInfo(), "127.0.0.1");

            Assert.Equal(ConfirmTokenResult.TokenNotFound, result);
            _userRepoMock.Verify(x => x.AddUserAsync(It.IsAny<User>()), Times.Never);
            _userRepoMock.Verify(x => x.SaveChangesAsync(), Times.Never);
            _automationEventPublisherMock.Verify(x => x.PublishAsync(It.IsAny<AutomationEvent>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task RegisterUserAsync_ShouldNotPublishEvent_WhenUniquenessCheckFails()
        {
            var service = CreateService();

            var userDto = new UserDto
            {
                Email = "existing@example.com",
                UserName = "existinguser",
                Password = "Password123!",
                Token = "valid_token"
            };

            _emailAuthServiceMock
                .Setup(x => x.ConfirmEmailAsync(userDto.Email, userDto.Token))
                .ReturnsAsync(ConfirmTokenResult.Success);

            _registrationServiceMock
                .Setup(x => x.EnsureEmailAndUsernameUniqueAsync(userDto.Email, userDto.UserName))
                .ThrowsAsync(new InvalidOperationException("The account with the provided email or username already exists."));

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.RegisterUserAsync(userDto, new DeviceInfo(), "127.0.0.1"));

            _userRepoMock.Verify(x => x.AddUserAsync(It.IsAny<User>()), Times.Never);
            _userRepoMock.Verify(x => x.SaveChangesAsync(), Times.Never);
            _automationEventPublisherMock.Verify(x => x.PublishAsync(It.IsAny<AutomationEvent>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task RegisterUserAsync_ShouldNotPublishEvent_WhenPersistenceFails()
        {
            var service = CreateService();

            var userDto = new UserDto
            {
                Email = "test@example.com",
                UserName = "testuser",
                Password = "Password123!",
                Token = "valid_token"
            };

            var user = new User
            {
                Id = Guid.NewGuid(),
                Username = userDto.UserName,
                Email = userDto.Email,
                Role = "User",
                PasswordHash = "hash",
                RegistrationDate = DateTime.UtcNow
            };

            _emailAuthServiceMock
                .Setup(x => x.ConfirmEmailAsync(userDto.Email, userDto.Token))
                .ReturnsAsync(ConfirmTokenResult.Success);

            _registrationServiceMock
                .Setup(x => x.EnsureEmailAndUsernameUniqueAsync(userDto.Email, userDto.UserName))
                .Returns(Task.CompletedTask);

            _securityServiceMock
                .Setup(x => x.HashPassword(userDto.Password))
                .Returns("hash");

            _registrationServiceMock
                .Setup(x => x.BuildNewUserAsync(userDto, "hash"))
                .ReturnsAsync(user);

            _userRepoMock
                .Setup(x => x.SaveChangesAsync())
                .ThrowsAsync(new Exception("Database connection lost."));

            await Assert.ThrowsAsync<Exception>(() =>
                service.RegisterUserAsync(userDto, new DeviceInfo(), "127.0.0.1"));

            _automationEventPublisherMock.Verify(
                x => x.PublishAsync(It.IsAny<AutomationEvent>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Fact]
        public async Task RegisterUserAsync_ShouldSucceedAndLog_WhenPublisherThrowsException()
        {
            var service = CreateService();

            var userDto = new UserDto
            {
                Email = "test@example.com",
                UserName = "testuser",
                Password = "Password123!",
                Token = "valid_token"
            };

            var user = new User
            {
                Id = Guid.NewGuid(),
                Username = userDto.UserName,
                Email = userDto.Email,
                Role = "User",
                PasswordHash = "hash",
                RegistrationDate = DateTime.UtcNow
            };

            _emailAuthServiceMock
                .Setup(x => x.ConfirmEmailAsync(userDto.Email, userDto.Token))
                .ReturnsAsync(ConfirmTokenResult.Success);

            _registrationServiceMock
                .Setup(x => x.EnsureEmailAndUsernameUniqueAsync(userDto.Email, userDto.UserName))
                .Returns(Task.CompletedTask);

            _securityServiceMock
                .Setup(x => x.HashPassword(userDto.Password))
                .Returns("hash");

            _registrationServiceMock
                .Setup(x => x.BuildNewUserAsync(userDto, "hash"))
                .ReturnsAsync(user);

            _automationEventPublisherMock
                .Setup(x => x.PublishAsync(It.IsAny<AutomationEvent>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new HttpRequestException("Failed to reach notify-api"));

            var result = await service.RegisterUserAsync(userDto, new DeviceInfo(), "127.0.0.1");

            Assert.Equal(ConfirmTokenResult.Success, result);
            _userRepoMock.Verify(x => x.AddUserAsync(user), Times.Once);
            _userRepoMock.Verify(x => x.SaveChangesAsync(), Times.Once);
            _automationEventPublisherMock.Verify(x => x.PublishAsync(It.IsAny<AutomationEvent>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task RegisterUserAsync_EventPayload_ShouldNotContainSensitiveAuthenticationData()
        {
            var service = CreateService();

            var userDto = new UserDto
            {
                Email = "test@example.com",
                UserName = "testuser",
                Password = "SuperSecretPassword123!",
                Token = "verification-code-123456"
            };

            var user = new User
            {
                Id = Guid.NewGuid(),
                Username = userDto.UserName,
                Email = userDto.Email,
                Role = "User",
                PasswordHash = "$2a$11$SensitiveHashedPasswordValue",
                RegistrationDate = DateTime.UtcNow
            };

            _emailAuthServiceMock
                .Setup(x => x.ConfirmEmailAsync(userDto.Email, userDto.Token))
                .ReturnsAsync(ConfirmTokenResult.Success);

            _registrationServiceMock
                .Setup(x => x.EnsureEmailAndUsernameUniqueAsync(userDto.Email, userDto.UserName))
                .Returns(Task.CompletedTask);

            _securityServiceMock
                .Setup(x => x.HashPassword(userDto.Password))
                .Returns(user.PasswordHash);

            _registrationServiceMock
                .Setup(x => x.BuildNewUserAsync(userDto, user.PasswordHash))
                .ReturnsAsync(user);

            AutomationEvent? capturedEvent = null;
            _automationEventPublisherMock
                .Setup(x => x.PublishAsync(It.IsAny<AutomationEvent>(), It.IsAny<CancellationToken>()))
                .Callback<AutomationEvent, CancellationToken>((e, _) => capturedEvent = e)
                .Returns(Task.CompletedTask);

            var result = await service.RegisterUserAsync(userDto, new DeviceInfo(), "127.0.0.1");

            Assert.Equal(ConfirmTokenResult.Success, result);
            Assert.NotNull(capturedEvent);

            var json = System.Text.Json.JsonSerializer.Serialize(capturedEvent);

            Assert.DoesNotContain("SuperSecretPassword123!", json);
            Assert.DoesNotContain("SensitiveHashedPasswordValue", json);
            Assert.DoesNotContain("verification-code-123456", json);
            Assert.DoesNotContain("PasswordHash", json);
        }

        [Fact]
        public async Task ConfirmEmailCodeAsync_ShouldPublishEmailConfirmedEvent_WhenConfirmationSucceeds()
        {
            var service = CreateService();

            var sessionKey = "valid_session_key_123";
            var code = "123456";
            var userId = Guid.NewGuid();
            var registrationDate = DateTime.UtcNow.AddDays(-2);

            var user = new User
            {
                Id = userId,
                Username = "confirmuser",
                Email = "confirmuser@example.com",
                Role = "User",
                Language = LanguageType.EN,
                IsActive = true,
                IsBlocked = false,
                RegistrationDate = registrationDate,
                PasswordHash = "hashed_secret",
                LoginTokens = new List<LoginToken>()
            };

            var emailCode = new EmailLoginCode
            {
                SessionKey = sessionKey,
                Code = code,
                UserId = userId,
                Email = user.Email,
                IpAddress = "127.0.0.1",
                UserAgent = "TestAgent",
                Platform = "TestOS",
                Remember = true,
                IsUsed = false,
                Expiration = DateTime.UtcNow.AddMinutes(15)
            };

            _emailLoginCodeRepoMock
                .Setup(x => x.GetBySessionKeyAsync(sessionKey))
                .ReturnsAsync(emailCode);

            _userRepoMock
                .Setup(x => x.GetUserByIdAsync(userId))
                .ReturnsAsync(user);

            var loginToken = new LoginToken
            {
                Token = "jwt_access_token_123",
                RefreshToken = "refresh_token_456",
                UserId = userId,
                Expiration = DateTime.UtcNow.AddHours(1),
                RefreshTokenExpiration = DateTime.UtcNow.AddDays(7),
                DeviceInfo = new DeviceInfo { UserAgent = "TestAgent", Platform = "TestOS" },
                IpAddress = "127.0.0.1",
                IsActive = true
            };

            _tokenServiceMock
                .Setup(x => x.IssueLoginTokenAsync(user, It.IsAny<DeviceInfo>(), emailCode.IpAddress, emailCode.Remember))
                .ReturnsAsync(loginToken);

            AutomationEvent? capturedEvent = null;
            _automationEventPublisherMock
                .Setup(x => x.PublishAsync(It.IsAny<AutomationEvent>(), It.IsAny<CancellationToken>()))
                .Callback<AutomationEvent, CancellationToken>((e, _) => capturedEvent = e)
                .Returns(Task.CompletedTask);

            var result = await service.ConfirmEmailCodeAsync(code, sessionKey);

            Assert.NotNull(result);
            Assert.Equal("jwt_access_token_123", result.AccessToken);
            Assert.Equal("refresh_token_456", result.RefreshToken);
            Assert.True(emailCode.IsUsed);

            _emailLoginCodeRepoMock.Verify(x => x.SaveChangesAsync(), Times.Once);
            _userRepoMock.Verify(x => x.SaveChangesAsync(), Times.Once);
            _automationEventPublisherMock.Verify(x => x.PublishAsync(It.IsAny<AutomationEvent>(), It.IsAny<CancellationToken>()), Times.Once);

            Assert.NotNull(capturedEvent);
            Assert.Equal("email.confirmed", capturedEvent.Type);
            Assert.False(string.IsNullOrWhiteSpace(capturedEvent.Id));
            Assert.True(Guid.TryParse(capturedEvent.Id, out _));
            Assert.NotEqual(userId.ToString(), capturedEvent.Id);
            Assert.Equal(userId.ToString(), capturedEvent.UserId);
            Assert.Equal(DateTimeKind.Utc, capturedEvent.OccurredAt.Kind);

            Assert.NotNull(capturedEvent.User);
            Assert.Equal(userId, capturedEvent.User.Id);
            Assert.Equal("confirmuser", capturedEvent.User.Username);
            Assert.Equal("confirmuser@example.com", capturedEvent.User.Email);
            Assert.Equal("User", capturedEvent.User.Role);
            Assert.Equal("EN", capturedEvent.User.Language);
            Assert.True(capturedEvent.User.IsActive);
            Assert.False(capturedEvent.User.IsBlocked);
            Assert.Equal(registrationDate, capturedEvent.User.RegistrationDate);

            Assert.NotNull(capturedEvent.Data);
            Assert.Equal("email_confirmation_flow", capturedEvent.Data["source"]);
        }

        [Fact]
        public async Task ConfirmEmailCodeAsync_ShouldNotPublishEvent_WhenSessionKeyNotFound()
        {
            var service = CreateService();

            _emailLoginCodeRepoMock
                .Setup(x => x.GetBySessionKeyAsync("non_existent_session"))
                .ReturnsAsync((EmailLoginCode?)null);

            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                service.ConfirmEmailCodeAsync("123456", "non_existent_session"));

            _automationEventPublisherMock.Verify(
                x => x.PublishAsync(It.IsAny<AutomationEvent>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Fact]
        public async Task ConfirmEmailCodeAsync_ShouldNotPublishEvent_WhenCodeMismatch()
        {
            var service = CreateService();

            var emailCode = new EmailLoginCode
            {
                SessionKey = "session_key",
                Code = "correct_code",
                UserId = Guid.NewGuid(),
                IpAddress = "127.0.0.1",
                UserAgent = "TestAgent",
                Platform = "TestOS",
                Remember = false,
                Expiration = DateTime.UtcNow.AddMinutes(15)
            };

            _emailLoginCodeRepoMock
                .Setup(x => x.GetBySessionKeyAsync("session_key"))
                .ReturnsAsync(emailCode);

            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                service.ConfirmEmailCodeAsync("wrong_code", "session_key"));

            _automationEventPublisherMock.Verify(
                x => x.PublishAsync(It.IsAny<AutomationEvent>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Fact]
        public async Task ConfirmEmailCodeAsync_ShouldNotPublishEvent_WhenUserBlocked()
        {
            var service = CreateService();

            var userId = Guid.NewGuid();
            var emailCode = new EmailLoginCode
            {
                SessionKey = "session_key",
                Code = "123456",
                UserId = userId,
                IpAddress = "127.0.0.1",
                UserAgent = "TestAgent",
                Platform = "TestOS",
                Remember = false,
                Expiration = DateTime.UtcNow.AddMinutes(15)
            };

            var user = new User
            {
                Id = userId,
                Username = "blockeduser",
                Email = "blocked@example.com",
                Role = "User",
                IsActive = true,
                IsBlocked = true,
                PasswordHash = "hash",
                RegistrationDate = DateTime.UtcNow
            };

            _emailLoginCodeRepoMock
                .Setup(x => x.GetBySessionKeyAsync("session_key"))
                .ReturnsAsync(emailCode);

            _userRepoMock
                .Setup(x => x.GetUserByIdAsync(userId))
                .ReturnsAsync(user);

            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                service.ConfirmEmailCodeAsync("123456", "session_key"));

            _automationEventPublisherMock.Verify(
                x => x.PublishAsync(It.IsAny<AutomationEvent>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Fact]
        public async Task ConfirmEmailCodeAsync_ShouldNotPublishEvent_WhenPersistenceFails()
        {
            var service = CreateService();

            var userId = Guid.NewGuid();
            var emailCode = new EmailLoginCode
            {
                SessionKey = "session_key",
                Code = "123456",
                UserId = userId,
                IpAddress = "127.0.0.1",
                UserAgent = "TestAgent",
                Platform = "TestOS",
                Remember = false,
                Expiration = DateTime.UtcNow.AddMinutes(15)
            };

            var user = new User
            {
                Id = userId,
                Username = "testuser",
                Email = "test@example.com",
                Role = "User",
                IsActive = true,
                IsBlocked = false,
                PasswordHash = "hash",
                RegistrationDate = DateTime.UtcNow,
                LoginTokens = new List<LoginToken>()
            };

            _emailLoginCodeRepoMock
                .Setup(x => x.GetBySessionKeyAsync("session_key"))
                .ReturnsAsync(emailCode);

            _userRepoMock
                .Setup(x => x.GetUserByIdAsync(userId))
                .ReturnsAsync(user);

            _tokenServiceMock
                .Setup(x => x.IssueLoginTokenAsync(user, It.IsAny<DeviceInfo>(), It.IsAny<string>(), It.IsAny<bool>()))
                .ReturnsAsync(new LoginToken
                {
                    Token = "jwt",
                    RefreshToken = "refresh",
                    UserId = userId,
                    Expiration = DateTime.UtcNow.AddHours(1),
                    RefreshTokenExpiration = DateTime.UtcNow.AddDays(7),
                    DeviceInfo = new DeviceInfo(),
                    IpAddress = "127.0.0.1"
                });

            _userRepoMock
                .Setup(x => x.SaveChangesAsync())
                .ThrowsAsync(new Exception("Database connection failure"));

            await Assert.ThrowsAsync<Exception>(() =>
                service.ConfirmEmailCodeAsync("123456", "session_key"));

            _automationEventPublisherMock.Verify(
                x => x.PublishAsync(It.IsAny<AutomationEvent>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Fact]
        public async Task ConfirmEmailCodeAsync_ShouldSucceedAndLog_WhenPublisherThrowsException()
        {
            var service = CreateService();

            var userId = Guid.NewGuid();
            var emailCode = new EmailLoginCode
            {
                SessionKey = "session_key",
                Code = "123456",
                UserId = userId,
                IpAddress = "127.0.0.1",
                UserAgent = "TestAgent",
                Platform = "TestOS",
                Remember = false,
                Expiration = DateTime.UtcNow.AddMinutes(15)
            };

            var user = new User
            {
                Id = userId,
                Username = "testuser",
                Email = "test@example.com",
                Role = "User",
                IsActive = true,
                IsBlocked = false,
                PasswordHash = "hash",
                RegistrationDate = DateTime.UtcNow,
                LoginTokens = new List<LoginToken>()
            };

            _emailLoginCodeRepoMock
                .Setup(x => x.GetBySessionKeyAsync("session_key"))
                .ReturnsAsync(emailCode);

            _userRepoMock
                .Setup(x => x.GetUserByIdAsync(userId))
                .ReturnsAsync(user);

            var loginToken = new LoginToken
            {
                Token = "jwt_token_123",
                RefreshToken = "refresh_token_456",
                UserId = userId,
                Expiration = DateTime.UtcNow.AddHours(1),
                RefreshTokenExpiration = DateTime.UtcNow.AddDays(7),
                DeviceInfo = new DeviceInfo(),
                IpAddress = "127.0.0.1"
            };

            _tokenServiceMock
                .Setup(x => x.IssueLoginTokenAsync(user, It.IsAny<DeviceInfo>(), It.IsAny<string>(), It.IsAny<bool>()))
                .ReturnsAsync(loginToken);

            _automationEventPublisherMock
                .Setup(x => x.PublishAsync(It.IsAny<AutomationEvent>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new HttpRequestException("Failed to reach notify-api"));

            var result = await service.ConfirmEmailCodeAsync("123456", "session_key");

            Assert.NotNull(result);
            Assert.Equal("jwt_token_123", result.AccessToken);
            Assert.Equal("refresh_token_456", result.RefreshToken);

            _emailLoginCodeRepoMock.Verify(x => x.SaveChangesAsync(), Times.Once);
            _userRepoMock.Verify(x => x.SaveChangesAsync(), Times.Once);
            _automationEventPublisherMock.Verify(x => x.PublishAsync(It.IsAny<AutomationEvent>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task ConfirmEmailCodeAsync_EventPayload_ShouldNotContainSensitiveAuthenticationData()
        {
            var service = CreateService();

            var userId = Guid.NewGuid();
            var sessionKey = "super_secret_session_key_789";
            var code = "654321";

            var user = new User
            {
                Id = userId,
                Username = "secretuser",
                Email = "secret@example.com",
                Role = "User",
                PasswordHash = "$2a$11$SuperSecretPasswordHashValue",
                RegistrationDate = DateTime.UtcNow,
                LoginTokens = new List<LoginToken>()
            };

            var emailCode = new EmailLoginCode
            {
                SessionKey = sessionKey,
                Code = code,
                UserId = userId,
                Email = user.Email,
                IpAddress = "127.0.0.1",
                UserAgent = "TestAgent",
                Platform = "TestOS",
                Remember = false,
                Expiration = DateTime.UtcNow.AddMinutes(15)
            };

            _emailLoginCodeRepoMock
                .Setup(x => x.GetBySessionKeyAsync(sessionKey))
                .ReturnsAsync(emailCode);

            _userRepoMock
                .Setup(x => x.GetUserByIdAsync(userId))
                .ReturnsAsync(user);

            var loginToken = new LoginToken
            {
                Token = "secret_access_jwt_value_abc",
                RefreshToken = "secret_refresh_token_value_xyz",
                UserId = userId,
                Expiration = DateTime.UtcNow.AddHours(1),
                RefreshTokenExpiration = DateTime.UtcNow.AddDays(7),
                DeviceInfo = new DeviceInfo(),
                IpAddress = "127.0.0.1"
            };

            _tokenServiceMock
                .Setup(x => x.IssueLoginTokenAsync(user, It.IsAny<DeviceInfo>(), It.IsAny<string>(), It.IsAny<bool>()))
                .ReturnsAsync(loginToken);

            AutomationEvent? capturedEvent = null;
            _automationEventPublisherMock
                .Setup(x => x.PublishAsync(It.IsAny<AutomationEvent>(), It.IsAny<CancellationToken>()))
                .Callback<AutomationEvent, CancellationToken>((e, _) => capturedEvent = e)
                .Returns(Task.CompletedTask);

            var result = await service.ConfirmEmailCodeAsync(code, sessionKey);

            Assert.NotNull(result);
            Assert.NotNull(capturedEvent);

            var json = System.Text.Json.JsonSerializer.Serialize(capturedEvent);

            Assert.DoesNotContain(code, json);
            Assert.DoesNotContain(sessionKey, json);
            Assert.DoesNotContain("secret_access_jwt_value_abc", json);
            Assert.DoesNotContain("secret_refresh_token_value_xyz", json);
            Assert.DoesNotContain("SuperSecretPasswordHashValue", json);
            Assert.DoesNotContain("PasswordHash", json);
        }
    }
}
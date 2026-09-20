using Xunit;
using Moq;
using Microsoft.Extensions.Logging;
using MatHelper.BLL.Services;
using MatHelper.CORE.Models;
using MatHelper.CORE.Enums;
using MatHelper.DAL.Interfaces;
using MatHelper.BLL.Interfaces;
using System.Reflection;

namespace MatHelper.Tests.BLL
{
    public class UserAdminServiceTests
    {
        private readonly Mock<IUserRepository> _userRepositoryMock = new();
        private readonly Mock<IUserMapper> _userMapperMock = new();
        private readonly Mock<ICacheService> _cacheMock = new();
        private readonly Mock<IAutomationEventPublisher> _automationEventPublisherMock = new();
        private readonly Mock<ILogger<UserAdminService>> _loggerMock = new();

        private UserAdminService CreateService()
        {
            return new UserAdminService(
                _userRepositoryMock.Object,
                _userMapperMock.Object,
                _cacheMock.Object,
                _automationEventPublisherMock.Object,
                _loggerMock.Object
            );
        }

        private static User CreateSampleUser(Guid userId, bool isBlocked)
        {
            return new User
            {
                Id = userId,
                Username = "admin_target_user",
                Email = "target@example.com",
                PasswordHash = "supersecret_hash",
                Role = "User",
                Language = LanguageType.EN,
                IsActive = true,
                IsBlocked = isBlocked,
                RegistrationDate = DateTime.UtcNow.AddDays(-10)
            };
        }

        [Fact]
        public async Task ActionUserAsync_Ban_ShouldPublishUserBlockedEvent_WhenUserWasUnblocked()
        {
            var userId = Guid.NewGuid();
            var blockedUser = CreateSampleUser(userId, true);

            _userRepositoryMock
                .Setup(x => x.ActionUserAsync(userId, UserAction.Ban))
                .ReturnsAsync((blockedUser, true));

            AutomationEvent? capturedEvent = null;
            _automationEventPublisherMock
                .Setup(x => x.PublishAsync(It.IsAny<AutomationEvent>(), It.IsAny<CancellationToken>()))
                .Callback<AutomationEvent, CancellationToken>((e, _) => capturedEvent = e)
                .Returns(Task.CompletedTask);

            var service = CreateService();

            await service.ActionUserAsync(userId, "Ban");

            _automationEventPublisherMock.Verify(x => x.PublishAsync(It.IsAny<AutomationEvent>(), It.IsAny<CancellationToken>()), Times.Once);
            Assert.NotNull(capturedEvent);
            Assert.Equal("user.blocked", capturedEvent.Type);
            Assert.Equal(userId.ToString(), capturedEvent.UserId);
            Assert.False(string.IsNullOrWhiteSpace(capturedEvent.Id));
            Assert.NotEqual(userId.ToString(), capturedEvent.Id);
            Assert.Equal(DateTimeKind.Utc, capturedEvent.OccurredAt.Kind);

            Assert.NotNull(capturedEvent.User);
            Assert.Equal(userId, capturedEvent.User.Id);
            Assert.Equal("admin_target_user", capturedEvent.User.Username);
            Assert.Equal("target@example.com", capturedEvent.User.Email);
            Assert.Equal("User", capturedEvent.User.Role);
            Assert.Equal("EN", capturedEvent.User.Language);
            Assert.True(capturedEvent.User.IsActive);
            Assert.True(capturedEvent.User.IsBlocked);

            Assert.NotNull(capturedEvent.Data);
            Assert.Equal("user_admin_action", capturedEvent.Data["source"]);
        }

        [Fact]
        public async Task ActionUserAsync_Unban_ShouldPublishUserUnblockedEvent_WhenUserWasBlocked()
        {
            var userId = Guid.NewGuid();
            var unblockedUser = CreateSampleUser(userId, false);

            _userRepositoryMock
                .Setup(x => x.ActionUserAsync(userId, UserAction.Unban))
                .ReturnsAsync((unblockedUser, true));

            AutomationEvent? capturedEvent = null;
            _automationEventPublisherMock
                .Setup(x => x.PublishAsync(It.IsAny<AutomationEvent>(), It.IsAny<CancellationToken>()))
                .Callback<AutomationEvent, CancellationToken>((e, _) => capturedEvent = e)
                .Returns(Task.CompletedTask);

            var service = CreateService();

            await service.ActionUserAsync(userId, "Unban");

            _automationEventPublisherMock.Verify(x => x.PublishAsync(It.IsAny<AutomationEvent>(), It.IsAny<CancellationToken>()), Times.Once);
            Assert.NotNull(capturedEvent);
            Assert.Equal("user.unblocked", capturedEvent.Type);
            Assert.Equal(userId.ToString(), capturedEvent.UserId);
            Assert.False(string.IsNullOrWhiteSpace(capturedEvent.Id));
            Assert.NotEqual(userId.ToString(), capturedEvent.Id);
            Assert.Equal(DateTimeKind.Utc, capturedEvent.OccurredAt.Kind);

            Assert.NotNull(capturedEvent.User);
            Assert.Equal(userId, capturedEvent.User.Id);
            Assert.False(capturedEvent.User.IsBlocked);

            Assert.NotNull(capturedEvent.Data);
            Assert.Equal("user_admin_action", capturedEvent.Data["source"]);
        }

        [Fact]
        public async Task ActionUserAsync_Ban_ShouldNotPublishEvent_WhenUserAlreadyBlocked()
        {
            var userId = Guid.NewGuid();
            var user = CreateSampleUser(userId, true);

            _userRepositoryMock
                .Setup(x => x.ActionUserAsync(userId, UserAction.Ban))
                .ReturnsAsync((user, false));

            var service = CreateService();

            await service.ActionUserAsync(userId, "Ban");

            _automationEventPublisherMock.Verify(x => x.PublishAsync(It.IsAny<AutomationEvent>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task ActionUserAsync_Unban_ShouldNotPublishEvent_WhenUserAlreadyUnblocked()
        {
            var userId = Guid.NewGuid();
            var user = CreateSampleUser(userId, false);

            _userRepositoryMock
                .Setup(x => x.ActionUserAsync(userId, UserAction.Unban))
                .ReturnsAsync((user, false));

            var service = CreateService();

            await service.ActionUserAsync(userId, "Unban");

            _automationEventPublisherMock.Verify(x => x.PublishAsync(It.IsAny<AutomationEvent>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task ActionUserAsync_ShouldNotPublishEvent_WhenPersistenceFails()
        {
            var userId = Guid.NewGuid();

            _userRepositoryMock
                .Setup(x => x.ActionUserAsync(userId, UserAction.Ban))
                .ThrowsAsync(new InvalidOperationException("DB error"));

            var service = CreateService();

            await Assert.ThrowsAsync<InvalidOperationException>(() => service.ActionUserAsync(userId, "Ban"));

            _automationEventPublisherMock.Verify(x => x.PublishAsync(It.IsAny<AutomationEvent>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task ActionUserAsync_ShouldNotPublishEvent_WhenUserNotFound()
        {
            var userId = Guid.NewGuid();

            _userRepositoryMock
                .Setup(x => x.ActionUserAsync(userId, UserAction.Ban))
                .ThrowsAsync(new InvalidOperationException("User not found."));

            var service = CreateService();

            await Assert.ThrowsAsync<InvalidOperationException>(() => service.ActionUserAsync(userId, "Ban"));

            _automationEventPublisherMock.Verify(x => x.PublishAsync(It.IsAny<AutomationEvent>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task ActionUserAsync_ShouldThrowAndNotPublishEvent_WhenActionIsInvalid()
        {
            var userId = Guid.NewGuid();
            var service = CreateService();

            await Assert.ThrowsAsync<ArgumentException>(() => service.ActionUserAsync(userId, "InvalidAction"));

            _automationEventPublisherMock.Verify(x => x.PublishAsync(It.IsAny<AutomationEvent>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task ActionUserAsync_Ban_ShouldSucceedAndLog_WhenPublisherThrowsException()
        {
            var userId = Guid.NewGuid();
            var blockedUser = CreateSampleUser(userId, true);

            _userRepositoryMock
                .Setup(x => x.ActionUserAsync(userId, UserAction.Ban))
                .ReturnsAsync((blockedUser, true));

            _automationEventPublisherMock
                .Setup(x => x.PublishAsync(It.IsAny<AutomationEvent>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new Exception("Publisher network timeout"));

            var service = CreateService();

            // Operation should NOT throw even though publisher failed
            await service.ActionUserAsync(userId, "Ban");

            _automationEventPublisherMock.Verify(x => x.PublishAsync(It.IsAny<AutomationEvent>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task ActionUserAsync_Unban_ShouldSucceedAndLog_WhenPublisherThrowsException()
        {
            var userId = Guid.NewGuid();
            var unblockedUser = CreateSampleUser(userId, false);

            _userRepositoryMock
                .Setup(x => x.ActionUserAsync(userId, UserAction.Unban))
                .ReturnsAsync((unblockedUser, true));

            _automationEventPublisherMock
                .Setup(x => x.PublishAsync(It.IsAny<AutomationEvent>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new Exception("Publisher network timeout"));

            var service = CreateService();

            // Operation should NOT throw even though publisher failed
            await service.ActionUserAsync(userId, "Unban");

            _automationEventPublisherMock.Verify(x => x.PublishAsync(It.IsAny<AutomationEvent>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task ActionUserAsync_EventPayload_ShouldNotContainSensitiveAuthenticationData()
        {
            var userId = Guid.NewGuid();
            var blockedUser = CreateSampleUser(userId, true);

            _userRepositoryMock
                .Setup(x => x.ActionUserAsync(userId, UserAction.Ban))
                .ReturnsAsync((blockedUser, true));

            AutomationEvent? capturedEvent = null;
            _automationEventPublisherMock
                .Setup(x => x.PublishAsync(It.IsAny<AutomationEvent>(), It.IsAny<CancellationToken>()))
                .Callback<AutomationEvent, CancellationToken>((e, _) => capturedEvent = e)
                .Returns(Task.CompletedTask);

            var service = CreateService();

            await service.ActionUserAsync(userId, "Ban");

            Assert.NotNull(capturedEvent);

            var forbiddenSubstrings = new[]
            {
                "password", "hash", "salt", "token", "secret", "session", "code", "refresh"
            };

            var userProperties = typeof(InternalUserDto).GetProperties(BindingFlags.Public | BindingFlags.Instance);
            foreach (var prop in userProperties)
            {
                var lowerName = prop.Name.ToLowerInvariant();
                foreach (var forbidden in forbiddenSubstrings)
                {
                    Assert.DoesNotContain(forbidden, lowerName);
                }
            }

            if (capturedEvent.Data != null)
            {
                foreach (var kvp in capturedEvent.Data)
                {
                    var lowerKey = kvp.Key.ToLowerInvariant();
                    Assert.False(lowerKey.Contains("password") || lowerKey.Contains("token") || lowerKey.Contains("hash"),
                        $"Data dictionary key '{kvp.Key}' contains sensitive credential name");

                    var strVal = kvp.Value?.ToString() ?? "";
                    Assert.DoesNotContain("supersecret", strVal.ToLowerInvariant());
                }
            }
        }
    }
}

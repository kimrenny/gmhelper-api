using MatHelper.BLL.Interfaces;
using MatHelper.BLL.Services;
using MatHelper.CORE.Enums;
using MatHelper.CORE.Models;
using MatHelper.CORE.Options;
using MatHelper.DAL.Interfaces;
using MatHelper.DAL.Models;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace MatHelper.Tests.Services
{
    public class UserManagementServiceTests
    {
        private readonly Mock<IUserRepository> _userRepoMock;
        private readonly Mock<ITwoFactorService> _twoFactorMock;
        private readonly Mock<ISecurityService> _securityMock;
        private readonly Mock<IAutomationEventPublisher> _automationEventPublisherMock;
        private readonly Mock<ILogger<UserManagementService>> _loggerMock;
        private readonly UserManagementService _service;

        public UserManagementServiceTests()
        {
            _userRepoMock = new Mock<IUserRepository>();
            _twoFactorMock = new Mock<ITwoFactorService>();
            _securityMock = new Mock<ISecurityService>();
            _automationEventPublisherMock = new Mock<IAutomationEventPublisher>();
            _loggerMock = new Mock<ILogger<UserManagementService>>();

            _service = new UserManagementService(
                _userRepoMock.Object,
                _twoFactorMock.Object,
                _securityMock.Object,
                _automationEventPublisherMock.Object,
                _loggerMock.Object
            );
        }

        [Fact]
        public async Task GetUserDetailsAsync_ReturnsCorrectDetails()
        {
            var userId = Guid.NewGuid();
            var user = new User
            {
                Id = userId,
                Username = "TestUser",
                Email = "test@example.com",
                PasswordHash = "hash",
                Role = "User",
                RegistrationDate = DateTime.UtcNow,
                IsBlocked = false,
            };
            var twoFactor = new UserTwoFactor
            {
                UserId = userId,
                IsEnabled = true,
                AlwaysAsk = true
            };

            _userRepoMock.Setup(r => r.GetUserByIdAsync(userId)).ReturnsAsync(user);
            _twoFactorMock.Setup(t => t.GetTwoFactorAsync(userId, "totp")).ReturnsAsync(twoFactor);

            var result = await _service.GetUserDetailsAsync(userId);

            Assert.Equal(user.Username, result.Nickname);
            Assert.Equal(user.Language.ToString(), result.Language);
            Assert.True(result.TwoFactor);
            Assert.True(result.AlwaysAsk);
        }

        [Fact]
        public async Task GetUserAvatarAsync_ReturnsAvatar_WhenExists()
        {
            var userId = Guid.NewGuid();
            var avatar = new byte[] { 1, 2, 3 };
            var user = new User
            {
                Id = userId,
                Avatar = avatar,
                Username = "Test",
                Email = "test@example.com",
                Role = "User",
                PasswordHash = "hash",
                RegistrationDate = DateTime.UtcNow,
                IsBlocked = false,
            };

            _userRepoMock.Setup(r => r.GetUserByIdAsync(userId)).ReturnsAsync(user);

            var result = await _service.GetUserAvatarAsync(userId);

            Assert.Equal(avatar, result);
        }

        [Fact]
        public async Task GetUserAvatarAsync_Throws_WhenNoAvatar()
        {
            var userId = Guid.NewGuid();
            var user = new User
            {
                Id = userId,
                Username = "Test",
                Email = "test@example.com",
                Role = "User",
                PasswordHash = "hash",
                RegistrationDate = DateTime.UtcNow,
                IsBlocked = false,
            };

            _userRepoMock.Setup(r => r.GetUserByIdAsync(userId)).ReturnsAsync(user);

            await Assert.ThrowsAsync<Exception>(() => _service.GetUserAvatarAsync(userId));
        }

        [Fact]
        public async Task SaveUserAvatarAsync_UpdatesAvatar()
        {
            var userId = Guid.NewGuid();
            var user = new User
            {
                Id = userId,
                Username = "Test",
                Email = "test@example.com",
                Role = "User",
                PasswordHash = "hash",
                RegistrationDate = DateTime.UtcNow,
                IsBlocked = false,
            };
            var newAvatar = new byte[] { 4, 5, 6 };

            _userRepoMock.Setup(r => r.GetUserByIdAsync(userId)).ReturnsAsync(user);
            _userRepoMock.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);

            await _service.SaveUserAvatarAsync(userId, newAvatar);

            Assert.Equal(newAvatar, user.Avatar);
        }

        [Fact]
        public async Task UpdateUserAsync_Throws_WhenNewPasswordTooShort()
        {
            var userId = Guid.NewGuid();
            var user = new User
            {
                Id = userId,
                Username = "OldName",
                Email = "old@example.com",
                PasswordHash = "hash",
                Role = "User",
                RegistrationDate = DateTime.UtcNow,
                IsBlocked = false,
            };

            var request = new UpdateUserRequest
            {
                CurrentPassword = "currentPass",
                NewPassword = "short"
            };

            _userRepoMock.Setup(r => r.GetUserByIdAsync(userId)).ReturnsAsync(user);
            _securityMock.Setup(s => s.VerifyPassword(request.CurrentPassword, user.PasswordHash))
                .Returns(true);

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                _service.UpdateUserAsync(userId, request)
            );

            Assert.Equal("New password is too short.", ex.Message);
        }


        [Fact]
        public async Task UpdateUserAsync_UpdatesFields_WhenValid()
        {
            var userId = Guid.NewGuid();
            var user = new User
            {
                Id = userId,
                Username = "OldName",
                Email = "old@example.com",
                PasswordHash = "hash",
                Role = "User",
                RegistrationDate = DateTime.UtcNow,
                IsBlocked = false,
            };

            var request = new UpdateUserRequest
            {
                CurrentPassword = "currentPass",
                Email = "new@example.com",
                Nickname = "NewName",
                NewPassword = "newPassword123"
            };

            _userRepoMock.Setup(r => r.GetUserByIdAsync(userId)).ReturnsAsync(user);
            _userRepoMock.Setup(r => r.GetUserByEmailAsync(It.IsAny<string>())).ReturnsAsync((User?)null);
            _userRepoMock.Setup(r => r.GetUserByUsernameAsync(It.IsAny<string>())).ReturnsAsync((User?)null);
            _userRepoMock.Setup(r => r.UpdateUserAsync(user)).Returns(Task.CompletedTask);
            _securityMock.Setup(s => s.VerifyPassword(request.CurrentPassword, user.PasswordHash))
                .Returns(true);
            _securityMock.Setup(s => s.HashPassword(request.NewPassword)).Returns("newHash");


            await _service.UpdateUserAsync(userId, request);

            Assert.Equal("new@example.com", user.Email);
            Assert.Equal("NewName", user.Username);
            Assert.Equal("newHash", user.PasswordHash);
            _automationEventPublisherMock.Verify(x => x.PublishAsync(It.Is<AutomationEvent>(e =>
                e.Type == "password.changed" &&
                e.UserId == userId.ToString() &&
                e.Data != null && (string)e.Data["source"] == "password_change_flow"), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task UpdateUserAsync_ShouldNotPublishEvent_WhenPasswordNotChanged()
        {
            var userId = Guid.NewGuid();
            var user = new User
            {
                Id = userId,
                Username = "OldName",
                Email = "old@example.com",
                PasswordHash = "hash",
                Role = "User",
                RegistrationDate = DateTime.UtcNow,
                IsBlocked = false,
            };

            var request = new UpdateUserRequest
            {
                CurrentPassword = "currentPass",
                Email = "updated@example.com",
                Nickname = "UpdatedName",
                NewPassword = null // No password update
            };

            _userRepoMock.Setup(r => r.GetUserByIdAsync(userId)).ReturnsAsync(user);
            _userRepoMock.Setup(r => r.GetUserByEmailAsync(It.IsAny<string>())).ReturnsAsync((User?)null);
            _userRepoMock.Setup(r => r.GetUserByUsernameAsync(It.IsAny<string>())).ReturnsAsync((User?)null);
            _userRepoMock.Setup(r => r.UpdateUserAsync(user)).Returns(Task.CompletedTask);
            _securityMock.Setup(s => s.VerifyPassword(request.CurrentPassword, user.PasswordHash))
                .Returns(true);

            await _service.UpdateUserAsync(userId, request);

            Assert.Equal("updated@example.com", user.Email);
            Assert.Equal("UpdatedName", user.Username);
            _automationEventPublisherMock.Verify(x => x.PublishAsync(It.IsAny<AutomationEvent>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task UpdateUserAsync_ShouldNotPublishEvent_WhenPersistenceFails()
        {
            var userId = Guid.NewGuid();
            var user = new User
            {
                Id = userId,
                Username = "OldName",
                Email = "old@example.com",
                PasswordHash = "hash",
                Role = "User",
                RegistrationDate = DateTime.UtcNow,
                IsBlocked = false,
            };

            var request = new UpdateUserRequest
            {
                CurrentPassword = "currentPass",
                NewPassword = "newPassword123"
            };

            _userRepoMock.Setup(r => r.GetUserByIdAsync(userId)).ReturnsAsync(user);
            _securityMock.Setup(s => s.VerifyPassword(request.CurrentPassword, user.PasswordHash))
                .Returns(true);
            _securityMock.Setup(s => s.HashPassword(request.NewPassword)).Returns("newHash");
            _userRepoMock.Setup(r => r.UpdateUserAsync(user)).ThrowsAsync(new Exception("DB save failed"));

            await Assert.ThrowsAsync<Exception>(() => _service.UpdateUserAsync(userId, request));

            _automationEventPublisherMock.Verify(x => x.PublishAsync(It.IsAny<AutomationEvent>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task UpdateUserAsync_ShouldSucceedAndLog_WhenPublisherThrowsException()
        {
            var userId = Guid.NewGuid();
            var user = new User
            {
                Id = userId,
                Username = "OldName",
                Email = "old@example.com",
                PasswordHash = "hash",
                Role = "User",
                RegistrationDate = DateTime.UtcNow,
                IsBlocked = false,
            };

            var request = new UpdateUserRequest
            {
                CurrentPassword = "currentPass",
                NewPassword = "newPassword123"
            };

            _userRepoMock.Setup(r => r.GetUserByIdAsync(userId)).ReturnsAsync(user);
            _securityMock.Setup(s => s.VerifyPassword(request.CurrentPassword, user.PasswordHash))
                .Returns(true);
            _securityMock.Setup(s => s.HashPassword(request.NewPassword)).Returns("newHash");
            _userRepoMock.Setup(r => r.UpdateUserAsync(user)).Returns(Task.CompletedTask);
            _automationEventPublisherMock
                .Setup(x => x.PublishAsync(It.IsAny<AutomationEvent>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new HttpRequestException("Publisher down"));

            await _service.UpdateUserAsync(userId, request);

            Assert.Equal("newHash", user.PasswordHash);
            _userRepoMock.Verify(r => r.UpdateUserAsync(user), Times.Once);
            _automationEventPublisherMock.Verify(x => x.PublishAsync(It.IsAny<AutomationEvent>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task UpdateUserAsync_EventPayload_ShouldNotContainSensitiveData()
        {
            var userId = Guid.NewGuid();
            var user = new User
            {
                Id = userId,
                Username = "OldName",
                Email = "old@example.com",
                PasswordHash = "$2a$11$OldHashedPasswordValue",
                Role = "User",
                RegistrationDate = DateTime.UtcNow,
                IsBlocked = false,
            };

            var request = new UpdateUserRequest
            {
                CurrentPassword = "currentPassSecret123!",
                NewPassword = "newPasswordSecret456!"
            };

            _userRepoMock.Setup(r => r.GetUserByIdAsync(userId)).ReturnsAsync(user);
            _securityMock.Setup(s => s.VerifyPassword(request.CurrentPassword, user.PasswordHash))
                .Returns(true);
            _securityMock.Setup(s => s.HashPassword(request.NewPassword)).Returns("$2a$11$NewHashedPasswordValue");
            _userRepoMock.Setup(r => r.UpdateUserAsync(user)).Returns(Task.CompletedTask);

            AutomationEvent? capturedEvent = null;
            _automationEventPublisherMock
                .Setup(x => x.PublishAsync(It.IsAny<AutomationEvent>(), It.IsAny<CancellationToken>()))
                .Callback<AutomationEvent, CancellationToken>((e, _) => capturedEvent = e)
                .Returns(Task.CompletedTask);

            await _service.UpdateUserAsync(userId, request);

            Assert.NotNull(capturedEvent);
            var json = System.Text.Json.JsonSerializer.Serialize(capturedEvent);

            Assert.DoesNotContain("currentPassSecret123!", json);
            Assert.DoesNotContain("newPasswordSecret456!", json);
            Assert.DoesNotContain("$2a$11$OldHashedPasswordValue", json);
            Assert.DoesNotContain("$2a$11$NewHashedPasswordValue", json);
            Assert.DoesNotContain("PasswordHash", json);
        }

        [Fact]
        public async Task UpdateUserLanguageAsync_UpdatesLanguage()
        {
            var userId = Guid.NewGuid();
            var user = new User
            {
                Id = userId,
                Username = "User",
                Email = "user@example.com",
                PasswordHash = "hash",
                Role = "User",
                RegistrationDate = DateTime.UtcNow,
                IsBlocked = false,
            };

            _userRepoMock.Setup(r => r.GetUserByIdAsync(userId)).ReturnsAsync(user);
            _userRepoMock.Setup(r => r.UpdateUserAsync(user)).Returns(Task.CompletedTask);

            await _service.UpdateUserLanguageAsync(userId, LanguageType.EN);

            Assert.Equal(LanguageType.EN, user.Language);
        }

        [Fact]
        public async Task GetInternalUserByIdAsync_ReturnsInternalUserDto_WhenUserExists()
        {
            var userId = Guid.NewGuid();
            var regDate = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
            var user = new User
            {
                Id = userId,
                Username = "johndoe",
                Email = "john@example.com",
                PasswordHash = "super-secret-hash",
                Role = "User",
                Language = LanguageType.EN,
                IsActive = true,
                IsBlocked = false,
                RegistrationDate = regDate,
            };

            _userRepoMock.Setup(r => r.GetUserAsync(It.IsAny<System.Linq.Expressions.Expression<Func<User, bool>>>()))
                .ReturnsAsync(user);

            var result = await _service.GetInternalUserByIdAsync(userId);

            Assert.NotNull(result);
            Assert.Equal(userId, result.Id);
            Assert.Equal("johndoe", result.Username);
            Assert.Equal("john@example.com", result.Email);
            Assert.Equal("User", result.Role);
            Assert.Equal("EN", result.Language);
            Assert.True(result.IsActive);
            Assert.False(result.IsBlocked);
            Assert.Equal(regDate, result.RegistrationDate);
        }

        [Fact]
        public async Task GetInternalUserByIdAsync_ReturnsNull_WhenUserDoesNotExist()
        {
            var userId = Guid.NewGuid();
            _userRepoMock.Setup(r => r.GetUserAsync(It.IsAny<System.Linq.Expressions.Expression<Func<User, bool>>>()))
                .ReturnsAsync((User?)null);

            var result = await _service.GetInternalUserByIdAsync(userId);

            Assert.Null(result);
        }

        [Fact]
        public async Task GetInternalUserByIdAsync_ReturnsNull_WhenIdIsEmpty()
        {
            var result = await _service.GetInternalUserByIdAsync(Guid.Empty);

            Assert.Null(result);
            _userRepoMock.Verify(r => r.GetUserAsync(It.IsAny<System.Linq.Expressions.Expression<Func<User, bool>>>()), Times.Never);
        }

        [Fact]
        public async Task GetInternalUserByIdAsync_ReturnsBlockedUser_WithoutThrowing()
        {
            var userId = Guid.NewGuid();
            var user = new User
            {
                Id = userId,
                Username = "blockeduser",
                Email = "blocked@example.com",
                PasswordHash = "hash",
                Role = "User",
                Language = LanguageType.RU,
                IsActive = false,
                IsBlocked = true,
                RegistrationDate = DateTime.UtcNow,
            };

            _userRepoMock.Setup(r => r.GetUserAsync(It.IsAny<System.Linq.Expressions.Expression<Func<User, bool>>>()))
                .ReturnsAsync(user);

            var result = await _service.GetInternalUserByIdAsync(userId);

            Assert.NotNull(result);
            Assert.True(result.IsBlocked);
            Assert.False(result.IsActive);
            Assert.Equal("RU", result.Language);
        }

        [Fact]
        public async Task SearchInternalUsersAsync_ReturnsInternalUserDtos_WhenMatchingUsersExist()
        {
            var userId1 = Guid.NewGuid();
            var userId2 = Guid.NewGuid();
            var regDate1 = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);
            var regDate2 = new DateTime(2026, 1, 2, 10, 0, 0, DateTimeKind.Utc);

            var users = new List<User>
            {
                new()
                {
                    Id = userId1,
                    Username = "alice",
                    Email = "alice@example.com",
                    PasswordHash = "hashed-pass-1",
                    Avatar = new byte[] { 1, 2, 3 },
                    Role = "Admin",
                    Language = LanguageType.EN,
                    IsActive = true,
                    IsBlocked = false,
                    RegistrationDate = regDate1
                },
                new()
                {
                    Id = userId2,
                    Username = "bob",
                    Email = "bob@example.com",
                    PasswordHash = "hashed-pass-2",
                    Avatar = null,
                    Role = "User",
                    Language = LanguageType.RU,
                    IsActive = false,
                    IsBlocked = true,
                    RegistrationDate = regDate2
                }
            };

            _userRepoMock.Setup(r => r.SearchUsersAsync("example", 20))
                .ReturnsAsync(users);

            var result = await _service.SearchInternalUsersAsync("example", 20);

            Assert.NotNull(result);
            Assert.Equal(2, result.Count);

            Assert.Equal(userId1, result[0].Id);
            Assert.Equal("alice", result[0].Username);
            Assert.Equal("alice@example.com", result[0].Email);
            Assert.Equal("Admin", result[0].Role);
            Assert.Equal("EN", result[0].Language);
            Assert.True(result[0].IsActive);
            Assert.False(result[0].IsBlocked);
            Assert.Equal(regDate1, result[0].RegistrationDate);

            Assert.Equal(userId2, result[1].Id);
            Assert.Equal("bob", result[1].Username);
            Assert.Equal("bob@example.com", result[1].Email);
            Assert.Equal("User", result[1].Role);
            Assert.Equal("RU", result[1].Language);
            Assert.False(result[1].IsActive);
            Assert.True(result[1].IsBlocked);
            Assert.Equal(regDate2, result[1].RegistrationDate);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public async Task SearchInternalUsersAsync_ReturnsEmptyList_WhenQueryIsNullOrWhitespace(string? query)
        {
            var result = await _service.SearchInternalUsersAsync(query!, 20);

            Assert.NotNull(result);
            Assert.Empty(result);
            _userRepoMock.Verify(r => r.SearchUsersAsync(It.IsAny<string>(), It.IsAny<int>()), Times.Never);
        }

        [Fact]
        public async Task GetInternalUsersPagedAsync_MapsToInternalUserDto_AndExcludesSensitiveFields()
        {
            var userId = Guid.NewGuid();
            var regDate = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
            var users = new List<User>
            {
                new()
                {
                    Id = userId,
                    Username = "safe_user",
                    Email = "safe@example.com",
                    PasswordHash = "super-secret-password-hash",
                    Avatar = new byte[] { 1, 2, 3 },
                    Role = "User",
                    Language = LanguageType.EN,
                    IsActive = true,
                    IsBlocked = false,
                    RegistrationDate = regDate
                }
            };

            var pagedUsers = new PagedResult<User>
            {
                Items = users,
                TotalCount = 1,
                Page = 1,
                PageSize = 50
            };

            _userRepoMock.Setup(r => r.GetInternalUsersPagedAsync(1, 50, true, true))
                .ReturnsAsync(pagedUsers);

            var result = await _service.GetInternalUsersPagedAsync(1, 50, true, true);

            Assert.NotNull(result);
            Assert.Single(result.Items);
            Assert.Equal(1, result.TotalCount);
            Assert.Equal(1, result.Page);
            Assert.Equal(50, result.PageSize);
            Assert.False(result.HasNextPage);

            var item = result.Items[0];
            Assert.Equal(userId, item.Id);
            Assert.Equal("safe_user", item.Username);
            Assert.Equal("safe@example.com", item.Email);
            Assert.Equal("User", item.Role);
            Assert.Equal("EN", item.Language);
            Assert.True(item.IsActive);
            Assert.False(item.IsBlocked);
            Assert.Equal(regDate, item.RegistrationDate);
        }

        [Fact]
        public async Task UpdateUserLanguageAsync_ShouldPublishUserLanguageChangedEvent_WhenLanguageChanges()
        {
            var userId = Guid.NewGuid();
            var user = new User
            {
                Id = userId,
                Username = "polyglot_user",
                Email = "polyglot@example.com",
                PasswordHash = "hashed-secret",
                Role = "User",
                Language = LanguageType.EN,
                IsActive = true,
                IsBlocked = false,
                RegistrationDate = DateTime.UtcNow.AddMonths(-2)
            };

            _userRepoMock.Setup(r => r.GetUserByIdAsync(userId)).ReturnsAsync(user);
            _userRepoMock.Setup(r => r.UpdateUserAsync(user)).Returns(Task.CompletedTask);

            AutomationEvent? capturedEvent = null;
            _automationEventPublisherMock
                .Setup(x => x.PublishAsync(It.IsAny<AutomationEvent>(), It.IsAny<CancellationToken>()))
                .Callback<AutomationEvent, CancellationToken>((e, _) => capturedEvent = e)
                .Returns(Task.CompletedTask);

            await _service.UpdateUserLanguageAsync(userId, LanguageType.UA);

            Assert.Equal(LanguageType.UA, user.Language);
            _userRepoMock.Verify(r => r.UpdateUserAsync(user), Times.Once);
            _automationEventPublisherMock.Verify(x => x.PublishAsync(It.IsAny<AutomationEvent>(), It.IsAny<CancellationToken>()), Times.Once);

            Assert.NotNull(capturedEvent);
            Assert.Equal("user.language_changed", capturedEvent.Type);
            Assert.Equal(userId.ToString(), capturedEvent.UserId);
            Assert.False(string.IsNullOrWhiteSpace(capturedEvent.Id));
            Assert.NotEqual(userId.ToString(), capturedEvent.Id);
            Assert.Equal(DateTimeKind.Utc, capturedEvent.OccurredAt.Kind);

            Assert.NotNull(capturedEvent.User);
            Assert.Equal(userId, capturedEvent.User.Id);
            Assert.Equal("polyglot_user", capturedEvent.User.Username);
            Assert.Equal("polyglot@example.com", capturedEvent.User.Email);
            Assert.Equal("UA", capturedEvent.User.Language);
            Assert.True(capturedEvent.User.IsActive);
            Assert.False(capturedEvent.User.IsBlocked);

            Assert.NotNull(capturedEvent.Data);
            Assert.Equal("language_change_flow", capturedEvent.Data["source"]);
        }

        [Fact]
        public async Task UpdateUserLanguageAsync_ShouldNotPublishEvent_WhenLanguageUnchanged()
        {
            var userId = Guid.NewGuid();
            var user = new User
            {
                Id = userId,
                Username = "same_lang_user",
                Email = "same@example.com",
                PasswordHash = "hash",
                Role = "User",
                Language = LanguageType.EN,
                IsActive = true,
                IsBlocked = false,
                RegistrationDate = DateTime.UtcNow
            };

            _userRepoMock.Setup(r => r.GetUserByIdAsync(userId)).ReturnsAsync(user);

            await _service.UpdateUserLanguageAsync(userId, LanguageType.EN);

            _userRepoMock.Verify(r => r.UpdateUserAsync(It.IsAny<User>()), Times.Never);
            _automationEventPublisherMock.Verify(x => x.PublishAsync(It.IsAny<AutomationEvent>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task UpdateUserLanguageAsync_ShouldNotPublishEvent_WhenUserNotFound()
        {
            var userId = Guid.NewGuid();
            _userRepoMock.Setup(r => r.GetUserByIdAsync(userId)).ReturnsAsync((User?)null);

            await Assert.ThrowsAsync<InvalidOperationException>(() => _service.UpdateUserLanguageAsync(userId, LanguageType.DE));

            _userRepoMock.Verify(r => r.UpdateUserAsync(It.IsAny<User>()), Times.Never);
            _automationEventPublisherMock.Verify(x => x.PublishAsync(It.IsAny<AutomationEvent>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task UpdateUserLanguageAsync_ShouldNotPublishEvent_WhenPersistenceFails()
        {
            var userId = Guid.NewGuid();
            var user = new User
            {
                Id = userId,
                Username = "user1",
                Email = "user1@example.com",
                PasswordHash = "hash",
                Role = "User",
                Language = LanguageType.EN,
                IsActive = true,
                IsBlocked = false,
                RegistrationDate = DateTime.UtcNow
            };

            _userRepoMock.Setup(r => r.GetUserByIdAsync(userId)).ReturnsAsync(user);
            _userRepoMock.Setup(r => r.UpdateUserAsync(user)).ThrowsAsync(new Exception("DB failure"));

            await Assert.ThrowsAsync<Exception>(() => _service.UpdateUserLanguageAsync(userId, LanguageType.FR));

            _automationEventPublisherMock.Verify(x => x.PublishAsync(It.IsAny<AutomationEvent>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task UpdateUserLanguageAsync_ShouldSucceedAndLog_WhenPublisherThrowsException()
        {
            var userId = Guid.NewGuid();
            var user = new User
            {
                Id = userId,
                Username = "user1",
                Email = "user1@example.com",
                PasswordHash = "hash",
                Role = "User",
                Language = LanguageType.EN,
                IsActive = true,
                IsBlocked = false,
                RegistrationDate = DateTime.UtcNow
            };

            _userRepoMock.Setup(r => r.GetUserByIdAsync(userId)).ReturnsAsync(user);
            _userRepoMock.Setup(r => r.UpdateUserAsync(user)).Returns(Task.CompletedTask);
            _automationEventPublisherMock
                .Setup(x => x.PublishAsync(It.IsAny<AutomationEvent>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new Exception("Publisher offline"));

            // Operation should succeed without throwing
            await _service.UpdateUserLanguageAsync(userId, LanguageType.JA);

            Assert.Equal(LanguageType.JA, user.Language);
            _userRepoMock.Verify(r => r.UpdateUserAsync(user), Times.Once);
            _automationEventPublisherMock.Verify(x => x.PublishAsync(It.IsAny<AutomationEvent>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task UpdateUserLanguageAsync_EventPayload_ShouldNotContainSensitiveAuthenticationData()
        {
            var userId = Guid.NewGuid();
            var user = new User
            {
                Id = userId,
                Username = "user1",
                Email = "user1@example.com",
                PasswordHash = "supersecret_hash",
                Role = "User",
                Language = LanguageType.EN,
                IsActive = true,
                IsBlocked = false,
                RegistrationDate = DateTime.UtcNow
            };

            _userRepoMock.Setup(r => r.GetUserByIdAsync(userId)).ReturnsAsync(user);
            _userRepoMock.Setup(r => r.UpdateUserAsync(user)).Returns(Task.CompletedTask);

            AutomationEvent? capturedEvent = null;
            _automationEventPublisherMock
                .Setup(x => x.PublishAsync(It.IsAny<AutomationEvent>(), It.IsAny<CancellationToken>()))
                .Callback<AutomationEvent, CancellationToken>((e, _) => capturedEvent = e)
                .Returns(Task.CompletedTask);

            await _service.UpdateUserLanguageAsync(userId, LanguageType.ZH);

            Assert.NotNull(capturedEvent);

            var forbiddenSubstrings = new[]
            {
                "password", "hash", "salt", "token", "secret", "session", "code", "refresh"
            };

            var userProperties = typeof(InternalUserDto).GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
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

        [Fact]
        public async Task UpdateUserAsync_UpdatesLastActivityAt_InUtc()
        {
            var userId = Guid.NewGuid();
            var user = new User
            {
                Id = userId,
                Username = "testuser",
                Email = "test@example.com",
                RegistrationDate = DateTime.UtcNow.AddMonths(-3),
                PasswordHash = "oldhash",
                Role = "User",
                IsActive = true,
                IsBlocked = false,
                LastActivityAt = null
            };

            _userRepoMock.Setup(r => r.GetUserByIdAsync(userId)).ReturnsAsync(user);
            _userRepoMock.Setup(r => r.UpdateUserAsync(It.IsAny<User>())).Returns(Task.CompletedTask);
            _securityMock.Setup(s => s.VerifyPassword("currentpass", "oldhash")).Returns(true);

            var before = DateTime.UtcNow.AddSeconds(-1);
            await _service.UpdateUserAsync(userId, new UpdateUserRequest { CurrentPassword = "currentpass", Nickname = "newnick" });
            var after = DateTime.UtcNow.AddSeconds(1);

            Assert.NotNull(user.LastActivityAt);
            Assert.True(user.LastActivityAt >= before && user.LastActivityAt <= after);
            Assert.Equal(DateTimeKind.Utc, user.LastActivityAt.Value.Kind);
        }

        [Fact]
        public async Task UpdateUserLanguageAsync_UpdatesLastActivityAt_InUtc()
        {
            var userId = Guid.NewGuid();
            var user = new User
            {
                Id = userId,
                Username = "testuser",
                Email = "test@example.com",
                RegistrationDate = DateTime.UtcNow.AddMonths(-3),
                PasswordHash = "hash",
                Role = "User",
                Language = LanguageType.EN,
                IsActive = true,
                IsBlocked = false,
                LastActivityAt = null
            };

            _userRepoMock.Setup(r => r.GetUserByIdAsync(userId)).ReturnsAsync(user);
            _userRepoMock.Setup(r => r.UpdateUserAsync(It.IsAny<User>())).Returns(Task.CompletedTask);

            var before = DateTime.UtcNow.AddSeconds(-1);
            await _service.UpdateUserLanguageAsync(userId, LanguageType.UA);
            var after = DateTime.UtcNow.AddSeconds(1);

            Assert.NotNull(user.LastActivityAt);
            Assert.True(user.LastActivityAt >= before && user.LastActivityAt <= after);
            Assert.Equal(DateTimeKind.Utc, user.LastActivityAt.Value.Kind);
        }

        [Fact]
        public async Task GetInternalUserByIdAsync_ReturnsLastActivityAt_WhenSetOrNull()
        {
            var userIdWithActivity = Guid.NewGuid();
            var activityTime = DateTime.UtcNow.AddDays(-10);
            var userWithActivity = new User
            {
                Id = userIdWithActivity,
                Username = "active_user",
                Email = "active@example.com",
                RegistrationDate = DateTime.UtcNow.AddDays(-60),
                PasswordHash = "hash",
                Role = "User",
                IsActive = true,
                IsBlocked = false,
                LastActivityAt = activityTime
            };

            var userIdWithoutActivity = Guid.NewGuid();
            var userWithoutActivity = new User
            {
                Id = userIdWithoutActivity,
                Username = "inactive_user",
                Email = "inactive@example.com",
                RegistrationDate = DateTime.UtcNow.AddDays(-60),
                PasswordHash = "hash",
                Role = "User",
                IsActive = true,
                IsBlocked = false,
                LastActivityAt = null
            };

            _userRepoMock.Setup(r => r.GetUserAsync(It.Is<System.Linq.Expressions.Expression<Func<User, bool>>>(e => true)))
                .ReturnsAsync((System.Linq.Expressions.Expression<Func<User, bool>> expr) =>
                {
                    var func = expr.Compile();
                    if (func(userWithActivity)) return userWithActivity;
                    if (func(userWithoutActivity)) return userWithoutActivity;
                    return null;
                });

            var resultWith = await _service.GetInternalUserByIdAsync(userIdWithActivity);
            var resultWithout = await _service.GetInternalUserByIdAsync(userIdWithoutActivity);

            Assert.NotNull(resultWith);
            Assert.Equal(activityTime, resultWith.LastActivityAt);

            Assert.NotNull(resultWithout);
            Assert.Null(resultWithout.LastActivityAt);
        }
    }
}

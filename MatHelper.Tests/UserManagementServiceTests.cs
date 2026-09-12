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
        private readonly Mock<ILogger<UserManagementService>> _loggerMock;
        private readonly UserManagementService _service;

        public UserManagementServiceTests()
        {
            _userRepoMock = new Mock<IUserRepository>();
            _twoFactorMock = new Mock<ITwoFactorService>();
            _securityMock = new Mock<ISecurityService>();
            _loggerMock = new Mock<ILogger<UserManagementService>>();

            _service = new UserManagementService(
                _userRepoMock.Object,
                _twoFactorMock.Object,
                _securityMock.Object,
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
    }
}

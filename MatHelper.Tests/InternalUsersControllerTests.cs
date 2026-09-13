using MatHelper.API.Common;
using MatHelper.API.Controllers;
using MatHelper.BLL.Interfaces;
using MatHelper.CORE.Enums;
using MatHelper.CORE.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using System.Security.Claims;
using Xunit;

namespace MatHelper.Tests.Controllers
{
    public class InternalUsersControllerTests
    {
        private readonly Mock<IUserManagementService> _userServiceMock;
        private readonly Mock<ITokenService> _tokenServiceMock;
        private readonly Mock<ILogger<InternalUsersController>> _loggerMock;
        private readonly InternalUsersController _controller;

        public InternalUsersControllerTests()
        {
            _userServiceMock = new Mock<IUserManagementService>();
            _tokenServiceMock = new Mock<ITokenService>();
            _loggerMock = new Mock<ILogger<InternalUsersController>>();

            _controller = new InternalUsersController(
                _userServiceMock.Object,
                _tokenServiceMock.Object,
                _loggerMock.Object
            );

            var httpContext = new DefaultHttpContext();
            _controller.ControllerContext = new ControllerContext
            {
                HttpContext = httpContext
            };
        }

        [Fact]
        public void Controller_HasAuthorizeAttribute_WithAdminOwnerAndServiceRoles()
        {
            var authAttribute = typeof(InternalUsersController)
                .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
                .FirstOrDefault() as AuthorizeAttribute;

            Assert.NotNull(authAttribute);
            Assert.Contains("Admin", authAttribute.Roles);
            Assert.Contains("Owner", authAttribute.Roles);
            Assert.Contains("Service", authAttribute.Roles);
            Assert.DoesNotContain("User", authAttribute.Roles);
        }

        [Fact]
        public void AdminController_DoesNotAllowServiceRole_NarrowScopePreserved()
        {
            var authAttribute = typeof(AdminController)
                .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
                .FirstOrDefault() as AuthorizeAttribute;

            Assert.NotNull(authAttribute);
            Assert.Contains("Admin", authAttribute.Roles);
            Assert.Contains("Owner", authAttribute.Roles);
            Assert.DoesNotContain("Service", authAttribute.Roles);
        }

        [Fact]
        public async Task GetUserById_ReturnsOk_WithInternalUserDto_WhenAuthorizedAsAdmin()
        {
            var userId = Guid.NewGuid();
            var expectedUser = new InternalUserDto
            {
                Id = userId,
                Username = "authoritative_user",
                Email = "authoritative@example.com",
                Role = "User",
                Language = "EN",
                IsActive = true,
                IsBlocked = false,
                RegistrationDate = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc)
            };

            _controller.ControllerContext.HttpContext.User = new ClaimsPrincipal(
                new ClaimsIdentity(new[] { new Claim(ClaimTypes.Role, "Admin"), new Claim(ClaimTypes.Name, Guid.NewGuid().ToString()) }, "TestAuth"));

            _tokenServiceMock.Setup(t => t.ValidateAdminAccessAsync(It.IsAny<HttpRequest>(), It.IsAny<ClaimsPrincipal>()))
                .ReturnsAsync(TokenValidationResult.Valid);

            _userServiceMock.Setup(s => s.GetInternalUserByIdAsync(userId))
                .ReturnsAsync(expectedUser);

            var actionResult = await _controller.GetUserById(userId.ToString());

            var okResult = Assert.IsType<OkObjectResult>(actionResult);
            var apiResponse = Assert.IsType<ApiResponse<InternalUserDto>>(okResult.Value);

            Assert.True(apiResponse.Success);
            Assert.NotNull(apiResponse.Data);
            Assert.Equal(userId, apiResponse.Data.Id);
            Assert.Equal("authoritative_user", apiResponse.Data.Username);
            Assert.Equal("authoritative@example.com", apiResponse.Data.Email);
            Assert.Equal("User", apiResponse.Data.Role);
            Assert.Equal("EN", apiResponse.Data.Language);
            Assert.True(apiResponse.Data.IsActive);
            Assert.False(apiResponse.Data.IsBlocked);
            _tokenServiceMock.Verify(t => t.ValidateAdminAccessAsync(It.IsAny<HttpRequest>(), It.IsAny<ClaimsPrincipal>()), Times.Once);
        }

        [Fact]
        public async Task GetUserById_ReturnsOk_WithInternalUserDto_WhenAuthorizedAsOwner()
        {
            var userId = Guid.NewGuid();
            var expectedUser = new InternalUserDto
            {
                Id = userId,
                Username = "authoritative_user",
                Email = "authoritative@example.com",
                Role = "User",
                Language = "EN",
                IsActive = true,
                IsBlocked = false,
                RegistrationDate = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc)
            };

            _controller.ControllerContext.HttpContext.User = new ClaimsPrincipal(
                new ClaimsIdentity(new[] { new Claim(ClaimTypes.Role, "Owner"), new Claim(ClaimTypes.Name, Guid.NewGuid().ToString()) }, "TestAuth"));

            _tokenServiceMock.Setup(t => t.ValidateAdminAccessAsync(It.IsAny<HttpRequest>(), It.IsAny<ClaimsPrincipal>()))
                .ReturnsAsync(TokenValidationResult.Valid);

            _userServiceMock.Setup(s => s.GetInternalUserByIdAsync(userId))
                .ReturnsAsync(expectedUser);

            var actionResult = await _controller.GetUserById(userId.ToString());

            var okResult = Assert.IsType<OkObjectResult>(actionResult);
            var apiResponse = Assert.IsType<ApiResponse<InternalUserDto>>(okResult.Value);

            Assert.True(apiResponse.Success);
            Assert.NotNull(apiResponse.Data);
            _tokenServiceMock.Verify(t => t.ValidateAdminAccessAsync(It.IsAny<HttpRequest>(), It.IsAny<ClaimsPrincipal>()), Times.Once);
        }

        [Fact]
        public async Task GetUserById_ReturnsOk_WhenAuthorizedAsService_WithoutCallingValidateAdminAccess()
        {
            var userId = Guid.NewGuid();
            var expectedUser = new InternalUserDto
            {
                Id = userId,
                Username = "service_resolved_user",
                Email = "resolved@example.com",
                Role = "User",
                Language = "EN",
                IsActive = true,
                IsBlocked = false,
                RegistrationDate = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc)
            };

            // Set principal to Service role
            _controller.ControllerContext.HttpContext.User = new ClaimsPrincipal(
                new ClaimsIdentity(new[]
                {
                    new Claim(ClaimTypes.Role, "Service"),
                    new Claim(ClaimTypes.Name, "gmhelper-notify-api")
                }, "TestAuth"));

            _userServiceMock.Setup(s => s.GetInternalUserByIdAsync(userId))
                .ReturnsAsync(expectedUser);

            var actionResult = await _controller.GetUserById(userId.ToString());

            var okResult = Assert.IsType<OkObjectResult>(actionResult);
            var apiResponse = Assert.IsType<ApiResponse<InternalUserDto>>(okResult.Value);

            Assert.True(apiResponse.Success);
            Assert.NotNull(apiResponse.Data);
            Assert.Equal(userId, apiResponse.Data.Id);
            Assert.Equal("service_resolved_user", apiResponse.Data.Username);

            // Verify that ValidateAdminAccessAsync (which checks login_tokens DB) was NEVER called for Service identity
            _tokenServiceMock.Verify(t => t.ValidateAdminAccessAsync(It.IsAny<HttpRequest>(), It.IsAny<ClaimsPrincipal>()), Times.Never);
        }

        [Fact]
        public async Task GetUserById_WithServiceRole_ReturnsNotFound_WhenUserDoesNotExist()
        {
            var userId = Guid.NewGuid();

            _controller.ControllerContext.HttpContext.User = new ClaimsPrincipal(
                new ClaimsIdentity(new[] { new Claim(ClaimTypes.Role, "Service"), new Claim(ClaimTypes.Name, "gmhelper-notify-api") }, "TestAuth"));

            _userServiceMock.Setup(s => s.GetInternalUserByIdAsync(userId))
                .ReturnsAsync((InternalUserDto?)null);

            var actionResult = await _controller.GetUserById(userId.ToString());

            var notFoundResult = Assert.IsType<NotFoundObjectResult>(actionResult);
            var apiResponse = Assert.IsType<ApiResponse<string>>(notFoundResult.Value);

            Assert.False(apiResponse.Success);
            Assert.Equal("User not found.", apiResponse.Message);
            _tokenServiceMock.Verify(t => t.ValidateAdminAccessAsync(It.IsAny<HttpRequest>(), It.IsAny<ClaimsPrincipal>()), Times.Never);
        }

        [Fact]
        public async Task GetUserById_WithServiceRole_ReturnsNotFound_WhenIdIsInvalidGuid()
        {
            _controller.ControllerContext.HttpContext.User = new ClaimsPrincipal(
                new ClaimsIdentity(new[] { new Claim(ClaimTypes.Role, "Service"), new Claim(ClaimTypes.Name, "gmhelper-notify-api") }, "TestAuth"));

            var actionResult = await _controller.GetUserById("invalid-uuid-format");

            var notFoundResult = Assert.IsType<NotFoundObjectResult>(actionResult);
            var apiResponse = Assert.IsType<ApiResponse<string>>(notFoundResult.Value);

            Assert.False(apiResponse.Success);
            Assert.Equal("User not found.", apiResponse.Message);
        }

        [Fact]
        public async Task GetUserById_ReturnsNotFound_WhenIdIsEmptyGuid()
        {
            _tokenServiceMock.Setup(t => t.ValidateAdminAccessAsync(It.IsAny<HttpRequest>(), It.IsAny<ClaimsPrincipal>()))
                .ReturnsAsync(TokenValidationResult.Valid);

            var actionResult = await _controller.GetUserById(Guid.Empty.ToString());

            var notFoundResult = Assert.IsType<NotFoundObjectResult>(actionResult);
            var apiResponse = Assert.IsType<ApiResponse<string>>(notFoundResult.Value);

            Assert.False(apiResponse.Success);
            Assert.Equal("User not found.", apiResponse.Message);
        }

        [Fact]
        public async Task GetUserById_ReturnsUnauthorized_WhenTokenMissing()
        {
            _tokenServiceMock.Setup(t => t.ValidateAdminAccessAsync(It.IsAny<HttpRequest>(), It.IsAny<ClaimsPrincipal>()))
                .ReturnsAsync(TokenValidationResult.MissingToken);

            var actionResult = await _controller.GetUserById(Guid.NewGuid().ToString());

            var unauthResult = Assert.IsType<UnauthorizedObjectResult>(actionResult);
            var apiResponse = Assert.IsType<ApiResponse<string>>(unauthResult.Value);

            Assert.False(apiResponse.Success);
            Assert.Equal("Authorization header is missing or invalid", apiResponse.Message);
        }

        [Fact]
        public async Task GetUserById_ReturnsUnauthorized_WhenTokenInactive()
        {
            _tokenServiceMock.Setup(t => t.ValidateAdminAccessAsync(It.IsAny<HttpRequest>(), It.IsAny<ClaimsPrincipal>()))
                .ReturnsAsync(TokenValidationResult.InactiveToken);

            var actionResult = await _controller.GetUserById(Guid.NewGuid().ToString());

            var unauthResult = Assert.IsType<UnauthorizedObjectResult>(actionResult);
            var apiResponse = Assert.IsType<ApiResponse<string>>(unauthResult.Value);

            Assert.False(apiResponse.Success);
            Assert.Equal("User token is not active.", apiResponse.Message);
        }

        [Fact]
        public async Task GetUserById_ReturnsForbid_WhenCallerLacksAdminPermissions()
        {
            _tokenServiceMock.Setup(t => t.ValidateAdminAccessAsync(It.IsAny<HttpRequest>(), It.IsAny<ClaimsPrincipal>()))
                .ReturnsAsync(TokenValidationResult.NoAdminPermissions);

            var actionResult = await _controller.GetUserById(Guid.NewGuid().ToString());

            Assert.IsType<ForbidResult>(actionResult);
        }

        [Fact]
        public async Task SearchUsers_ReturnsOk_WithInternalUserDtos_WhenAuthorizedAsService()
        {
            var expectedUsers = new List<InternalUserDto>
            {
                new()
                {
                    Id = Guid.NewGuid(),
                    Username = "alice",
                    Email = "alice@example.com",
                    Role = "User",
                    Language = "EN",
                    IsActive = true,
                    IsBlocked = false,
                    RegistrationDate = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc)
                }
            };

            _controller.ControllerContext.HttpContext.User = new ClaimsPrincipal(
                new ClaimsIdentity(new[]
                {
                    new Claim(ClaimTypes.Role, "Service"),
                    new Claim(ClaimTypes.Name, "gmhelper-notify-api")
                }, "TestAuth"));

            _userServiceMock.Setup(s => s.SearchInternalUsersAsync("ali", 20))
                .ReturnsAsync(expectedUsers);

            var actionResult = await _controller.SearchUsers("ali", 20);

            var okResult = Assert.IsType<OkObjectResult>(actionResult);
            var apiResponse = Assert.IsType<ApiResponse<List<InternalUserDto>>>(okResult.Value);

            Assert.True(apiResponse.Success);
            Assert.NotNull(apiResponse.Data);
            Assert.Single(apiResponse.Data);
            Assert.Equal("alice", apiResponse.Data[0].Username);
            Assert.Equal("alice@example.com", apiResponse.Data[0].Email);
            _tokenServiceMock.Verify(t => t.ValidateAdminAccessAsync(It.IsAny<HttpRequest>(), It.IsAny<ClaimsPrincipal>()), Times.Never);
        }

        [Fact]
        public async Task SearchUsers_ReturnsOk_WhenAuthorizedAsAdmin()
        {
            var expectedUsers = new List<InternalUserDto>
            {
                new()
                {
                    Id = Guid.NewGuid(),
                    Username = "bob",
                    Email = "bob@example.com",
                    Role = "User",
                    Language = "EN",
                    IsActive = true,
                    IsBlocked = false,
                    RegistrationDate = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc)
                }
            };

            _controller.ControllerContext.HttpContext.User = new ClaimsPrincipal(
                new ClaimsIdentity(new[] { new Claim(ClaimTypes.Role, "Admin"), new Claim(ClaimTypes.Name, Guid.NewGuid().ToString()) }, "TestAuth"));

            _tokenServiceMock.Setup(t => t.ValidateAdminAccessAsync(It.IsAny<HttpRequest>(), It.IsAny<ClaimsPrincipal>()))
                .ReturnsAsync(TokenValidationResult.Valid);

            _userServiceMock.Setup(s => s.SearchInternalUsersAsync("bob", 20))
                .ReturnsAsync(expectedUsers);

            var actionResult = await _controller.SearchUsers("bob", 20);

            var okResult = Assert.IsType<OkObjectResult>(actionResult);
            var apiResponse = Assert.IsType<ApiResponse<List<InternalUserDto>>>(okResult.Value);

            Assert.True(apiResponse.Success);
            Assert.NotNull(apiResponse.Data);
            Assert.Single(apiResponse.Data);
            _tokenServiceMock.Verify(t => t.ValidateAdminAccessAsync(It.IsAny<HttpRequest>(), It.IsAny<ClaimsPrincipal>()), Times.Once);
        }

        [Fact]
        public async Task SearchUsers_ReturnsOk_WhenAuthorizedAsOwner()
        {
            var expectedUsers = new List<InternalUserDto>
            {
                new()
                {
                    Id = Guid.NewGuid(),
                    Username = "charlie",
                    Email = "charlie@example.com",
                    Role = "Owner",
                    Language = "RU",
                    IsActive = true,
                    IsBlocked = false,
                    RegistrationDate = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc)
                }
            };

            _controller.ControllerContext.HttpContext.User = new ClaimsPrincipal(
                new ClaimsIdentity(new[] { new Claim(ClaimTypes.Role, "Owner"), new Claim(ClaimTypes.Name, Guid.NewGuid().ToString()) }, "TestAuth"));

            _tokenServiceMock.Setup(t => t.ValidateAdminAccessAsync(It.IsAny<HttpRequest>(), It.IsAny<ClaimsPrincipal>()))
                .ReturnsAsync(TokenValidationResult.Valid);

            _userServiceMock.Setup(s => s.SearchInternalUsersAsync("char", 20))
                .ReturnsAsync(expectedUsers);

            var actionResult = await _controller.SearchUsers("char", 20);

            var okResult = Assert.IsType<OkObjectResult>(actionResult);
            var apiResponse = Assert.IsType<ApiResponse<List<InternalUserDto>>>(okResult.Value);

            Assert.True(apiResponse.Success);
            Assert.NotNull(apiResponse.Data);
            Assert.Single(apiResponse.Data);
            _tokenServiceMock.Verify(t => t.ValidateAdminAccessAsync(It.IsAny<HttpRequest>(), It.IsAny<ClaimsPrincipal>()), Times.Once);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("\t\n")]
        public async Task SearchUsers_ReturnsBadRequest_WhenQueryIsNullOrWhitespace(string? query)
        {
            _controller.ControllerContext.HttpContext.User = new ClaimsPrincipal(
                new ClaimsIdentity(new[] { new Claim(ClaimTypes.Role, "Service"), new Claim(ClaimTypes.Name, "gmhelper-notify-api") }, "TestAuth"));

            var actionResult = await _controller.SearchUsers(query, 20);

            var badReqResult = Assert.IsType<BadRequestObjectResult>(actionResult);
            var apiResponse = Assert.IsType<ApiResponse<string>>(badReqResult.Value);

            Assert.False(apiResponse.Success);
            Assert.Equal("Search query is required.", apiResponse.Message);
            _userServiceMock.Verify(s => s.SearchInternalUsersAsync(It.IsAny<string>(), It.IsAny<int>()), Times.Never);
        }

        [Fact]
        public async Task SearchUsers_ReturnsUnauthorized_WhenTokenMissing()
        {
            _tokenServiceMock.Setup(t => t.ValidateAdminAccessAsync(It.IsAny<HttpRequest>(), It.IsAny<ClaimsPrincipal>()))
                .ReturnsAsync(TokenValidationResult.MissingToken);

            var actionResult = await _controller.SearchUsers("test", 20);

            var unauthResult = Assert.IsType<UnauthorizedObjectResult>(actionResult);
            var apiResponse = Assert.IsType<ApiResponse<string>>(unauthResult.Value);

            Assert.False(apiResponse.Success);
            Assert.Equal("Authorization header is missing or invalid", apiResponse.Message);
        }

        [Fact]
        public async Task SearchUsers_ReturnsUnauthorized_WhenTokenInactive()
        {
            _tokenServiceMock.Setup(t => t.ValidateAdminAccessAsync(It.IsAny<HttpRequest>(), It.IsAny<ClaimsPrincipal>()))
                .ReturnsAsync(TokenValidationResult.InactiveToken);

            var actionResult = await _controller.SearchUsers("test", 20);

            var unauthResult = Assert.IsType<UnauthorizedObjectResult>(actionResult);
            var apiResponse = Assert.IsType<ApiResponse<string>>(unauthResult.Value);

            Assert.False(apiResponse.Success);
            Assert.Equal("User token is not active.", apiResponse.Message);
        }

        [Fact]
        public async Task SearchUsers_ReturnsForbid_WhenCallerLacksAdminPermissions()
        {
            _tokenServiceMock.Setup(t => t.ValidateAdminAccessAsync(It.IsAny<HttpRequest>(), It.IsAny<ClaimsPrincipal>()))
                .ReturnsAsync(TokenValidationResult.NoAdminPermissions);

            var actionResult = await _controller.SearchUsers("test", 20);

            Assert.IsType<ForbidResult>(actionResult);
        }

        [Fact]
        public async Task SearchUsers_ReturnsEmptyList_WhenNoUsersMatch()
        {
            _controller.ControllerContext.HttpContext.User = new ClaimsPrincipal(
                new ClaimsIdentity(new[] { new Claim(ClaimTypes.Role, "Service"), new Claim(ClaimTypes.Name, "gmhelper-notify-api") }, "TestAuth"));

            _userServiceMock.Setup(s => s.SearchInternalUsersAsync("nonexistent", 20))
                .ReturnsAsync(new List<InternalUserDto>());

            var actionResult = await _controller.SearchUsers("nonexistent", 20);

            var okResult = Assert.IsType<OkObjectResult>(actionResult);
            var apiResponse = Assert.IsType<ApiResponse<List<InternalUserDto>>>(okResult.Value);

            Assert.True(apiResponse.Success);
            Assert.NotNull(apiResponse.Data);
            Assert.Empty(apiResponse.Data);
        }

        [Fact]
        public async Task SearchUsers_Returns500_WhenServiceThrowsException()
        {
            _controller.ControllerContext.HttpContext.User = new ClaimsPrincipal(
                new ClaimsIdentity(new[] { new Claim(ClaimTypes.Role, "Service"), new Claim(ClaimTypes.Name, "gmhelper-notify-api") }, "TestAuth"));

            _userServiceMock.Setup(s => s.SearchInternalUsersAsync("err", 20))
                .ThrowsAsync(new Exception("Database connection failure"));

            var actionResult = await _controller.SearchUsers("err", 20);

            var statusResult = Assert.IsType<ObjectResult>(actionResult);
            Assert.Equal(StatusCodes.Status500InternalServerError, statusResult.StatusCode);
            var apiResponse = Assert.IsType<ApiResponse<string>>(statusResult.Value);
            Assert.False(apiResponse.Success);
            Assert.Equal("Internal server error.", apiResponse.Message);
        }
    }
}

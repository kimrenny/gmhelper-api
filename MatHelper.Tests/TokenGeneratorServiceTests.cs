using MatHelper.BLL.Services;
using MatHelper.CORE.Models;
using MatHelper.CORE.Options;
using Microsoft.Extensions.Logging;
using Moq;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using Xunit;

namespace MatHelper.Tests.Services
{
    public class TokenGeneratorServiceTests
    {
        private readonly TokenGeneratorService _service;
        private readonly JwtOptions _jwtOptions;
        private readonly Mock<ILogger<TokenService>> _loggerMock;

        public TokenGeneratorServiceTests()
        {
            _jwtOptions = new JwtOptions
            {
                SecretKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
                Issuer = "TestIssuer",
                Audience = "TestAudience"
            };

            _loggerMock = new Mock<ILogger<TokenService>>();

            _service = new TokenGeneratorService(_jwtOptions, _loggerMock.Object);
        }

        [Fact]
        public void GenerateJwtToken_ReturnsValidToken()
        {
            var user = new User
            {
                Id = Guid.NewGuid(),
                Username = "TestUser",
                Email = "test@example.com",
                PasswordHash = "hash",
                Role = "Admin",
                RegistrationDate = DateTime.UtcNow,
                IsBlocked = false,
                LoginTokens = new List<LoginToken>()
            };

            var deviceInfo = new DeviceInfo
            {
                UserAgent = "TestAgent",
                Platform = "Windows"
            };

            var tokenString = _service.GenerateJwtToken(user, deviceInfo);

            Assert.False(string.IsNullOrWhiteSpace(tokenString));

            var handler = new JwtSecurityTokenHandler();
            var token = handler.ReadJwtToken(tokenString);

            Assert.Equal(user.Id.ToString(), token.Claims.First(c => c.Type == System.Security.Claims.ClaimTypes.Name).Value);
            Assert.Equal(user.Role, token.Claims.First(c => c.Type == System.Security.Claims.ClaimTypes.Role).Value);
            Assert.Equal(deviceInfo.UserAgent, token.Claims.First(c => c.Type == "Device").Value);
            Assert.Equal(deviceInfo.Platform, token.Claims.First(c => c.Type == "Platform").Value);
        }

        [Fact]
        public void GenerateJwtToken_Throws_WhenDeviceInfoInvalid()
        {
            var user = new User
            {
                Id = Guid.NewGuid(),
                Username = "TestUser",
                Email = "test@example.com",
                PasswordHash = "hash",
                Role = "User",
                RegistrationDate = DateTime.UtcNow,
                IsBlocked = false,
                LoginTokens = new List<LoginToken>()
            };

            var deviceInfo = new DeviceInfo
            {
                UserAgent = null,
                Platform = "Windows"
            };

            Assert.Throws<InvalidDataException>(() => _service.GenerateJwtToken(user, deviceInfo));

            deviceInfo = new DeviceInfo { UserAgent = "UA", Platform = null };
            Assert.Throws<InvalidDataException>(() => _service.GenerateJwtToken(user, deviceInfo));
        }

        [Fact]
        public void GenerateRefreshToken_ReturnsNonEmptyString()
        {
            var token1 = _service.GenerateRefreshToken();
            var token2 = _service.GenerateRefreshToken();

            Assert.False(string.IsNullOrWhiteSpace(token1));
            Assert.False(string.IsNullOrWhiteSpace(token2));
            Assert.NotEqual(token1, token2);
        }

        [Fact]
        public void GenerateServiceToken_ReturnsValidServiceToken()
        {
            var tokenString = _service.GenerateServiceToken("gmhelper-api");

            Assert.False(string.IsNullOrWhiteSpace(tokenString));

            var handler = new JwtSecurityTokenHandler();
            var token = handler.ReadJwtToken(tokenString);

            Assert.Equal("TestIssuer", token.Issuer);
            Assert.Contains("TestAudience", token.Audiences);
            Assert.Equal("gmhelper-api", token.Claims.First(c => c.Type == System.Security.Claims.ClaimTypes.Name).Value);
            Assert.Equal("Service", token.Claims.First(c => c.Type == System.Security.Claims.ClaimTypes.Role).Value);
            Assert.Equal("gmhelper-api", token.Claims.First(c => c.Type == "sub").Value);
            Assert.Equal("Service", token.Claims.First(c => c.Type == "role").Value);
        }
    }
}


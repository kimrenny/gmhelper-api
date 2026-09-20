using MatHelper.BLL.Interfaces;
using MatHelper.CORE.Models;
using MatHelper.CORE.Options;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;

namespace MatHelper.BLL.Services
{
    public class TokenGeneratorService : ITokenGeneratorService
    {
        private readonly JwtOptions _jwtOptions;
        private readonly ILogger _logger;

        private const ushort AccessTokenLifetimeMinutes = 30;
        private const ushort RefreshTokenSizeInBytes = 64;

        public TokenGeneratorService(JwtOptions jwtOptions, ILogger<TokenService> logger)
        {
            _jwtOptions = jwtOptions;
            _logger = logger;
        }

        public string GenerateJwtToken(User user, DeviceInfo deviceInfo)
        {
            var key = new SymmetricSecurityKey(Convert.FromBase64String(_jwtOptions.SecretKey));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            if (deviceInfo.UserAgent == null || deviceInfo.Platform == null)
            {
                _logger.LogError("deviceInfo does not meet the requirements");
                throw new InvalidDataException("deviceInfo does not meet the requirements");
            }

            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.Name, user.Id.ToString()),
                new Claim(ClaimTypes.Role, user!.Role),
                new Claim("Device", deviceInfo.UserAgent),
                new Claim("Platform", deviceInfo.Platform)
            };

            var token = new JwtSecurityToken(
                issuer: _jwtOptions.Issuer,
                audience: _jwtOptions.Audience,
                claims: claims,
                expires: DateTime.UtcNow.AddMinutes(AccessTokenLifetimeMinutes),
                signingCredentials: creds);

            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        public string GenerateRefreshToken()
        {
            return Convert.ToBase64String(RandomNumberGenerator.GetBytes(RefreshTokenSizeInBytes));
        }

        public string GenerateServiceToken(string serviceName = "gmhelper-api")
        {
            var key = new SymmetricSecurityKey(Convert.FromBase64String(_jwtOptions.SecretKey));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.Name, serviceName),
                new Claim(ClaimTypes.Role, "Service"),
                new Claim("sub", serviceName),
                new Claim("role", "Service")
            };

            var token = new JwtSecurityToken(
                issuer: _jwtOptions.Issuer,
                audience: _jwtOptions.Audience,
                claims: claims,
                expires: DateTime.UtcNow.AddMinutes(AccessTokenLifetimeMinutes),
                signingCredentials: creds);

            return new JwtSecurityTokenHandler().WriteToken(token);
        }
    }
}
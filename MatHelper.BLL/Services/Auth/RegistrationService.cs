using MatHelper.BLL.Interfaces;
using MatHelper.CORE.Models;
using MatHelper.CORE.Enums;
using MatHelper.DAL.Models;
using MatHelper.DAL.Interfaces;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;

namespace MatHelper.BLL.Services
{
    public class RegistrationService : IRegistrationService
    {
        private readonly ITokenGeneratorService _tokenGeneratorService;
        private readonly IUserRepository _userRepository;

        public RegistrationService(
            ITokenGeneratorService tokenGeneratorService,
            IUserRepository userRepository)
        {
            _tokenGeneratorService = tokenGeneratorService;
            _userRepository = userRepository;
        }

        public async Task EnsureEmailAndUsernameUniqueAsync(string email, string username)
        {
            var existingUserByEmail = await _userRepository.GetUserByEmailAsync(email);
            var existingUserByUsername = await _userRepository.GetUserByUsernameAsync(username);

            if (existingUserByEmail != null || existingUserByUsername != null)
            {
                throw new InvalidOperationException("The account with the provided email or username already exists.");
            }
        }

        public Task<User> BuildNewUserAsync(UserDto dto, string passwordHash)
        {
            var user = new User
            {
                Id = Guid.NewGuid(),
                Username = dto.UserName,
                Email = dto.Email,
                RegistrationDate = DateTime.UtcNow,
                PasswordHash = passwordHash,
                Avatar = null,
                Role = "User",
                IsActive = true,
                LastActivityAt = DateTime.UtcNow,
            };

            return Task.FromResult(user);
        }

        public async Task CreateInactiveInitialSessionAsync(User user, DeviceInfo device, string ip)
        {
            var accessToken = _tokenGeneratorService.GenerateJwtToken(user, device);
            var refreshToken = _tokenGeneratorService.GenerateRefreshToken();

            var loginToken = new LoginToken
            {
                Token = accessToken,
                RefreshToken = refreshToken,
                Expiration = DateTime.UtcNow,
                RefreshTokenExpiration = DateTime.UtcNow,
                UserId = user.Id,
                DeviceInfo = device,
                IpAddress = ip,
                IsActive = false
            };

            user.LoginTokens = new List<LoginToken> { loginToken };

            await Task.CompletedTask;
        }
    }
}
using MatHelper.CORE.Models;
using MatHelper.CORE.Options;
using MatHelper.DAL.Interfaces;
using Microsoft.Extensions.Logging;
using MatHelper.CORE.Enums;
using MatHelper.BLL.Interfaces;

namespace MatHelper.BLL.Services
{
    public class UserManagementService : IUserManagementService
    {
        private readonly IUserRepository _userRepository;
        private readonly ITwoFactorService _twoFactorService;
        private readonly ISecurityService _securityService;
        private readonly ILogger _logger;

        private const ushort MinPasswordLength = 8;

        public UserManagementService(IUserRepository userRepository, ITwoFactorService twoFactorService, ISecurityService securityService, ILogger<UserManagementService> logger)
        {
            _userRepository = userRepository;
            _twoFactorService = twoFactorService;
            _securityService = securityService;
            _logger = logger;
        }

        public async Task<UserDetails> GetUserDetailsAsync(Guid userId)
        {
            var user = await _userRepository.GetUserByIdAsync(userId);
            if (user == null) throw new InvalidDataException("User not found.");
            var twoFactor = await _twoFactorService.GetTwoFactorAsync(user.Id, "totp");

            return new UserDetails
            {
                Avatar = user.Avatar != null ? user.Avatar : null,
                Nickname = user.Username,
                Language = user.Language.ToString(),
                TwoFactor = twoFactor != null ? twoFactor.IsEnabled : false,
                AlwaysAsk = twoFactor != null ? twoFactor.AlwaysAsk : false
            };
        }

        public async Task<byte[]> GetUserAvatarAsync(Guid userId)
        {
            var user = await _userRepository.GetUserByIdAsync(userId);
            if (user == null)
                throw new Exception("User not found.");
            if (user.Avatar == null)
                throw new Exception("Avatar not found.");

            return user!.Avatar;
        }

        public async Task<String> GetUserLanguageByEmail(string email)
        {
            try
            {
                var language = await _userRepository.GetUserLanguageByEmail(email);

                return language?.ToLowerInvariant() ?? LanguageType.EN.ToString().ToLowerInvariant();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to get user language for email {email}. Defaulting to English.");
                return LanguageType.EN.ToString().ToLowerInvariant();
            }
        }

        public async Task SaveUserAvatarAsync(Guid userId, byte[] avatarBytes)
        {
            var user = await _userRepository.GetUserByIdAsync(userId);
            if (user == null) throw new Exception("User not found.");

            user.Avatar = avatarBytes;
            await _userRepository.SaveChangesAsync();
        }

        public async Task UpdateUserAsync(Guid userId, UpdateUserRequest request)
        {
            //_logger.LogInformation("Starting user update process for UserId: {UserId}", userId);

            var user = await _userRepository.GetUserByIdAsync(userId);
            if(user == null)
            {
                _logger.LogWarning("User with ID {UserId} not found.", userId);
                throw new InvalidOperationException("User not found");
            }

            //_logger.LogInformation("User {UserId} found. Validating current password.", userId);

            if (string.IsNullOrEmpty(request.CurrentPassword) || !_securityService.VerifyPassword(request.CurrentPassword, user.PasswordHash))
            {
                _logger.LogWarning("Invalid current password for user {UserId}.", userId);
                throw new InvalidOperationException("Invalid current password.");
            }

            //_logger.LogInformation("Password validated successfully for UserId: {UserId}", userId);


            if (!string.IsNullOrEmpty(request.Email) && request.Email != user.Email)
            {
                //_logger.LogInformation("Checking if email {Email} is already in use.", request.Email);
                var existingUser = await _userRepository.GetUserByEmailAsync(request.Email);
                if (existingUser != null)
                {
                    _logger.LogWarning("Email {Email} is already used by another user.", request.Email);
                    throw new InvalidOperationException("Email is already used by another user.");
                }
                //_logger.LogInformation("Email {Email} is available. Updating user.", request.Email);
                user.Email = request.Email;
            }

            if (!string.IsNullOrEmpty(request.Nickname) && request.Nickname != user.Username)
            {
                //_logger.LogInformation("Checking if username {Username} is already in use.", request.Nickname);
                var existingUser = await _userRepository.GetUserByUsernameAsync(request.Nickname);
                if(existingUser != null)
                {
                    _logger.LogWarning("Username {Username} is already used by another user.", request.Nickname);
                    throw new InvalidOperationException("Username is already used by another user.");
                }

                //_logger.LogInformation("Username {Username} is available. Updating user.", request.Nickname);
                user.Username = request.Nickname;
            }

            if (!string.IsNullOrEmpty(request.NewPassword))
            {
                _logger.LogInformation("Validating new password for UserId: {UserId}", userId);

                if (request.NewPassword.Length < MinPasswordLength)
                {
                    _logger.LogWarning("New password is too short for user {UserId}.", userId);
                    throw new InvalidOperationException("New password is too short.");
                }

                //_logger.LogInformation("Generating new salt and hashing password for UserId: {UserId}", userId);
                user.PasswordHash = _securityService.HashPassword(request.NewPassword);
            }

            //_logger.LogInformation("Saving changes for UserId: {UserId}", userId);
            await _userRepository.UpdateUserAsync(user);
            //_logger.LogInformation("User {UserId} updated successfully.", userId);
        }

        public async Task UpdateUserLanguageAsync(Guid userId, LanguageType language)
        {
            var user = await _userRepository.GetUserByIdAsync(userId);
            if (user == null)
            {
                _logger.LogWarning("User with ID {UserId} not found.", userId);
                throw new InvalidOperationException("User not found");
            }

            user.Language = language;
            await _userRepository.UpdateUserAsync(user);
        }

        public async Task<InternalUserDto?> GetInternalUserByIdAsync(Guid userId)
        {
            if (userId == Guid.Empty)
            {
                return null;
            }

            var user = await _userRepository.GetUserAsync(u => u.Id == userId);
            if (user == null)
            {
                _logger.LogInformation("Internal user lookup: User with ID {UserId} not found.", userId);
                return null;
            }

            return new InternalUserDto
            {
                Id = user.Id,
                Username = user.Username,
                Email = user.Email,
                Role = user.Role,
                Language = user.Language.ToString(),
                IsActive = user.IsActive,
                IsBlocked = user.IsBlocked,
                RegistrationDate = user.RegistrationDate
            };
        }
    }
}
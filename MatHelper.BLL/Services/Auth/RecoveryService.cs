using MatHelper.BLL.Interfaces;
using MatHelper.CORE.Models;
using MatHelper.CORE.Enums;
using MatHelper.DAL.Models;
using MatHelper.DAL.Interfaces;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;

namespace MatHelper.BLL.Services
{
    public class RecoveryService : IRecoveryService
    {
        private const ushort RecoveryTokenLifetimeMinutes = 15;

        private readonly IUserRepository _userRepository;
        private readonly IPasswordRecoveryRepository _passwordRecoveryRepository;
        private readonly IMailService _mailService;
        private readonly ITokenService _tokenService;
        private readonly ISecurityService _securityService;
        private readonly IAutomationEventPublisher _automationEventPublisher;
        private readonly ILogger<RecoveryService> _logger;

        public RecoveryService(
            IUserRepository userRepository,
            IPasswordRecoveryRepository passwordRecoveryRepository,
            IMailService mailService,
            ITokenService tokenService,
            ISecurityService securityService,
            IAutomationEventPublisher automationEventPublisher,
            ILogger<RecoveryService> logger)
        {
            _userRepository = userRepository;
            _passwordRecoveryRepository = passwordRecoveryRepository;
            _mailService = mailService;
            _tokenService = tokenService;   
            _securityService = securityService;
            _automationEventPublisher = automationEventPublisher;
            _logger = logger;
        }

        public async Task<PasswordRecoveryToken> CreateRecoveryTokenAsync(User user)
        {
            var recoveryToken = new PasswordRecoveryToken
            {
                Token = Guid.NewGuid().ToString(),
                UserId = user.Id,
                ExpirationDate = DateTime.UtcNow.AddMinutes(RecoveryTokenLifetimeMinutes),
                IsUsed = false,
                User = user
            };

            await _passwordRecoveryRepository.AddPasswordRecoveryTokenAsync(recoveryToken);
            await _passwordRecoveryRepository.SaveChangesAsync();

            return recoveryToken;
        }

        public async Task<bool> SendRecoveryEmailAsync(string email)
        {
            if (string.IsNullOrWhiteSpace(email))
                throw new ArgumentException("Email cannot be null or empty.");

            var user = await _userRepository.GetUserByEmailAsync(email);

            if (user == null)
                return false;

            var recoveryToken = await CreateRecoveryTokenAsync(user);

            await _mailService.SendPasswordRecoveryEmailAsync(
                user.Email,
                recoveryToken.Token
            );

            return true;
        }

        public async Task<RecoverPasswordResult> ResetPasswordAsync(string token, string newPassword)
        {
            var (result, user) = await _passwordRecoveryRepository.GetUserByRecoveryToken(token);

            if (user == null)
                return result;

            if (result != RecoverPasswordResult.Success)
                return result;

            var hashedPassword = _securityService.HashPassword(newPassword);

            await _userRepository.ChangePassword(user, hashedPassword);

            await _passwordRecoveryRepository.InvalidateAllUserRecoveryTokensAsync(user.Id);
            await _tokenService.DeactivateAllUserTokensAsync(user.Id);

            var eventId = Guid.NewGuid().ToString();
            try
            {
                var automationEvent = new AutomationEvent
                {
                    Id = eventId,
                    Type = "password.changed",
                    UserId = user.Id.ToString(),
                    OccurredAt = DateTime.UtcNow,
                    User = new InternalUserDto
                    {
                        Id = user.Id,
                        Username = user.Username,
                        Email = user.Email,
                        Role = user.Role,
                        Language = user.Language.ToString(),
                        IsActive = user.IsActive,
                        IsBlocked = user.IsBlocked,
                        RegistrationDate = user.RegistrationDate,
                        LastActivityAt = user.LastActivityAt
                    },
                    Data = new Dictionary<string, object>
                    {
                        { "source", "password_reset_flow" }
                    }
                };

                await _automationEventPublisher.PublishAsync(automationEvent);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to publish password.changed automation event {EventId} for user {UserId}", eventId, user.Id);
            }

            return RecoverPasswordResult.Success;
        }
    }
}
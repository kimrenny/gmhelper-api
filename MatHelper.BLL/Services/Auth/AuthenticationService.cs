using MatHelper.BLL.Interfaces;
using MatHelper.CORE.Models;
using MatHelper.CORE.Enums;
using MatHelper.DAL.Models;
using MatHelper.DAL.Interfaces;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;

namespace MatHelper.BLL.Services
{
    public class AuthenticationService: IAuthenticationService
    {
        private readonly IUserRepository _userRepository;
        private readonly IAppTwoFactorSessionRepository _twoFactorSessionRepository;
        private readonly ITwoFactorService _twoFactorService;
        private readonly IEmailLoginCodeRepository _emailLoginCodeRepository;
        private readonly IAuthLogRepository _authLogRepository;
        private readonly IMailService _mailService;
        private readonly ISecurityService _securityService;
        private readonly ILoginAttemptService _loginAttemptService;
        private readonly IRegistrationService _registrationService;
        private readonly ISecurityPolicyService _securityPolicy;
        private readonly IEmailAuthService _emailAuthService;
        private readonly ILoginService _loginService;
        private readonly IRecoveryService _recoveryService;
        private readonly ITwoFactorAuthService _twoFactorAuthService;
        private readonly ITokenService _tokenService;
        private readonly ICacheService _cache;
        private readonly IAutomationEventPublisher _automationEventPublisher;
        private readonly ILogger<AuthenticationService> _logger;

        private const string RegisterAttemptsKey = "register:attempts";
        private const string UsersVersionKey = "admin:users:version";
        private const string AnalyticsVersionKey = "analytics:version";
        private const string TokensVersionKey = "tokens:admin:version";
        private const string TokensDashboardCacheKey = "tokens:dashboard";

        public AuthenticationService(IUserRepository userRepository, IAppTwoFactorSessionRepository appTwoFactorSessionRepository, ITwoFactorService twoFactorService, IEmailLoginCodeRepository emailLoginCodeRepository, IAuthLogRepository authLogRepository, IMailService mailService, ISecurityService securityService, ILoginAttemptService loginAttemptService, IRegistrationService registrationService, ISecurityPolicyService securityPolicy, IEmailAuthService emailAuthService, ILoginService loginService, IRecoveryService recoveryService, ITwoFactorAuthService twoFactorAuthService, ITokenService tokenService, ICacheService cache, IAutomationEventPublisher automationEventPublisher, ILogger<AuthenticationService> logger)
        {
            _userRepository = userRepository;
            _twoFactorSessionRepository = appTwoFactorSessionRepository;
            _twoFactorService = twoFactorService;
            _emailLoginCodeRepository = emailLoginCodeRepository;
            _authLogRepository = authLogRepository;
            _mailService = mailService;
            _securityService = securityService;
            _loginAttemptService = loginAttemptService;
            _registrationService = registrationService;
            _securityPolicy = securityPolicy;
            _emailAuthService = emailAuthService;
            _loginService = loginService;
            _recoveryService = recoveryService;
            _twoFactorAuthService = twoFactorAuthService;
            _tokenService = tokenService;
            _cache = cache;
            _automationEventPublisher = automationEventPublisher;
            _logger = logger;
        }

        public async Task<EmailLoginCode> InitRegisterUserAsync(RegisterRequestDto userDto, DeviceInfo deviceInfo, string ipAddress)
        {
            if (string.IsNullOrWhiteSpace(userDto.Email))
            {
                throw new ArgumentException("Email cannot be null or empty.");
            }
            if (string.IsNullOrWhiteSpace(userDto.UserName))
            {
                throw new ArgumentException("Username cannot be null or empty.");
            }
            if (string.IsNullOrWhiteSpace(ipAddress))
            {
                throw new ArgumentException("IP Address cannot be null or empty.");
            }

            try
            {
                var attempts = await _cache.GetAsync<int?>($"{RegisterAttemptsKey}:{userDto.Email}") ?? 0;

                if (attempts >= 3)
                {
                    throw new InvalidOperationException("Too many invalid confirmation code attempts. Try again in 1 hour.");
                }

                _mailService.ValidateEmailFormatAsync(userDto.Email);

                await _securityPolicy.EnforceRegistrationIpLimitAsync(ipAddress);

                await _registrationService.EnsureEmailAndUsernameUniqueAsync(userDto.Email, userDto.UserName);

                var emailCode = await _emailAuthService.CreateEmailRegisterCodeAsync(userDto, deviceInfo, ipAddress);

                return emailCode;
            }
            catch (InvalidOperationException)
            {
                throw;
            }
            catch(ArgumentException)
            {
                throw;
            }
            catch(Exception ex)
            {
                _logger.LogError(ex, "Unknown error occurred during registration initialization.");
                throw new Exception("Unknown error occurred during request", ex);
            }
        }

        public async Task<ConfirmTokenResult> RegisterUserAsync(UserDto userDto, DeviceInfo deviceInfo, string ipAddress)
        {
            var attempts = await _cache.GetAsync<int?>($"{RegisterAttemptsKey}:{userDto.Email}") ?? 0;
            _logger.LogInformation("Current registration confirmation attempts for {Email}: {Attempts}", userDto.Email, attempts);

            if (attempts >= 3)
            {
                throw new InvalidOperationException("Too many invalid confirmation code attempts. Try again in 1 hour.");
            }

            var confirmResult = await _emailAuthService.ConfirmEmailAsync(userDto.Email, userDto.Token);
            if (confirmResult != ConfirmTokenResult.Success)
            {
                attempts++;

                await _cache.SetAsync(
                   $"{RegisterAttemptsKey}:{userDto.Email}",
                   attempts,
                   TimeSpan.FromHours(1));

                if (attempts >= 3)
                {
                    throw new InvalidOperationException(
                        "Too many invalid confirmation code attempts. Try again in 1 hour.");
                }

                return confirmResult;
            }

            await _cache.RemoveAsync($"{RegisterAttemptsKey}:{userDto.Email}");

            if (string.IsNullOrWhiteSpace(userDto.Email))
            {
                throw new ArgumentException("Email cannot be null or empty.");
            }
            if (string.IsNullOrWhiteSpace(userDto.UserName))
            {
                throw new ArgumentException("Username cannot be null or empty.");
            }
            if (string.IsNullOrWhiteSpace(ipAddress))
            {
                throw new ArgumentException("IP Address cannot be null or empty.");
            }

            await _securityPolicy.EnforceRegistrationIpLimitAsync(ipAddress);

            await _registrationService.EnsureEmailAndUsernameUniqueAsync(userDto.Email, userDto.UserName);

            var passwordHash = _securityService.HashPassword(userDto.Password);

            var user = await _registrationService.BuildNewUserAsync(userDto, passwordHash);

            await _registrationService.CreateInactiveInitialSessionAsync(user, deviceInfo, ipAddress);

            await _userRepository.AddUserAsync(user);
            await _userRepository.SaveChangesAsync();

            await _cache.IncrementVersionAsync(UsersVersionKey);
            await _cache.IncrementVersionAsync(AnalyticsVersionKey);

            await _mailService.SendConfirmationEmailAsync(user.Email);

            var eventId = Guid.NewGuid().ToString();
            try
            {
                var automationEvent = new AutomationEvent
                {
                    Id = eventId,
                    Type = "user.registered",
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
                        { "source", "registration_flow" }
                    }
                };

                await _automationEventPublisher.PublishAsync(automationEvent);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to publish user.registered automation event {EventId} for user {UserId}", eventId, user.Id);
            }

            return ConfirmTokenResult.Success;
        }

        public async Task<LoginResponse> LoginUserAsync(LoginDto loginDto, DeviceInfo deviceInfo, string ipAddress)
        {
            try
            {
                await _loginAttemptService.CheckIpBlockedAsync(ipAddress);

                var user = await _loginService.ValidateUserCredentialsAsync(loginDto.Email, loginDto.Password);

                await _loginService.EnsureUserCanLoginAsync(user);

                await _loginAttemptService.ResetAttemptsAsync(ipAddress);

                _securityPolicy.ValidateDeviceInfo(deviceInfo);

                await _loginService.CleanupUserSessionsAsync(user, deviceInfo, ipAddress);

                await _loginService.ApplySessionLimitsAsync(user);

                var twoFactor = await _twoFactorService.GetTwoFactorAsync(user.Id, "totp");

                if (twoFactor != null && twoFactor.IsEnabled && twoFactor.AlwaysAsk)
                {
                    var session = await _twoFactorAuthService.CreateTwoFactorSessionAsync(user, deviceInfo, ipAddress, loginDto.Remember);

                    return new LoginResponse
                    {
                        Message = "Enter the 2FA code from your app",
                        SessionKey = session.SessionKey
                    };
                }

                var lastToken = user.LoginTokens?
                    .Where(t => t.IpAddress == ipAddress)
                    .OrderByDescending(t => t.Expiration)
                    .FirstOrDefault();

                var unfamiliar = _securityPolicy.IsUnfamiliar(lastToken, ipAddress, deviceInfo.UserAgent!); 

                if (unfamiliar)
                {
                    var emailCode = await _emailAuthService.CreateEmailLoginCodeAsync(user, deviceInfo, ipAddress, loginDto.Remember);

                    return new LoginResponse
                    {
                        Message = "Check your email for the code",
                        SessionKey = emailCode.SessionKey
                    };
                }

                var token = await _loginService.IssueLoginTokenAsync(user, deviceInfo, ipAddress, loginDto.Remember);

                user.LoginTokens!.Add(token);
                user.LastActivityAt = DateTime.UtcNow;
                await _userRepository.SaveChangesAsync();

                await _cache.IncrementVersionAsync(TokensVersionKey);
                await _cache.IncrementVersionAsync(TokensDashboardCacheKey);

                if (await _securityService.CheckSuspiciousActivityAsync(ipAddress, deviceInfo.UserAgent!, deviceInfo.Platform!))
                    throw new UnauthorizedAccessException("Suspicious activity detected. Accounts blocked.");

                return new LoginResponse
                {
                    AccessToken = token.Token,
                    RefreshToken = token.RefreshToken,
                    RefreshTokenExpiration = token.RefreshTokenExpiration
                };
            }
            catch (Exception ex) when (
                ex is UnauthorizedAccessException ||
                ex is InvalidOperationException ||
                ex is InvalidDataException)
            {
                await _loginAttemptService.RegisterFailedAttemptAsync(ipAddress);
                await _authLogRepository.LogAuthAsync("Unknown", ipAddress, deviceInfo.UserAgent ?? "Unknown", "Failed", ex.Message);
                throw;
            }
            catch (Exception ex)
            {
                await _authLogRepository.LogAuthAsync("Unknown", ipAddress, deviceInfo.UserAgent ?? "Unknown", "Failed", "Unknown error occurred.");
                throw new Exception("Unknown error occurred during request", ex);
            }
        }

        public async Task<LoginResponse> ConfirmEmailCodeAsync(string code, string sessionKey)
        {
            var emailCode = await _emailLoginCodeRepository.GetBySessionKeyAsync(sessionKey);
            if (emailCode == null)
                throw new UnauthorizedAccessException("Invalid or expired session key.");

            if (emailCode.Code != code)
                throw new UnauthorizedAccessException("Invalid confirmation code.");

            if (emailCode.UserId == null)
                throw new UnauthorizedAccessException("This code is not linked to a user.");

            var user = await _userRepository.GetUserByIdAsync(emailCode.UserId.Value);
            if (user == null)
                throw new UnauthorizedAccessException("User not found.");

            if (user.IsBlocked)
                throw new UnauthorizedAccessException("User is banned.");

            emailCode.IsUsed = true;
            await _emailLoginCodeRepository.SaveChangesAsync();

            var deviceInfo = new DeviceInfo
            {
                UserAgent = emailCode.UserAgent,
                Platform = emailCode.Platform
            };

            var loginToken = await _tokenService.IssueLoginTokenAsync(
                user,
                deviceInfo,
                emailCode.IpAddress,
                emailCode.Remember
            );

            user.LoginTokens!.Add(loginToken);
            user.LastActivityAt = DateTime.UtcNow;
            await _userRepository.SaveChangesAsync();

            await _cache.IncrementVersionAsync(TokensVersionKey);
            await _cache.IncrementVersionAsync(TokensDashboardCacheKey);

            await _authLogRepository.LogAuthAsync(
                user.Id.ToString(),
                emailCode.IpAddress,
                deviceInfo.UserAgent,
                "Success via email confirmation"
            );

            var eventId = Guid.NewGuid().ToString();
            try
            {
                var automationEvent = new AutomationEvent
                {
                    Id = eventId,
                    Type = "email.confirmed",
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
                        { "source", "email_confirmation_flow" }
                    }
                };

                await _automationEventPublisher.PublishAsync(automationEvent);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to publish email.confirmed automation event {EventId} for user {UserId}", eventId, user.Id);
            }

            return new LoginResponse
            {
                AccessToken = loginToken.Token,
                RefreshToken = loginToken.RefreshToken
            };
        }

        public async Task<LoginResponse> ConfirmTwoFactorCodeAsync(string code, string sessionKey)
        {
            var result = await _twoFactorAuthService.ValidateTwoFactorSessionAsync(sessionKey, code);

            if (!result.Success)
                throw new UnauthorizedAccessException(result.Error ?? "2FA validation failed.");

            if (!result.UserId.HasValue)
                throw new UnauthorizedAccessException("UserId is missing.");

            var user = await _userRepository.GetUserByIdAsync(result.UserId.Value);

            if (user == null)
                throw new UnauthorizedAccessException("User not found.");

            var session = await _twoFactorSessionRepository.GetBySessionKeyAsync(sessionKey);

            if (session == null)
                throw new UnauthorizedAccessException("Session not found.");

            var deviceInfo = new DeviceInfo
            {
                UserAgent = session.UserAgent,
                Platform = session.Platform
            };

            var token = await _loginService.IssueLoginTokenAsync(
                user,
                deviceInfo,
                session.IpAddress,
                session.Remember
            );

            user.LoginTokens ??= new List<LoginToken>();
            user.LoginTokens.Add(token);
            user.LastActivityAt = DateTime.UtcNow;
            
            session.IsUsed = true;

            await _userRepository.SaveChangesAsync();
            await _twoFactorSessionRepository.SaveChangesAsync();

            await _cache.IncrementVersionAsync(TokensVersionKey);
            await _cache.IncrementVersionAsync(TokensDashboardCacheKey);

            await _authLogRepository.LogAuthAsync(
                user.Id.ToString(),
                session.IpAddress,
                deviceInfo.UserAgent,
                "Success via 2FA"
            );

            return new LoginResponse
            {
                AccessToken = token.Token,
                RefreshToken = token.RefreshToken
            };
        }

        public async Task<bool> SendRecoverPasswordLinkAsync(string email)
        {
            try
            {
                return await _recoveryService.SendRecoveryEmailAsync(email);
            }
            catch(Exception ex)
            {
                _logger.LogError(ex, "Error while sending password recovery link.");
                throw;
            }
        }

        public async Task<RecoverPasswordResult> RecoverPassword(string token, string password)
        {
            try
            {
                var result = await _recoveryService.ResetPasswordAsync(token, password);

                if (result == RecoverPasswordResult.Success)
                {
                    await _cache.IncrementVersionAsync(TokensVersionKey);
                    await _cache.IncrementVersionAsync(TokensDashboardCacheKey);
                }

                return result;
            }
            catch (Exception ex) 
            {
                _logger.LogError("Unknown error occurred during request: {ex}", ex);

                return RecoverPasswordResult.Failed;
            }
        }
    }
}
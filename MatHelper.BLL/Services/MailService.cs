using MatHelper.BLL.Interfaces;
using MatHelper.CORE.Models;
using MatHelper.DAL.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace MatHelper.BLL.Services
{
    public class MailService : IMailService
    {
        private readonly INotifyApiClient _notifyApiClient;
        private readonly IUserRepository _userRepository;
        private readonly IUserManagementService _userManagementService;
        private readonly IConfiguration _configuration;
        private readonly ILogger<MailService> _logger;

        public MailService(
            INotifyApiClient notifyApiClient,
            IUserRepository userRepository,
            IUserManagementService userManagementService,
            IConfiguration configuration,
            ILogger<MailService> logger)
        {
            _notifyApiClient = notifyApiClient ?? throw new ArgumentNullException(nameof(notifyApiClient));
            _userRepository = userRepository ?? throw new ArgumentNullException(nameof(userRepository));
            _userManagementService = userManagementService ?? throw new ArgumentNullException(nameof(userManagementService));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task SendRegistrationCodeEmailAsync(string toEmail, string code)
        {
            var clientBaseUrl = _configuration["ClientApp:BaseUrl"];
            if (string.IsNullOrWhiteSpace(clientBaseUrl))
            {
                throw new InvalidOperationException("Client application base URL is not configured.");
            }

            var mainLink = $"{clientBaseUrl.TrimEnd('/')}/";

            _logger.LogInformation("Dispatching registration code email request for {RecipientEmail} to notify-api", toEmail);

            var request = new SendNotificationRequest
            {
                TemplateKey = "auth.register_code",
                Locale = "en",
                RecipientEmail = toEmail,
                Variables = new Dictionary<string, object>
                {
                    { "mainLink", mainLink },
                    { "code", code }
                }
            };

            await _notifyApiClient.SendNotificationAsync(request);
        }

        public async Task SendConfirmationEmailAsync(string toEmail)
        {
            var clientBaseUrl = _configuration["ClientApp:BaseUrl"];
            if (string.IsNullOrWhiteSpace(clientBaseUrl))
            {
                throw new InvalidOperationException("Client application base URL is not configured.");
            }

            var mainLink = $"{clientBaseUrl.TrimEnd('/')}/";

            _logger.LogInformation("Dispatching registration welcome email request for {RecipientEmail} to notify-api", toEmail);

            var user = await _userRepository.GetUserByEmailAsync(toEmail);
            var language = await _userManagementService.GetUserLanguageByEmail(toEmail);
            var locale = NormalizeLocale(language);

            var request = new SendNotificationRequest
            {
                TemplateKey = "auth.welcome",
                Locale = locale,
                ExternalUserId = user?.Id.ToString(),
                RecipientEmail = toEmail,
                RecipientName = user?.Username,
                Variables = new Dictionary<string, object>
                {
                    { "mainLink", mainLink }
                }
            };

            await _notifyApiClient.SendNotificationAsync(request);
        }

        public async Task SendPasswordRecoveryEmailAsync(string toEmail, string token)
        {
            var clientBaseUrl = _configuration["ClientApp:BaseUrl"];
            if (string.IsNullOrWhiteSpace(clientBaseUrl))
            {
                throw new InvalidOperationException("Client application base URL is not configured.");
            }

            var recoveryLink = $"{clientBaseUrl.TrimEnd('/')}/recover?token={token}";
            var mainLink = $"{clientBaseUrl.TrimEnd('/')}/";

            var language = await _userManagementService.GetUserLanguageByEmail(toEmail);
            var locale = NormalizeLocale(language);
            var user = await _userRepository.GetUserByEmailAsync(toEmail);

            _logger.LogInformation("Dispatching password recovery email request for {RecipientEmail} to notify-api", toEmail);

            var request = new SendNotificationRequest
            {
                TemplateKey = "auth.password_recovery",
                Locale = locale,
                ExternalUserId = user?.Id.ToString(),
                RecipientEmail = toEmail,
                RecipientName = user?.Username,
                Variables = new Dictionary<string, object>
                {
                    { "mainLink", mainLink },
                    { "recoveryLink", recoveryLink }
                }
            };

            await _notifyApiClient.SendNotificationAsync(request);
        }

        public async Task SendIpConfirmationCodeEmailAsync(string toEmail, string code)
        {
            var clientBaseUrl = _configuration["ClientApp:BaseUrl"];
            if (string.IsNullOrWhiteSpace(clientBaseUrl))
            {
                throw new InvalidOperationException("Client application base URL is not configured.");
            }

            var mainLink = $"{clientBaseUrl.TrimEnd('/')}/";

            var language = await _userManagementService.GetUserLanguageByEmail(toEmail);
            var locale = NormalizeLocale(language);
            var user = await _userRepository.GetUserByEmailAsync(toEmail);

            _logger.LogInformation("Dispatching IP confirmation code email request for {RecipientEmail} to notify-api", toEmail);

            var request = new SendNotificationRequest
            {
                TemplateKey = "auth.ip_confirmation",
                Locale = locale,
                ExternalUserId = user?.Id.ToString(),
                RecipientEmail = toEmail,
                RecipientName = user?.Username,
                Variables = new Dictionary<string, object>
                {
                    { "mainLink", mainLink },
                    { "code", code }
                }
            };

            await _notifyApiClient.SendNotificationAsync(request);
        }

        public bool ValidateEmailFormatAsync(string email)
        {
            if (string.IsNullOrWhiteSpace(email))
                throw new ArgumentException("Email cannot be empty.");

            email = email.Trim();

            var parts = email.Split('@');

            if (parts.Length != 2)
                throw new ArgumentException("Invalid email format.");

            var local = parts[0];
            var domain = parts[1];

            if (string.IsNullOrWhiteSpace(local))
                throw new ArgumentException("Invalid email format.");

            if (string.IsNullOrWhiteSpace(domain))
                throw new ArgumentException("Invalid email format.");

            var domainParts = domain.Split('.');

            if (domainParts.Length < 2)
                throw new ArgumentException("Invalid email domain.");

            return true;
        }

        private static string NormalizeLocale(string? language)
        {
            if (string.IsNullOrWhiteSpace(language))
                return "en";

            var normalized = language.Trim().ToLowerInvariant();
            if (normalized == "ua")
                return "uk";

            return normalized;
        }
    }
}
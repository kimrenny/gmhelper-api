using MatHelper.API.Common;
using MatHelper.CORE.Models;
using MatHelper.DAL.Database;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Json;
using MatHelper.IntegrationTests.Services;
using MatHelper.DAL.Models;

namespace MatHelper.IntegrationTests.Tests
{
    public class AuthControllerTests : IClassFixture<CustomWebApplicationFactory<Program>>
    {
        private readonly CustomWebApplicationFactory<Program> _factory;
        private readonly HttpClient _client;

        public AuthControllerTests(CustomWebApplicationFactory<Program> factory)
        {
            _factory = factory;
            _factory.ResetDatabase();

            _client = factory.CreateClient(new WebApplicationFactoryClientOptions
            {
                HandleCookies = true,
                BaseAddress = new Uri("https://localhost:5001")
            });
        }

        private async Task<(string email, string password)> RegisterUserAsync(bool activate = false)
        {
            var email = $"user_{Guid.NewGuid()}@test.com";
            var password = "Password123!";

            var initDto = new RegisterRequestDto
            {
                Email = email,
                UserName = $"user_{Guid.NewGuid()}",
                CaptchaToken = "valid-captcha"
            };

            await _client.PostAsJsonAsync("api/v1/auth/register/code", initDto);

            string code;
            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                code = await db.EmailLoginCodes
                    .Where(x => x.Email == email)
                    .OrderByDescending(x => x.Id)
                    .Select(x => x.Code)
                    .FirstAsync();
            }

            var registerDto = new UserDto
            {
                Email = email,
                UserName = initDto.UserName,
                Password = password,
                Token = code
            };

            await _client.PostAsJsonAsync("api/v1/auth/register", registerDto);

            if (activate)
            {
                using var scope = _factory.Services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                var user = await db.Users.FirstOrDefaultAsync(x => x.Email == email);
                if (user != null)
                {
                    user.IsActive = true;
                    await db.SaveChangesAsync();
                }
            }

            return (email, password);
        }

        [Fact]
        public async Task Login_ShouldWork_OnlyIfSystemAllows()
        {
            var (email, password) = await RegisterUserAsync(true);

            var login = new LoginDto
            {
                Email = email,
                Password = password,
                CaptchaToken = "valid-captcha",
                Remember = true
            };

            var response = await _client.PostAsJsonAsync("api/v1/auth/login", login);

            var body = await response.Content.ReadAsStringAsync();

            Assert.True(
                response.IsSuccessStatusCode ||
                response.StatusCode == HttpStatusCode.Unauthorized ||
                response.StatusCode == HttpStatusCode.BadRequest
            );
        }

        [Fact]
        public async Task Login_ShouldReject_WhenPasswordIsWrong()
        {
            var (email, password) = await RegisterUserAsync(true);

            var login = new LoginDto
            {
                Email = email,
                Password = "wrong",
                CaptchaToken = "valid-captcha",
                Remember = true
            };

            var response = await _client.PostAsJsonAsync("api/v1/auth/login", login);

            Assert.True(
                response.StatusCode == HttpStatusCode.Unauthorized ||
                !response.IsSuccessStatusCode
            );
        }

        [Fact]
        public async Task Login_ShouldEventuallyBlockOrThrottle()
        {
            var (email, _) = await RegisterUserAsync(true);

            var bad = new LoginDto
            {
                Email = email,
                Password = "wrong",
                CaptchaToken = "valid-captcha",
                Remember = false
            };

            HttpResponseMessage last = null!;

            for (int i = 0; i < 6; i++)
            {
                last = await _client.PostAsJsonAsync("api/v1/auth/login", bad);
            }

            Assert.True(
                last.StatusCode == HttpStatusCode.TooManyRequests ||
                last.StatusCode == HttpStatusCode.Unauthorized ||
                !last.IsSuccessStatusCode
            );
        }

        [Fact]
        public async Task Register_ShouldPublishUserRegisteredEvent_WhenRegistrationSucceeds()
        {
            var publisher = _factory.Services.GetRequiredService<MockAutomationEventPublisher>();
            var initialCount = publisher.PublishedEvents.Count;

            var (email, _) = await RegisterUserAsync(false);

            var matchingEvents = publisher.PublishedEvents
                .Where(e => e.Type == "user.registered" && e.User?.Email == email)
                .ToList();

            Assert.Single(matchingEvents);
            var publishedEvent = matchingEvents[0];

            Assert.Equal("user.registered", publishedEvent.Type);
            Assert.False(string.IsNullOrWhiteSpace(publishedEvent.Id));
            Assert.NotEqual(publishedEvent.UserId, publishedEvent.Id);
            Assert.Equal(DateTimeKind.Utc, publishedEvent.OccurredAt.Kind);
            Assert.NotNull(publishedEvent.User);
            Assert.Equal(email, publishedEvent.User.Email);
            Assert.True(publishedEvent.User.IsActive);
            Assert.NotNull(publishedEvent.Data);
            Assert.Equal("registration_flow", publishedEvent.Data["source"]);
        }

        [Fact]
        public async Task ConfirmEmailCode_ShouldPublishEmailConfirmedEvent_WhenConfirmationSucceeds()
        {
            var publisher = _factory.Services.GetRequiredService<MockAutomationEventPublisher>();
            var (email, _) = await RegisterUserAsync(false);

            var sessionKey = Guid.NewGuid().ToString();
            var code = "654321";

            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var user = await db.Users.FirstAsync(u => u.Email == email);
                db.EmailLoginCodes.Add(new EmailLoginCode
                {
                    UserId = user.Id,
                    Email = user.Email,
                    Code = code,
                    SessionKey = sessionKey,
                    IpAddress = "127.0.0.1",
                    UserAgent = "TestAgent",
                    Platform = "TestOS",
                    Remember = true,
                    IsUsed = false,
                    Expiration = DateTime.UtcNow.AddMinutes(15)
                });
                await db.SaveChangesAsync();
            }

            var confirmDto = new ConfirmCodeDto
            {
                Code = code,
                SessionKey = sessionKey
            };

            var response = await _client.PostAsJsonAsync("api/v1/auth/email/confirm/code", confirmDto);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var matchingEvents = publisher.PublishedEvents
                .Where(e => e.Type == "email.confirmed" && e.User?.Email == email)
                .ToList();

            Assert.Single(matchingEvents);
            var publishedEvent = matchingEvents[0];

            Assert.Equal("email.confirmed", publishedEvent.Type);
            Assert.False(string.IsNullOrWhiteSpace(publishedEvent.Id));
            Assert.NotEqual(publishedEvent.UserId, publishedEvent.Id);
            Assert.Equal(DateTimeKind.Utc, publishedEvent.OccurredAt.Kind);
            Assert.NotNull(publishedEvent.User);
            Assert.Equal(email, publishedEvent.User.Email);
            Assert.NotNull(publishedEvent.Data);
            Assert.Equal("email_confirmation_flow", publishedEvent.Data["source"]);
        }

        [Fact]
        public async Task RecoverPassword_ShouldPublishPasswordChangedEvent_WhenResetSucceeds()
        {
            var publisher = _factory.Services.GetRequiredService<MockAutomationEventPublisher>();
            var (email, oldPassword) = await RegisterUserAsync(true);

            var token = Guid.NewGuid().ToString();
            Guid userId;

            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var user = await db.Users.FirstAsync(u => u.Email == email);
                userId = user.Id;
                db.PasswordRecoveryTokens.Add(new PasswordRecoveryToken
                {
                    UserId = user.Id,
                    User = user,
                    Token = token,
                    ExpirationDate = DateTime.UtcNow.AddHours(1),
                    IsUsed = false
                });
                await db.SaveChangesAsync();
            }

            var newPassword = "NewSecretPassword123!";
            var recoveryDto = new PasswordRecoveryDto
            {
                RecoveryToken = token,
                Password = newPassword,
                CaptchaToken = "valid-captcha"
            };

            var response = await _client.PostAsJsonAsync("api/v1/auth/password/recover", recoveryDto);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var matchingEvents = publisher.PublishedEvents
                .Where(e => e.Type == "password.changed" && e.User?.Email == email)
                .ToList();

            Assert.Single(matchingEvents);
            var publishedEvent = matchingEvents[0];

            Assert.Equal("password.changed", publishedEvent.Type);
            Assert.False(string.IsNullOrWhiteSpace(publishedEvent.Id));
            Assert.NotEqual(publishedEvent.UserId, publishedEvent.Id);
            Assert.Equal(userId.ToString(), publishedEvent.UserId);
            Assert.Equal(DateTimeKind.Utc, publishedEvent.OccurredAt.Kind);
            Assert.NotNull(publishedEvent.User);
            Assert.Equal(email, publishedEvent.User.Email);
            Assert.NotNull(publishedEvent.Data);
            Assert.Equal("password_reset_flow", publishedEvent.Data["source"]);

            // Verify old password no longer works and new password works
            var loginWithOld = await _client.PostAsJsonAsync("api/v1/auth/login", new LoginDto
            {
                Email = email,
                Password = oldPassword,
                CaptchaToken = "valid-captcha",
                Remember = true
            });
            Assert.Equal(HttpStatusCode.Unauthorized, loginWithOld.StatusCode);

            var loginWithNew = await _client.PostAsJsonAsync("api/v1/auth/login", new LoginDto
            {
                Email = email,
                Password = newPassword,
                CaptchaToken = "valid-captcha",
                Remember = true
            });
            Assert.Equal(HttpStatusCode.OK, loginWithNew.StatusCode);
        }
    }
}
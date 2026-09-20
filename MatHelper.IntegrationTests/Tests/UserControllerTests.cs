using MatHelper.API.Common;
using MatHelper.CORE.Models;
using MatHelper.DAL.Database;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MatHelper.IntegrationTests.Services;
using MatHelper.DAL.Models;

namespace MatHelper.IntegrationTests.Tests
{
    public class UserControllerTests : IClassFixture<CustomWebApplicationFactory<Program>>
    {
        private readonly CustomWebApplicationFactory<Program> _factory;
        private readonly HttpClient _client;

        public UserControllerTests(CustomWebApplicationFactory<Program> factory)
        {
            _factory = factory;
            _factory.ResetDatabase();

            _client = factory.CreateClient(new WebApplicationFactoryClientOptions
            {
                HandleCookies = true,
                BaseAddress = new Uri("https://localhost:5001")
            });
        }

        private async Task<(string email, string password, string token, Guid userId)> RegisterAndLoginUserAsync()
        {
            var email = $"user_{Guid.NewGuid()}@test.com";
            var password = "Password123!";
            var username = $"user_{Guid.NewGuid():N}"[..15];

            var initDto = new RegisterRequestDto
            {
                Email = email,
                UserName = username,
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
                UserName = username,
                Password = password,
                Token = code
            };

            await _client.PostAsJsonAsync("api/v1/auth/register", registerDto);

            Guid userId;
            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var user = await db.Users.FirstAsync(x => x.Email == email);
                user.IsActive = true;
                await db.SaveChangesAsync();
                userId = user.Id;
            }

            var sessionKey = Guid.NewGuid().ToString();
            var loginCode = "654321";

            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                db.EmailLoginCodes.Add(new EmailLoginCode
                {
                    UserId = userId,
                    Email = email,
                    Code = loginCode,
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
                Code = loginCode,
                SessionKey = sessionKey
            };

            var confirmResponse = await _client.PostAsJsonAsync("api/v1/auth/email/confirm/code", confirmDto);
            var loginResult = await confirmResponse.Content.ReadFromJsonAsync<ApiResponse<LoginResponse>>();
            Assert.NotNull(loginResult?.Data?.AccessToken);
            var accessToken = loginResult.Data.AccessToken;

            return (email, password, accessToken, userId);
        }

        [Fact]
        public async Task UpdateUser_ShouldPublishPasswordChangedEvent_WhenPasswordIsChanged()
        {
            var publisher = _factory.Services.GetRequiredService<MockAutomationEventPublisher>();
            var (email, oldPassword, accessToken, userId) = await RegisterAndLoginUserAsync();

            var newPassword = "BrandNewPassword123!";

            using var request = new HttpRequestMessage(HttpMethod.Patch, "api/v1/user/profile");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            using var formData = new MultipartFormDataContent();
            formData.Add(new StringContent(oldPassword), "CurrentPassword");
            formData.Add(new StringContent(newPassword), "NewPassword");
            request.Content = formData;

            var response = await _client.SendAsync(request);

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
            Assert.Equal("password_change_flow", publishedEvent.Data["source"]);

            // Verify login with new password succeeds and old password fails
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

        [Fact]
        public async Task UpdateUser_ShouldNotPublishPasswordChangedEvent_WhenOnlyNicknameIsUpdated()
        {
            var publisher = _factory.Services.GetRequiredService<MockAutomationEventPublisher>();
            var (email, password, accessToken, _) = await RegisterAndLoginUserAsync();

            var eventCountBefore = publisher.PublishedEvents.Count(e => e.Type == "password.changed");

            using var request = new HttpRequestMessage(HttpMethod.Patch, "api/v1/user/profile");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            using var formData = new MultipartFormDataContent();
            formData.Add(new StringContent(password), "CurrentPassword");
            formData.Add(new StringContent("NewNickName"), "Nickname");
            request.Content = formData;

            var response = await _client.SendAsync(request);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var eventCountAfter = publisher.PublishedEvents.Count(e => e.Type == "password.changed");
            Assert.Equal(eventCountBefore, eventCountAfter);
        }

        [Fact]
        public async Task UpdateLanguage_ShouldPublishUserLanguageChangedEvent_WhenLanguageChanges()
        {
            var publisher = _factory.Services.GetRequiredService<MockAutomationEventPublisher>();
            var (email, _, accessToken, userId) = await RegisterAndLoginUserAsync();

            using var request = new HttpRequestMessage(HttpMethod.Patch, "api/v1/user/profile/language");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            request.Content = JsonContent.Create(new UpdateLanguageRequest { Language = "UA" });

            var response = await _client.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var matchingEvents = publisher.PublishedEvents
                .Where(e => e.Type == "user.language_changed" && e.UserId == userId.ToString())
                .ToList();

            Assert.Single(matchingEvents);
            var publishedEvent = matchingEvents[0];

            Assert.Equal("user.language_changed", publishedEvent.Type);
            Assert.False(string.IsNullOrWhiteSpace(publishedEvent.Id));
            Assert.NotEqual(publishedEvent.UserId, publishedEvent.Id);
            Assert.Equal(userId.ToString(), publishedEvent.UserId);
            Assert.Equal(DateTimeKind.Utc, publishedEvent.OccurredAt.Kind);
            Assert.NotNull(publishedEvent.User);
            Assert.Equal(email, publishedEvent.User.Email);
            Assert.Equal("UA", publishedEvent.User.Language);
            Assert.NotNull(publishedEvent.Data);
            Assert.Equal("language_change_flow", publishedEvent.Data["source"]);

            // Repeat with same language -> Expect NO duplicate event
            await Task.Delay(550);
            using var repeatRequest = new HttpRequestMessage(HttpMethod.Patch, "api/v1/user/profile/language");
            repeatRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            repeatRequest.Content = JsonContent.Create(new UpdateLanguageRequest { Language = "UA" });

            var repeatResponse = await _client.SendAsync(repeatRequest);
            Assert.Equal(HttpStatusCode.OK, repeatResponse.StatusCode);

            var matchingEventsAfterRepeat = publisher.PublishedEvents
                .Where(e => e.Type == "user.language_changed" && e.UserId == userId.ToString())
                .ToList();

            Assert.Single(matchingEventsAfterRepeat); // Still only 1 event
        }
    }
}

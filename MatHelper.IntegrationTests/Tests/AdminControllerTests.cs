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
    public class AdminControllerTests : IClassFixture<CustomWebApplicationFactory<Program>>
    {
        private readonly CustomWebApplicationFactory<Program> _factory;
        private readonly HttpClient _client;

        public AdminControllerTests(CustomWebApplicationFactory<Program> factory)
        {
            _factory = factory;
            _factory.ResetDatabase();

            _client = factory.CreateClient(new WebApplicationFactoryClientOptions
            {
                HandleCookies = true,
                BaseAddress = new Uri("https://localhost:5001")
            });
        }

        private async Task<(string email, string password, string token, Guid userId)> CreateAndLoginUserAsync(string role = "User")
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
                user.Role = role;
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
        public async Task ActionUser_BanAndUnban_ShouldPublishCorrectEventsOnActualStateTransitions()
        {
            var publisher = _factory.Services.GetRequiredService<MockAutomationEventPublisher>();

            var (_, _, adminToken, _) = await CreateAndLoginUserAsync("Admin");
            var (targetEmail, _, _, targetUserId) = await CreateAndLoginUserAsync("User");

            // 1. Block the unblocked user -> Expect user.blocked event
            using var banRequest = new HttpRequestMessage(HttpMethod.Put, $"api/v1/admin/users/{targetUserId}/action");
            banRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
            banRequest.Content = JsonContent.Create(new AdminActionDto { Action = "Ban" });

            var banResponse = await _client.SendAsync(banRequest);
            Assert.Equal(HttpStatusCode.OK, banResponse.StatusCode);

            var blockedEvents = publisher.PublishedEvents
                .Where(e => e.Type == "user.blocked" && e.UserId == targetUserId.ToString())
                .ToList();

            Assert.Single(blockedEvents);
            var blockedEvent = blockedEvents[0];

            Assert.Equal("user.blocked", blockedEvent.Type);
            Assert.False(string.IsNullOrWhiteSpace(blockedEvent.Id));
            Assert.NotEqual(blockedEvent.UserId, blockedEvent.Id);
            Assert.Equal(targetUserId.ToString(), blockedEvent.UserId);
            Assert.Equal(DateTimeKind.Utc, blockedEvent.OccurredAt.Kind);
            Assert.NotNull(blockedEvent.User);
            Assert.Equal(targetEmail, blockedEvent.User.Email);
            Assert.True(blockedEvent.User.IsBlocked);
            Assert.NotNull(blockedEvent.Data);
            Assert.Equal("user_admin_action", blockedEvent.Data["source"]);

            // 2. Repeat Ban on already blocked user -> Expect NO additional event
            await Task.Delay(550);
            using var repeatBanRequest = new HttpRequestMessage(HttpMethod.Put, $"api/v1/admin/users/{targetUserId}/action");
            repeatBanRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
            repeatBanRequest.Content = JsonContent.Create(new AdminActionDto { Action = "Ban" });

            var repeatBanResponse = await _client.SendAsync(repeatBanRequest);
            Assert.Equal(HttpStatusCode.OK, repeatBanResponse.StatusCode);

            var blockedEventsAfterRepeat = publisher.PublishedEvents
                .Where(e => e.Type == "user.blocked" && e.UserId == targetUserId.ToString())
                .ToList();

            Assert.Single(blockedEventsAfterRepeat); // Still only 1 event

            // 3. Unblock the blocked user -> Expect user.unblocked event
            await Task.Delay(550);
            using var unbanRequest = new HttpRequestMessage(HttpMethod.Put, $"api/v1/admin/users/{targetUserId}/action");
            unbanRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
            unbanRequest.Content = JsonContent.Create(new AdminActionDto { Action = "Unban" });

            var unbanResponse = await _client.SendAsync(unbanRequest);
            Assert.Equal(HttpStatusCode.OK, unbanResponse.StatusCode);

            var unblockedEvents = publisher.PublishedEvents
                .Where(e => e.Type == "user.unblocked" && e.UserId == targetUserId.ToString())
                .ToList();

            Assert.Single(unblockedEvents);
            var unblockedEvent = unblockedEvents[0];

            Assert.Equal("user.unblocked", unblockedEvent.Type);
            Assert.False(string.IsNullOrWhiteSpace(unblockedEvent.Id));
            Assert.NotEqual(unblockedEvent.UserId, unblockedEvent.Id);
            Assert.Equal(targetUserId.ToString(), unblockedEvent.UserId);
            Assert.Equal(DateTimeKind.Utc, unblockedEvent.OccurredAt.Kind);
            Assert.NotNull(unblockedEvent.User);
            Assert.Equal(targetEmail, unblockedEvent.User.Email);
            Assert.False(unblockedEvent.User.IsBlocked);
            Assert.NotNull(unblockedEvent.Data);
            Assert.Equal("user_admin_action", unblockedEvent.Data["source"]);

            // 4. Repeat Unban on already unblocked user -> Expect NO additional event
            await Task.Delay(550);
            using var repeatUnbanRequest = new HttpRequestMessage(HttpMethod.Put, $"api/v1/admin/users/{targetUserId}/action");
            repeatUnbanRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
            repeatUnbanRequest.Content = JsonContent.Create(new AdminActionDto { Action = "Unban" });

            var repeatUnbanResponse = await _client.SendAsync(repeatUnbanRequest);
            Assert.Equal(HttpStatusCode.OK, repeatUnbanResponse.StatusCode);

            var unblockedEventsAfterRepeat = publisher.PublishedEvents
                .Where(e => e.Type == "user.unblocked" && e.UserId == targetUserId.ToString())
                .ToList();

            Assert.Single(unblockedEventsAfterRepeat); // Still only 1 event
        }
    }
}

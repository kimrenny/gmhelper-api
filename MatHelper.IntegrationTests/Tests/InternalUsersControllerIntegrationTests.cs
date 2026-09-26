using MatHelper.API.Common;
using MatHelper.BLL.Interfaces;
using MatHelper.CORE.Enums;
using MatHelper.CORE.Models;
using MatHelper.DAL.Database;
using MatHelper.DAL.Models;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace MatHelper.IntegrationTests.Tests
{
    public class InternalUsersControllerIntegrationTests : IClassFixture<CustomWebApplicationFactory<Program>>
    {
        private readonly CustomWebApplicationFactory<Program> _factory;
        private readonly HttpClient _client;

        public InternalUsersControllerIntegrationTests(CustomWebApplicationFactory<Program> factory)
        {
            _factory = factory;
            _factory.ResetDatabase();

            _client = factory.CreateClient(new WebApplicationFactoryClientOptions
            {
                HandleCookies = true,
                BaseAddress = new Uri("https://localhost:5001")
            });
        }

        private string GenerateServiceToken(string serviceName = "gmhelper-notify-api")
        {
            using var scope = _factory.Services.CreateScope();
            var tokenGen = scope.ServiceProvider.GetRequiredService<ITokenGeneratorService>();
            return tokenGen.GenerateServiceToken(serviceName);
        }

        private async Task<(Guid userId, string token)> CreateAndLoginUserAsync(string role = "User")
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
            return (userId, loginResult!.Data!.AccessToken!);
        }

        [Fact]
        public async Task GetUserById_NoToken_Returns401Unauthorized()
        {
            var userId = Guid.NewGuid();
            var request = new HttpRequestMessage(HttpMethod.Get, $"api/v1/internal/users/{userId}");

            var response = await _client.SendAsync(request);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task GetUserById_OrdinaryUserToken_Returns403Forbidden()
        {
            var (_, userToken) = await CreateAndLoginUserAsync("User");
            var targetUserId = Guid.NewGuid();

            var request = new HttpRequestMessage(HttpMethod.Get, $"api/v1/internal/users/{targetUserId}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", userToken);

            var response = await _client.SendAsync(request);

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        [Fact]
        public async Task GetUserById_ValidServiceToken_Returns200Ok_WithAuthoritativeUserData()
        {
            // Seed a test user directly in DB
            var userId = Guid.NewGuid();
            var activityTime = new DateTime(2026, 9, 20, 10, 30, 0, DateTimeKind.Utc);
            var regTime = new DateTime(2026, 1, 15, 8, 0, 0, DateTimeKind.Utc);

            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                db.Users.Add(new User
                {
                    Id = userId,
                    Username = "authoritative_alice",
                    Email = "alice@authoritative.org",
                    PasswordHash = "secret_hash_value_12345",
                    Role = "User",
                    Language = LanguageType.EN,
                    IsActive = true,
                    IsBlocked = false,
                    RegistrationDate = regTime,
                    LastActivityAt = activityTime
                });
                await db.SaveChangesAsync();
            }

            var serviceToken = GenerateServiceToken("gmhelper-notify-api");
            var request = new HttpRequestMessage(HttpMethod.Get, $"api/v1/internal/users/{userId}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", serviceToken);

            var response = await _client.SendAsync(request);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var rawJson = await response.Content.ReadAsStringAsync();
            var apiResponse = await response.Content.ReadFromJsonAsync<ApiResponse<InternalUserDto>>();

            Assert.NotNull(apiResponse);
            Assert.True(apiResponse.Success);
            Assert.NotNull(apiResponse.Data);

            // Verify all 8 contract fields
            Assert.Equal(userId, apiResponse.Data.Id);
            Assert.Equal("authoritative_alice", apiResponse.Data.Username);
            Assert.Equal("alice@authoritative.org", apiResponse.Data.Email);
            Assert.Equal("User", apiResponse.Data.Role);
            Assert.Equal("EN", apiResponse.Data.Language);
            Assert.False(apiResponse.Data.IsBlocked);
            Assert.True(apiResponse.Data.IsActive);
            Assert.Equal(regTime, apiResponse.Data.RegistrationDate);
            Assert.Equal(activityTime, apiResponse.Data.LastActivityAt);

            // Verify sensitive fields are NOT exposed in raw JSON
            Assert.DoesNotContain("secret_hash_value_12345", rawJson);
            Assert.DoesNotContain("passwordHash", rawJson, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("refreshToken", rawJson, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task GetUserById_ValidServiceToken_ReturnsNullLastActivityAt_WhenNotSet()
        {
            var userId = Guid.NewGuid();
            var regTime = new DateTime(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc);

            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                db.Users.Add(new User
                {
                    Id = userId,
                    Username = "inactive_bob",
                    Email = "bob@inactive.org",
                    PasswordHash = "secret_hash_bob",
                    Role = "User",
                    Language = LanguageType.RU,
                    IsActive = true,
                    IsBlocked = false,
                    RegistrationDate = regTime,
                    LastActivityAt = null
                });
                await db.SaveChangesAsync();
            }

            var serviceToken = GenerateServiceToken("gmhelper-notify-api");
            var request = new HttpRequestMessage(HttpMethod.Get, $"api/v1/internal/users/{userId}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", serviceToken);

            var response = await _client.SendAsync(request);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var apiResponse = await response.Content.ReadFromJsonAsync<ApiResponse<InternalUserDto>>();
            Assert.NotNull(apiResponse);
            Assert.True(apiResponse.Success);
            Assert.NotNull(apiResponse.Data);
            Assert.Null(apiResponse.Data.LastActivityAt);
            Assert.Equal("RU", apiResponse.Data.Language);

            // Verify raw JSON serialization contains lastActivityAt: null
            var rawJson = await response.Content.ReadAsStringAsync();
            using var jsonDoc = JsonDocument.Parse(rawJson);
            var dataProp = jsonDoc.RootElement.GetProperty("data");
            var lastActivityProp = dataProp.GetProperty("lastActivityAt");
            Assert.Equal(JsonValueKind.Null, lastActivityProp.ValueKind);
        }

        [Fact]
        public async Task GetUserById_ValidServiceToken_NonExistentUser_Returns404NotFound()
        {
            var nonExistentId = Guid.NewGuid();
            var serviceToken = GenerateServiceToken("gmhelper-notify-api");
            var request = new HttpRequestMessage(HttpMethod.Get, $"api/v1/internal/users/{nonExistentId}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", serviceToken);

            var response = await _client.SendAsync(request);

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            var apiResponse = await response.Content.ReadFromJsonAsync<ApiResponse<string>>();
            Assert.NotNull(apiResponse);
            Assert.False(apiResponse.Success);
            Assert.Equal("User not found.", apiResponse.Message);
        }

        [Fact]
        public async Task GetUserById_ValidServiceToken_InvalidGuid_Returns404NotFound()
        {
            var serviceToken = GenerateServiceToken("gmhelper-notify-api");
            var request = new HttpRequestMessage(HttpMethod.Get, "api/v1/internal/users/not-a-valid-guid");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", serviceToken);

            var response = await _client.SendAsync(request);

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            var apiResponse = await response.Content.ReadFromJsonAsync<ApiResponse<string>>();
            Assert.NotNull(apiResponse);
            Assert.False(apiResponse.Success);
            Assert.Equal("User not found.", apiResponse.Message);
        }

        [Fact]
        public async Task GetUserById_DoesNotModifyLastActivityAtOrUserState()
        {
            var userId = Guid.NewGuid();
            var activityTime = new DateTime(2026, 5, 10, 14, 0, 0, DateTimeKind.Utc);

            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                db.Users.Add(new User
                {
                    Id = userId,
                    Username = "charlie_check",
                    Email = "charlie@check.org",
                    PasswordHash = "charlie_hash",
                    Role = "User",
                    Language = LanguageType.KO,
                    IsActive = true,
                    IsBlocked = false,
                    RegistrationDate = DateTime.UtcNow.AddDays(-30),
                    LastActivityAt = activityTime
                });
                await db.SaveChangesAsync();
            }

            var serviceToken = GenerateServiceToken("gmhelper-notify-api");
            var request = new HttpRequestMessage(HttpMethod.Get, $"api/v1/internal/users/{userId}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", serviceToken);

            var response = await _client.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            // Re-read directly from database
            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var userInDb = await db.Users.AsNoTracking().FirstAsync(u => u.Id == userId);

                Assert.Equal(activityTime, userInDb.LastActivityAt);
                Assert.Equal("charlie_check", userInDb.Username);
                Assert.Equal("charlie@check.org", userInDb.Email);
                Assert.False(userInDb.IsBlocked);
                Assert.True(userInDb.IsActive);
                Assert.Equal(LanguageType.KO, userInDb.Language);
            }
        }

        [Fact]
        public async Task GetUsers_AudienceFilters_RoleAndLanguageAndRegistrationDate_MatchesCorrectUsers()
        {
            var user1Id = Guid.NewGuid();
            var user2Id = Guid.NewGuid();
            var user3Id = Guid.NewGuid();

            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                db.Users.AddRange(
                    new User
                    {
                        Id = user1Id,
                        Username = "aud_admin_en",
                        Email = "admin_en@aud.org",
                        PasswordHash = "hash1",
                        Role = "Admin",
                        Language = LanguageType.EN,
                        IsActive = true,
                        IsBlocked = false,
                        RegistrationDate = DateTime.UtcNow.AddDays(-2),
                    },
                    new User
                    {
                        Id = user2Id,
                        Username = "aud_user_en",
                        Email = "user_en@aud.org",
                        PasswordHash = "hash2",
                        Role = "User",
                        Language = LanguageType.EN,
                        IsActive = true,
                        IsBlocked = false,
                        RegistrationDate = DateTime.UtcNow.AddDays(-2),
                    },
                    new User
                    {
                        Id = user3Id,
                        Username = "aud_admin_ru",
                        Email = "admin_ru@aud.org",
                        PasswordHash = "hash3",
                        Role = "Admin",
                        Language = LanguageType.RU,
                        IsActive = true,
                        IsBlocked = false,
                        RegistrationDate = DateTime.UtcNow.AddDays(-40),
                    }
                );
                await db.SaveChangesAsync();
            }

            var serviceToken = GenerateServiceToken("gmhelper-notify-api");

            // Filter 1: Role=Admin & Language=EN
            var request1 = new HttpRequestMessage(HttpMethod.Get, "api/v1/internal/users?role=Admin&language=EN");
            request1.Headers.Authorization = new AuthenticationHeaderValue("Bearer", serviceToken);
            var response1 = await _client.SendAsync(request1);
            Assert.Equal(HttpStatusCode.OK, response1.StatusCode);
            var res1 = await response1.Content.ReadFromJsonAsync<ApiResponse<PagedResult<InternalUserDto>>>();
            Assert.NotNull(res1?.Data);
            Assert.Contains(res1.Data.Items, u => u.Id == user1Id);
            Assert.DoesNotContain(res1.Data.Items, u => u.Id == user2Id);
            Assert.DoesNotContain(res1.Data.Items, u => u.Id == user3Id);

            // Filter 2: registrationDate=last_7_days
            await Task.Delay(550);
            var request2 = new HttpRequestMessage(HttpMethod.Get, "api/v1/internal/users?registrationDate=last_7_days");
            request2.Headers.Authorization = new AuthenticationHeaderValue("Bearer", serviceToken);
            var response2 = await _client.SendAsync(request2);
            var raw2 = await response2.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.OK, response2.StatusCode);
            var res2 = JsonSerializer.Deserialize<ApiResponse<PagedResult<InternalUserDto>>>(raw2, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            Assert.NotNull(res2?.Data);
            Assert.Contains(res2.Data.Items, u => u.Id == user1Id);
            Assert.Contains(res2.Data.Items, u => u.Id == user2Id);
            Assert.DoesNotContain(res2.Data.Items, u => u.Id == user3Id); // > 7 days ago

            // Filter 3: registrationDate=older_30d
            await Task.Delay(550);
            var request3 = new HttpRequestMessage(HttpMethod.Get, "api/v1/internal/users?registrationDate=older_30d");
            request3.Headers.Authorization = new AuthenticationHeaderValue("Bearer", serviceToken);
            var response3 = await _client.SendAsync(request3);
            Assert.Equal(HttpStatusCode.OK, response3.StatusCode);
            var res3 = await response3.Content.ReadFromJsonAsync<ApiResponse<PagedResult<InternalUserDto>>>();
            Assert.NotNull(res3?.Data);
            Assert.DoesNotContain(res3.Data.Items, u => u.Id == user1Id);
            Assert.DoesNotContain(res3.Data.Items, u => u.Id == user2Id);
            Assert.Contains(res3.Data.Items, u => u.Id == user3Id);

            // Filter 4: emailConfirmed=unconfirmed -> matches 0
            await Task.Delay(550);
            var request4 = new HttpRequestMessage(HttpMethod.Get, "api/v1/internal/users?emailConfirmed=unconfirmed");
            request4.Headers.Authorization = new AuthenticationHeaderValue("Bearer", serviceToken);
            var response4 = await _client.SendAsync(request4);
            Assert.Equal(HttpStatusCode.OK, response4.StatusCode);
            var res4 = await response4.Content.ReadFromJsonAsync<ApiResponse<PagedResult<InternalUserDto>>>();
            Assert.NotNull(res4?.Data);
            Assert.Empty(res4.Data.Items);
        }
    }
}

using MatHelper.CORE.Enums;
using MatHelper.CORE.Models;
using MatHelper.DAL.Database;
using MatHelper.DAL.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MatHelper.Tests
{
    public class UserRepositoryTests
    {
        private AppDbContext CreateDbContext()
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;

            return new AppDbContext(options);
        }

        [Fact]
        public async Task SearchUsersAsync_MatchesExactEmail()
        {
            using var context = CreateDbContext();
            var targetUser = new User
            {
                Id = Guid.NewGuid(),
                Username = "target_user",
                Email = "exactmatch@example.com",
                PasswordHash = "hash",
                Role = "User",
                Language = LanguageType.EN,
                IsActive = true,
                RegistrationDate = DateTime.UtcNow
            };
            var otherUser = new User
            {
                Id = Guid.NewGuid(),
                Username = "other_user",
                Email = "other@example.com",
                PasswordHash = "hash",
                Role = "User",
                Language = LanguageType.EN,
                IsActive = true,
                RegistrationDate = DateTime.UtcNow
            };
            context.Users.AddRange(targetUser, otherUser);
            await context.SaveChangesAsync();

            var repository = new UserRepository(context);
            var results = await repository.SearchUsersAsync("exactmatch@example.com", 20);

            Assert.Single(results);
            Assert.Equal(targetUser.Id, results[0].Id);
            Assert.Equal("exactmatch@example.com", results[0].Email);
        }

        [Fact]
        public async Task SearchUsersAsync_MatchesPartialEmail()
        {
            using var context = CreateDbContext();
            var user1 = new User
            {
                Id = Guid.NewGuid(),
                Username = "user_one",
                Email = "alice.smith@domain.com",
                PasswordHash = "hash",
                Role = "User",
                Language = LanguageType.EN,
                IsActive = true,
                RegistrationDate = DateTime.UtcNow
            };
            var user2 = new User
            {
                Id = Guid.NewGuid(),
                Username = "user_two",
                Email = "bob.smith@other.com",
                PasswordHash = "hash",
                Role = "User",
                Language = LanguageType.EN,
                IsActive = true,
                RegistrationDate = DateTime.UtcNow
            };
            var user3 = new User
            {
                Id = Guid.NewGuid(),
                Username = "user_three",
                Email = "charlie@nowhere.com",
                PasswordHash = "hash",
                Role = "User",
                Language = LanguageType.EN,
                IsActive = true,
                RegistrationDate = DateTime.UtcNow
            };
            context.Users.AddRange(user1, user2, user3);
            await context.SaveChangesAsync();

            var repository = new UserRepository(context);
            var results = await repository.SearchUsersAsync(".smith@", 20);

            Assert.Equal(2, results.Count);
            Assert.Contains(results, u => u.Email == "alice.smith@domain.com");
            Assert.Contains(results, u => u.Email == "bob.smith@other.com");
        }

        [Fact]
        public async Task SearchUsersAsync_MatchesPartialUsername()
        {
            using var context = CreateDbContext();
            var user = new User
            {
                Id = Guid.NewGuid(),
                Username = "commander_shepard",
                Email = "shepard@normandy.com",
                PasswordHash = "hash",
                Role = "User",
                Language = LanguageType.EN,
                IsActive = true,
                RegistrationDate = DateTime.UtcNow
            };
            context.Users.Add(user);
            await context.SaveChangesAsync();

            var repository = new UserRepository(context);
            var results = await repository.SearchUsersAsync("shepard", 20);

            Assert.Single(results);
            Assert.Equal("commander_shepard", results[0].Username);
        }

        [Fact]
        public async Task SearchUsersAsync_IsCaseInsensitive()
        {
            using var context = CreateDbContext();
            var user = new User
            {
                Id = Guid.NewGuid(),
                Username = "JohnDoe",
                Email = "John.Doe@Corporate.COM",
                PasswordHash = "hash",
                Role = "User",
                Language = LanguageType.EN,
                IsActive = true,
                RegistrationDate = DateTime.UtcNow
            };
            context.Users.Add(user);
            await context.SaveChangesAsync();

            var repository = new UserRepository(context);

            var usernameResults = await repository.SearchUsersAsync("johndoe", 20);
            Assert.Single(usernameResults);
            Assert.Equal("JohnDoe", usernameResults[0].Username);

            var emailResults = await repository.SearchUsersAsync("corporate.com", 20);
            Assert.Single(emailResults);
            Assert.Equal("John.Doe@Corporate.COM", emailResults[0].Email);
        }

        [Fact]
        public async Task SearchUsersAsync_TrimsWhitespace()
        {
            using var context = CreateDbContext();
            var user = new User
            {
                Id = Guid.NewGuid(),
                Username = "whitespace_tester",
                Email = "whitespace@test.com",
                PasswordHash = "hash",
                Role = "User",
                Language = LanguageType.EN,
                IsActive = true,
                RegistrationDate = DateTime.UtcNow
            };
            context.Users.Add(user);
            await context.SaveChangesAsync();

            var repository = new UserRepository(context);
            var results = await repository.SearchUsersAsync("   whitespace   ", 20);

            Assert.Single(results);
            Assert.Equal("whitespace_tester", results[0].Username);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public async Task SearchUsersAsync_ReturnsEmptyList_WhenQueryIsNullOrWhitespace(string? query)
        {
            using var context = CreateDbContext();
            var repository = new UserRepository(context);
            var results = await repository.SearchUsersAsync(query!, 20);

            Assert.NotNull(results);
            Assert.Empty(results);
        }

        [Fact]
        public async Task SearchUsersAsync_EnforcesLimitAndDeterministicOrdering()
        {
            using var context = CreateDbContext();
            for (int i = 0; i < 30; i++)
            {
                context.Users.Add(new User
                {
                    Id = Guid.NewGuid(),
                    Username = $"testuser_{i:D2}",
                    Email = $"user_{i:D2}@example.com",
                    PasswordHash = "hash",
                    Role = "User",
                    Language = LanguageType.EN,
                    IsActive = true,
                    RegistrationDate = DateTime.UtcNow
                });
            }
            await context.SaveChangesAsync();

            var repository = new UserRepository(context);
            var results = await repository.SearchUsersAsync("testuser", limit: 5);

            Assert.Equal(5, results.Count);
            // Ordering should be testuser_00, testuser_01, testuser_02, testuser_03, testuser_04
            Assert.Equal("testuser_00", results[0].Username);
            Assert.Equal("testuser_01", results[1].Username);
            Assert.Equal("testuser_02", results[2].Username);
            Assert.Equal("testuser_03", results[3].Username);
            Assert.Equal("testuser_04", results[4].Username);
        }

        [Fact]
        public async Task GetInternalUsersPagedAsync_AppliesActiveOnlyFilter()
        {
            using var context = CreateDbContext();
            var activeUser = new User
            {
                Id = Guid.NewGuid(),
                Username = "active_user",
                Email = "active@example.com",
                PasswordHash = "hash",
                Role = "User",
                Language = LanguageType.EN,
                IsActive = true,
                IsBlocked = false,
                RegistrationDate = DateTime.UtcNow
            };
            var inactiveUser = new User
            {
                Id = Guid.NewGuid(),
                Username = "inactive_user",
                Email = "inactive@example.com",
                PasswordHash = "hash",
                Role = "User",
                Language = LanguageType.EN,
                IsActive = false,
                IsBlocked = false,
                RegistrationDate = DateTime.UtcNow
            };
            context.Users.AddRange(activeUser, inactiveUser);
            await context.SaveChangesAsync();

            var repository = new UserRepository(context);

            var activeResults = await repository.GetInternalUsersPagedAsync(page: 1, pageSize: 50, activeOnly: true, unblockedOnly: false);
            Assert.Single(activeResults.Items);
            Assert.Equal(activeUser.Id, activeResults.Items[0].Id);
            Assert.Equal(1, activeResults.TotalCount);

            var allResults = await repository.GetInternalUsersPagedAsync(page: 1, pageSize: 50, activeOnly: false, unblockedOnly: false);
            Assert.Equal(2, allResults.Items.Count);
            Assert.Equal(2, allResults.TotalCount);
        }

        [Fact]
        public async Task GetInternalUsersPagedAsync_AppliesUnblockedOnlyFilter()
        {
            using var context = CreateDbContext();
            var unblockedUser = new User
            {
                Id = Guid.NewGuid(),
                Username = "unblocked_user",
                Email = "unblocked@example.com",
                PasswordHash = "hash",
                Role = "User",
                Language = LanguageType.EN,
                IsActive = true,
                IsBlocked = false,
                RegistrationDate = DateTime.UtcNow
            };
            var blockedUser = new User
            {
                Id = Guid.NewGuid(),
                Username = "blocked_user",
                Email = "blocked@example.com",
                PasswordHash = "hash",
                Role = "User",
                Language = LanguageType.EN,
                IsActive = true,
                IsBlocked = true,
                RegistrationDate = DateTime.UtcNow
            };
            context.Users.AddRange(unblockedUser, blockedUser);
            await context.SaveChangesAsync();

            var repository = new UserRepository(context);

            var unblockedResults = await repository.GetInternalUsersPagedAsync(page: 1, pageSize: 50, activeOnly: false, unblockedOnly: true);
            Assert.Single(unblockedResults.Items);
            Assert.Equal(unblockedUser.Id, unblockedResults.Items[0].Id);
            Assert.Equal(1, unblockedResults.TotalCount);

            var allResults = await repository.GetInternalUsersPagedAsync(page: 1, pageSize: 50, activeOnly: false, unblockedOnly: false);
            Assert.Equal(2, allResults.Items.Count);
            Assert.Equal(2, allResults.TotalCount);
        }

        [Fact]
        public async Task GetInternalUsersPagedAsync_AppliesBothFiltersTogether()
        {
            using var context = CreateDbContext();
            var validUser = new User
            {
                Id = Guid.NewGuid(),
                Username = "valid_user",
                Email = "valid@example.com",
                PasswordHash = "hash",
                Role = "User",
                Language = LanguageType.EN,
                IsActive = true,
                IsBlocked = false,
                RegistrationDate = DateTime.UtcNow
            };
            var blockedActive = new User
            {
                Id = Guid.NewGuid(),
                Username = "blocked_active",
                Email = "blocked_active@example.com",
                PasswordHash = "hash",
                Role = "User",
                Language = LanguageType.EN,
                IsActive = true,
                IsBlocked = true,
                RegistrationDate = DateTime.UtcNow
            };
            var unblockedInactive = new User
            {
                Id = Guid.NewGuid(),
                Username = "unblocked_inactive",
                Email = "unblocked_inactive@example.com",
                PasswordHash = "hash",
                Role = "User",
                Language = LanguageType.EN,
                IsActive = false,
                IsBlocked = false,
                RegistrationDate = DateTime.UtcNow
            };
            var blockedInactive = new User
            {
                Id = Guid.NewGuid(),
                Username = "blocked_inactive",
                Email = "blocked_inactive@example.com",
                PasswordHash = "hash",
                Role = "User",
                Language = LanguageType.EN,
                IsActive = false,
                IsBlocked = true,
                RegistrationDate = DateTime.UtcNow
            };

            context.Users.AddRange(validUser, blockedActive, unblockedInactive, blockedInactive);
            await context.SaveChangesAsync();

            var repository = new UserRepository(context);
            var results = await repository.GetInternalUsersPagedAsync(page: 1, pageSize: 50, activeOnly: true, unblockedOnly: true);

            Assert.Single(results.Items);
            Assert.Equal(validUser.Id, results.Items[0].Id);
            Assert.Equal(1, results.TotalCount);
        }

        [Fact]
        public async Task GetInternalUsersPagedAsync_EnforcesDeterministicOrderingByIdAndPagination()
        {
            using var context = CreateDbContext();
            var id1 = Guid.Parse("00000000-0000-0000-0000-000000000001");
            var id2 = Guid.Parse("00000000-0000-0000-0000-000000000002");
            var id3 = Guid.Parse("00000000-0000-0000-0000-000000000003");

            context.Users.AddRange(
                new User { Id = id3, Username = "user3", Email = "u3@test.com", PasswordHash = "h", Role = "User", IsActive = true, IsBlocked = false, RegistrationDate = DateTime.UtcNow },
                new User { Id = id1, Username = "user1", Email = "u1@test.com", PasswordHash = "h", Role = "User", IsActive = true, IsBlocked = false, RegistrationDate = DateTime.UtcNow },
                new User { Id = id2, Username = "user2", Email = "u2@test.com", PasswordHash = "h", Role = "User", IsActive = true, IsBlocked = false, RegistrationDate = DateTime.UtcNow }
            );
            await context.SaveChangesAsync();

            var repository = new UserRepository(context);

            var page1 = await repository.GetInternalUsersPagedAsync(page: 1, pageSize: 2, activeOnly: true, unblockedOnly: true);
            Assert.Equal(2, page1.Items.Count);
            Assert.Equal(3, page1.TotalCount);
            Assert.Equal(id1, page1.Items[0].Id);
            Assert.Equal(id2, page1.Items[1].Id);
            Assert.True(page1.HasNextPage);

            var page2 = await repository.GetInternalUsersPagedAsync(page: 2, pageSize: 2, activeOnly: true, unblockedOnly: true);
            Assert.Single(page2.Items);
            Assert.Equal(3, page2.TotalCount);
            Assert.Equal(id3, page2.Items[0].Id);
            Assert.False(page2.HasNextPage);
        }
    }
}

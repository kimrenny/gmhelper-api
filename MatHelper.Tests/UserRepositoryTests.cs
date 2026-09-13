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
    }
}

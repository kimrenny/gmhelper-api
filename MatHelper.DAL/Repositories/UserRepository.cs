using MatHelper.CORE.Enums;
using MatHelper.CORE.Models;
using MatHelper.DAL.Database;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;
using MatHelper.DAL.Interfaces;

namespace MatHelper.DAL.Repositories
{
    public class UserRepository : IUserRepository
    {
        private readonly AppDbContext _context;

        public UserRepository(AppDbContext context)
        {
            _context = context;
        }

        public void Detach<TEntity>(TEntity entity) where TEntity : class
        {
            _context.Entry(entity).State = EntityState.Detached;
        }

        public async Task AddUserAsync(User user)
        {
            _context.Users.Add(user);
            await _context.SaveChangesAsync();
        }

        public async Task DeleteUserAsync(User user)
        {
            _context.Users.Remove(user);
            await _context.SaveChangesAsync();
        }

        public async Task<bool> ChangePassword(User user, string password)
        {
            try
            {
                user.PasswordHash = password;
                user.LastActivityAt = DateTime.UtcNow;

                await _context.SaveChangesAsync();

                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }


        public async Task<User?> GetUserAsync(Expression<Func<User, bool>> predicate)
        {
            return await _context.Users.Include(u => u.LoginTokens).FirstOrDefaultAsync(predicate);
        }

        public async Task<User?> GetUserByEmailAsync(string email)
        {
            ValidateEmailOrUsername(email, "email");

            var user = await GetUserAsync(u => u.Email == email);
            if (user != null && user.IsBlocked)
            {
                throw new UnauthorizedAccessException("User is blocked.");
            }
            return user;
        }

        public async Task<User?> GetUserByUsernameAsync(string username)
        {
            ValidateEmailOrUsername(username, "username");

            var user = await GetUserAsync(u => u.Username == username);
            if (user != null && user.IsBlocked)
            {
                throw new InvalidOperationException("User is blocked.");
            }
            return user;
        }        

        public async Task<User?> GetUserByIdAsync(Guid id)
        {
            if (id == Guid.Empty)
            {
                throw new InvalidDataException("id is null or empty");
            }

            var user = await GetUserAsync(u => u.Id == id);
            if (user != null && user.IsBlocked)
            {
                throw new InvalidOperationException("User is blocked.");
            }
            return user;
        }
        
        public async Task<String> GetUserLanguageByEmail(string email)
        {
            ValidateEmailOrUsername(email, "email");

            var user = await GetUserAsync(u => u.Email == email);
            if(user != null && user.IsBlocked)
            {
                throw new InvalidOperationException("User is blocked.");
            }

            return user?.Language.ToString() ?? "En";
        }

        public async Task<List<User>> GetUsersByIpAsync(string ipAddress)
        {
            if (string.IsNullOrWhiteSpace(ipAddress))
            {
                throw new InvalidDataException("ipAddress is null or empty");
            }

            return await _context.Users.Include(u => u.LoginTokens).Where(u => u.LoginTokens != null && u.LoginTokens.Any(t => t.IpAddress == ipAddress)).ToListAsync();
        }

        public async Task<int> GetUserCountByIpAsync(string ipAddress)
        {
            if (string.IsNullOrWhiteSpace(ipAddress))
            {
                return 0;
            }

            return await _context.LoginTokens.Where(t => t.IpAddress == ipAddress).Select(t => t.UserId).Distinct().CountAsync();
        }

        public async Task<List<User>> GetAllUsersAsync()
        {
            var users = await _context.Users.ToListAsync();
            return users ?? new List<User>();
        }

        public IQueryable<User> GetUsersQuery()
        {
            return _context.Users.AsQueryable();
        }

        public async Task<List<TokenDto>> GetAllTokensAsync()
        {
            try
            {
                var tokens = await _context.LoginTokens
                    .Include(t => t.DeviceInfo)
                    .ToListAsync();

                if (tokens == null || !tokens.Any())
                {
                    throw new InvalidOperationException("No tokens found.");
                }

                return tokens.Select(t => new TokenDto
                {
                    Id = t.Id,
                    Token = t.Token,
                    Expiration = t.Expiration,
                    RefreshTokenExpiration = t.RefreshTokenExpiration,
                    UserId = t.UserId,
                    DeviceInfo = t.DeviceInfo != null
                        ? new DeviceInfo
                        {
                            Platform = t.DeviceInfo.Platform,
                            UserAgent = t.DeviceInfo.UserAgent
                        }
                        : new DeviceInfo(),
                    IpAddress = t.IpAddress,
                    IsActive = t.IsActive,
                }).ToList();
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Could not fetch tokens.", ex);
            }
        }

        public IQueryable<LoginToken> GetTokensQuery()
        {
            return _context.LoginTokens
                .Include(t => t.DeviceInfo)
                .AsQueryable();
        }

        public async Task UpdateUserAsync(User user)
        {
            _context.Users.Update(user);
            await _context.SaveChangesAsync();
        }

        public async Task<(User User, bool StateChanged)> ActionUserAsync(Guid id, UserAction action)
        {
            if (id == Guid.Empty)
            {
                throw new InvalidDataException("Id is null or empty");
            }

            var user = await _context.Users.Include(u => u.LoginTokens).FirstOrDefaultAsync(u => u.Id == id);

            if (user == null)
            {
                throw new InvalidOperationException("User not found.");
            }

            bool wasBlocked = user.IsBlocked;
            bool targetBlocked = action == UserAction.Ban;

            if (wasBlocked == targetBlocked)
            {
                return (user, false);
            }

            user.IsBlocked = targetBlocked;

            await _context.SaveChangesAsync();

            return (user, true);
        }

        public async Task<List<RegistrationsDto>> GetUserRegistrationsGroupedByDateAsync()
        {
            var users = await _context.Users
                                      .Select(u => u.RegistrationDate)
                                      .ToListAsync();

            var groupedByDate = users
                .GroupBy(date => date.Date)
                .Select(group => new RegistrationsDto
                { 
                    Date = DateOnly.FromDateTime(group.Key), 
                    Registrations = (ushort)group.Count() 
                })
                .ToList();

            return groupedByDate;
        }

        public async Task<List<User>> SearchUsersAsync(string query, int limit = 20)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return new List<User>();
            }

            var trimmed = query.Trim().ToLower();
            var effectiveLimit = limit <= 0 ? 20 : Math.Min(limit, 50);

            return await _context.Users
                .Where(u => u.Username.ToLower().Contains(trimmed) || u.Email.ToLower().Contains(trimmed))
                .OrderBy(u => u.Username)
                .ThenBy(u => u.Email)
                .Take(effectiveLimit)
                .ToListAsync();
        }

        public Task<PagedResult<User>> GetInternalUsersPagedAsync(int page = 1, int pageSize = 50, bool activeOnly = true, bool unblockedOnly = true)
        {
            return GetInternalUsersPagedAsync(page, pageSize, activeOnly, unblockedOnly, null, null, null, null, null);
        }

        public async Task<PagedResult<User>> GetInternalUsersPagedAsync(
            int page,
            int pageSize,
            bool activeOnly,
            bool unblockedOnly,
            string? role,
            string? language,
            string? registrationDate,
            string? emailConfirmed,
            string? accountStatus)
        {
            var effectivePage = page < 1 ? 1 : page;
            var effectivePageSize = pageSize < 1 ? 50 : Math.Min(pageSize, 250);

            var query = _context.Users.AsNoTracking().AsQueryable();

            if (activeOnly)
            {
                query = query.Where(u => u.IsActive);
            }

            if (unblockedOnly)
            {
                query = query.Where(u => !u.IsBlocked);
            }

            if (!string.IsNullOrWhiteSpace(role))
            {
                var trimmedRole = role.Trim();
                if (!trimmedRole.Equals("all", StringComparison.OrdinalIgnoreCase) &&
                    !trimmedRole.Equals("all roles", StringComparison.OrdinalIgnoreCase))
                {
                    query = query.Where(u => u.Role.ToLower() == trimmedRole.ToLower());
                }
            }

            if (!string.IsNullOrWhiteSpace(language))
            {
                var trimmedLang = language.Trim();
                if (!trimmedLang.Equals("all", StringComparison.OrdinalIgnoreCase))
                {
                    if (Enum.TryParse<LanguageType>(trimmedLang, true, out var langEnum))
                    {
                        query = query.Where(u => u.Language == langEnum);
                    }
                    else
                    {
                        query = query.Where(u => false);
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(registrationDate))
            {
                var trimmedReg = registrationDate.Trim().ToLowerInvariant();
                if (!trimmedReg.Equals("any", StringComparison.OrdinalIgnoreCase) &&
                    !trimmedReg.Equals("any date", StringComparison.OrdinalIgnoreCase))
                {
                    var now = DateTime.UtcNow;
                    if (trimmedReg == "today")
                    {
                        var startOfToday = now.Date;
                        query = query.Where(u => u.RegistrationDate >= startOfToday);
                    }
                    else if (trimmedReg == "last_7_days" || trimmedReg == "7d" || trimmedReg == "7 days" || trimmedReg == "last 7 days")
                    {
                        var cutoff = now.AddDays(-7);
                        query = query.Where(u => u.RegistrationDate >= cutoff);
                    }
                    else if (trimmedReg == "last_30_days" || trimmedReg == "30d" || trimmedReg == "30 days" || trimmedReg == "last 30 days")
                    {
                        var cutoff = now.AddDays(-30);
                        query = query.Where(u => u.RegistrationDate >= cutoff);
                    }
                    else if (trimmedReg == "last_90_days" || trimmedReg == "90d" || trimmedReg == "90 days" || trimmedReg == "last 90 days")
                    {
                        var cutoff = now.AddDays(-90);
                        query = query.Where(u => u.RegistrationDate >= cutoff);
                    }
                    else if (trimmedReg == "older_30d" || trimmedReg == "older_30_days" || trimmedReg == ">30d" || trimmedReg == "older than 30 days")
                    {
                        var cutoff = now.AddDays(-30);
                        query = query.Where(u => u.RegistrationDate < cutoff);
                    }
                    else if (trimmedReg == "older_90d" || trimmedReg == "older_90_days" || trimmedReg == ">90d" || trimmedReg == "older than 90 days")
                    {
                        var cutoff = now.AddDays(-90);
                        query = query.Where(u => u.RegistrationDate < cutoff);
                    }
                    else if (DateTime.TryParse(registrationDate, null, System.Globalization.DateTimeStyles.AdjustToUniversal, out var parsedDate))
                    {
                        query = query.Where(u => u.RegistrationDate >= parsedDate);
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(emailConfirmed))
            {
                var trimmedEmailConf = emailConfirmed.Trim().ToLowerInvariant();
                if (trimmedEmailConf == "unconfirmed" || trimmedEmailConf == "false" || trimmedEmailConf == "no" || trimmedEmailConf == "0")
                {
                    query = query.Where(u => false);
                }
            }

            int totalCount = await query.CountAsync();

            var users = await query
                .OrderBy(u => u.Id)
                .Skip((effectivePage - 1) * effectivePageSize)
                .Take(effectivePageSize)
                .ToListAsync();

            return new PagedResult<User>
            {
                Items = users,
                TotalCount = totalCount,
                Page = effectivePage,
                PageSize = effectivePageSize
            };
        }

        private void ValidateEmailOrUsername(string value, string fieldName)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidDataException($"{fieldName} is null or empty.");
            }
        }

        public async Task SaveChangesAsync()
        {
            try
            {
                await _context.SaveChangesAsync();
            }
            catch(DbUpdateConcurrencyException ex)
            {
                foreach(var entry in ex.Entries)
                {
                    if(entry.Entity is User)
                    {
                        var proposedValues = entry.CurrentValues;
                        var databaseValues = entry.GetDatabaseValues();
                    }
                }

                throw new Exception("Concurrent exception due updating the data");
            }
        }
    }
}
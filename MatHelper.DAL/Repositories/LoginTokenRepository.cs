using MatHelper.DAL.Database;
using Microsoft.EntityFrameworkCore;
using MatHelper.CORE.Models;
using MatHelper.DAL.Interfaces;

namespace MatHelper.DAL.Repositories
{
    public class LoginTokenRepository : ILoginTokenRepository
    {
        private readonly AppDbContext _context;

        public LoginTokenRepository(AppDbContext context)
        {
            _context = context;
        }

        public async Task<LoginToken?> GetLoginTokenByRefreshTokenAsync(string refreshToken)
        {
            return await _context.LoginTokens
                .Include(t => t.User)
                .FirstOrDefaultAsync(t => t.RefreshToken == refreshToken);
        }

        public async Task<LoginToken?> GetLoginTokenAsync(string token)
        {
            return await _context.LoginTokens.Where(t => t.Token == token).FirstOrDefaultAsync();
        }

        public async Task RemoveLoginTokenAsync(LoginToken token)
        {
            _context.LoginTokens.Remove(token);
            await _context.SaveChangesAsync();
        }

        public async Task DeactivateTokenAsync(LoginToken token)
        {
            token.IsActive = false;
            _context.LoginTokens.Update(token);
            await _context.SaveChangesAsync();
        }

        public async Task DeactivateAllUserTokensAsync(Guid userId)
        {
            if (_context.Database.IsRelational())
            {
                await _context.LoginTokens
                    .Where(t => t.UserId == userId && t.IsActive)
                    .ExecuteUpdateAsync(setters =>
                        setters.SetProperty(t => t.IsActive, false));
            }
            else
            {
                var tokens = await _context.LoginTokens
                    .Where(t => t.UserId == userId && t.IsActive)
                    .ToListAsync();

                foreach (var token in tokens)
                    token.IsActive = false;

                await _context.SaveChangesAsync();
            }
        }

        public async Task<List<LoginToken>> GetAllLoginTokensAsync()
        {
            return await _context.LoginTokens.Include(t => t.User).ToListAsync();
        }

        public async Task ActionTokenAsync(string authToken, string action)
        {
            if (string.IsNullOrWhiteSpace(authToken))
            {
                throw new InvalidDataException("Id is null or empty");
            }

            var token = await _context.LoginTokens.FirstOrDefaultAsync(u => u.Token == authToken);

            if (token == null)
            {
                throw new InvalidOperationException("Token not found.");
            }

            if (action == "disable")
            {
                token.IsActive = false;
            }
            else if (action == "activate")
            {
                token.IsActive = true;
            }

            await _context.SaveChangesAsync();
        }

        public async Task<DashboardTokensDto> GetDashboardTokensAsync()
        {
            try
            {
                var tokensData = await _context.Users
                    .Where(u => u.LoginTokens!.Any())
                    .Select(u => new
                    {
                        u.Id,
                        u.Role,
                        ActiveTokens = u.LoginTokens!.Count(t => t.Expiration > DateTime.UtcNow && t.IsActive),
                        TotalTokens = u.LoginTokens!.Count()
                    })
                    .ToListAsync();

                var activeTokens = tokensData.Sum(x => x.ActiveTokens);
                var totalTokens = tokensData.Sum(x => x.TotalTokens);
                var activeAdminTokens = tokensData.Where(u => u.Role.ToLower() == "admin" || u.Role.ToLower() == "owner")
                                                  .Sum(u => u.ActiveTokens);
                var totalAdminTokens = tokensData.Where(u => u.Role.ToLower() == "admin" || u.Role.ToLower() == "owner")
                                                 .Sum(u => u.TotalTokens);

                return new DashboardTokensDto
                {
                    ActiveTokens = activeTokens,
                    TotalTokens = totalTokens,
                    ActiveAdminTokens = activeAdminTokens,
                    TotalAdminTokens = totalAdminTokens
                };
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Could not fetch tokens.", ex);
            }
        }

        public async Task<List<UserIp>> GetUsersWithLastIpAsync()
        {
            var users = await _context.Users
                .Include(u => u.LoginTokens)
                .AsNoTracking()
                .ToListAsync();

            var result = users
                .Where(u => u.LoginTokens != null && u.LoginTokens.Any())
                .Select(u => {
                    var tokens = u.LoginTokens!;
                    return new UserIp
                    {
                        Id = u.Id,
                        IpAddress = tokens
                            .OrderByDescending(t => t.Expiration)
                            .Select(t => t.IpAddress)
                            .FirstOrDefault() ?? "Unknown"
                    };
                })
                .ToList();

            return result;
        }

        public async Task<Guid?> GetUserIdByAuthTokenAsync(string authToken)
        {
            if (string.IsNullOrWhiteSpace(authToken))
            {
                throw new InvalidDataException("Token is null or empty");
            }

            var token = await _context.LoginTokens
                .Include(t => t.User)
                .FirstOrDefaultAsync(t => t.Token == authToken && t.IsActive);

            if (token == null)
            {
                return null;
            }

            return token.UserId != Guid.Empty ? token.UserId : token.User?.Id;
        }

        public async Task SaveChangesAsync()
        {
            await _context.SaveChangesAsync();
        }
    }
}

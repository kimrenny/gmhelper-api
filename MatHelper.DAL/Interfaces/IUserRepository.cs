using MatHelper.CORE.Enums;
using MatHelper.CORE.Models;
using System.Linq.Expressions;

namespace MatHelper.DAL.Interfaces
{
    public interface IUserRepository
    {
        void Detach<TEntity>(TEntity entity) where TEntity : class;

        Task AddUserAsync(User user);
        Task DeleteUserAsync(User user);
        Task<bool> ChangePassword(User user, string password);

        Task<User?> GetUserAsync(Expression<Func<User, bool>> predicate);
        Task<User?> GetUserByEmailAsync(string email);
        Task<User?> GetUserByUsernameAsync(string username);
        Task<User?> GetUserByIdAsync(Guid id);

        Task<String> GetUserLanguageByEmail(string username);

        Task<List<User>> GetUsersByIpAsync(string ipAddress);
        Task<int> GetUserCountByIpAsync(string ipAddress);

        Task<List<User>> GetAllUsersAsync();
        IQueryable<User> GetUsersQuery();
        Task<List<TokenDto>> GetAllTokensAsync();
        IQueryable<LoginToken> GetTokensQuery();

        Task UpdateUserAsync(User user);
        Task ActionUserAsync(Guid id, UserAction action);

        Task<List<RegistrationsDto>> GetUserRegistrationsGroupedByDateAsync();

        Task<List<User>> SearchUsersAsync(string query, int limit = 20);

        Task<PagedResult<User>> GetInternalUsersPagedAsync(int page = 1, int pageSize = 50, bool activeOnly = true, bool unblockedOnly = true);

        Task SaveChangesAsync();
    }
}

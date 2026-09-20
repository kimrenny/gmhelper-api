using MatHelper.CORE.Enums;

namespace MatHelper.CORE.Models
{
    public class User
    {
        public required Guid Id { get; set; }
        public required string Username { get; set; }
        public required string Email { get; set; }
        public required DateTime RegistrationDate { get; set; }
        public required string PasswordHash { get; set; }
        public byte[]? Avatar { get; set; }
        public required string Role { get; set; }
        public List<LoginToken>? LoginTokens { get; set; }
        public bool IsBlocked { get; set; }
        public bool IsActive { get; set; }
        public LanguageType Language { get; set; } = LanguageType.EN;
        public DateTime? LastActivityAt { get; set; }
    }
}
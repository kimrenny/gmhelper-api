namespace MatHelper.CORE.Models
{
    public class InternalUserDto
    {
        public required Guid Id { get; set; }
        public required string Username { get; set; }
        public required string Email { get; set; }
        public required string Role { get; set; }
        public required string Language { get; set; }
        public bool IsActive { get; set; }
        public bool IsBlocked { get; set; }
        public DateTime RegistrationDate { get; set; }
    }
}

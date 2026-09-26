namespace MatHelper.CORE.Models
{
    public class SendNotificationRequest
    {
        public string TemplateKey { get; set; } = string.Empty;
        public string? Locale { get; set; }
        public string? ExternalUserId { get; set; }
        public string RecipientEmail { get; set; } = string.Empty;
        public string? RecipientName { get; set; }
        public Dictionary<string, object>? Variables { get; set; }
    }
}

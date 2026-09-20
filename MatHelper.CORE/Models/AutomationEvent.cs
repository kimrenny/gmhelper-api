using System.Text.Json.Serialization;

namespace MatHelper.CORE.Models
{
    /// <summary>
    /// Represents an internal automation domain event payload published to gmhelper-notify-api.
    /// </summary>
    public class AutomationEvent
    {
        /// <summary>
        /// Unique event identifier. Must remain stable across retries for idempotency.
        /// </summary>
        [JsonPropertyName("id")]
        public required string Id { get; set; }

        /// <summary>
        /// Event type identifier (e.g. "user.registered", "user.inactive", "email.confirmed", "password.changed").
        /// </summary>
        [JsonPropertyName("type")]
        public required string Type { get; set; }

        /// <summary>
        /// Identifier of the affected user.
        /// </summary>
        [JsonPropertyName("userId")]
        public required string UserId { get; set; }

        /// <summary>
        /// Event timestamp in UTC.
        /// </summary>
        [JsonPropertyName("occurredAt")]
        public DateTime OccurredAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Optional user profile data snapshot. If omitted, notify-api can resolve user data by userId.
        /// </summary>
        [JsonPropertyName("user")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public InternalUserDto? User { get; set; }

        /// <summary>
        /// Optional event-specific metadata dictionary.
        /// </summary>
        [JsonPropertyName("data")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public Dictionary<string, object>? Data { get; set; }
    }
}

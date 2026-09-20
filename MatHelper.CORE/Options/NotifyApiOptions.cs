namespace MatHelper.CORE.Options
{
    /// <summary>
    /// Configuration options for communicating with gmhelper-notify-api.
    /// </summary>
    public class NotifyApiOptions
    {
        /// <summary>
        /// Base URL of the gmhelper-notify-api service (e.g. "http://localhost:8080").
        /// </summary>
        public string BaseUrl { get; set; } = "http://localhost:8080";

        /// <summary>
        /// Timeout in seconds for HTTP requests to notify-api.
        /// </summary>
        public int TimeoutSeconds { get; set; } = 10;

        /// <summary>
        /// Maximum number of retry attempts for transient failures (HTTP 5xx, network errors, timeouts).
        /// Default is 2 attempts. 4xx client errors are never retried.
        /// </summary>
        public int MaxRetries { get; set; } = 2;

        /// <summary>
        /// Base delay in milliseconds between retry attempts.
        /// </summary>
        public int RetryDelayMilliseconds { get; set; } = 200;
    }
}

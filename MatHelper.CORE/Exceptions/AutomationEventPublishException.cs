using System.Net;

namespace MatHelper.CORE.Exceptions
{
    /// <summary>
    /// Exception thrown when publishing an automation event to gmhelper-notify-api fails.
    /// </summary>
    public class AutomationEventPublishException : Exception
    {
        /// <summary>
        /// HTTP status code returned by the remote service, if an HTTP response was received.
        /// </summary>
        public HttpStatusCode? StatusCode { get; }

        /// <summary>
        /// Indicates whether the failure was deemed transient (e.g. 5xx, timeout, network error).
        /// </summary>
        public bool IsTransient { get; }

        public AutomationEventPublishException(
            string message,
            HttpStatusCode? statusCode = null,
            bool isTransient = false,
            Exception? innerException = null)
            : base(message, innerException)
        {
            StatusCode = statusCode;
            IsTransient = isTransient;
        }
    }
}

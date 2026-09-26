using System.Net;

namespace MatHelper.CORE.Exceptions
{
    public class NotifyApiException : Exception
    {
        public HttpStatusCode? StatusCode { get; }

        public NotifyApiException(string message, HttpStatusCode? statusCode = null, Exception? innerException = null)
            : base(message, innerException)
        {
            StatusCode = statusCode;
        }
    }
}

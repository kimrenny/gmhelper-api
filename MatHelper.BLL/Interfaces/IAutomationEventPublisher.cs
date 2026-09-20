using MatHelper.CORE.Models;

namespace MatHelper.BLL.Interfaces
{
    /// <summary>
    /// Service abstraction for publishing automation domain events to gmhelper-notify-api.
    /// </summary>
    public interface IAutomationEventPublisher
    {
        /// <summary>
        /// Publishes an automation domain event to the internal notify-api ingestion endpoint.
        /// </summary>
        /// <param name="automationEvent">The event payload to publish.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        Task PublishAsync(AutomationEvent automationEvent, CancellationToken cancellationToken = default);
    }
}

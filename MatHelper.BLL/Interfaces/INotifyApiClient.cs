using MatHelper.CORE.Models;

namespace MatHelper.BLL.Interfaces
{
    public interface INotifyApiClient
    {
        Task SendNotificationAsync(SendNotificationRequest request, CancellationToken cancellationToken = default);
    }
}

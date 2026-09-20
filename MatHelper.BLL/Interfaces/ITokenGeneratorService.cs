using MatHelper.CORE.Models;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace MatHelper.BLL.Interfaces
{
    public interface ITokenGeneratorService
    {
        string GenerateJwtToken(User user, DeviceInfo deviceInfo);
        string GenerateRefreshToken();
        string GenerateServiceToken(string serviceName = "gmhelper-api");
    }
}
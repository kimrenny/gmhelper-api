using MatHelper.BLL.Middlewares;
using System.Text;
using Microsoft.AspNetCore.Http;
using Moq;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace MatHelper.Tests
{
    public class DuplicateRequestMiddlewareTests
    {
        [Fact]
        public async Task InvokeAsync_ShouldBlock_DuplicateRequest()
        {
            var mockClientInfo = new Mock<MatHelper.BLL.Interfaces.IClientInfoService>();
            mockClientInfo.Setup(c => c.GetClientIp(It.IsAny<HttpContext>())).Returns("127.0.0.1");
            mockClientInfo.Setup(c => c.GetDeviceInfo(It.IsAny<HttpContext>())).Returns(new MatHelper.CORE.Models.DeviceInfo { UserAgent = "ua", Platform = "plat" });

            var nextCalled = false;
            RequestDelegate next = (ctx) => { nextCalled = true; return Task.CompletedTask; };

            var middleware = new DuplicateRequestMiddleware(next);

            var body = "{\"a\":1}";

            var ctx1 = new DefaultHttpContext();
            ctx1.Request.Method = "POST";
            ctx1.Request.Path = "/api/test";
            var ms1 = new MemoryStream(Encoding.UTF8.GetBytes(body));
            ctx1.Request.Body = ms1;
            ctx1.Request.ContentLength = ms1.Length;

            ctx1.Response.Body = new MemoryStream();

            await middleware.InvokeAsync(ctx1, mockClientInfo.Object);

            Assert.True(nextCalled);

            // second request with same payload should be blocked
            var ctx2 = new DefaultHttpContext();
            ctx2.Request.Method = "POST";
            ctx2.Request.Path = "/api/test";
            var ms2 = new MemoryStream(Encoding.UTF8.GetBytes(body));
            ctx2.Request.Body = ms2;
            ctx2.Request.ContentLength = ms2.Length;
            ctx2.Response.Body = new MemoryStream();

            await middleware.InvokeAsync(ctx2, mockClientInfo.Object);

            Assert.Equal(StatusCodes.Status429TooManyRequests, ctx2.Response.StatusCode);

            ctx2.Response.Body.Seek(0, SeekOrigin.Begin);
            using var reader = new StreamReader(ctx2.Response.Body);
            var text = await reader.ReadToEndAsync();

            Assert.Contains("Duplicate request detected", text);
        }
    }
}

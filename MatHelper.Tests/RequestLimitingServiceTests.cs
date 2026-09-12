using MatHelper.BLL.Services;
using MatHelper.CORE.Options;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using System.Net;
using Xunit;

namespace MatHelper.Tests
{
    public class RequestLimitingServiceTests
    {
        [Fact]
        public void TryAcquire_ShouldEnforce_IpLimit()
        {
            var opts = Options.Create(new RequestLimitingOptions
            {
                Default = new RequestLimitRule
                {
                    MaxConcurrentPerIp = 1,
                    MaxConcurrentEndpoint = 10,
                    MaxConcurrentApplication = 100
                }
            });

            var service = new RequestLimitingService(opts);

            var ctx1 = new DefaultHttpContext();
            ctx1.Request.Method = "GET";
            ctx1.Request.Path = "/api/test";
            ctx1.Connection.RemoteIpAddress = IPAddress.Parse("127.0.0.1");

            Assert.True(service.TryAcquire(ctx1, out var _));

            var ctx2 = new DefaultHttpContext();
            ctx2.Request.Method = "GET";
            ctx2.Request.Path = "/api/test";
            ctx2.Connection.RemoteIpAddress = IPAddress.Parse("127.0.0.1");

            Assert.False(service.TryAcquire(ctx2, out var err));
            Assert.Contains("Too many concurrent", err);

            // release and ensure second can acquire
            service.Release(ctx1);
            Assert.True(service.TryAcquire(ctx2, out var _));
            service.Release(ctx2);
        }

        [Fact]
        public void TryAcquire_ShouldEnforce_EndpointLimit()
        {
            var opts = Options.Create(new RequestLimitingOptions
            {
                Default = new RequestLimitRule
                {
                    MaxConcurrentPerIp = 10,
                    MaxConcurrentEndpoint = 1,
                    MaxConcurrentApplication = 100
                }
            });

            var service = new RequestLimitingService(opts);

            var ctx1 = new DefaultHttpContext();
            ctx1.Request.Method = "GET";
            ctx1.Request.Path = "/api/test";
            ctx1.Connection.RemoteIpAddress = IPAddress.Parse("127.0.0.1");

            var ctx2 = new DefaultHttpContext();
            ctx2.Request.Method = "GET";
            ctx2.Request.Path = "/api/test";
            ctx2.Connection.RemoteIpAddress = IPAddress.Parse("127.0.0.2");

            Assert.True(service.TryAcquire(ctx1, out var _));
            Assert.False(service.TryAcquire(ctx2, out var err));
            Assert.Contains("This endpoint is currently overloaded", err);

            service.Release(ctx1);
        }
    }
}

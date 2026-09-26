using MatHelper.BLL.Interfaces;
using MatHelper.BLL.Middlewares;
using MatHelper.CORE.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Moq;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace MatHelper.Tests
{
    public class DuplicateRequestMiddlewareTests
    {
        private Mock<IClientInfoService> CreateMockClientInfo(string ip = "127.0.0.1", string userAgent = "ua", string platform = "plat")
        {
            var mock = new Mock<IClientInfoService>();
            mock.Setup(c => c.GetClientIp(It.IsAny<HttpContext>())).Returns(ip);
            mock.Setup(c => c.GetDeviceInfo(It.IsAny<HttpContext>())).Returns(new DeviceInfo { UserAgent = userAgent, Platform = platform });
            return mock;
        }

        private HttpContext CreateHttpContext(string method = "POST", string path = "/api/test", string? body = "{\"a\":1}")
        {
            var ctx = new DefaultHttpContext();
            ctx.Request.Method = method;
            ctx.Request.Path = path;
            if (body != null)
            {
                var bytes = Encoding.UTF8.GetBytes(body);
                var ms = new MemoryStream(bytes);
                ctx.Request.Body = ms;
                ctx.Request.ContentLength = bytes.Length;
            }
            ctx.Response.Body = new MemoryStream();
            return ctx;
        }

        [Fact]
        public async Task InvokeAsync_FirstRequest_IsAccepted()
        {
            var mockClientInfo = CreateMockClientInfo();
            var nextCalled = false;
            RequestDelegate next = (ctx) => { nextCalled = true; return Task.CompletedTask; };

            var memoryCache = new MemoryCache(new MemoryCacheOptions());
            var middleware = new DuplicateRequestMiddleware(next, memoryCache);

            var ctx = CreateHttpContext();
            await middleware.InvokeAsync(ctx, mockClientInfo.Object);

            Assert.True(nextCalled);
            Assert.NotEqual(StatusCodes.Status429TooManyRequests, ctx.Response.StatusCode);
        }

        [Fact]
        public async Task InvokeAsync_IdenticalRequestWithinWindow_IsRejectedWith429()
        {
            var mockClientInfo = CreateMockClientInfo();
            var nextCallCount = 0;
            RequestDelegate next = (ctx) => { Interlocked.Increment(ref nextCallCount); return Task.CompletedTask; };

            var memoryCache = new MemoryCache(new MemoryCacheOptions());
            var middleware = new DuplicateRequestMiddleware(next, memoryCache);

            var ctx1 = CreateHttpContext();
            await middleware.InvokeAsync(ctx1, mockClientInfo.Object);

            Assert.Equal(1, nextCallCount);

            // Second identical request within window
            var ctx2 = CreateHttpContext();
            await middleware.InvokeAsync(ctx2, mockClientInfo.Object);

            Assert.Equal(1, nextCallCount);
            Assert.Equal(StatusCodes.Status429TooManyRequests, ctx2.Response.StatusCode);

            ctx2.Response.Body.Seek(0, SeekOrigin.Begin);
            using var reader = new StreamReader(ctx2.Response.Body);
            var text = await reader.ReadToEndAsync();
            Assert.Contains("Duplicate request detected. Please wait before retrying.", text);
        }

        [Fact]
        public async Task InvokeAsync_DifferentRequestKeys_AreBothAccepted()
        {
            var mockClientInfo = CreateMockClientInfo();
            var nextCallCount = 0;
            RequestDelegate next = (ctx) => { Interlocked.Increment(ref nextCallCount); return Task.CompletedTask; };

            var memoryCache = new MemoryCache(new MemoryCacheOptions());
            var middleware = new DuplicateRequestMiddleware(next, memoryCache);

            // Different paths
            var ctx1 = CreateHttpContext(method: "POST", path: "/api/test1", body: "{\"a\":1}");
            var ctx2 = CreateHttpContext(method: "POST", path: "/api/test2", body: "{\"a\":1}");

            await middleware.InvokeAsync(ctx1, mockClientInfo.Object);
            await middleware.InvokeAsync(ctx2, mockClientInfo.Object);

            // Different methods
            var ctx3 = CreateHttpContext(method: "GET", path: "/api/test1", body: null);
            await middleware.InvokeAsync(ctx3, mockClientInfo.Object);

            // Different body
            var ctx4 = CreateHttpContext(method: "POST", path: "/api/test1", body: "{\"a\":2}");
            await middleware.InvokeAsync(ctx4, mockClientInfo.Object);

            // Different IP
            var diffClientInfo = CreateMockClientInfo(ip: "192.168.1.1");
            var ctx5 = CreateHttpContext(method: "POST", path: "/api/test1", body: "{\"a\":1}");
            await middleware.InvokeAsync(ctx5, diffClientInfo.Object);

            Assert.Equal(5, nextCallCount);
        }

        [Fact]
        public async Task InvokeAsync_SameKey_IsAcceptedAfterExpiration()
        {
            var mockClientInfo = CreateMockClientInfo();
            var nextCallCount = 0;
            RequestDelegate next = (ctx) => { Interlocked.Increment(ref nextCallCount); return Task.CompletedTask; };

            var memoryCache = new MemoryCache(new MemoryCacheOptions());
            var middleware = new DuplicateRequestMiddleware(next, memoryCache);

            // Using /logs which has a 100ms threshold
            var ctx1 = CreateHttpContext(method: "POST", path: "/api/logs/event", body: "{\"log\":1}");
            await middleware.InvokeAsync(ctx1, mockClientInfo.Object);
            Assert.Equal(1, nextCallCount);

            // Wait past the 100ms threshold
            await Task.Delay(150);

            var ctx2 = CreateHttpContext(method: "POST", path: "/api/logs/event", body: "{\"log\":1}");
            await middleware.InvokeAsync(ctx2, mockClientInfo.Object);

            Assert.Equal(2, nextCallCount);
            Assert.NotEqual(StatusCodes.Status429TooManyRequests, ctx2.Response.StatusCode);
        }

        [Fact]
        public async Task InvokeAsync_MultipleConcurrentIdenticalRequests_OnlyOnePassesThrough()
        {
            var mockClientInfo = CreateMockClientInfo();
            var nextCallCount = 0;
            RequestDelegate next = async (ctx) =>
            {
                Interlocked.Increment(ref nextCallCount);
                await Task.Delay(10); // simulate some work
            };

            var memoryCache = new MemoryCache(new MemoryCacheOptions());
            var middleware = new DuplicateRequestMiddleware(next, memoryCache);

            const int concurrentRequests = 10;
            var contexts = new HttpContext[concurrentRequests];
            for (int i = 0; i < concurrentRequests; i++)
            {
                contexts[i] = CreateHttpContext(method: "POST", path: "/api/concurrent-test", body: "{\"test\":\"concurrent\"}");
            }

            var tasks = contexts.Select(ctx => middleware.InvokeAsync(ctx, mockClientInfo.Object)).ToArray();
            await Task.WhenAll(tasks);

            // Exactly 1 should have invoked next
            Assert.Equal(1, nextCallCount);

            var passedCount = contexts.Count(c => c.Response.StatusCode != StatusCodes.Status429TooManyRequests);
            var rejectedCount = contexts.Count(c => c.Response.StatusCode == StatusCodes.Status429TooManyRequests);

            Assert.Equal(1, passedCount);
            Assert.Equal(concurrentRequests - 1, rejectedCount);
        }

        [Fact]
        public async Task InvokeAsync_LargeNumberOfUniqueKeys_AllAcceptedWithoutLeak()
        {
            var mockClientInfo = CreateMockClientInfo();
            var nextCallCount = 0;
            RequestDelegate next = (ctx) => { Interlocked.Increment(ref nextCallCount); return Task.CompletedTask; };

            var memoryCache = new MemoryCache(new MemoryCacheOptions());
            var middleware = new DuplicateRequestMiddleware(next, memoryCache);

            const int totalRequests = 1000;
            for (int i = 0; i < totalRequests; i++)
            {
                var ctx = CreateHttpContext(method: "POST", path: $"/api/item/{i}", body: $"{{\"id\":{i}}}");
                await middleware.InvokeAsync(ctx, mockClientInfo.Object);
            }

            Assert.Equal(totalRequests, nextCallCount);
        }

        [Fact]
        public async Task InvokeAsync_DefaultConstructor_ResolvesCacheAutomatically()
        {
            var mockClientInfo = CreateMockClientInfo();
            var nextCalled = false;
            RequestDelegate next = (ctx) => { nextCalled = true; return Task.CompletedTask; };

            // Default constructor without explicit IMemoryCache
            var middleware = new DuplicateRequestMiddleware(next);

            var ctx = CreateHttpContext();
            await middleware.InvokeAsync(ctx, mockClientInfo.Object);

            Assert.True(nextCalled);
        }
    }
}

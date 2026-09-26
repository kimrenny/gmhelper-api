using DotNetEnv;
using MatHelper.BLL.Filters;
using MatHelper.BLL.Interfaces;
using MatHelper.BLL.Mappers;
using MatHelper.BLL.Middlewares;
using MatHelper.BLL.Services;
using MatHelper.CORE.Options;
using MatHelper.DAL.Database;
using MatHelper.DAL.Interfaces;
using MatHelper.DAL.Repositories;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.InMemory;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using StackExchange.Redis;
using SolutionHub;
using System.Net;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenAnyIP(7057);
});

Env.Load("../.env");

builder.Configuration.AddJsonFile(
    "Configuration/security.json",
    optional: false,
    reloadOnChange: false);

builder.Services.AddHttpContextAccessor();
builder.Services.AddMemoryCache();
builder.Services.AddControllers(options =>
{
    options.Filters.Add<RequestLoggingFilter>();
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var corsOrigins = Environment.GetEnvironmentVariable("CORS_ORIGINS")?
    .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
    ?? throw new InvalidOperationException("CORS_ORIGINS is not configured.");

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy
              .WithOrigins(corsOrigins)
              .AllowAnyMethod()
              .AllowAnyHeader()
              .AllowCredentials();
    });
});

builder.Services.Configure<DbOptions>(
    builder.Configuration.GetSection("DbOptions"));

builder.Services.Configure<RequestLimitingOptions>(
    builder.Configuration.GetSection("RequestLimiting"));

builder.Services.AddDbContext<AppDbContext>((provider, ctx) =>
{
    var options = provider.GetRequiredService<IOptions<DbOptions>>().Value;

    var isTest = builder.Environment.IsEnvironment("IntegrationTest");
    var isDev = builder.Environment.IsDevelopment();

    if (isTest)
    {
        ctx.UseInMemoryDatabase("TestDb");
        return;
    }

    var connectionString =
        isDev
            ? Environment.GetEnvironmentVariable("ConnectionStrings__DevConnection")
            : Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection");

    ctx.UseNpgsql(connectionString, npgsqlOptions =>
    {
        npgsqlOptions.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery);
    });
});

var secretKey = Environment.GetEnvironmentVariable("JWT_SECRET_KEY") ?? builder.Configuration["Jwt:SecretKey"];
var issuer = Environment.GetEnvironmentVariable("JWT_ISSUER") ?? builder.Configuration["Jwt:Issuer"];
var audience = Environment.GetEnvironmentVariable("JWT_AUDIENCE") ?? builder.Configuration["Jwt:Audience"];

if (string.IsNullOrWhiteSpace(secretKey) || string.IsNullOrWhiteSpace(issuer) || string.IsNullOrWhiteSpace(audience))
{
    throw new InvalidOperationException("JWT options are not configured properly.");
}

var jwtOptions = new JwtOptions
{
    SecretKey = secretKey!,
    Issuer = issuer!,
    Audience = audience!
};

builder.Services.AddStackExchangeRedisCache(options =>
{
    var isTestEnv = builder.Environment.IsEnvironment("IntegrationTest");
    var isDev = builder.Environment.IsDevelopment();

    var redisConnection = builder.Configuration.GetConnectionString("Redis")
    ?? (isDev || isTestEnv
        ? "localhost:6379"
        : throw new InvalidOperationException("Redis connection string is required in production."));

    var config = ConfigurationOptions.Parse(redisConnection);

    if (!isDev && !isTestEnv && string.IsNullOrEmpty(config.Password))
    {
        throw new InvalidOperationException("Redis password is required outside Development.");
    }

    config.AbortOnConnectFail = true;
    config.ConnectTimeout = 500;
    config.SyncTimeout = 500;
    config.ConnectRetry = 0;

    options.ConfigurationOptions = config;
});

builder.Services.AddSingleton(jwtOptions);
builder.Services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();
builder.Services.AddSingleton<IRequestLimitingService, RequestLimitingService>();

builder.Services.AddScoped<IAuthenticationService, AuthenticationService>();
builder.Services.AddScoped<IRegistrationService, RegistrationService>();
builder.Services.AddScoped<ISecurityPolicyService, SecurityPolicyService>();
builder.Services.AddScoped<ILoginService, LoginService>();
builder.Services.AddScoped<IEmailAuthService, EmailAuthService>();
builder.Services.AddScoped<IRecoveryService, RecoveryService>();
builder.Services.AddScoped<ITwoFactorAuthService, TwoFactorAuthService>();
builder.Services.AddScoped<ISecurityService, SecurityService>();
builder.Services.AddScoped<ITokenService, TokenService>();
builder.Services.AddScoped<ITokenGeneratorService,  TokenGeneratorService>();
builder.Services.AddScoped<IAdminService, AdminService>();
builder.Services.AddScoped<IAnalyticsService, AnalyticsService>();
builder.Services.AddScoped<IUserAdminService, UserAdminService>();
builder.Services.AddScoped<ITokenAdminService, TokenAdminService>();
builder.Services.AddScoped<IOwnerService, OwnerService>();
builder.Services.AddScoped<IUserManagementService, UserManagementService>();
builder.Services.AddScoped<IDeviceManagementService, DeviceManagementService>();
builder.Services.AddScoped<IRequestLogService, RequestLogService>();
builder.Services.AddScoped<IAdminSettingsService, AdminSettingsService>();
builder.Services.AddScoped<IMailService, MailService>();
builder.Services.AddScoped<IGeoTaskProcessingService, GeoTaskProcessingService>();
builder.Services.AddScoped<IMathTaskProcessingService, MathTaskProcessingService>();
builder.Services.AddScoped<IClientInfoService, ClientInfoService>();
builder.Services.AddScoped<ITwoFactorService, TwoFactorService>();
builder.Services.AddScoped<ILoginAttemptService, LoginAttemptService>();
builder.Services.AddScoped<IReportService, ReportService>();
builder.Services.AddScoped<IUserMapper, UserMapper>();
builder.Services.AddScoped<ICaptchaValidationService, CaptchaValidationService>();
builder.Services.AddScoped<IErrorLogRepository, ErrorLogRepository>();
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IEmailLoginCodeRepository, EmailLoginCodeRepository>();
builder.Services.AddScoped<ILoginTokenRepository, LoginTokenRepository>();
builder.Services.AddScoped<IPasswordRecoveryRepository, PasswordRecoveryRepository>();
builder.Services.AddScoped<IRequestLogRepository, RequestLogRepository>();
builder.Services.AddScoped<IAuthLogRepository, AuthLogRepository>();
builder.Services.AddScoped<IAdminSettingsRepository, AdminSettingsRepository>();
builder.Services.AddScoped<ITaskRequestRepository, TaskRequestRepository>();
builder.Services.AddScoped<ITaskRatingRepository, TaskRatingRepository>();
builder.Services.AddScoped<ITwoFactorRepository, TwoFactorRepository>();
builder.Services.AddScoped<IAppTwoFactorSessionRepository, AppTwoFactorSessionRepository>();
builder.Services.AddScoped<IIpLoginAttemptRepository, IpLoginAttemptRepository>();
builder.Services.AddScoped<INotFoundReportRepository, NotFoundReportRepository>();
builder.Services.AddScoped<ICacheService, CacheService>();

builder.Services.AddScoped<ErrorLoggingMiddleware>();

builder.Services.Configure<NotifyApiOptions>(options =>
{
    builder.Configuration.GetSection("NotifyApi").Bind(options);
    var envBaseUrl = Environment.GetEnvironmentVariable("NOTIFY_API_BASE_URL");
    if (!string.IsNullOrWhiteSpace(envBaseUrl))
    {
        options.BaseUrl = envBaseUrl;
    }
});

builder.Services.AddHttpClient<IAutomationEventPublisher, AutomationEventPublisher>((provider, client) =>
{
    var options = provider.GetRequiredService<IOptions<NotifyApiOptions>>().Value;
    var baseUrl = (string.IsNullOrWhiteSpace(options.BaseUrl) ? "http://localhost:8080" : options.BaseUrl).TrimEnd('/') + "/";
    client.BaseAddress = new Uri(baseUrl);
    client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds > 0 ? options.TimeoutSeconds : 10);
});

builder.Services.AddHttpClient<INotifyApiClient, NotifyApiClient>((provider, client) =>
{
    var options = provider.GetRequiredService<IOptions<NotifyApiOptions>>().Value;
    var baseUrl = (string.IsNullOrWhiteSpace(options.BaseUrl) ? "http://localhost:8080" : options.BaseUrl).TrimEnd('/') + "/";
    client.BaseAddress = new Uri(baseUrl);
    client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds > 0 ? options.TimeoutSeconds : 10);
});

builder.Services.AddGrpcClient<SolutionHubService.SolutionHubServiceClient>(options =>
{
    options.Address = new Uri(Environment.GetEnvironmentVariable("SOLUTION_HUB_URL") ?? "http://solution-hub:50051");
});


builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ClockSkew = TimeSpan.Zero,
            ValidIssuer = jwtOptions.Issuer,
            ValidAudience = jwtOptions.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Convert.FromBase64String(jwtOptions.SecretKey))
        };
    });

var app = builder.Build();

if (!app.Environment.IsEnvironment("IntegrationTest"))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
}

app.UseCors("AllowFrontend");

app.Use(async (context, next) =>
{
    if (context.Request.Method == "OPTIONS")
    {
        context.Response.StatusCode = 200;
        await context.Response.CompleteAsync();
        return;
    }
    await next();
});

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHsts();

var forwardedHeadersOptions = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
};
forwardedHeadersOptions.KnownProxies.Clear();
forwardedHeadersOptions.KnownNetworks.Clear();
foreach (var proxyIp in (Environment.GetEnvironmentVariable("TRUSTED_PROXIES") ?? "")
             .Split(';', StringSplitOptions.RemoveEmptyEntries))
{
    if (IPAddress.TryParse(proxyIp, out var ip))
        forwardedHeadersOptions.KnownProxies.Add(ip);
}

app.UseForwardedHeaders(forwardedHeadersOptions);

app.UseRouting();

app.UseMiddleware<RequestLimitingMiddleware>();
app.UseMiddleware<ErrorLoggingMiddleware>();

app.Use(async (context, next) =>
{
    context.Request.EnableBuffering();
    var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();

    var remoteIp = context.Connection.RemoteIpAddress?.ToString();

    var sensitiveHeaders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    { "Authorization", "Cookie", "Set-Cookie", "X-Api-Key" };

    var headers = context.Request.Headers
        .Select(h => sensitiveHeaders.Contains(h.Key)
            ? $"{h.Key}: [REDACTED]"
            : $"{h.Key}: {h.Value}")
        .ToList();

    context.Response.Headers["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains; preload";
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    context.Response.Headers["Permissions-Policy"] = "geolocation=(), microphone=(), camera=()";
    context.Response.Headers["Content-Security-Policy"] = "default-src 'self'; script-src 'self' https://www.google.com https://www.gstatic.com; style-src 'self'; img-src 'self'; font-src 'self'; object-src 'none'; frame-ancestors 'self' https://www.google.com https://www.gstatic.com; base-uri 'self'; form-action 'self'";
    
    var startTime = DateTime.UtcNow;
    logger.LogInformation($"[{DateTime.UtcNow}] Handling request: {context.Request.Method} {context.Request.Path}");
    
    await next();

    var elapsedTime = DateTime.UtcNow - startTime;
    logger.LogInformation($"[{DateTime.UtcNow}] Response status: {context.Response.StatusCode}. Time taken: {elapsedTime}. [{context.Request.Method} {context.Request.Path}]");
});

app.UseDuplicateRequestMiddleware();

app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();

public partial class Program { }
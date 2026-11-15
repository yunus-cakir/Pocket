using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using Pocket.Shared.DTOs;
using Pocket.Shared.Interfaces;
using Microsoft.Extensions.Hosting.WindowsServices;
using Microsoft.IdentityModel.Tokens;
using Pocket.Server.Hubs;
using Pocket.Server.Services;

// As a Windows Service the working dir is System32, so the content root (where
// appsettings*.json are read from) must be set to the app folder up front —
// WebApplicationBuilder doesn't allow changing it later.
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = WindowsServiceHelpers.IsWindowsService() ? AppContext.BaseDirectory : default
});

// Runs as a Windows Service when started by the SCM (no-op under `dotnet run`).
builder.Host.UseWindowsService(options => options.ServiceName = "PocketServer");

// Listen address comes from config ("Urls" in appsettings*.json, ASPNETCORE_URLS or --urls).
// Dev default listens on all interfaces for LAN/emulator testing; the installed service
// overrides it to loopback-only so it is reachable from outside only via the tunnel/funnel.
if (string.IsNullOrWhiteSpace(builder.Configuration["Urls"]))
{
    builder.WebHost.UseUrls("http://0.0.0.0:5200");
}

builder.Services.AddSignalR(options =>
{
    options.MaximumReceiveMessageSize = 15 * 1024 * 1024; // 15 MB for E2EE photo/video payloads
});
builder.Services.AddSingleton<TransientMemoryStore>();

// Primary/standby cluster (see deploy/README.md). Registered after the web host so the
// handoff runs before Kestrel stops on shutdown.
builder.Services.Configure<ClusterOptions>(builder.Configuration.GetSection("Cluster"));
builder.Services.AddSingleton<ClusterState>();
builder.Services.AddHttpClient("handoff", c => c.Timeout = TimeSpan.FromSeconds(60));
builder.Services.AddHostedService<HandoffService>();
builder.Services.Configure<HostOptions>(o => o.ShutdownTimeout = TimeSpan.FromMinutes(2));

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                var path = context.HttpContext.Request.Path;
                
                // SignalR sends the token in the query string for WebSockets
                if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs/relay"))
                {
                    context.Token = accessToken;
                }
                return Task.CompletedTask;
            }
        };

        options.TokenValidationParameters = new TokenValidationParameters
        {
            // WARNING: Configure these securely using builder.Configuration in production!
            ValidateIssuer = false, 
            ValidateAudience = false,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = false,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("SuperSecretDummyKeyForDevelopmentPurposesOnly!!!"))
        };
    });
builder.Services.AddAuthorization();

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyHeader()
              .AllowAnyMethod()
              .SetIsOriginAllowed(_ => true)
              .AllowCredentials();
    });
});

var app = builder.Build();

app.UseCors();

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/", () => "📸 Pocket Transient Relay Server is running.");
app.MapHub<RelayHub>("/hubs/relay");

// Liveness + identity, used by clients (failover/failback) and the standby watchdog.
app.MapGet("/health", (IOptions<ClusterOptions> options, ClusterState state) =>
    state.Draining
        ? Results.StatusCode(StatusCodes.Status503ServiceUnavailable)
        : Results.Ok(new
        {
            node = options.Value.NodeName,
            role = options.Value.Role,
            startedAt = state.StartedAtUtc,
            connections = state.ConnectionCount
        }));

// Receives a peer's undelivered payloads on its shutdown (memory to memory, BR-201).
app.MapPost("/internal/handoff", async (HttpContext http, EncryptedPayloadDto payload,
        IOptions<ClusterOptions> options, TransientMemoryStore store,
        IHubContext<RelayHub, IRelayClient> hub) =>
    {
        var secret = options.Value.Secret;
        var provided = http.Request.Headers[HandoffService.SecretHeader].ToString();
        if (string.IsNullOrEmpty(secret) ||
            !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(provided), Encoding.UTF8.GetBytes(secret)))
        {
            return Results.Unauthorized();
        }

        store.EnqueuePayload(payload); // keyed by MessageId, so retries are idempotent
        foreach (var connectionId in RelayHub.GetConnectionIds(payload.RecipientId))
        {
            await hub.Clients.Client(connectionId).ReceivePayload(payload);
        }
        return Results.Ok();
    })
    .WithMetadata(new RequestSizeLimitAttribute(64 * 1024 * 1024));

app.Run();

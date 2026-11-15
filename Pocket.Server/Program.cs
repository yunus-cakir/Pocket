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
builder.Services.AddSingleton<IdentitySync>();
builder.Services.AddHostedService<HandoffService>();
builder.Services.Configure<HostOptions>(o => o.ShutdownTimeout = TimeSpan.FromMinutes(2));

// Auth: clients prove possession of their identity key (/auth/challenge + /auth/token) and get a
// JWT signed with a random per-process key — nothing secret ships in the client anymore.
var jwtSigningKey = AuthService.CreateSigningKey();
builder.Services.AddSingleton(jwtSigningKey);
builder.Services.AddSingleton<IdentityRegistry>();
builder.Services.AddSingleton<AuthService>();

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
            ValidateIssuer = true,
            ValidIssuer = AuthService.Issuer,
            ValidateAudience = true,
            ValidAudience = AuthService.Audience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = jwtSigningKey,
            ClockSkew = TimeSpan.FromMinutes(1)
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

// Login step 1: one-time nonce to sign.
app.MapGet("/auth/challenge", (AuthService auth) => Results.Ok(new { nonce = auth.IssueNonce() }));

// Login step 2: signature over "pocket-auth-v1|{userId}|{nonce}" with the identity key → JWT.
app.MapPost("/auth/token", (TokenRequest request, AuthService auth) =>
{
    var (outcome, token, expiresAt) = auth.IssueToken(
        request.UserId, request.Username, request.PublicKey, request.Nonce, request.Signature);
    return outcome switch
    {
        AuthService.Outcome.Ok => Results.Ok(new { token, expiresAt }),
        AuthService.Outcome.BadRequest => Results.BadRequest(),
        AuthService.Outcome.KeyMismatch => Results.StatusCode(StatusCodes.Status403Forbidden),
        _ => Results.Unauthorized()
    };
});

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

// Peer node ⇄ this node: both sides merge the other's identity registry (public keys and
// usernames only). Users whose entry changed are reconnected so they log in/register again.
app.MapPost("/internal/identities/sync", (HttpContext http, Dictionary<string, IdentityRegistry.Entry> incoming,
        IOptions<ClusterOptions> options, IdentityRegistry registry, ClusterState state) =>
    {
        if (!IsPeer(http, options.Value)) return Results.Unauthorized();

        var result = registry.Merge(incoming);
        var affected = result.KeyReplaced.Concat(result.UsernameChanged).Distinct().ToArray();
        if (affected.Length > 0)
        {
            RelayHub.ForgetUsernames(affected);
            foreach (var userId in affected) state.AbortUser(userId);
        }
        if (result.Changed)
        {
            app.Logger.LogInformation("Identity sync from peer: {Added} added, {Updated} updated, {Total} total.",
                result.Added, result.Updated, registry.Count);
        }
        return Results.Ok(registry.Snapshot());
    })
    .WithMetadata(new RequestSizeLimitAttribute(8 * 1024 * 1024));

// Receives a peer's undelivered payloads on its shutdown (memory to memory, BR-201).
app.MapPost("/internal/handoff", async (HttpContext http, EncryptedPayloadDto payload,
        IOptions<ClusterOptions> options, TransientMemoryStore store,
        IHubContext<RelayHub, IRelayClient> hub) =>
    {
        if (!IsPeer(http, options.Value)) return Results.Unauthorized();

        store.EnqueuePayload(payload); // keyed by MessageId, so retries are idempotent
        foreach (var connectionId in RelayHub.GetConnectionIds(payload.RecipientId))
        {
            await hub.Clients.Client(connectionId).ReceivePayload(payload);
        }
        return Results.Ok();
    })
    .WithMetadata(new RequestSizeLimitAttribute(64 * 1024 * 1024));

// Catch up with the peer's registry before accepting logins, so a UserId the peer already
// bound can't be claimed here first. The peer is usually down; that only costs the timeout.
await app.Services.GetRequiredService<IdentitySync>().SyncWithPeerAsync(TimeSpan.FromSeconds(5));

app.Run();

static bool IsPeer(HttpContext http, ClusterOptions options)
{
    var secret = options.Secret;
    var provided = http.Request.Headers[HandoffService.SecretHeader].ToString();
    return !string.IsNullOrEmpty(secret) &&
           CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(provided), Encoding.UTF8.GetBytes(secret));
}

/// <summary>Body of POST /auth/token.</summary>
record TokenRequest(string UserId, string Username, string PublicKey, string Nonce, string Signature);

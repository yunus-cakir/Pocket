using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
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
// overrides it to loopback-only so it is reachable from outside only via Cloudflare Tunnel.
if (string.IsNullOrWhiteSpace(builder.Configuration["Urls"]))
{
    builder.WebHost.UseUrls("http://0.0.0.0:5200");
}

builder.Services.AddSignalR(options =>
{
    options.MaximumReceiveMessageSize = 15 * 1024 * 1024; // 15 MB for E2EE photo/video payloads
});
builder.Services.AddSingleton<TransientMemoryStore>();

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

app.Run();

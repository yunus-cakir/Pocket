using Pocket.Server.Hubs;
using Pocket.Server.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSignalR();
builder.Services.AddSingleton<TransientMemoryStore>();

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

app.MapGet("/", () => "📸 Pocket Transient Relay Server is running.");
app.MapHub<RelayHub>("/hubs/relay");

app.Run();

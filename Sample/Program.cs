using Microsoft.AspNetCore.Http.Connections;
using Sample;

var builder = WebApplication.CreateBuilder(args);

var redisConnection = builder.Configuration.GetConnectionString("redis");

var signalr = builder.Services.AddSignalR();

if (redisConnection is not null)
{
    signalr.AddStackExchangeRedis(redisConnection);
}

builder.Services.AddSmartLoadBalancing();

var app = builder.Build();

// This id helps identify server instances
var id = Guid.NewGuid().ToString();

app.Use((context, next) =>
{
    context.Response.OnStarting(() =>
    {
        context.Response.Headers["X-Server"] = id;
        return Task.CompletedTask;
    });

    return next(context);
});

app.UseFileServer();

app.UseSmartLoadBalancing();

// Working around an issue in tye
app.MapHub<Chat>("/chat", o => o.Transports = HttpTransportType.ServerSentEvents);

app.Run();

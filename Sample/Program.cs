using Sample;

var builder = WebApplication.CreateBuilder(args);

var redisConnection = builder.Configuration.GetConnectionString("redis");

var signalr = builder.Services.AddSignalR();

if (redisConnection is not null)
{
    signalr.AddStackExchangeRedis(redisConnection);
}

var app = builder.Build();

app.UseFileServer();

app.MapHub<Chat>("/chat");

app.Run();

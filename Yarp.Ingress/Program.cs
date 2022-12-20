using Yarp.ReverseProxy.Configuration;

var builder = WebApplication.CreateBuilder(args);

var routes = new[]
{
    new RouteConfig()
    {
        RouteId = "route1",
        ClusterId = "cluster1",
        Match = new RouteMatch
        {
            Path = "{**catch-all}"
        }
    },
    new RouteConfig()
    {
        RouteId = "route2",
        ClusterId = "cluster1",
        Match = new RouteMatch
        {
            Path = "/chat/negotiate"
        },
        Metadata = new Dictionary<string, string> { ["signalr"] = "true" }
    },
};

var destinations = new Dictionary<string, DestinationConfig>(StringComparer.OrdinalIgnoreCase)
{
    ["destination0"] = new DestinationConfig() { Address = builder.Configuration.GetServiceUri("sample0")?.ToString() },
    ["destination1"] = new DestinationConfig() { Address = builder.Configuration.GetServiceUri("sample1")?.ToString() },
};

var clusters = new[]
{
    new ClusterConfig()
    {
        ClusterId = "cluster1",
        SessionAffinity = new SessionAffinityConfig()
        {
            AffinityKeyName = "Sticky",
            Policy = "SignalR",
            Enabled = true
        },
        Destinations = destinations
    }
};

builder.Services.AddReverseProxy()
    .AddSignalRSessionAffinity()
    .LoadFromMemory(routes, clusters);

var app = builder.Build();

app.MapReverseProxy();

app.Run();

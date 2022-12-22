using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.ServiceDiscovery;
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

var destinations = new Dictionary<string, DestinationConfig>(StringComparer.OrdinalIgnoreCase);

var discovery = new TyeServiceDiscovery(builder.Configuration);

foreach (var (key, address) in await discovery.GetAddresses("sample"))
{
    destinations[key] = new DestinationConfig()
    {
        Address = address.ToString(),
    };
    Console.WriteLine($"{key} => {address}");
}

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

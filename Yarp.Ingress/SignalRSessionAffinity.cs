
using System.Text.Json;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.Extensions.Caching.Memory;
using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.Model;
using Yarp.ReverseProxy.SessionAffinity;
using Yarp.ReverseProxy.Transforms;

namespace Microsoft.Extensions.DependencyInjection;

public static class SignalRSessionAffinity
{
    public static IReverseProxyBuilder AddSignalRSessionAffinity(this IReverseProxyBuilder builder)
    {
        builder.Services.AddSingleton<ISessionAffinityPolicy, SignalRAffinity>();
        builder.Services.AddMemoryCache();

        builder.AddTransforms(transforms =>
        {
            var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
            var cache = transforms.Services.GetRequiredService<IMemoryCache>();

            // REVIEW: Is this good enough for SSE and long polling?
            var cacheEntryOptions = new MemoryCacheEntryOptions()
            {
                SlidingExpiration = TimeSpan.FromMinutes(5)
            };

            transforms.AddResponseTransform(async c =>
            {
                var proxyFeature = c.HttpContext.GetReverseProxyFeature();

                // If this route is marked as a signalr route, then get the negotiate response
                // and associate the connection id with this destination. The first request can land anywhere
                // so we need to store the association between the connection id/token and the proxied
                // destination.
                if (proxyFeature.Route.Config.Metadata?.ContainsKey("signalr") is true &&
                    c.ProxyResponse is { IsSuccessStatusCode: true })
                {
                    // The response should be small so we can buffer it
                    var data = await c.ProxyResponse.Content.ReadAsByteArrayAsync(c.HttpContext.RequestAborted);
                    var negotiateResponse = JsonSerializer.Deserialize<NegotiationResponse>(data, jsonOptions);

                    // Restore the content so it can be read again
                    c.ProxyResponse.Content = new ByteArrayContent(data);

                    if (negotiateResponse?.ConnectionToken is string key &&
                        proxyFeature.ProxiedDestination is { } destination)
                    {
                        // Store the association between the destination and the connection token
                        cache.Set(key, destination.DestinationId, cacheEntryOptions);
                    }
                }
            });
        });

        return builder;
    }

    private sealed class SignalRAffinity : ISessionAffinityPolicy
    {
        private readonly IMemoryCache _cache;

        public SignalRAffinity(IMemoryCache cache)
        {
            _cache = cache;
        }

        public string Name => "SignalR";

        public void AffinitizeResponse(HttpContext context, ClusterState cluster, SessionAffinityConfig config, DestinationState destination)
        {
            // Nothing is written to the response
        }

        public AffinityResult FindAffinitizedDestinations(HttpContext context, ClusterState cluster, SessionAffinityConfig config, IReadOnlyList<DestinationState> destinations)
        {
            string? token = context.Request.Query["id"];
            if (token is not null && _cache.TryGetValue<string>(token, out var destinationId))
            {
                foreach (var d in destinations)
                {
                    if (d.DestinationId == destinationId)
                    {
                        return new(d, AffinityStatus.OK);
                    }
                }

                // Remove the assocition if we can't find the destination
                _cache.Remove(token);

                return new(null, AffinityStatus.DestinationNotFound);
            }

            return new(null, AffinityStatus.AffinityKeyNotSet);
        }
    }
}
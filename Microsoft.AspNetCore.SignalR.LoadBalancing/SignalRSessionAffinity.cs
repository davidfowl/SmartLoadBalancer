using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.Extensions.Primitives;
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

        var hashes = new DestinationHashes();
        builder.Services.AddSingleton(hashes);

        builder.AddTransforms(transforms =>
        {
            transforms.AddResponseTransform(async c =>
            {
                var proxyFeature = c.HttpContext.GetReverseProxyFeature();

                // If this route is marked as a signalr route, then get the negotiate response
                // and associate the connection id with this destination.

                if (proxyFeature.Route.Config.Metadata?.ContainsKey("hub") is true &&
                    StringValues.IsNullOrEmpty(c.HttpContext.Request.Query["yarp.affinity"]) &&
                    c.ProxyResponse is { IsSuccessStatusCode: true })
                {
                    // Redirect to the same URL with 
                    if (proxyFeature.ProxiedDestination is { } destination)
                    {
                        var req = c.HttpContext.Request;
                        var query = req.QueryString.Add("yarp.affinity", hashes.GetDestinationHash(destination));

                        var url = UriHelper.BuildAbsolute(req.Scheme, req.Host, req.PathBase, new(req.Path.Value!.Replace("/negotiate", "")), query);

                        // The negoitate response supports redirecting the client to another URL, we're taking advantage of that here
                        // to affinitize the request. https://github.com/dotnet/aspnetcore/blob/main/src/SignalR/docs/specs/TransportProtocols.md#all-versions
                        // back to whatever destination we landed on initially.

                        // This will force a renegotiation.

                        // This can be improved by natively supporting round tripping an affinity token from the response header
                        // into the query string.

                        c.SuppressResponseBody = true;

                        c.HttpContext.Response.Clear();
                        await c.HttpContext.Response.WriteAsJsonAsync(new
                        {
                            url
                        });
                    }
                }
            });
        });

        return builder;
    }

    private sealed class SignalRAffinity : ISessionAffinityPolicy
    {
        private readonly DestinationHashes _hashes;
        public SignalRAffinity(DestinationHashes hashes) => _hashes = hashes;
        
        public string Name => "SignalR";

        public void AffinitizeResponse(HttpContext context, ClusterState cluster, SessionAffinityConfig config, DestinationState destination)
        {
            // Nothing is written to the response
        }

        public AffinityResult FindAffinitizedDestinations(HttpContext context, ClusterState cluster, SessionAffinityConfig config, IReadOnlyList<DestinationState> destinations)
        {
            string? affinity = context.Request.Query["yarp.affinity"];
            if (affinity is not null)
            {
                foreach (var d in destinations)
                {
                    var hash = _hashes.GetDestinationHash(d);

                    if (hash == affinity)
                    {
                        return new(d, AffinityStatus.OK);
                    }
                }

                return new(null, AffinityStatus.DestinationNotFound);
            }

            return new(null, AffinityStatus.AffinityKeyNotSet);
        }
    }
    private class DestinationHashes
    {
        private readonly ConditionalWeakTable<DestinationState, string> _hashes = new();

        public string GetDestinationHash(DestinationState destination)
        {
            return _hashes.GetValue(destination, static d =>
            {
                var destinationUtf8Bytes = Encoding.UTF8.GetBytes(d.DestinationId.ToUpperInvariant());
                return Convert.ToHexString(SHA256.HashData(destinationUtf8Bytes)).ToLowerInvariant();
            });
        }
    }
}
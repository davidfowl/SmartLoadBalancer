using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.Extensions.Primitives;
using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.Model;
using Yarp.ReverseProxy.SessionAffinity;

namespace Microsoft.Extensions.DependencyInjection;

public static class SignalRSessionAffinity
{
    public static IReverseProxyBuilder AddSignalRSessionAffinity(this IReverseProxyBuilder builder)
    {
        builder.Services.AddSingleton<ISessionAffinityPolicy, SignalRAffinity>();
        builder.Services.AddSingleton<DestinationHashes>();

        builder.AddTransforms(transforms =>
        {
            // YARP 2.0
            //transforms.AddRequestTransform(async c =>
            //{
            //    await AffinitizeNegotiateRequest(c.HttpContext);
            //});

            // YARP 1.1.x
            //transforms.AddResponseTransform(async c =>
            //{
            //    if (c.ProxyResponse is { IsSuccessStatusCode: true })
            //    {
            //        c.SuppressResponseBody = await AffinitizeNegotiateRequest(c.HttpContext);
            //    }
            //});
        });

        return builder;
    }

    public static IReverseProxyApplicationBuilder UseAffinitizeNegotiateRequest(this IReverseProxyApplicationBuilder proxy)
    {
        proxy.Use(async (context, next) =>
        {
            if (await AffinitizeNegotiateRequest(context))
            {
                return;
            }

            await next(context);
        });

        return proxy;
    }

    private static async Task<bool> AffinitizeNegotiateRequest(HttpContext httpContext)
    {
        var hashes = httpContext.RequestServices.GetRequiredService<DestinationHashes>();
        var proxyFeature = httpContext.GetReverseProxyFeature();

        // If this route is marked as a signalr route, then get the negotiate response
        // and associate the connection id with this destination.

        if (proxyFeature.Route.Config.Metadata?.ContainsKey("hub") is true &&
            StringValues.IsNullOrEmpty(httpContext.Request.Query["yarp.affinity"]))
        {
            var destination = proxyFeature.ProxiedDestination ?? proxyFeature.AllDestinations[Random.Shared.Next(proxyFeature.AvailableDestinations.Count)];

            // Redirect to the same URL with 

            var req = httpContext.Request;
            var query = req.QueryString.Add("yarp.affinity", hashes.GetDestinationHash(destination));

            var url = UriHelper.BuildAbsolute(req.Scheme, req.Host, req.PathBase, new(req.Path.Value!.Replace("/negotiate", "")), query);

            // The negoitate response supports redirecting the client to another URL, we're taking advantage of that here
            // to affinitize the request. https://github.com/dotnet/aspnetcore/blob/main/src/SignalR/docs/specs/TransportProtocols.md#all-versions
            // back to whatever destination we landed on initially.

            // This will force a renegotiation.

            // This can be improved by natively supporting round tripping an affinity token from the response header
            // into the query string.

            httpContext.Response.Clear();
            await httpContext.Response.WriteAsJsonAsync(new
            {
                url
            });

            return true;
        }
        return false;

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
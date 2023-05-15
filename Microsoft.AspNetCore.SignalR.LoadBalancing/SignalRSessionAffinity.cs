using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
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
        builder.Services.AddSingleton<DestinationHashes>();

        builder.AddTransforms(transforms =>
        {
            if (transforms.Route.Metadata is { } metatada && metatada.ContainsKey("hub") is true)
            {
                // With YARP 2.0, request transforms can write short circuit the request
                transforms.AddResponseTransform(async c =>
                {
                    c.SuppressResponseBody = await AffinitizeNegotiateRequest(c.HttpContext);
                });
            }
        });

        return builder;
    }

    private static async Task<bool> AffinitizeNegotiateRequest(HttpContext httpContext)
    {
        // Check if should be affinitizing this route
        if (httpContext.GetReverseProxyFeature() is { } proxyFeature &&
            proxyFeature is { Cluster.Config.SessionAffinity.AffinityKeyName: var affinityKey } &&
            StringValues.IsNullOrEmpty(httpContext.Request.Query[affinityKey]))
        {
            var destination = (proxyFeature.ProxiedDestination, proxyFeature.AvailableDestinations) switch
            {
                (DestinationState proxied, _) => proxied,
                (_, [var one]) => one,
                (_, var many) => many[Random.Shared.Next(many.Count)],
            };

            var hashes = httpContext.RequestServices.GetRequiredService<DestinationHashes>();

            // Redirect to the same URL with the destination hash added to the query string
            var req = httpContext.Request;
            var query = req.QueryString.Add(affinityKey, hashes.GetDestinationHash(destination));

            var url = UriHelper.BuildAbsolute(req.Scheme, req.Host, req.PathBase, new(req.Path.Value!.Replace("/negotiate", "")), query);

            // The negoitate response supports redirecting the client to another URL, we're taking advantage of that here
            // to affinitize the request. https://github.com/dotnet/aspnetcore/blob/main/src/SignalR/docs/specs/TransportProtocols.md#all-versions
            // back to whatever destination we landed on initially.

            // This will force a renegotiation.

            // This can be improved by natively supporting round tripping an affinity token from the response header
            // into the query string.

            httpContext.Response.Clear();
            await httpContext.Response.WriteAsJsonAsync(new RedirectResponse(url), JsonContext.Default.RedirectResponse);

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
            string? affinity = context.Request.Query[config.AffinityKeyName];
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
    private sealed class DestinationHashes
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

internal record struct RedirectResponse(string Url);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(RedirectResponse))]
partial class JsonContext : JsonSerializerContext
{

}
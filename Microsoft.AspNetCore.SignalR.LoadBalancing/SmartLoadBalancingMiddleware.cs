using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Yarp.ReverseProxy.Forwarder;

namespace Sample;

public class SmartLoadBalancerOptions
{
    // Maximum number of retries after a failed request before giving up
    public int RetryAttempts { get; set; } = 10;

    public string? IngressUrl { get; set; }
}

public static class SmartLoadBalancingMiddlewareExtensions
{
    public static IServiceCollection AddSmartLoadBalancing(this IServiceCollection services, Action<SmartLoadBalancerOptions>? configure = null)
    {
        services.AddHttpForwarder();
        if (configure is not null)
        {
            services.Configure(configure);
        }
        return services;
    }

    public static IApplicationBuilder UseSmartLoadBalancing(this IApplicationBuilder app)
    {
        var forwarder = app.ApplicationServices.GetRequiredService<IHttpForwarder>();
        var options = app.ApplicationServices.GetRequiredService<IOptions<SmartLoadBalancerOptions>>().Value;

        var invoker = new HttpMessageInvoker(new SocketsHttpHandler());
        var config = new ForwarderRequestConfig();

        var transformer = new Transformer();
        var fwdOptions = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.All
        };

        return app.UseForwardedHeaders(fwdOptions).Use(async (context, next) =>
        {
            // If this is from an internal server, we don't proxy anything as that would cause a chain
            // of LB -> Server X -> LB -> Server Y requests that would spam the load balancer and servers.
            // Each retry is allowed a single hop.
            var from = context.Request.Headers["X-Internal"];

            var hubMetadata = !StringValues.IsNullOrEmpty(from) ? null : context.GetEndpoint()?.Metadata.GetMetadata<HubMetadata>();

            BufferingStream? bufferedBody = null;

            if (hubMetadata is not null)
            {
                // Buffer the body for POST requests. This allows us to retry them during proxying
                // if they fail initially.
                if (context.Request.Method == "POST" &&
                    context.Features.Get<IHttpRequestBodyDetectionFeature>()?.CanHaveBody == true)
                {
                    context.Request.EnableBuffering();
                }

                bufferedBody = new BufferingStream(context);

                context.Response.OnStarting(() =>
                {
                    // On first flush, if the response is successful, we want the stream to be pass through
                    if (context.WebSockets.IsWebSocketRequest)
                    {
                        return bufferedBody!.StopBufferingAsync();
                    }

                    return Task.CompletedTask;
                });

                context.Response.Body = bufferedBody;
            }

            await next(context);

            if (hubMetadata is not null)
            {
                var url = options.IngressUrl;

                if (url is null)
                {
                    // Grab the host and protocol from the proxy so we can make another request through it
                    // This should be the url for the proxy
                    url = $"{context.Request.Scheme}://{context.Request.Host}";
                }

                // Number of times we're going to try to resolve this request through the proxy.
                // This would be configurable.
                var tries = options.RetryAttempts;

                if (url is not null)
                {
                    // Try until we get a non 404 response from through the load balancer
                    while (context.Response.StatusCode == 404 && tries-- > 0)
                    {
                        // If we're posting, we need to re-send the body
                        if (context.Request.Body.CanSeek)
                        {
                            context.Request.Body.Position = 0;
                        }

                        // Clear the response in case some content was written to the body
                        context.Response.Clear();

                        // Forward requests via the load balancer to find the right instance
                        await forwarder.SendAsync(context, url, invoker, config, transformer);
                    }
                }

                await bufferedBody!.StopBufferingAsync();
            }
        });
    }

    private sealed class Transformer : HttpTransformer
    {
        public override ValueTask TransformRequestAsync(HttpContext httpContext, HttpRequestMessage proxyRequest, string destinationPrefix)
        {
            // Set this header so that we can determine if this request is a probing request or the original request from
            // the client.
            proxyRequest.Headers.TryAddWithoutValidation("X-Internal", "1");
            return base.TransformRequestAsync(httpContext, proxyRequest, destinationPrefix);
        }

        public override async ValueTask<bool> TransformResponseAsync(HttpContext httpContext, HttpResponseMessage? proxyResponse)
        {
            if (proxyResponse is not null)
            {
                if (proxyResponse.IsSuccessStatusCode)
                {
                    // Successful status code, stop buffering and use the real response body
                    if (httpContext.Response.Body is BufferingStream bufferingStream)
                    {
                        await bufferingStream.StopBufferingAsync();
                    }
                }
            }
            return await base.TransformResponseAsync(httpContext, proxyResponse);
        }
    }
}

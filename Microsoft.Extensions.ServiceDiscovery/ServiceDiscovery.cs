using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.Extensions.ServiceDiscovery;

public static class ServiceDiscoveryExtensions
{
    public static IServiceCollection AddTyeSeviceDiscvery(this IServiceCollection services) =>
        services.AddSingleton<IServiceDiscovery, TyeServiceDiscovery>();

}

public interface IServiceDiscovery
{
    ValueTask<IReadOnlyList<Replica>> GetAddresses(string name);

    ValueTask<Uri?> GetAddress(string name);
}

public record struct Replica(string Name, Uri Address);

public class TyeServiceDiscovery : IServiceDiscovery
{
    private readonly IConfiguration _configuration;
    private readonly HttpClient _client = new HttpClient();

    public TyeServiceDiscovery(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public ValueTask<Uri?> GetAddress(string name)
    {
        return ValueTask.FromResult(_configuration.GetServiceUri(name));
    }

    public async ValueTask<IReadOnlyList<Replica>> GetAddresses(string name)
    {
        // Quick check to see if this is even a service
        if (_configuration.GetServiceUri(name) is null)
        {
            return Array.Empty<Replica>();
        }

        // TODO: A TYE_HOST variable should be injected
        var serviceDefinition = await _client.GetFromJsonAsync<JsonObject>($"http://127.0.0.1:8000/api/v1/services/{name}");

        List<Replica>? replicas = null;
        foreach (var (key, replica) in serviceDefinition!["replicas"]!.AsObject())
        {
            var env = replica!["environment"]!.AsObject();
            var ports = replica!["ports"]!.AsArray().Select(n => n!.GetValue<int>()).ToArray();
            var replicaAddress = $"http://127.0.0.1:{ports[0]}";


            replicas ??= new();
            replicas.Add(new(key, new(replicaAddress)));
        }
        return replicas?.ToArray() ?? Array.Empty<Replica>();
    }
}
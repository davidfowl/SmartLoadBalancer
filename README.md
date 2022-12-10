# Smart load balancing for SignalR

When running SignalR behind a load balancer, it requires sticky sessions as SignalR is an inherently stateful technology. This can prove difficult in some pieces of infrastructure since it may be shared for all sorts of applications. 

This is a prototype showing technique for using sticky sessions without configuring the load balancer to do so. It accomplishes this by forwarding failed requests (404 missing connection id)
to the load balancer until the number of retries are exhausted or the proxying has found the right server.

This could be optimized in lots of different ways but the assumptions here are:
- There's no conncectivity between instances
- There's no way to configure the load balancer for stickiness
- The load balancing algorithm is going to eventually pick the right server after some number of requests

The retries are bounded so sending additional requests adds latency which can be dialed per request.

## Running the Sample

This requires [tye](https://github.com/dotnet/tye), which can be installed with the following command:

```
dotnet tool install --global Microsoft.Tye --version 0.11.0-alpha.22111.1
```

Run `tye run` in the root of the repository and it will launch 2 instances of the application, a load balancer
and a redis container.

Here's how a successful SignalR connection is made with this pattern:

![image](https://user-images.githubusercontent.com/95136/206862842-d1375a87-a38a-4276-a07c-77cfdfdeba7d.png)


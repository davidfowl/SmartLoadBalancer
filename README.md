# Smart load balancing for SignalR

When running SignalR behind a load balancer, it requires sticky sessions as SignalR is an inherently stateful technology. This can prove difficult in some pieces of infrastructure since it may be shared for all sorts of applications. 

This is a prototype showing a technique for using sticky sessions without configuring the load balancer to do so. It uses [YARP's](https://github.com/microsoft/reverse-proxy/) `IHttpFowarder` to forwarding failed requests (404 missing connection id)
to the load balancer until the number of retries are exhausted or the load balancer has found the "right server" (we got a valid response).

This may be useful if your environment has the following constraints:
- There's no way to configure the load balancer for stickiness.
- The load balancing algorithm is going to eventually pick the right server after some number of requests.
- There's no connectivity between instances.

## Running the Sample

This requires [tye](https://github.com/dotnet/tye), which can be installed with the following command:

```
dotnet tool install --global Microsoft.Tye --version 0.11.0-alpha.22111.1
```

Run `tye run` in the root of the repository and it will launch 2 instances of the application, a load balancer
and a redis container.

Here's how a successful SignalR connection is made with this pattern:

![image](https://user-images.githubusercontent.com/95136/206862842-d1375a87-a38a-4276-a07c-77cfdfdeba7d.png)

## Caveats

- There will be more connections from individual instances going through the load balancer for HTTP/1.1 requests. This can be improved if the load balancer supports HTTP/2 requests as the request from the internal server can be multiplexed over less connections.
- Worst case runtime assuming the load balancing algorithm will eventually distribute the load is [O(N log N)](https://en.wikipedia.org/wiki/Coupon_collector%27s_problem), where N is the number of instances the load balancing candidates for the particular request. This is also bounded by the max number of retries (which is configured to 10 by default).
- There are 2 hops to get data back to the client if it landed on the wrong server.

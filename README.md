# Smart load balancing for SignalR

When running SignalR behind a load balancer, it requires sticky sessions as SignalR is an inherently stateful technology. This can prove difficult in some pieces of infrastructure since it may be shared for all sorts of applications. 

This is a SignalR aware [YARP](https://github.com/microsoft/reverse-proxy/) session affinity provider that can be used to affinitize signalr
connections. 

## Running the Sample

This requires [tye](https://github.com/dotnet/tye), which can be installed with the following command:

```
dotnet tool install --global Microsoft.Tye --version 0.11.0-alpha.22111.1
```

Run `tye run` in the root of the repository and it will launch 2 instances of the application, a load balancer
and a redis container.

Here's how it works:

![image](https://user-images.githubusercontent.com/95136/218275323-0c8f496e-976d-436e-b5ec-5f4bfe26500e.png)

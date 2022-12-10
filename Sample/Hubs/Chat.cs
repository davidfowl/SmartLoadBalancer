using Microsoft.AspNetCore.SignalR;

namespace Sample;

class Chat : Hub
{
    public Task Send(string s)
    {
        return Clients.All.SendAsync("Send", s);
    }
}

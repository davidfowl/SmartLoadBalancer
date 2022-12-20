
using Microsoft.AspNetCore.SignalR.Client;

var connection = new HubConnectionBuilder()
             .WithUrl("http://localhost:8080/chat")
             .Build();

connection.On("Send", (string message) =>
{
    Console.WriteLine($"R: {message}");
});

await connection.StartAsync();

while (true)
{
    Console.Write("S: ");
    var line = Console.ReadLine();
    Console.WriteLine();
    await connection.InvokeAsync("Send", line);
}
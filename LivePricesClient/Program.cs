using System.Net.WebSockets;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Newtonsoft.Json.Linq;

var http = new HttpClient { BaseAddress = new Uri("http://localhost:5000") };
var wsUri = "ws://localhost:5000/ws";

int numClients = args.Length > 0 && int.TryParse(args[0], out var n) ? n : 1;

Console.WriteLine($"Spawning {numClients} client(s)...");

var instruments = await http.GetFromJsonAsync<List<string>>("/api/instruments");
if (instruments is null || instruments.Count == 0)
{
    Console.WriteLine("No instruments available.");
    return;
}

var tasks = Enumerable.Range(0, numClients)
    .Select((int i) => {
        var t = Task.Run(() => RunClientAsync(i, wsUri, instruments));
        Thread.Sleep(10);
        return t;
        }
    )
    .ToList();

await Task.WhenAll(tasks);

async Task RunClientAsync(int id, string uri, List<string> allInstruments)
{
    using var ws = new ClientWebSocket();

    try
    {
        await ws.ConnectAsync(new Uri(uri), CancellationToken.None);
        Console.WriteLine($"[Client {id}] Connected");

        var rnd = new Random(Guid.NewGuid().GetHashCode());
        var symbols = allInstruments.OrderBy(_ => rnd.Next()).Take(rnd.Next(1, allInstruments.Count)).ToList();

        var json = JsonSerializer.Serialize(new
        {
            subscribe = symbols,
        });

        var msg = Encoding.UTF8.GetBytes(json);
        await ws.SendAsync(msg, WebSocketMessageType.Text, true, CancellationToken.None);
        Console.WriteLine($"[Client {id}] Subscribed to: {string.Join(", ", symbols)}");

        var buffer = new byte[4096];
       var p=0;
        while (ws.State == WebSocketState.Open)
        {
            var res = await ws.ReceiveAsync(buffer, CancellationToken.None);
            var msgStr = Encoding.UTF8.GetString(buffer, 0, res.Count);
            Console.WriteLine($"[Client {id}] Message: {msgStr}");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[Client {id}] Error: {ex.Message}");
    }
}

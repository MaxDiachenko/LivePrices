using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;

namespace LiveFinPrices.Services;

public sealed class WebSocketBroadcastService : BackgroundService
{
    private readonly IPriceProvider _prov;
    private readonly DateTime[] _symbolUpdateTimes = new DateTime[Enum.GetValues<Instrument>().Length];
    //lock for _clients - fast enaugh 
    private readonly Object _lock = new();
    //using LinkedList for smooth performance
    private readonly LinkedList<ClientState> _clients = new();
    private readonly CancellationTokenSource _cts = new();
    //used like signal for update loop
    private readonly SemaphoreSlim _pricesUpdated = new(0, 1);

    private static UTF8Encoding utf8 = new UTF8Encoding(false);

  
    public static readonly IReadOnlyDictionary<Instrument, byte[]> _instrumentToId =
        new Dictionary<Instrument, byte[]>
        {
            [Instrument.BtcUsdt] = Encoding.UTF8.GetBytes(Instrument.BtcUsdt.ToString()),
            [Instrument.EthUsdt] = Encoding.UTF8.GetBytes(Instrument.EthUsdt.ToString()),
        };

    public WebSocketBroadcastService(IPriceProvider prov)
    {
        _prov = prov;

        //update just set update time, to let input thread away
        _prov.OnPriceUpdate += (Instrument, price) =>
        {
            _symbolUpdateTimes[(int)Instrument] = DateTime.UtcNow;
           
            if (_pricesUpdated.CurrentCount == 0)
            {
                _pricesUpdated.Release();
            }
        };
    }

    public async Task HandleClientAsync(WebSocket ws, CancellationToken token)
    {
        LinkedListNode<ClientState> client = null;

        lock(_lock)
        { 
            client =  _clients.AddLast(
                new ClientState(new JsonUpdateStreamer(ws, Settings.UpdatesBufferSize, token), new HashSet<Instrument>(), new Dictionary<Instrument, DateTime>())
            ); 
        }

        try
        {
            while (ws.State == WebSocketState.Open && !token.IsCancellationRequested)
            {
                var doc = await JsonWebSocketHelper.ReadJsonFromWebSocketAsync(ws, token);
                var root = doc.RootElement;

                if (root.TryGetProperty("subscribe", out var arr))
                {
                    var newSubs = new HashSet<Instrument>();

                    foreach (var el in arr.EnumerateArray())
                    {
                        var sym = el.GetString()!.ToLowerInvariant();
                        if (Settings.InstrumentByName.TryGetValue(sym, out var instr))
                            newSubs.Add(instr);
                    }
                    if(!newSubs.Any()) throw new Exception("client sent empty subscription.");

                    var newTimestamps = new Dictionary<Instrument, DateTime>();

                    foreach (var sym in newSubs)
                    {
                        if (client.Value.LastUpdates.TryGetValue(sym, out var ts))
                            newTimestamps[sym] = ts;
                        else
                            newTimestamps[sym] = DateTime.MinValue;
                    }

                    //no lock needed to update references
                    Volatile.Write(ref client.Value.Subscriptions, newSubs);
                    Volatile.Write(ref client.Value.LastUpdates, newTimestamps);
                    Console.WriteLine($"Client updated subscriptions: {string.Join(",", newSubs)}");
                  
                }
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Client error: {ex.Message}");
        }
        finally
        {
            CleanupClient(client);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken token)
    {
         Stopwatch sw = new Stopwatch();
        try{
            while (!token.IsCancellationRequested)
            {
               await _pricesUpdated.WaitAsync(token);
                sw.Restart();
                var count=0;
                var node = _clients.First;
                while(node != null)
                {
                    var client = node.Value;
                
                    if (client.UpdateStreamer.State == WebSocketState.Open)
                    {
                        try{
                            //forms update for each instrunet
                            foreach (var instr in Volatile.Read(ref client.Subscriptions))
                            {
                                var updated = _symbolUpdateTimes[(int)instr];
                                if (!Volatile.Read(ref client.LastUpdates).TryGetValue(instr, out var last) || updated > last)
                                {
                                    var price = _prov.GetCurrentPrice(instr);
                                    client.LastUpdates[instr] = updated;
                                    client.UpdateStreamer.AddUpdate(instr.ToString(), price);
                                }
                            }

                            //send update in launch and forget mode 
                            var task = client.UpdateStreamer.Complete();
                            if(task != null){
                                _ = task.ContinueWith(t =>
                                {
                                    if (!t.IsCompletedSuccessfully)
                                        Console.Error.WriteLine("Client send operation didn't complete.");

                                });
                                count++;
                            }
                        }
                        catch(Exception e)
                        {
                             Console.Error.WriteLine("Error updating client: "+e.Message);
                        }
                    }

                    lock(_lock)
                    { 
                        node = node.Next;
                    }
                }
                Console.WriteLine($"Update {count} client took {sw.ElapsedMilliseconds}");
               
            
            }
        }
        catch(Exception e)
        {
            Console.Error.WriteLine(e.Message+ "\n"+e.StackTrace);
        }
        Console.WriteLine("Main cycle exiting.");
    }

    private void CleanupClient(LinkedListNode<ClientState> client)
    {
        lock(_lock)
        { 
            try{
            if (client.List == _clients)
            { 
                _clients.Remove(client);
                client.Value.UpdateStreamer.Dispose();
            }
        }
        catch(Exception e)
        {
            Console.Error.WriteLine("Error removing client: "+e.Message);
        }
      
        }
    }

    public override void Dispose()
    {
        _cts.Cancel();
        base.Dispose();
    }

     public class ClientState{
        public JsonUpdateStreamer UpdateStreamer;
        public HashSet<Instrument> Subscriptions;
        public Dictionary<Instrument, DateTime> LastUpdates;

        public ClientState(JsonUpdateStreamer updateStreamer, HashSet<Instrument> subscriptions, Dictionary<Instrument, DateTime> lastUpdates)
        {
            UpdateStreamer = updateStreamer;
            Subscriptions = subscriptions;
            LastUpdates = lastUpdates;
        }
    }
}

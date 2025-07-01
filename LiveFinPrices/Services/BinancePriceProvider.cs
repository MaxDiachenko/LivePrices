using System.Net.WebSockets;
using System.Text.Json;
using System.Text;

namespace LiveFinPrices.Services
{
    

    public class BinancePriceProvider : BackgroundService, IPriceProvider
    {
        private ClientWebSocket _socket = new();
        private readonly CancellationTokenSource _cts = new();
        private readonly Uri _url = new("wss://stream.binance.com:443/ws");
        private readonly float[] _prices = new float[Enum.GetValues<Instrument>().Length];
        public event Action<Instrument, float> OnPriceUpdate = (_, _) => { };

       
        public IEnumerable<string> GetAvailableInstruments() => Settings.InstrumentByName.Keys;

        public float GetCurrentPrice(string symbol)
        {
            return Settings.InstrumentByName.TryGetValue(symbol, out var i) ? _prices[(int)i] : 0f;
        }
        public float GetCurrentPrice(Instrument instrument)
        {
            return Volatile.Read(ref _prices[(int)instrument]);
        }

        protected override async Task ExecuteAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                _socket?.Dispose();
                _socket = new ClientWebSocket();

                try
                {
                    await _socket.ConnectAsync(_url, token);

                    var msg = new
                    {
                        method = "SUBSCRIBE",
                        @params = Settings.InstrumentByName.Keys.Select(s => $"{s}@aggTrade").ToArray(),
                        id = 1
                    };
                    var data = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(msg));
                    await _socket.SendAsync(data, WebSocketMessageType.Text, true, token);

                    while (_socket.State == WebSocketState.Open && !token.IsCancellationRequested)
                    {
                       
                        var doc = await JsonWebSocketHelper.ReadJsonFromWebSocketAsync(_socket, token);
                        
                        var root = doc.RootElement;
                        
                        if (root.TryGetProperty("s", out var sProp)
                         && root.TryGetProperty("p", out var pProp)
                         && Settings.InstrumentByName.TryGetValue(sProp.GetString()!, out var instr))
                        {
                            var sym = sProp.GetString()!.ToLowerInvariant();
                            var price = float.Parse(pProp.GetString());
                            _prices[(int)instr] = price;
                            Volatile.Write(ref _prices[(int)instr], price );
                            Console.WriteLine($"Update symbol:{instr.ToString()}, price: {price}");
                            
                            OnPriceUpdate(instr, price);
                        }
                        
                    }
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Binance WS error: {ex.Message}");
                }

                await Task.Delay(3000, token);
            }
        }

        public override void Dispose()
        {
            _cts.Cancel();
            _socket?.Dispose();
            base.Dispose();
        }
    }
}
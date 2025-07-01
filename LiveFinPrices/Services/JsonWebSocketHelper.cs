using System.Buffers;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace LiveFinPrices.Services
{
    public static class JsonWebSocketHelper
    {
        private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);
        public static async Task<JsonDocument> ReadJsonFromWebSocketAsync(WebSocket ws, CancellationToken token)
        {
            using IMemoryOwner<byte> memory = MemoryPool<byte>.Shared.Rent(1024 * 8);
            int total = 0;

            while (true)
            {
                var segment = memory.Memory.Slice(total);
                if (segment.Length == 0)
                    throw new InvalidOperationException("Buffer too small.");

                var res = await ws.ReceiveAsync(segment, token);

                if (res.MessageType == WebSocketMessageType.Close)
                    throw new WebSocketException("Socket closed unexpectedly");

                total += res.Count;

                if (res.EndOfMessage)
                    return JsonDocument.Parse(memory.Memory.Slice(0, total));

            }
        }
    }
}
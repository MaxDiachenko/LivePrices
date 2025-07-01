using System.Buffers;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace LiveFinPrices.Services
{
    public class JsonUpdateStreamer : IDisposable
    {
        private readonly WebSocket _socket;
        private readonly byte[] _bufferA;
        private readonly byte[] _bufferB;
        private byte[] _currentBuffer;
        private readonly CancellationTokenSource _cts;
        private int _position;
        private bool _first = true;
        private readonly int _bufferSize;
        private readonly CancellationToken _token;
        private Task _previousSendTask = Task.CompletedTask;

        public WebSocketState State => _socket.State;

        private static readonly byte[] Prefix = Encoding.UTF8.GetBytes("[");
        private static readonly byte[] Suffix = Encoding.UTF8.GetBytes("]");
        private static readonly byte[] Comma = Encoding.UTF8.GetBytes(",");
        private static readonly byte[] IdPrefix = Encoding.UTF8.GetBytes("{\"id\":\"");
        private static readonly byte[] PricePrefix = Encoding.UTF8.GetBytes("\",\"price\":");
        private static readonly byte[] ObjectSuffix = Encoding.UTF8.GetBytes("}");

        public JsonUpdateStreamer(WebSocket socket, int bufferSize = 4096, CancellationToken token = default)
        {
            _socket = socket;
            _bufferSize = bufferSize;
            _bufferA = ArrayPool<byte>.Shared.Rent(bufferSize);
            _bufferB = ArrayPool<byte>.Shared.Rent(bufferSize);
            _currentBuffer = _bufferA;
            _cts = new CancellationTokenSource();
            _token = CancellationTokenSource.CreateLinkedTokenSource(token, _cts.Token).Token;
            _position = 0;

            Prefix.CopyTo(_currentBuffer.AsSpan(_position));
            _position += Prefix.Length;
        }

        public void AddUpdate(string id, float price)
        {
            var obj = new { id, price };
            string json = JsonSerializer.Serialize(obj);
            byte[] jsonUtf8 = Encoding.UTF8.GetBytes(json);

            int estimatedUpdateSize = (_first ? 0 : Comma.Length) + jsonUtf8.Length + Suffix.Length;

            if (_currentBuffer.Length - _position < estimatedUpdateSize)
            {
                if(!FlushCurrentBufferAsync(false))
                    return;
            }

            if (!_first)
            {
                Comma.CopyTo(_currentBuffer.AsSpan(_position));
                _position += Comma.Length;
            }

            _first = false;
            
            jsonUtf8.CopyTo(_currentBuffer.AsSpan(_position));
            _position += jsonUtf8.Length;
        }

        public Task Complete()
        {
            if (_position == Prefix.Length) return Task.CompletedTask;

            Suffix.CopyTo(_currentBuffer.AsSpan(_position));
            _position += Suffix.Length;
            _first = true;

            if(!FlushCurrentBufferAsync(true))
                return null;
            return _previousSendTask;
        }

        private bool FlushCurrentBufferAsync(bool endMessage)
        {
            if (_position == 0) return true;

            if (!_previousSendTask.IsCompleted)
            {
                Console.Error.WriteLine("Skip client update. ");
                return false;
            } 

            var segment = new ArraySegment<byte>(_currentBuffer, 0, _position);

            if (_socket.State == WebSocketState.Open)
            {
                _previousSendTask = _socket.SendAsync(segment, WebSocketMessageType.Text, endMessage, _token);
            }
            else
            {
                Console.Error.WriteLine("Socket closed, skip client update. ");
                return false;
            }

            _currentBuffer = ReferenceEquals(_currentBuffer, _bufferA) ? _bufferB : _bufferA;
            _position = 0;
            _first = true;

            Prefix.CopyTo(_currentBuffer.AsSpan(_position));
            _position += Prefix.Length;
            return true;
        }

        public void Dispose()
        {
            if(_cts.IsCancellationRequested) return;
            _cts.Cancel();
            ArrayPool<byte>.Shared.Return(_bufferA);
            ArrayPool<byte>.Shared.Return(_bufferB);
        }
    }
}
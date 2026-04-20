using System.Collections.Concurrent;
using System.Net.Sockets;
using kcp2k;
using ErrorCode = kcp2k.ErrorCode;

namespace StellarNetLite.LoadTest;

internal interface ILoadTestTransport : IDisposable
{
    bool IsConnected { get; }
    void Connect(string host, int port);
    void Stop();
    void Send(byte[] payload, int length);
    void Pump();
    bool TryDequeue(out PacketData packet);
}

internal sealed class KcpLoadTestTransport : ILoadTestTransport
{
    private readonly ConcurrentQueue<PacketData> _receivedPackets = new();
    private readonly KcpConfig _config = new(
        DualMode: false,
        RecvBufferSize: 1024 * 1024 * 8,
        SendBufferSize: 1024 * 1024 * 8,
        Mtu: 1200,
        NoDelay: true,
        Interval: 10,
        FastResend: 2,
        CongestionWindow: false,
        SendWindowSize: 4096,
        ReceiveWindowSize: 4096,
        Timeout: 10000,
        MaxRetransmits: 40
    );

    private KcpClient? _client;

    public bool IsConnected { get; private set; }

    public void Connect(string host, int port)
    {
        if (_client != null)
        {
            return;
        }

        _client = new KcpClient(OnConnected, OnDataReceived, OnDisconnected, OnError, _config);
        _client.Connect(host, (ushort)port);
    }

    public void Stop()
    {
        IsConnected = false;
        _client?.Disconnect();
        _client = null;
        while (_receivedPackets.TryDequeue(out _))
        {
        }
    }

    public void Send(byte[] payload, int length)
    {
        if (!IsConnected || _client == null)
        {
            return;
        }

        _client.Send(new ArraySegment<byte>(payload, 0, length), KcpChannel.Reliable);
    }

    public void Pump()
    {
        if (_client == null)
        {
            return;
        }

        _client.TickIncoming();
        _client.TickOutgoing();
    }

    public bool TryDequeue(out PacketData packet)
    {
        return _receivedPackets.TryDequeue(out packet);
    }

    public void Dispose()
    {
        Stop();
    }

    private void OnConnected()
    {
        IsConnected = true;
    }

    private void OnDisconnected()
    {
        IsConnected = false;
    }

    private void OnDataReceived(ArraySegment<byte> message, KcpChannel channel)
    {
        if (message.Array == null)
        {
            return;
        }

        byte[] safeBuffer = new byte[message.Count];
        Buffer.BlockCopy(message.Array, message.Offset, safeBuffer, 0, message.Count);
        if (LitePacketCodec.TryDeserialize(safeBuffer, 0, safeBuffer.Length, out PacketData packet))
        {
            _receivedPackets.Enqueue(packet);
        }
    }

    private void OnError(ErrorCode error, string reason)
    {
        Console.WriteLine($"[错误][KCP] 传输层异常: {error} - {reason}");
    }
}

internal sealed class TcpLoadTestTransport : ILoadTestTransport
{
    private readonly ConcurrentQueue<PacketData> _receivedPackets = new();
    private TcpClient? _client;
    private NetworkStream? _stream;
    private CancellationTokenSource? _receiveCts;

    public bool IsConnected { get; private set; }

    public void Connect(string host, int port)
    {
        if (_client != null)
        {
            return;
        }

        _client = new TcpClient
        {
            NoDelay = true
        };
        _client.Connect(host, port);
        _stream = _client.GetStream();
        _receiveCts = new CancellationTokenSource();
        IsConnected = true;
        _ = ReceiveLoopAsync(_receiveCts.Token);
    }

    public void Stop()
    {
        IsConnected = false;
        _receiveCts?.Cancel();
        _stream?.Close();
        _client?.Close();
        _stream = null;
        _client = null;
        _receiveCts?.Dispose();
        _receiveCts = null;
        while (_receivedPackets.TryDequeue(out _))
        {
        }
    }

    public void Send(byte[] payload, int length)
    {
        if (!IsConnected || _stream == null)
        {
            return;
        }

        byte[] frame = new byte[length + 4];
        BitConverter.GetBytes(length).CopyTo(frame, 0);
        Buffer.BlockCopy(payload, 0, frame, 4, length);
        _stream.Write(frame, 0, frame.Length);
    }

    public void Pump()
    {
    }

    public bool TryDequeue(out PacketData packet)
    {
        return _receivedPackets.TryDequeue(out packet);
    }

    public void Dispose()
    {
        Stop();
    }

    private async Task ReceiveLoopAsync(CancellationToken token)
    {
        byte[] header = new byte[4];
        try
        {
            while (!token.IsCancellationRequested && _stream != null)
            {
                if (!await ReadExactAsync(_stream, header, 4, token))
                {
                    break;
                }

                int length = BitConverter.ToInt32(header, 0);
                if (length <= 0 || length > 10 * 1024 * 1024)
                {
                    break;
                }

                byte[] payload = new byte[length];
                if (!await ReadExactAsync(_stream, payload, length, token))
                {
                    break;
                }

                if (LitePacketCodec.TryDeserialize(payload, 0, payload.Length, out PacketData packet))
                {
                    _receivedPackets.Enqueue(packet);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[错误][TCP] 传输层异常: {ex.GetType().Name} - {ex.Message}");
        }
        finally
        {
            IsConnected = false;
        }
    }

    private static async Task<bool> ReadExactAsync(NetworkStream stream, byte[] buffer, int length, CancellationToken token)
    {
        int totalRead = 0;
        while (totalRead < length)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(totalRead, length - totalRead), token);
            if (read <= 0)
            {
                return false;
            }

            totalRead += read;
        }

        return true;
    }
}

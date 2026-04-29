using System.Collections.Concurrent;
using System.Net.Sockets;
using kcp2k;
using ErrorCode = kcp2k.ErrorCode;

namespace StellarNetLite.LoadTest;

/// <summary>
/// 压测客户端传输层抽象。
/// 屏蔽 KCP 和 TCP 的差异。
/// </summary>
internal interface ILoadTestTransport : IDisposable
{
    /// <summary>
    /// 当前是否处于已连接状态。
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// 建立到目标地址的连接。
    /// </summary>
    void Connect(string host, int port);

    /// <summary>
    /// 关闭连接并清理缓存。
    /// </summary>
    void Stop();

    /// <summary>
    /// 发送一个已编码好的网络包。
    /// </summary>
    void Send(byte[] payload, int length);

    /// <summary>
    /// 驱动传输层内部收发。
    /// </summary>
    void Pump();

    /// <summary>
    /// 读取一个已经反序列化完成的包。
    /// </summary>
    bool TryDequeue(out PacketData packet);
}

/// <summary>
/// 基于 kcp2k 的压测传输实现。
/// </summary>
internal sealed class KcpLoadTestTransport : ILoadTestTransport
{
    /// <summary>
    /// 已收到并等待上层处理的数据包队列。
    /// </summary>
    private readonly ConcurrentQueue<PacketData> _receivedPackets = new();

    /// <summary>
    /// KCP 客户端配置。
    /// </summary>
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

    /// <summary>
    /// 当前 KCP 客户端实例。
    /// </summary>
    private KcpClient? _client;

    /// <summary>
    /// 当前是否已连接成功。
    /// </summary>
    public bool IsConnected { get; private set; }

    /// <summary>
    /// 建立 KCP 连接。
    /// </summary>
    public void Connect(string host, int port)
    {
        if (_client != null)
        {
            return;
        }

        _client = new KcpClient(OnConnected, OnDataReceived, OnDisconnected, OnError, _config);
        _client.Connect(host, (ushort)port);
    }

    /// <summary>
    /// 断开 KCP 连接并清空接收队列。
    /// </summary>
    public void Stop()
    {
        IsConnected = false;
        _client?.Disconnect();
        _client = null;
        while (_receivedPackets.TryDequeue(out _))
        {
        }
    }

    /// <summary>
    /// 发送 KCP 可靠通道数据。
    /// </summary>
    public void Send(byte[] payload, int length)
    {
        if (!IsConnected || _client == null)
        {
            return;
        }

        _client.Send(new ArraySegment<byte>(payload, 0, length), KcpChannel.Reliable);
    }

    /// <summary>
    /// 推进 KCP 收发 Tick。
    /// </summary>
    public void Pump()
    {
        if (_client == null)
        {
            return;
        }

        _client.TickIncoming();
        _client.TickOutgoing();
    }

    /// <summary>
    /// 取出一条已解码的数据包。
    /// </summary>
    public bool TryDequeue(out PacketData packet)
    {
        return _receivedPackets.TryDequeue(out packet);
    }

    /// <summary>
    /// 释放传输资源。
    /// </summary>
    public void Dispose()
    {
        Stop();
    }

    /// <summary>
    /// KCP 建连成功回调。
    /// </summary>
    private void OnConnected()
    {
        IsConnected = true;
    }

    /// <summary>
    /// KCP 断开连接回调。
    /// </summary>
    private void OnDisconnected()
    {
        IsConnected = false;
    }

    /// <summary>
    /// 收到网络数据后尝试解码成 PacketData。
    /// </summary>
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

    /// <summary>
    /// 输出 KCP 传输层错误日志。
    /// </summary>
    private void OnError(ErrorCode error, string reason)
    {
        Console.WriteLine($"[错误][KCP] 传输层异常: {error} - {reason}");
    }
}

/// <summary>
/// 基于 TcpClient 的压测传输实现。
/// </summary>
internal sealed class TcpLoadTestTransport : ILoadTestTransport
{
    /// <summary>
    /// 已收到并等待上层处理的数据包队列。
    /// </summary>
    private readonly ConcurrentQueue<PacketData> _receivedPackets = new();

    /// <summary>
    /// TCP 客户端实例。
    /// </summary>
    private TcpClient? _client;

    /// <summary>
    /// 当前网络流。
    /// </summary>
    private NetworkStream? _stream;

    /// <summary>
    /// 接收循环取消源。
    /// </summary>
    private CancellationTokenSource? _receiveCts;

    /// <summary>
    /// 当前是否已连接成功。
    /// </summary>
    public bool IsConnected { get; private set; }

    /// <summary>
    /// 建立 TCP 连接并启动接收循环。
    /// </summary>
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

    /// <summary>
    /// 停止 TCP 连接并回收资源。
    /// </summary>
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

    /// <summary>
    /// 发送带长度前缀的 TCP 数据帧。
    /// </summary>
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

    /// <summary>
    /// TCP 版本不需要显式 Tick。
    /// </summary>
    public void Pump()
    {
    }

    /// <summary>
    /// 取出一条已解码的数据包。
    /// </summary>
    public bool TryDequeue(out PacketData packet)
    {
        return _receivedPackets.TryDequeue(out packet);
    }

    /// <summary>
    /// 释放传输资源。
    /// </summary>
    public void Dispose()
    {
        Stop();
    }

    /// <summary>
    /// 持续读取长度前缀帧并反序列化为 PacketData。
    /// </summary>
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

    /// <summary>
    /// 从网络流中精确读取指定长度的数据。
    /// </summary>
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

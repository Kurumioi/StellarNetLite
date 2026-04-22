using System.Buffers;
using System.Collections.Concurrent;
using StellarNet.Lite.Shared.Core;

namespace StellarNet.Lite.Server.Infrastructure
{
    /// <summary>
    /// 服务端统一出站发包队列。
    /// 房间工作线程只负责入队，真正的 Transport 发送仍回到主服务端线程执行。
    /// </summary>
    public sealed class ServerOutboundDispatcher
    {
        private sealed class OutboundEnvelope
        {
            public int ConnectionId;
            public int[] ConnectionIds;
            public int ConnectionCount;
            public Packet Packet;
            public bool PayloadFromPool;
        }

        private readonly ConcurrentQueue<OutboundEnvelope> _queue = new ConcurrentQueue<OutboundEnvelope>();

        public int PendingCount => _queue.Count;

        public void EnqueueSingle(int connectionId, Packet packet, bool payloadFromPool)
        {
            _queue.Enqueue(new OutboundEnvelope
            {
                ConnectionId = connectionId,
                Packet = packet,
                PayloadFromPool = payloadFromPool
            });
        }

        public void EnqueueMany(int[] connectionIds, int connectionCount, Packet packet, bool payloadFromPool)
        {
            _queue.Enqueue(new OutboundEnvelope
            {
                ConnectionIds = connectionIds,
                ConnectionCount = connectionCount,
                Packet = packet,
                PayloadFromPool = payloadFromPool
            });
        }

        public void Drain(INetworkTransport transport)
        {
            if (transport == null)
            {
                return;
            }

            while (_queue.TryDequeue(out OutboundEnvelope envelope))
            {
                try
                {
                    if (envelope.ConnectionIds != null)
                    {
                        for (int i = 0; i < envelope.ConnectionCount; i++)
                        {
                            transport.SendToClient(envelope.ConnectionIds[i], envelope.Packet);
                        }
                    }
                    else
                    {
                        transport.SendToClient(envelope.ConnectionId, envelope.Packet);
                    }
                }
                finally
                {
                    if (envelope.PayloadFromPool && envelope.Packet.Payload != null)
                    {
                        ArrayPool<byte>.Shared.Return(envelope.Packet.Payload);
                    }
                }
            }
        }
    }
}

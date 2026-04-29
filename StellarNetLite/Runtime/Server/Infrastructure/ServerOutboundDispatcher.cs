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
        /// <summary>
        /// 单次出站投递封套。
        /// </summary>
        private sealed class OutboundEnvelope
        {
            /// <summary>
            /// 单播目标连接 Id。
            /// </summary>
            public int ConnectionId;

            /// <summary>
            /// 批量发送目标连接数组。
            /// </summary>
            public int[] ConnectionIds;

            /// <summary>
            /// 批量发送有效连接数。
            /// </summary>
            public int ConnectionCount;

            /// <summary>
            /// 待发送的数据包。
            /// </summary>
            public Packet Packet;

            /// <summary>
            /// 当前载荷是否来自共享池。
            /// </summary>
            public bool PayloadFromPool;
        }

        /// <summary>
        /// 当前待发送出站队列。
        /// </summary>
        private readonly ConcurrentQueue<OutboundEnvelope> _queue = new ConcurrentQueue<OutboundEnvelope>();

        /// <summary>
        /// 当前等待发送的封套数量。
        /// </summary>
        public int PendingCount => _queue.Count;

        /// <summary>
        /// 入队一条单播消息。
        /// </summary>
        public void EnqueueSingle(int connectionId, Packet packet, bool payloadFromPool)
        {
            _queue.Enqueue(new OutboundEnvelope
            {
                ConnectionId = connectionId,
                Packet = packet,
                PayloadFromPool = payloadFromPool
            });
        }

        /// <summary>
        /// 入队一条批量发送消息。
        /// </summary>
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

        /// <summary>
        /// 把当前队列中的所有出站消息写入传输层。
        /// </summary>
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

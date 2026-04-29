using System;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using StellarNet.Lite.Shared.Core;

namespace StellarNet.Lite.Shared.Infrastructure
{
    /// <summary>
    /// 框架序列化抽象。
    /// 用于屏蔽 JSON / 二进制协议的具体实现。
    /// </summary>
    public interface INetSerializer
    {
        /// <summary>
        /// 序列化对象并返回新字节数组。
        /// </summary>
        byte[] Serialize(object obj);

        /// <summary>
        /// 序列化对象到外部提供的缓冲区。
        /// 返回实际写入长度。
        /// </summary>
        int Serialize(object obj, byte[] buffer);

        /// <summary>
        /// 从指定字节片段反序列化目标对象。
        /// </summary>
        object Deserialize(byte[] data, int offset, int length, Type type);
    }

    /// <summary>
    /// 高频协议自定义二进制接口。
    /// 适合对象同步、回放分块这类高频/大体积消息。
    /// </summary>
    public interface ILiteNetSerializable
    {
        /// <summary>
        /// 写入自定义二进制内容。
        /// </summary>
        void Serialize(BinaryWriter writer);

        /// <summary>
        /// 从二进制内容中恢复对象状态。
        /// </summary>
        void Deserialize(BinaryReader reader);
    }

    /// <summary>
    /// 默认混合序列化器。
    /// ILiteNetSerializable 走二进制，其余对象走 JSON。
    /// </summary>
    public sealed class LiteNetSerializer : INetSerializer
    {
        /// <summary>
        /// 不带 BOM 的 UTF8 编码器。
        /// </summary>
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        /// <summary>
        /// 序列化对象并返回新的字节数组。
        /// </summary>
        public byte[] Serialize(object obj)
        {
            if (obj == null)
            {
                NetLogger.LogError("LiteNetSerializer", "序列化失败: obj 为空");
                return Array.Empty<byte>();
            }

            // 高频消息优先走手写二进制，减少 GC 和字符串开销。
            if (obj is ILiteNetSerializable serializable)
            {
                using (var ms = new MemoryStream())
                using (var writer = new BinaryWriter(ms, Utf8NoBom, false))
                {
                    serializable.Serialize(writer);
                    writer.Flush();
                    byte[] result = ms.ToArray();
                    if (result == null || result.Length <= 0)
                    {
                        NetLogger.LogError("LiteNetSerializer", $"二进制序列化失败: 输出结果为空, Type:{obj.GetType().FullName}");
                        return Array.Empty<byte>();
                    }

                    return result;
                }
            }

            // 低频消息走 JSON，换取开发效率。
            string json = JsonConvert.SerializeObject(obj);
            if (string.IsNullOrEmpty(json))
            {
                NetLogger.LogError("LiteNetSerializer", $"JSON 序列化失败: 结果为空, Type:{obj.GetType().FullName}");
                return Array.Empty<byte>();
            }

            return Utf8NoBom.GetBytes(json);
        }

        /// <summary>
        /// 序列化对象到外部缓冲区，减少发包时的额外分配。
        /// </summary>
        public int Serialize(object obj, byte[] buffer)
        {
            if (obj == null)
            {
                NetLogger.LogError("LiteNetSerializer", "序列化失败: obj 为空");
                return 0;
            }

            if (buffer == null)
            {
                NetLogger.LogError("LiteNetSerializer", $"序列化失败: buffer 为空, Type:{obj.GetType().FullName}");
                return 0;
            }

            if (buffer.Length == 0)
            {
                NetLogger.LogError("LiteNetSerializer", $"序列化失败: buffer 长度为 0, Type:{obj.GetType().FullName}");
                return 0;
            }

            // 复用外部 buffer，避免发包时频繁 new byte[]。
            if (obj is ILiteNetSerializable serializable)
            {
                using (var ms = new MemoryStream(buffer, 0, buffer.Length, true, true))
                using (var writer = new BinaryWriter(ms, Utf8NoBom, false))
                {
                    long startPosition = ms.Position;
                    serializable.Serialize(writer);
                    writer.Flush();
                    int length = (int)(ms.Position - startPosition);

                    if (length <= 0)
                    {
                        NetLogger.LogError("LiteNetSerializer",
                            $"二进制序列化失败: 输出长度非法, Type:{obj.GetType().FullName}, BufferLength:{buffer.Length}, Length:{length}");
                        return 0;
                    }

                    if (length > buffer.Length)
                    {
                        NetLogger.LogError("LiteNetSerializer",
                            $"二进制序列化失败: 输出长度越界, Type:{obj.GetType().FullName}, BufferLength:{buffer.Length}, Length:{length}");
                        return 0;
                    }

                    return length;
                }
            }

            string json = JsonConvert.SerializeObject(obj);
            if (string.IsNullOrEmpty(json))
            {
                NetLogger.LogError("LiteNetSerializer", $"JSON 序列化失败: 结果为空, Type:{obj.GetType().FullName}");
                return 0;
            }

            int requiredLength = Utf8NoBom.GetByteCount(json);
            if (requiredLength <= 0)
            {
                NetLogger.LogError("LiteNetSerializer", $"JSON 序列化失败: 字节长度非法, Type:{obj.GetType().FullName}, JsonLength:{json.Length}");
                return 0;
            }

            if (requiredLength > buffer.Length)
            {
                NetLogger.LogError(
                    "LiteNetSerializer",
                    $"JSON 序列化失败: buffer 容量不足, Type:{obj.GetType().FullName}, Required:{requiredLength}, BufferLength:{buffer.Length}");
                return 0;
            }

            return Utf8NoBom.GetBytes(json, 0, json.Length, buffer, 0);
        }

        /// <summary>
        /// 从字节片段中反序列化对象。
        /// </summary>
        public object Deserialize(byte[] data, int offset, int length, Type type)
        {
            if (data == null)
            {
                NetLogger.LogError("LiteNetSerializer", $"反序列化失败: data 为空, Type:{type?.FullName ?? "null"}");
                return null;
            }

            if (type == null)
            {
                NetLogger.LogError("LiteNetSerializer", $"反序列化失败: type 为空, DataLength:{data.Length}, Offset:{offset}, Length:{length}");
                return null;
            }

            if (offset < 0 || length < 0 || offset + length > data.Length)
            {
                NetLogger.LogError(
                    "LiteNetSerializer",
                    $"反序列化失败: 数据边界非法, Type:{type.FullName}, DataLength:{data.Length}, Offset:{offset}, Length:{length}");
                return null;
            }

            if (length == 0)
            {
                NetLogger.LogError("LiteNetSerializer", $"反序列化失败: length 为 0, Type:{type.FullName}, Offset:{offset}");
                return null;
            }

            // 反序列化时按类型能力选择 JSON 或二进制解码。
            if (typeof(ILiteNetSerializable).IsAssignableFrom(type))
            {
                object instance = Activator.CreateInstance(type);
                if (!(instance is ILiteNetSerializable serializable))
                {
                    NetLogger.LogError("LiteNetSerializer", $"二进制反序列化失败: 无法实例化 ILiteNetSerializable, Type:{type.FullName}");
                    return null;
                }

                try
                {
                    using (var ms = new MemoryStream(data, offset, length, false))
                    using (var reader = new BinaryReader(ms, Utf8NoBom, false))
                    {
                        serializable.Deserialize(reader);
                        if (ms.Position > ms.Length)
                        {
                            NetLogger.LogError("LiteNetSerializer",
                                $"二进制反序列化失败: 读取位置越界, Type:{type.FullName}, Position:{ms.Position}, Length:{ms.Length}");
                            return null;
                        }
                    }
                }
                catch (Exception ex)
                {
                    NetLogger.LogError(
                        "LiteNetSerializer",
                        $"二进制反序列化失败: Type:{type.FullName}, Offset:{offset}, Length:{length}, Exception:{ex.GetType().Name}, Message:{ex.Message}");
                    return null;
                }

                return serializable;
            }

            string json = Utf8NoBom.GetString(data, offset, length);
            if (string.IsNullOrEmpty(json))
            {
                NetLogger.LogError("LiteNetSerializer", $"JSON 反序列化失败: 内容为空, Type:{type.FullName}, Offset:{offset}, Length:{length}");
                return null;
            }

            object result = JsonConvert.DeserializeObject(json, type);
            if (result == null)
            {
                NetLogger.LogError("LiteNetSerializer", $"JSON 反序列化失败: 结果为空, Type:{type.FullName}, JsonLength:{json.Length}");
                return null;
            }

            return result;
        }
    }
}

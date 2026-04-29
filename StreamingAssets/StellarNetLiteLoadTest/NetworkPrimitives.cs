using System.Buffers.Binary;
using System.Text;
using System.Text.Json;

namespace StellarNetLite.LoadTest;

/// <summary>
/// 压测工具使用的网络作用域。
/// </summary>
internal enum NetScope : byte
{
    Global = 0,
    Room = 1
}

/// <summary>
/// 压测工具内部使用的轻量网络包结构。
/// </summary>
internal readonly struct PacketData
{
    /// <summary>
    /// 包序号。
    /// </summary>
    public readonly uint Seq;

    /// <summary>
    /// 协议 Id。
    /// </summary>
    public readonly int MsgId;

    /// <summary>
    /// 协议作用域。
    /// </summary>
    public readonly NetScope Scope;

    /// <summary>
    /// 房间协议对应的 RoomId。
    /// </summary>
    public readonly string RoomId;

    /// <summary>
    /// 原始载荷数据。
    /// </summary>
    public readonly byte[] Payload;

    /// <summary>
    /// 载荷起始偏移。
    /// </summary>
    public readonly int PayloadOffset;

    /// <summary>
    /// 载荷长度。
    /// </summary>
    public readonly int PayloadLength;

    /// <summary>
    /// 使用完整偏移信息构造一个网络包。
    /// </summary>
    public PacketData(uint seq, int msgId, NetScope scope, string roomId, byte[] payload, int payloadOffset, int payloadLength)
    {
        Seq = seq;
        MsgId = msgId;
        Scope = scope;
        RoomId = roomId ?? string.Empty;
        Payload = payload;
        PayloadOffset = payloadOffset;
        PayloadLength = payloadLength;
    }

    /// <summary>
    /// 使用默认偏移 0 构造一个网络包。
    /// </summary>
    public PacketData(uint seq, int msgId, NetScope scope, string roomId, byte[] payload, int payloadLength)
        : this(seq, msgId, scope, roomId, payload, 0, payloadLength)
    {
    }
}

/// <summary>
/// 压测工具使用的轻量协议编解码器。
/// </summary>
internal static class LitePacketCodec
{
    /// <summary>
    /// 不带 BOM 的 UTF8 编码器。
    /// </summary>
    private static readonly UTF8Encoding Utf8NoBom = new(false);

    /// <summary>
    /// 把 PacketData 编码到目标缓冲区。
    /// </summary>
    public static int Serialize(in PacketData packet, byte[] buffer, int startOffset = 0)
    {
        int offset = startOffset;

        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(offset, 4), packet.Seq);
        offset += 4;

        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(offset, 4), packet.MsgId);
        offset += 4;

        buffer[offset++] = (byte)packet.Scope;

        if (string.IsNullOrEmpty(packet.RoomId))
        {
            buffer[offset++] = 0;
        }
        else
        {
            int byteCount = Utf8NoBom.GetByteCount(packet.RoomId);
            if (byteCount > byte.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(packet.RoomId), "RoomId byte length cannot exceed 255.");
            }

            buffer[offset++] = (byte)byteCount;
            Utf8NoBom.GetBytes(packet.RoomId, 0, packet.RoomId.Length, buffer, offset);
            offset += byteCount;
        }

        if (packet.PayloadLength > 0 && packet.Payload != null)
        {
            Buffer.BlockCopy(packet.Payload, packet.PayloadOffset, buffer, offset, packet.PayloadLength);
            offset += packet.PayloadLength;
        }

        return offset - startOffset;
    }

    /// <summary>
    /// 尝试把二进制数据解码成 PacketData。
    /// </summary>
    public static bool TryDeserialize(byte[] data, int startOffset, int length, out PacketData packet)
    {
        packet = default;
        if (data == null || length < 10)
        {
            return false;
        }

        int offset = startOffset;
        int end = startOffset + length;
        if (offset + 10 > end)
        {
            return false;
        }

        uint seq = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset, 4));
        offset += 4;

        int msgId = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(offset, 4));
        offset += 4;

        NetScope scope = (NetScope)data[offset++];
        int roomIdLength = data[offset++];
        if (roomIdLength < 0 || offset + roomIdLength > end)
        {
            return false;
        }

        string roomId = roomIdLength > 0 ? Utf8NoBom.GetString(data, offset, roomIdLength) : string.Empty;
        offset += roomIdLength;

        int payloadLength = end - offset;
        packet = new PacketData(seq, msgId, scope, roomId, data, offset, payloadLength);
        return true;
    }
}

/// <summary>
/// 压测工具专用 JSON 序列化封装。
/// </summary>
internal static class LiteJson
{
    /// <summary>
    /// 启用字段序列化的 JSON 配置。
    /// </summary>
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        IncludeFields = true
    };

    /// <summary>
    /// 序列化协议对象。
    /// </summary>
    public static byte[] Serialize<T>(T value)
    {
        return JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions);
    }

    /// <summary>
    /// 从指定缓冲区片段反序列化协议对象。
    /// </summary>
    public static T? Deserialize<T>(byte[] buffer, int offset, int length)
    {
        return JsonSerializer.Deserialize<T>(buffer.AsSpan(offset, length), JsonOptions);
    }
}

/// <summary>
/// 压测工具使用到的协议 Id 常量。
/// </summary>
internal static class MsgIds
{
    public const int C2S_Login = 100;
    public const int S2C_LoginResult = 101;
    public const int C2S_CreateRoom = 200;
    public const int S2C_CreateRoomResult = 201;
    public const int C2S_JoinRoom = 202;
    public const int S2C_JoinRoomResult = 203;
    public const int C2S_LeaveRoom = 204;
    public const int S2C_LeaveRoomResult = 205;
    public const int C2S_RoomSetupReady = 206;
    public const int S2C_RoomSetupResult = 208;
    public const int C2S_SetReady = 303;
    public const int C2S_StartGame = 500;
    public const int S2C_GameStarted = 501;
    public const int C2S_EndGame = 502;
    public const int S2C_GameEnded = 503;
    public const int C2S_SocialMoveReq = 1301;
    public const int C2S_SocialActionReq = 1302;
    public const int C2S_SocialBubbleReq = 1303;
}

/// <summary>
/// 压测工具使用到的房间组件 Id 常量。
/// </summary>
internal static class ComponentIds
{
    public const int RoomSettings = 1;
    public const int SocialRoom = 102;
    public const int ObjectSync = 200;
}

/// <summary>
/// 登录请求。
/// </summary>
internal sealed class C2S_Login
{
    public string AccountId = string.Empty;
    public string ClientVersion = string.Empty;
}

/// <summary>
/// 登录返回。
/// </summary>
internal sealed class S2C_LoginResult
{
    public bool Success = false;
    public string SessionId = string.Empty;
    public bool HasReconnectRoom = false;
    public string Reason = string.Empty;
}

/// <summary>
/// 建房使用的房间配置对象。
/// </summary>
internal sealed class RoomDTO
{
    public string RoomName = string.Empty;
    public int[] ComponentIds = Array.Empty<int>();
    public int MaxMembers;
    public bool EnableReplayRecording = false;
    public string Password = string.Empty;
    public Dictionary<string, string> CustomProperties = new();
}

/// <summary>
/// 建房请求。
/// </summary>
internal sealed class C2S_CreateRoom
{
    public RoomDTO RoomConfig = new();
}

/// <summary>
/// 建房返回。
/// </summary>
internal sealed class S2C_CreateRoomResult
{
    public bool Success = false;
    public string RoomId = string.Empty;
    public int[] ComponentIds = Array.Empty<int>();
    public string Reason = string.Empty;
}

/// <summary>
/// 加入房间请求。
/// </summary>
internal sealed class C2S_JoinRoom
{
    public string RoomId = string.Empty;
    public string Password = string.Empty;
}

/// <summary>
/// 加入房间返回。
/// </summary>
internal sealed class S2C_JoinRoomResult
{
    public bool Success = false;
    public string RoomId = string.Empty;
    public int[] ComponentIds = Array.Empty<int>();
    public string Reason = string.Empty;
}

/// <summary>
/// 进房准备完成确认。
/// </summary>
internal sealed class C2S_RoomSetupReady
{
    public string RoomId = string.Empty;
}

/// <summary>
/// 离开房间请求。
/// </summary>
internal sealed class C2S_LeaveRoom
{
}

/// <summary>
/// 离开房间返回。
/// </summary>
internal sealed class S2C_LeaveRoomResult
{
    public bool Success = false;
}

/// <summary>
/// 进房确认返回。
/// </summary>
internal sealed class S2C_RoomSetupResult
{
    public bool Success = false;
    public string RoomId = string.Empty;
    public string Reason = string.Empty;
}

/// <summary>
/// 准备状态设置请求。
/// </summary>
internal sealed class C2S_SetReady
{
    public bool IsReady;
}

/// <summary>
/// 开局请求。
/// </summary>
internal sealed class C2S_StartGame
{
}

/// <summary>
/// 开局通知。
/// </summary>
internal sealed class S2C_GameStarted
{
    public long StartUnixTime = 0;
}

/// <summary>
/// 结束对局请求。
/// </summary>
internal sealed class C2S_EndGame
{
}

/// <summary>
/// 对局结束通知。
/// </summary>
internal sealed class S2C_GameEnded
{
    public string WinnerSessionId = string.Empty;
    public string ReplayId = string.Empty;
}

/// <summary>
/// 社交房间移动同步载荷。
/// </summary>
internal sealed class SocialMovePayload
{
    public float PosX;
    public float PosY;
    public float PosZ;
    public float VelX;
    public float VelY;
    public float VelZ;
    public float RotY;
}

/// <summary>
/// 社交房间移动载荷序列化器。
/// </summary>
internal static class SocialMoveSerializer
{
    /// <summary>
    /// 把移动载荷编码成定长二进制。
    /// </summary>
    public static byte[] Serialize(SocialMovePayload payload)
    {
        byte[] buffer = new byte[sizeof(float) * 7];
        int offset = 0;
        BinaryPrimitives.WriteSingleLittleEndian(buffer.AsSpan(offset, 4), payload.PosX);
        offset += 4;
        BinaryPrimitives.WriteSingleLittleEndian(buffer.AsSpan(offset, 4), payload.PosY);
        offset += 4;
        BinaryPrimitives.WriteSingleLittleEndian(buffer.AsSpan(offset, 4), payload.PosZ);
        offset += 4;
        BinaryPrimitives.WriteSingleLittleEndian(buffer.AsSpan(offset, 4), payload.VelX);
        offset += 4;
        BinaryPrimitives.WriteSingleLittleEndian(buffer.AsSpan(offset, 4), payload.VelY);
        offset += 4;
        BinaryPrimitives.WriteSingleLittleEndian(buffer.AsSpan(offset, 4), payload.VelZ);
        offset += 4;
        BinaryPrimitives.WriteSingleLittleEndian(buffer.AsSpan(offset, 4), payload.RotY);
        return buffer;
    }
}

/// <summary>
/// 社交动作请求序列化器。
/// </summary>
internal static class SocialActionSerializer
{
    /// <summary>
    /// 把动作 Id 编码成 4 字节整型。
    /// </summary>
    public static byte[] Serialize(int actionId)
    {
        byte[] buffer = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(0, 4), actionId);
        return buffer;
    }
}

/// <summary>
/// 聊天气泡请求序列化器。
/// </summary>
internal static class SocialBubbleSerializer
{
    /// <summary>
    /// 不带 BOM 的 UTF8 编码器。
    /// </summary>
    private static readonly UTF8Encoding Utf8NoBom = new(false);

    /// <summary>
    /// 把聊天文本编码成二进制载荷。
    /// </summary>
    public static byte[] Serialize(string content)
    {
        using var ms = new MemoryStream(64);
        using var writer = new BinaryWriter(ms, Utf8NoBom, leaveOpen: true);
        writer.Write(content ?? string.Empty);
        writer.Flush();
        return ms.ToArray();
    }
}

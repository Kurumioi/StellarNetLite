using System.Buffers.Binary;
using System.Text;
using System.Text.Json;

namespace StellarNetLite.LoadTest;

internal enum NetScope : byte
{
    Global = 0,
    Room = 1
}

internal readonly struct PacketData
{
    public readonly uint Seq;
    public readonly int MsgId;
    public readonly NetScope Scope;
    public readonly string RoomId;
    public readonly byte[] Payload;
    public readonly int PayloadOffset;
    public readonly int PayloadLength;

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

    public PacketData(uint seq, int msgId, NetScope scope, string roomId, byte[] payload, int payloadLength)
        : this(seq, msgId, scope, roomId, payload, 0, payloadLength)
    {
    }
}

internal static class LitePacketCodec
{
    private static readonly UTF8Encoding Utf8NoBom = new(false);

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

internal static class LiteJson
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        IncludeFields = true
    };

    public static byte[] Serialize<T>(T value)
    {
        return JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions);
    }

    public static T? Deserialize<T>(byte[] buffer, int offset, int length)
    {
        return JsonSerializer.Deserialize<T>(buffer.AsSpan(offset, length), JsonOptions);
    }
}

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

internal static class ComponentIds
{
    public const int RoomSettings = 1;
    public const int SocialRoom = 102;
    public const int ObjectSync = 200;
}

internal sealed class C2S_Login
{
    public string AccountId = string.Empty;
    public string ClientVersion = string.Empty;
}

internal sealed class S2C_LoginResult
{
    public bool Success = false;
    public string SessionId = string.Empty;
    public bool HasReconnectRoom = false;
    public string Reason = string.Empty;
}

internal sealed class RoomDTO
{
    public string RoomName = string.Empty;
    public int[] ComponentIds = Array.Empty<int>();
    public int MaxMembers;
    public bool EnableReplayRecording = false;
    public string Password = string.Empty;
    public Dictionary<string, string> CustomProperties = new();
}

internal sealed class C2S_CreateRoom
{
    public RoomDTO RoomConfig = new();
}

internal sealed class S2C_CreateRoomResult
{
    public bool Success = false;
    public string RoomId = string.Empty;
    public int[] ComponentIds = Array.Empty<int>();
    public string Reason = string.Empty;
}

internal sealed class C2S_JoinRoom
{
    public string RoomId = string.Empty;
    public string Password = string.Empty;
}

internal sealed class S2C_JoinRoomResult
{
    public bool Success = false;
    public string RoomId = string.Empty;
    public int[] ComponentIds = Array.Empty<int>();
    public string Reason = string.Empty;
}

internal sealed class C2S_RoomSetupReady
{
    public string RoomId = string.Empty;
}

internal sealed class C2S_LeaveRoom
{
}

internal sealed class S2C_LeaveRoomResult
{
    public bool Success = false;
}

internal sealed class S2C_RoomSetupResult
{
    public bool Success = false;
    public string RoomId = string.Empty;
    public string Reason = string.Empty;
}

internal sealed class C2S_SetReady
{
    public bool IsReady;
}

internal sealed class C2S_StartGame
{
}

internal sealed class S2C_GameStarted
{
    public long StartUnixTime = 0;
}

internal sealed class C2S_EndGame
{
}

internal sealed class S2C_GameEnded
{
    public string WinnerSessionId = string.Empty;
    public string ReplayId = string.Empty;
}

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

internal static class SocialMoveSerializer
{
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

internal static class SocialActionSerializer
{
    public static byte[] Serialize(int actionId)
    {
        byte[] buffer = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(0, 4), actionId);
        return buffer;
    }
}

internal static class SocialBubbleSerializer
{
    private static readonly UTF8Encoding Utf8NoBom = new(false);

    public static byte[] Serialize(string content)
    {
        using var ms = new MemoryStream(64);
        using var writer = new BinaryWriter(ms, Utf8NoBom, leaveOpen: true);
        writer.Write(content ?? string.Empty);
        writer.Flush();
        return ms.ToArray();
    }
}

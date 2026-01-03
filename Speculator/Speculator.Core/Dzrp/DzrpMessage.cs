// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any non-commercial
// purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

namespace Speculator.Core.Dzrp;

/// <summary>
/// Handles DZRP message parsing and serialization.
/// All multi-byte values are little-endian per DZRP specification.
/// </summary>
public class DzrpMessage
{
    public byte SequenceNumber { get; }
    public byte CommandId { get; }
    public byte[] Payload { get; }

    public DzrpMessage(byte seqNum, byte cmdId, byte[] payload)
    {
        SequenceNumber = seqNum;
        CommandId = cmdId;
        Payload = payload ?? Array.Empty<byte>();
    }

    /// <summary>
    /// Read a byte from the payload at the specified offset.
    /// </summary>
    public byte ReadByte(int offset) =>
        offset < Payload.Length ? Payload[offset] : (byte)0;

    /// <summary>
    /// Read a 16-bit word from the payload (little-endian).
    /// </summary>
    public ushort ReadWord(int offset)
    {
        if (offset + 1 >= Payload.Length)
            return 0;
        return (ushort)(Payload[offset] | (Payload[offset + 1] << 8));
    }

    /// <summary>
    /// Read a 32-bit dword from the payload (little-endian).
    /// </summary>
    public uint ReadDword(int offset)
    {
        if (offset + 3 >= Payload.Length)
            return 0;
        return (uint)(Payload[offset] |
                      (Payload[offset + 1] << 8) |
                      (Payload[offset + 2] << 16) |
                      (Payload[offset + 3] << 24));
    }

    /// <summary>
    /// Create a response message with the given sequence number and payload.
    /// </summary>
    public static byte[] CreateResponse(byte seqNum, byte cmdId, params byte[] payload)
    {
        // Format: [4-byte length][seqNum][cmdId][payload]
        var length = 2 + (payload?.Length ?? 0);
        var result = new byte[4 + length];

        // Length (little-endian)
        result[0] = (byte)(length & 0xFF);
        result[1] = (byte)((length >> 8) & 0xFF);
        result[2] = (byte)((length >> 16) & 0xFF);
        result[3] = (byte)((length >> 24) & 0xFF);

        result[4] = seqNum;
        result[5] = cmdId;

        if (payload != null && payload.Length > 0)
            Array.Copy(payload, 0, result, 6, payload.Length);

        return result;
    }

    /// <summary>
    /// Create a notification message (sequence number = 0).
    /// </summary>
    public static byte[] CreateNotification(byte notifId, params byte[] payload)
    {
        // Notifications use sequence number 0
        return CreateResponse(0, notifId, payload);
    }

    /// <summary>
    /// Write a 16-bit word to a list (little-endian).
    /// </summary>
    public static void WriteWord(List<byte> buffer, ushort value)
    {
        buffer.Add((byte)(value & 0xFF));
        buffer.Add((byte)((value >> 8) & 0xFF));
    }

    /// <summary>
    /// Write a 32-bit dword to a list (little-endian).
    /// </summary>
    public static void WriteDword(List<byte> buffer, uint value)
    {
        buffer.Add((byte)(value & 0xFF));
        buffer.Add((byte)((value >> 8) & 0xFF));
        buffer.Add((byte)((value >> 16) & 0xFF));
        buffer.Add((byte)((value >> 24) & 0xFF));
    }

    /// <summary>
    /// Create a simple response with just an error byte (0 = success).
    /// </summary>
    public static byte[] CreateSimpleResponse(byte seqNum, byte cmdId, byte errorCode = 0) =>
        CreateResponse(seqNum, cmdId, errorCode);
}

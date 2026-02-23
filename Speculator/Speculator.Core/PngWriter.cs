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

using System.IO.Compression;
using System.Text;

namespace Speculator.Core;

/// <summary>
/// Minimal PNG encoder for RGBA pixel data. No Avalonia dependency.
/// </summary>
internal static class PngWriter
{
    private static readonly byte[] Signature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
    private static readonly uint[] CrcTable = BuildCrcTable();

    public static void Write(string path, byte[] rgbaPixels, int width, int height)
    {
        using var fs = File.Create(path);
        fs.Write(Signature);

        // IHDR chunk
        var ihdr = new byte[13];
        WriteBE32(ihdr, 0, width);
        WriteBE32(ihdr, 4, height);
        ihdr[8] = 8;  // bit depth
        ihdr[9] = 6;  // color type: RGBA
        WriteChunk(fs, "IHDR", ihdr);

        // IDAT chunk: zlib header + deflated rows + Adler32
        using var compressed = new MemoryStream();
        compressed.WriteByte(0x78); // zlib CMF (deflate, 32K window)
        compressed.WriteByte(0x01); // zlib FLG

        uint a1 = 1, a2 = 0;
        using (var deflate = new DeflateStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
        {
            for (var y = 0; y < height; y++)
            {
                // Filter byte: None
                deflate.WriteByte(0);
                Adler(0, ref a1, ref a2);

                // Row pixels
                var offset = y * width * 4;
                var count = width * 4;
                deflate.Write(rgbaPixels, offset, count);
                for (var i = 0; i < count; i++)
                    Adler(rgbaPixels[offset + i], ref a1, ref a2);
            }
        }

        // Adler32 checksum (big-endian)
        var adler = new byte[4];
        WriteBE32(adler, 0, (int)((a2 << 16) | a1));
        compressed.Write(adler);

        WriteChunk(fs, "IDAT", compressed.ToArray());

        // IEND chunk
        WriteChunk(fs, "IEND", Array.Empty<byte>());
    }

    private static void WriteChunk(Stream s, string type, byte[] data)
    {
        var t = Encoding.ASCII.GetBytes(type);

        // Length (4 bytes, big-endian)
        var len = new byte[4];
        WriteBE32(len, 0, data.Length);
        s.Write(len);

        // Type
        s.Write(t);

        // Data
        if (data.Length > 0)
            s.Write(data);

        // CRC32 of type + data
        var crc = 0xFFFFFFFFu;
        foreach (var b in t)
            crc = (crc >> 8) ^ CrcTable[(crc ^ b) & 0xFF];
        foreach (var b in data)
            crc = (crc >> 8) ^ CrcTable[(crc ^ b) & 0xFF];
        crc ^= 0xFFFFFFFFu;

        var cb = new byte[4];
        WriteBE32(cb, 0, (int)crc);
        s.Write(cb);
    }

    private static void Adler(byte b, ref uint s1, ref uint s2)
    {
        s1 = (s1 + b) % 65521;
        s2 = (s2 + s1) % 65521;
    }

    private static void WriteBE32(byte[] buf, int off, int val)
    {
        buf[off] = (byte)(val >> 24);
        buf[off + 1] = (byte)(val >> 16);
        buf[off + 2] = (byte)(val >> 8);
        buf[off + 3] = (byte)val;
    }

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            var c = n;
            for (var k = 0; k < 8; k++)
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            table[n] = c;
        }
        return table;
    }
}

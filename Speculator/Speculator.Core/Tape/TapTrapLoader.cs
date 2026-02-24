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

using CSharp.Core;
using CSharp.Core.Extensions;

namespace Speculator.Core.Tape;

/// <summary>
/// Instant TAP block loading by intercepting the ROM's LD-BYTES routine.
/// When PC reaches $0556 (LD-BYTES entry point), this loader copies data
/// directly into memory instead of using real-time tape signal emulation.
/// </summary>
public class TapTrapLoader
{
    private const ushort LdBytesAddress = 0x0556;

    private readonly TapBlock128[] m_blocks;
    private int m_blockIndex;

    private TapTrapLoader(TapBlock128[] blocks)
    {
        m_blocks = blocks;
    }

    /// <summary>
    /// Parse a TAP file into structured blocks for trap loading.
    /// </summary>
    public static TapTrapLoader FromFile(FileInfo tapFile)
    {
        var tapeBytes = tapFile.ReadAllBytes();
        var blocks = new List<TapBlock128>();
        var i = 0;
        while (i + 1 < tapeBytes.Length)
        {
            var blockSize = tapeBytes[i] + (tapeBytes[i + 1] << 8);
            i += 2;
            if (blockSize == 0 || i + blockSize > tapeBytes.Length)
                break;
            blocks.Add(new TapBlock128(tapeBytes, i, blockSize));
            i += blockSize;
        }

        Logger.Instance.Info($"TAP trap loader: {blocks.Count} blocks parsed from '{tapFile.Name}'.");
        return new TapTrapLoader(blocks.ToArray());
    }

    /// <summary>
    /// Check if the CPU is at the LD-BYTES entry point and handle the load.
    /// Called from CPU.Step() after instruction execution.
    /// </summary>
    /// <returns>True if the trap was handled (CPU should skip normal flow).</returns>
    public bool TryIntercept(CPU cpu)
    {
        if (cpu.TheRegisters.PC != LdBytesAddress)
            return false;

        // Verify we're executing from ROM (not a custom loader).
        if (!cpu.MainMemory.IsRomArea(LdBytesAddress))
            return false;

        if (m_blockIndex >= m_blocks.Length)
            return false; // No more blocks.

        // LD-BYTES register convention:
        // CF = 1 for LOAD, 0 for VERIFY
        // A = expected block type (0=header, $FF=data)
        // IX = destination address
        // DE = block length

        if (!cpu.TheRegisters.CarryFlag)
        {
            // VERIFY: Set carry flag to indicate success and return.
            cpu.TheRegisters.CarryFlag = true;
            m_blockIndex++;
            cpu.RETN();
            return true;
        }

        var block = m_blocks[m_blockIndex];
        var expectedType = cpu.TheRegisters.Main.A;

        // Check block type matches.
        if (block.TypeByte != expectedType)
        {
            // Type mismatch: signal error.
            cpu.TheRegisters.CarryFlag = false;
            cpu.RETN();
            return true;
        }

        // Copy block data to IX, length DE.
        var destAddr = cpu.TheRegisters.IX;
        var len = Math.Min(block.DataLength, cpu.TheRegisters.Main.DE);
        for (var i = 0; i < len; i++)
            cpu.MainMemory.Poke((ushort)(destAddr + i), block.Data[i]);

        // Set success flags per ROM convention.
        cpu.TheRegisters.CarryFlag = true;
        cpu.TheRegisters.IX = (ushort)(destAddr + len);
        cpu.TheRegisters.Main.DE = (ushort)(cpu.TheRegisters.Main.DE - len);

        m_blockIndex++;
        cpu.RETN();
        Logger.Instance.Info($"TAP trap: loaded block {m_blockIndex}/{m_blocks.Length} ({len} bytes to ${destAddr:X4}).");
        return true;
    }

    /// <summary>
    /// A single TAP block with its type byte and data payload.
    /// </summary>
    private class TapBlock128
    {
        /// <summary>
        /// The flag/type byte (first byte of the block): 0x00=header, 0xFF=data.
        /// </summary>
        public byte TypeByte { get; }

        /// <summary>
        /// The data payload (excluding the type byte and checksum).
        /// </summary>
        public byte[] Data { get; }

        /// <summary>
        /// Length of the data payload.
        /// </summary>
        public int DataLength => Data.Length;

        public TapBlock128(byte[] tapeBytes, int blockStart, int blockSize)
        {
            TypeByte = tapeBytes[blockStart];

            // Data is everything after the type byte, excluding the trailing checksum byte.
            var dataLen = blockSize - 2; // -1 for type byte, -1 for checksum.
            if (dataLen < 0) dataLen = 0;
            Data = new byte[dataLen];
            if (dataLen > 0)
                Array.Copy(tapeBytes, blockStart + 1, Data, 0, dataLen);
        }
    }
}

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

using System.Diagnostics;
using CSharp.Core;
using CSharp.Core.Extensions;
using Speculator.Core.Tape;
using Speculator.Core.Utils;

// ReSharper disable MustUseReturnValue
// ReSharper disable InconsistentlySynchronizedField

namespace Speculator.Core;

public class ZxFileIo
{
    private readonly CPU m_cpu;
    private readonly ZxDisplay m_zxDisplay;
    private readonly TapeLoader m_tapeLoader;

    public enum RomType
    {
        System,
        Game
    }

    public static string[] OpenFilters { get; } = { "*.z80", "*.bin", "*.scr", "*.sna", "*.zip", "*.tap", "*.trd", "*.scl" };
    public static string[] SaveFilters { get; } = { "*.sna" };

    public event EventHandler<RomType> RomLoading;
    public event EventHandler<RomType> RomLoaded;

    /// <summary>
    /// The TAP trap loader, if active. Set by LoadFileInternal for .tap files.
    /// </summary>
    public TapTrapLoader TapTrap { get; private set; }

    /// <summary>
    /// When true, .tap files use real-time signal loading instead of trap loading.
    /// </summary>
    public bool ForceTapRealtime { get; set; }

    public ZxFileIo(CPU cpu, ZxDisplay zxDisplay, TapeLoader tapeLoader)
    {
        m_cpu = cpu;
        m_zxDisplay = zxDisplay;
        m_tapeLoader = tapeLoader;
    }

    public void LoadSystemRom(FileInfo systemRom)
    {
        m_cpu.MainMemory.LoadRom(systemRom);
        m_cpu.ClockSync.Reset();
        RomLoaded?.Invoke(this, RomType.System);
    }

    public void LoadFile(FileInfo fileInfo)
    {
        using var _ = m_cpu.ClockSync.CreatePauser();

        if (!fileInfo.Exists())
            throw new FileNotFoundException(fileInfo.FullName);

        lock (m_cpu.CpuStepLock)
        {
            RomLoading?.Invoke(this, RomType.Game);
            LoadFileInternal(fileInfo);
            RomLoaded?.Invoke(this, RomType.Game);
        }
    }

    private void LoadFileInternal(FileInfo fileInfo)
    {
        switch (fileInfo.Extension.ToLower())
        {
            case ".zip":
            {
                var romFile = ZipExtractor.ExtractZxFile(fileInfo);
                if (romFile?.Exists() == true)
                    LoadFileInternal(romFile);
                return;
            }
            case ".bin":
                LoadBin(fileInfo);
                return;
            case ".sna":
                LoadSna(fileInfo, m_cpu, out var borderAttr);
                m_zxDisplay.BorderAttr = borderAttr;
                return;
            case ".scr":
                LoadScr(fileInfo);
                return;
            case ".z80":
                LoadZ80(fileInfo);
                return;
            case ".tap":
                if (ForceTapRealtime)
                {
                    m_tapeLoader.Load(fileInfo);
                }
                else
                {
                    TapTrap = TapTrapLoader.FromFile(fileInfo);
                    m_cpu.TapTrap = TapTrap;
                    m_tapeLoader.Load(fileInfo); // Also start signal loading as fallback.
                }
                return;
            case ".trd":
            case ".scl":
                TrdosLoader.LoadFile(fileInfo, m_cpu);
                return;
        }
    }
    public static bool IsInstantLoadSupported(FileInfo fileInfo)
    {
        switch (fileInfo.Extension.ToLower())
        {
            case ".bin":
            case ".sna":
            case ".scr":
            case ".z80":
            case ".tap":
            case ".trd":
            case ".scl":
                return true;
            default:
                return false;
        }
    }

    private void LoadZ80(FileInfo file)
    {
        using var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read);
        m_cpu.TheRegisters.Clear();
        m_cpu.TheRegisters.Main.A = (byte)stream.ReadByte();
        m_cpu.TheRegisters.Main.F = (byte)stream.ReadByte();
        m_cpu.TheRegisters.Main.BC = ReadZxWord(stream);
        m_cpu.TheRegisters.Main.HL = ReadZxWord(stream);
        m_cpu.TheRegisters.PC = ReadZxWord(stream);
        m_cpu.TheRegisters.SP = ReadZxWord(stream);
        m_cpu.TheRegisters.I = (byte)stream.ReadByte();
        m_cpu.TheRegisters.R = (byte)(stream.ReadByte() & 0x7F);

        var byte12 = (byte)stream.ReadByte();
        if (byte12 == 0xFF)
            byte12 = 0x01; // Version 1
        if ((byte12 & 0x01) != 0)
            m_cpu.TheRegisters.R |= 0x80;

        if (m_zxDisplay != null)
            m_zxDisplay.BorderAttr = (byte)((byte12 & 0x0e) >> 1);

        var isDataCompressed = (byte12 & 0x20) != 0;

        m_cpu.TheRegisters.Main.DE = ReadZxWord(stream);
        m_cpu.TheRegisters.Alt.BC = ReadZxWord(stream);
        m_cpu.TheRegisters.Alt.DE = ReadZxWord(stream);
        m_cpu.TheRegisters.Alt.HL = ReadZxWord(stream);
        m_cpu.TheRegisters.Alt.A = (byte)stream.ReadByte();
        m_cpu.TheRegisters.Alt.F = (byte)stream.ReadByte();
        m_cpu.TheRegisters.IY = ReadZxWord(stream);
        m_cpu.TheRegisters.IX = ReadZxWord(stream);
        m_cpu.TheRegisters.IFF1 = stream.ReadByte() != 0;
        m_cpu.TheRegisters.IFF2 = stream.ReadByte() != 0;
        m_cpu.TheRegisters.IM = (byte)(stream.ReadByte() & 0x03);

        Debug.Assert(stream.Position == 30);

        var isVersion1 = m_cpu.TheRegisters.PC != 0x0000;
        if (isVersion1)
        {
            // Version 1 (48K only).
            var bytesToRead = (int)(stream.Length - stream.Position);
            var data = ReadBytes(stream, bytesToRead);
            if (isDataCompressed)
            {
                Decompress(data);

                // Remove trailer.
                data.RemoveRange(data.Count - 4, 4);
            }

            Debug.Assert(data.Count <= 48 * 1024);
            m_cpu.MainMemory.LoadData(data, 0x4000);
            return;
        }

        // Version 2/3: Read extended header.
        int extendedHeaderLength = ReadZxWord(stream);
        var extendedHeader = new byte[extendedHeaderLength];
        stream.Read(extendedHeader, 0, extendedHeaderLength);

        m_cpu.TheRegisters.PC = (ushort)((extendedHeader[1] << 8) + extendedHeader[0]);
        var hardwareMode = extendedHeader[2];
        var is128K = hardwareMode >= 3 && hardwareMode <= 4;

        if (!is128K && hardwareMode > 1)
        {
            // Unsupported model (SamRam, etc.).
            ReportHardwareMode(hardwareMode);
            return;
        }

        // For 128K files, read port $7FFD state from extended header.
        byte port7FFD = 0;
        if (is128K && extendedHeaderLength >= 35)
            port7FFD = extendedHeader[35 - 32]; // Byte 35 = offset 3 from header start.

        if (is128K)
        {
            // Initialize 128K banking if not already done by ROM.
            if (!m_cpu.MainMemory.Is128K)
                m_cpu.MainMemory.Init128K();
        }

        // Read blocks.
        while (stream.Position < stream.Length)
        {
            var blockSize = ReadZxWord(stream);
            var pageNumber = stream.ReadByte();

            var isUncompressed = blockSize == 0xFFFF;
            if (isUncompressed)
                blockSize = 0x4000;

            var data = ReadBytes(stream, blockSize);
            if (!isUncompressed)
                Decompress(data);

            if (is128K)
            {
                // 128K page mapping: page 3=bank 0, page 4=bank 1, ..., page 10=bank 7.
                var bankNumber = pageNumber - 3;
                if (bankNumber >= 0 && bankNumber <= 7)
                {
                    m_cpu.MainMemory.LoadBank(bankNumber, data.ToArray());
                }
            }
            else
            {
                // 48K page mapping.
                switch (pageNumber)
                {
                    case 0: m_cpu.MainMemory.LoadData(data, 0x0000);
                        break;
                    case 4: m_cpu.MainMemory.LoadData(data, 0x8000);
                        break;
                    case 5: m_cpu.MainMemory.LoadData(data, 0xC000);
                        break;
                    case 8: m_cpu.MainMemory.LoadData(data, 0x4000);
                        break;
                }
            }
        }

        if (is128K)
        {
            // Apply port $7FFD state: select bank at $C000 and ROM.
            var bankPage = port7FFD & 0x07;
            var romPage = (port7FFD & 0x10) != 0 ? 1 : 0;
            var screenPage = (port7FFD & 0x08) != 0 ? 7 : 5;
            m_cpu.MainMemory.SwitchBank(bankPage);
            m_cpu.MainMemory.SwitchRom(romPage);
            m_cpu.MainMemory.SetScreenPage(screenPage);
            if ((port7FFD & 0x20) != 0)
                m_cpu.MainMemory.LockPaging();

            // Ensure fixed banks (5 at $4000, 2 at $8000) are in the flat view.
            m_cpu.MainMemory.SyncFixedBanks();
        }
    }

    private static List<byte> ReadBytes(Stream stream, int byteCount)
    {
        var data = new List<byte>(byteCount);
        for (var i = 0; i < byteCount; i++)
            data.Add((byte)stream.ReadByte());
        return data;
    }

    private static void Decompress(List<byte> data)
    {
        var offset = 0;
        while (offset < data.Count - 4)
        {
            if (data[offset] == 0xED && data[offset + 1] == 0xED)
            {
                var count = data[offset + 2];
                var b = data[offset + 3];

                data.RemoveRange(offset, 4);
                for (var i = 0; i < count; i++)
                    data.Insert(offset, b);

                offset += count;
            }
            else
            {
                offset++;
            }
        }
    }

    private static bool ReportHardwareMode(int hardwareMode)
    {
        var modeDescription = hardwareMode switch
        {
            0 => "48K Spectrum",
            1 => "48K Spectrum + Interface 1",
            2 => "SamRam",
            3 => "128K Spectrum",
            4 => "128K Spectrum + Interface 1",
            _ => $"Unknown hardware mode: {hardwareMode}"
        };

        var isSupported = hardwareMode <= 4 && hardwareMode != 2;
        if (isSupported)
            return true;

        Logger.Instance.Warn($"Unsupported model: {modeDescription}");
        return false;
    }

    private void LoadScr(FileInfo file) =>
        m_cpu.MainMemory.LoadData(file.ReadAllBytes(), ZxDisplay.ScreenBase);

    public void SaveFile(FileInfo file)
    {
        using var _ = m_cpu.ClockSync.CreatePauser();
        lock (m_cpu.CpuStepLock)
        {
            switch (file.Extension)
            {
                case ".sna":
                    SaveSna(file);
                    return;
            }
        }
    }

    public static void LoadSna(FileInfo file, CPU cpu, out byte borderAttr)
    {
        using var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read);
        var fileSize = stream.Length;
        var is128K = fileSize > 49179;

        cpu.TheRegisters.Clear();
        cpu.TheRegisters.I = (byte)stream.ReadByte();
        cpu.TheRegisters.Alt.HL = ReadZxWord(stream);
        cpu.TheRegisters.Alt.DE = ReadZxWord(stream);
        cpu.TheRegisters.Alt.BC = ReadZxWord(stream);
        cpu.TheRegisters.Alt.AF = ReadZxWord(stream);
        cpu.TheRegisters.Main.HL = ReadZxWord(stream);
        cpu.TheRegisters.Main.DE = ReadZxWord(stream);
        cpu.TheRegisters.Main.BC = ReadZxWord(stream);
        cpu.TheRegisters.IY = ReadZxWord(stream);
        cpu.TheRegisters.IX = ReadZxWord(stream);
        cpu.TheRegisters.IFF1 = cpu.TheRegisters.IFF2 = stream.ReadByte() != 0;
        cpu.TheRegisters.R = (byte)stream.ReadByte();
        cpu.TheRegisters.Main.AF = ReadZxWord(stream);
        cpu.TheRegisters.SP = ReadZxWord(stream);
        cpu.TheRegisters.IM = (byte)stream.ReadByte();
        borderAttr = (byte)stream.ReadByte();

        // Read 48KB of RAM ($4000-$FFFF).
        for (var i = 16384; i <= 65535; i++)
            cpu.MainMemory.Poke((ushort)i, (byte)stream.ReadByte());

        if (!is128K)
        {
            cpu.RETN();
            return;
        }

        // 128K SNA extension: PC, port $7FFD, TR-DOS flag, extra banks.
        cpu.TheRegisters.PC = ReadZxWord(stream);
        var port7FFD = (byte)stream.ReadByte();
        stream.ReadByte(); // TR-DOS ROM paged flag (ignored).

        if (!cpu.MainMemory.Is128K)
            cpu.MainMemory.Init128K();

        // Determine which banks are already in the 48K image.
        // The 48K image contains: bank 5 at $4000, bank 2 at $8000, bank N at $C000.
        var pagedBank = port7FFD & 0x07;

        // Copy the 48K image regions into their bank arrays.
        cpu.MainMemory.LoadBank(5, cpu.MainMemory.Data[0x4000..0x8000]);
        cpu.MainMemory.LoadBank(2, cpu.MainMemory.Data[0x8000..0xC000]);
        cpu.MainMemory.LoadBank(pagedBank, cpu.MainMemory.Data[0xC000..0x10000]);

        // Read the remaining 5 banks (those not already loaded).
        for (var bank = 0; bank < 8; bank++)
        {
            if (bank == 5 || bank == 2 || bank == pagedBank)
                continue;
            var bankData = new byte[0x4000];
            stream.Read(bankData, 0, 0x4000);
            cpu.MainMemory.LoadBank(bank, bankData);
        }

        // Apply port $7FFD state.
        var romPage = (port7FFD & 0x10) != 0 ? 1 : 0;
        var screenPage = (port7FFD & 0x08) != 0 ? 7 : 5;
        cpu.MainMemory.SwitchBank(pagedBank);
        cpu.MainMemory.SwitchRom(romPage);
        cpu.MainMemory.SetScreenPage(screenPage);
        if ((port7FFD & 0x20) != 0)
            cpu.MainMemory.LockPaging();
        cpu.MainMemory.SyncFixedBanks();
    }

    private static ushort ReadZxWord(Stream stream) =>
        (ushort)(stream.ReadByte() + (stream.ReadByte() << 8));

    private static void WriteSnaWord(Stream stream, int n)
    {
        stream.WriteByte((byte)(n & 0x00FF));
        stream.WriteByte((byte)(n >> 8 & 0xFF));
    }

    /// <summary>
    /// Create and write a .sna system snapshot to file.
    /// </summary>
    private void SaveSna(FileInfo file)
    {
        using var stream = new FileStream(file.FullName, FileMode.Create, FileAccess.Write);
        WriteSnaToStream(stream);
    }

    public void WriteSnaToStream(Stream stream)
    {
        try
        {
            m_cpu.TheRegisters.SP -= 2;
            m_cpu.MainMemory.Poke(m_cpu.TheRegisters.SP, m_cpu.TheRegisters.PC);

            stream.WriteByte(m_cpu.TheRegisters.I);
            WriteSnaWord(stream, m_cpu.TheRegisters.Alt.HL);
            WriteSnaWord(stream, m_cpu.TheRegisters.Alt.DE);
            WriteSnaWord(stream, m_cpu.TheRegisters.Alt.BC);
            WriteSnaWord(stream, m_cpu.TheRegisters.Alt.AF);
            WriteSnaWord(stream, m_cpu.TheRegisters.Main.HL);
            WriteSnaWord(stream, m_cpu.TheRegisters.Main.DE);
            WriteSnaWord(stream, m_cpu.TheRegisters.Main.BC);
            WriteSnaWord(stream, m_cpu.TheRegisters.IY);
            WriteSnaWord(stream, m_cpu.TheRegisters.IX);
            stream.WriteByte((byte)(m_cpu.TheRegisters.IFF2 ? 0x04 : 0x00));
            stream.WriteByte(m_cpu.TheRegisters.R);
            WriteSnaWord(stream, m_cpu.TheRegisters.Main.AF);
            WriteSnaWord(stream, m_cpu.TheRegisters.SP);
            stream.WriteByte(m_cpu.TheRegisters.IM);
            stream.WriteByte(m_zxDisplay.BorderAttr);
            for (var i = 16384; i <= 65535; i++)
                stream.WriteByte(m_cpu.MainMemory.Peek((ushort)i));
        }
        finally
        {
            m_cpu.TheRegisters.SP += 2;
        }
    }

    private void LoadBin(FileInfo file)
    {
        m_cpu.MainMemory.LoadData(file.ReadAllBytes(), 0x8000);
        m_cpu.TheRegisters.PC = 0x8000;
    }
}

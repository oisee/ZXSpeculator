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
using System.Runtime.CompilerServices;
using System.Text;
using CSharp.Core;
using CSharp.Core.Extensions;

namespace Speculator.Core;

public class Memory
{
    private int m_romSize;

    // 128K banking state.
    private byte[][] m_banks;
    private int m_currentBank3Page;
    private int m_currentRomPage;
    private bool m_pagingLocked;
    private byte[] m_rom128;
    private int m_screenPage = 5;

    /// <summary>
    /// Raised when a large chunk of data is loaded from an external source (I.e. Disk).
    /// </summary>
    public event EventHandler DataLoaded;

    public byte[] Data { get; } = new byte[0x10000];

    /// <summary>
    /// True when running in 128K mode with banked memory.
    /// </summary>
    public bool Is128K => m_banks != null;

    /// <summary>
    /// The currently selected screen page (5=normal, 7=shadow).
    /// </summary>
    public int ScreenPage => m_screenPage;

    /// <summary>
    /// True when port $7FFD bit 5 has locked paging until next reset.
    /// </summary>
    public bool IsPagingLocked => m_pagingLocked;

    /// <summary>
    /// The RAM page currently mapped at $C000.
    /// </summary>
    public int CurrentBank3Page => m_currentBank3Page;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public byte Poke(ushort addr, byte value)
    {
        if (IsRomArea(addr))
            return Data[addr]; // Can't write to ROM.

        // In 128K mode, keep bank arrays in sync for the paged region.
        if (m_banks != null && addr >= 0xC000)
            m_banks[m_currentBank3Page][addr - 0xC000] = value;

        Data[addr] = value;
        return value;
    }

    public void Poke(ushort addr, ushort v)
    {
        Poke(addr, (byte)(v & 0x00ff));
        Poke((ushort)(addr + 1), (byte)(v >> 8));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public byte Peek(ushort addr) => Data[addr];

    public ushort PeekWord(ushort addr) =>
        (ushort)(Data[(ushort)(addr + 1)] << 8 | Data[addr]);

    public string ReadAsHexString(ushort addr, ushort byteCount, bool wantSpaces = false)
    {
        var result = new StringBuilder();
        for (var i = 0; i < byteCount && addr + i <= 0xffff; i++)
        {
            result.Append($"{Peek((ushort)(addr + i)):X2}");
            if (wantSpaces)
                result.Append(' ');
        }

        return result.ToString().Trim();
    }

    public void LoadRom(FileInfo systemRom)
    {
        Logger.Instance.Info($"Loading ROM '{systemRom}'.");
        Debug.Assert(systemRom.Exists(), "ROM file does not exist: " + systemRom);

        var romBytes = systemRom.ReadAllBytes();
        Logger.Instance.Info($"ROM size: {romBytes.Length} bytes.");

        if (romBytes.Length == 0x8000)
        {
            // 32KB ROM: 128K mode (ROM0=128K editor, ROM1=48K BASIC).
            Init128K();
            m_rom128 = romBytes;
            SwitchRom(1); // Start with 48K BASIC ROM.
            return;
        }

        if (romBytes.Length > 0xffff)
        {
            Logger.Instance.Error("ROM is too large to fit in memory.");
            return;
        }

        Array.Clear(Data);
        m_romSize = romBytes.Length;
        LoadData(romBytes, 0x0000);
    }

    public bool IsRomArea(ushort addr) => addr < m_romSize;

    /// <summary>
    /// Bulk load data into memory (such as from disk).
    /// </summary>
    public void LoadData(IList<byte> data, ushort addr)
    {
        data.CopyTo(Data, addr);
        DataLoaded?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Initialize 128K banking: allocate 8 x 16KB RAM banks.
    /// Default layout: ROM1 at $0000, bank 5 at $4000, bank 2 at $8000, bank 0 at $C000.
    /// </summary>
    public void Init128K()
    {
        m_banks = new byte[8][];
        for (var i = 0; i < 8; i++)
            m_banks[i] = new byte[0x4000];

        m_currentBank3Page = 0;
        m_currentRomPage = 1;
        m_pagingLocked = false;
        m_screenPage = 5;

        // Copy current $C000 region into bank 0 (in case data was pre-loaded).
        Array.Copy(Data, 0xC000, m_banks[0], 0, 0x4000);
    }

    /// <summary>
    /// Switch the RAM page mapped at $C000-$FFFF.
    /// </summary>
    public void SwitchBank(int page)
    {
        if (m_banks == null || page == m_currentBank3Page)
            return;

        // Save current $C000 region to its bank.
        Array.Copy(Data, 0xC000, m_banks[m_currentBank3Page], 0, 0x4000);

        // Load new bank into $C000.
        Array.Copy(m_banks[page], 0, Data, 0xC000, 0x4000);
        m_currentBank3Page = page;
    }

    /// <summary>
    /// Switch the ROM mapped at $0000-$3FFF.
    /// </summary>
    public void SwitchRom(int rom)
    {
        if (m_rom128 == null)
            return;

        m_currentRomPage = rom;
        var offset = rom * 0x4000;
        Array.Copy(m_rom128, offset, Data, 0, 0x4000);
        m_romSize = 0x4000;
    }

    /// <summary>
    /// Set the active screen page (5=normal, 7=shadow).
    /// </summary>
    public void SetScreenPage(int page)
    {
        m_screenPage = page;
    }

    /// <summary>
    /// Lock paging until next reset (port $7FFD bit 5).
    /// </summary>
    public void LockPaging()
    {
        m_pagingLocked = true;
    }

    /// <summary>
    /// Read a byte from the active screen page's data.
    /// For screen page 5 (normal), data lives in-place at $4000.
    /// For screen page 7 (shadow), data is read from bank 7.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public byte GetScreenByte(int offset)
    {
        if (m_screenPage == 7 && m_banks != null)
            return m_banks[7][offset];
        return Data[0x4000 + offset];
    }

    /// <summary>
    /// Load data directly into a 128K RAM bank (used by file loaders).
    /// </summary>
    public void LoadBank(int page, byte[] data)
    {
        if (m_banks == null)
            return;

        Array.Copy(data, 0, m_banks[page], 0, Math.Min(data.Length, 0x4000));

        // If this bank is currently paged in, also update the flat view.
        if (page == m_currentBank3Page)
            Array.Copy(m_banks[page], 0, Data, 0xC000, 0x4000);
        else if (page == 5)
            Array.Copy(m_banks[5], 0, Data, 0x4000, 0x4000);
        else if (page == 2)
            Array.Copy(m_banks[2], 0, Data, 0x8000, 0x4000);
    }

    /// <summary>
    /// Sync the flat Data[] view of banks 5 and 2 from their bank arrays.
    /// Called after loading all banks to ensure the fixed-address regions are correct.
    /// </summary>
    public void SyncFixedBanks()
    {
        if (m_banks == null)
            return;
        Array.Copy(m_banks[5], 0, Data, 0x4000, 0x4000);
        Array.Copy(m_banks[2], 0, Data, 0x8000, 0x4000);
        Array.Copy(m_banks[m_currentBank3Page], 0, Data, 0xC000, 0x4000);
    }

    /// <summary>
    /// Reset paging lock (called on machine reset).
    /// </summary>
    public void ResetPaging()
    {
        m_pagingLocked = false;
    }
}

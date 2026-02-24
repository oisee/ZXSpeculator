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

using System.Text;
using CSharp.Core;
using CSharp.Core.Extensions;

namespace Speculator.Core;

/// <summary>
/// Parses TR-DOS (.trd) and SCL (.scl) disk image formats and loads files into memory.
/// Uses a catalog-based approach: parse the file catalog, find CODE/BASIC files,
/// and load them directly into memory at the specified address.
/// </summary>
public static class TrdosLoader
{
    /// <summary>
    /// A file entry from the TR-DOS disk catalog.
    /// </summary>
    private class CatalogEntry
    {
        public string Name { get; init; }
        public char Extension { get; init; }
        public ushort StartAddress { get; init; }
        public ushort Length { get; init; }
        public byte[] Data { get; init; }

        public bool IsCode => Extension == 'C';
        public bool IsBasic => Extension == 'B';
        public bool IsDeleted => Name.Length > 0 && Name[0] == '\x01';
    }

    /// <summary>
    /// Load a .trd or .scl file, extract the first CODE or BASIC file, and load it into memory.
    /// </summary>
    public static void LoadFile(FileInfo file, CPU cpu)
    {
        var entries = file.Extension.ToLower() == ".scl"
            ? ParseScl(file)
            : ParseTrd(file);

        if (entries.Count == 0)
        {
            Logger.Instance.Warn($"TR-DOS: No files found in '{file.Name}'.");
            return;
        }

        // Log catalog.
        Logger.Instance.Info($"TR-DOS: {entries.Count} file(s) in '{file.Name}':");
        foreach (var entry in entries)
            Logger.Instance.Info($"  {entry.Name,-8}.{entry.Extension}  ${entry.StartAddress:X4}  {entry.Length} bytes");

        // Find first CODE file, then BASIC, then first file.
        var target = entries.FirstOrDefault(e => e.IsCode && !e.IsDeleted)
                     ?? entries.FirstOrDefault(e => e.IsBasic && !e.IsDeleted)
                     ?? entries.FirstOrDefault(e => !e.IsDeleted);

        if (target == null)
        {
            Logger.Instance.Warn("TR-DOS: No loadable file found in catalog.");
            return;
        }

        Logger.Instance.Info($"TR-DOS: Loading '{target.Name}.{target.Extension}' ({target.Length} bytes to ${target.StartAddress:X4}).");

        if (target.IsCode)
        {
            // Load CODE file directly at its start address.
            for (var i = 0; i < target.Length && i < target.Data.Length; i++)
                cpu.MainMemory.Poke((ushort)(target.StartAddress + i), target.Data[i]);
            cpu.TheRegisters.PC = target.StartAddress;
        }
        else if (target.IsBasic)
        {
            // Load BASIC program at $5CCB (PROG system variable area).
            const ushort progAddr = 0x5CCB;
            for (var i = 0; i < target.Length && i < target.Data.Length; i++)
                cpu.MainMemory.Poke((ushort)(progAddr + i), target.Data[i]);
        }
        else
        {
            // Generic: load at start address.
            for (var i = 0; i < target.Length && i < target.Data.Length; i++)
                cpu.MainMemory.Poke((ushort)(target.StartAddress + i), target.Data[i]);
        }
    }

    /// <summary>
    /// Parse a TRD disk image (640KB raw, 16 sectors x 256 bytes x 2 sides x 80 tracks).
    /// The first 8 sectors (sector 0 of track 0) contain the file catalog.
    /// </summary>
    private static List<CatalogEntry> ParseTrd(FileInfo file)
    {
        var disk = file.ReadAllBytes();
        var entries = new List<CatalogEntry>();

        // Catalog is in the first 8 sectors (2048 bytes), 16 entries per sector.
        // Each entry is 16 bytes.
        const int entriesPerSector = 16;
        const int sectorSize = 256;
        const int catalogSectors = 8;

        for (var sector = 0; sector < catalogSectors; sector++)
        {
            for (var entry = 0; entry < entriesPerSector; entry++)
            {
                var offset = sector * sectorSize + entry * 16;
                if (offset + 16 > disk.Length)
                    break;

                // First byte 0 = end of catalog.
                if (disk[offset] == 0)
                    return entries;

                var name = Encoding.ASCII.GetString(disk, offset, 8).TrimEnd();
                var ext = (char)disk[offset + 8];
                var startAddr = (ushort)(disk[offset + 9] | (disk[offset + 10] << 8));
                var length = (ushort)(disk[offset + 11] | (disk[offset + 12] << 8));
                var sectorCount = disk[offset + 13];
                var firstSector = disk[offset + 14];
                var firstTrack = disk[offset + 15];

                // Calculate data offset in disk image.
                var dataOffset = (firstTrack * 16 + firstSector) * sectorSize;
                var dataLen = sectorCount * sectorSize;

                byte[] data;
                if (dataOffset + dataLen <= disk.Length)
                {
                    data = new byte[length];
                    Array.Copy(disk, dataOffset, data, 0, Math.Min(length, dataLen));
                }
                else
                {
                    data = Array.Empty<byte>();
                }

                entries.Add(new CatalogEntry
                {
                    Name = name,
                    Extension = ext,
                    StartAddress = startAddr,
                    Length = length,
                    Data = data
                });
            }
        }

        return entries;
    }

    /// <summary>
    /// Parse an SCL disk image.
    /// Format: "SINCLAIR" (8 bytes) + file count (1 byte) + catalog entries + file data.
    /// Each catalog entry: name(8) + ext(1) + start(2) + length(2) + sectors(1) = 14 bytes.
    /// </summary>
    private static List<CatalogEntry> ParseScl(FileInfo file)
    {
        var disk = file.ReadAllBytes();
        var entries = new List<CatalogEntry>();

        // Validate header.
        if (disk.Length < 9)
            return entries;
        var header = Encoding.ASCII.GetString(disk, 0, 8);
        if (header != "SINCLAIR")
        {
            Logger.Instance.Warn("SCL: Invalid header (expected 'SINCLAIR').");
            return entries;
        }

        var fileCount = disk[8];
        var catalogOffset = 9;
        var dataOffset = 9 + fileCount * 14;

        for (var i = 0; i < fileCount; i++)
        {
            var entryOffset = catalogOffset + i * 14;
            if (entryOffset + 14 > disk.Length)
                break;

            var name = Encoding.ASCII.GetString(disk, entryOffset, 8).TrimEnd();
            var ext = (char)disk[entryOffset + 8];
            var startAddr = (ushort)(disk[entryOffset + 9] | (disk[entryOffset + 10] << 8));
            var length = (ushort)(disk[entryOffset + 11] | (disk[entryOffset + 12] << 8));
            var sectorCount = disk[entryOffset + 13];

            var fileDataLen = sectorCount * 256;
            byte[] data;
            if (dataOffset + fileDataLen <= disk.Length)
            {
                data = new byte[length];
                Array.Copy(disk, dataOffset, data, 0, Math.Min(length, fileDataLen));
            }
            else
            {
                data = Array.Empty<byte>();
            }

            entries.Add(new CatalogEntry
            {
                Name = name,
                Extension = ext,
                StartAddress = startAddr,
                Length = length,
                Data = data
            });

            dataOffset += fileDataLen;
        }

        return entries;
    }
}

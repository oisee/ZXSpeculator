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
/// DZRP command identifiers per DeZog specification.
/// </summary>
public static class DzrpCommands
{
    // Core commands
    public const byte CMD_INIT = 1;
    public const byte CMD_CLOSE = 2;
    public const byte CMD_GET_REGISTERS = 3;
    public const byte CMD_SET_REGISTER = 4;
    public const byte CMD_WRITE_BANK = 5;
    public const byte CMD_CONTINUE = 6;
    public const byte CMD_PAUSE = 7;
    public const byte CMD_READ_MEM = 8;
    public const byte CMD_WRITE_MEM = 9;
    public const byte CMD_SET_SLOT = 10;

    // Breakpoint commands
    public const byte CMD_ADD_BREAKPOINT = 40;
    public const byte CMD_REMOVE_BREAKPOINT = 41;

    // Notification ID (sequence number = 0)
    public const byte NTF_PAUSE = 1;
}

/// <summary>
/// Pause notification reason codes.
/// </summary>
public enum PauseReason : byte
{
    ManualBreak = 1,
    BreakpointHit = 2,
    Watchpoint = 3,
    AssertionFailed = 4,
    OtherReason = 5
}

/// <summary>
/// Register IDs as defined in DZRP specification.
/// </summary>
public enum RegisterId : byte
{
    PC = 0,
    SP = 1,
    AF = 2,
    BC = 3,
    DE = 4,
    HL = 5,
    IX = 6,
    IY = 7,
    AF2 = 8,
    BC2 = 9,
    DE2 = 10,
    HL2 = 11,
    I = 12,
    R = 13,
    IM = 14
}

/// <summary>
/// Machine types for CMD_INIT response.
/// </summary>
public enum MachineType : byte
{
    ZX16K = 1,
    ZX48K = 3,
    ZX128K = 4,
    ZXNext = 7
}

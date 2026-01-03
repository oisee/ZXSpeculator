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
/// Interface bridging DZRP protocol to emulator internals.
/// Abstracts CPU, registers, memory, and breakpoint access.
/// </summary>
public interface IDzrpDebugBridge
{
    /// <summary>
    /// True if the CPU is currently paused.
    /// </summary>
    bool IsPaused { get; }

    /// <summary>
    /// Pause CPU execution.
    /// </summary>
    void Pause();

    /// <summary>
    /// Continue CPU execution, optionally with temporary breakpoints.
    /// </summary>
    void Continue(ushort[] tempBreakpoints = null);

    /// <summary>
    /// Execute a single instruction.
    /// </summary>
    void StepInto();

    /// <summary>
    /// Get all Z80 registers packed per DZRP specification.
    /// </summary>
    byte[] GetAllRegisters();

    /// <summary>
    /// Set a specific register value.
    /// </summary>
    void SetRegister(RegisterId regId, ushort value);

    /// <summary>
    /// Read a block of memory.
    /// </summary>
    byte[] ReadMemory(ushort address, ushort length);

    /// <summary>
    /// Write data to memory.
    /// </summary>
    void WriteMemory(ushort address, byte[] data);

    /// <summary>
    /// Add a breakpoint at the specified address.
    /// </summary>
    /// <returns>Unique breakpoint ID.</returns>
    int AddBreakpoint(ushort address);

    /// <summary>
    /// Remove a breakpoint by its ID.
    /// </summary>
    void RemoveBreakpoint(int breakpointId);

    /// <summary>
    /// Raised when execution pauses (breakpoint hit, manual pause, etc.).
    /// </summary>
    event EventHandler<PauseEventArgs> Paused;
}

/// <summary>
/// Event arguments for pause notifications.
/// </summary>
public class PauseEventArgs : EventArgs
{
    public PauseReason Reason { get; }
    public ushort Address { get; }

    public PauseEventArgs(PauseReason reason, ushort address = 0)
    {
        Reason = reason;
        Address = address;
    }
}

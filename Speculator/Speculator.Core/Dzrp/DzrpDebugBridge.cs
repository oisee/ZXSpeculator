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
/// Bridges DZRP protocol to ZX Speculator's CPU, registers, and memory.
/// Thread-safe access is ensured via CPU.CpuStepLock.
/// </summary>
public class DzrpDebugBridge : IDzrpDebugBridge
{
    private readonly CPU m_cpu;
    private readonly Dictionary<int, DzrpBreakpoint> m_breakpoints = new();
    private readonly List<DzrpBreakpoint> m_tempBreakpoints = new();

    public event EventHandler<PauseEventArgs> Paused;

    public bool IsPaused => m_cpu.IsDebuggerActive;

    public DzrpDebugBridge(CPU cpu)
    {
        m_cpu = cpu;
    }

    public void Pause()
    {
        m_cpu.IsDebuggerActive = true;
        Paused?.Invoke(this, new PauseEventArgs(PauseReason.ManualBreak, m_cpu.TheRegisters.PC));
    }

    public void Continue(ushort[] tempBreakpoints = null)
    {
        // Clear existing temporary breakpoints
        ClearTempBreakpoints();

        // Add new temporary breakpoints if specified
        if (tempBreakpoints != null)
        {
            foreach (var addr in tempBreakpoints)
            {
                var bp = new DzrpBreakpoint(m_cpu, addr, isTemporary: true);
                bp.Hit += OnTempBreakpointHit;
                bp.Enable();
                m_tempBreakpoints.Add(bp);
            }
        }

        m_cpu.IsDebuggerActive = false;
    }

    public void StepInto()
    {
        // Allow one instruction to execute
        m_cpu.DebuggerStep();
    }

    public byte[] GetAllRegisters()
    {
        // Pack registers per DZRP spec (28 bytes):
        // PC(2), SP(2), AF(2), BC(2), DE(2), HL(2),
        // IX(2), IY(2), AF'(2), BC'(2), DE'(2), HL'(2),
        // I(1), R(1), IM(1), reserved(1)
        lock (m_cpu.CpuStepLock)
        {
            var regs = m_cpu.TheRegisters;
            var buffer = new List<byte>(28);

            DzrpMessage.WriteWord(buffer, regs.PC);
            DzrpMessage.WriteWord(buffer, regs.SP);
            DzrpMessage.WriteWord(buffer, regs.Main.AF);
            DzrpMessage.WriteWord(buffer, regs.Main.BC);
            DzrpMessage.WriteWord(buffer, regs.Main.DE);
            DzrpMessage.WriteWord(buffer, regs.Main.HL);
            DzrpMessage.WriteWord(buffer, regs.IX);
            DzrpMessage.WriteWord(buffer, regs.IY);
            DzrpMessage.WriteWord(buffer, regs.Alt.AF);
            DzrpMessage.WriteWord(buffer, regs.Alt.BC);
            DzrpMessage.WriteWord(buffer, regs.Alt.DE);
            DzrpMessage.WriteWord(buffer, regs.Alt.HL);
            buffer.Add(regs.I);
            buffer.Add(regs.R);
            buffer.Add(regs.IM);
            buffer.Add(0); // Reserved

            return buffer.ToArray();
        }
    }

    public void SetRegister(RegisterId regId, ushort value)
    {
        lock (m_cpu.CpuStepLock)
        {
            var regs = m_cpu.TheRegisters;
            switch (regId)
            {
                case RegisterId.PC:
                    regs.PC = value;
                    break;
                case RegisterId.SP:
                    regs.SP = value;
                    break;
                case RegisterId.AF:
                    regs.Main.AF = value;
                    break;
                case RegisterId.BC:
                    regs.Main.BC = value;
                    break;
                case RegisterId.DE:
                    regs.Main.DE = value;
                    break;
                case RegisterId.HL:
                    regs.Main.HL = value;
                    break;
                case RegisterId.IX:
                    regs.IX = value;
                    break;
                case RegisterId.IY:
                    regs.IY = value;
                    break;
                case RegisterId.AF2:
                    regs.Alt.AF = value;
                    break;
                case RegisterId.BC2:
                    regs.Alt.BC = value;
                    break;
                case RegisterId.DE2:
                    regs.Alt.DE = value;
                    break;
                case RegisterId.HL2:
                    regs.Alt.HL = value;
                    break;
                case RegisterId.I:
                    regs.I = (byte)value;
                    break;
                case RegisterId.R:
                    regs.R = (byte)value;
                    break;
                case RegisterId.IM:
                    regs.IM = (byte)value;
                    break;
            }
        }
    }

    public byte[] ReadMemory(ushort address, ushort length)
    {
        lock (m_cpu.CpuStepLock)
        {
            var result = new byte[length];
            for (var i = 0; i < length; i++)
                result[i] = m_cpu.MainMemory.Peek((ushort)(address + i));
            return result;
        }
    }

    public void WriteMemory(ushort address, byte[] data)
    {
        lock (m_cpu.CpuStepLock)
        {
            for (var i = 0; i < data.Length; i++)
                m_cpu.MainMemory.Poke((ushort)(address + i), data[i]);
        }
    }

    public int AddBreakpoint(ushort address)
    {
        var bp = new DzrpBreakpoint(m_cpu, address);
        bp.Hit += OnBreakpointHit;
        bp.Enable();
        m_breakpoints[bp.Id] = bp;
        return bp.Id;
    }

    public void RemoveBreakpoint(int breakpointId)
    {
        if (!m_breakpoints.TryGetValue(breakpointId, out var bp))
            return;

        bp.Hit -= OnBreakpointHit;
        bp.Disable();
        m_breakpoints.Remove(breakpointId);
    }

    private void OnBreakpointHit(object sender, EventArgs e)
    {
        var bp = (DzrpBreakpoint)sender;
        ClearTempBreakpoints();
        Paused?.Invoke(this, new PauseEventArgs(PauseReason.BreakpointHit, bp.Address));
    }

    private void OnTempBreakpointHit(object sender, EventArgs e)
    {
        var bp = (DzrpBreakpoint)sender;
        ClearTempBreakpoints();
        Paused?.Invoke(this, new PauseEventArgs(PauseReason.BreakpointHit, bp.Address));
    }

    private void ClearTempBreakpoints()
    {
        foreach (var bp in m_tempBreakpoints)
        {
            bp.Hit -= OnTempBreakpointHit;
            bp.Disable();
        }

        m_tempBreakpoints.Clear();
    }
}

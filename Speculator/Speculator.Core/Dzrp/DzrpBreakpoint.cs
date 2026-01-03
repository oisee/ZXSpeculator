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
/// DZRP-managed breakpoint with unique identifier.
/// Similar to SingleBreakpoint but with ID tracking for DZRP protocol.
/// </summary>
public class DzrpBreakpoint
{
    private static int s_nextId = 1;

    private readonly CPU m_cpu;
    private bool m_isEnabled;

    public int Id { get; }
    public ushort Address { get; }
    public bool IsTemporary { get; }

    public event EventHandler Hit;

    public DzrpBreakpoint(CPU cpu, ushort address, bool isTemporary = false)
    {
        m_cpu = cpu;
        Address = address;
        IsTemporary = isTemporary;
        Id = s_nextId++;
    }

    public void Enable()
    {
        if (m_isEnabled)
            return;

        m_isEnabled = true;
        m_cpu.Ticked += OnCpuTicked;
    }

    public void Disable()
    {
        if (!m_isEnabled)
            return;

        m_isEnabled = false;
        m_cpu.Ticked -= OnCpuTicked;
    }

    private void OnCpuTicked(object sender, (int elapsedTicks, ushort prevPC, ushort currentPC) args)
    {
        if (args.currentPC != Address)
            return;

        // Pause the CPU and notify listeners
        m_cpu.IsDebuggerActive = true;
        Hit?.Invoke(this, EventArgs.Empty);
    }

    public override string ToString() => $"BP#{Id} @ {Address:X04}{(IsTemporary ? " (temp)" : "")}";
}

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

namespace Speculator.Core;

/// <summary>
/// A condition that, when met, triggers the debugger.
/// Supports: PC=XXXX, SP=XXXX, OP=XX, T=NNNNN (all hex except T which is decimal).
/// </summary>
public class BreakAtCondition
{
    private readonly HashSet<ushort> m_pcTriggers = new HashSet<ushort>();
    private readonly HashSet<ushort> m_spTriggers = new HashSet<ushort>();
    private readonly HashSet<byte> m_opTriggers = new HashSet<byte>();
    private readonly List<long> m_tStateTriggers = new List<long>();
    private readonly HashSet<long> m_firedTStateTriggers = new HashSet<long>();

    public bool HasTriggers => m_pcTriggers.Count > 0 || m_spTriggers.Count > 0 || m_opTriggers.Count > 0 || m_tStateTriggers.Count > 0;

    /// <summary>
    /// Parse a --break-at spec string. Comma-separated conditions.
    /// Examples: "PC=8000", "SP=FFFF", "OP=FF", "T=100000", "PC=8000,T=500000"
    /// </summary>
    public static BreakAtCondition Parse(string spec)
    {
        var result = new BreakAtCondition();
        if (string.IsNullOrWhiteSpace(spec))
            return result;

        var parts = spec.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var part in parts)
        {
            if (part.StartsWith("PC=", StringComparison.OrdinalIgnoreCase) &&
                ushort.TryParse(part.Substring(3), System.Globalization.NumberStyles.HexNumber, null, out var pc))
            {
                result.m_pcTriggers.Add(pc);
            }
            else if (part.StartsWith("SP=", StringComparison.OrdinalIgnoreCase) &&
                     ushort.TryParse(part.Substring(3), System.Globalization.NumberStyles.HexNumber, null, out var sp))
            {
                result.m_spTriggers.Add(sp);
            }
            else if (part.StartsWith("OP=", StringComparison.OrdinalIgnoreCase) &&
                     byte.TryParse(part.Substring(3), System.Globalization.NumberStyles.HexNumber, null, out var op))
            {
                result.m_opTriggers.Add(op);
            }
            else if (part.StartsWith("T=", StringComparison.OrdinalIgnoreCase) &&
                     long.TryParse(part.Substring(2), out var tStates))
            {
                result.m_tStateTriggers.Add(tStates);
            }
            else
            {
                Console.WriteLine($"Warning: Unknown --break-at condition: {part}");
            }
        }

        return result;
    }

    /// <summary>
    /// Check if any condition is met. Called per CPU tick.
    /// Returns true if debugger should be triggered (fires once per condition).
    /// </summary>
    public bool Check(CPU cpu, ushort prevPC, ushort currentPC)
    {
        if (m_pcTriggers.Contains(currentPC))
        {
            m_pcTriggers.Remove(currentPC);
            Console.WriteLine($"Break: PC={currentPC:X4}");
            return true;
        }

        if (m_spTriggers.Contains(cpu.TheRegisters.SP))
        {
            m_spTriggers.Remove(cpu.TheRegisters.SP);
            Console.WriteLine($"Break: SP={cpu.TheRegisters.SP:X4}");
            return true;
        }

        if (m_opTriggers.Count > 0)
        {
            var opcode = cpu.MainMemory.Peek(prevPC);
            if (m_opTriggers.Contains(opcode))
            {
                m_opTriggers.Remove(opcode);
                Console.WriteLine($"Break: OP={opcode:X2}");
                return true;
            }
        }

        foreach (var t in m_tStateTriggers)
        {
            if (cpu.TStatesSinceCpuStart >= t && !m_firedTStateTriggers.Contains(t))
            {
                m_firedTStateTriggers.Add(t);
                Console.WriteLine($"Break: T={t} (actual: {cpu.TStatesSinceCpuStart})");
                return true;
            }
        }

        return false;
    }
}

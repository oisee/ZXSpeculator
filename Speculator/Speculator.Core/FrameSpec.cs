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
/// Parses and evaluates frame range specifications for the frame dump system.
/// Supports numeric ranges (e.g. "1,50..100"), event triggers (e.g. "load-end",
/// "PC=4000", "SP=FFFF", "OP=FF", "T=100000"), and offsets on events (e.g. "load-end+200").
/// </summary>
public class FrameSpec
{
    private readonly List<FrameRange> m_ranges = new List<FrameRange>();
    private bool m_captureAll;

    /// <summary>
    /// A single range entry within a frame spec.
    /// </summary>
    private class FrameRange
    {
        public long? StartFrame;
        public long? EndFrame;
        public string StartEvent;
        public string EndEvent;
        public long StartOffset;
        public long EndOffset;
        public bool IsSingleFrame;
    }

    private FrameSpec()
    {
    }

    /// <summary>
    /// Parse a frame spec string. Returns a FrameSpec that matches all frames if spec is null/empty.
    /// </summary>
    public static FrameSpec Parse(string spec)
    {
        var result = new FrameSpec();

        if (string.IsNullOrWhiteSpace(spec))
        {
            result.m_captureAll = true;
            return result;
        }

        var parts = spec.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var part in parts)
        {
            var dotIndex = part.IndexOf("..", StringComparison.Ordinal);
            if (dotIndex >= 0)
            {
                // Range: "start..end"
                var startStr = part.Substring(0, dotIndex);
                var endStr = part.Substring(dotIndex + 2);
                var range = new FrameRange();
                ParseEndpoint(startStr, out range.StartEvent, out var startFrame, out range.StartOffset);
                range.StartFrame = startFrame;
                ParseEndpoint(endStr, out range.EndEvent, out var endFrame, out range.EndOffset);
                range.EndFrame = endFrame;
                result.m_ranges.Add(range);
            }
            else
            {
                // Single frame or event
                var range = new FrameRange { IsSingleFrame = true };
                ParseEndpoint(part, out range.StartEvent, out var frame, out range.StartOffset);
                range.StartFrame = frame;
                range.EndFrame = frame;
                range.EndOffset = range.StartOffset;
                result.m_ranges.Add(range);
            }
        }

        return result;
    }

    /// <summary>
    /// Parse an endpoint string like "100", "load-end", "load-end+50", "PC=4000", "SP=FFFF", "OP=FF", "T=100000".
    /// </summary>
    private static void ParseEndpoint(string s, out string eventName, out long? frame, out long offset)
    {
        eventName = null;
        frame = null;
        offset = 0;

        if (string.IsNullOrWhiteSpace(s))
            return;

        // Check for offset: "something+N" or "something-N"
        var offsetIndex = -1;
        // Find the last '+' or '-' that's not inside parens and not the start
        for (var i = s.Length - 1; i > 0; i--)
        {
            if (s[i] == '+' || s[i] == '-')
            {
                // Make sure it's after the event part (not inside parens or hex)
                var before = s.Substring(0, i);
                if (before.Length > 0 && !before.EndsWith("=", StringComparison.Ordinal))
                {
                    offsetIndex = i;
                    break;
                }
            }
        }

        var baseStr = offsetIndex >= 0 ? s.Substring(0, offsetIndex) : s;
        if (offsetIndex >= 0)
        {
            var offsetStr = s.Substring(offsetIndex);
            if (long.TryParse(offsetStr, out var off))
                offset = off;
        }

        // Try parse as numeric frame
        if (long.TryParse(baseStr, out var n))
        {
            frame = n + offset;
            offset = 0; // Offset already folded in
            return;
        }

        // It's an event
        eventName = baseStr;
    }

    /// <summary>
    /// Returns true if the given frame number should be captured.
    /// </summary>
    public bool ShouldCapture(long frameNumber)
    {
        if (m_captureAll)
            return true;

        foreach (var range in m_ranges)
        {
            // If start or end have unresolved events, skip this range
            if (range.StartEvent != null && range.StartFrame == null)
                continue;
            if (range.EndEvent != null && range.EndFrame == null)
                continue;

            var start = range.StartFrame ?? long.MinValue;
            var end = range.EndFrame ?? long.MaxValue;

            if (range.IsSingleFrame)
            {
                if (frameNumber == start)
                    return true;
            }
            else
            {
                if (frameNumber >= start && frameNumber <= end)
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Returns true when all ranges have been fully exhausted (past all end frames).
    /// </summary>
    public bool IsComplete(long frameNumber)
    {
        if (m_captureAll)
            return false;

        if (m_ranges.Count == 0)
            return true;

        foreach (var range in m_ranges)
        {
            // If any range has unresolved events, not complete yet
            if (range.StartEvent != null && range.StartFrame == null)
                return false;
            if (range.EndEvent != null && range.EndFrame == null)
                return false;

            var end = range.EndFrame ?? long.MaxValue;
            if (frameNumber <= end)
                return false;
        }

        return true;
    }

    /// <summary>
    /// Resolve a named event to a frame number. Call when the event fires.
    /// </summary>
    public void ResolveEvent(string name, long frameNumber)
    {
        foreach (var range in m_ranges)
        {
            if (range.StartEvent != null && range.StartEvent == name && range.StartFrame == null)
                range.StartFrame = frameNumber + range.StartOffset;

            if (range.EndEvent != null && range.EndEvent == name && range.EndFrame == null)
                range.EndFrame = frameNumber + range.EndOffset;
        }
    }

    /// <summary>
    /// Get all PC address triggers (from "PC=XXXX" events).
    /// </summary>
    public HashSet<ushort> GetPcTriggers()
    {
        var result = new HashSet<ushort>();
        foreach (var range in m_ranges)
        {
            TryAddPcTrigger(range.StartEvent, result);
            TryAddPcTrigger(range.EndEvent, result);
        }
        return result;
    }

    /// <summary>
    /// Get all SP value triggers (from "SP=XXXX" events).
    /// </summary>
    public HashSet<ushort> GetSpTriggers()
    {
        var result = new HashSet<ushort>();
        foreach (var range in m_ranges)
        {
            TryAddRegTrigger(range.StartEvent, "SP=", result);
            TryAddRegTrigger(range.EndEvent, "SP=", result);
        }
        return result;
    }

    /// <summary>
    /// Get all opcode triggers (from "OP=XX" events).
    /// </summary>
    public HashSet<byte> GetOpTriggers()
    {
        var result = new HashSet<byte>();
        foreach (var range in m_ranges)
        {
            TryAddOpTrigger(range.StartEvent, result);
            TryAddOpTrigger(range.EndEvent, result);
        }
        return result;
    }

    /// <summary>
    /// Get all T-state triggers (from "T=NNNNN" events).
    /// </summary>
    public List<long> GetTStateTriggers()
    {
        var result = new List<long>();
        foreach (var range in m_ranges)
        {
            TryAddTStateTrigger(range.StartEvent, result);
            TryAddTStateTrigger(range.EndEvent, result);
        }
        return result;
    }

    /// <summary>
    /// Returns true if this spec has any event triggers that require CPU monitoring.
    /// </summary>
    public bool HasCpuTriggers()
    {
        return GetPcTriggers().Count > 0 || GetSpTriggers().Count > 0 || GetOpTriggers().Count > 0 || GetTStateTriggers().Count > 0;
    }

    /// <summary>
    /// Returns true if this spec has any load event triggers.
    /// </summary>
    public bool HasLoadTriggers()
    {
        foreach (var range in m_ranges)
        {
            if (range.StartEvent is "load-start" or "load-end")
                return true;
            if (range.EndEvent is "load-start" or "load-end")
                return true;
        }
        return false;
    }

    private static void TryAddPcTrigger(string eventName, HashSet<ushort> triggers)
    {
        if (eventName == null || !eventName.StartsWith("PC=", StringComparison.OrdinalIgnoreCase))
            return;
        if (ushort.TryParse(eventName.Substring(3), System.Globalization.NumberStyles.HexNumber, null, out var addr))
            triggers.Add(addr);
    }

    private static void TryAddRegTrigger(string eventName, string prefix, HashSet<ushort> triggers)
    {
        if (eventName == null || !eventName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return;
        if (ushort.TryParse(eventName.Substring(prefix.Length), System.Globalization.NumberStyles.HexNumber, null, out var val))
            triggers.Add(val);
    }

    private static void TryAddOpTrigger(string eventName, HashSet<byte> triggers)
    {
        if (eventName == null || !eventName.StartsWith("OP=", StringComparison.OrdinalIgnoreCase))
            return;
        if (byte.TryParse(eventName.Substring(3), System.Globalization.NumberStyles.HexNumber, null, out var op))
            triggers.Add(op);
    }

    private static void TryAddTStateTrigger(string eventName, List<long> triggers)
    {
        if (eventName == null || !eventName.StartsWith("T=", StringComparison.OrdinalIgnoreCase))
            return;
        if (long.TryParse(eventName.Substring(2), out var tStates))
            triggers.Add(tStates);
    }
}

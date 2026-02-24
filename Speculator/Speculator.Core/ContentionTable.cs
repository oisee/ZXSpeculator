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
/// Pre-computed ULA contention delay table.
/// On a real 48K Spectrum, when the CPU accesses memory in $4000-$7FFF during
/// active screen rendering, the ULA forces WAIT states. This table stores the
/// delay (in T-states) for each frame T-state position.
/// </summary>
public class ContentionTable
{
    private readonly byte[] m_delays;

    /// <summary>
    /// Whether contention is active for this machine profile.
    /// </summary>
    public bool IsActive { get; }

    public ContentionTable(MachineProfile profile, bool enabled)
    {
        m_delays = new byte[profile.TStatesPerFrame];
        IsActive = enabled && profile.HasContention;

        if (!IsActive)
            return;

        // 48K contention pattern: repeating {6,5,4,3,2,1,0,0} during active screen lines.
        var pattern = new byte[] { 6, 5, 4, 3, 2, 1, 0, 0 };

        // Screen area: 192 lines starting at FirstScreenLine.
        for (var line = 0; line < 192; line++)
        {
            var scanline = profile.FirstScreenLine + line;
            var lineStart = scanline * profile.TStatesPerLine;

            // Contention applies during the 128 T-states of screen pixel rendering.
            for (var t = 0; t < 128; t++)
            {
                var frameTState = lineStart + profile.ScreenTStatesOffset + t;
                if (frameTState < m_delays.Length)
                    m_delays[frameTState] = pattern[t % 8];
            }
        }
    }

    /// <summary>
    /// Get the contention delay for a given frame T-state position.
    /// </summary>
    public int GetDelay(int frameTState)
    {
        if (!IsActive || (uint)frameTState >= (uint)m_delays.Length)
            return 0;
        return m_delays[frameTState];
    }
}

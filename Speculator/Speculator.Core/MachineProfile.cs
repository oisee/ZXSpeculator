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
/// Defines timing parameters for a specific ZX Spectrum machine variant.
/// </summary>
public record MachineProfile(
    string Name,
    int TStatesPerLine,
    int LinesPerFrame,
    int FirstScreenLine,
    double CpuFreqHz,
    int ScreenTStatesOffset,
    bool HasContention
)
{
    /// <summary>
    /// Default CPU frequency for the ZX Spectrum 48K (used by sound handler).
    /// </summary>
    public const double DefaultCpuFreqHz = 3494400;

    /// <summary>
    /// Total T-states per frame (determines interrupt rate).
    /// </summary>
    public int TStatesPerFrame => TStatesPerLine * LinesPerFrame;

    public static readonly MachineProfile ZX48K = new(
        "ZX Spectrum 48K",
        TStatesPerLine: 224,
        LinesPerFrame: 312,
        FirstScreenLine: 48,
        CpuFreqHz: 3494400,
        ScreenTStatesOffset: 16,
        HasContention: true
    );

    public static readonly MachineProfile ZX128K = new(
        "ZX Spectrum 128K",
        TStatesPerLine: 228,
        LinesPerFrame: 311,
        FirstScreenLine: 63,
        CpuFreqHz: 3546900,
        ScreenTStatesOffset: 16,
        HasContention: true
    );

    public static readonly MachineProfile Pentagon128 = new(
        "Pentagon 128",
        TStatesPerLine: 224,
        LinesPerFrame: 320,
        FirstScreenLine: 80,
        CpuFreqHz: 3500000,
        ScreenTStatesOffset: 16,
        HasContention: false
    );

    public static MachineProfile FromName(string name) => name.ToLowerInvariant() switch
    {
        "48k" => ZX48K,
        "128k" => ZX128K,
        "pentagon" => Pentagon128,
        _ => throw new ArgumentException($"Unknown machine profile: {name}. Valid options: 48k, 128k, pentagon")
    };
}

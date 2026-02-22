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

using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Speculator.Core;

/// <summary>
/// Captures emulator frames as PNG files based on a FrameSpec.
/// Supports keyframe-only mode and border/no-border capture.
/// </summary>
public class FrameDumper : IDisposable
{
    private readonly string m_outputDir;
    private readonly FrameSpec m_spec;
    private readonly bool m_keyframesOnly;
    private readonly bool m_includeBorder;
    private int m_fileNumber;
    private readonly HashSet<ushort> m_pcTriggers;
    private readonly HashSet<ushort> m_spTriggers;
    private readonly HashSet<byte> m_opTriggers;
    private readonly List<long> m_tStateTriggers;
    private bool m_isComplete;

    public int CapturedCount => m_fileNumber;

    public FrameDumper(string outputDir, FrameSpec spec, bool keyframesOnly, bool includeBorder)
    {
        m_outputDir = outputDir;
        m_spec = spec;
        m_keyframesOnly = keyframesOnly;
        m_includeBorder = includeBorder;
        m_pcTriggers = spec.GetPcTriggers();
        m_spTriggers = spec.GetSpTriggers();
        m_opTriggers = spec.GetOpTriggers();
        m_tStateTriggers = spec.GetTStateTriggers();

        Directory.CreateDirectory(outputDir);
    }

    /// <summary>
    /// Called by ZxDisplay.FrameCompleted at the bottom of each frame.
    /// </summary>
    public void OnFrameComplete(object sender, FrameCompleteEventArgs e)
    {
        if (m_isComplete)
            return;

        if (m_spec.IsComplete(e.FrameNumber))
        {
            m_isComplete = true;
            Console.WriteLine($"Frame dump complete: {m_fileNumber} frames captured to {m_outputDir}");
            return;
        }

        if (!m_spec.ShouldCapture(e.FrameNumber))
            return;

        if (m_keyframesOnly && !e.DidPixelsChange)
            return;

        CaptureFrame(e.Memory, e.BorderAttr);
    }

    /// <summary>
    /// Called when file loading starts (resolves "load-start" events).
    /// </summary>
    public void OnLoadStarted(long frameNumber)
    {
        m_spec.ResolveEvent("load-start", frameNumber);
    }

    /// <summary>
    /// Called when file loading completes (resolves "load-end" events).
    /// </summary>
    public void OnLoadCompleted(long frameNumber)
    {
        m_spec.ResolveEvent("load-end", frameNumber);
    }

    /// <summary>
    /// Called per CPU tick when FrameSpec has PC/SP/OP/T-state triggers.
    /// </summary>
    public void OnCpuTicked(object sender, (int elapsedTicks, ushort prevPC, ushort currentPC) e)
    {
        if (m_isComplete)
            return;

        var cpu = (CPU)sender;
        var frameNumber = cpu.TStatesSinceCpuStart / 69888;

        // Check PC triggers
        if (m_pcTriggers.Contains(e.currentPC))
            m_spec.ResolveEvent($"PC={e.currentPC:X4}", frameNumber);

        // Check SP triggers
        if (m_spTriggers.Contains(cpu.TheRegisters.SP))
            m_spec.ResolveEvent($"SP={cpu.TheRegisters.SP:X4}", frameNumber);

        // Check opcode triggers
        if (m_opTriggers.Count > 0)
        {
            var opcode = cpu.MainMemory.Peek(e.prevPC);
            if (m_opTriggers.Contains(opcode))
                m_spec.ResolveEvent($"OP={opcode:X2}", frameNumber);
        }

        // Check T-state triggers
        foreach (var tStateTrigger in m_tStateTriggers)
        {
            if (cpu.TStatesSinceCpuStart >= tStateTrigger)
                m_spec.ResolveEvent($"T={tStateTrigger}", frameNumber);
        }
    }

    private void CaptureFrame(Memory memory, byte borderAttr)
    {
        m_fileNumber++;
        var fileName = Path.Combine(m_outputDir, $"frame_{m_fileNumber:D6}.png");

        var pixels = ZxDisplay.RenderScreenToPixels(memory, borderAttr, m_includeBorder);

        int width, height;
        if (m_includeBorder)
        {
            width = 320;  // 32 + 256 + 32
            height = 240;  // 24 + 192 + 24
        }
        else
        {
            width = 256;
            height = 192;
        }

        // Write PNG using Avalonia's WriteableBitmap
        var bitmap = new WriteableBitmap(
            new PixelSize(width, height),
            new Avalonia.Vector(96, 96),
            PixelFormat.Rgba8888);

        using (var fb = bitmap.Lock())
        {
            unsafe
            {
                var dest = new Span<byte>((byte*)fb.Address, fb.RowBytes * fb.Size.Height);
                var stride = fb.RowBytes;
                for (var y = 0; y < height; y++)
                {
                    var srcOffset = y * width * 4;
                    var destOffset = y * stride;
                    pixels.AsSpan(srcOffset, width * 4).CopyTo(dest.Slice(destOffset, width * 4));
                }
            }
        }

        bitmap.Save(fileName);
        bitmap.Dispose();
    }

    public void Dispose()
    {
        if (m_fileNumber > 0)
            Console.WriteLine($"Frame dump: {m_fileNumber} frames captured to {m_outputDir}");
    }
}

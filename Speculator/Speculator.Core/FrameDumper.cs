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

using System.Collections.Concurrent;
using System.Diagnostics;

namespace Speculator.Core;

/// <summary>
/// Captures emulator frames as PNG files based on a FrameSpec.
/// Raw screen memory (6912 bytes) is buffered on the CPU thread;
/// PNG rendering and writing happens on a background thread.
/// </summary>
public class FrameDumper : IDisposable
{
    private const int ScreenDataSize = 6912; // $4000-$5AFF (6144 pixels + 768 attributes)

    private readonly string m_outputDir;
    private readonly FrameSpec m_spec;
    private readonly bool m_keyframesOnly;
    private readonly bool m_includeBorder;
    private readonly bool m_addTimestamps;
    private volatile int m_fileNumber;
    private readonly HashSet<ushort> m_pcTriggers;
    private readonly HashSet<ushort> m_spTriggers;
    private readonly HashSet<byte> m_opTriggers;
    private readonly List<long> m_tStateTriggers;
    private volatile bool m_isComplete;
    private volatile bool m_disposed;
    private readonly Stopwatch m_stopwatch = new();
    private long m_pendingLoadStartFrame = -1;
    private long m_pendingLoadEndFrame = -1;

    private readonly ConcurrentQueue<FrameSnapshot> m_queue = new();
    private readonly Thread m_writerThread;

    private struct FrameSnapshot
    {
        public byte[] ScreenData; // 6912 bytes copied from $4000-$5AFF
        public byte BorderAttr;
        public int FileNumber;
        public long TimestampUs; // Microseconds since first capture
    }

    public int CapturedCount => m_fileNumber;

    public FrameDumper(string outputDir, FrameSpec spec, bool keyframesOnly, bool includeBorder, bool addTimestamps = false)
    {
        m_outputDir = outputDir;
        m_spec = spec;
        m_keyframesOnly = keyframesOnly;
        m_includeBorder = includeBorder;
        m_addTimestamps = addTimestamps;
        m_pcTriggers = spec.GetPcTriggers();
        m_spTriggers = spec.GetSpTriggers();
        m_opTriggers = spec.GetOpTriggers();
        m_tStateTriggers = spec.GetTStateTriggers();

        Directory.CreateDirectory(outputDir);

        m_writerThread = new Thread(WriterLoop)
        {
            IsBackground = true,
            Name = "FrameDumper-Writer"
        };
        m_writerThread.Start();
    }

    /// <summary>
    /// Called by ZxDisplay.FrameCompleted at the bottom of each frame (CPU thread).
    /// Copies 6912 bytes of raw screen memory and enqueues for background PNG writing.
    /// </summary>
    public void OnFrameComplete(object sender, FrameCompleteEventArgs e)
    {
        if (m_isComplete)
            return;

        // Resolve deferred load events on the CPU thread (thread-safe with ShouldCapture/IsComplete)
        var loadStart = Interlocked.Exchange(ref m_pendingLoadStartFrame, -1);
        if (loadStart >= 0)
            m_spec.ResolveEvent("load-start", loadStart);

        var loadEnd = Interlocked.Exchange(ref m_pendingLoadEndFrame, -1);
        if (loadEnd >= 0)
            m_spec.ResolveEvent("load-end", loadEnd);

        if (m_spec.IsComplete(e.FrameNumber))
        {
            m_isComplete = true;
            return;
        }

        if (!m_spec.ShouldCapture(e.FrameNumber))
            return;

        if (m_keyframesOnly && !e.DidPixelsChange)
            return;

        // Fast path: copy just 6912 bytes of ZX Spectrum screen memory
        if (!m_stopwatch.IsRunning)
            m_stopwatch.Start();
        m_fileNumber++;
        var screenData = new byte[ScreenDataSize];
        Buffer.BlockCopy(e.Memory.Data, ZxDisplay.ScreenBase, screenData, 0, ScreenDataSize);

        m_queue.Enqueue(new FrameSnapshot
        {
            ScreenData = screenData,
            BorderAttr = e.BorderAttr,
            FileNumber = m_fileNumber,
            TimestampUs = m_stopwatch.ElapsedTicks * 1_000_000 / Stopwatch.Frequency
        });
    }

    /// <summary>
    /// Called when file loading starts (may be called from UI thread).
    /// Defers resolution to the CPU thread via OnFrameComplete.
    /// </summary>
    public void OnLoadStarted(long frameNumber)
    {
        Interlocked.Exchange(ref m_pendingLoadStartFrame, frameNumber);
    }

    /// <summary>
    /// Called when file loading completes (may be called from UI thread).
    /// Defers resolution to the CPU thread via OnFrameComplete.
    /// </summary>
    public void OnLoadCompleted(long frameNumber)
    {
        Interlocked.Exchange(ref m_pendingLoadEndFrame, frameNumber);
    }

    /// <summary>
    /// Called per CPU tick when FrameSpec has PC/SP/OP/T-state triggers.
    /// </summary>
    public void OnCpuTicked(object sender, (int elapsedTicks, ushort prevPC, ushort currentPC) e)
    {
        if (m_isComplete)
            return;

        var cpu = (CPU)sender;
        var frameNumber = cpu.TStatesSinceCpuStart / cpu.TStatesPerInterrupt;

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

    /// <summary>
    /// Background thread: dequeues snapshots, renders to RGBA, writes PNG.
    /// </summary>
    private void WriterLoop()
    {
        var tempMemory = new Memory();
        var completionPrinted = false;

        while (!m_disposed || !m_queue.IsEmpty)
        {
            if (m_queue.TryDequeue(out var snapshot))
            {
                WriteFramePng(snapshot, tempMemory);
            }
            else
            {
                if (m_isComplete && !completionPrinted)
                {
                    m_stopwatch.Stop();
                    completionPrinted = true;
                    Console.WriteLine($"Frame dump complete: {m_fileNumber} frames captured to {m_outputDir} ({m_stopwatch.Elapsed.TotalSeconds:F2}s)");
                }

                Thread.Sleep(1);
            }
        }

        if (!completionPrinted && m_fileNumber > 0)
        {
            m_stopwatch.Stop();
            Console.WriteLine($"Frame dump: {m_fileNumber} frames captured to {m_outputDir} ({m_stopwatch.Elapsed.TotalSeconds:F2}s)");
        }
    }

    private void WriteFramePng(FrameSnapshot snapshot, Memory tempMemory)
    {
        var name = m_addTimestamps
            ? $"frame_{snapshot.FileNumber:D6}_{snapshot.TimestampUs:X}.png"
            : $"frame_{snapshot.FileNumber:D6}.png";
        var fileName = Path.Combine(m_outputDir, name);

        // Populate temp memory with the snapshot's screen data
        Buffer.BlockCopy(snapshot.ScreenData, 0, tempMemory.Data, ZxDisplay.ScreenBase, ScreenDataSize);

        // Render to RGBA pixels (pure computation, no Avalonia)
        var pixels = ZxDisplay.RenderScreenToPixels(tempMemory, snapshot.BorderAttr, m_includeBorder);

        var width = m_includeBorder ? 320 : 256;
        var height = m_includeBorder ? 240 : 192;

        // Write PNG using custom encoder (no Avalonia dependency)
        PngWriter.Write(fileName, pixels, width, height);
    }

    public void Dispose()
    {
        m_disposed = true;
        m_writerThread?.Join(TimeSpan.FromSeconds(30));
    }
}

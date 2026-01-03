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

using CSharp.Core.ViewModels;
using Speculator.Core.Dzrp;
using Speculator.Core.Tape;

namespace Speculator.Core;

/// <summary>
/// The main emulation entry point object.
/// </summary>
public class ZxSpectrum : ViewModelBase, IDisposable
{
    private SoundHandler m_soundHandler;
    private DzrpServer m_dzrpServer;
    private readonly ZxFileIo m_zxFileIo;
    private ClockSync.Speed m_emulationSpeed;

    private ZxDisplay TheDisplay { get; }
    public CPU TheCpu { get; }
    public ZxPortHandler PortHandler { get; }
    public SoundHandler SoundHandler => m_soundHandler ??= new SoundHandler();
    public TapeLoader TheTapeLoader { get; } = new TapeLoader();
    public Debugger.Debugger TheDebugger { get; }
    public CpuHistory CpuHistory { get; }

    public ClockSync.Speed EmulationSpeed
    {
        get => m_emulationSpeed;
        set
        {
            if (!SetField(ref m_emulationSpeed, value))
                return;
            TheCpu.SetSpeed(value);
            TheDisplay.IsPaused = value == ClockSync.Speed.Pause;
        }
    }

    public ZxSpectrum(ZxDisplay display)
    {
        TheDisplay = display;
        PortHandler = new ZxPortHandler(SoundHandler, TheDisplay, TheTapeLoader);
        TheCpu = new CPU(new Memory(), PortHandler, SoundHandler);
        TheTapeLoader.SetCpu(TheCpu);
        TheDebugger = new Debugger.Debugger(TheCpu);

        TheCpu.RenderScanline += TheDisplay.OnRenderScanline;

        TheDebugger.IsSteppingChanged += (_, _) =>
        {
            if (TheDebugger.IsStepping)
                SoundHandler.SetEnabled(false);
        };

        m_zxFileIo = new ZxFileIo(TheCpu, TheDisplay, TheTapeLoader);
        CpuHistory = new CpuHistory(TheCpu, m_zxFileIo);
    }

    public void PowerOnAsync() =>
        TheCpu.PowerOnAsync();

    public void LoadSystemRom(FileInfo systemRom)
    {
        EmulationSpeed = ClockSync.Speed.Actual;
        m_zxFileIo.LoadSystemRom(systemRom);
    }

    public void LoadRom(FileInfo romFile)
    {
        EmulationSpeed = ClockSync.Speed.Actual;
        m_zxFileIo.LoadFile(romFile);
    }

    public void SaveRom(FileInfo romFile) =>
        m_zxFileIo.SaveFile(romFile);

    public void ResetAsync()
    {
        EmulationSpeed = ClockSync.Speed.Actual;
        TheCpu.ResetAsync();
    }

    /// <summary>
    /// Enable DZRP (DeZog Remote Protocol) server for VS Code debugging.
    /// </summary>
    /// <param name="port">TCP port (default 11000)</param>
    /// <param name="bindAddress">Bind address: "127.0.0.1" (local only) or "0.0.0.0" (remote access)</param>
    public void EnableDzrp(int port = DzrpServer.DefaultPort, string bindAddress = DzrpServer.DefaultBindAddress)
    {
        if (m_dzrpServer == null)
        {
            m_dzrpServer = new DzrpServer(TheCpu, port, bindAddress);

            // Forward DZRP events for UI layer to handle (needs UI thread dispatching)
            m_dzrpServer.ExecutionPaused += (_, args) => DzrpExecutionPaused?.Invoke(this, args);
            m_dzrpServer.ExecutionContinued += (_, _) => DzrpExecutionContinued?.Invoke(this, EventArgs.Empty);
        }

        m_dzrpServer.Start();
    }

    /// <summary>
    /// Disable DZRP server.
    /// </summary>
    public void DisableDzrp()
    {
        m_dzrpServer?.Stop();
    }

    /// <summary>
    /// Returns true if DZRP server is running.
    /// </summary>
    public bool IsDzrpEnabled => m_dzrpServer?.IsEnabled ?? false;

    /// <summary>
    /// Returns true if a DZRP client (DeZog) is connected.
    /// </summary>
    public bool IsDzrpClientConnected => m_dzrpServer?.IsClientConnected ?? false;

    /// <summary>
    /// Force a screen refresh from current memory state.
    /// Useful when DZRP modifies memory while paused.
    /// </summary>
    public void RefreshScreen() => TheDisplay.ForceRender(TheCpu.MainMemory);

    /// <summary>
    /// Fired when DZRP pauses execution. UI layer should handle on UI thread.
    /// </summary>
    public event EventHandler<PauseEventArgs> DzrpExecutionPaused;

    /// <summary>
    /// Fired when DZRP continues execution. UI layer should handle on UI thread.
    /// </summary>
    public event EventHandler DzrpExecutionContinued;

    public void Dispose()
    {
        m_dzrpServer?.Dispose();
        m_soundHandler?.Dispose();
        PortHandler?.Dispose();
        TheCpu?.PowerOffAsync();
    }
}
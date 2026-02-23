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

using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Threading;
using CSharp.Core.Commands;
using CSharp.Core.Extensions;
using CSharp.Core.UI;
using CSharp.Core.ViewModels;
using Material.Icons;
using Speculator.Core;
using Speculator.Extensions;

namespace Speculator.ViewModels;

public class MainWindowViewModel : ViewModelBase, IDisposable
{
    private bool m_enableDzrpOnStart;
    private int? m_dzrpPortOverride;
    private string m_dzrpBindOverride;
    private bool? m_skipKeyboardHookOverride;
    private bool m_showDebuggerOnStart;
    private string m_dumpFramesDir;
    private string m_dumpKeyframesDir;
    private string m_frameSpec;
    private bool m_noBorder;
    private string m_breakAt;
    private bool m_maxSpeed;
    private bool m_timestamp;

    public ZxSpectrum Speccy { get; }
    public ZxDisplay Display { get; }
    public Settings Settings => Settings.Instance;
    public MruFiles Mru { get; }
    public RomSelectorViewModel RomSelectorDetails { get; }

    /// <summary>
    /// Whether keyboard hook should be skipped (for DZRP mode).
    /// Command-line flag overrides Settings.
    /// </summary>
    public bool ShouldSkipKeyboardHook => m_skipKeyboardHookOverride ?? Settings.SkipKeyboardHook;

    public MainWindowViewModel(string[] args = null)
    {
        Display = new ZxDisplay();
        Speccy = new ZxSpectrum(Display);
        Speccy.PortHandler.EmulateCursorJoystick = Settings.EmulateCursorJoystick;
        Speccy.TheCpu.LoadRequested += (_, _) =>
        {
            if (!Speccy.TheTapeLoader.IsLoading)
                Dispatcher.UIThread.InvokeAsync(LoadGameRom);
        };

        Speccy.CpuHistory.Activated += (_, _) => Speccy.EmulationSpeed = ClockSync.Speed.Actual;

        // Handle DZRP execution events on UI thread for proper debugger UI updates
        Speccy.DzrpExecutionPaused += (_, _) => Dispatcher.UIThread.InvokeAsync(() =>
        {
            Speccy.TheDebugger.StartDebugging();
            Speccy.TheDebugger.Show();
            Speccy.TheDebugger.RefreshUi();
            Speccy.RefreshScreen();
        });
        Speccy.DzrpExecutionContinued += (_, _) => Dispatcher.UIThread.InvokeAsync(() =>
        {
            Speccy.TheDebugger.StopDebugging();
        });

        Mru = new MruFiles().InitFromString(Settings.MruFiles);
        Mru.OpenRequested += (_, file) => Speccy.LoadRom(file);

        RomSelectorDetails = new RomSelectorViewModel(Speccy);
        RomSelectorDetails.LoadBasicRomAction(Settings.RomFile);

        // Parse DZRP command-line arguments (override settings)
        ParseArgs(args);

        Settings.PropertyChanged += (_, _) => OnSettingsChanged(true);
        OnSettingsChanged(false);

        if (args != null && args.Length > 0)
        {
            // Find last arg that's not a flag or flag value
            var file = args.LastOrDefault(a => !a.StartsWith("--") && !a.StartsWith("-h") && !IsArgValue(args, a));
            if (file != null)
            {
                var info = new FileInfo(file);
                if (ZxFileIo.IsInstantLoadSupported(info))
                {
                    Console.WriteLine("Loading: " + file);
                    OneShotDispatcherTimer.CreateAndStart(TimeSpan.FromSeconds(3), () => LoadGameRomDirect(info));
                }
            }
        }

        OneShotDispatcherTimer.CreateAndStart(TimeSpan.FromSeconds(3), ShowCrtMessage);

        // Start DZRP after a delay if requested via command line
        // This avoids a crash in Avalonia's NSApplication initialization on macOS
        if (m_enableDzrpOnStart)
        {
            OneShotDispatcherTimer.CreateAndStart(TimeSpan.FromSeconds(1), () =>
            {
                var port = m_dzrpPortOverride ?? Settings.DzrpPort;
                var bind = m_dzrpBindOverride ?? Settings.DzrpBindAddress;
                Speccy.EnableDzrp(port, bind);
            });
        }

        // Show debugger view on startup if requested
        if (m_showDebuggerOnStart)
        {
            OneShotDispatcherTimer.CreateAndStart(TimeSpan.FromSeconds(1), () =>
            {
                Speccy.TheDebugger.Show();
            });
        }

        // Enable frame dump if requested via command line
        var dumpDir = m_dumpFramesDir ?? m_dumpKeyframesDir;
        if (dumpDir != null)
        {
            var keyframesOnly = m_dumpKeyframesDir != null;
            var includeBorder = !m_noBorder;
            Speccy.EnableFrameDump(dumpDir, m_frameSpec, keyframesOnly, includeBorder, m_timestamp);
        }

        // Enable max speed if requested
        if (m_maxSpeed)
            Speccy.EnableMaxSpeed();

        // Enable break-at conditions if requested via command line
        if (m_breakAt != null)
            Speccy.EnableBreakAt(m_breakAt);

        return;

        void OnSettingsChanged(bool allowMessages)
        {
            Display.IsCrt = Settings.IsCrt;
            Speccy.PortHandler.EmulateCursorJoystick = Settings.EmulateCursorJoystick;
            Speccy.SoundHandler.SetEnabled(Settings.IsSoundEnabled);

            // Handle DZRP (DeZog Remote Protocol) server
            if (Settings.IsDzrpEnabled && !Speccy.IsDzrpEnabled)
                Speccy.EnableDzrp(Settings.DzrpPort, Settings.DzrpBindAddress);
            else if (!Settings.IsDzrpEnabled && Speccy.IsDzrpEnabled)
                Speccy.DisableDzrp();

            if (allowMessages)
                ShowCrtMessage();
        }
    }
    
    public void LoadGameRom()
    {
        var keyBlocker = Speccy.PortHandler.CreateKeyBlocker();
        var command = new FileOpenCommand("Load ROM file", "ROM Files", ZxFileIo.OpenFilters);
        command.FileSelected += (_, info) =>
        {
            try
            {
                LoadGameRomDirect(info);
            }
            finally
            {
                keyBlocker.Dispose();
            }
        };
        command.Cancelled += (_, _) => keyBlocker.Dispose();
        command.Execute(null);
    }
    
    public void LoadGameRomDirect(FileInfo info)
    {
        Speccy.LoadRom(info);
        Mru.Add(info);
    }

    public void SaveGameRom()
    {
        var keyBlocker = Speccy.PortHandler.CreateKeyBlocker();
        var command = new FileSaveCommand("Save ROM file", "ROM Files", ZxFileIo.SaveFilters);
        command.FileSelected += (_, info) =>
        {
            try
            {
                Speccy.SaveRom(info);
                Mru.Add(info);
            }
            finally
            {
                keyBlocker.Dispose();
            }
        };
        command.Cancelled += (_, _) => keyBlocker.Dispose();
        command.Execute(null);
    }

    public void ResetMachine() =>
        DialogService.Instance.Warn(
            "Reset Emulator?",
            "This will simulate a restart of the ZX Spectrum.",
            "CANCEL",
            "RESET",
            confirmed =>
            {
                if (confirmed)
                    Speccy.ResetAsync();
            },
            MaterialIconKind.Power);
    
    public void QuickRollback() =>
        Speccy.CpuHistory.RollbackByTime(5);

    public void SetCursorJoystick(bool b) =>
        Settings.EmulateCursorJoystick = b;

    public void CloseCommand() =>
        Application.Current.GetMainWindow().Close();

    public void SetCrtMode(bool b) =>
        Settings.IsCrt = b;
    
    public void RotateEmulationSpeed()
    {
        switch (Speccy.EmulationSpeed)
        {
            case ClockSync.Speed.Actual:
                Speccy.EmulationSpeed = ClockSync.Speed.Fast;
                break;
            case ClockSync.Speed.Fast:
                Speccy.EmulationSpeed = ClockSync.Speed.Maximum;
                break;
            case ClockSync.Speed.Maximum:
                Speccy.EmulationSpeed = ClockSync.Speed.Pause;
                break;
            case ClockSync.Speed.Pause:
                Speccy.EmulationSpeed = ClockSync.Speed.Actual;
                break;
        }
    }

    public void ToggleAmbientBlur() =>
        Settings.IsAmbientBlurred = !Settings.IsAmbientBlurred;

    public void OpenProjectPage() =>
        new Uri("https://github.com/deanthecoder/ZXSpeculator").Open();

    private void ShowCrtMessage()
    {
        if (!Settings.IsCrt || !Settings.DisplayCrtHelp)
            return;
        Settings.DisplayCrtHelp = false;
        DialogService.Instance.ShowMessage("CRT Mode Enabled", "CRT Mode is best viewed with a maximized window.", MaterialIconKind.TelevisionClassic);
    }

    public void SaveScreenshot()
    {
        var keyBlocker = Speccy.PortHandler.CreateKeyBlocker();
        var command = new FileSaveCommand("Save Screenshot", "PNG Files", new[] { "*.png" });
        command.FileSelected += (_, info) =>
        {
            try
            {
                Display.SaveAs(info);
            }
            finally
            {
                keyBlocker.Dispose();
            }
        };
        command.Cancelled += (_, _) => keyBlocker.Dispose();
        command.Execute(null);
    }
    
    public void Dispose()
    {
        Speccy.Dispose();
        Settings.MruFiles = Mru.AsString();
    }

    /// <summary>
    /// Parse command-line arguments.
    /// </summary>
    private void ParseArgs(string[] args)
    {
        if (args == null || args.Length == 0)
            return;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "-h":
                case "--help":
                    PrintHelp();
                    Environment.Exit(0);
                    break;

                case "--dzrp":
                    m_enableDzrpOnStart = true;
                    m_skipKeyboardHookOverride = true;
                    Console.WriteLine("DZRP: Enabled via command line (keyboard hook disabled)");
                    break;

                case "--dzrp-port" when i + 1 < args.Length && int.TryParse(args[i + 1], out var port):
                    m_dzrpPortOverride = port;
                    Console.WriteLine($"DZRP: Port set to {port}");
                    i++;
                    break;

                case "--dzrp-bind" when i + 1 < args.Length:
                    m_dzrpBindOverride = args[i + 1];
                    Console.WriteLine($"DZRP: Bind address set to {args[i + 1]}");
                    i++;
                    break;

                case "--no-keyboard-hook":
                    m_skipKeyboardHookOverride = true;
                    Console.WriteLine("Keyboard hook disabled");
                    break;

                case "--with-keyboard-hook":
                    m_skipKeyboardHookOverride = false;
                    Console.WriteLine("Keyboard hook enabled");
                    break;

                case "--debugger":
                    m_showDebuggerOnStart = true;
                    Console.WriteLine("Debugger view will open on startup");
                    break;

                case "--trace":
                    Speculator.Core.Dzrp.DzrpSession.TraceEnabled = true;
                    Console.WriteLine("DZRP trace mode enabled");
                    break;

                case "--dump-frames" when i + 1 < args.Length:
                    m_dumpFramesDir = args[i + 1];
                    Console.WriteLine($"Frame dump: {args[i + 1]}");
                    i++;
                    break;

                case "--dump-keyframes" when i + 1 < args.Length:
                    m_dumpKeyframesDir = args[i + 1];
                    Console.WriteLine($"Keyframe dump: {args[i + 1]}");
                    i++;
                    break;

                case "--frame-spec" when i + 1 < args.Length:
                    m_frameSpec = args[i + 1];
                    Console.WriteLine($"Frame spec: {args[i + 1]}");
                    i++;
                    break;

                case "--no-border":
                    m_noBorder = true;
                    Console.WriteLine("Frame dump: no border (256x192)");
                    break;

                case "--break-at" when i + 1 < args.Length:
                    m_breakAt = args[i + 1];
                    Console.WriteLine($"Break-at: {args[i + 1]}");
                    i++;
                    break;

                case "--max-speed":
                    m_maxSpeed = true;
                    Console.WriteLine("Max speed: emulation throttle disabled");
                    break;

                case "--timestamp":
                    m_timestamp = true;
                    Console.WriteLine("Timestamp: hex microseconds in filenames");
                    break;
            }
        }
    }

    private static void PrintHelp()
    {
        Console.WriteLine(@"ZX Speculator - ZX Spectrum 48K Emulator

Usage: zxs [OPTIONS] [FILE]

Arguments:
  FILE                         Load a file on startup (.z80, .sna, .tap, .scr, .bin, .zip)

General:
  -h, --help                   Show this help message and exit
  --max-speed                  Disable emulation throttle (run as fast as possible)

Display:
  --debugger                   Open debugger view on startup

DZRP (DeZog Remote Protocol):
  --dzrp                       Enable DZRP debug server on TCP socket
                               (default: 127.0.0.1:11000). Connect with DeZog
                               (VS Code) or any DZRP-speaking client.
  --dzrp-port <PORT>           Set DZRP port (default: 11000)
  --dzrp-bind <ADDR>           Bind address: 127.0.0.1 (local) or 0.0.0.0 (remote)
  --trace                      Enable DZRP protocol tracing
  --no-keyboard-hook           Disable keyboard input (useful for remote debugging)
  --with-keyboard-hook         Force enable keyboard input in DZRP mode

Debugging:
  --break-at <SPEC>            Break into debugger when condition is met

Frame Dump:
  --dump-frames <DIR>          Save every frame as PNG to directory
  --dump-keyframes <DIR>       Save frames only when screen content changes
  --frame-spec <SPEC>          Frame range specification (default: all frames)
  --no-border                  Capture 256x192 screen only (no border area)
  --timestamp                  Add hex microsecond timestamps to filenames

Trigger Syntax (used in --break-at and --frame-spec):
  PC=4000                      When program counter hits address (hex)
  SP=FFFF                      When stack pointer equals value (hex)
  OP=FF                        When opcode byte is executed (hex, e.g. FF=RST 38)
  T=100000                     When T-state counter reaches value (decimal)
  DI:HALT                      When CPU executes HALT with interrupts disabled
                               (dead state — --break-at only)

Frame Spec Syntax:
  100                          Single frame number
  100..200                     Inclusive range
  1,50,100..200                Comma-separated mix
  load-start, load-end         File load events
  PC=4000..PC=4000+50          Trigger with offset (50 frames after PC hits 0x4000)
  T=100000..T=100000+200       200 frames after T-state 100000

Environment Variables:
  DZRP_HOST                    Default DZRP bind address (default: 127.0.0.1)
  DZRP_PORT                    Default DZRP port (default: 11000)

Examples:
  zxs game.z80                                     Load and run a game
  zxs --dzrp --dzrp-bind 0.0.0.0                   Start with DZRP debug server
  zxs --break-at ""PC=8000"" game.z80                Break when PC hits 0x8000
  zxs --break-at ""T=100000,OP=FF"" game.z80         Break at T-state or RST 38
  zxs --break-at ""DI:HALT"" game.z80                 Break on stuck CPU (DI + HALT)
  zxs --dump-frames /tmp/frames game.z80            Capture every frame
  zxs --dump-keyframes /tmp/kf --no-border game.z80 Capture screen changes, no border
  zxs --dump-frames /tmp/f --frame-spec ""load-end..load-end+200"" game.tap");
    }

    /// <summary>
    /// Check if an argument is a value for a preceding flag.
    /// </summary>
    private static bool IsArgValue(string[] args, string arg)
    {
        var index = Array.IndexOf(args, arg);
        if (index <= 0)
            return false;
        var prev = args[index - 1];
        return prev is "--dzrp-port" or "--dzrp-bind" or "--dump-frames" or "--dump-keyframes" or "--frame-spec" or "--break-at";
    }
}

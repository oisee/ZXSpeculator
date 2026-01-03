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
    public ZxSpectrum Speccy { get; }
    public ZxDisplay Display { get; }
    public Settings Settings => Settings.Instance;
    public MruFiles Mru { get; }
    public RomSelectorViewModel RomSelectorDetails { get; }

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

        Mru = new MruFiles().InitFromString(Settings.MruFiles);
        Mru.OpenRequested += (_, file) => Speccy.LoadRom(file);

        RomSelectorDetails = new RomSelectorViewModel(Speccy);
        RomSelectorDetails.LoadBasicRomAction(Settings.RomFile);

        // Parse DZRP command-line arguments (override settings)
        ParseDzrpArgs(args);

        Settings.PropertyChanged += (_, _) => OnSettingsChanged(true);
        OnSettingsChanged(false);

        if (args != null && args.Length > 0)
        {
            // Find last arg that's not a DZRP flag
            var file = args.LastOrDefault(a => !a.StartsWith("--dzrp") && !IsArgValue(args, a));
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
    /// Parse DZRP-related command-line arguments.
    /// Supported: --dzrp, --dzrp-port N, --dzrp-bind ADDR
    /// </summary>
    private void ParseDzrpArgs(string[] args)
    {
        if (args == null || args.Length == 0)
            return;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--dzrp":
                    Settings.IsDzrpEnabled = true;
                    Console.WriteLine("DZRP: Enabled via command line");
                    break;

                case "--dzrp-port" when i + 1 < args.Length && int.TryParse(args[i + 1], out var port):
                    Settings.DzrpPort = port;
                    Console.WriteLine($"DZRP: Port set to {port}");
                    i++; // Skip the value
                    break;

                case "--dzrp-bind" when i + 1 < args.Length:
                    Settings.DzrpBindAddress = args[i + 1];
                    Console.WriteLine($"DZRP: Bind address set to {args[i + 1]}");
                    i++; // Skip the value
                    break;
            }
        }
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
        return prev == "--dzrp-port" || prev == "--dzrp-bind";
    }
}

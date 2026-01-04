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

using System.IO;
using CSharp.Core.Settings;
using Speculator.Core.Dzrp;

namespace Speculator.ViewModels;

/// <summary>
/// Application settings.
/// </summary>
public class Settings : UserSettingsBase
{
    public static Settings Instance { get; } = new Settings();

    override protected void ApplyDefaults()
    {
        IsCrt = true;
        DisplayCrtHelp = true;
        IsAmbientBlurred = true;
        IsSoundEnabled = true;
        MruFiles = string.Empty;
        UseSpeccyColors = true;
        IsDzrpEnabled = false;
        // Use environment variables as defaults if set (DZRP_PORT, DZRP_HOST)
        DzrpPort = DzrpServer.GetDefaultPort();
        DzrpBindAddress = DzrpServer.GetDefaultBindAddress();
        SkipKeyboardHook = false;
    }
    
    public bool IsCrt
    {
        get => Get<bool>();
        set => Set(value);
    }

    public bool DisplayCrtHelp
    {
        get => Get<bool>();
        set => Set(value);
    }

    public bool EmulateCursorJoystick
    {
        get => Get<bool>();
        set => Set(value);
    }

    public bool IsAmbientBlurred
    {
        get => Get<bool>();
        set => Set(value);
    }

    public bool IsSoundEnabled
    {
        get => Get<bool>();
        set => Set(value);
    }

    public string MruFiles
    {
        get => Get<string>();
        set => Set(value);
    }

    public FileInfo RomFile
    {
        get => Get<FileInfo>();
        set => Set(value);
    }

    public bool UseSpeccyColors
    {
        get => Get<bool>();
        set => Set(value);
    }

    public bool UseBbcColors
    {
        get => Get<bool>();
        set => Set(value);
    }

    public bool UseC64Colors
    {
        get => Get<bool>();
        set => Set(value);
    }

    /// <summary>
    /// Enable DZRP (DeZog Remote Protocol) server for VS Code debugging.
    /// </summary>
    public bool IsDzrpEnabled
    {
        get => Get<bool>();
        set => Set(value);
    }

    /// <summary>
    /// DZRP server port (default 11000).
    /// Can be overridden via DZRP_PORT environment variable.
    /// </summary>
    public int DzrpPort
    {
        get => Get<int>();
        set => Set(value);
    }

    /// <summary>
    /// DZRP server bind address.
    /// Use "127.0.0.1" for local-only (secure), "0.0.0.0" for remote access.
    /// Can be overridden via DZRP_HOST environment variable.
    /// </summary>
    public string DzrpBindAddress
    {
        get => Get<string>();
        set => Set(value);
    }

    /// <summary>
    /// Skip global keyboard hook (for DZRP mode on macOS).
    /// When true, keyboard input is disabled. Useful for headless/remote debugging.
    /// </summary>
    public bool SkipKeyboardHook
    {
        get => Get<bool>();
        set => Set(value);
    }
}
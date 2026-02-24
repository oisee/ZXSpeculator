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

using System.Diagnostics;
using CSharp.Core.Extensions;
using CSharp.Core.ViewModels;
using Speculator.Core.Tape;

// ReSharper disable InconsistentNaming
namespace Speculator.Core;

public partial class CPU : ViewModelBase
{
    private readonly SoundHandler m_soundHandler;
    private Alu TheAlu { get; }
    private IPortHandler ThePortHandler { get; set; }
    private Thread m_cpuThread;
    private readonly AutoResetEvent m_debuggerTickEvent = new AutoResetEvent(false);
    private bool m_shutdownRequested;
    private bool m_resetRequested;
    private bool m_isDebuggerActive;
    private int m_previousScanline = -1;

    public MachineProfile Profile { get; }
    private ContentionTable m_contentionTable;

    /// <summary>
    /// T-states per interrupt (frame). Determined by machine profile.
    /// </summary>
    public int TStatesPerInterrupt { get; }

    /// <summary>
    /// CPU clock frequency in Hz. Determined by machine profile.
    /// </summary>
    public double TStatesPerSecond { get; }

    public event EventHandler PoweredOff;
    public event EventHandler LoadRequested;

    
    public long TStatesSinceCpuStart { get; private set; }

    /// <summary>
    /// Called immediately after an instruction has been processed and PC incremented.
    /// </summary>
    public event EventHandler<(int elapsedTicks, ushort prevPC, ushort currentPC)> Ticked;
    
    /// <summary>
    /// Called immediately after an interrupt jump has been handled.
    /// </summary>
    public event EventHandler InterruptFired;
    
    /// <summary>
    /// Called periodically throughout each 1/50th second.
    /// </summary>
    public event EventHandler<(Memory memory, int scanline)> RenderScanline;
    
    public Z80Instructions InstructionSet { get; }
    public ClockSync ClockSync { get; }
    public Registers TheRegisters { get; }
    public Memory MainMemory { get; }
    public bool IsHalted { get; private set; }
    public object CpuStepLock { get; } = new object();

    /// <summary>
    /// TAP trap loader for instant tape block loading. Set by ZxFileIo when a .tap is loaded.
    /// </summary>
    public TapTrapLoader TapTrap { get; set; }

    /// <summary>
    /// Current T-state position within the current frame.
    /// </summary>
    public int FrameTState => (int)(TStatesSinceCpuStart % TStatesPerInterrupt);

    public CPU(Memory mainMemory, IPortHandler portHandler = null, SoundHandler soundHandler = null, MachineProfile profile = null)
    {
        m_soundHandler = soundHandler;
        MainMemory = mainMemory;
        Profile = profile ?? MachineProfile.ZX48K;
        TStatesPerInterrupt = Profile.TStatesPerFrame;
        TStatesPerSecond = Profile.CpuFreqHz;
        InstructionSet = new Z80Instructions();
        TheRegisters = new Registers();
        TheAlu = new Alu(TheRegisters);
        ThePortHandler = portHandler;
        ClockSync = new ClockSync(TStatesPerSecond, () => TStatesSinceCpuStart, () => TStatesSinceCpuStart = 0);
    }

    /// <summary>
    /// Set the port handler after construction (needed when port handler
    /// requires a reference back to the CPU for T-state queries).
    /// </summary>
    public void SetPortHandler(IPortHandler portHandler)
    {
        ThePortHandler = portHandler;
    }

    /// <summary>
    /// Enable ULA contention delays for memory access in $4000-$7FFF.
    /// </summary>
    public void EnableContention()
    {
        m_contentionTable = new ContentionTable(Profile, true);
    }

    public void SetTStatesSinceCpuStart(long tStates)
    {
        TStatesSinceCpuStart = tStates;
        ClockSync.Reset();
    }

    public void SetSpeed(ClockSync.Speed speed) =>
        ClockSync.SetSpeed(speed);

    public void PowerOnAsync()
    {
        TheRegisters.Clear();
        m_cpuThread = new Thread(RunLoop) { Name = "Z80 CPU" };
        m_cpuThread.Start();
    }

    public void PowerOffAsync()
    {
        PoweredOff?.Invoke(this, EventArgs.Empty);
        m_shutdownRequested = true;
    }

    public void ResetAsync()
    {
        m_resetRequested = true;
        PowerOffAsync();
    }

    /// <summary>
    /// Triggers the event which allows the CPU to 'tick' to the next instruction.
    /// </summary>
    public void DebuggerStep() => m_debuggerTickEvent.Set();

    /// <summary>
    /// Indicates the debugger is active, requiring DebuggerStep() to advance the CPU.
    /// </summary>
    public bool IsDebuggerActive
    {
        get => m_isDebuggerActive;
        set
        {
            if (SetField(ref m_isDebuggerActive, value))
                ClockSync.Reset();
        }
    }
    
    private void RunLoop()
    {
        m_shutdownRequested = false;
        m_resetRequested = false;
        IsHalted = false;
        ClockSync.Reset();
        
        m_soundHandler?.Start();

        while (!m_shutdownRequested)
        {
            // Allow debugger to stall execution.
            if (IsDebuggerActive)
            {
                if (!m_debuggerTickEvent.WaitOne(100))
                    continue;
            }
            else
            {
                // Sync the clock speed.
                ClockSync.SyncWithRealTime();
            }

            lock (CpuStepLock)
            {
                var prevPC = TheRegisters.PC;
                var oldTickCount = TStatesSinceCpuStart;
                Step();
                var elapsedTicks = (int)(TStatesSinceCpuStart - oldTickCount);
                Ticked?.Invoke(this, (elapsedTicks, prevPC, TheRegisters.PC));
            }
        }

        if (m_resetRequested)
        {
            m_resetRequested = false;
            PowerOnAsync();
        }

        m_shutdownRequested = false;
    }
    
    /// <summary>
    /// Run the instruction at the current PC, and handle any interrupt state.
    /// </summary>
    public void Step()
    {
        // TAP trap: intercept ROM LD-BYTES routine for instant tape loading.
        if (TapTrap != null && TapTrap.TryIntercept(this))
            return;

        var oldIFF = TheRegisters.IFF1;

        // Apply ULA contention delay if PC is in contended memory.
        if (m_contentionTable is { IsActive: true } && TheRegisters.PC >= 0x4000 && TheRegisters.PC < 0x8000)
            TStatesSinceCpuStart += m_contentionTable.GetDelay(FrameTState);

        // Execute instruction.
        var tStates = Tick();
        var ticksSinceInterrupt = (int)((TStatesSinceCpuStart % TStatesPerInterrupt) + tStates);
        TStatesSinceCpuStart += tStates;

        // Record speaker state.
        m_soundHandler?.SampleSpeakerState(tStates);

        // Screen build-up. Render the previous (completed) scanline so that all
        // border color events for that scanline have been recorded before rendering.
        var scanline = ticksSinceInterrupt / Profile.TStatesPerLine;
        if (scanline != m_previousScanline)
        {
            if (m_previousScanline >= 0)
                RenderScanline?.Invoke(this, (MainMemory, m_previousScanline));
            m_previousScanline = scanline;
        }
        
        // Special case 'LOAD ""' instruction.
        // (Double-checking the standard Sinclair BASIC ROM is loaded...)
        if (TheRegisters.PC == 0x056A && MainMemory.Peek(0x1540) == 0x53)
            LoadRequested?.Invoke(this, EventArgs.Empty);

        // Time to handle interrupts?
        if (TStatesPerInterrupt == 0 || ticksSinceInterrupt < TStatesPerInterrupt)
            return;

        // Handle MI interrupts.
        if (TheRegisters.IFF1 && !oldIFF)
            return; // This instruction is EI, so wait one instruction.
        
        if (!TheRegisters.IFF1)
            return; // Interrupts are disabled.

        if (IsHalted)
        {
            // The CPU was halted earlier, which ends when an interrupt is triggered.
            IsHalted = false;
            TheRegisters.PC++;
        }

        TheRegisters.IFF1 = TheRegisters.IFF2 = false;
        switch (TheRegisters.IM)
        {
            case 0:
            case 1:
                CallIfTrue(0x0038, true);
                TStatesSinceCpuStart += 17;
                break;
            case 2:
                CallIfTrue(MainMemory.PeekWord((ushort)((TheRegisters.I << 8) | 0xff)), true);
                TStatesSinceCpuStart += 19;
                break;
            default:
                Debug.Fail("Invalid interrupt mode.");
                break;
        }

        InterruptFired?.Invoke(this, EventArgs.Empty);
    }
    
    private byte doIN_addrC()
    {
        var portAddress = (TheRegisters.Main.B << 8) + TheRegisters.Main.C;
        var b = ThePortHandler?.In((ushort)portAddress) ?? 0x00;
        TheRegisters.SignFlag = !Alu.IsBytePositive(b);
        TheRegisters.ZeroFlag = b == 0;
        TheRegisters.HalfCarryFlag = false;
        TheRegisters.ParityFlag = Alu.IsEvenParity(b);
        TheRegisters.SubtractFlag = false;
        TheRegisters.SetFlags53From(b);
        return b;
    }

    public void RETN()
    {
        doRet();
        TheRegisters.IFF1 = TheRegisters.IFF2;
    }

    private void doRet()
    {
        TheRegisters.PC = MainMemory.PeekWord(TheRegisters.SP);
        TheRegisters.SP += 2;
    }

    private bool JumpIfTrue(ushort addr, bool b)
    {
        if (b)
            TheRegisters.PC = addr;

        return b;
    }

    /// <summary>
    /// Push PC onto stack and jump to 'addr'.
    /// </summary>
    private bool CallIfTrue(ushort addr, bool b)
    {
        if (b)
        {
            TheRegisters.SP -= 2;
            MainMemory.Poke(TheRegisters.SP, TheRegisters.PC);
            TheRegisters.PC = addr;
        }

        return b;
    }

    private void doBitTest(byte b, byte i)
    {
        TheRegisters.ZeroFlag = !b.IsBitSet(i);
        TheRegisters.SubtractFlag = false;
        TheRegisters.HalfCarryFlag = true;

        // From 'undocumented' docs.
        TheRegisters.ParityFlag = TheRegisters.ZeroFlag;
        TheRegisters.SignFlag = i == 7 && !Alu.IsBytePositive(b);
        TheRegisters.SetFlags53From(b);
    }

    private void doCPI()
    {
        var oldCarry = TheRegisters.CarryFlag;
        var v = (byte)(TheRegisters.Main.A - MainMemory.Peek(TheRegisters.Main.HL));
        TheAlu.SubtractAndSetFlags(TheRegisters.Main.A, MainMemory.Peek(TheRegisters.Main.HL), false);
        if (TheRegisters.HalfCarryFlag)
            v--;
        TheRegisters.Main.HL++;
        TheRegisters.Main.BC--;
        TheRegisters.ParityFlag = TheRegisters.Main.BC != 0;
        TheRegisters.SubtractFlag = true;
        TheRegisters.CarryFlag = oldCarry;
        TheRegisters.Flag3 = v.IsBitSet(3);
        TheRegisters.Flag5 = v.IsBitSet(1);
    }
        
    private void doCPD()
    {
        var oldCarry = TheRegisters.CarryFlag;
        var v = (byte)(TheRegisters.Main.A - MainMemory.Peek(TheRegisters.Main.HL));
        TheAlu.SubtractAndSetFlags(TheRegisters.Main.A, MainMemory.Peek(TheRegisters.Main.HL), false);
        if (TheRegisters.HalfCarryFlag)
            v--;
        TheRegisters.Main.HL--;
        TheRegisters.Main.BC--;
        TheRegisters.ParityFlag = TheRegisters.Main.BC != 0;
        TheRegisters.SubtractFlag = true;
        TheRegisters.CarryFlag = oldCarry;
        TheRegisters.Flag3 = v.IsBitSet(3);
        TheRegisters.Flag5 = v.IsBitSet(1);
    }

    private void doLDI()
    {
        var v = MainMemory.Peek(TheRegisters.Main.HL);
        MainMemory.Poke(TheRegisters.Main.DE, v);
        TheRegisters.Main.HL++;
        TheRegisters.Main.DE++;
        TheRegisters.Main.BC--;
        TheRegisters.HalfCarryFlag = false;
        TheRegisters.ParityFlag = TheRegisters.Main.BC != 0;
        TheRegisters.SubtractFlag = false;
        TheRegisters.Flag5 = ((byte)(v + TheRegisters.Main.A)).IsBitSet(1);
        TheRegisters.Flag3 = ((byte)(v + TheRegisters.Main.A)).IsBitSet(3);
    }

    private void doLDD()
    {
        var b = MainMemory.Peek(TheRegisters.Main.HL);
        MainMemory.Poke(TheRegisters.Main.DE, b);
        TheRegisters.Main.HL--;
        TheRegisters.Main.DE--;
        TheRegisters.Main.BC--;
        TheRegisters.HalfCarryFlag = false;
        TheRegisters.ParityFlag = TheRegisters.Main.BC != 0;
        TheRegisters.SubtractFlag = false;
        TheRegisters.Flag3 = ((byte)(b + TheRegisters.Main.A)).IsBitSet(3);
        TheRegisters.Flag5 = ((byte)(b + TheRegisters.Main.A)).IsBitSet(1);
    }

    /// <summary>
    /// T-states per line, from the machine profile.
    /// </summary>
    public int TStatesPerLine => Profile.TStatesPerLine;

    public double UpTime => TStatesSinceCpuStart / TStatesPerSecond;
}

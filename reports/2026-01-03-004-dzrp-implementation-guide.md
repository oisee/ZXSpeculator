# DZRP Implementation Guide for Z80 Emulators

## Overview

DZRP (DeZog Remote Protocol) enables external debugging of Z80 programs via VS Code's DeZog extension or custom tools like `mzrun`. This guide explains the implementation in ZX Speculator.

## Architecture

```
┌─────────────────┐     TCP/11000      ┌──────────────────┐
│  DeZog / mzrun  │ ◄────────────────► │   DZRP Server    │
│  (debug client) │                    │                  │
└─────────────────┘                    │  ┌────────────┐  │
                                       │  │DzrpSession │  │ (per connection)
                                       │  └─────┬──────┘  │
                                       │        │         │
                                       │  ┌─────▼──────┐  │
                                       │  │DzrpBridge  │  │ (CPU/Memory access)
                                       │  └─────┬──────┘  │
                                       └────────┼─────────┘
                                                │
                                       ┌────────▼─────────┐
                                       │   Z80 Emulator   │
                                       │  ┌────┐ ┌─────┐  │
                                       │  │CPU │ │Memory│ │
                                       └──┴────┴─┴─────┴──┘
```

## Files Created

```
Speculator.Core/Dzrp/
├── DzrpCommands.cs      # Protocol constants
├── DzrpMessage.cs       # Message parsing/serialization
├── IDzrpDebugBridge.cs  # Interface for CPU access
├── DzrpDebugBridge.cs   # Bridge implementation
├── DzrpBreakpoint.cs    # Breakpoint management
├── DzrpSession.cs       # Per-client protocol handler
├── DzrpServer.cs        # TCP listener
└── PauseEventArgs.cs    # Event data
```

## Protocol Format

All messages are **little-endian**.

### Message Structure
```
┌──────────────┬─────────────┬────────────┬─────────────┐
│ Length (4B)  │ SeqNum (1B) │ CmdID (1B) │ Payload (N) │
└──────────────┴─────────────┴────────────┴─────────────┘

- Length: Total bytes after this field (seqnum + cmd + payload)
- SeqNum: 1-255 for commands, 0 for notifications
- CmdID: Command identifier
- Payload: Command-specific data
```

### Command IDs
```csharp
public static class DzrpCommands
{
    public const byte CMD_INIT = 1;
    public const byte CMD_CLOSE = 2;
    public const byte CMD_GET_REGISTERS = 3;
    public const byte CMD_SET_REGISTER = 4;
    public const byte CMD_CONTINUE = 6;
    public const byte CMD_PAUSE = 7;
    public const byte CMD_READ_MEM = 8;
    public const byte CMD_WRITE_MEM = 9;
    public const byte CMD_ADD_BREAKPOINT = 40;
    public const byte CMD_REMOVE_BREAKPOINT = 41;
    public const byte CMD_STEP_INTO = 17;

    public const byte NTF_PAUSE = 1;  // Notification (seqnum=0)
}
```

## Implementation Details

### 1. DzrpServer (TCP Listener)

```csharp
public class DzrpServer : IDisposable
{
    private TcpListener m_listener;
    private DzrpSession m_currentSession;

    public void Start()
    {
        m_listener = new TcpListener(IPAddress.Parse(bindAddress), port);
        m_listener.Start();

        // Accept loop in background thread
        m_acceptThread = new Thread(AcceptLoop);
        m_acceptThread.Start();
    }

    private void AcceptLoop()
    {
        while (m_isRunning)
        {
            var client = m_listener.AcceptTcpClient();

            // DZRP is 1:1 - disconnect existing client
            m_currentSession?.Dispose();

            m_currentSession = new DzrpSession(client, m_bridge);
            m_currentSession.Start();
        }
    }
}
```

### 2. DzrpSession (Protocol Handler)

Each client connection gets a session that:
- Reads messages in a loop
- Dispatches to command handlers
- Sends responses and notifications

```csharp
public class DzrpSession : IDisposable
{
    private void ProcessLoop()
    {
        while (m_isRunning && m_client.Connected)
        {
            var message = ReadMessage();
            ProcessMessage(message);
        }
    }

    private void ProcessMessage(DzrpMessage msg)
    {
        switch (msg.CommandId)
        {
            case DzrpCommands.CMD_INIT:
                HandleInit(msg);
                break;
            case DzrpCommands.CMD_PAUSE:
                HandlePause(msg);
                break;
            case DzrpCommands.CMD_CONTINUE:
                HandleContinue(msg);
                break;
            // ... etc
        }
    }
}
```

### 3. DzrpDebugBridge (CPU Interface)

Bridges DZRP commands to your emulator's CPU/Memory:

```csharp
public class DzrpDebugBridge : IDzrpDebugBridge
{
    private readonly CPU m_cpu;

    public event EventHandler<PauseEventArgs> Paused;
    public event EventHandler Continued;

    public void Pause()
    {
        m_cpu.IsDebuggerActive = true;  // Stop CPU loop
        Paused?.Invoke(this, new PauseEventArgs(PauseReason.ManualBreak, m_cpu.PC));
    }

    public void Continue(ushort[] tempBreakpoints = null)
    {
        // Set up temporary breakpoints for step-over
        foreach (var addr in tempBreakpoints ?? Array.Empty<ushort>())
            AddTempBreakpoint(addr);

        m_cpu.IsDebuggerActive = false;  // Resume CPU loop
        Continued?.Invoke(this, EventArgs.Empty);
    }

    public byte[] GetAllRegisters()
    {
        // Pack registers per DZRP spec (28 bytes):
        // PC(2), SP(2), AF(2), BC(2), DE(2), HL(2),
        // IX(2), IY(2), AF'(2), BC'(2), DE'(2), HL'(2),
        // I(1), R(1), IM(1), reserved(1)
        var buffer = new List<byte>(28);
        WriteWord(buffer, m_cpu.PC);
        WriteWord(buffer, m_cpu.SP);
        WriteWord(buffer, m_cpu.AF);
        // ... etc
        return buffer.ToArray();
    }

    public byte[] ReadMemory(ushort address, ushort length)
    {
        var result = new byte[length];
        for (var i = 0; i < length; i++)
            result[i] = m_cpu.Memory.Peek((ushort)(address + i));
        return result;
    }

    public void WriteMemory(ushort address, byte[] data)
    {
        for (var i = 0; i < data.Length; i++)
            m_cpu.Memory.Poke((ushort)(address + i), data[i]);
    }
}
```

### 4. Breakpoint Implementation

```csharp
public class DzrpBreakpoint
{
    private static int s_nextId = 1;

    public int Id { get; }
    public ushort Address { get; }
    public event EventHandler Hit;

    public void Enable()
    {
        m_cpu.Ticked += OnCpuTicked;
    }

    private void OnCpuTicked(object sender, (int ticks, ushort prevPC, ushort currentPC) args)
    {
        if (args.currentPC == Address)
        {
            m_cpu.IsDebuggerActive = true;  // Pause
            Hit?.Invoke(this, EventArgs.Empty);
        }
    }
}
```

### 5. Message Serialization

```csharp
public class DzrpMessage
{
    public byte SequenceNumber { get; }
    public byte CommandId { get; }
    public byte[] Payload { get; }

    public static byte[] CreateResponse(byte seqNum, byte cmdId, byte[] payload)
    {
        var length = 2 + payload.Length;  // seqnum + cmd + payload
        var result = new byte[4 + length];

        // Length (little-endian)
        result[0] = (byte)(length & 0xFF);
        result[1] = (byte)((length >> 8) & 0xFF);
        result[2] = (byte)((length >> 16) & 0xFF);
        result[3] = (byte)((length >> 24) & 0xFF);

        result[4] = seqNum;
        result[5] = cmdId;
        Array.Copy(payload, 0, result, 6, payload.Length);

        return result;
    }

    public static byte[] CreateNotification(byte notifId, byte[] payload)
    {
        return CreateResponse(0, notifId, payload);  // SeqNum=0 for notifications
    }
}
```

## Command Handlers

### CMD_INIT (1)
```
Request:  (empty)
Response: [Error:1][MachineType:1][Name:null-terminated string]

MachineType: 1=ZX16K, 3=ZX48K, 4=ZX128K, 7=ZXNext
```

### CMD_PAUSE (7)
```
Request:  (empty)
Response: [Error:1]
Notification: NTF_PAUSE sent after pausing
```

### CMD_CONTINUE (6)
```
Request:  [TempBpCount:2][TempBp1:2][TempBp2:2]...
Response: [Error:1]

TempBpCount: Number of temporary breakpoints (for step-over)
TempBpN: Address of temporary breakpoint
```

### CMD_GET_REGISTERS (3)
```
Request:  (empty)
Response: [Error:1][Registers:28]

Register layout (28 bytes, little-endian words):
  PC, SP, AF, BC, DE, HL, IX, IY,
  AF', BC', DE', HL', I(byte), R(byte), IM(byte), reserved(byte)
```

### CMD_SET_REGISTER (4)
```
Request:  [RegId:1][Value:2]
Response: [Error:1]

RegId: 0=PC, 1=SP, 2=AF, 3=BC, 4=DE, 5=HL, 6=IX, 7=IY,
       8=AF', 9=BC', 10=DE', 11=HL', 12=I, 13=R, 14=IM
```

### CMD_READ_MEM (8)
```
Request:  [Address:2][Length:2]
Response: [Error:1][Data:N]
```

### CMD_WRITE_MEM (9)
```
Request:  [Address:2][Length:2][Data:N]
Response: [Error:1]
```

### CMD_ADD_BREAKPOINT (40)
```
Request:  [Address:2]
Response: [Error:1][BreakpointId:2]
```

### CMD_REMOVE_BREAKPOINT (41)
```
Request:  [BreakpointId:2]
Response: [Error:1]
```

### NTF_PAUSE (Notification)
```
Payload: [Reason:1][Address:2]

Reason: 1=ManualBreak, 2=BreakpointHit, 3=Watchpoint, 4=Assertion, 5=Other
Address: Current PC
```

## CPU Integration Requirements

Your emulator's CPU needs:

1. **Pause flag**: `bool IsDebuggerActive` - when true, CPU loop should block/wait
2. **Step function**: Execute single instruction then pause
3. **Tick event**: Notification after each instruction (for breakpoint checking)
4. **Thread safety**: Lock for register/memory access during debugging

```csharp
// Example CPU loop
public void RunLoop()
{
    while (m_isRunning)
    {
        if (IsDebuggerActive)
        {
            Thread.Sleep(10);  // Or use wait handle
            continue;
        }

        var pc = PC;
        ExecuteInstruction();
        Ticked?.Invoke(this, (ticks, pc, PC));
    }
}

public void DebuggerStep()
{
    // Execute exactly one instruction
    var pc = PC;
    ExecuteInstruction();
    Ticked?.Invoke(this, (ticks, pc, PC));
}
```

## UI Integration

When DZRP pauses, update your emulator's UI:

```csharp
// Subscribe to DZRP events
dzrpServer.ExecutionPaused += (_, args) =>
{
    // Must dispatch to UI thread!
    Dispatcher.UIThread.InvokeAsync(() =>
    {
        debugger.StartDebugging();
        debugger.RefreshUi();
        display.ForceRender(memory);  // Refresh screen
    });
};

dzrpServer.ExecutionContinued += (_, _) =>
{
    Dispatcher.UIThread.InvokeAsync(() =>
    {
        debugger.StopDebugging();
    });
};
```

## Testing with DeZog

1. Start emulator with DZRP enabled
2. Create `.vscode/launch.json`:
```json
{
  "version": "0.2.0",
  "configurations": [{
    "type": "dezog",
    "request": "launch",
    "name": "Debug",
    "remoteType": "dzrp",
    "dzrp": { "hostname": "localhost", "port": 11000 }
  }]
}
```
3. Press F5 in VS Code

## Debugging Tips

Add `--trace` flag to log all protocol messages:
```
[DZRP] <-- Recv: seq=1 CMD_INIT payload=0 bytes
[DZRP] --> Resp: seq=1 CMD_INIT payload=15 bytes
[DZRP] <-- Recv: seq=2 CMD_PAUSE payload=0 bytes
[DZRP] --> Notif: NTF_PAUSE reason=ManualBreak PC=$8000
```

## Key Lessons Learned

1. **Thread safety**: DZRP runs on background thread, UI updates need dispatching
2. **Notifications**: Always send NTF_PAUSE after pause/step/breakpoint hit
3. **Screen refresh**: Force render display when paused (CPU doesn't run normal scanline cycle)
4. **Step-over**: Implemented via CMD_CONTINUE with temporary breakpoint at next instruction
5. **Register bindings**: If your UI framework doesn't auto-refresh nested bindings, create wrapper properties

## Command-Line Flags

The emulator supports these DZRP-related flags:

| Flag | Description |
|------|-------------|
| `--dzrp` | Enable DZRP server (default: port 11000, local only) |
| `--dzrp-bind <addr>` | Bind address: `127.0.0.1` (local) or `0.0.0.0` (remote) |
| `--dzrp-port <port>` | Custom port (default: 11000) |
| `--debugger` | Open debugger view on startup |
| `--trace` | Enable DZRP protocol tracing |

## Example Session

```bash
# Terminal 1: Start emulator
./Speculator --dzrp --dzrp-bind 0.0.0.0 --debugger --trace

# Terminal 2: Run debugger client
./mzrun --host 192.168.1.100 --debug program.bin
```

## References

- [DeZog Extension](https://marketplace.visualstudio.com/items?itemName=maziac.dezog)
- [DZRP Protocol Specification](https://github.com/maziac/DeZog/blob/main/documentation/dezogprotocol.md)

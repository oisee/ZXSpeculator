# DZRP Implementation Plan for ZX Speculator

**Date**: 2026-01-03
**Topic**: DeZog Remote Protocol (DZRP) Integration

## Overview

Implement DZRP server to enable VS Code debugging via [DeZog extension](https://github.com/maziac/DeZog).

**Port**: 11000 (configurable)
**Protocol**: TCP socket, little-endian, request-response

## Research Findings

### Debug Protocol Options Evaluated

| Protocol | Best For | Complexity | VS Code Support |
|----------|----------|------------|-----------------|
| **DZRP** (DeZog Remote Protocol) | ZX Spectrum dev | Medium | Via DeZog extension |
| **GDB-RSP** | Universal debugging | Higher | Via generic GDB adapters |
| **DAP** | Native VS Code | N/A | DeZog already translates DAP to DZRP |

### Existing ZX Speculator Debug Infrastructure

The emulator already has solid debug foundations:

- **Breakpoints**: `Debugger.cs` with `SingleBreakpoint` class
- **CPU Stepping**: `CPU.DebuggerStep()` and `IsDebuggerActive` flag
- **Register Access**: Full Z80 registers in `Registers.cs`
- **Memory Access**: `MainMemory.Peek()` / `Poke()`
- **Disassembly**: `CPU.Disassemble()`
- **Thread Model**: CPU runs on dedicated thread with `AutoResetEvent` for debugger control

### DZRP Protocol Specification

Message format:
```
[4 bytes: length (little-endian)]
[1 byte: sequence number (1-255 for commands, 0 for notifications)]
[1 byte: command ID]
[N bytes: payload]
```

Core commands:
- `CMD_INIT` (1) - Handshake, report machine type
- `CMD_CLOSE` (2) - Close session
- `CMD_GET_REGISTERS` (3) - Return all Z80 registers
- `CMD_SET_REGISTER` (4) - Set individual register
- `CMD_CONTINUE` (6) - Resume execution
- `CMD_PAUSE` (7) - Pause execution
- `CMD_READ_MEM` (8) - Read memory block
- `CMD_WRITE_MEM` (9) - Write memory block
- `CMD_ADD_BREAKPOINT` (40) - Add breakpoint
- `CMD_REMOVE_BREAKPOINT` (41) - Remove breakpoint
- `NTF_PAUSE` - Notification when execution stops

## Architecture

```
DzrpServer (TCP listener on port 11000)
    └── DzrpSession (per client, dedicated thread)
            └── DzrpDebugBridge
                    ├── CPU.IsDebuggerActive (pause/continue)
                    ├── CPU.CpuStepLock (thread-safe access)
                    ├── CPU.Ticked event (breakpoint detection)
                    ├── Registers (all Z80 registers)
                    └── Memory.Peek()/Poke()
```

## New Files

All in `Speculator/Speculator.Core/Dzrp/`:

| File | Purpose |
|------|---------|
| `DzrpCommands.cs` | Command ID constants |
| `DzrpMessage.cs` | Message parsing/serialization |
| `IDzrpDebugBridge.cs` | Interface for CPU/debugger access |
| `DzrpDebugBridge.cs` | Bridge implementation |
| `DzrpBreakpoint.cs` | DZRP-managed breakpoint with unique ID |
| `DzrpSession.cs` | Per-client protocol handler |
| `DzrpServer.cs` | TCP listener |

## Files to Modify

| File | Changes |
|------|---------|
| `ZxSpectrum.cs` | Add `DzrpServer` field, `EnableDzrp()`/`DisableDzrp()` |
| `Settings.cs` | Add `IsDzrpEnabled`, `DzrpPort` properties |
| `MainWindowViewModel.cs` | Wire up DZRP enable/disable |

## Source Mapping (How it works)

The emulator does NOT need to handle source mapping. The workflow is:
1. Assembler (sjasmplus, z80asm) generates `.sld` or `.lst` files with debug info
2. DeZog reads these files and creates source-to-address mapping
3. Emulator just reports PC and handles breakpoints

## Sources

- [DeZog GitHub](https://github.com/maziac/DeZog)
- [DeZog VS Code Marketplace](https://marketplace.visualstudio.com/items?itemName=maziac.dezog)
- [DeZog Protocol Documentation](https://github.com/maziac/DeZog/blob/main/design/DeZogProtocol.md)
- [TLMBoy GDB-RSP Implementation](https://www.chciken.com/tlmboy/2022/04/03/gdb-z80.html)
- [GDB Remote Protocol Docs](https://sourceware.org/gdb/current/onlinedocs/gdb.html/Remote-Protocol.html)

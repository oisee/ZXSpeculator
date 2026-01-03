# DZRP Implementation: Purpose and .minz Language Integration

**Date**: 2026-01-03
**Topic**: Why DZRP was implemented and how it supports .minz language development

## Executive Summary

This fork of ZX Speculator adds DZRP (DeZog Remote Protocol) support to enable **external debugging** of Z80 programs. The primary goal is to create a fully-featured development environment for the **.minz programming language** - a modern language that compiles to Z80 machine code for the ZX Spectrum.

## What is DZRP?

DZRP (DeZog Remote Protocol) is a TCP-based debugging protocol that allows external tools to:
- **Control execution**: Pause, continue, step through code
- **Inspect state**: Read CPU registers, memory contents
- **Set breakpoints**: Stop at specific addresses
- **Modify state**: Write to registers and memory

The protocol is used by [DeZog](https://github.com/maziac/DeZog), a popular VS Code extension for Z80 development.

## Why This Fork?

### The .minz Language Vision

The .minz language aims to be a modern, type-safe language that compiles to efficient Z80 machine code. To develop and debug .minz programs effectively, developers need:

1. **Source-level debugging** - Step through .minz code, not just assembly
2. **Variable inspection** - See .minz variables, not just registers
3. **Breakpoints** - Set breakpoints in .minz source files
4. **Live testing** - Run code on an accurate emulator

### The Integration Challenge

Existing ZX Spectrum emulators either:
- Have no external debugging interface (closed ecosystem)
- Use proprietary protocols (vendor lock-in)
- Lack accuracy for development (timing issues)

ZX Speculator is:
- **Open source** (C#, cross-platform)
- **Accurate** (passes ZEXALL and FUSE tests)
- **Well-architected** (clean separation of concerns)
- **Now DZRP-enabled** (standard protocol)

## Architecture Overview

```
┌─────────────────────────────────────────────────────────────┐
│                    .minz Development                         │
├─────────────────────────────────────────────────────────────┤
│                                                              │
│  ┌──────────────┐    ┌──────────────┐    ┌──────────────┐   │
│  │ .minz Source │───▶│ minz Compiler│───▶│ Z80 Binary   │   │
│  │    Files     │    │              │    │ + Debug Info │   │
│  └──────────────┘    └──────────────┘    └──────────────┘   │
│                                                 │            │
│                                                 ▼            │
│  ┌──────────────────────────────────────────────────────┐   │
│  │                    VS Code                            │   │
│  │  ┌────────────┐  ┌────────────┐  ┌────────────────┐  │   │
│  │  │ .minz      │  │ DeZog      │  │ Debug Console  │  │   │
│  │  │ Extension  │  │ Extension  │  │ Variables/Watch│  │   │
│  │  └────────────┘  └─────┬──────┘  └────────────────┘  │   │
│  └────────────────────────┼─────────────────────────────┘   │
│                           │ DAP                              │
│                           ▼                                  │
│  ┌──────────────────────────────────────────────────────┐   │
│  │                 DeZog Debug Adapter                   │   │
│  │            (Translates DAP ↔ DZRP)                    │   │
│  └────────────────────────┬─────────────────────────────┘   │
│                           │ DZRP (TCP:11000)                 │
│                           ▼                                  │
│  ┌──────────────────────────────────────────────────────┐   │
│  │              ZX Speculator (This Fork)                │   │
│  │  ┌────────────┐  ┌────────────┐  ┌────────────────┐  │   │
│  │  │ DZRP Server│  │ Z80 CPU    │  │ ZX Spectrum    │  │   │
│  │  │ (New!)     │  │ Emulation  │  │ Hardware       │  │   │
│  │  └────────────┘  └────────────┘  └────────────────┘  │   │
│  └──────────────────────────────────────────────────────┘   │
│                                                              │
└─────────────────────────────────────────────────────────────┘
```

## Source Mapping for .minz

For source-level debugging, the .minz compiler needs to generate debug information that DeZog can understand. Supported formats:

### Option 1: SLD Files (Source Level Debug)
```
|SLD.DATA.VERSION|1
|FILE|0|/path/to/program.minz
|LINE|0|10|0x8000|5
|LINE|0|11|0x8005|3
```

### Option 2: List Files
```
# File: program.minz
     10  0x8000  3E 42       ld a, 66    ; let x = 66
     11  0x8002  32 00 C0    ld (0xC000), a
```

### Option 3: Symbol Maps
```json
{
  "labels": {
    "main": "0x8000",
    "variable_x": "0xC000"
  },
  "sourceMap": {
    "0x8000": {"file": "program.minz", "line": 10},
    "0x8005": {"file": "program.minz", "line": 11}
  }
}
```

## Benefits of This Approach

### For .minz Development
- **Single debugger** - One tool for .minz and assembly
- **Standard protocol** - No custom debugger needed
- **Rich UI** - Full VS Code debugging experience
- **Cross-platform** - Works on Mac, Windows, Linux

### For the ZX Spectrum Community
- **Modern tooling** - Professional debugging in VS Code
- **Accurate emulation** - ZX Speculator's proven accuracy
- **Open protocol** - Other tools can integrate too

## Implementation Status

### Completed (This Fork)
- [x] DZRP server (TCP port 11000)
- [x] Core commands: INIT, CLOSE, GET_REGISTERS, SET_REGISTER
- [x] Execution control: CONTINUE, PAUSE
- [x] Memory access: READ_MEM, WRITE_MEM
- [x] Breakpoints: ADD_BREAKPOINT, REMOVE_BREAKPOINT
- [x] Notifications: NTF_PAUSE

### Future Work
- [ ] .minz compiler with debug info generation
- [ ] .minz VS Code extension
- [ ] Enhanced breakpoint conditions
- [ ] Watchpoints (memory breakpoints)
- [ ] Step over/step out support

## Getting Started

1. **Enable DZRP** in ZX Speculator settings
2. **Install DeZog** VS Code extension
3. **Configure launch.json** (see SDK Integration Guide)
4. **Load your Z80 binary** in the emulator
5. **Start debugging** from VS Code

## References

- [DeZog Extension](https://marketplace.visualstudio.com/items?itemName=maziac.dezog)
- [DZRP Protocol Specification](https://github.com/maziac/DeZog/blob/main/design/DeZogProtocol.md)
- [ZX Speculator Original](https://github.com/deanthecoder/ZXSpeculator)

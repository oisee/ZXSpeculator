# ZX Speculator Fork: Changelog and Rationale

This document covers every feature added to the [oisee/ZXSpeculator](https://github.com/oisee/ZXSpeculator) fork since it diverged from [deanthecoder/ZXSpeculator](https://github.com/deanthecoder/ZXSpeculator). The fork's primary goal is to turn a standalone emulator into a **development platform** for Z80/ZX Spectrum language projects (specifically the .minz programming language), while keeping it fully functional as a regular emulator.

---

## Phase 1: DZRP and Remote Debugging (January 2026)

### DZRP Protocol Server

**What**: Full implementation of the DeZog Remote Protocol (DZRP) — a TCP-based protocol that lets VS Code's [DeZog extension](https://marketplace.visualstudio.com/items?itemName=maziac.dezog) control the emulator externally.

**Why**: The .minz language compiler targets Z80 machine code. To debug .minz programs, we need source-level debugging in a modern IDE rather than the emulator's built-in hex-level debugger. DZRP is the standard protocol DeZog speaks.

**Components** (all in `Speculator.Core/Dzrp/`):
- `DzrpServer.cs` — TCP listener on port 11000 (configurable)
- `DzrpSession.cs` — Per-client protocol handler
- `DzrpDebugBridge.cs` — Bridges DZRP commands to CPU registers, memory, breakpoints
- `DzrpMessage.cs` — Little-endian wire format serialization
- `DzrpCommands.cs` — Protocol command constants

**Supported commands**: Init, Close, Get/Set Registers, Continue, Pause, Step Into, Read/Write Memory, Add/Remove Breakpoint, Pause Notification.

**CLI flags**: `--dzrp`, `--dzrp-port <N>`, `--dzrp-bind <ADDR>`, `--trace`, `--debugger`, `--no-keyboard-hook`

### Configurable Bind Address and Environment Variables

**What**: DZRP server can bind to `0.0.0.0` for remote access. Configuration via `DZRP_HOST` and `DZRP_PORT` environment variables.

**Why**: Run the emulator on a Mac with a display, debug from a headless Linux dev machine over the network. Environment variables let all tools (emulator, taploader, mzrun) share one configuration.

### taploader Tool

**What**: Standalone Go program (`tools/taploader/`) that parses TAP files and loads CODE blocks into the emulator via DZRP's write-memory command.

**Why**: Before trap loading existed in the emulator itself, this was the only way to bypass the minutes-long tape loading process during development. It works with any DZRP-speaking emulator, not just this one.

### Debugger UI Fixes

**What**: Register display updates during DZRP stepping, screen refresh via `ForceRender()`, `--trace` for protocol debugging.

**Why**: The original debugger UI was designed for the built-in debugger only. DZRP pauses/steps happen from an external thread, so the UI wasn't updating. These fixes make external debugging actually usable.

### Avalonia Upgrade (11.0.7 to 11.0.9)

**What**: Framework version bump.

**Why**: Avalonia 11.0.7 crashed on macOS when command-line arguments were passed. This blocked all CLI flags.

### .NET 8.0 Upgrade

**What**: All projects upgraded from .NET 7.0 to .NET 8.0.

**Why**: .NET 7.0 reached end-of-life. .NET 8.0 is the current LTS release.

---

## Phase 2: Frame Dumping and Automation (February 22-23, 2026)

### Frame Dump System

**What**: Capture emulator frames as PNG files, controlled by a flexible frame specification language.

**Why**: The .minz book project ("Coding the Impossible") needs automated screenshots of programs running in the emulator. Manual screenshots don't scale when regenerating hundreds of figures after code changes.

**Components**:
- `FrameSpec.cs` — Parser for frame range specifications (`100..200`, `load-end..load-end+50`, `PC=8000..PC=8000+100`)
- `FrameDumper.cs` — Subscribes to display frame events, writes PNGs via producer/consumer pattern
- `PngWriter.cs` — Custom PNG encoder that runs on a background thread (avoids Avalonia SIGSEGV from creating bitmaps off the UI thread)

**CLI flags**: `--dump-frames <dir>`, `--dump-keyframes <dir>`, `--frame-spec <spec>`, `--no-border`, `--timestamp`

### Conditional Debugger Breakpoints

**What**: `--break-at <spec>` opens the debugger when a CPU condition is met.

**Why**: Debugging a program that takes thousands of frames to reach the interesting point. Set `--break-at "PC=8000"` and walk away.

**Components**:
- `BreakAtCondition.cs` — Parses and evaluates trigger conditions
- Triggers: `PC=XXXX`, `SP=XXXX`, `OP=XX`, `T=NNNNN`, `DI:HALT`

### Max Speed Mode

**What**: `--max-speed` disables the real-time clock sync, running the CPU as fast as the host allows.

**Why**: Frame dumping at 50 fps real-time means waiting minutes for a 500-frame capture. At max speed, the same capture completes in seconds.

### Makefile

**What**: Standard `make build / run / test / install / clean` targets. `make install` publishes a self-contained binary and symlinks it to `~/.local/bin/zxs`.

**Why**: The `dotnet run --project Speculator/Speculator/Speculator.csproj` incantation is too long to type. `zxs game.z80` is not.

---

## Phase 3: 128K Spectrum and Advanced Loaders (February 24, 2026)

### Machine Profile System

**What**: `MachineProfile` record defines timing parameters (T-states/line, lines/frame, CPU frequency, contention) for three machines:
- ZX Spectrum 48K (224 T/line, 312 lines, 3.4944 MHz)
- ZX Spectrum 128K (228 T/line, 311 lines, 3.5469 MHz)
- Pentagon 128 (224 T/line, 320 lines, 3.5 MHz, no contention)

**Why**: 128K software requires different timing. Pentagon 128 is the most common platform for Russian demoscene productions (like RAGE by X-Trade). The profile system also enabled ULA memory contention (`ContentionTable.cs`) for cycle-accurate timing when needed.

**CLI flag**: `--machine <48k|128k|pentagon>`, `--contention`

### 16-bit Port Address

**What**: `IPortHandler.Out(byte port, byte b)` changed to `Out(ushort portAddress, byte b)`. All OUT instructions in `CPU_Execute.cs` now pass the full 16-bit address bus value.

**Why**: Port $7FFD (128K bank switching) is decoded using bits 1 and 15 of the full address. With only the low byte, port $7FFD is indistinguishable from port $FD (joystick). This is a prerequisite for 128K support. The change is invisible to 48K programs — the FUSE test suite (1335 tests) still passes.

### 128K Memory Banking

**What**: `Memory.cs` extended with:
- 8 x 16KB RAM bank arrays
- `SwitchBank(page)` — save/restore $C000 region on page switch
- `SwitchRom(rom)` — switch between ROM 0 (128K editor) and ROM 1 (48K BASIC)
- `SetScreenPage(page)` — select display source (bank 5 or bank 7)
- `LockPaging()` — bit 5 of port $7FFD locks paging until reset
- `LoadBank(page, data)` / `SyncFixedBanks()` — for file loaders

Architecture uses "copy-on-switch": the flat `Data[0x10000]` array remains the CPU's view of memory, and bank data is copied in/out of $C000 when the page changes. Banks 5 ($4000) and 2 ($8000) are fixed in the address map and never need copying.

**Why**: 128K Spectrum programs are the majority of the demoscene and many commercial titles. The Pentagon 128 is the target platform for the RAGE megademo.

### Port $7FFD Handling

**What**: `ZxPortHandler.Out()` decodes port $7FFD writes `(portAddress & 0x8002) == 0` and applies: RAM page (bits 0-2), screen page (bit 3), ROM select (bit 4), paging lock (bit 5).

**Why**: This is the hardware register that controls 128K memory banking. Every 128K program writes to it.

### Shadow Screen

**What**: `ZxDisplay.RenderScanlineIntoBuffer()` reads screen data via `Memory.GetScreenByte(offset)` instead of directly from `Data[0x4000+offset]`. When screen page is 7, data comes from bank 7 instead of the fixed $4000 region.

**Why**: 128K programs use the shadow screen for double-buffering — draw on the hidden page, then flip instantly by writing to port $7FFD bit 3.

### 128K ROM Detection

**What**: `Memory.LoadRom()` detects 32KB ROM files and auto-enables 128K mode.

**Why**: The 128K ROM is 32KB (ROM 0 + ROM 1). The 48K ROM is 16KB. Size alone is sufficient to distinguish them. The 128K ROM is not bundled due to copyright — the user supplies it.

### 128K File Format Loading

**What**: `ZxFileIo.LoadZ80()` now handles v2/v3 .z80 files with hardware modes 3-4 (128K Spectrum). Pages 3-10 map to banks 0-7. The port $7FFD state is read from extended header byte 35 and applied after loading. `LoadSna()` detects 128K SNA files by size (>49179 bytes) and reads PC, port $7FFD, and 5 extra 16KB banks.

**Why**: These are the standard snapshot formats. Without 128K support, the emulator rejects most 128K snapshots with "Unsupported model."

### TAP Trap Loading

**What**: `TapTrapLoader` intercepts the Z80 CPU at address $0556 (the ROM's LD-BYTES entry point). Instead of letting the ROM execute its tight polling loop, the trap loader reads the register convention (A=block type, IX=dest, DE=length, CF=load/verify), copies the TAP block data directly into memory, sets the success flags, and returns via RETN.

**Why**: Real-time tape loading takes minutes because the ROM polls port $FE thousands of times per byte, and the CPU must execute every poll at real-time speed. Trap loading is instant. The `--tap-realtime` flag preserves the original behavior for programs with custom loaders that don't use the standard ROM routine.

### TR-DOS / SCL Disk Image Support

**What**: `TrdosLoader` parses .trd (raw 640KB disk image) and .scl (SINCLAIR-header catalog format) files, extracts the file catalog, and auto-loads the first CODE file (falling back to BASIC).

**Why**: TR-DOS is the standard disk system for Pentagon 128 and many Eastern European Spectrum clones. The RAGE megademo and much of the Russian demoscene distributes as .trd or .scl files.

---

## Summary

| Feature | Lines | Purpose |
|---------|-------|---------|
| DZRP server | ~1200 | VS Code debugging for .minz development |
| taploader (Go) | ~850 | Instant TAP loading via DZRP |
| Frame dump system | ~870 | Automated screenshots for book/docs |
| 128K banking | ~510 | Run 128K software and demoscene |
| TAP trap loading | ~150 | Instant tape loading (replaces minutes-long signal emulation) |
| TR-DOS/SCL | ~220 | Load disk images from Russian demoscene |
| Machine profiles | ~140 | Correct timing for 48K/128K/Pentagon |
| CLI + Makefile | ~300 | Developer UX (flags, help, `make install`) |
| **Total** | **~4200** | |

All changes maintain backward compatibility. The FUSE test suite (1335 Z80 instruction accuracy tests) passes after every change.

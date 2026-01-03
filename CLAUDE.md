# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

ZX Speculator is a cross-platform ZX Spectrum 48K emulator written in C#, using Avalonia for the UI. It emulates the Z80 CPU, ULA display, sound via OpenAL, and supports various file formats (.z80, .sna, .tap, .scr, .bin).

**This fork** adds DZRP (DeZog Remote Protocol) support for external debugging via VS Code, enabling .minz language development.

## Build and Run Commands

```bash
# Build the solution
dotnet build Speculator/Speculator.sln

# Run the application
dotnet run --project Speculator/Speculator/Speculator.csproj

# Run all tests
dotnet test Speculator/Speculator.sln

# Run a specific test class
dotnet test Speculator/UnitTests/UnitTests.csproj --filter "FullyQualifiedName~FuseTests"

# Run a single test by name
dotnet test Speculator/UnitTests/UnitTests.csproj --filter "Name=TestRunner"
```

## Architecture

### Project Structure

- **Speculator** - Main Avalonia UI application (entry point: `Program.cs`)
- **Speculator.Core** - Core emulation engine (Z80 CPU, memory, display, sound, file I/O)
- **CSharp.Core** - Shared utilities (UI components, extensions, compression, settings)
- **UnitTests** - NUnit tests including FUSE and ZEXALL Z80 instruction tests

### Key Components in Speculator.Core

- `ZxSpectrum` - Main emulation entry point, orchestrates CPU, display, and sound
- `CPU` / `CPU_Execute.cs` - Z80 processor with instruction execution loop
- `Z80Instructions.cs` / `Z80Instructions_List.cs` - Instruction set definition and lookup tables
- `ALU.cs` - Arithmetic logic unit operations
- `Memory.cs` - 64KB memory model with ROM protection
- `ZxDisplay.cs` - ULA display rendering (scanline-based)
- `ZxPortHandler.cs` - I/O port handling (keyboard, sound, tape)
- `ZxFileIo.cs` - File format loading/saving (.z80, .sna, .tap, etc.)

### DZRP Components (Dzrp/)

- `DzrpServer.cs` - TCP server on port 11000, manages client connections
- `DzrpSession.cs` - Per-client protocol handler
- `DzrpDebugBridge.cs` - Bridges DZRP commands to CPU/memory/registers
- `DzrpBreakpoint.cs` - DZRP-managed breakpoints with unique IDs
- `DzrpMessage.cs` - Message serialization (little-endian)
- `DzrpCommands.cs` - Protocol constants

### Emulation Flow

1. `ZxSpectrum` creates `CPU` with `Memory` and `ZxPortHandler`
2. `CPU.RunLoop()` executes on dedicated thread, calling `Step()` each cycle
3. `Step()` fetches/executes instructions, fires `RenderScanline` events for display
4. Interrupts fire every 69888 T-states (50Hz frame rate)
5. `ClockSync` maintains real-time speed synchronization

## Conventions

- **Target Framework**: .NET 8.0
- **Nullable**: Disabled project-wide
- **UI Framework**: Avalonia 11.x with Material Design
- **Testing**: NUnit with NSubstitute for mocking
- **English**: American English for identifiers and comments
- **License Header**: All source files include the author's copyright header
- **DZRP Thread Safety**: Use `CPU.CpuStepLock` when accessing registers/memory from DZRP

## Test Suites

- **FUSE Tests** (`FuseTests.cs`) - Z80 instruction accuracy tests from the FUSE emulator
- **ZEXALL Tests** (`ZexAllTests.cs`) - Comprehensive Z80 instruction exerciser
- Test data in `UnitTests/FuseTestData/` and `UnitTests/ZexTestData/`

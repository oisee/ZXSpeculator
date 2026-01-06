# ZXSpeculator Release - January 6, 2026

## What's New

### DZRP Environment Variables
Configure DZRP once, use everywhere:
```bash
export DZRP_HOST=192.168.1.5
export DZRP_PORT=11000

./Speculator --dzrp
./taploader game.tap
```

### taploader Tool
New companion tool for instant TAP file loading:
- Bypasses tape emulation completely
- Loads 19KB game in ~100ms instead of minutes
- Built-in step debugger (`--debug`)
- Cross-platform (macOS, Linux, Windows)

### Improvements
- Step debugger now properly follows JP/JR/CALL/RET instructions
- Register UI updates correctly during DZRP debugging
- Screen refreshes during stepping
- Protocol trace mode (`--trace` flag)

## Binaries

### ZXSpeculator (Emulator)
| Platform | File |
|----------|------|
| macOS ARM64 | `osx-arm64/Speculator` |

### taploader (TAP Loader)
| Platform | File |
|----------|------|
| macOS ARM64 | `taploader/taploader-darwin-arm64` |
| macOS Intel | `taploader/taploader-darwin-amd64` |
| Linux x64 | `taploader/taploader-linux-amd64` |
| Windows x64 | `taploader/taploader-windows-amd64.exe` |

## Quick Start

```bash
# 1. Start emulator with DZRP
./osx-arm64/Speculator --dzrp

# 2. Load a TAP file instantly
./taploader/taploader-darwin-arm64 game.tap

# 3. Or with step debugger
./taploader/taploader-darwin-arm64 --debug game.tap
```

## Requirements

- **ZXSpeculator**: macOS 11+ (ARM64), .NET 8 runtime included
- **taploader**: No dependencies (statically linked)

## Documentation

- [DZRP Implementation Guide](../reports/2026-01-03-004-dzrp-implementation-guide.md)
- [Environment Variables](../reports/2026-01-06-001-dzrp-environment-variables.md)
- [taploader README](../tools/taploader/README.md)

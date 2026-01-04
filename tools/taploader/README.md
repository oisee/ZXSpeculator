# taploader

A command-line tool for instantly loading ZX Spectrum TAP files into any DZRP-compatible emulator.

## Overview

`taploader` bypasses traditional tape emulation by:
1. Parsing TAP file structure
2. Extracting CODE blocks
3. Writing directly to emulator memory via DZRP protocol
4. Setting PC and starting execution

This provides **instant loading** regardless of program size - no more waiting for tape loading!

## Supported Emulators

Any emulator implementing the DeZog Remote Protocol (DZRP):
- **ZXSpeculator** (with `--dzrp` flag)
- **ZEsarUX** (built-in DZRP support)
- **CSpect** (with DeZog plugin)

## Building

```bash
cd tools/taploader
go build -o taploader
```

## Usage

```bash
# Basic usage (localhost:11000)
./taploader game.tap

# Show TAP file contents without loading
./taploader --info game.tap

# Load to remote emulator
./taploader --host 192.168.1.5 game.tap

# Load with verbose output
./taploader --verbose game.tap

# Load and start step debugger
./taploader --debug game.tap

# Override load/start addresses
./taploader --load 0x6000 --start 0x6000 game.tap
```

## Options

| Flag | Description |
|------|-------------|
| `--host` | DZRP emulator host (default: localhost, env: DZRP_HOST) |
| `--port` | DZRP port (default: 11000, env: DZRP_PORT) |
| `--load` | Override load address (0 = use TAP address) |
| `--start` | Override start address (0 = same as load) |
| `--timeout` | Execution timeout in seconds (0 = run forever) |
| `--verbose` | Show detailed loading info |
| `--debug` | Interactive step debugger |
| `--info` | Just show TAP contents, don't load |

## Environment Variables

Configure once, use everywhere:

```bash
export DZRP_HOST=192.168.1.5
export DZRP_PORT=11000

# Now just run without flags
./taploader game.tap
```

## TAP File Format

TAP files contain a sequence of blocks:
- **Header blocks** (19 bytes): describe the following data
- **Data blocks**: actual program/data

For CODE blocks, the header contains:
- Block type (3 = CODE)
- 10-character name
- Data length
- Start address (where to load in memory)

## Example

```bash
# Start emulator with DZRP
./Speculator --dzrp

# In another terminal, load a game
./taploader --verbose Experiments/OneSmallStep/OneSmallStep.tap
```

Output:
```
TAP file contains 2 block(s):
  1. BASIC Program: "Loader" (30 bytes, autostart: 10)
  2. CODE: "OneSmallSt" (18920 bytes at $8000)
Loading 1 CODE block(s) via DZRP...
Connecting to localhost:11000...
Connected! Initializing...
Loading "OneSmallSt" (18920 bytes) at $8000...
Starting execution at $8000...
Program running from $8000 (use emulator to stop)
```

## Debugger Commands

When using `--debug`:

| Command | Description |
|---------|-------------|
| `s` / Enter | Step one instruction |
| `c` | Continue execution |
| `r` | Show registers |
| `m` | Show memory at PC |
| `q` | Quit debugger |

## License

Same as ZXSpeculator - free for non-commercial use.

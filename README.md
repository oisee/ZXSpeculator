# ZX Speculator
ZX Speculator is a cross-platform ZX Spectrum 48K emulator written in C#.
![Main UI](img/MainUI.png?raw=true "Main UI")

---

## About This Fork

This fork extends ZX Speculator with **DZRP (DeZog Remote Protocol)** support to enable external debugging via VS Code. The primary goal is to support development of the **.minz programming language** - a modern language that compiles to Z80 machine code.

### Why This Fork?

- **External Debugging**: Control the emulator from VS Code using the [DeZog extension](https://marketplace.visualstudio.com/items?itemName=maziac.dezog)
- **.minz Language Support**: Provides the runtime environment for debugging .minz programs
- **SDK Integration**: Enables building custom development tools via the DZRP protocol
- **Accurate Emulation**: Leverages ZX Speculator's proven Z80 accuracy (passes ZEXALL/FUSE tests)

### Documentation

- [Fork Changelog & Rationale](reports/2026-02-24-001-fork-changelog.md) - Complete history of all changes since fork
- [DZRP Purpose & .minz Integration](reports/2026-01-03-002-dzrp-purpose-and-minz-integration.md) - Why DZRP was implemented
- [SDK/IDE Integration Guide](reports/2026-01-03-003-sdk-ide-integration-guide.md) - How to integrate with your tools
- [DZRP Implementation Guide](reports/2026-01-03-004-dzrp-implementation-guide.md) - Technical implementation details
- [Environment Variables](reports/2026-01-06-001-dzrp-environment-variables.md) - DZRP_HOST/DZRP_PORT configuration

### Tools

- **[taploader](tools/taploader/)** - Instant TAP file loading via DZRP (bypasses tape emulation)

---

## Features
- **128K Spectrum Support**: Full 128K memory banking with port $7FFD paging, shadow screen, and switchable ROM. Loads 128K .z80 (v2/v3) and .sna files. Supply a 32KB 128K ROM to enable (not bundled due to copyright).
- **Instant TAP Loading**: TAP files load instantly by trapping the ROM's LD-BYTES routine. Use `--tap-realtime` for the original signal-based loading.
- **TR-DOS/SCL Disk Images**: Load .trd and .scl disk images directly. The loader parses the catalog and auto-loads the first CODE or BASIC file.
- **DZRP Support** *(This Fork)*: External debugging via VS Code's [DeZog extension](https://marketplace.visualstudio.com/items?itemName=maziac.dezog). Control execution, set breakpoints, inspect registers and memory.
- **Cross Platform**: Built using [Avalonia](https://avaloniaui.net/), ensuring compatibility across various platforms.
- **Key Mapping**: Most keys on a modern PC keyboard are automatically mapped to the Spectrum, making it much easier to type in code.
- **File Format Support**: Compatible with .z80, .bin, .scr, .tap, .sna, .trd, and .scl files.
- **Archive Support**: Load files directly from `.zip` archives.
- **Display**: Optional CRT TV and 'Ambient Blur' effects. ![CRT](img/CRT.png)
- **Joysticks**: Kempston and Cursor joystick support.
- **Sound**: Utilizes [OpenAL](https://www.openal.org/) on Mac and Windows for sound emulation.
- **Integrated Debugger**: Includes a built-in debugger for examination of the Z80 CPU state, including:
  - Instruction stepping.
  - Breakpoints.
  - Instruction history.
- **Rollback**: Die in your favourite game? Accidentally delete a line of code? Continuous recording allows you to 'roll back' to an earlier time. (`F1` will roll back 5 seconds.)
![Rollback](img/Rollback.png)
- **Theming**: The Sinclair BASIC ROM can be customized to allow for:
  - Classic ZX Spectrum input vs a per-character typing strategy. (Courtesy of the [JGH Spectrum 48K ROM](http://mdfs.net/Software/Spectrum/Harston) by J.G.Harston)
  - Selectable colors schemes and fonts.
    - ZX Spectrum
    - BBC Micro
    - Commodore 64
![Speccy Theme](img/Theme_Speccy.png)
![BBC Micro Theme](img/Theme_BBC.png)
![Commodore 64 Theme](img/Theme_C64.png)

## Download
* Download from the [Releases](https://github.com/deanthecoder/ZXSpeculator/releases) section.
* Mac users may need to run the following command to unblock the application:<br>`xattr -d com.apple.quarantine /Applications/ZX\ Speculator.app`

## Development and Testing
Developed on a Mac environment, ZX Speculator is also tested on Windows and passes all the ZEXDOC tests and FUSE emulator tests.
![Jetpac published by Ultimate Play the Game](img/Jetpac.png?raw=true "Jetpac")

## Getting Started
### Loading Files
Common ZX Spectrum image files (.z80, .sna, etc) can be opened from the File->Open menu.

### Loading .tap Files

TAP files now load instantly by default using trap loading (intercepting the ROM's tape routine). Just open the file and it loads in a fraction of a second.

To use the original real-time tape signal loading (slow, but accurate for non-standard loaders):
```bash
zxs --tap-realtime game.tap
```

Or the classic way: Type `Load ""` in BASIC, and the File->Open dialog will open.

### 128K Spectrum Mode

To run 128K software, place a 32KB 128K Spectrum ROM file (not bundled due to copyright) in the `ROMs/` directory alongside the 48K ROM. When a 32KB ROM is detected, the emulator automatically enables:

- **8 x 16KB RAM banks** with port $7FFD paging
- **Shadow screen** (screen page 5 or 7)
- **Switchable ROM** (ROM 0: 128K editor, ROM 1: 48K BASIC)
- **128K .z80 and .sna file loading** with correct bank/page restoration

```bash
zxs --machine 128k game.z80     # 128K Spectrum timing
zxs --machine pentagon game.z80  # Pentagon 128 timing
```

### Loading TR-DOS / SCL Disk Images

TR-DOS (.trd) and SCL (.scl) disk images can be loaded directly. The loader parses the disk catalog and auto-loads the first CODE file (or BASIC file if no CODE is found):

```bash
zxs game.trd
zxs game.scl
```

### Keyboard
Move the mouse pointer to the small keyboard icon at the top-right of the screen to see a representation of the ZX Spectrum keyboard.
![Keyboard](img/Keyboard.png?raw=true "Keyboard")
Many keys on a modern keyboard are automatically mapped to their ZX Spectrum equivalent.  For example, backspace, quotes, math symbols etc.

The left shift key maps to **CAPS SHIFT** on the Spectrum, and the right shift key maps to **SYMBOL SHIFT**.

ESCape will reset the machine.

### Joystick
The emulator will mimic either a Kempston or Cursor joystick.

In both cases the keyboard arrow keys are used for direction control, and the backslash or backtick keys will 'fire'.

## Building From Source

### Prerequisites
- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- `make` (included on macOS/Linux)

### Quick Start

```bash
git clone https://github.com/oisee/ZXSpeculator.git
cd ZXSpeculator

make              # Build the solution
make run          # Build and run the emulator
make test         # Run all tests
make install      # Publish self-contained binary and install to ~/.local/bin/zxs
make clean        # Remove all build artifacts
make uninstall    # Remove the symlink from ~/.local/bin
```

After `make install`, run the emulator from anywhere:
```bash
zxs                          # Launch the emulator
zxs --dzrp                   # Launch with DZRP debugging enabled
zxs game.z80                 # Launch and load a file
```

Override the target architecture or install directory:
```bash
make install ARCH=osx-x64                      # Intel Mac
make install INSTALL_DIR=/usr/local/bin         # Custom install path
```

### IDE Setup
Alternatively, open `Speculator/Speculator.sln` in [JetBrains Rider](https://www.jetbrains.com/rider/) or [Visual Studio 2022](https://visualstudio.microsoft.com/vs/).

## Using DZRP (External Debugging)

DZRP enables VS Code debugging of Z80 programs running in the emulator.

### Running with DZRP

```bash
# Local debugging (127.0.0.1:11000)
zxs --dzrp

# Remote access (0.0.0.0:11000)
zxs --dzrp --dzrp-bind 0.0.0.0

# Or use the helper script from the publish directory
./run-dzrp.sh --remote
```

### Command-Line Flags

Run `zxs --help` for full usage. Key flags:

| Flag | Description |
|------|-------------|
| `-h`, `--help` | Show full usage and exit |
| `--dzrp` | Enable DZRP server on TCP socket (default: 127.0.0.1:11000) |
| `--dzrp-bind <addr>` | Bind address: `127.0.0.1` (local) or `0.0.0.0` (remote) |
| `--dzrp-port <port>` | Custom port (default: 11000) |
| `--debugger` | Open debugger view on startup |
| `--trace` | Enable DZRP protocol tracing (prints all commands/responses) |
| `--machine <model>` | Machine model: `48k` (default), `128k`, `pentagon` |
| `--contention` | Enable ULA memory contention (accurate timing) |
| `--tap-realtime` | Force real-time TAP signal loading (slow but accurate) |
| `--max-speed` | Disable emulation throttle (run as fast as possible) |
| `--no-keyboard-hook` | Disable keyboard hooks (auto-enabled with --dzrp) |
| `--with-keyboard-hook` | Force enable keyboard hooks even in DZRP mode |
| `--break-at <spec>` | Break into debugger when condition is met (see below) |
| `--dump-frames <dir>` | Save every frame as PNG to directory |
| `--dump-keyframes <dir>` | Save frames only when screen content changes |
| `--frame-spec <spec>` | Frame range specification (default: all frames) |
| `--no-border` | Capture 256x192 screen only (no border area) |
| `--timestamp` | Add hex microsecond timestamps to dump filenames |

### Environment Variables

DZRP can also be configured via environment variables, enabling consistent configuration across tools:

| Variable | Description | Default |
|----------|-------------|---------|
| `DZRP_HOST` | Default bind address | `127.0.0.1` |
| `DZRP_PORT` | Default port | `11000` |

Example:
```bash
# Configure once in your shell profile
export DZRP_HOST=0.0.0.0
export DZRP_PORT=11000

# All tools pick up the same config
./Speculator --dzrp           # Uses DZRP_HOST and DZRP_PORT
./mzrun program.minz          # Connects to DZRP_HOST:DZRP_PORT
./taploader game.tap          # Same configuration
```

Command-line flags override environment variables.

### Remote Development with mzrun

Run the emulator on a machine with a display, debug from anywhere:

```bash
# On macOS (with display)
cd Speculator/publish
./Speculator --dzrp --dzrp-bind 0.0.0.0

# From dev machine (Linux, Windows, etc.)
./mzrun --host mac-host.local program.minz
```

### Quick Start (VS Code + DeZog)

1. **Start emulator**: `./Speculator --dzrp`
2. **Install DeZog** in VS Code: `code --install-extension maziac.dezog`
3. **Create `.vscode/launch.json`**:
```json
{
  "version": "0.2.0",
  "configurations": [{
    "type": "dezog",
    "request": "launch",
    "name": "ZX Speculator",
    "remoteType": "dzrp",
    "dzrp": { "hostname": "localhost", "port": 11000 },
    "listFiles": [{ "path": "build/program.lst", "asm": "sjasmplus" }]
  }]
}
```
4. **Start debugging** with F5 in VS Code

### Supported Commands
- Pause/Continue execution
- Set breakpoints
- Read/write registers and memory
- Step through code

See the [SDK/IDE Integration Guide](reports/2026-01-03-003-sdk-ide-integration-guide.md) for detailed examples.

## Automated Frame Dump

Capture emulator frames as PNG files for automated testing, visual regression, animation capture, and documentation.

### Basic Usage

```bash
# Capture every frame
zxs --dump-frames /tmp/frames game.z80

# Capture only frames where the screen changed (keyframes)
zxs --dump-keyframes /tmp/kf game.sna

# Capture without border (256x192 instead of 320x240)
zxs --dump-frames /tmp/f --no-border game.z80
```

Output files are named `frame_000001.png`, `frame_000002.png`, etc. (sequential, only counting captured frames).

### Frame Spec

Use `--frame-spec` to capture specific frame ranges:

```bash
# Single frame
zxs --dump-frames /tmp/f --frame-spec "100" game.z80

# Range
zxs --dump-frames /tmp/f --frame-spec "100..200" game.z80

# Comma-separated mix
zxs --dump-frames /tmp/f --frame-spec "1,50,100..200" game.z80

# After file loading completes
zxs --dump-frames /tmp/f --frame-spec "load-end..load-end+200" game.tap

# T-state based
zxs --dump-frames /tmp/f --frame-spec "T=100000..T=100000+50" game.z80

# CPU state triggers
zxs --dump-frames /tmp/f --frame-spec "PC=8000..PC=8000+100" game.z80
```

### Trigger Types

These triggers work in both `--frame-spec` and `--break-at`:

| Trigger | Description | Example |
|---------|-------------|---------|
| `PC=XXXX` | Program counter hits address (hex) | `PC=8000` |
| `SP=XXXX` | Stack pointer equals value (hex) | `SP=FF00` |
| `OP=XX` | Specific opcode executed (hex) | `OP=C9` (RET), `OP=FF` (RST 38h) |
| `T=NNNNN` | T-state counter reaches value (decimal) | `T=500000` |
| `load-start` | File loading begins | `load-start` |
| `load-end` | File loading completes | `load-end+100` |

## Conditional Debugger Break

Use `--break-at` to pause execution and open the debugger when a condition is met:

```bash
# Break when PC hits address
zxs --break-at "PC=8000" game.z80

# Break at T-state count
zxs --break-at "T=500000" game.sna

# Break on specific opcode (e.g. RET)
zxs --break-at "OP=C9" game.sna

# Multiple conditions (first one wins)
zxs --break-at "PC=8000,T=1000000" game.z80
```

Each condition fires once. When triggered, the built-in debugger opens for inspection.

## Videos
There's a [YouTube playlist](https://www.youtube.com/playlist?list=PLPA1ndSnAZTwt7cQjDNwwsPjS89Dd3yqv) showing some classic games played in the emulator.
![](img/ManicMiner.png)
![](img/ChuckieEgg.png)
![](img/BoulderDash.png)
![](img/Tapper.png)

## Experiments - Raytracer
As my other hobby is writing GLSL shaders on [ShaderToy](https://www.shadertoy.com/user/dean_the_coder) (See [here](https://github.com/deanthecoder/GLSLShaderShrinker) for my GLSL Shader Shrinker application), I thought it'd be interesting to try a 'cross over' project.

I've taken inspiration from the [Human Shader](https://humanshader.com/) project and recreated the algorithm using ZX Spectrum BASIC, using this emulator.

Here's the result:
![](Experiments/HumanShader/Pass3_AdvancedDither.png)
Earlier version, with a basic dither:
![](Experiments/HumanShader/Pass2_BasicDither.png)
First iteration: Solid blocks:
![](Experiments/HumanShader/Pass1_Rough.png)
I've included a `.sna` snapshot of the code [here](Experiments/HumanShader/HumanShader.sna).
![](Experiments/HumanShader/Code.png)

# Experiments - Conway's Game Of Life
I realized I had never written a [Conway's Game Of Life](https://en.wikipedia.org/wiki/Conway%27s_Game_of_Life), so decided to make one using the emulator.

Performance in this BASIC version isn't great... (See [here](Experiments/GameOfLife/Conway.sna) - Requires the JGH ROM)
![](Experiments/GameOfLife/GameOfLife.png)

...so I rewrote it in C, compiled into Z80 machine code ([here](Experiments/GameOfLife/Conway.tap)).  Muuuch faster!
![](Experiments/GameOfLife/GameOfLifeC.png)

# Experiments - Retro Fire
C compiled into Z80 machine code ([here](Experiments/FireFX/fire.tap)). Too slow to call 'real time', but not bad for the Speccy.
![](Experiments/FireFX/Fire.png)

# Experiments - The Matrix
C compiled into Z80 machine code ([here](Experiments/TheMatrix/matrix.tap)). This one runs in real time, which surprised me.

I first populate the entire screen with characters (black on black), then build up a 768 element array of color values with a repeated sequence ranging from green to bright green, white to bright white.

This array is then used to set the colors on the screen, and a `memmove` command is used to scroll the buffer by one byte. This keeps the framerate up as it's only the color attributes that change - Not the characters on the screen.
![](Experiments/TheMatrix/TheMatrix.png)

# Experiments - 10PRINT
C compiled into Z80 machine code ([here](Experiments/10print/10print.tap)).

The screen is filled with forward and backslashes (Drawn with a small gap in my case), then I randomly fill areas of the screen.  Quite relaxing to watch!
![](Experiments/10print/10print.png)

# Experiments - Sandy Situation
C compiled into Z80 machine code ([here](Experiments/Sand/sand.tap)).

Iterating over a fixed number of grains, advancing each depending on what is below them. I did try a different approach of iterating through all cells in an x/y grid which worked well, but wasn't anywhere near as performant.

I'm making use of the [z88dk](https://z88dk.org/site/)'s 'chunky' pixel blitting routines too.
![](Experiments/Sand/Sand.png)

# Experiments - Twister
Pushing my graphical skills on the ZX Spectrum, resulting in the creation of a classic 'twister' effect. The code is written in C and compiled into Z80 machine code ([here](Experiments/Twister/twister.tap)) - Needed to keep the performance up.

The twister effect, a staple of early computer graphics and demoscene productions, seemed like the perfect challenge - I've always wanted to make one of these but never got round to it. Helped with details from [8bitshack](https://8bitshack.org/post/twister/).
It takes a while to complete the precaching, but I like the result.
![](Experiments/Twister/Twister.png)

# Experiments - Breakout
Two balls fighting for dominance! I saw an effect similar to this on Twitter and thought it was a great idea.
A little more of a 'demo-style' with this one - I've added a DTC logo (which I'll developed more in the future).

The code is written in C and compiled into Z80 machine code ([here](Experiments/BreakOut/breakout.tap)).
![](Experiments/BreakOut/Breakout.png)

# Experiments - One Small Step
This is based on a GLSL [shader](https://www.shadertoy.com/view/tt3yRH) I wrote a while ago. I built a library of GLSL-like functions in C to recreate the original code, then ran it over many hours.

The final quality was achieved with a Floyd-Steinberg dithering algorithm. A random dither is much easier to implement, but the result was way too noisy.

The executable is [here](Experiments/OneSmallStep/OneSmallStep.tap).
![](Experiments/OneSmallStep/OneSmallStep.png)

## Contribution and Improvements
ZX Speculator is an ongoing project and contributions are welcome. Whether it's improving emulation accuracy, testing on different platforms, or enhancing existing features, your input is valuable (although I can't always promise a fast response, as this is a side project).

## Credits
[Flux Capacitor icon](https://www.onlinewebfonts.com/icon) licensed by CC BY 4.0.

## Useful Resources
- [The Undocumented Z80 Documented](http://www.z80.info/zip/z80-documented.pdf)
- [Z80 Undocumented Instructions (World Of Spectrum)](https://worldofspectrum.org/z88forever/dn327/z80undoc.htm)
- [ClrHome's Z80 Opcode Table](https://clrhome.org/table/#%20)
- [ZX Spectrum Keyboard Cheat Sheet](http://slady.net/Sinclair-ZX-Spectrum-keyboard/)
- [Z80 Instruction Exerciser (zexall, zexdoc)](https://mdfs.net/Software/Z80/Exerciser/Spectrum/)
- [JSMoo Z80 tests](https://github.com/raddad772/jsmoo/tree/main/misc/tests/GeneratedTests/z80)
- [JGH Spectrum 48K ROM](http://mdfs.net/Software/Spectrum/Harston) by J.G.Harston.
- [z88dk C to z80 compiler](https://z88dk.org/site/)
---
Feel free to follow me on Twitter for more updates: [@deanthecoder](https://twitter.com/deanthecoder)

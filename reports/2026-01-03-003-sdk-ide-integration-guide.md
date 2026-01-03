# SDK/IDE Integration Guide for ZX Speculator DZRP

**Date**: 2026-01-03
**Topic**: How to integrate ZX Speculator with your development tools

## Overview

ZX Speculator now supports DZRP (DeZog Remote Protocol), enabling integration with VS Code and other development tools. This guide covers:

1. VS Code + DeZog setup
2. Custom tool integration via DZRP
3. Building your own debug adapter
4. CI/CD integration

## VS Code + DeZog Setup

### Prerequisites
- ZX Speculator (this fork)
- VS Code
- DeZog extension

### Step 1: Enable DZRP in ZX Speculator

Edit settings or programmatically enable:
```csharp
// In code
Speccy.EnableDzrp(11000);  // Default port

// Or via settings
Settings.IsDzrpEnabled = true;
Settings.DzrpPort = 11000;
```

### Step 2: Install DeZog Extension

```bash
code --install-extension maziac.dezog
```

### Step 3: Create launch.json

Create `.vscode/launch.json` in your Z80 project:

```json
{
  "version": "0.2.0",
  "configurations": [
    {
      "type": "dezog",
      "request": "launch",
      "name": "ZX Speculator Debug",
      "remoteType": "dzrp",
      "dzrp": {
        "hostname": "localhost",
        "port": 11000
      },
      "rootFolder": "${workspaceFolder}",
      "listFiles": [
        {
          "path": "build/program.lst",
          "asm": "sjasmplus",
          "mainFile": "src/main.asm"
        }
      ],
      "load": "build/program.bin",
      "loadAddress": "0x8000",
      "startAutomatically": true,
      "stopOnEntry": true
    }
  ]
}
```

### Step 4: Build Your Z80 Program

Using sjasmplus:
```bash
sjasmplus --lst=build/program.lst --raw=build/program.bin src/main.asm
```

### Step 5: Start Debugging

1. Launch ZX Speculator
2. In VS Code, press F5 or click "Run and Debug"
3. DeZog connects to the emulator
4. Set breakpoints, step through code, inspect registers

## DeZog Configuration Options

### List File Formats

DeZog supports multiple assemblers:

```json
{
  "listFiles": [
    {"path": "out.lst", "asm": "sjasmplus"},
    {"path": "out.lst", "asm": "z80asm"},
    {"path": "out.lst", "asm": "z88dk"},
    {"path": "out.lst", "asm": "pasmo"}
  ]
}
```

### SLD Files (Recommended)

For better source mapping:
```json
{
  "sldFiles": ["build/program.sld"]
}
```

Generate with sjasmplus:
```bash
sjasmplus --sld=build/program.sld src/main.asm
```

### Memory Configuration

```json
{
  "memoryModel": "ZX48K",
  "loadObjs": [
    {"path": "build/part1.bin", "start": "0x8000"},
    {"path": "build/part2.bin", "start": "0xC000"}
  ]
}
```

## Custom Tool Integration via DZRP

### Protocol Basics

DZRP is a simple TCP protocol:
- **Port**: 11000 (default)
- **Byte order**: Little-endian
- **Format**: `[4-byte length][1-byte seq][1-byte cmd][payload]`

### Python Example: Simple DZRP Client

```python
import socket
import struct

class DzrpClient:
    def __init__(self, host='localhost', port=11000):
        self.sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        self.sock.connect((host, port))
        self.seq = 1

    def send_command(self, cmd_id, payload=b''):
        seq = self.seq
        self.seq = (self.seq % 255) + 1

        data = bytes([seq, cmd_id]) + payload
        length = struct.pack('<I', len(data))
        self.sock.sendall(length + data)

        # Read response
        resp_len = struct.unpack('<I', self.sock.recv(4))[0]
        return self.sock.recv(resp_len)

    def init(self):
        # CMD_INIT = 1
        resp = self.send_command(1, b'\x02\x01\x00MyTool\x00')
        return resp[0] == 0  # Error code

    def get_registers(self):
        # CMD_GET_REGISTERS = 3
        resp = self.send_command(3)
        if resp[0] != 0:
            return None
        # Parse registers (little-endian)
        data = resp[1:]
        return {
            'PC': struct.unpack('<H', data[0:2])[0],
            'SP': struct.unpack('<H', data[2:4])[0],
            'AF': struct.unpack('<H', data[4:6])[0],
            'BC': struct.unpack('<H', data[6:8])[0],
            'DE': struct.unpack('<H', data[8:10])[0],
            'HL': struct.unpack('<H', data[10:12])[0],
            'IX': struct.unpack('<H', data[12:14])[0],
            'IY': struct.unpack('<H', data[14:16])[0],
        }

    def read_memory(self, address, length):
        # CMD_READ_MEM = 8
        payload = struct.pack('<HH', address, length)
        resp = self.send_command(8, payload)
        return resp[1:] if resp[0] == 0 else None

    def pause(self):
        # CMD_PAUSE = 7
        return self.send_command(7)[0] == 0

    def continue_exec(self):
        # CMD_CONTINUE = 6
        return self.send_command(6)[0] == 0

    def add_breakpoint(self, address):
        # CMD_ADD_BREAKPOINT = 40
        payload = struct.pack('<H', address)
        resp = self.send_command(40, payload)
        if resp[0] == 0:
            return struct.unpack('<H', resp[1:3])[0]  # BP ID
        return None

# Usage
client = DzrpClient()
client.init()
client.pause()
regs = client.get_registers()
print(f"PC: {regs['PC']:04X}")
memory = client.read_memory(0x4000, 256)
client.continue_exec()
```

### Node.js Example

```javascript
const net = require('net');

class DzrpClient {
    constructor(host = 'localhost', port = 11000) {
        this.socket = new net.Socket();
        this.seq = 1;
        this.pending = new Map();

        this.socket.on('data', (data) => this.handleData(data));
    }

    connect() {
        return new Promise((resolve) => {
            this.socket.connect(11000, 'localhost', resolve);
        });
    }

    sendCommand(cmdId, payload = Buffer.alloc(0)) {
        return new Promise((resolve) => {
            const seq = this.seq++;
            if (this.seq > 255) this.seq = 1;

            const data = Buffer.concat([
                Buffer.from([seq, cmdId]),
                payload
            ]);

            const length = Buffer.alloc(4);
            length.writeUInt32LE(data.length);

            this.pending.set(seq, resolve);
            this.socket.write(Buffer.concat([length, data]));
        });
    }

    handleData(data) {
        const seq = data[4];
        const payload = data.slice(5);

        if (this.pending.has(seq)) {
            this.pending.get(seq)(payload);
            this.pending.delete(seq);
        }
    }

    async getRegisters() {
        const resp = await this.sendCommand(3);
        return {
            PC: resp.readUInt16LE(1),
            SP: resp.readUInt16LE(3),
            AF: resp.readUInt16LE(5),
            // ... etc
        };
    }
}
```

## Building a Custom Debug Adapter

For languages other than assembly, you may want a custom VS Code debug adapter.

### Architecture

```
VS Code ──DAP──▶ Your Debug Adapter ──DZRP──▶ ZX Speculator
                       │
                       ▼
              Source Map (your format)
```

### DAP to DZRP Mapping

| DAP Request | DZRP Command |
|-------------|--------------|
| `initialize` | `CMD_INIT` |
| `launch` | Load binary + `CMD_CONTINUE` |
| `pause` | `CMD_PAUSE` |
| `continue` | `CMD_CONTINUE` |
| `next` | `CMD_CONTINUE` with temp BP |
| `stepIn` | Single step (not yet impl) |
| `stackTrace` | `CMD_GET_REGISTERS` (PC, SP) |
| `scopes` | N/A (use variables) |
| `variables` | `CMD_READ_MEM` |
| `setBreakpoints` | `CMD_ADD_BREAKPOINT` |
| `disconnect` | `CMD_CLOSE` |

### Example: Minimal DAP Adapter (Node.js)

```javascript
const { DebugSession } = require('@vscode/debugadapter');
const { DzrpClient } = require('./dzrp-client');

class MinzDebugSession extends DebugSession {
    constructor() {
        super();
        this.dzrp = new DzrpClient();
        this.sourceMap = new Map();
    }

    initializeRequest(response, args) {
        response.body = {
            supportsBreakpointLocationsRequest: true,
            supportsPauseRequest: true,
        };
        this.sendResponse(response);
    }

    async launchRequest(response, args) {
        await this.dzrp.connect();
        await this.dzrp.init();

        // Load source map
        this.loadSourceMap(args.sourceMap);

        this.sendResponse(response);
        this.sendEvent(new StoppedEvent('entry', 1));
    }

    async setBreakPointsRequest(response, args) {
        const breakpoints = [];
        for (const bp of args.breakpoints) {
            const addr = this.sourceToAddress(args.source.path, bp.line);
            if (addr !== undefined) {
                const id = await this.dzrp.addBreakpoint(addr);
                breakpoints.push({ id, verified: true, line: bp.line });
            }
        }
        response.body = { breakpoints };
        this.sendResponse(response);
    }

    sourceToAddress(file, line) {
        return this.sourceMap.get(`${file}:${line}`);
    }
}

DebugSession.run(MinzDebugSession);
```

## CI/CD Integration

### Automated Testing with DZRP

```python
# test_z80_program.py
import unittest
from dzrp_client import DzrpClient

class TestZ80Program(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        # Start emulator in headless mode (future feature)
        cls.client = DzrpClient()
        cls.client.init()

    def test_add_routine(self):
        # Set up: Write test values
        self.client.write_memory(0xC000, bytes([5, 3]))

        # Run add routine at 0x8000
        self.client.add_breakpoint(0x8010)  # After routine
        self.client.set_register('PC', 0x8000)
        self.client.continue_exec()

        # Wait for breakpoint
        # ...

        # Check result
        result = self.client.read_memory(0xC002, 1)
        self.assertEqual(result[0], 8)  # 5 + 3 = 8
```

### GitHub Actions Example

```yaml
name: Z80 Tests
on: [push]

jobs:
  test:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4

      - name: Build Z80 program
        run: sjasmplus --raw=build/test.bin src/test.asm

      - name: Start emulator
        run: |
          dotnet run --project ZXSpeculator &
          sleep 5

      - name: Run tests
        run: python -m pytest tests/
```

## Troubleshooting

### Connection Issues

```bash
# Check if DZRP is listening
nc -zv localhost 11000

# Test with netcat
echo -en '\x02\x00\x00\x00\x01\x01' | nc localhost 11000 | xxd
```

### Debug Logging

Enable logging in ZX Speculator to see DZRP traffic:
```csharp
Logger.Instance.Info("DZRP: ...");
```

### Common Errors

| Error | Cause | Solution |
|-------|-------|----------|
| Connection refused | DZRP not enabled | Enable in settings |
| Timeout | Emulator paused | Send CMD_PAUSE first |
| Unknown command | Protocol version | Update DZRP impl |

## References

- [DZRP Protocol Spec](https://github.com/maziac/DeZog/blob/main/design/DeZogProtocol.md)
- [DeZog Extension](https://marketplace.visualstudio.com/items?itemName=maziac.dezog)
- [VS Code DAP](https://microsoft.github.io/debug-adapter-protocol/)
- [sjasmplus Assembler](https://github.com/z00m128/sjasmplus)

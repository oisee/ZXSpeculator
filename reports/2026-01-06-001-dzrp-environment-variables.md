# DZRP Environment Variable Support

**Date:** January 6, 2026
**Status:** Implemented and Released

## Overview

Added standardized environment variable support for DZRP configuration across the ZXSpeculator ecosystem. This enables consistent configuration between the emulator and companion tools like `taploader` and `mzrun`.

## Environment Variables

| Variable | Description | Default |
|----------|-------------|---------|
| `DZRP_HOST` | Bind address (emulator) / Connect address (tools) | `127.0.0.1` |
| `DZRP_PORT` | DZRP TCP port | `11000` |

## Benefits

### 1. Configure Once, Use Everywhere
```bash
# Add to ~/.bashrc or ~/.zshrc
export DZRP_HOST=192.168.1.100
export DZRP_PORT=11000

# All tools pick up the same config
./Speculator --dzrp
./taploader game.tap
./mzrun program.minz
```

### 2. Remote Development Made Easy
```bash
# On your Mac (running emulator)
export DZRP_HOST=0.0.0.0  # Listen on all interfaces
./Speculator --dzrp

# On your dev machine (anywhere)
export DZRP_HOST=mac-mini.local
./taploader game.tap
```

### 3. CI/CD Integration
```yaml
# GitHub Actions / GitLab CI
env:
  DZRP_HOST: emulator-host
  DZRP_PORT: 11000

steps:
  - run: ./mzrun tests/test_suite.minz
```

## Priority Order

Configuration is resolved in this order (highest priority first):

1. **Command-line flags** (`--dzrp-port 12000`)
2. **Environment variables** (`DZRP_PORT=12000`)
3. **Saved settings** (from previous sessions)
4. **Hard-coded defaults** (`11000`)

## Implementation Details

### Emulator (C#)

```csharp
// DzrpServer.cs
public static int GetDefaultPort()
{
    var envPort = Environment.GetEnvironmentVariable("DZRP_PORT");
    if (!string.IsNullOrEmpty(envPort) && int.TryParse(envPort, out var port))
        return port;
    return DefaultPort;
}

public static string GetDefaultBindAddress()
{
    var envHost = Environment.GetEnvironmentVariable("DZRP_HOST");
    if (!string.IsNullOrEmpty(envHost))
        return envHost;
    return DefaultBindAddress;
}
```

### taploader (Go)

```go
// main.go init()
defaultHost := "localhost"
if envHost := os.Getenv("DZRP_HOST"); envHost != "" {
    defaultHost = envHost
}
```

## Tools Updated

| Tool | Repository | Status |
|------|------------|--------|
| ZXSpeculator | ZXSpeculator | ✅ Implemented |
| taploader | ZXSpeculator/tools | ✅ Implemented |
| mzrun | minz-ts (feature branch) | ✅ Implemented |

## Compatibility

- **Backward compatible**: Tools work without env vars (use defaults)
- **Cross-platform**: Works on macOS, Linux, Windows
- **No breaking changes**: Existing configs continue to work

## Related Work

- [DZRP Implementation Guide](2026-01-03-004-dzrp-implementation-guide.md)
- [taploader tool](../tools/taploader/README.md)
- [MinZ mzrun](https://github.com/oisee/minz/tree/feature/tap-loader)

## Future Considerations

The `DZRP_SOCKET` variable is reserved for future WebSocket transport support:

```bash
export DZRP_SOCKET=ws  # Future: WebSocket instead of TCP
```

This would enable browser-based tools and cloud development environments.

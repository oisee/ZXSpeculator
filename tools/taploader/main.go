// taploader - ZX Spectrum TAP file loader via DZRP
//
// Loads TAP files directly into any DZRP-compatible emulator,
// bypassing tape emulation for instant loading.
//
// Usage:
//   taploader game.tap                    # Load to localhost:11000
//   taploader --host 192.168.1.5 game.tap # Load to remote emulator
//   taploader --debug game.tap            # Load with step debugger

package main

import (
	"bufio"
	"encoding/binary"
	"fmt"
	"io"
	"net"
	"os"
	"strings"
	"time"

	"github.com/spf13/cobra"
)

// DZRP Commands (per ZXSpeculator/DeZog protocol)
const (
	CMD_INIT          = 1
	CMD_GET_REGISTERS = 3
	CMD_SET_REGISTER  = 4
	CMD_CONTINUE      = 6
	CMD_PAUSE         = 7
	CMD_READ_MEM      = 8
	CMD_WRITE_MEM     = 9
	CMD_STEP_INTO     = 17
)

var seqNum byte = 1

var (
	host      string
	port      int
	loadAddr  uint16
	startAddr uint16
	timeout   int
	verbose   bool
	debug     bool
	info      bool
)

var rootCmd = &cobra.Command{
	Use:   "taploader <tap-file>",
	Short: "Load ZX Spectrum TAP files via DZRP protocol",
	Long: `taploader loads TAP files directly into any DZRP-compatible emulator,
bypassing tape emulation for instant loading.

Supported emulators:
  - ZXSpeculator (with DZRP enabled)
  - ZEsarUX (built-in DZRP support)
  - CSpect (with DeZog plugin)
  - Any emulator implementing DeZog Remote Protocol

The tool extracts CODE blocks from TAP files and writes them directly
to emulator memory via DZRP, then sets PC and starts execution.

Examples:
  taploader game.tap                      # Load to localhost:11000
  taploader --host 192.168.1.5 game.tap   # Load to remote emulator
  taploader --verbose game.tap            # Show detailed info
  taploader --debug game.tap              # Load with step debugger
  taploader --info game.tap               # Just show TAP contents
  taploader --load 0x6000 game.tap        # Override load address`,
	Args: cobra.ExactArgs(1),
	RunE: runLoader,
}

func init() {
	// Use environment variables as defaults
	defaultHost := "localhost"
	if envHost := os.Getenv("DZRP_HOST"); envHost != "" {
		defaultHost = envHost
	}
	defaultPort := 11000
	if envPort := os.Getenv("DZRP_PORT"); envPort != "" {
		if p, err := fmt.Sscanf(envPort, "%d", &defaultPort); err != nil || p != 1 {
			defaultPort = 11000
		}
	}

	rootCmd.Flags().StringVar(&host, "host", defaultHost, "DZRP emulator host/IP (env: DZRP_HOST)")
	rootCmd.Flags().IntVar(&port, "port", defaultPort, "DZRP port (env: DZRP_PORT)")
	rootCmd.Flags().Uint16Var(&loadAddr, "load", 0, "Override load address (0 = use TAP address)")
	rootCmd.Flags().Uint16Var(&startAddr, "start", 0, "Override start address (0 = same as load)")
	rootCmd.Flags().IntVar(&timeout, "timeout", 0, "Execution timeout in seconds (0 = run forever)")
	rootCmd.Flags().BoolVarP(&verbose, "verbose", "v", false, "Verbose output")
	rootCmd.Flags().BoolVar(&debug, "debug", false, "Interactive step debugger")
	rootCmd.Flags().BoolVar(&info, "info", false, "Just show TAP file info, don't load")
}

func main() {
	if err := rootCmd.Execute(); err != nil {
		os.Exit(1)
	}
}

func runLoader(cmd *cobra.Command, args []string) error {
	filename := args[0]

	// Parse TAP file
	tap, err := ParseTapFile(filename)
	if err != nil {
		return fmt.Errorf("failed to parse TAP: %w", err)
	}

	// Show info
	if verbose || info {
		tap.PrintInfo()
	}

	if info {
		return nil // Just showing info, don't load
	}

	// Get loadable blocks
	blocks := tap.GetAllLoadableBlocks()
	if len(blocks) == 0 {
		return fmt.Errorf("no CODE blocks found in TAP file")
	}

	// Determine addresses
	firstBlock := blocks[0]
	effectiveLoadAddr := loadAddr
	if loadAddr == 0 {
		effectiveLoadAddr = firstBlock.Address
	}
	effectiveStartAddr := startAddr
	if startAddr == 0 {
		effectiveStartAddr = effectiveLoadAddr
	}

	if verbose {
		fmt.Printf("Loading %d CODE block(s) via DZRP...\n", len(blocks))
		fmt.Printf("Connecting to %s:%d...\n", host, port)
	}

	// Connect to DZRP emulator
	addr := fmt.Sprintf("%s:%d", host, port)
	conn, err := net.DialTimeout("tcp", addr, 5*time.Second)
	if err != nil {
		return fmt.Errorf("failed to connect to DZRP emulator at %s: %w", addr, err)
	}
	defer conn.Close()

	if verbose {
		fmt.Println("Connected! Initializing...")
	}

	// Initialize DZRP session
	if err := dzrpInit(conn); err != nil {
		return fmt.Errorf("DZRP init failed: %w", err)
	}

	// Pause emulation
	if err := dzrpPause(conn); err != nil {
		return fmt.Errorf("failed to pause: %w", err)
	}

	// Load all CODE blocks
	for i, block := range blocks {
		targetAddr := block.Address
		if loadAddr != 0 && i == 0 {
			targetAddr = effectiveLoadAddr
		}
		if verbose {
			fmt.Printf("Loading \"%s\" (%d bytes) at $%04X...\n",
				block.Name, len(block.Data), targetAddr)
		}
		if err := dzrpWriteMem(conn, targetAddr, block.Data); err != nil {
			return fmt.Errorf("failed to write memory: %w", err)
		}
	}

	// Set PC to start address
	if err := dzrpSetPC(conn, effectiveStartAddr); err != nil {
		return fmt.Errorf("failed to set PC: %w", err)
	}

	if debug {
		return runDebugger(conn, effectiveStartAddr)
	}

	if verbose {
		fmt.Printf("Starting execution at $%04X...\n", effectiveStartAddr)
	}

	// Continue execution
	if err := dzrpContinue(conn); err != nil {
		return fmt.Errorf("failed to continue: %w", err)
	}

	if timeout == 0 {
		fmt.Printf("Program running from $%04X (use emulator to stop)\n", effectiveStartAddr)
		return nil
	}

	// Wait for timeout
	time.Sleep(time.Duration(timeout) * time.Second)

	// Pause and show final state
	if err := dzrpPause(conn); err != nil {
		return fmt.Errorf("failed to pause: %w", err)
	}

	regs, err := dzrpGetRegisters(conn)
	if err != nil {
		return fmt.Errorf("failed to get registers: %w", err)
	}

	fmt.Printf("Execution stopped. PC=$%04X SP=$%04X\n", regs["PC"], regs["SP"])

	return nil
}

// DZRP protocol functions

func dzrpSend(conn net.Conn, cmd byte, payload []byte) error {
	length := uint32(2 + len(payload)) // seqNum + cmd + payload
	buf := make([]byte, 4+length)

	binary.LittleEndian.PutUint32(buf[0:4], length)
	buf[4] = seqNum
	buf[5] = cmd
	if len(payload) > 0 {
		copy(buf[6:], payload)
	}

	seqNum++
	if seqNum == 0 {
		seqNum = 1
	}

	_, err := conn.Write(buf)
	return err
}

func dzrpRecv(conn net.Conn) (byte, byte, []byte, error) {
	for {
		lenBuf := make([]byte, 4)
		if _, err := io.ReadFull(conn, lenBuf); err != nil {
			return 0, 0, nil, err
		}
		length := binary.LittleEndian.Uint32(lenBuf)

		if length < 2 {
			return 0, 0, nil, fmt.Errorf("invalid message length: %d", length)
		}

		msgBuf := make([]byte, length)
		if _, err := io.ReadFull(conn, msgBuf); err != nil {
			return 0, 0, nil, err
		}

		seq := msgBuf[0]
		cmd := msgBuf[1]
		payload := msgBuf[2:]

		// Skip notifications (seq=0)
		if seq == 0 {
			if verbose {
				fmt.Printf("  <- notification: cmd=%d len=%d (skipping)\n", cmd, len(payload))
			}
			continue
		}

		if verbose {
			fmt.Printf("  <- response: seq=%d cmd=%d len=%d\n", seq, cmd, len(payload))
		}
		return seq, cmd, payload, nil
	}
}

func dzrpRecvRaw(conn net.Conn) (byte, byte, []byte, error) {
	lenBuf := make([]byte, 4)
	if _, err := io.ReadFull(conn, lenBuf); err != nil {
		return 0, 0, nil, err
	}
	length := binary.LittleEndian.Uint32(lenBuf)

	if length < 2 {
		return 0, 0, nil, fmt.Errorf("invalid message length: %d", length)
	}

	msgBuf := make([]byte, length)
	if _, err := io.ReadFull(conn, msgBuf); err != nil {
		return 0, 0, nil, err
	}

	return msgBuf[0], msgBuf[1], msgBuf[2:], nil
}

func dzrpInit(conn net.Conn) error {
	if err := dzrpSend(conn, CMD_INIT, nil); err != nil {
		return err
	}
	_, _, _, err := dzrpRecv(conn)
	return err
}

func dzrpPause(conn net.Conn) error {
	if err := dzrpSend(conn, CMD_PAUSE, nil); err != nil {
		return err
	}
	_, _, _, err := dzrpRecv(conn)
	return err
}

func dzrpContinue(conn net.Conn) error {
	if err := dzrpSend(conn, CMD_CONTINUE, nil); err != nil {
		return err
	}
	_, _, _, err := dzrpRecv(conn)
	return err
}

func dzrpReadMem(conn net.Conn, addr uint16, length uint16) ([]byte, error) {
	payload := make([]byte, 4)
	binary.LittleEndian.PutUint16(payload[0:2], addr)
	binary.LittleEndian.PutUint16(payload[2:4], length)

	if err := dzrpSend(conn, CMD_READ_MEM, payload); err != nil {
		return nil, err
	}

	_, _, data, err := dzrpRecv(conn)
	if err != nil {
		return nil, err
	}

	if len(data) > 0 && data[0] != 0 {
		return nil, fmt.Errorf("read memory error: %d", data[0])
	}

	return data[1:], nil
}

func dzrpWriteMem(conn net.Conn, addr uint16, data []byte) error {
	const chunkSize = 256

	for offset := 0; offset < len(data); offset += chunkSize {
		end := offset + chunkSize
		if end > len(data) {
			end = len(data)
		}
		chunk := data[offset:end]

		payload := make([]byte, 4+len(chunk))
		binary.LittleEndian.PutUint16(payload[0:2], addr+uint16(offset))
		binary.LittleEndian.PutUint16(payload[2:4], uint16(len(chunk)))
		copy(payload[4:], chunk)

		if err := dzrpSend(conn, CMD_WRITE_MEM, payload); err != nil {
			return err
		}

		_, _, _, err := dzrpRecv(conn)
		if err != nil {
			return err
		}
	}

	return nil
}

func dzrpSetPC(conn net.Conn, pc uint16) error {
	data := make([]byte, 3)
	data[0] = 0 // Register ID 0 = PC
	binary.LittleEndian.PutUint16(data[1:3], pc)

	if err := dzrpSend(conn, CMD_SET_REGISTER, data); err != nil {
		return err
	}

	_, _, _, err := dzrpRecv(conn)
	return err
}

func dzrpGetRegisters(conn net.Conn) (map[string]uint16, error) {
	if err := dzrpSend(conn, CMD_GET_REGISTERS, nil); err != nil {
		return nil, err
	}

	_, _, data, err := dzrpRecv(conn)
	if err != nil {
		return nil, err
	}

	if len(data) >= 29 {
		data = data[1:] // Skip error byte
		regs := make(map[string]uint16)
		regs["PC"] = binary.LittleEndian.Uint16(data[0:2])
		regs["SP"] = binary.LittleEndian.Uint16(data[2:4])
		regs["AF"] = binary.LittleEndian.Uint16(data[4:6])
		regs["BC"] = binary.LittleEndian.Uint16(data[6:8])
		regs["DE"] = binary.LittleEndian.Uint16(data[8:10])
		regs["HL"] = binary.LittleEndian.Uint16(data[10:12])
		regs["IX"] = binary.LittleEndian.Uint16(data[12:14])
		regs["IY"] = binary.LittleEndian.Uint16(data[14:16])
		return regs, nil
	}

	return nil, fmt.Errorf("invalid register data")
}

func dzrpStepInto(conn net.Conn) error {
	if err := dzrpSend(conn, CMD_STEP_INTO, nil); err != nil {
		return err
	}

	for {
		seqNum, cmd, _, err := dzrpRecvRaw(conn)
		if err != nil {
			return err
		}
		if seqNum == 0 && cmd == 1 {
			return nil
		}
		if cmd == CMD_STEP_INTO {
			continue
		}
	}
}

// Simple step debugger
func runDebugger(conn net.Conn, startPC uint16) error {
	reader := bufio.NewReader(os.Stdin)
	pc := startPC

	fmt.Println("\n=== TAP Loader Debugger ===")
	fmt.Println("Commands: s=step, c=continue, r=regs, m=mem, q=quit")
	fmt.Println()

	for {
		mem, err := dzrpReadMem(conn, pc, 4)
		if err != nil {
			return fmt.Errorf("failed to read memory: %w", err)
		}

		instr := disasmSimple(mem, pc)
		fmt.Printf("$%04X: %-20s > ", pc, instr)

		input, _ := reader.ReadString('\n')
		input = strings.TrimSpace(strings.ToLower(input))

		switch input {
		case "", "s", "step":
			if err := dzrpStepInto(conn); err != nil {
				return err
			}
			regs, err := dzrpGetRegisters(conn)
			if err != nil {
				return err
			}
			pc = regs["PC"]

		case "c", "continue":
			fmt.Println("Continuing...")
			if err := dzrpContinue(conn); err != nil {
				return err
			}
			return nil

		case "r", "regs":
			regs, err := dzrpGetRegisters(conn)
			if err != nil {
				return err
			}
			fmt.Printf("PC=$%04X SP=$%04X AF=$%04X BC=$%04X DE=$%04X HL=$%04X\n",
				regs["PC"], regs["SP"], regs["AF"], regs["BC"], regs["DE"], regs["HL"])
			pc = regs["PC"]

		case "m", "mem":
			fmt.Printf("Memory at $%04X:\n", pc)
			mem, _ := dzrpReadMem(conn, pc, 32)
			for i := 0; i < len(mem); i += 16 {
				fmt.Printf("%04X: ", pc+uint16(i))
				end := i + 16
				if end > len(mem) {
					end = len(mem)
				}
				for j := i; j < end; j++ {
					fmt.Printf("%02X ", mem[j])
				}
				fmt.Println()
			}

		case "q", "quit":
			fmt.Println("Quitting debugger...")
			return nil

		default:
			fmt.Println("Unknown command. s=step, c=continue, r=regs, m=mem, q=quit")
		}
	}
}

// Simple disassembler for common instructions
func disasmSimple(mem []byte, pc uint16) string {
	if len(mem) == 0 {
		return "???"
	}
	op := mem[0]

	n := func() byte {
		if len(mem) >= 2 {
			return mem[1]
		}
		return 0
	}
	nn := func() uint16 {
		if len(mem) >= 3 {
			return binary.LittleEndian.Uint16(mem[1:3])
		}
		return 0
	}

	switch op {
	case 0x00:
		return "NOP"
	case 0x01:
		return fmt.Sprintf("LD BC,$%04X", nn())
	case 0x11:
		return fmt.Sprintf("LD DE,$%04X", nn())
	case 0x21:
		return fmt.Sprintf("LD HL,$%04X", nn())
	case 0x31:
		return fmt.Sprintf("LD SP,$%04X", nn())
	case 0x3E:
		return fmt.Sprintf("LD A,$%02X", n())
	case 0xC3:
		return fmt.Sprintf("JP $%04X", nn())
	case 0xC9:
		return "RET"
	case 0xCD:
		return fmt.Sprintf("CALL $%04X", nn())
	case 0xFB:
		return "EI"
	case 0x76:
		return "HALT"
	default:
		return fmt.Sprintf("DB $%02X", op)
	}
}

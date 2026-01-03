// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any non-commercial
// purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using System.Net.Sockets;
using System.Text;
using CSharp.Core;

namespace Speculator.Core.Dzrp;

/// <summary>
/// Handles a single DZRP client connection.
/// Processes protocol commands and sends notifications.
/// </summary>
public class DzrpSession : IDisposable
{
    private readonly TcpClient m_client;
    private readonly IDzrpDebugBridge m_bridge;
    private readonly NetworkStream m_stream;
    private readonly Thread m_thread;
    private readonly object m_streamLock = new();
    private volatile bool m_isRunning;

    public event EventHandler Disconnected;

    public DzrpSession(TcpClient client, IDzrpDebugBridge bridge)
    {
        m_client = client;
        m_bridge = bridge;
        m_stream = client.GetStream();

        m_bridge.Paused += OnBridgePaused;

        m_thread = new Thread(ProcessLoop) { Name = "DZRP Session" };
    }

    public void Start()
    {
        m_isRunning = true;
        m_thread.Start();
    }

    public void Stop()
    {
        m_isRunning = false;
        try
        {
            m_client.Close();
        }
        catch
        {
            // Ignore close errors
        }
    }

    private void ProcessLoop()
    {
        try
        {
            while (m_isRunning && m_client.Connected)
            {
                var message = ReadMessage();
                if (message == null)
                    break;

                ProcessMessage(message);
            }
        }
        catch (Exception ex) when (m_isRunning)
        {
            Logger.Instance.Warn($"DZRP session error: {ex.Message}");
        }
        finally
        {
            m_bridge.Paused -= OnBridgePaused;
            Disconnected?.Invoke(this, EventArgs.Empty);
        }
    }

    private DzrpMessage ReadMessage()
    {
        try
        {
            // Read 4-byte length
            var lengthBytes = new byte[4];
            var bytesRead = 0;
            while (bytesRead < 4)
            {
                var read = m_stream.Read(lengthBytes, bytesRead, 4 - bytesRead);
                if (read == 0)
                    return null;
                bytesRead += read;
            }

            var length = BitConverter.ToInt32(lengthBytes, 0);
            if (length < 2)
                return null;

            // Read seq + cmd + payload
            var data = new byte[length];
            var totalRead = 0;
            while (totalRead < length)
            {
                var read = m_stream.Read(data, totalRead, length - totalRead);
                if (read == 0)
                    return null;
                totalRead += read;
            }

            var payload = length > 2 ? data.Skip(2).ToArray() : Array.Empty<byte>();
            return new DzrpMessage(data[0], data[1], payload);
        }
        catch
        {
            return null;
        }
    }

    private void ProcessMessage(DzrpMessage msg)
    {
        switch (msg.CommandId)
        {
            case DzrpCommands.CMD_INIT:
                HandleInit(msg);
                break;
            case DzrpCommands.CMD_CLOSE:
                HandleClose(msg);
                break;
            case DzrpCommands.CMD_GET_REGISTERS:
                HandleGetRegisters(msg);
                break;
            case DzrpCommands.CMD_SET_REGISTER:
                HandleSetRegister(msg);
                break;
            case DzrpCommands.CMD_CONTINUE:
                HandleContinue(msg);
                break;
            case DzrpCommands.CMD_PAUSE:
                HandlePause(msg);
                break;
            case DzrpCommands.CMD_READ_MEM:
                HandleReadMem(msg);
                break;
            case DzrpCommands.CMD_WRITE_MEM:
                HandleWriteMem(msg);
                break;
            case DzrpCommands.CMD_ADD_BREAKPOINT:
                HandleAddBreakpoint(msg);
                break;
            case DzrpCommands.CMD_REMOVE_BREAKPOINT:
                HandleRemoveBreakpoint(msg);
                break;
            default:
                // Unknown command - send error response
                SendResponse(msg.SequenceNumber, msg.CommandId, 1); // Error code 1
                break;
        }
    }

    private void HandleInit(DzrpMessage msg)
    {
        // Response: [Error (1)][MachineType (1)][ProgramName (null-terminated string)]
        var response = new List<byte>
        {
            0, // No error
            (byte)MachineType.ZX48K
        };
        response.AddRange(Encoding.UTF8.GetBytes("ZXSpeculator"));
        response.Add(0); // Null terminator

        SendResponse(msg.SequenceNumber, DzrpCommands.CMD_INIT, response.ToArray());
        Logger.Instance.Info("DZRP: Client initialized");
    }

    private void HandleClose(DzrpMessage msg)
    {
        SendResponse(msg.SequenceNumber, DzrpCommands.CMD_CLOSE, 0);
        m_isRunning = false;
    }

    private void HandleGetRegisters(DzrpMessage msg)
    {
        var regs = m_bridge.GetAllRegisters();
        var response = new byte[1 + regs.Length];
        response[0] = 0; // No error
        Array.Copy(regs, 0, response, 1, regs.Length);
        SendResponse(msg.SequenceNumber, DzrpCommands.CMD_GET_REGISTERS, response);
    }

    private void HandleSetRegister(DzrpMessage msg)
    {
        var regId = (RegisterId)msg.ReadByte(0);
        var value = msg.ReadWord(1);
        m_bridge.SetRegister(regId, value);
        SendResponse(msg.SequenceNumber, DzrpCommands.CMD_SET_REGISTER, 0);
    }

    private void HandleContinue(DzrpMessage msg)
    {
        // Parse optional temporary breakpoints from payload
        ushort[] tempBps = null;
        if (msg.Payload.Length >= 2)
        {
            var count = msg.ReadWord(0);
            if (count > 0 && msg.Payload.Length >= 2 + count * 2)
            {
                tempBps = new ushort[count];
                for (var i = 0; i < count; i++)
                    tempBps[i] = msg.ReadWord(2 + i * 2);
            }
        }

        m_bridge.Continue(tempBps);
        SendResponse(msg.SequenceNumber, DzrpCommands.CMD_CONTINUE, 0);
    }

    private void HandlePause(DzrpMessage msg)
    {
        m_bridge.Pause();
        SendResponse(msg.SequenceNumber, DzrpCommands.CMD_PAUSE, 0);
    }

    private void HandleReadMem(DzrpMessage msg)
    {
        var address = msg.ReadWord(0);
        var length = msg.ReadWord(2);
        var data = m_bridge.ReadMemory(address, length);

        var response = new byte[1 + data.Length];
        response[0] = 0; // No error
        Array.Copy(data, 0, response, 1, data.Length);
        SendResponse(msg.SequenceNumber, DzrpCommands.CMD_READ_MEM, response);
    }

    private void HandleWriteMem(DzrpMessage msg)
    {
        var address = msg.ReadWord(0);
        var length = msg.ReadWord(2);
        var data = msg.Payload.Skip(4).Take(length).ToArray();
        m_bridge.WriteMemory(address, data);
        SendResponse(msg.SequenceNumber, DzrpCommands.CMD_WRITE_MEM, 0);
    }

    private void HandleAddBreakpoint(DzrpMessage msg)
    {
        var address = msg.ReadWord(0);
        var bpId = m_bridge.AddBreakpoint(address);

        var response = new List<byte> { 0 }; // No error
        DzrpMessage.WriteWord(response, (ushort)bpId);
        SendResponse(msg.SequenceNumber, DzrpCommands.CMD_ADD_BREAKPOINT, response.ToArray());
    }

    private void HandleRemoveBreakpoint(DzrpMessage msg)
    {
        var bpId = msg.ReadWord(0);
        m_bridge.RemoveBreakpoint(bpId);
        SendResponse(msg.SequenceNumber, DzrpCommands.CMD_REMOVE_BREAKPOINT, 0);
    }

    private void OnBridgePaused(object sender, PauseEventArgs e)
    {
        // Send NTF_PAUSE notification
        var payload = new List<byte>
        {
            (byte)e.Reason
        };
        DzrpMessage.WriteWord(payload, e.Address);

        SendNotification(DzrpCommands.NTF_PAUSE, payload.ToArray());
    }

    private void SendResponse(byte seqNum, byte cmdId, params byte[] payload)
    {
        var response = DzrpMessage.CreateResponse(seqNum, cmdId, payload);
        lock (m_streamLock)
        {
            try
            {
                m_stream.Write(response, 0, response.Length);
            }
            catch
            {
                // Connection closed
            }
        }
    }

    private void SendNotification(byte notifId, byte[] payload)
    {
        var notification = DzrpMessage.CreateNotification(notifId, payload);
        lock (m_streamLock)
        {
            try
            {
                m_stream.Write(notification, 0, notification.Length);
            }
            catch
            {
                // Connection closed
            }
        }
    }

    public void Dispose()
    {
        Stop();
        m_stream?.Dispose();
        m_client?.Dispose();
    }
}

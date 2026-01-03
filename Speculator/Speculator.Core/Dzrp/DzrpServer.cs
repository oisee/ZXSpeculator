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

using System.Net;
using System.Net.Sockets;
using CSharp.Core;

namespace Speculator.Core.Dzrp;

/// <summary>
/// TCP server for DZRP (DeZog Remote Protocol) connections.
/// Allows VS Code's DeZog extension to debug Z80 programs.
/// </summary>
public class DzrpServer : IDisposable
{
    public const int DefaultPort = 11000;
    public const string DefaultBindAddress = "127.0.0.1";

    private readonly CPU m_cpu;
    private readonly int m_port;
    private readonly string m_bindAddress;
    private readonly IDzrpDebugBridge m_bridge;
    private TcpListener m_listener;
    private DzrpSession m_currentSession;
    private Thread m_acceptThread;
    private volatile bool m_isRunning;

    public bool IsEnabled { get; private set; }
    public bool IsClientConnected => m_currentSession != null;
    public int Port => m_port;

    public event EventHandler<bool> ClientConnectionChanged;
    public event EventHandler<PauseEventArgs> ExecutionPaused;
    public event EventHandler ExecutionContinued;

    public DzrpServer(CPU cpu, int port = DefaultPort, string bindAddress = DefaultBindAddress)
    {
        m_cpu = cpu;
        m_port = port;
        m_bindAddress = bindAddress;
        m_bridge = new DzrpDebugBridge(cpu);

        // Forward bridge events
        ((DzrpDebugBridge)m_bridge).Paused += (_, args) => ExecutionPaused?.Invoke(this, args);
        ((DzrpDebugBridge)m_bridge).Continued += (_, _) => ExecutionContinued?.Invoke(this, EventArgs.Empty);
    }

    public void Start()
    {
        if (m_isRunning)
            return;

        try
        {
            var bindIp = IPAddress.Parse(m_bindAddress);
            m_listener = new TcpListener(bindIp, m_port);
            m_listener.Start();
            m_isRunning = true;
            IsEnabled = true;

            m_acceptThread = new Thread(AcceptLoop) { Name = "DZRP Accept" };
            m_acceptThread.Start();

            Logger.Instance.Info($"DZRP server started on {m_bindAddress}:{m_port}");
        }
        catch (Exception ex)
        {
            Logger.Instance.Error($"Failed to start DZRP server: {ex.Message}");
            IsEnabled = false;
        }
    }

    public void Stop()
    {
        if (!m_isRunning)
            return;

        m_isRunning = false;

        m_currentSession?.Dispose();
        m_currentSession = null;

        try
        {
            m_listener?.Stop();
        }
        catch
        {
            // Ignore stop errors
        }

        m_acceptThread?.Join(1000);

        IsEnabled = false;
        Logger.Instance.Info("DZRP server stopped");
    }

    private void AcceptLoop()
    {
        while (m_isRunning)
        {
            try
            {
                var client = m_listener.AcceptTcpClient();

                // Disconnect existing session (DZRP is 1:1)
                if (m_currentSession != null)
                {
                    Logger.Instance.Info("DZRP: Disconnecting existing client");
                    m_currentSession.Dispose();
                    m_currentSession = null;
                }

                m_currentSession = new DzrpSession(client, m_bridge);
                m_currentSession.Disconnected += OnSessionDisconnected;
                m_currentSession.Start();

                ClientConnectionChanged?.Invoke(this, true);
                Logger.Instance.Info("DZRP client connected");
            }
            catch (SocketException) when (!m_isRunning)
            {
                // Expected during shutdown
            }
            catch (Exception ex)
            {
                if (m_isRunning)
                    Logger.Instance.Warn($"DZRP accept error: {ex.Message}");
            }
        }
    }

    private void OnSessionDisconnected(object sender, EventArgs e)
    {
        m_currentSession = null;
        ClientConnectionChanged?.Invoke(this, false);
        Logger.Instance.Info("DZRP client disconnected");
    }

    public void Dispose()
    {
        Stop();
    }
}

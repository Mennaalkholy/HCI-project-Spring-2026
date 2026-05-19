using System;
using System.ComponentModel;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;

public class SocketClient
{
    private TcpClient client;
    private NetworkStream stream;
    private StreamReader streamReader;
    private readonly string host;
    private readonly int port;
    private readonly object sendLock = new object();
    private volatile bool connected = false;
    private volatile bool intentionalDisconnect = false;
    private readonly ISynchronizeInvoke invokeTarget;

    public event Action<string> OnLineReceived;
    public event Action<bool> OnConnectionStateChanged;

    public bool IsConnected => connected;

    public SocketClient(string host = "localhost", int port = 5000, ISynchronizeInvoke invokeTarget = null)
    {
        this.host = host;
        this.port = port;
        this.invokeTarget = invokeTarget;
    }

    public void ConnectAsync()
    {
        Thread t = new Thread(() =>
        {
            while (!connected)
            {
                try
                {
                    client = new TcpClient(host, port);
                    stream = client.GetStream();
                    streamReader = new StreamReader(stream, Encoding.UTF8, false, 1024, true);
                    connected = true;
                    Console.WriteLine("[Socket] Connected to Python server at " + host + ":" + port);
                    try
                    {
                        byte[] regLine = Encoding.UTF8.GetBytes("{\"role\":\"csharp\"}\n");
                        stream.Write(regLine, 0, regLine.Length);
                        stream.Flush();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("[Socket] Role registration failed: " + ex.Message);
                    }
                    NotifyConnectionState(true);
                    Thread readThread = new Thread(ReadLoop);
                    readThread.IsBackground = true;
                    readThread.Start();
                }
                catch (Exception ex)
                {
                    Console.WriteLine("[Socket] Connection failed: " + ex.Message + " — retrying in 3s…");
                    try { client?.Close(); } catch { }
                    client = null;
                    stream = null;
                    streamReader = null;
                    Thread.Sleep(3000);
                }
            }
        });
        t.IsBackground = true;
        t.Start();
    }

    private void NotifyConnectionState(bool ok)
    {
        if (invokeTarget != null && invokeTarget.InvokeRequired)
        {
            try
            {
                invokeTarget.BeginInvoke(new Action(() => { OnConnectionStateChanged?.Invoke(ok); }), null);
            }
            catch (ObjectDisposedException) { }
        }
        else
            OnConnectionStateChanged?.Invoke(ok);
    }

    private void ReadLoop()
    {
        try
        {
            while (connected && streamReader != null)
            {
                string line = streamReader.ReadLine();
                if (line == null) break;
                Console.WriteLine("[Socket] Received: " + line);
                string copy = line;
                if (invokeTarget != null && invokeTarget.InvokeRequired)
                {
                    try
                    {
                        invokeTarget.BeginInvoke(new Action(() => { OnLineReceived?.Invoke(copy); }), null);
                    }
                    catch (ObjectDisposedException) { }
                }
                else
                {
                    OnLineReceived?.Invoke(copy);
                }
            }
        }
        catch (Exception ex)
        {
            if (connected)
                Console.WriteLine("[Socket] Read error: " + ex.Message);
        }
        finally
        {
            connected = false;
            try { streamReader?.Close(); } catch { }
            try { stream?.Close(); } catch { }
            try { client?.Close(); } catch { }
            NotifyConnectionState(false);
            if (!intentionalDisconnect)
            {
                Console.WriteLine("[Socket] Disconnected — will attempt to reconnect…");
                ConnectAsync();
            }
        }
    }

    public void Send(string message)
    {
        if (!connected || stream == null)
        {
            string preview = message ?? "";
            if (preview.Length > 120) preview = preview.Substring(0, 120) + "…";
            Console.WriteLine("[Socket] Send skipped (not connected to hub). Message was: " + preview);
            return;
        }
        lock (sendLock)
        {
            try
            {
                byte[] data = Encoding.UTF8.GetBytes(message + "\n");
                stream.Write(data, 0, data.Length);
                stream.Flush();
                Console.WriteLine("[Socket] Sent: " + message);
            }
            catch (Exception ex)
            {
                Console.WriteLine("[Socket] Send failed: " + ex.Message);
                connected = false;
                NotifyConnectionState(false);
            }
        }
    }

    /// <param name="targetService">When set, NotificationHub routes this line only to that Python service id (e.g. face_auth).</param>
    public void SendEvent(string eventName, string details, string targetService = null)
    {
        string json;
        if (string.IsNullOrEmpty(targetService))
            json = "{\"event\": \"" + eventName + "\", " + details + "}";
        else
            json = "{\"event\": \"" + eventName + "\", \"target\": \"" + targetService + "\", " + details + "}";
        Send(json);
    }

    public void Disconnect()
    {
        intentionalDisconnect = true;
        Send("{\"event\": \"disconnect\"}");
        connected = false;
        NotifyConnectionState(false);
        try { streamReader?.Close(); } catch { }
        try { stream?.Close(); } catch { }
        try { client?.Close(); } catch { }
        Console.WriteLine("[Socket] Disconnected.");
    }
}

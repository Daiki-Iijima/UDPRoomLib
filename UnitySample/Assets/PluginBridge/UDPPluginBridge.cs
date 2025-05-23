using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using UnityEngine;

public static class UDPPluginBridge
{
    private static readonly ConcurrentQueue<string> MessageQueue = new();
    private static UdpClient _receiver;
    private static UdpClient _sender;
    private static CancellationTokenSource _receiveCts;
    private static int _defaultPort = 5000;

#if UNITY_IOS && !UNITY_EDITOR
    [DllImport("__Internal")] private static extern void StartUDPBroadcastWithPort(int port);
    [DllImport("__Internal")] private static extern void StartUDPReceivingWithPort(int port);
    [DllImport("__Internal")] private static extern void StopUDPBroadcast();
    [DllImport("__Internal")] private static extern void SendUDPMessage(string message);
    [DllImport("__Internal")] private static extern IntPtr PollUDPMessage();
    [DllImport("__Internal")] private static extern IntPtr GetLocalIPAddress();
#endif

    public static void StartSender(int port = 5000)
    {
        _defaultPort = port;
#if UNITY_IOS && !UNITY_EDITOR
        StartUDPBroadcastWithPort(port);
#else
        // Debug.Log($"[UDPPluginBridge] StartSender({port})");
        StartCSharpSender();
#endif
    }

    public static void StartReceiver(int port = 5000)
    {
        _defaultPort = port;
#if UNITY_IOS && !UNITY_EDITOR
        StartUDPReceivingWithPort(port);
#else
        // Debug.Log($"[UDPPluginBridge] StartReceiver({port})");
        StartCSharpReceiver(port);
#endif
    }

    public static void Stop()
    {
#if UNITY_IOS && !UNITY_EDITOR
        StopUDPBroadcast();
#else
        // Debug.Log("[UDPPluginBridge] Stop()");
        StopCSharpReceiver();
        StopCSharpSender();
#endif
    }

    public static void Send(string message)
    {
#if UNITY_IOS && !UNITY_EDITOR
        SendUDPMessage(message);
#else
        // Debug.Log($"[UDPPluginBridge] Send: {message}");
        SendCSharpUDP(message);
#endif
    }

    public static string PollBufferedMessage()
    {
#if UNITY_IOS && !UNITY_EDITOR
        IntPtr ptr = PollUDPMessage();
        if (ptr != IntPtr.Zero)
        {
            string message = Marshal.PtrToStringUTF8(ptr);
            Marshal.FreeHGlobal(ptr);
            return message;
        }
        return null;
#else
        if (MessageQueue.TryDequeue(out var msg))
        {
            return msg;
        }
        return null;
#endif
    }

    // === C# 受信処理 (UWP/Editor) ===
#if !UNITY_IOS || UNITY_EDITOR
    private static void StartCSharpReceiver(int port)
    {
        _receiveCts = new CancellationTokenSource();
        _receiver = new UdpClient(port);
        _receiver.EnableBroadcast = true;

        var token = _receiveCts.Token;
        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    if (_receiver.Available > 0)
                    {
                        IPEndPoint remoteEp = new IPEndPoint(IPAddress.Any, 0);
                        byte[] data = _receiver.Receive(ref remoteEp);
                        string msg = Encoding.UTF8.GetString(data);
                        MessageQueue.Enqueue(msg);
                        Debug.Log($"C# UDP受信: {msg}");
                    }
                    Thread.Sleep(10);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[UDPPluginBridge] UDP受信エラー: {ex.Message}");
            }
        });
    }

    private static void StopCSharpReceiver()
    {
        try
        {
            _receiveCts?.Cancel();
            _receiver?.Close();
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[UDPPluginBridge] StopReceiver error: {ex.Message}");
        }
    }
#endif
    
    public static string GetLocalIP()
    {
#if UNITY_IOS && !UNITY_EDITOR
    IntPtr ptr = GetLocalIPAddress();
    if (ptr != IntPtr.Zero)
        return Marshal.PtrToStringUTF8(ptr);
    return null;
#else
        var host = Dns.GetHostEntry(Dns.GetHostName());

        foreach (var ip in host.AddressList)
        {
            if (ip.AddressFamily == AddressFamily.InterNetwork)
            {
                string ipStr = ip.ToString();
                if (ipStr.StartsWith("192.")) // 優先的に使いたいIP
                    return ipStr;
            }
        }

        // 192.が見つからなかったら10.xでも返す
        foreach (var ip in host.AddressList)
        {
            if (ip.AddressFamily == AddressFamily.InterNetwork)
                return ip.ToString();
        }

        return null;
#endif
    } 

    // === C# 送信処理 (UWP/Editor) ===
#if !UNITY_IOS || UNITY_EDITOR
    private static void StartCSharpSender()
    {
        try
        {
            _sender = new UdpClient();
            _sender.EnableBroadcast = true;
            Debug.Log("[UDPPluginBridge] C# UDP送信初期化完了");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[UDPPluginBridge] UDP送信初期化エラー: {ex.Message}");
        }
    }

    private static void StopCSharpSender()
    {
        try
        {
            _sender?.Close();
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[UDPPluginBridge] StopSender error: {ex.Message}");
        }
    }

    private static void SendCSharpUDP(string message)
    {
        if (_sender == null)
        {
            Debug.LogWarning("[UDPPluginBridge] 送信未初期化");
            return;
        }

        try
        {
            UDPMessage msg = new UDPMessage();
            msg.timestamp = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ssZ");
            msg.payload = message;
            var json = JsonUtility.ToJson(msg);
            byte[] data = Encoding.UTF8.GetBytes(json);
            IPEndPoint endPoint = new IPEndPoint(GetBroadcastAddress(), _defaultPort);
            _sender.Send(data, data.Length, endPoint);
            Debug.Log($"C# UDP送信: {json}");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[UDPPluginBridge] UDP送信失敗: {ex.Message}");
        }
    }
    
/// <summary>
/// サブネットのブロードキャストアドレスを計算
/// 例: IP 192.168.0.12, Mask 255.255.255.0 → 192.168.0.255
/// </summary>
private static IPAddress GetBroadcastAddress()
{
    foreach (var ni in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
    {
        if (ni.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up)
            continue;

        foreach (var ua in ni.GetIPProperties().UnicastAddresses)
        {
            if (ua.Address.AddressFamily == AddressFamily.InterNetwork)
            {
                byte[] ip = ua.Address.GetAddressBytes();
                byte[] mask = ua.IPv4Mask?.GetAddressBytes();
                if (mask == null) continue;

                byte[] broadcast = new byte[4];
                for (int i = 0; i < 4; i++)
                    broadcast[i] = (byte)(ip[i] | (mask[i] ^ 255));

                return new IPAddress(broadcast);
            }
        }
    }

    // Fallback to 255.255.255.255
    return IPAddress.Broadcast;
}
#endif
}

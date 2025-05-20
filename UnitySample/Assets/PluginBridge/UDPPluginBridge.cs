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
    private static readonly ConcurrentQueue<string> messageQueue = new();
    private static UdpClient receiver;
    private static UdpClient sender;
    private static CancellationTokenSource receiveCts;
    private static int defaultPort = 5000;

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
        defaultPort = port;
#if UNITY_IOS && !UNITY_EDITOR
        StartUDPBroadcastWithPort(port);
#else
        // Debug.Log($"[UDPPluginBridge] StartSender({port})");
        StartCSharpSender();
#endif
    }

    public static void StartReceiver(int port = 5000)
    {
        defaultPort = port;
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
        if (messageQueue.TryDequeue(out var msg))
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
        receiveCts = new CancellationTokenSource();
        receiver = new UdpClient(port);
        receiver.EnableBroadcast = true;

        var token = receiveCts.Token;
        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    if (receiver.Available > 0)
                    {
                        IPEndPoint remoteEP = new IPEndPoint(IPAddress.Any, 0);
                        byte[] data = receiver.Receive(ref remoteEP);
                        string msg = Encoding.UTF8.GetString(data);
                        messageQueue.Enqueue(msg);
                        // Debug.Log($"📥 C# UDP受信: {msg}");
                    }
                    Thread.Sleep(10);
                }
            }
            catch (Exception ex)
            {
                // Debug.LogError($"[UDPPluginBridge] UDP受信エラー: {ex.Message}");
            }
        });
    }

    private static void StopCSharpReceiver()
    {
        try
        {
            receiveCts?.Cancel();
            receiver?.Close();
        }
        catch (Exception ex)
        {
            // Debug.LogWarning($"[UDPPluginBridge] StopReceiver error: {ex.Message}");
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
        var host = System.Net.Dns.GetHostEntry(System.Net.Dns.GetHostName());

        foreach (var ip in host.AddressList)
        {
            if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            {
                string ipStr = ip.ToString();
                if (ipStr.StartsWith("192.")) // 優先的に使いたいIP
                    return ipStr;
            }
        }

        // 192.が見つからなかったら10.xでも返す
        foreach (var ip in host.AddressList)
        {
            if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
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
            sender = new UdpClient();
            sender.EnableBroadcast = true;
            // Debug.Log("[UDPPluginBridge] C# UDP送信初期化完了");
        }
        catch (Exception ex)
        {
            // Debug.LogError($"[UDPPluginBridge] UDP送信初期化エラー: {ex.Message}");
        }
    }

    private static void StopCSharpSender()
    {
        try
        {
            sender?.Close();
        }
        catch (Exception ex)
        {
            // Debug.LogWarning($"[UDPPluginBridge] StopSender error: {ex.Message}");
        }
    }

    private static void SendCSharpUDP(string message)
    {
        if (sender == null)
        {
            // Debug.LogWarning("[UDPPluginBridge] 送信未初期化");
            return;
        }

        try
        {
            byte[] data = Encoding.UTF8.GetBytes(message);
            IPEndPoint endPoint = new IPEndPoint(IPAddress.Broadcast, defaultPort);
            sender.Send(data, data.Length, endPoint);
            // Debug.Log($"📤 C# UDP送信: {message}");
        }
        catch (Exception ex)
        {
            // Debug.LogError($"[UDPPluginBridge] UDP送信失敗: {ex.Message}");
        }
    }
#endif
}

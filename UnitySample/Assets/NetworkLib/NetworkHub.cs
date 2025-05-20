using System;
using UnityEngine;

public class NetworkHub : MonoBehaviour
{
    [Header("Ports")]
    public int udpPort = 5000;
    public int wsPort = 8765;

    public bool AutoJoinFirst = true;

    /* Public events */
    public event Action<UdpTestUI.DeviceInfo> OnJoin, OnLeave;
    public event Action<UdpTestUI.DeviceInfo, string> OnMessage;

    /* internals */
    UdpDiscovery udp;
    WsRoomServer server;
    WsRoomClient client;

    public event Action<string> OnServerDiscovered;

    /* ----------------- Public API ----------------- */
    public void StartServer()
    {
        Stop(); // reset
        var url = $"ws://{UDPPluginBridge.GetLocalIP()}:{wsPort}";
        server = new WsRoomServer(wsPort);

        server.OnJoin    += i => OnJoin?.Invoke(i);
        server.OnLeave   += i => OnLeave?.Invoke(i);
        server.OnMessage += (i, m) => OnMessage?.Invoke(i, m);

        udp = new UdpDiscovery(udpPort);
        udp.StartBroadcasting(url, 1f);
        Debug.Log($"[Hub] Server started @ {url}");
    }

    public void StartClient(bool autoJoin = true)
    {
        Stop(); // reset

        client = new WsRoomClient();
        client.OnOpen    += () => NetworkThread.QueueOnMain(() => Debug.Log("[Hub] Ws Connected"));
        client.OnClose   += () => NetworkThread.QueueOnMain(() => Debug.Log("[Hub] Ws Closed"));
        client.OnMessage += m => NetworkThread.QueueOnMain(() => OnMessage?.Invoke(null, m));

        udp = new UdpDiscovery(udpPort);
        udp.StartListening(url =>
        {
            Debug.Log($"[Hub] Found {url}");
            OnServerDiscovered?.Invoke(url);

            if (autoJoin && !client.IsConnected)
                client.Connect(url);
        });
    }

    public void ConnectTo(string url)
    {
        NetworkThread.QueueOnMain(() =>
        {
            if (client == null)
            {
                client = new WsRoomClient();
                client.OnOpen    += () => NetworkThread.QueueOnMain(() => Debug.Log("[Hub] Ws Connected"));
                client.OnClose   += () => NetworkThread.QueueOnMain(() => Debug.Log("[Hub] Ws Closed"));
                client.OnMessage += m => NetworkThread.QueueOnMain(() => OnMessage?.Invoke(null, m));
            }

            if (!client.IsConnected)
            {
                client.Connect(url);
            }
            else
            {
                Debug.LogWarning("[Hub] すでに接続済みです");
            }
        });
    }

    public void Send(string msg)
    {
        if (client != null && client.IsConnected)
        {
            client.Send(msg);
        }
    }

    /// <summary>
    /// 指定されたクライアントにメッセージを送信（サーバー側のみ）
    /// </summary>
    public bool SendToClient(string deviceId, string msg)
    {
        if (server != null)
        {
            return server.SendTo(deviceId, msg);
        }
        return false;
    }

    /// <summary>
    /// 全クライアントにブロードキャスト（サーバー側のみ）
    /// </summary>
    public void Broadcast(string msg)
    {
        if (server != null)
        {
            foreach (var d in server.Clients)
            {
                server.SendTo(d.id, msg);
            }
        }
    }

    public void Stop()
    {
        udp?.Stop(); udp = null;
        server?.Stop(); server = null;
        client?.Close(); client = null;
    }

    void Update()
    {
        NetworkThread.FlushMainQueue();

        if (server != null && udp != null)
        {
            var url = $"ws://{UDPPluginBridge.GetLocalIP()}:{wsPort}";
            udp.Update(Time.deltaTime, wsUrlIfServer: url);
        }
        else if (client != null && udp != null)
        {
            udp.Update(Time.deltaTime);
        }
    }

    void OnApplicationQuit() => Stop();
}

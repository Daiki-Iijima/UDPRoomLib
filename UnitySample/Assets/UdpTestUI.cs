using TMPro;
using UnityEngine;
using UnityEngine.UI;
using System;
using System.Collections.Generic;
using WebSocketSharp;
using WebSocketSharp.Server;
using System.Collections.Concurrent;

public class UdpTestUI : MonoBehaviour
{
    [Header("Client")] public Button startClientBtn;
    public Button stopClientBtn;

    [Header("Server")] public Button startServerBtn;
    public Button stopServerBtn;

    [Header("UI")] public TMP_Text logText;
    public TMP_Text deviceListText;

    private const int udpPort = 5000;
    private const int wsPort = 8765;
    private readonly Queue<string> logQueue = new();

    private WebSocketServer wsServer;
    private Dictionary<string, DeviceInfo> connectedDevices = new();
private Dictionary<string, string> wsToIdMap = new(); // key: session ID, value: device ID

    private WebSocket wsClient;

    private float broadcastInterval = 1f;
    private float broadcastTimer = 0f;
    private bool isServerMode = false;

    private readonly ConcurrentQueue<Action> mainThreadQueue = new();

    void Start()
    {
        gameObject.name = "UdpBridge";

        startClientBtn.onClick.AddListener(() =>
        {
            UDPPluginBridge.StartReceiver(udpPort);
            AppendLog("[Client] UDP受信開始");
        });

        stopClientBtn.onClick.AddListener(() =>
        {
            UDPPluginBridge.Stop();
            wsClient?.Close();
            AppendLog("[Client] 停止");
        });

        startServerBtn.onClick.AddListener(() =>
        {
            UDPPluginBridge.StartSender(udpPort);
            StartWebSocketServer();
            isServerMode = true;
            AppendLog("[Server] モード開始");
        });

        stopServerBtn.onClick.AddListener(() =>
        {
            UDPPluginBridge.Stop();
            StopWebSocketServer();
            isServerMode = false;
            AppendLog("[Server] 停止");
        });
    }

    void OnApplicationQuit()
    {
        UDPPluginBridge.Stop();
        StopWebSocketServer();
        wsClient?.Close();
    }

    void Update()
    {
        while (mainThreadQueue.TryDequeue(out var action)) action?.Invoke();

        if (isServerMode)
        {
            broadcastTimer += Time.deltaTime;
            if (broadcastTimer >= broadcastInterval)
            {
                broadcastTimer = 0f;
                string ip = UDPPluginBridge.GetLocalIP();
                if (!string.IsNullOrEmpty(ip))
                {
                    string msg = $"ws://{ip}:{wsPort}";
                    UDPPluginBridge.Send(msg);
                    AppendLog($"[Server] UDPブロードキャスト送信: {msg}");
                }
            }
        }

        string udpMsg;
        while ((udpMsg = UDPPluginBridge.PollBufferedMessage()) != null)
        {
            AppendLog("[Client UDP Receive]");
            try
            {
                var parsed = JsonUtility.FromJson<UDPMessage>(udpMsg);
                string wsUrl = parsed.payload.Replace("\\/", "/");

                if (wsUrl.StartsWith("ws://") && wsClient == null)
                {
                    wsClient = new WebSocket(wsUrl);
                    wsClient.OnOpen += (sender, e) =>
                    {
                        mainThreadQueue.Enqueue(() =>
                        {
                            AppendLog("[Client] WebSocket 接続成功");
                            UDPPluginBridge.Stop();
                            SendDeviceInfoToServer();
                        });
                    };
                    wsClient.OnMessage += (sender, e) => AppendLog("[Client] 受信: " + e.Data);
                    wsClient.OnClose += (sender, e) =>
                    {
                        mainThreadQueue.Enqueue(() =>
                        {
                            AppendLog("[Client] 切断");
                            wsClient = null; // ✅ 再接続を許可
                        });
                    };
                    wsClient.OnError += (sender, e) =>
                    {
                        mainThreadQueue.Enqueue(() =>
                        {
                            AppendLog("[Client] エラー: " + e.Message);
                            wsClient = null; // 安全のため破棄
                        });
                    };
                    wsClient.ConnectAsync();
                }
            }
            catch (Exception ex)
            {
                AppendLog("[Client UDP] 解析エラー: " + ex.Message);
            }
        }
    }

    private void SendDeviceInfoToServer()
    {
        var info = new DeviceInfo
        {
            id = SystemInfo.deviceUniqueIdentifier,
            device = Application.platform.ToString()
        };
        string json = JsonUtility.ToJson(info);
        AppendLog($"[Client] 送信準備: {json}");
        try
        {
            wsClient?.Send(json);
            AppendLog("[Client] 情報送信完了");
        }
        catch (Exception ex)
        {
            AppendLog("[Client] 送信失敗: " + ex.Message);
        }
    }

    private void StartWebSocketServer()
    {
        IdentifyBehavior.OnIdentified = OnClientIdentified;
        IdentifyBehavior.OnClosed = OnClientDisconnected;
        wsServer = new WebSocketServer(wsPort);
        wsServer.AddWebSocketService<IdentifyBehavior>("/");
        wsServer.Start();
        AppendLog("[Server] WebSocketサーバー起動");
    }

    private void StopWebSocketServer()
    {
        wsServer?.Stop();
        wsServer = null;
        connectedDevices.Clear();
        wsToIdMap.Clear();
        UpdateDeviceList();
    }

    private void OnClientIdentified(DeviceInfo info, string sessionId)
    {
        connectedDevices[info.id] = info;
        wsToIdMap[sessionId] = info.id;
        AppendLog($"[Server] 新規デバイス: {info.device} ({info.id})");
        UpdateDeviceList();
    }

    private void OnClientDisconnected(string sessionId)
    {
        if (wsToIdMap.TryGetValue(sessionId, out var id))
        {
            connectedDevices.Remove(id);
            wsToIdMap.Remove(sessionId);
            AppendLog($"[Server] デバイス切断: {id}");
            UpdateDeviceList();
        }
    }

    private void UpdateDeviceList()
    {
        deviceListText.text = "接続中デバイス:\n";
        foreach (var dev in connectedDevices.Values)
        {
            deviceListText.text += $"- {dev.device} ({dev.id})\n";
        }
    }

    private void AppendLog(string message)
    {
        if (logQueue.Count >= 10) logQueue.Dequeue();
        logQueue.Enqueue(message);
        logText.text = string.Join("\n", logQueue);
    }

    [Serializable]
    public class DeviceInfo { public string id; public string device; }

    public class IdentifyBehavior : WebSocketBehavior
    {
        public static Action<DeviceInfo, string> OnIdentified;
        public static Action<string> OnClosed;

        protected override void OnMessage(MessageEventArgs e)
        {
            var json = e.Data;
            var info = JsonUtility.FromJson<DeviceInfo>(json);
            OnIdentified?.Invoke(info, ID); // ID は string 型
        }

        protected override void OnClose(CloseEventArgs e)
        {
            OnClosed?.Invoke(ID);
        }
    }
}
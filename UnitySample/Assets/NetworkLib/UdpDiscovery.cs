using System;
using System.Collections.Concurrent;
using UnityEngine;

[Serializable]
public class UDPMessage
{
    public string timestamp;
    public string payload;
}

public class UdpDiscovery
{
    private readonly int port;
    private bool isServer = false;
    private bool running = false;
    private float interval = 1f;
    private float timer = 0f;
    private readonly ConcurrentQueue<string> rxQueue = new();

    public event Action<string> OnDiscovered;

    public UdpDiscovery(int port = 5000)
    {
        this.port = port;
    }

    public void StartBroadcasting(string wsUrl, float intervalSec = 1f)
    {
        if (running) return;
        this.interval = intervalSec;
        isServer = true;
        running = true;
        UDPPluginBridge.StartSender(port);
        Debug.Log("[Discovery] Start Broadcasting");
    }

    public void StartListening(Action<string> onDiscovered)
    {
        if (running) return;
        isServer = false;
        running = true;
        OnDiscovered = onDiscovered;
        UDPPluginBridge.StartReceiver(port);
        Debug.Log("[Discovery] Start Listening");
    }

    public void Stop()
    {
        running = false;
        UDPPluginBridge.Stop();
    }

    // 毎フレーム呼ぶこと（MonoBehaviourのUpdate内など）
    public void Update(float deltaTime, string wsUrlIfServer = null)
    {
        if (!running) return;

        if (isServer && !string.IsNullOrEmpty(wsUrlIfServer))
        {
            timer += deltaTime;
            if (timer >= interval)
            {
                timer = 0f;
                UDPPluginBridge.Send(wsUrlIfServer);
            }
        }

        string msg;
        while ((msg = UDPPluginBridge.PollBufferedMessage()) != null)
        {
            try
            {
                var parsed = JsonUtility.FromJson<UDPMessage>(msg);
                var url = parsed.payload.Replace("\\/", "/");
                if (url.StartsWith("ws://"))
                    rxQueue.Enqueue(url);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Discovery] Parse failed: " + ex.Message);
            }
        }

        while (rxQueue.TryDequeue(out var url))
        {
            OnDiscovered?.Invoke(url);
        }
    }
}

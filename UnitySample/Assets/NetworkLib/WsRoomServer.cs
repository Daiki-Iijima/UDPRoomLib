using System;
using WebSocketSharp;
using WebSocketSharp.Server;
using System.Collections.Generic;
using UnityEngine;

public class WsRoomServer
{
    private readonly WebSocketServer wss;
    private readonly Dictionary<string, UdpTestUI.DeviceInfo> devices = new();

    public event Action<UdpTestUI.DeviceInfo> OnJoin;
    public event Action<UdpTestUI.DeviceInfo> OnLeave;
    public event Action<UdpTestUI.DeviceInfo, string> OnMessage;

    public WsRoomServer(int port = 8765)
    {
        wss = new WebSocketServer(port);
        RoomBehavior.OnIdentified = HandleIdentified;
        RoomBehavior.OnClosed = HandleClosed;
        RoomBehavior.OnTextMessage = HandleMessage;
        wss.AddWebSocketService<RoomBehavior>("/");
        wss.Start();
    }

    public void Stop() => wss?.Stop();

    public IEnumerable<UdpTestUI.DeviceInfo> Clients => devices.Values;

    public bool SendTo(string deviceId, string msg)
    {
        foreach (var kv in devices)
        {
            if (kv.Value.id == deviceId)
            {
                wss.WebSocketServices["/"].Sessions.SendTo(msg, kv.Key);
                return true;
            }
        }
        return false;
    }

    public void Broadcast(string msg)
    {
        foreach (var device in devices.Values)
        {
            SendTo(device.id, msg);
        }
    }

    private void HandleIdentified(UdpTestUI.DeviceInfo info, string sessionId)
    {
        devices[sessionId] = info;
        OnJoin?.Invoke(info);
    }

    private void HandleClosed(string sessionId)
    {
        if (devices.TryGetValue(sessionId, out var info))
        {
            devices.Remove(sessionId);
            OnLeave?.Invoke(info);
        }
    }

    private void HandleMessage(string sessionId, string msg)
    {
        if (devices.TryGetValue(sessionId, out var info))
        {
            OnMessage?.Invoke(info, msg);
        }
    }

    class RoomBehavior : WebSocketBehavior
    {
        public static Action<UdpTestUI.DeviceInfo, string> OnIdentified;
        public static Action<string> OnClosed;
        public static Action<string, string> OnTextMessage;

        protected override void OnMessage(MessageEventArgs e)
        {
            if (e.IsText)
            {
                if (e.Data.StartsWith("{"))
                {
                    var info = JsonUtility.FromJson<UdpTestUI.DeviceInfo>(e.Data);
                    OnIdentified?.Invoke(info, ID);
                }
                else
                {
                    OnTextMessage?.Invoke(ID, e.Data);
                }
            }
        }

        protected override void OnClose(CloseEventArgs e)
        {
            OnClosed?.Invoke(ID);
        }
    }
}

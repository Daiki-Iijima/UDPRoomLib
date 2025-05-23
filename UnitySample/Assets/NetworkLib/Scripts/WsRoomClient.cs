using System;
using WebSocketSharp;
using UnityEngine;

public class WsRoomClient
{
    WebSocket ws;
    public bool IsConnected => ws?.IsAlive == true;

    public event Action OnOpen, OnClose;
    public event Action<string> OnMessage;
    readonly DeviceInfo myInfo = new()
    {
        id     = SystemInfo.deviceUniqueIdentifier,
        device = Application.platform.ToString()
    };

    public void Connect(string url)
    {
        Debug.Log($"Connecting to {url}");
        if (IsConnected) return;
        ws = new WebSocket(url);
        ws.OnOpen    += (_,__) => { ws.Send(JsonUtility.ToJson(myInfo)); OnOpen?.Invoke(); };
        ws.OnMessage += (_,e)  => OnMessage?.Invoke(e.Data);
        ws.OnClose   += (_,__) => { ws = null; OnClose?.Invoke(); };
        ws.ConnectAsync();
    }
    public void Send(string msg) => ws?.Send(msg);
    public void Close() => ws?.Close();
}
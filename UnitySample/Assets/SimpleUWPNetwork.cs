using System;
using System.Collections;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;
using UnityEngine;

public class SimpleUWPNetwork : MonoBehaviour
{
    private string localIP;
    private string udpIP;
    private UdpClient client;
    private void Start()
    {
        //  UDPで使用するブロードキャストアドレスの作成
        localIP=UDPPluginBridge.GetLocalIP();
        var ipArray = localIP.Split('.');
        ipArray[3] = "255";
        udpIP = string.Join(".", ipArray);
        
        //  UDPクライアントの生成
        client = new UdpClient();
        client.EnableBroadcast = true;
        client.Connect(udpIP,5000);
    }

    private const float WaitTime = 1f;
    private float timer = WaitTime;

    private void Update()
    {
        timer -= Time.deltaTime;
        
        if (timer <= 0)
        {
            var payload = $"ws://{localIP}:8765";
            UDPMessage msg = new UDPMessage();
            msg.timestamp = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ssZ");
            msg.payload = payload;
            var json = JsonUtility.ToJson(msg);
            byte[] dgram = Encoding.UTF8.GetBytes(json);
            
            client.Send(dgram, dgram.Length);
            
            Debug.Log($"送信:{json}");
            
            timer = WaitTime;
        }
    }
}

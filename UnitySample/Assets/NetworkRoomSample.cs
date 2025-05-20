using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class NetworkRoomSample : MonoBehaviour
{
    [SerializeField] private Button startHostBtn;
    [SerializeField] private TextMeshProUGUI toggleHostLabel;

    [SerializeField] private Button startClientBtn;
    [SerializeField] private TextMeshProUGUI logText;

    [SerializeField] private Transform hostListRootTrans;
    [SerializeField] private GameObject hostListItemPrefab;
    [SerializeField] private GameObject connectedDeviceItemPrefab;

    [SerializeField] private TMP_InputField messageInputField;
    [SerializeField] private Button sendBtn;
    [SerializeField] private TMP_Dropdown deviceDropdown;
    [SerializeField] private Toggle sendToAllToggle; // ✅ 追加

    [SerializeField] private NetworkHub hub;

    private bool isHosting = false;

    private readonly HashSet<string> discoveredHosts = new();
    private readonly Dictionary<string, float> hostLastSeen = new();
    private readonly Dictionary<string, GameObject> hostListItems = new();
    private readonly Dictionary<string, GameObject> connectedDeviceItems = new();
    private readonly Dictionary<string, Queue<string>> messageBuffers = new();

    void Start()
    {
        startClientBtn.onClick.AddListener(() =>
        {
            isHosting = false;
            ClearLog();
            discoveredHosts.Clear();
            ClearHostList();
            hub.StartClient(autoJoin: false);
            Append("[System] サーバー検索中...");
            toggleHostLabel.text = "Start Host";
        });

        startHostBtn.onClick.AddListener(() =>
        {
            isHosting = !isHosting;
            ClearLog();
            ClearHostList();

            if (isHosting)
            {
                hub.StartServer();
                Append("[System] サーバー起動");
                toggleHostLabel.text = "Stop Host";
            }
            else
            {
                hub.Stop();
                Append("[System] サーバー停止");
                toggleHostLabel.text = "Start Host";
            }
        });

        hub.OnServerDiscovered += url =>
        {
            hostLastSeen[url] = Time.time;
            if (!isHosting && discoveredHosts.Add(url))
            {
                AddHostListItem(url);
                Append($"[Client] 検出: {url}");
            }
        };

        hub.OnJoin += dev =>
        {
            NetworkThread.QueueOnMain(() =>
            {
                if (!isHosting) return;
                Append($"[Join] {dev.device} ({dev.id})");

                if (!connectedDeviceItems.ContainsKey(dev.id))
                {
                    var go = Instantiate(connectedDeviceItemPrefab, hostListRootTrans);
                    var label = go.transform.Find("Title")?.GetComponent<TextMeshProUGUI>();
                    if (label != null)
                        label.text = $"{dev.device} ({dev.id})";

                    var messageText = go.transform.Find("MessageLog")?.GetComponent<TextMeshProUGUI>();
                    if (messageText != null)
                        messageText.text = "";

                    connectedDeviceItems[dev.id] = go;
                    messageBuffers[dev.id] = new Queue<string>();

                    deviceDropdown.options.Add(new TMP_Dropdown.OptionData(dev.id));
                    deviceDropdown.RefreshShownValue();
                }
            });
        };

        hub.OnLeave += dev =>
        {
            if (!isHosting) return;
            Append($"[Leave] {dev.device} ({dev.id})");

            if (connectedDeviceItems.TryGetValue(dev.id, out var go))
            {
                Destroy(go);
                connectedDeviceItems.Remove(dev.id);
            }
            messageBuffers.Remove(dev.id);

            int index = deviceDropdown.options.FindIndex(o => o.text == dev.id);
            if (index >= 0)
            {
                deviceDropdown.options.RemoveAt(index);
                deviceDropdown.RefreshShownValue();
            }
        };

        hub.OnMessage += (dev, msg) =>
        {
            var from = dev != null ? dev.device : "Server";
            Append($"[{from}] {msg}");

            if (!isHosting || dev == null) return;

            NetworkThread.QueueOnMain(() =>
            {
                if (!connectedDeviceItems.TryGetValue(dev.id, out var go)) return;
                if (!messageBuffers.TryGetValue(dev.id, out var buffer)) return;

                buffer.Enqueue(msg);
                while (buffer.Count > 8) buffer.Dequeue();

                var messageText = go.transform.Find("MessageLog")?.GetComponent<TextMeshProUGUI>();
                if (messageText != null)
                    messageText.text = string.Join("\n", buffer);
            });
        };

        sendBtn.onClick.AddListener(() =>
        {
            var text = messageInputField.text.Trim();
            if (string.IsNullOrEmpty(text)) return;

            if (isHosting)
            {
                if (sendToAllToggle.isOn)
                {
                    hub.Broadcast(text);
                    Append($"[Host→All] {text}");
                }
                else if (deviceDropdown.value >= 0 && deviceDropdown.options.Count > 0)
                {
                    var targetId = deviceDropdown.options[deviceDropdown.value].text;
                    hub.SendToClient(targetId, text);
                    Append($"[Host→{targetId}] {text}");
                }
            }
            else
            {
                hub.Send(text);
                Append($"[Client] {text}");
            }

            messageInputField.text = "";
        });
    }

    void Update()
    {
        if (!isHosting)
        {
            float now = Time.time;
            var expired = new List<string>();

            foreach (var kv in hostLastSeen)
            {
                if (now - kv.Value > 3f)
                    expired.Add(kv.Key);
            }

            foreach (var url in expired)
            {
                hostLastSeen.Remove(url);
                discoveredHosts.Remove(url);
                if (hostListItems.TryGetValue(url, out var go))
                {
                    Destroy(go);
                    hostListItems.Remove(url);
                    Append($"[Client] タイムアウト: {url}");
                }
            }
        }
    }

    void AddHostListItem(string url)
    {
        var go = Instantiate(hostListItemPrefab, hostListRootTrans);
        var urlText = go.transform.Find("UrlText")?.GetComponent<TextMeshProUGUI>();
        if (urlText != null) urlText.text = url;

        var btn = go.GetComponent<Button>();
        if (btn != null)
        {
            btn.onClick.AddListener(() =>
            {
                hub.ConnectTo(url);
                Append($"[Client] 接続中: {url}");
                ClearHostList();
            });
        }

        hostListItems[url] = go;
    }

    void Append(string message)
    {
        logText.text += message + "\n";
        if (logText.text.Length > 3000)
            logText.text = logText.text.Substring(logText.text.Length - 2000);
    }

    void ClearLog() => logText.text = "";

    void ClearHostList()
    {
        foreach (Transform child in hostListRootTrans)
            Destroy(child.gameObject);

        hostListItems.Clear();
        connectedDeviceItems.Clear();
        messageBuffers.Clear();
        deviceDropdown.ClearOptions();
    }
}

using System;
using System.Threading;
using System.Collections.Concurrent;
using UnityEngine;

public static class NetworkThread
{
    static readonly ConcurrentQueue<Action> mainQ = new();
    public static void Run(ThreadStart work) =>
        new Thread(work) { IsBackground = true }.Start();

    /* すぐ or delay */
    public static void QueueOnMain(Action a) => mainQ.Enqueue(a);
    public static void QueueOnMain(float delaySec, Action a) =>
        Run(() => { Thread.Sleep((int)(delaySec*1000)); mainQ.Enqueue(a); });

    /* MonoBehaviour 用 Update 呼び出し */
    public static void FlushMainQueue()
    {
        while (mainQ.TryDequeue(out var a)) a?.Invoke();
    }
}
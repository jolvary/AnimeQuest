using System;
using System.Collections.Generic;

public static class DozzleLogger
{
    private const int MaxPendingLogs = 100;
    private static readonly List<ClientLogEvent> PendingLogs = new List<ClientLogEvent>();

    public static void Action(string action, string details = null)
    {
        EnqueueOrSend(new ClientLogEvent
        {
            level = "action",
            action = action,
            details = details,
            timestamp = DateTime.UtcNow.ToString("O"),
        });
    }

    public static void Error(string action, Exception ex)
    {
        Error(action, ex?.Message);
    }

    public static void Error(string action, string message)
    {
        EnqueueOrSend(new ClientLogEvent
        {
            level = "error",
            action = action,
            message = message,
            timestamp = DateTime.UtcNow.ToString("O"),
        });
    }

    public static void FlushPending()
    {
        if (ApiClient.Instance == null || PendingLogs.Count == 0)
        {
            return;
        }

        var logs = PendingLogs.ToArray();
        PendingLogs.Clear();

        foreach (var logEvent in logs)
        {
            Send(logEvent);
        }
    }

    private static void EnqueueOrSend(ClientLogEvent logEvent)
    {
        if (ApiClient.Instance == null)
        {
            if (PendingLogs.Count >= MaxPendingLogs)
            {
                PendingLogs.RemoveAt(0);
            }

            PendingLogs.Add(logEvent);
            return;
        }

        Send(logEvent);
    }

    private static async void Send(ClientLogEvent logEvent)
    {
        try
        {
            await ApiClient.Instance.PostClientLog(
                logEvent.level,
                logEvent.action,
                logEvent.details,
                logEvent.message,
                logEvent.timestamp
            );
        }
        catch
        {
            // Keep gameplay/UI flows independent from external log delivery.
        }
    }

    private class ClientLogEvent
    {
        public string level;
        public string action;
        public string details;
        public string message;
        public string timestamp;
    }
}

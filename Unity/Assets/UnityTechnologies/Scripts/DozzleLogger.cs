using System;
using System.Collections.Generic;
using UnityEngine;

public static class DozzleLogger
{
    private const int MaxPendingLogs = 100;
    private const string NakamaClientLogRpc = "client_logs";
    private static readonly List<ClientLogEvent> PendingLogs = new List<ClientLogEvent>();
    private static bool _isFlushing;

    public static event Action<string, string> ErrorReported;

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

        if (IsUserFacingError(action))
        {
            NotifyError(action, message);
        }
    }

    public static void FlushPending()
    {
        if (!CanSendToNakama() || PendingLogs.Count == 0 || _isFlushing)
        {
            return;
        }

        _isFlushing = true;
        var logs = PendingLogs.ToArray();
        PendingLogs.Clear();

        foreach (var logEvent in logs)
        {
            Send(logEvent);
        }
        _isFlushing = false;
    }

    private static void EnqueueOrSend(ClientLogEvent logEvent)
    {
        if (!CanSendToNakama())
        {
            if (PendingLogs.Count >= MaxPendingLogs)
            {
                PendingLogs.RemoveAt(0);
            }

            PendingLogs.Add(logEvent);
            return;
        }

        FlushPending();
        Send(logEvent);
    }

    private static async void Send(ClientLogEvent logEvent)
    {
        try
        {
            var auth = NakamaAuthManager.Instance;
            if (auth == null || auth.Client == null || auth.Session == null || auth.Session.IsExpired)
            {
                return;
            }

            logEvent.platform = Application.platform.ToString();
            logEvent.unityVersion = Application.unityVersion;
            await auth.Client.RpcAsync(auth.Session, NakamaClientLogRpc, JsonUtility.ToJson(logEvent));
        }
        catch
        {
            // Keep gameplay/UI flows independent from external log delivery.
        }
    }

    private static bool CanSendToNakama()
    {
        var auth = NakamaAuthManager.Instance;
        return auth != null && auth.Client != null && auth.Session != null && !auth.Session.IsExpired;
    }

    private static bool IsUserFacingError(string action)
    {
        if (string.IsNullOrWhiteSpace(action)) return false;

        string normalized = action.Trim().ToLowerInvariant();
        return normalized.Contains("login") ||
               normalized.Contains("register") ||
               normalized.Contains("chat") ||
               normalized.Contains("session") ||
               normalized.Contains("logout") ||
               normalized.Contains("incognito") ||
               normalized.Contains("mal link") ||
               normalized.Contains("mal import") ||
               normalized.Contains("myanimelist");
    }

    private static void NotifyError(string action, string message)
    {
        try
        {
            ErrorReported?.Invoke(action, message);
        }
        catch
        {
            // Error UI must never break the original flow.
        }
    }

    private class ClientLogEvent
    {
        public string level;
        public string action;
        public string details;
        public string message;
        public string timestamp;
        public string platform;
        public string unityVersion;
    }
}

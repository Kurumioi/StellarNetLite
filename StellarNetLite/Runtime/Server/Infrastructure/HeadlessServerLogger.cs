using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using StellarNet.Lite.Runtime;
using StellarNet.Lite.Server.Core;
using StellarNet.Lite.Server.Modules;
using StellarNet.Lite.Shared.Core;
using StellarNet.Lite.Shared.Infrastructure;
using StellarNet.Lite.Shared.Protocol;
using UnityEngine;

namespace StellarNet.Lite.Server.Infrastructure
{
    /// <summary>
    /// Headless server console and log persistence component.
    /// Provides headless console commands, async log writing and retained log search.
    /// </summary>
    [DisallowMultipleComponent]
    public class HeadlessServerLogger : MonoBehaviour
    {
        private const int MaxCachedLogEntries = 10000;
        private static readonly TimeSpan LogRetentionWindow = TimeSpan.FromDays(30);
        private static readonly TimeSpan PersistenceShutdownWaitSlice = TimeSpan.FromSeconds(5);

        private sealed class LogRecord
        {
            public string TimestampText;
            public string Level;
            public string Summary;
            public string SearchText;
            public string CombinedText;
        }

        private string _logDirectoryPath;
        private string _logFilePath;
        private StreamWriter _logWriter;
        private readonly ConcurrentQueue<LogRecord> _logQueue = new ConcurrentQueue<LogRecord>();
        private readonly ConcurrentQueue<LogRecord> _recentLogQueue = new ConcurrentQueue<LogRecord>();
        private readonly ConcurrentQueue<string> _commandQueue = new ConcurrentQueue<string>();
        private int _recentLogCount;
        private bool _isRunning;
        private bool _isShutdownInProgress;
        private Thread _logThread;
        private DateTime _startupTimeUtc;
        private StellarNetAppManager _appManager;
        private Dictionary<int, RoomComponentMeta> _roomComponentMetaMap;

        private void Awake()
        {
            bool isHeadless = SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null || Application.isBatchMode;
            _startupTimeUtc = DateTime.UtcNow;
            _appManager = GetComponent<StellarNetAppManager>() ?? FindObjectOfType<StellarNetAppManager>();

            InitializeLogFile();
            CleanupExpiredLogs();
            _roomComponentMetaMap = BuildComponentMetaMap();
            Application.logMessageReceivedThreaded += HandleLogMessage;

            _isRunning = true;
            _logThread = new Thread(LogWriteLoop)
            {
                IsBackground = true,
                Name = "HeadlessServerLogger"
            };
            _logThread.Start();

            if (isHeadless)
            {
                TryEnableUtf8Console();
                PrintConsoleBanner();
                NetLogger.LogInfo("HeadlessServerLogger",
                    "Headless mode detected. Standard input command listener started. Type 'help' for commands.");
                Task.Run(ConsoleReadLoop);
            }
        }

        private void InitializeLogFile()
        {
            _logDirectoryPath = Path.Combine(Application.dataPath, "../ServerLogs").Replace("\\", "/");
            if (!Directory.Exists(_logDirectoryPath))
            {
                Directory.CreateDirectory(_logDirectoryPath);
            }

            string fileName = $"ServerLog_{DateTime.Now:yyyyMMdd_HHmmss}.txt";
            _logFilePath = Path.Combine(_logDirectoryPath, fileName).Replace("\\", "/");

            try
            {
                _logWriter = new StreamWriter(_logFilePath, true, Encoding.UTF8) { AutoFlush = true };
            }
            catch (Exception ex)
            {
                Debug.LogError($"[HeadlessServerLogger] Failed to create log file: {ex.Message}");
            }
        }

        private void HandleLogMessage(string logString, string stackTrace, LogType type)
        {
            if (!IsTrackedErrorLogType(type))
            {
                return;
            }

            LogRecord record = CreateLogRecord(logString, stackTrace, type);
            _logQueue.Enqueue(record);
            CacheRecentLog(record);
        }

        private void LogWriteLoop()
        {
            while (_isRunning || !_logQueue.IsEmpty)
            {
                if (_logQueue.TryDequeue(out LogRecord logRecord))
                {
                    _logWriter?.WriteLine(logRecord.CombinedText);
                }
                else
                {
                    Thread.Sleep(10);
                }
            }

            while (_logQueue.TryDequeue(out LogRecord logRecord))
            {
                _logWriter?.WriteLine(logRecord.CombinedText);
            }

            _logWriter?.Dispose();
        }

        private async Task ConsoleReadLoop()
        {
            while (_isRunning)
            {
                try
                {
                    string input = await Task.Run(Console.ReadLine);
                    if (input == null)
                    {
                        await Task.Delay(50);
                        continue;
                    }

                    if (!string.IsNullOrWhiteSpace(input))
                    {
                        _commandQueue.Enqueue(input.Trim());
                    }
                }
                catch (Exception)
                {
                    break;
                }
            }
        }

        private void Update()
        {
            while (_commandQueue.TryDequeue(out string cmd))
            {
                ExecuteCommand(cmd);
            }
        }

        private LogRecord CreateLogRecord(string logString, string stackTrace, LogType type)
        {
            string timestampText = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            string level = type.ToString().ToUpperInvariant();
            string summary = BuildSingleLineSummary(logString);
            string safeLog = string.IsNullOrWhiteSpace(logString) ? "-" : logString.Trim();
            string safeStackTrace = string.IsNullOrWhiteSpace(stackTrace) ? string.Empty : stackTrace.Trim();

            var builder = new StringBuilder();
            builder.Append('[').Append(timestampText).Append("][").Append(level).Append("] ").Append(safeLog);

            if (!string.IsNullOrWhiteSpace(safeStackTrace))
            {
                builder.AppendLine();
                builder.Append("StackTrace: ").Append(safeStackTrace);
            }

            string combinedText = builder.ToString().Replace("\r\n", "\n").Replace('\r', '\n');
            string searchText = (safeLog + "\n" + safeStackTrace).ToLowerInvariant();

            return new LogRecord
            {
                TimestampText = timestampText,
                Level = level,
                Summary = summary,
                SearchText = searchText,
                CombinedText = combinedText
            };
        }

        private void CacheRecentLog(LogRecord record)
        {
            if (record == null)
            {
                return;
            }

            _recentLogQueue.Enqueue(record);
            int currentCount = Interlocked.Increment(ref _recentLogCount);
            while (currentCount > MaxCachedLogEntries)
            {
                if (!_recentLogQueue.TryDequeue(out _))
                {
                    break;
                }

                currentCount = Interlocked.Decrement(ref _recentLogCount);
            }
        }

        private int CleanupExpiredLogs()
        {
            if (string.IsNullOrWhiteSpace(_logDirectoryPath) || !Directory.Exists(_logDirectoryPath))
            {
                return 0;
            }

            int deletedCount = 0;
            DateTime expireBeforeUtc = DateTime.UtcNow - LogRetentionWindow;

            try
            {
                DirectoryInfo directoryInfo = new DirectoryInfo(_logDirectoryPath);
                FileInfo[] files = directoryInfo.GetFiles("ServerLog_*.txt", SearchOption.TopDirectoryOnly);
                for (int i = 0; i < files.Length; i++)
                {
                    FileInfo file = files[i];
                    if (file == null || file.LastWriteTimeUtc >= expireBeforeUtc)
                    {
                        continue;
                    }

                    try
                    {
                        file.Delete();
                        deletedCount++;
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[HeadlessServerLogger] Failed to delete expired log '{file.FullName}': {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[HeadlessServerLogger] Failed to cleanup expired logs: {ex.Message}");
            }

            return deletedCount;
        }

        private string BuildSingleLineSummary(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return "-";
            }

            string summary = Regex.Replace(message, "<.*?>", string.Empty);
            summary = summary.Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' ');
            summary = Regex.Replace(summary, @"\s+", " ").Trim();

            const int maxSummaryLength = 120;
            if (summary.Length > maxSummaryLength)
            {
                summary = summary.Substring(0, maxSummaryLength - 3) + "...";
            }

            return string.IsNullOrWhiteSpace(summary) ? "-" : summary;
        }

        private void TryEnableUtf8Console()
        {
            try
            {
                Console.InputEncoding = new UTF8Encoding(false);
                Console.OutputEncoding = new UTF8Encoding(false);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[HeadlessServerLogger] Failed to enable UTF-8 console: {ex.Message}");
            }
        }

        private void PrintConsoleBanner()
        {
            PrintLine("+------------------------------------------------------------------------------+");
            PrintLine("| StellarNet Lite Headless Server Console                                      |");
            PrintLine("| Type 'help' to view status, rooms, sessions, logs and persist commands.      |");
            PrintLine("+------------------------------------------------------------------------------+");
        }

        private bool IsTrackedErrorLogType(LogType type)
        {
            return type == LogType.Error || type == LogType.Assert || type == LogType.Exception;
        }

        private bool IsTrackedErrorLevel(string level)
        {
            return string.Equals(level, "ERROR", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(level, "ASSERT", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(level, "EXCEPTION", StringComparison.OrdinalIgnoreCase);
        }

        private IEnumerator ShutdownServerRoutine()
        {
            _isShutdownInProgress = true;
            _isRunning = false;

            PrintLine("Exit requested. Starting graceful server shutdown...");

            StellarNetAppManager manager = _appManager ?? GetComponent<StellarNetAppManager>() ?? FindObjectOfType<StellarNetAppManager>();
            _appManager = manager;
            ServerApp serverApp = manager != null ? manager.ServerApp : null;

            if (serverApp != null)
            {
                ServerPersistenceRuntime persistenceRuntime = serverApp.PersistenceRuntime;
                persistenceRuntime.BeginShutdown();
                PrintLine("Persistence runtime entered shutdown mode. New tracked tasks are blocked by default.");

                int joinPendingCount = persistenceRuntime.JoinOnShutdownPendingCount;
                if (joinPendingCount > 0)
                {
                    PrintLine($"Waiting for {joinPendingCount} tracked persistence task(s) before room cleanup...");
                    PrintPersistenceEntries(
                        persistenceRuntime.SnapshotPending()
                            .Where(info => info != null && info.JoinOnShutdown)
                            .ToArray());

                    while (true)
                    {
                        Task<PersistenceDrainResult> drainTask = persistenceRuntime.WaitForIdleAsync(PersistenceShutdownWaitSlice);
                        while (!drainTask.IsCompleted)
                        {
                            yield return null;
                        }

                        if (drainTask.IsFaulted)
                        {
                            Exception ex = drainTask.Exception != null ? drainTask.Exception.GetBaseException() : null;
                            PrintLine($"Persistence wait failed: {(ex != null ? ex.Message : "Unknown error")}. Shutdown will continue.");
                            break;
                        }

                        if (drainTask.IsCanceled)
                        {
                            PrintLine("Persistence wait was canceled unexpectedly. Shutdown will continue.");
                            break;
                        }

                        PersistenceDrainResult drainResult = drainTask.Result;
                        if (drainResult.Completed)
                        {
                            PrintLine("Tracked persistence tasks completed.");
                            break;
                        }

                        PrintLine($"Still waiting for {drainResult.PendingCount} tracked persistence task(s)...");
                        PrintPersistenceEntries(drainResult.Pending);
                    }
                }
                else
                {
                    PrintLine("No tracked persistence tasks require shutdown wait.");
                }

                Room[] rooms = serverApp.Rooms.Values.Where(room => room != null).ToArray();
                Session[] sessions = serverApp.Sessions.Values.Where(session => session != null).ToArray();
                int playingRoomCount = rooms.Count(room => room.State == RoomState.Playing);
                int onlineSessionCount = sessions.Count(session => session.IsOnline);

                PrintTable(
                    new[] { "Step", "Value" },
                    new List<string[]>
                    {
                        new[] { "Rooms", rooms.Length.ToString() },
                        new[] { "PlayingRooms", playingRoomCount.ToString() },
                        new[] { "Sessions", sessions.Length.ToString() },
                        new[] { "OnlineSessions", onlineSessionCount.ToString() }
                    });

                for (int i = 0; i < rooms.Length; i++)
                {
                    Room room = rooms[i];
                    if (room.State == RoomState.Playing)
                    {
                        room.EndGame("Server shutdown");
                    }
                }

                for (int i = 0; i < sessions.Length; i++)
                {
                    Session session = sessions[i];
                    if (session.IsOnline)
                    {
                        serverApp.SendMessageToSession(session, new S2C_KickOut { Reason = "Server shutting down." });
                    }
                }

                if (onlineSessionCount > 0)
                {
                    PrintLine($"Sent shutdown notice to {onlineSessionCount} online session(s).");
                    yield return new WaitForSecondsRealtime(0.5f);
                }

                PrintLine("Stopping transport and server runtime...");
                manager.StopServer();
                manager.StopClient();

                float timeoutAt = Time.realtimeSinceStartup + 5f;
                while (manager.ServerApp != null && Time.realtimeSinceStartup < timeoutAt)
                {
                    yield return null;
                }
            }
            else
            {
                PrintLine("Server runtime is not active. Exiting application directly.");
            }

            PrintLine("Server shutdown completed. Exiting process...");
            yield return null;

#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private void ExecuteCommand(string commandLine)
        {
            List<string> args = ParseCommandArguments(commandLine);
            if (args.Count == 0)
            {
                return;
            }

            string command = args[0].ToLowerInvariant();
            switch (command)
            {
                case "help":
                    PrintHelp();
                    return;

                case "status":
                    if (TryEnsureServerApp(out StellarNetAppManager statusManager, out ServerApp statusApp))
                    {
                        PrintStatusTable(statusManager, statusApp);
                    }
                    return;

                case "rooms":
                    if (TryEnsureServerApp(out _, out ServerApp roomsApp))
                    {
                        PrintRoomsTable(roomsApp);
                    }
                    return;

                case "room":
                    if (args.Count < 2)
                    {
                        PrintUsage("room <roomId>");
                        return;
                    }

                    if (TryEnsureServerApp(out _, out ServerApp roomApp))
                    {
                        PrintRoomDetail(roomApp, args[1]);
                    }
                    return;

                case "sessions":
                    if (TryEnsureServerApp(out _, out ServerApp sessionsApp))
                    {
                        PrintSessionsTable(sessionsApp);
                    }
                    return;

                case "session":
                    if (args.Count < 2)
                    {
                        PrintUsage("session <sessionId|accountId>");
                        return;
                    }

                    if (TryEnsureServerApp(out _, out ServerApp sessionApp))
                    {
                        PrintSessionDetail(sessionApp, args[1]);
                    }
                    return;

                case "kick":
                    if (args.Count < 2)
                    {
                        PrintUsage("kick <sessionId|accountId> [reason]");
                        return;
                    }

                    if (TryEnsureServerApp(out StellarNetAppManager kickManager, out ServerApp kickApp))
                    {
                        string reason = args.Count > 2 ? string.Join(" ", args.Skip(2).ToArray()) : "Kicked by headless server console.";
                        ExecuteKick(kickManager, kickApp, args[1], reason);
                    }
                    return;

                case "logs":
                    if (args.Count > 2)
                    {
                        PrintUsage("logs [count]");
                        return;
                    }

                    int logCount = 20;
                    if (args.Count == 2 && !ParsePositiveInt(args[1], out logCount))
                    {
                        PrintLine("Invalid count. Example: logs 30");
                        return;
                    }

                    PrintRecentLogs(logCount);
                    return;

                case "findlog":
                    if (args.Count < 2 || args.Count > 3)
                    {
                        PrintUsage("findlog <keyword> [limit]");
                        return;
                    }

                    int searchLimit = 20;
                    if (args.Count == 3 && !ParsePositiveInt(args[2], out searchLimit))
                    {
                        PrintLine("Invalid limit. Example: findlog timeout 20");
                        return;
                    }

                    SearchLogs(args[1], searchLimit);
                    return;

                case "logfiles":
                    PrintLogFiles();
                    return;

                case "persist":
                case "pending":
                    if (args.Count > 1)
                    {
                        PrintUsage("persist");
                        return;
                    }

                    if (TryEnsureServerApp(out _, out ServerApp persistenceApp))
                    {
                        PrintPersistenceStatus(persistenceApp);
                    }
                    return;

                case "gc":
                    if (TryEnsureServerApp(out StellarNetAppManager gcManager, out ServerApp gcApp))
                    {
                        gcApp.Tick();
                        PrintLine("Manual GC tick executed.");
                        PrintStatusTable(gcManager, gcApp);
                    }
                    return;

                case "exit":
                case "quit":
                    if (_isShutdownInProgress)
                    {
                        PrintLine("Shutdown already in progress.");
                        return;
                    }

                    StartCoroutine(ShutdownServerRoutine());
                    return;

                default:
                    PrintLine($"Unknown command: {command}. Type 'help' for the command list.");
                    return;
            }
        }

        private void PrintHelp()
        {
            var rows = new List<string[]>
            {
                new[] { "help", "Show available commands." },
                new[] { "status", "Show server runtime summary." },
                new[] { "rooms", "List all active rooms." },
                new[] { "room <roomId>", "Show detailed room information and member table." },
                new[] { "sessions", "List all sessions with online/offline state." },
                new[] { "session <sessionId|accountId>", "Show detailed session information." },
                new[] { "kick <sessionId|accountId> [reason]", "Disconnect and remove a session from server state." },
                new[] { "logs [count]", "Show recent in-memory error logs as a table." },
                new[] { "findlog <keyword> [limit]", "Search recent and retained error logs, then print details." },
                new[] { "logfiles", "List retained log files within the last 30 days." },
                new[] { "persist / pending", "Show tracked async persistence tasks and shutdown wait state." },
                new[] { "gc", "Trigger one immediate server GC tick." },
                new[] { "exit", "Wait tracked persistence tasks, cleanup rooms, then exit the process." }
            };

            PrintTable(new[] { "Command", "Description" }, rows);
        }

        private bool TryEnsureServerApp(out StellarNetAppManager manager, out ServerApp serverApp)
        {
            manager = _appManager;
            if (manager == null)
            {
                manager = GetComponent<StellarNetAppManager>() ?? FindObjectOfType<StellarNetAppManager>();
                _appManager = manager;
            }

            if (manager == null)
            {
                serverApp = null;
                PrintLine("Server runtime not found. StellarNetAppManager is missing.");
                return false;
            }

            serverApp = manager.ServerApp;
            if (serverApp == null)
            {
                PrintLine("ServerApp is not ready. Start the server first and retry.");
                return false;
            }

            return true;
        }

        private void ExecuteKick(StellarNetAppManager manager, ServerApp serverApp, string identifier, string reason)
        {
            if (!TryFindSession(serverApp, identifier, out Session targetSession))
            {
                return;
            }

            string roomId = targetSession.CurrentRoomId;
            Room room = string.IsNullOrEmpty(roomId) ? null : serverApp.GetRoom(roomId);
            if (room != null)
            {
                room.RemoveMember(targetSession);
                if (room.MemberCount == 0)
                {
                    serverApp.DestroyRoom(roomId);
                }
            }

            if (targetSession.IsOnline)
            {
                serverApp.SendMessageToSession(targetSession, new S2C_KickOut { Reason = reason });
                manager.Transport?.DisconnectClient(targetSession.ConnectionId);
            }

            serverApp.RemoveSession(targetSession.SessionId);
            ServerLobbyModule.BroadcastOnlinePlayerList(serverApp);

            PrintTable(
                new[] { "Action", "SessionId", "AccountId", "RoomId", "Online", "Reason" },
                new List<string[]>
                {
                    new[]
                    {
                        "Kicked",
                        targetSession.SessionId,
                        string.IsNullOrEmpty(targetSession.AccountId) ? "-" : targetSession.AccountId,
                        string.IsNullOrEmpty(roomId) ? "-" : roomId,
                        targetSession.IsOnline ? "Yes" : "No",
                        string.IsNullOrWhiteSpace(reason) ? "-" : reason
                    }
                });
        }

        private void PrintStatusTable(StellarNetAppManager manager, ServerApp serverApp)
        {
            Session[] sessions = serverApp.Sessions.Values.Where(session => session != null).ToArray();
            int onlineCount = sessions.Count(session => session.IsOnline);
            int offlineCount = sessions.Length - onlineCount;
            TimeSpan uptime = DateTime.UtcNow - _startupTimeUtc;
            FileInfo[] retainedFiles = GetRetainedLogFiles();
            ServerPersistenceRuntime persistenceRuntime = serverApp.PersistenceRuntime;

            var rows = new List<string[]>
            {
                new[] { "StartedUtc", _startupTimeUtc.ToString("yyyy-MM-dd HH:mm:ss") },
                new[] { "Uptime", uptime.ToString(@"dd\.hh\:mm\:ss") },
                new[] { "NowLocal", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") },
                new[] { "Transport", manager != null && manager.Transport != null ? manager.Transport.GetType().Name : "-" },
                new[] { "Port", serverApp.Config != null ? serverApp.Config.Port.ToString() : "-" },
                new[] { "TickRate", serverApp.Config != null ? serverApp.Config.TickRate.ToString() : "-" },
                new[] { "Rooms", serverApp.Rooms.Count.ToString() },
                new[] { "Sessions", sessions.Length.ToString() },
                new[] { "Online", onlineCount.ToString() },
                new[] { "Offline", offlineCount.ToString() },
                new[] { "EmptyRoomTimeoutMin", serverApp.Config != null ? serverApp.Config.EmptyRoomTimeoutMinutes.ToString() : "-" },
                new[] { "OfflineLobbyTimeoutMin", serverApp.Config != null ? serverApp.Config.OfflineTimeoutLobbyMinutes.ToString() : "-" },
                new[] { "OfflineRoomTimeoutMin", serverApp.Config != null ? serverApp.Config.OfflineTimeoutRoomMinutes.ToString() : "-" },
                new[] { "PersistencePending", persistenceRuntime.PendingCount.ToString() },
                new[] { "PersistenceJoinOnShutdown", persistenceRuntime.JoinOnShutdownPendingCount.ToString() },
                new[] { "PersistenceShutdownMode", persistenceRuntime.IsShuttingDown ? "Yes" : "No" },
                new[] { "CurrentLogFile", string.IsNullOrEmpty(_logFilePath) ? "-" : _logFilePath },
                new[] { "RetainedLogFiles", retainedFiles.Length.ToString() },
                new[] { "RecentLogCache", $"{Volatile.Read(ref _recentLogCount)}/{MaxCachedLogEntries}" }
            };

            PrintTable(new[] { "Metric", "Value" }, rows);
        }

        private void PrintPersistenceStatus(ServerApp serverApp)
        {
            ServerPersistenceRuntime persistenceRuntime = serverApp.PersistenceRuntime;
            var summaryRows = new List<string[]>
            {
                new[] { "ShutdownMode", persistenceRuntime.IsShuttingDown ? "Yes" : "No" },
                new[] { "Pending", persistenceRuntime.PendingCount.ToString() },
                new[] { "JoinOnShutdownPending", persistenceRuntime.JoinOnShutdownPendingCount.ToString() }
            };

            PrintTable(new[] { "Metric", "Value" }, summaryRows);
            PrintPersistenceEntries(persistenceRuntime.SnapshotPending());
        }

        private void PrintPersistenceEntries(IReadOnlyList<PersistencePendingInfo> pendingInfos)
        {
            var rows = new List<string[]>();
            IReadOnlyList<PersistencePendingInfo> safePendingInfos = pendingInfos ?? Array.Empty<PersistencePendingInfo>();
            for (int i = 0; i < safePendingInfos.Count; i++)
            {
                PersistencePendingInfo info = safePendingInfos[i];
                if (info == null)
                {
                    continue;
                }

                string taskId = info.Id.ToString("N");
                rows.Add(new[]
                {
                    taskId.Substring(0, Math.Min(taskId.Length, 8)),
                    info.Owner,
                    info.Operation,
                    info.JoinOnShutdown ? "Yes" : "No",
                    info.StartUtc.ToString("yyyy-MM-dd HH:mm:ss"),
                    info.Elapsed.TotalSeconds.ToString("F1")
                });
            }

            if (rows.Count == 0)
            {
                rows.Add(new[] { "-", "-", "-", "-", "-", "-" });
            }

            PrintTable(new[] { "TaskId", "Owner", "Operation", "JoinOnShutdown", "StartUtc", "ElapsedSec" }, rows);
        }

        private void PrintRoomsTable(ServerApp serverApp)
        {
            Dictionary<int, RoomComponentMeta> componentMetaMap = BuildComponentMetaMap();
            Room[] rooms = serverApp.Rooms.Values.Where(room => room != null).OrderBy(room => room.RoomId).ToArray();
            var rows = new List<string[]>();

            for (int i = 0; i < rooms.Length; i++)
            {
                Room room = rooms[i];
                int onlineMembers = room.Members.Values.Count(session => session != null && session.IsOnline);
                string componentSummary = room.ComponentIds == null || room.ComponentIds.Length == 0
                    ? "-"
                    : string.Join(", ", room.ComponentIds.Select(id =>
                    {
                        if (componentMetaMap.TryGetValue(id, out RoomComponentMeta meta))
                        {
                            return meta.DisplayName;
                        }

                        return id.ToString();
                    }).ToArray());

                rows.Add(new[]
                {
                    room.RoomId,
                    string.IsNullOrEmpty(room.RoomName) ? "-" : room.RoomName,
                    room.State.ToString(),
                    $"{room.MemberCount}/{room.Config.MaxMembers}",
                    onlineMembers.ToString(),
                    room.CurrentTick.ToString(),
                    ((DateTime.UtcNow - room.CreateTime).TotalMinutes).ToString("F1"),
                    componentSummary
                });
            }

            if (rows.Count == 0)
            {
                rows.Add(new[] { "-", "-", "-", "0/0", "0", "0", "0.0", "-" });
            }

            PrintTable(new[] { "RoomId", "Name", "State", "Members", "Online", "Tick", "AgeMin", "Components" }, rows);
        }

        private void PrintRoomDetail(ServerApp serverApp, string roomId)
        {
            Room room = serverApp.GetRoom(roomId);
            if (room == null)
            {
                PrintLine($"Room not found: {roomId}");
                return;
            }

            Dictionary<int, RoomComponentMeta> componentMetaMap = BuildComponentMetaMap();
            var summaryRows = new List<string[]>
            {
                new[] { "RoomId", room.RoomId },
                new[] { "RoomName", string.IsNullOrEmpty(room.RoomName) ? "-" : room.RoomName },
                new[] { "State", room.State.ToString() },
                new[] { "Members", $"{room.MemberCount}/{room.Config.MaxMembers}" },
                new[] { "IsPrivate", room.Config.IsPrivate ? "Yes" : "No" },
                new[] { "CreateUtc", room.CreateTime.ToString("yyyy-MM-dd HH:mm:ss") },
                new[] { "EmptySinceUtc", room.EmptySince == DateTime.MaxValue ? "-" : room.EmptySince.ToString("yyyy-MM-dd HH:mm:ss") },
                new[] { "CurrentTick", room.CurrentTick.ToString() },
                new[] { "Recording", room.IsRecording ? "Yes" : "No" },
                new[] { "LastReplayId", string.IsNullOrEmpty(room.LastReplayId) ? "-" : room.LastReplayId },
                new[] { "CustomProps", room.Config.CustomProperties != null ? room.Config.CustomProperties.Count.ToString() : "0" }
            };
            PrintTable(new[] { "Field", "Value" }, summaryRows);

            var componentRows = new List<string[]>();
            int[] componentIds = room.ComponentIds ?? Array.Empty<int>();
            for (int i = 0; i < componentIds.Length; i++)
            {
                int componentId = componentIds[i];
                if (componentMetaMap.TryGetValue(componentId, out RoomComponentMeta meta))
                {
                    componentRows.Add(new[] { meta.Id.ToString(), meta.Name, meta.DisplayName });
                }
                else
                {
                    componentRows.Add(new[] { componentId.ToString(), "-", componentId.ToString() });
                }
            }

            if (componentRows.Count == 0)
            {
                componentRows.Add(new[] { "-", "-", "-" });
            }

            PrintTable(new[] { "ComponentId", "CodeName", "DisplayName" }, componentRows);

            var memberRows = new List<string[]>();
            Session[] members = room.Members.Values.Where(session => session != null).OrderBy(session => session.AccountId).ThenBy(session => session.SessionId).ToArray();
            float realtimeNow = Time.realtimeSinceStartup;
            for (int i = 0; i < members.Length; i++)
            {
                Session session = members[i];
                memberRows.Add(new[]
                {
                    session.SessionId,
                    string.IsNullOrEmpty(session.AccountId) ? "-" : session.AccountId,
                    session.ConnectionId.ToString(),
                    session.IsOnline ? "Yes" : "No",
                    session.IsRoomReady ? "Yes" : "No",
                    (realtimeNow - session.LastActiveRealtime).ToString("F1"),
                    (realtimeNow - session.LastRoomActiveRealtime).ToString("F1")
                });
            }

            if (memberRows.Count == 0)
            {
                memberRows.Add(new[] { "-", "-", "-", "-", "-", "-", "-" });
            }

            PrintTable(new[] { "SessionId", "AccountId", "ConnId", "Online", "Ready", "ActiveAgoSec", "RoomActiveAgoSec" }, memberRows);
        }

        private void PrintSessionsTable(ServerApp serverApp)
        {
            Session[] sessions = serverApp.Sessions.Values
                .Where(session => session != null)
                .OrderByDescending(session => session.IsOnline)
                .ThenBy(session => session.AccountId)
                .ThenBy(session => session.SessionId)
                .ToArray();

            var rows = new List<string[]>();
            float realtimeNow = Time.realtimeSinceStartup;
            for (int i = 0; i < sessions.Length; i++)
            {
                Session session = sessions[i];
                rows.Add(new[]
                {
                    session.SessionId,
                    string.IsNullOrEmpty(session.AccountId) ? "-" : session.AccountId,
                    session.ConnectionId.ToString(),
                    session.IsOnline ? "Yes" : "No",
                    string.IsNullOrEmpty(session.CurrentRoomId) ? "-" : session.CurrentRoomId,
                    session.IsRoomReady ? "Yes" : "No",
                    session.LastReceivedSeq.ToString(),
                    (realtimeNow - session.LastActiveRealtime).ToString("F1"),
                    session.IsOnline ? "-" : (DateTime.UtcNow - session.LastOfflineTime).TotalMinutes.ToString("F1")
                });
            }

            if (rows.Count == 0)
            {
                rows.Add(new[] { "-", "-", "-", "-", "-", "-", "-", "-", "-" });
            }

            PrintTable(new[] { "SessionId", "AccountId", "ConnId", "Online", "RoomId", "Ready", "LastSeq", "ActiveAgoSec", "OfflineMin" }, rows);
        }

        private void PrintSessionDetail(ServerApp serverApp, string identifier)
        {
            if (!TryFindSession(serverApp, identifier, out Session session))
            {
                return;
            }

            float realtimeNow = Time.realtimeSinceStartup;
            var rows = new List<string[]>
            {
                new[] { "SessionId", session.SessionId },
                new[] { "AccountId", string.IsNullOrEmpty(session.AccountId) ? "-" : session.AccountId },
                new[] { "ConnectionId", session.ConnectionId.ToString() },
                new[] { "Online", session.IsOnline ? "Yes" : "No" },
                new[] { "CurrentRoomId", string.IsNullOrEmpty(session.CurrentRoomId) ? "-" : session.CurrentRoomId },
                new[] { "AuthorizedRoomId", string.IsNullOrEmpty(session.AuthorizedRoomId) ? "-" : session.AuthorizedRoomId },
                new[] { "RoomReady", session.IsRoomReady ? "Yes" : "No" },
                new[] { "LastReceivedSeq", session.LastReceivedSeq.ToString() },
                new[] { "LastOfflineUtc", session.LastOfflineTime == default(DateTime) ? "-" : session.LastOfflineTime.ToString("yyyy-MM-dd HH:mm:ss") },
                new[] { "LastActiveAgoSec", (realtimeNow - session.LastActiveRealtime).ToString("F1") },
                new[] { "LastRoomActiveAgoSec", (realtimeNow - session.LastRoomActiveRealtime).ToString("F1") }
            };

            PrintTable(new[] { "Field", "Value" }, rows);
        }

        private bool TryFindSession(ServerApp serverApp, string identifier, out Session targetSession)
        {
            targetSession = null;
            if (string.IsNullOrWhiteSpace(identifier))
            {
                PrintLine("Session identifier cannot be empty.");
                return false;
            }

            if (serverApp.Sessions.TryGetValue(identifier, out Session exactSession) && exactSession != null)
            {
                targetSession = exactSession;
                return true;
            }

            Session accountSession = serverApp.GetSessionByAccountId(identifier);
            if (accountSession != null)
            {
                targetSession = accountSession;
                return true;
            }

            string keyword = identifier.Trim();
            Session[] fuzzyMatches = serverApp.Sessions.Values
                .Where(session => session != null &&
                                  ((session.SessionId != null && session.SessionId.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0) ||
                                   (session.AccountId != null && session.AccountId.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)))
                .Take(10)
                .ToArray();

            if (fuzzyMatches.Length == 1)
            {
                targetSession = fuzzyMatches[0];
                return true;
            }

            if (fuzzyMatches.Length > 1)
            {
                var rows = new List<string[]>();
                for (int i = 0; i < fuzzyMatches.Length; i++)
                {
                    Session session = fuzzyMatches[i];
                    rows.Add(new[]
                    {
                        session.SessionId,
                        string.IsNullOrEmpty(session.AccountId) ? "-" : session.AccountId,
                        session.IsOnline ? "Yes" : "No",
                        string.IsNullOrEmpty(session.CurrentRoomId) ? "-" : session.CurrentRoomId
                    });
                }

                PrintLine($"Multiple sessions matched '{identifier}'. Please use a more specific value.");
                PrintTable(new[] { "SessionId", "AccountId", "Online", "RoomId" }, rows);
                return false;
            }

            PrintLine($"Session not found: {identifier}");
            return false;
        }

        private Dictionary<int, RoomComponentMeta> BuildComponentMetaMap()
        {
            if (_roomComponentMetaMap != null && _roomComponentMetaMap.Count > 0)
            {
                return _roomComponentMetaMap;
            }

            var map = new Dictionary<int, RoomComponentMeta>();
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                Type[] types;
                try
                {
                    types = assemblies[i].GetTypes();
                }
                catch (ReflectionTypeLoadException ex)
                {
                    types = ex.Types.Where(type => type != null).ToArray();
                }
                catch
                {
                    continue;
                }

                for (int j = 0; j < types.Length; j++)
                {
                    Type type = types[j];
                    if (type == null || type.IsAbstract || !typeof(ServerRoomComponent).IsAssignableFrom(type))
                    {
                        continue;
                    }

                    RoomComponentAttribute attr = type.GetCustomAttribute<RoomComponentAttribute>();
                    if (attr == null || map.ContainsKey(attr.Id))
                    {
                        continue;
                    }

                    map[attr.Id] = new RoomComponentMeta
                    {
                        Id = attr.Id,
                        Name = string.IsNullOrEmpty(attr.Name) ? type.Name : attr.Name,
                        DisplayName = string.IsNullOrEmpty(attr.DisplayName) ? (string.IsNullOrEmpty(attr.Name) ? type.Name : attr.Name) : attr.DisplayName
                    };
                }
            }

            _roomComponentMetaMap = map;
            return _roomComponentMetaMap;
        }

        private void PrintRecentLogs(int count)
        {
            int safeCount = Math.Max(1, Math.Min(count, 200));
            LogRecord[] records = _recentLogQueue.ToArray();
            var rows = new List<string[]>();

            for (int i = records.Length - 1; i >= 0 && rows.Count < safeCount; i--)
            {
                LogRecord record = records[i];
                if (record == null)
                {
                    continue;
                }

                rows.Add(new[] { record.TimestampText, record.Level, record.Summary });
            }

            if (rows.Count == 0)
            {
                rows.Add(new[] { "-", "-", "No recent logs cached." });
            }

            PrintTable(new[] { "Time", "Level", "Summary" }, rows);
        }

        private void SearchLogs(string keyword, int limit)
        {
            string normalizedKeyword = string.IsNullOrWhiteSpace(keyword) ? string.Empty : keyword.Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(normalizedKeyword))
            {
                PrintLine("Keyword cannot be empty.");
                return;
            }

            int safeLimit = Math.Max(1, Math.Min(limit, 100));
            var resultRows = new List<string[]>();
            var detailBlocks = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            LogRecord[] recentRecords = _recentLogQueue.ToArray();
            for (int i = recentRecords.Length - 1; i >= 0 && resultRows.Count < safeLimit; i--)
            {
                LogRecord record = recentRecords[i];
                if (record == null || string.IsNullOrEmpty(record.SearchText) || !record.SearchText.Contains(normalizedKeyword))
                {
                    continue;
                }

                if (!seen.Add(record.CombinedText))
                {
                    continue;
                }

                resultRows.Add(new[] { record.TimestampText, record.Level, "memory", record.Summary });
                detailBlocks.Add(record.CombinedText);
            }

            FileInfo[] retainedFiles = GetRetainedLogFiles();
            for (int i = 0; i < retainedFiles.Length && resultRows.Count < safeLimit; i++)
            {
                FileInfo file = retainedFiles[i];
                foreach (LogRecord record in ReadPersistedLogRecords(file))
                {
                    if (resultRows.Count >= safeLimit)
                    {
                        break;
                    }

                    if (record == null || string.IsNullOrEmpty(record.SearchText) || !record.SearchText.Contains(normalizedKeyword))
                    {
                        continue;
                    }

                    if (!seen.Add(record.CombinedText))
                    {
                        continue;
                    }

                    resultRows.Add(new[] { record.TimestampText, record.Level, file.Name, record.Summary });
                    detailBlocks.Add(record.CombinedText);
                }
            }

            if (resultRows.Count == 0)
            {
                PrintLine($"No retained logs matched keyword: {keyword}");
                return;
            }

            PrintTable(new[] { "Time", "Level", "Source", "Summary" }, resultRows);

            for (int i = 0; i < detailBlocks.Count; i++)
            {
                PrintLine(new string('=', 78));
                PrintLine($"Match {i + 1}");
                PrintLine(new string('-', 78));
                PrintLine(detailBlocks[i]);
            }

            PrintLine(new string('=', 78));
        }

        private IEnumerable<LogRecord> ReadPersistedLogRecords(FileInfo file)
        {
            if (file == null || !file.Exists)
            {
                yield break;
            }

            string content;
            try
            {
                content = File.ReadAllText(file.FullName, Encoding.UTF8);
            }
            catch
            {
                content = null;
            }

            if (string.IsNullOrEmpty(content))
            {
                yield break;
            }

            MatchCollection matches = Regex.Matches(
                content,
                @"^\[\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3}\]\[[A-Z]+\]",
                RegexOptions.Multiline);

            if (matches.Count == 0)
            {
                yield break;
            }

            for (int i = matches.Count - 1; i >= 0; i--)
            {
                int start = matches[i].Index;
                int end = i == matches.Count - 1 ? content.Length : matches[i + 1].Index;
                string block = content.Substring(start, end - start).Trim();
                if (!string.IsNullOrWhiteSpace(block))
                {
                    yield return CreatePersistedLogRecord(block);
                }
            }
        }

        private LogRecord CreatePersistedLogRecord(string block)
        {
            string combinedText = string.IsNullOrWhiteSpace(block) ? "-" : block.Trim();
            string firstLine = combinedText;
            int newlineIndex = combinedText.IndexOf('\n');
            if (newlineIndex >= 0)
            {
                firstLine = combinedText.Substring(0, newlineIndex);
            }

            string timestampText = "-";
            string level = "-";
            string summary = BuildSingleLineSummary(firstLine);

            Match headerMatch = Regex.Match(firstLine, @"^\[(?<time>[^\]]+)\]\[(?<level>[^\]]+)\]\s*(?<message>.*)$");
            if (headerMatch.Success)
            {
                timestampText = headerMatch.Groups["time"].Value;
                level = headerMatch.Groups["level"].Value;
                summary = BuildSingleLineSummary(headerMatch.Groups["message"].Value);
            }

            if (!IsTrackedErrorLevel(level))
            {
                return null;
            }

            return new LogRecord
            {
                TimestampText = timestampText,
                Level = level,
                Summary = summary,
                SearchText = combinedText.ToLowerInvariant(),
                CombinedText = combinedText
            };
        }

        private void PrintLogFiles()
        {
            FileInfo[] files = GetRetainedLogFiles();
            var rows = new List<string[]>();

            for (int i = 0; i < files.Length; i++)
            {
                FileInfo file = files[i];
                rows.Add(new[]
                {
                    file.Name,
                    file.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss"),
                    (DateTime.Now - file.LastWriteTime).TotalDays.ToString("F1"),
                    (file.Length / 1024d).ToString("F1")
                });
            }

            if (rows.Count == 0)
            {
                rows.Add(new[] { "-", "-", "-", "-" });
            }

            PrintTable(new[] { "File", "UpdatedLocal", "AgeDays", "SizeKB" }, rows);
        }

        private FileInfo[] GetRetainedLogFiles()
        {
            if (string.IsNullOrWhiteSpace(_logDirectoryPath) || !Directory.Exists(_logDirectoryPath))
            {
                return Array.Empty<FileInfo>();
            }

            DateTime expireBeforeUtc = DateTime.UtcNow - LogRetentionWindow;

            try
            {
                return new DirectoryInfo(_logDirectoryPath)
                    .GetFiles("ServerLog_*.txt", SearchOption.TopDirectoryOnly)
                    .Where(file => file != null && file.Exists && file.LastWriteTimeUtc >= expireBeforeUtc)
                    .OrderByDescending(file => file.LastWriteTimeUtc)
                    .ToArray();
            }
            catch
            {
                return Array.Empty<FileInfo>();
            }
        }

        private List<string> ParseCommandArguments(string commandLine)
        {
            var args = new List<string>();
            if (string.IsNullOrWhiteSpace(commandLine))
            {
                return args;
            }

            MatchCollection matches = Regex.Matches(commandLine, "\"([^\"]*)\"|(\\S+)");
            for (int i = 0; i < matches.Count; i++)
            {
                Match match = matches[i];
                if (match.Groups[1].Success)
                {
                    args.Add(match.Groups[1].Value);
                }
                else if (match.Groups[2].Success)
                {
                    args.Add(match.Groups[2].Value);
                }
            }

            return args;
        }

        private bool ParsePositiveInt(string text, out int value)
        {
            if (int.TryParse(text, out value) && value > 0)
            {
                return true;
            }

            value = 0;
            return false;
        }

        private void PrintUsage(string usage)
        {
            PrintLine($"Usage: {usage}");
        }

        private void PrintLine(string text)
        {
            string line = text ?? string.Empty;
            try
            {
                Console.WriteLine(line);
            }
            catch
            {
                Debug.Log(line);
            }
        }

        private void PrintTable(string[] headers, List<string[]> rows)
        {
            if (headers == null || headers.Length == 0)
            {
                return;
            }

            int columnCount = headers.Length;
            int[] widths = new int[columnCount];
            for (int i = 0; i < columnCount; i++)
            {
                widths[i] = Math.Min(60, Math.Max(4, GetDisplayWidth(headers[i])));
            }

            for (int rowIndex = 0; rowIndex < rows.Count; rowIndex++)
            {
                string[] row = rows[rowIndex];
                for (int colIndex = 0; colIndex < columnCount; colIndex++)
                {
                    string cell = row != null && colIndex < row.Length ? row[colIndex] : string.Empty;
                    widths[colIndex] = Math.Min(60, Math.Max(widths[colIndex], GetDisplayWidth(cell)));
                }
            }

            var borderBuilder = new StringBuilder();
            borderBuilder.Append('+');
            for (int i = 0; i < columnCount; i++)
            {
                borderBuilder.Append(new string('-', widths[i] + 2)).Append('+');
            }

            string border = borderBuilder.ToString();
            PrintLine(border);
            PrintLine(BuildTableRow(headers, widths));
            PrintLine(border);

            for (int i = 0; i < rows.Count; i++)
            {
                PrintLine(BuildTableRow(rows[i], widths));
            }

            PrintLine(border);
        }

        private string BuildTableRow(IList<string> cells, int[] widths)
        {
            var builder = new StringBuilder();
            builder.Append('|');

            for (int i = 0; i < widths.Length; i++)
            {
                string cell = cells != null && i < cells.Count ? cells[i] : string.Empty;
                builder.Append(' ')
                    .Append(NormalizeCell(cell, widths[i]))
                    .Append(' ')
                    .Append('|');
            }

            return builder.ToString();
        }

        private string NormalizeCell(string value, int width)
        {
            string cell = string.IsNullOrWhiteSpace(value) ? "-" : value;
            cell = cell.Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ');
            cell = Regex.Replace(cell, @"\s+", " ").Trim();
            if (string.IsNullOrEmpty(cell))
            {
                cell = "-";
            }

            int cellWidth = GetDisplayWidth(cell);
            bool isTruncated = cellWidth > width;
            int targetWidth = isTruncated && width >= 3 ? width - 3 : width;

            var builder = new StringBuilder();
            int currentWidth = 0;
            for (int i = 0; i < cell.Length; i++)
            {
                char ch = cell[i];
                int charWidth = ch <= 255 ? 1 : 2;
                if (currentWidth + charWidth > targetWidth)
                {
                    break;
                }

                builder.Append(ch);
                currentWidth += charWidth;
            }

            if (isTruncated && width >= 3)
            {
                builder.Append("...");
                currentWidth += 3;
            }

            if (currentWidth < width)
            {
                builder.Append(' ', width - currentWidth);
            }

            return builder.ToString();
        }

        private int GetDisplayWidth(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return 1;
            }

            int width = 0;
            for (int i = 0; i < value.Length; i++)
            {
                char ch = value[i];
                if (ch == '\r' || ch == '\n')
                {
                    continue;
                }

                width += ch <= 255 ? 1 : 2;
            }

            return Math.Max(width, 1);
        }

        private void OnDestroy()
        {
            _isRunning = false;
            Application.logMessageReceivedThreaded -= HandleLogMessage;

            if (_logThread != null && _logThread.IsAlive)
            {
                _logThread.Join(2000);
            }
        }
    }
}

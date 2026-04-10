using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using StellarNet.Lite.Runtime;
using StellarNet.Lite.Shared.Infrastructure;
using UnityEngine;

namespace StellarNet.Lite.Server.Infrastructure
{
    /// <summary>
    /// Headless server console and log persistence component.
    /// Provides command-line interaction and async log writing for Linux/Windows headless modes.
    /// </summary>
    [DisallowMultipleComponent]
    public class HeadlessServerLogger : MonoBehaviour
    {
        private string _logFilePath;
        private StreamWriter _logWriter;
        private readonly ConcurrentQueue<string> _logQueue = new ConcurrentQueue<string>();
        private readonly ConcurrentQueue<string> _commandQueue = new ConcurrentQueue<string>();

        private bool _isRunning;
        private Thread _logThread;

        private void Awake()
        {
            bool isHeadless = SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null || Application.isBatchMode;

            InitializeLogFile();
            Application.logMessageReceivedThreaded += HandleLogMessage;

            _isRunning = true;
            _logThread = new Thread(LogWriteLoop) { IsBackground = true };
            _logThread.Start();

            if (isHeadless)
            {
                NetLogger.LogInfo("HeadlessServerLogger",
                    "Headless mode detected. Standard input command listener started. Type 'help' for commands.");
                Task.Run(ConsoleReadLoop);
            }
        }

        private void InitializeLogFile()
        {
            string logDir = Path.Combine(Application.dataPath, "../ServerLogs").Replace("\\", "/");
            if (!Directory.Exists(logDir))
            {
                Directory.CreateDirectory(logDir);
            }

            string fileName = $"ServerLog_{DateTime.Now:yyyyMMdd_HHmmss}.txt";
            _logFilePath = Path.Combine(logDir, fileName);

            try
            {
                _logWriter = new StreamWriter(_logFilePath, true, System.Text.Encoding.UTF8) { AutoFlush = true };
                _logWriter.WriteLine($"=== StellarNet Server Log Started at {DateTime.Now} ===");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[HeadlessServerLogger] Failed to create log file: {ex.Message}");
            }
        }

        private void HandleLogMessage(string logString, string stackTrace, LogType type)
        {
            string timeStr = DateTime.Now.ToString("HH:mm:ss.fff");
            string logLine = $"[{timeStr}][{type}] {logString}";

            if (type == LogType.Error || type == LogType.Exception)
            {
                logLine += $"\nStackTrace:\n{stackTrace}";
            }

            _logQueue.Enqueue(logLine);
        }

        private void LogWriteLoop()
        {
            while (_isRunning)
            {
                if (_logQueue.TryDequeue(out string logLine))
                {
                    _logWriter?.WriteLine(logLine);
                }
                else
                {
                    Thread.Sleep(10);
                }
            }

            while (_logQueue.TryDequeue(out string logLine))
            {
                _logWriter?.WriteLine(logLine);
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

        private void ExecuteCommand(string cmd)
        {
            var appManager = FindObjectOfType<StellarNetAppManager>();
            var serverApp = appManager?.ServerApp;

            if (serverApp == null)
            {
                NetLogger.LogWarning("Console", "ServerApp is not running. Cannot execute business commands.");
                return;
            }

            string[] args = cmd.Split(' ');
            string mainCmd = args[0].ToLower();

            switch (mainCmd)
            {
                case "help":
                    Console.WriteLine("Available commands: status, rooms, kick <accountId>, gc");
                    break;
                case "status":
                    Console.WriteLine($"[Status] Online Sessions: {serverApp.Sessions.Count} | Active Rooms: {serverApp.Rooms.Count}");
                    break;
                case "rooms":
                    Console.WriteLine("=== Room List ===");
                    foreach (var kvp in serverApp.Rooms)
                    {
                        Console.WriteLine($"- {kvp.Value.RoomId} [{kvp.Value.State}] ({kvp.Value.MemberCount} players)");
                    }

                    break;
                case "kick":
                    if (args.Length < 2)
                    {
                        Console.WriteLine("Usage: kick <accountId>");
                        break;
                    }

                    var session = serverApp.GetSessionByAccountId(args[1]);
                    if (session != null)
                    {
                        serverApp.SendMessageToSession(session, new Shared.Protocol.S2C_KickOut { Reason = "Kicked by administrator." });
                        serverApp.UnbindConnection(session);
                        Console.WriteLine($"Successfully kicked account: {args[1]}");
                    }
                    else
                    {
                        Console.WriteLine($"Account not found: {args[1]}");
                    }

                    break;
                case "gc":
                    GC.Collect();
                    Console.WriteLine("Forced Garbage Collection triggered.");
                    break;
                default:
                    Console.WriteLine($"Unknown command: {mainCmd}");
                    break;
            }
        }

        private void OnDestroy()
        {
            _isRunning = false;
            Application.logMessageReceivedThreaded -= HandleLogMessage;
        }
    }
}
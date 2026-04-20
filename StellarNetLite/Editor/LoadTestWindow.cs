#if UNITY_EDITOR
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace StellarNet.Lite.Editor
{
    /// <summary>
    /// Unity Editor 压测窗口。
    /// 用于在编辑器里直接启动独立压测工具。
    /// </summary>
    public sealed class LoadTestWindow : EditorWindow
    {
        private const string EditorPrefsPrefix = "StellarNetLite.LoadTest.";
        private const string DefaultHost = "127.0.0.1";
        private const int DefaultPort = 7777;
        private const int DefaultRoomCount = 1;
        private const int DefaultClientsPerRoom = 50;
        private const int DefaultConnectRate = 10;
        private const int DefaultDuration = 0;
        private const int DefaultMoveRate = 8;
        private const int DefaultLogInterval = 5;
        private const string DefaultRoomName = "LoadTestRoom";
        private const string DefaultAccountPrefix = "bot";
        private const string DefaultClientVersion = "0.0.1";

        private enum LoadTestTransport
        {
            Kcp = 0,
            Tcp = 1
        }

        private LoadTestTransport _transport = LoadTestTransport.Kcp;
        private string _host = DefaultHost;
        private int _port = DefaultPort;
        private int _roomCount = DefaultRoomCount;
        private int _clientsPerRoom = DefaultClientsPerRoom;
        private int _redundantClientsPerRoom = 0;
        private int _connectRate = DefaultConnectRate;
        private int _duration = DefaultDuration;
        private int _moveRate = DefaultMoveRate;
        private int _logInterval = DefaultLogInterval;
        private string _roomName = DefaultRoomName;
        private string _accountPrefix = DefaultAccountPrefix;
        private string _clientVersion = DefaultClientVersion;
        private string _runtimeCommandText = "status";
        private int _runtimeRoomDelta = 1;
        private int _runtimeRoomNumber = 1;
        private Vector2 _scrollPos;
        private bool _autoScroll = true;
        private bool _scrollToBottomRequested;
        private readonly StringBuilder _outputBuilder = new StringBuilder(8192);
        private readonly ConcurrentQueue<string> _pendingOutputLines = new ConcurrentQueue<string>();

        private Process _runningProcess;
        private GUIStyle _outputStyle;
        private bool _stylesInitialized;

        [MenuItem("StellarNetLite/压测工具 (Load Test)")]
        public static void Open()
        {
            LoadTestWindow window = GetWindow<LoadTestWindow>("压测工具");
            window.minSize = new Vector2(760f, 580f);
            window.Show();
        }

        private void OnEnable()
        {
            LoadPrefs();
            EditorApplication.update += OnEditorUpdate;
        }

        private void OnDisable()
        {
            SavePrefs();
            EditorApplication.update -= OnEditorUpdate;
        }

        private void OnDestroy()
        {
            if (_runningProcess != null && !_runningProcess.HasExited)
            {
                TryStopProcess();
            }
        }

        private void OnGUI()
        {
            InitializeStyles();

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("StellarNetLite 压测工具", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("支持配置多房间、每房多客户端，并直接在 Unity Editor 中启动独立压测程序。", MessageType.Info);

            DrawConfigFields();
            EditorGUILayout.Space(8f);
            DrawActionButtons();
            EditorGUILayout.Space(8f);
            DrawRuntimeInfo();
            EditorGUILayout.Space(8f);
            DrawRuntimeCommands();
            EditorGUILayout.Space(6f);
            DrawOutput();
        }

        private void InitializeStyles()
        {
            if (_stylesInitialized)
            {
                return;
            }

            _outputStyle = new GUIStyle(EditorStyles.textArea)
            {
                richText = false,
                wordWrap = false,
                fontSize = 11
            };

            _stylesInitialized = true;
        }

        private void DrawConfigFields()
        {
            EditorGUILayout.BeginVertical("box");
            _transport = (LoadTestTransport)EditorGUILayout.EnumPopup("传输层", _transport);
            _host = EditorGUILayout.TextField("Host", _host);
            _port = EditorGUILayout.IntField("Port", _port);
            _roomCount = EditorGUILayout.IntField("房间数", _roomCount);
            _clientsPerRoom = EditorGUILayout.IntField("每房客户端数", _clientsPerRoom);
            _redundantClientsPerRoom = EditorGUILayout.IntField("每房冗余成员数", _redundantClientsPerRoom);
            EditorGUILayout.LabelField("总机器人客户端数", Mathf.Max(0, _roomCount * _clientsPerRoom).ToString());
            EditorGUILayout.LabelField("每房最大成员数", Mathf.Max(0, _clientsPerRoom + _redundantClientsPerRoom).ToString());
            _connectRate = EditorGUILayout.IntField("建连速率/秒", _connectRate);
            _duration = EditorGUILayout.IntField("压测时长/秒 (0=直到手动停止)", _duration);
            _moveRate = EditorGUILayout.IntField("移动频率/秒", _moveRate);
            _logInterval = EditorGUILayout.IntField("日志间隔/秒", _logInterval);
            _roomName = EditorGUILayout.TextField("房间名前缀", _roomName);
            _accountPrefix = EditorGUILayout.TextField("账号前缀", _accountPrefix);
            _clientVersion = EditorGUILayout.TextField("客户端版本", _clientVersion);
            EditorGUILayout.EndVertical();
        }

        private void DrawActionButtons()
        {
            EditorGUILayout.BeginHorizontal();

            GUI.enabled = _runningProcess == null;
            if (GUILayout.Button("启动压测", GUILayout.Height(32f)))
            {
                StartLoadTest();
            }

            GUI.enabled = _runningProcess != null;
            if (GUILayout.Button("停止压测", GUILayout.Height(32f)))
            {
                TryStopProcess();
            }

            GUI.enabled = true;
            if (GUILayout.Button("打开工具目录", GUILayout.Height(32f)))
            {
                OpenToolFolder();
            }

            if (GUILayout.Button("清空输出", GUILayout.Height(32f)))
            {
                _outputBuilder.Clear();
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawRuntimeInfo()
        {
            EditorGUILayout.BeginVertical("box");

            string toolDirectory = GetLoadTestDirectory();
            string scriptPath = GetLoadTestScriptPath();
            EditorGUILayout.LabelField("工具目录", string.IsNullOrEmpty(toolDirectory) ? "-" : toolDirectory);
            EditorGUILayout.LabelField("启动脚本", string.IsNullOrEmpty(scriptPath) ? "-" : scriptPath);
            EditorGUILayout.LabelField("当前状态", _runningProcess == null ? "未运行" : $"运行中 (PID: {_runningProcess.Id})");

            if (_transport == LoadTestTransport.Kcp)
            {
                EditorGUILayout.HelpBox("推荐用于主链路压测。每个房间都会有一个房主机器人负责建房和开局。", MessageType.None);
            }
            else
            {
                EditorGUILayout.HelpBox("适合做 TCP 纯 C# transport 的对照压测。", MessageType.None);
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawRuntimeCommands()
        {
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Runtime Commands", EditorStyles.boldLabel);

            GUI.enabled = _runningProcess != null;

            EditorGUILayout.BeginHorizontal();
            _runtimeRoomDelta = EditorGUILayout.IntField("Room Delta", Mathf.Max(1, _runtimeRoomDelta));
            if (GUILayout.Button("Add Room", GUILayout.Height(24f)))
            {
                SendRuntimeCommand($"addroom {Mathf.Max(1, _runtimeRoomDelta)}");
            }

            if (GUILayout.Button("Remove Room", GUILayout.Height(24f)))
            {
                SendRuntimeCommand($"removeroom {Mathf.Max(1, _runtimeRoomDelta)}");
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            _runtimeRoomNumber = EditorGUILayout.IntField("Room Number", Mathf.Max(1, _runtimeRoomNumber));
            if (GUILayout.Button("End Room", GUILayout.Height(24f)))
            {
                SendRuntimeCommand($"endroom {Mathf.Max(1, _runtimeRoomNumber)}");
            }

            if (GUILayout.Button("Status", GUILayout.Height(24f)))
            {
                SendRuntimeCommand("status");
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            _runtimeCommandText = EditorGUILayout.TextField("Raw", _runtimeCommandText);
            if (GUILayout.Button("Send", GUILayout.Width(80f), GUILayout.Height(24f)))
            {
                SendRuntimeCommand(_runtimeCommandText);
            }

            EditorGUILayout.EndHorizontal();

            GUI.enabled = true;
            EditorGUILayout.EndVertical();
        }

        private void DrawOutput()
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("输出日志", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            _autoScroll = EditorGUILayout.ToggleLeft("自动滚动", _autoScroll, GUILayout.Width(80f));
            EditorGUILayout.EndHorizontal();

            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos, GUILayout.ExpandHeight(true));
            EditorGUILayout.SelectableLabel(
                _outputBuilder.Length > 0 ? _outputBuilder.ToString() : "暂无输出。",
                _outputStyle,
                GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();

            if (_autoScroll && _scrollToBottomRequested)
            {
                _scrollPos.y = float.MaxValue;
                _scrollToBottomRequested = false;
                Repaint();
            }
        }

        private void StartLoadTest()
        {
            string scriptPath = GetLoadTestScriptPath();
            if (string.IsNullOrEmpty(scriptPath) || !File.Exists(scriptPath))
            {
                EditorUtility.DisplayDialog("启动失败", $"未找到压测脚本：\n{scriptPath}", "确定");
                return;
            }

            if (!ValidateInputs())
            {
                return;
            }

            string powerShellPath = ResolvePowerShellExecutable();
            if (string.IsNullOrEmpty(powerShellPath))
            {
                EditorUtility.DisplayDialog("启动失败", "未找到 powershell.exe。", "确定");
                return;
            }

            string arguments =
                $"-ExecutionPolicy Bypass -File \"{scriptPath}\" " +
                $"-Transport {(_transport == LoadTestTransport.Kcp ? "kcp" : "tcp")} " +
                $"-ServerHost \"{_host}\" " +
                $"-Port {_port} " +
                $"-Rooms {_roomCount} " +
                $"-ClientsPerRoom {_clientsPerRoom} " +
                $"-RedundantClientsPerRoom {_redundantClientsPerRoom} " +
                $"-ConnectRate {_connectRate} " +
                $"-Duration {_duration} " +
                $"-MoveRate {_moveRate} " +
                $"-RoomName \"{EscapeArgument(_roomName)}\" " +
                $"-AccountPrefix \"{EscapeArgument(_accountPrefix)}\" " +
                $"-ClientVersion \"{EscapeArgument(_clientVersion)}\" " +
                $"-LogInterval {_logInterval}";

            var startInfo = new ProcessStartInfo
            {
                FileName = powerShellPath,
                Arguments = arguments,
                WorkingDirectory = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            try
            {
                _outputBuilder.Clear();
                AppendOutput($"[Editor] 启动压测: 传输层={_transport}, 地址={_host}:{_port}, 房间数={_roomCount}, 每房机器人={_clientsPerRoom}, 每房冗余={_redundantClientsPerRoom}, 总机器人={_roomCount * _clientsPerRoom}, 时长={(_duration > 0 ? _duration + "s" : "直到手动停止")}");

                _runningProcess = new Process
                {
                    StartInfo = startInfo,
                    EnableRaisingEvents = true
                };
                _runningProcess.OutputDataReceived += OnProcessOutput;
                _runningProcess.ErrorDataReceived += OnProcessError;
                _runningProcess.Exited += OnProcessExited;
                _runningProcess.Start();
                _runningProcess.BeginOutputReadLine();
                _runningProcess.BeginErrorReadLine();
            }
            catch (Exception ex)
            {
                _runningProcess = null;
                EditorUtility.DisplayDialog("启动失败", ex.Message, "确定");
            }
        }

        private void TryStopProcess()
        {
            if (_runningProcess == null)
            {
                return;
            }

            try
            {
                if (!_runningProcess.HasExited)
                {
                    AppendOutput("[Editor] 正在停止压测进程...");
                    KillProcessCompat(_runningProcess);
                }
            }
            catch (Exception ex)
            {
                AppendOutput($"[Editor] 停止失败: {ex.GetType().Name} - {ex.Message}");
            }
        }

        private static void KillProcessCompat(Process process)
        {
            if (process == null || process.HasExited)
            {
                return;
            }

            System.Reflection.MethodInfo killProcessTree = typeof(Process).GetMethod("Kill", new[] { typeof(bool) });
            if (killProcessTree != null)
            {
                killProcessTree.Invoke(process, new object[] { true });
                return;
            }

            process.Kill();
        }

        private void OnProcessOutput(object sender, DataReceivedEventArgs e)
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                AppendOutput(e.Data);
            }
        }

        private void OnProcessError(object sender, DataReceivedEventArgs e)
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                AppendOutput("[ERR] " + e.Data);
            }
        }

        private void OnProcessExited(object sender, EventArgs e)
        {
            AppendOutput("[Editor] 压测进程已退出。");
        }

        private void SendRuntimeCommand(string command)
        {
            if (_runningProcess == null || _runningProcess.HasExited || string.IsNullOrWhiteSpace(command))
            {
                return;
            }

            try
            {
                _runningProcess.StandardInput.WriteLine(command.Trim());
                _runningProcess.StandardInput.Flush();
                AppendOutput($"[Editor->LoadTest] {command.Trim()}");
            }
            catch (Exception ex)
            {
                AppendOutput($"[Editor] failed to send command: {ex.GetType().Name} - {ex.Message}");
            }
        }

        private void OnEditorUpdate()
        {
            FlushPendingOutput();

            if (_runningProcess != null && _runningProcess.HasExited)
            {
                CleanupProcess();
                Repaint();
            }
        }

        private void CleanupProcess()
        {
            if (_runningProcess == null)
            {
                return;
            }

            _runningProcess.OutputDataReceived -= OnProcessOutput;
            _runningProcess.ErrorDataReceived -= OnProcessError;
            _runningProcess.Exited -= OnProcessExited;
            _runningProcess.Dispose();
            _runningProcess = null;
        }

        private void AppendOutput(string line)
        {
            if (string.IsNullOrEmpty(line))
            {
                return;
            }

            _pendingOutputLines.Enqueue(line);
        }

        private void FlushPendingOutput()
        {
            bool changed = false;
            while (_pendingOutputLines.TryDequeue(out string line))
            {
                if (string.IsNullOrEmpty(line))
                {
                    continue;
                }

                changed = true;
                _outputBuilder.AppendLine(line);
            }

            if (!changed)
            {
                return;
            }

            if (_outputBuilder.Length > 64 * 1024)
            {
                _outputBuilder.Remove(0, _outputBuilder.Length - 48 * 1024);
            }

            _scrollToBottomRequested = true;
            Repaint();
        }

        private bool ValidateInputs()
        {
            if (string.IsNullOrWhiteSpace(_host))
            {
                EditorUtility.DisplayDialog("参数错误", "Host 不能为空。", "确定");
                return false;
            }

            if (_port <= 0 || _roomCount <= 0 || _clientsPerRoom <= 0 || _redundantClientsPerRoom < 0 || _connectRate <= 0 || _duration < 0 || _moveRate <= 0 || _logInterval <= 0)
            {
                EditorUtility.DisplayDialog("参数错误", "端口、房间数、每房客户端数、建连速率、移动频率、日志间隔必须大于 0；冗余成员数与压测时长必须大于等于 0。", "确定");
                return false;
            }

            return true;
        }

        private void OpenToolFolder()
        {
            string path = GetLoadTestDirectory();
            if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
            {
                EditorUtility.DisplayDialog("打开失败", "未找到压测工具目录。", "确定");
                return;
            }

            EditorUtility.RevealInFinder(path);
        }

        private static string GetLoadTestDirectory()
        {
            return Path.GetFullPath(Path.Combine(Application.dataPath, "StreamingAssets/StellarNetLiteLoadTest"));
        }

        private static string GetLoadTestScriptPath()
        {
            return Path.GetFullPath(Path.Combine(Application.dataPath, "StreamingAssets/StellarNetLiteLoadTest/run-loadtest.ps1"));
        }

        private static string ResolvePowerShellExecutable()
        {
            string systemPowerShell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
            if (File.Exists(systemPowerShell))
            {
                return systemPowerShell;
            }

            return "powershell.exe";
        }

        private static string EscapeArgument(string value)
        {
            return string.IsNullOrEmpty(value) ? string.Empty : value.Replace("\"", "\\\"");
        }

        private void LoadPrefs()
        {
            _transport = (LoadTestTransport)EditorPrefs.GetInt(EditorPrefsPrefix + "transport", (int)LoadTestTransport.Kcp);
            _host = EditorPrefs.GetString(EditorPrefsPrefix + "host", DefaultHost);
            _port = EditorPrefs.GetInt(EditorPrefsPrefix + "port", DefaultPort);
            _roomCount = EditorPrefs.GetInt(EditorPrefsPrefix + "roomCount", DefaultRoomCount);
            _clientsPerRoom = EditorPrefs.GetInt(EditorPrefsPrefix + "clientsPerRoom", DefaultClientsPerRoom);
            _redundantClientsPerRoom = EditorPrefs.GetInt(EditorPrefsPrefix + "redundantClientsPerRoom", 0);
            _connectRate = EditorPrefs.GetInt(EditorPrefsPrefix + "connectRate", DefaultConnectRate);
            _duration = EditorPrefs.GetInt(EditorPrefsPrefix + "duration", DefaultDuration);
            _moveRate = EditorPrefs.GetInt(EditorPrefsPrefix + "moveRate", DefaultMoveRate);
            _logInterval = EditorPrefs.GetInt(EditorPrefsPrefix + "logInterval", DefaultLogInterval);
            _roomName = EditorPrefs.GetString(EditorPrefsPrefix + "roomName", DefaultRoomName);
            _accountPrefix = EditorPrefs.GetString(EditorPrefsPrefix + "accountPrefix", DefaultAccountPrefix);
            _clientVersion = EditorPrefs.GetString(EditorPrefsPrefix + "clientVersion", DefaultClientVersion);
        }

        private void SavePrefs()
        {
            EditorPrefs.SetInt(EditorPrefsPrefix + "transport", (int)_transport);
            EditorPrefs.SetString(EditorPrefsPrefix + "host", _host ?? DefaultHost);
            EditorPrefs.SetInt(EditorPrefsPrefix + "port", _port);
            EditorPrefs.SetInt(EditorPrefsPrefix + "roomCount", _roomCount);
            EditorPrefs.SetInt(EditorPrefsPrefix + "clientsPerRoom", _clientsPerRoom);
            EditorPrefs.SetInt(EditorPrefsPrefix + "redundantClientsPerRoom", _redundantClientsPerRoom);
            EditorPrefs.SetInt(EditorPrefsPrefix + "connectRate", _connectRate);
            EditorPrefs.SetInt(EditorPrefsPrefix + "duration", _duration);
            EditorPrefs.SetInt(EditorPrefsPrefix + "moveRate", _moveRate);
            EditorPrefs.SetInt(EditorPrefsPrefix + "logInterval", _logInterval);
            EditorPrefs.SetString(EditorPrefsPrefix + "roomName", _roomName ?? DefaultRoomName);
            EditorPrefs.SetString(EditorPrefsPrefix + "accountPrefix", _accountPrefix ?? DefaultAccountPrefix);
            EditorPrefs.SetString(EditorPrefsPrefix + "clientVersion", _clientVersion ?? DefaultClientVersion);
        }
    }
}
#endif

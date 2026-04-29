#if UNITY_EDITOR
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
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
        /// <summary>
        /// EditorPrefs 键名前缀。
        /// </summary>
        private const string EditorPrefsPrefix = "StellarNetLite.LoadTest.";

        /// <summary>
        /// 记录当前运行中压测进程 Id 的键名。
        /// </summary>
        private const string RunningPidPrefsKey = EditorPrefsPrefix + "runningPid";
        private const string DefaultHost = "127.0.0.1";
        private const int DefaultPort = 7777;
        private const int DefaultRoomCount = 1;
        private const int DefaultClientsPerRoom = 50;
        private const int DefaultConnectRate = 10;
        private const int DefaultDuration = 0;
        private const int DefaultMoveRate = 8;
        private const int DefaultLogInterval = 5;
        private const int DefaultRoomEndMinutes = 0;
        private const string DefaultRoomName = "LoadTestRoom";
        private const string DefaultAccountPrefix = "bot";
        private const string DefaultClientVersion = "0.0.1";

        /// <summary>
        /// 编辑器窗口可选的压测传输层。
        /// </summary>
        private enum LoadTestTransport
        {
            Kcp = 0,
            Tcp = 1
        }

        // 当前窗口保存的压测参数。
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
        private int _roomEndMinutes = DefaultRoomEndMinutes;
        private bool _enableReplayRecording;
        private string _roomName = DefaultRoomName;
        private string _accountPrefix = DefaultAccountPrefix;
        private string _clientVersion = DefaultClientVersion;

        // 运行中命令区的输入状态。
        private string _runtimeCommandText = "status";
        private int _runtimeRoomDelta = 1;
        private int _runtimeRoomNumber = 1;

        // 输出区域滚动状态。
        private Vector2 _scrollPos;
        private bool _autoScroll = true;
        private bool _scrollToBottomRequested;
        private readonly StringBuilder _outputBuilder = new StringBuilder(8192);
        private readonly ConcurrentQueue<string> _pendingOutputLines = new ConcurrentQueue<string>();

        // 当前正在跟踪的压测进程和输出样式。
        private Process _runningProcess;
        private GUIStyle _outputStyle;
        private bool _stylesInitialized;

        /// <summary>
        /// 打开压测工具窗口。
        /// </summary>
        [MenuItem("StellarNetLite/压测工具", false, 5)]
        public static void Open()
        {
            LoadTestWindow window = GetWindow<LoadTestWindow>("压测工具");
            window.minSize = new Vector2(760f, 580f);
            window.Show();
        }

        /// <summary>
        /// 初始化窗口状态并恢复上次参数。
        /// </summary>
        private void OnEnable()
        {
            LoadPrefs();
            EditorApplication.update += OnEditorUpdate;
            EditorApplication.quitting += OnEditorQuitting;
            RestoreTrackedProcess();
        }

        /// <summary>
        /// 关闭窗口时保存参数并移除编辑器回调。
        /// </summary>
        private void OnDisable()
        {
            SavePrefs();
            EditorApplication.update -= OnEditorUpdate;
            EditorApplication.quitting -= OnEditorQuitting;
        }

        /// <summary>
        /// 销毁窗口时尝试停止压测进程。
        /// </summary>
        private void OnDestroy()
        {
            TryStopProcess();
        }

        /// <summary>
        /// 绘制压测工具窗口主体。
        /// </summary>
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

        /// <summary>
        /// 延迟初始化输出区域样式。
        /// </summary>
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

        /// <summary>
        /// 绘制压测配置输入区。
        /// </summary>
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
            _roomEndMinutes = EditorGUILayout.IntField("房间自动结束/分钟 (0=关闭)", _roomEndMinutes);
            _enableReplayRecording = EditorGUILayout.Toggle("建房启用录像录制", _enableReplayRecording);
            _roomName = EditorGUILayout.TextField("房间名前缀", _roomName);
            _accountPrefix = EditorGUILayout.TextField("账号前缀", _accountPrefix);
            _clientVersion = EditorGUILayout.TextField("客户端版本", _clientVersion);
            EditorGUILayout.EndVertical();
        }

        /// <summary>
        /// 绘制启动、停止和清空等操作按钮。
        /// </summary>
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

        /// <summary>
        /// 绘制当前脚本路径与进程状态。
        /// </summary>
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

        /// <summary>
        /// 绘制运行态命令输入区域。
        /// </summary>
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

        /// <summary>
        /// 绘制日志输出区域。
        /// </summary>
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

        /// <summary>
        /// 启动独立压测脚本并开始跟踪进程输出。
        /// </summary>
        private void StartLoadTest()
        {
            if (TryGetTrackedProcess(out Process existingProcess))
            {
                EditorUtility.DisplayDialog("已有压测进程", $"已有压测进程正在运行 (PID: {existingProcess.Id})，请先停止当前进程。", "确定");
                return;
            }

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
                $"-LogInterval {_logInterval} " +
                $"-RoomEndMinutes {_roomEndMinutes} " +
                (_enableReplayRecording ? " -EnableReplayRecording" : string.Empty);

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
                AppendOutput(
                    $"[Editor] 启动压测: 传输层={_transport}, 地址={_host}:{_port}, 房间数={_roomCount}, 每房机器人={_clientsPerRoom}, 每房冗余={_redundantClientsPerRoom}, 总机器人={_roomCount * _clientsPerRoom}, 时长={(_duration > 0 ? _duration + "s" : "直到手动停止")}, 自动结束={(_roomEndMinutes > 0 ? _roomEndMinutes + "分钟" : "关闭")}, 录像={(_enableReplayRecording ? "开启" : "关闭")}");

                _runningProcess = new Process
                {
                    StartInfo = startInfo,
                    EnableRaisingEvents = true
                };
                _runningProcess.OutputDataReceived += OnProcessOutput;
                _runningProcess.ErrorDataReceived += OnProcessError;
                _runningProcess.Exited += OnProcessExited;
                _runningProcess.Start();
                SetTrackedProcessId(_runningProcess.Id);
                _runningProcess.BeginOutputReadLine();
                _runningProcess.BeginErrorReadLine();
            }
            catch (Exception ex)
            {
                _runningProcess = null;
                ClearTrackedProcessId();
                EditorUtility.DisplayDialog("启动失败", ex.Message, "确定");
            }
        }

        /// <summary>
        /// 停止当前压测进程。
        /// </summary>
        private void TryStopProcess()
        {
            if (!TryGetTrackedProcess(out Process process))
            {
                return;
            }

            try
            {
                if (!process.HasExited)
                {
                    AppendOutput($"[Editor] 正在停止压测进程... PID={process.Id}");
                    KillProcessCompat(process);
                }
            }
            catch (Exception ex)
            {
                AppendOutput($"[Editor] 停止失败: {ex.GetType().Name} - {ex.Message}");
            }
            finally
            {
                CleanupProcess();
            }
        }

        /// <summary>
        /// 兼容不同运行时的进程树终止方式。
        /// </summary>
        private static void KillProcessCompat(Process process)
        {
            if (process == null || process.HasExited)
            {
                return;
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                try
                {
                    var taskKillStartInfo = new ProcessStartInfo
                    {
                        FileName = "taskkill.exe",
                        Arguments = $"/PID {process.Id} /T /F",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };

                    using (Process taskKillProcess = Process.Start(taskKillStartInfo))
                    {
                        taskKillProcess?.WaitForExit(5000);
                    }

                    process.WaitForExit(5000);
                    if (process.HasExited)
                    {
                        return;
                    }
                }
                catch
                {
                    // 回退到下方的 Kill 逻辑。
                }
            }

            System.Reflection.MethodInfo killProcessTree = typeof(Process).GetMethod("Kill", new[] { typeof(bool) });
            if (killProcessTree != null)
            {
                try
                {
                    killProcessTree.Invoke(process, new object[] { true });
                    process.WaitForExit(5000);
                    if (process.HasExited)
                    {
                        return;
                    }
                }
                catch
                {
                    // 回退到最基础 Kill。
                }
            }

            process.Kill();
            process.WaitForExit(5000);
        }

        /// <summary>
        /// 处理标准输出回调。
        /// </summary>
        private void OnProcessOutput(object sender, DataReceivedEventArgs e)
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                AppendOutput(e.Data);
            }
        }

        /// <summary>
        /// 处理标准错误输出回调。
        /// </summary>
        private void OnProcessError(object sender, DataReceivedEventArgs e)
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                AppendOutput("[ERR] " + e.Data);
            }
        }

        /// <summary>
        /// 处理进程退出事件。
        /// </summary>
        private void OnProcessExited(object sender, EventArgs e)
        {
            AppendOutput("[Editor] 压测进程已退出。");
            ClearTrackedProcessId();
        }

        /// <summary>
        /// 向运行中的压测进程发送控制台命令。
        /// </summary>
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

        /// <summary>
        /// 刷新输出缓冲并检测进程退出。
        /// </summary>
        private void OnEditorUpdate()
        {
            FlushPendingOutput();

            if (_runningProcess != null && _runningProcess.HasExited)
            {
                CleanupProcess();
                Repaint();
            }
        }

        /// <summary>
        /// 解除当前进程绑定并清理跟踪状态。
        /// </summary>
        private void CleanupProcess()
        {
            if (_runningProcess == null)
            {
                ClearTrackedProcessId();
                return;
            }

            _runningProcess.OutputDataReceived -= OnProcessOutput;
            _runningProcess.ErrorDataReceived -= OnProcessError;
            _runningProcess.Exited -= OnProcessExited;
            _runningProcess.Dispose();
            _runningProcess = null;
            ClearTrackedProcessId();
        }

        /// <summary>
        /// Unity 退出时一并停止压测进程。
        /// </summary>
        private void OnEditorQuitting()
        {
            TryStopProcess();
        }

        /// <summary>
        /// 将一行输出加入待刷新队列。
        /// </summary>
        private void AppendOutput(string line)
        {
            if (string.IsNullOrEmpty(line))
            {
                return;
            }

            _pendingOutputLines.Enqueue(line);
        }

        /// <summary>
        /// 把待输出日志刷新到可视文本区。
        /// </summary>
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

        /// <summary>
        /// 校验启动参数是否有效。
        /// </summary>
        private bool ValidateInputs()
        {
            if (string.IsNullOrWhiteSpace(_host))
            {
                EditorUtility.DisplayDialog("参数错误", "Host 不能为空。", "确定");
                return false;
            }

            if (_port <= 0 || _roomCount <= 0 || _clientsPerRoom <= 0 || _redundantClientsPerRoom < 0 || _connectRate <= 0 || _duration < 0 ||
                _roomEndMinutes < 0 ||
                _moveRate <= 0 || _logInterval <= 0)
            {
                EditorUtility.DisplayDialog("参数错误", "端口、房间数、每房客户端数、建连速率、移动频率、日志间隔必须大于 0；冗余成员数、压测时长和房间自动结束分钟数必须大于等于 0。", "确定");
                return false;
            }

            return true;
        }

        /// <summary>
        /// 在文件管理器中打开压测工具目录。
        /// </summary>
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

        /// <summary>
        /// 返回压测工具目录绝对路径。
        /// </summary>
        private static string GetLoadTestDirectory()
        {
            return Path.GetFullPath(Path.Combine(Application.dataPath, "StreamingAssets/StellarNetLiteLoadTest"));
        }

        /// <summary>
        /// 返回压测启动脚本绝对路径。
        /// </summary>
        private static string GetLoadTestScriptPath()
        {
            return Path.GetFullPath(Path.Combine(Application.dataPath, "StreamingAssets/StellarNetLiteLoadTest/run-loadtest.ps1"));
        }

        /// <summary>
        /// 解析本机可用的 PowerShell 可执行文件。
        /// </summary>
        private static string ResolvePowerShellExecutable()
        {
            string systemPowerShell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0",
                "powershell.exe");
            if (File.Exists(systemPowerShell))
            {
                return systemPowerShell;
            }

            return "powershell.exe";
        }

        /// <summary>
        /// 转义命令行参数中的引号。
        /// </summary>
        private static string EscapeArgument(string value)
        {
            return string.IsNullOrEmpty(value) ? string.Empty : value.Replace("\"", "\\\"");
        }

        /// <summary>
        /// 从 EditorPrefs 恢复上一次配置。
        /// </summary>
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
            _roomEndMinutes = EditorPrefs.GetInt(EditorPrefsPrefix + "roomEndMinutes", DefaultRoomEndMinutes);
            _enableReplayRecording = EditorPrefs.GetBool(EditorPrefsPrefix + "enableReplayRecording", false);
            _roomName = EditorPrefs.GetString(EditorPrefsPrefix + "roomName", DefaultRoomName);
            _accountPrefix = EditorPrefs.GetString(EditorPrefsPrefix + "accountPrefix", DefaultAccountPrefix);
            _clientVersion = EditorPrefs.GetString(EditorPrefsPrefix + "clientVersion", DefaultClientVersion);
        }

        /// <summary>
        /// 把当前配置写回 EditorPrefs。
        /// </summary>
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
            EditorPrefs.SetInt(EditorPrefsPrefix + "roomEndMinutes", _roomEndMinutes);
            EditorPrefs.SetBool(EditorPrefsPrefix + "enableReplayRecording", _enableReplayRecording);
            EditorPrefs.SetString(EditorPrefsPrefix + "roomName", _roomName ?? DefaultRoomName);
            EditorPrefs.SetString(EditorPrefsPrefix + "accountPrefix", _accountPrefix ?? DefaultAccountPrefix);
            EditorPrefs.SetString(EditorPrefsPrefix + "clientVersion", _clientVersion ?? DefaultClientVersion);
        }

        /// <summary>
        /// 尝试根据上次记录的进程 Id 恢复进程跟踪。
        /// </summary>
        private void RestoreTrackedProcess()
        {
            if (_runningProcess != null)
            {
                return;
            }

            int pid = GetTrackedProcessId();
            if (pid <= 0)
            {
                return;
            }

            try
            {
                Process process = Process.GetProcessById(pid);
                if (process == null || process.HasExited)
                {
                    ClearTrackedProcessId();
                    return;
                }

                _runningProcess = process;
                _runningProcess.EnableRaisingEvents = true;
                _runningProcess.Exited -= OnProcessExited;
                _runningProcess.Exited += OnProcessExited;
                AppendOutput($"[Editor] 已恢复跟踪压测进程 PID={pid}");
            }
            catch
            {
                ClearTrackedProcessId();
            }
        }

        /// <summary>
        /// 获取当前应当跟踪的压测进程。
        /// </summary>
        private bool TryGetTrackedProcess(out Process process)
        {
            process = null;
            if (_runningProcess != null)
            {
                if (_runningProcess.HasExited)
                {
                    CleanupProcess();
                    return false;
                }

                process = _runningProcess;
                return true;
            }

            int pid = GetTrackedProcessId();
            if (pid <= 0)
            {
                return false;
            }

            try
            {
                Process recovered = Process.GetProcessById(pid);
                if (recovered == null || recovered.HasExited)
                {
                    ClearTrackedProcessId();
                    return false;
                }

                _runningProcess = recovered;
                _runningProcess.EnableRaisingEvents = true;
                _runningProcess.Exited -= OnProcessExited;
                _runningProcess.Exited += OnProcessExited;
                process = _runningProcess;
                return true;
            }
            catch
            {
                ClearTrackedProcessId();
                return false;
            }
        }

        /// <summary>
        /// 读取已记录的压测进程 Id。
        /// </summary>
        private static int GetTrackedProcessId()
        {
            return EditorPrefs.GetInt(RunningPidPrefsKey, 0);
        }

        /// <summary>
        /// 保存当前压测进程 Id。
        /// </summary>
        private static void SetTrackedProcessId(int pid)
        {
            EditorPrefs.SetInt(RunningPidPrefsKey, Mathf.Max(0, pid));
        }

        /// <summary>
        /// 清空当前压测进程 Id 记录。
        /// </summary>
        private static void ClearTrackedProcessId()
        {
            EditorPrefs.DeleteKey(RunningPidPrefsKey);
        }
    }
}
#endif

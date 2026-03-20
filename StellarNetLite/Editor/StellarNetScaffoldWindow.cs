using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace StellarNet.Lite.Editor
{
    public sealed class StellarNetScaffoldWindow : EditorWindow
    {
        #region 常量

        private const string DefaultOutputRoot = "Assets/Scripts/Game";
        private const string DefaultBaseNamespace = "MineGame";
        private const string ManifestFolderName = ".Scaffold";
        private const int DefaultRoomComponentId = 10;
        private const int DefaultRoomC2SMsgId = 10000;
        private const int DefaultRoomS2CMsgId = 10001;
        private const int DefaultGlobalC2SMsgId = 11000;
        private const int DefaultGlobalS2CMsgId = 11001;

        private const string GeneratedRootPath = "Assets/StellarNetLite/Runtime/Shared/Generated";
        private const string GeneratedProtocolMsgIdsFolderPath = GeneratedRootPath + "/Protocol/MsgIds";
        private const string GeneratedProtocolMetaFolderPath = GeneratedRootPath + "/Protocol/Meta";
        private const string GeneratedComponentIdsFolderPath = GeneratedRootPath + "/Protocol/Components";
        private const string GeneratedRoomBinderFolderPath = GeneratedRootPath + "/Binders/RoomComponents";
        private const string GeneratedGlobalModuleBinderFolderPath = GeneratedRootPath + "/Binders/GlobalModules";

        #endregion

        #region 枚举与数据结构

        private enum ScaffoldBusinessType
        {
            RoomComponent = 0,
            GlobalModule = 1
        }

        [Serializable]
        private sealed class ScaffoldManifest
        {
            public string FeatureName;
            public string DisplayName;
            public string NamespacePrefix;
            public string BaseNamespace;
            public string FullRootNamespace;
            public string OutputRoot;
            public string BusinessType;
            public int ComponentId;
            public int C2SMsgId;
            public int S2CMsgId;
            public string[] SourceFiles;
            public string[] GeneratedFiles;
            public string[] Files;
        }

        private sealed class ManagedManifestEntry
        {
            public string ManifestAssetPath;
            public ScaffoldManifest Manifest;
        }

        #endregion

        #region 字段

        private ScaffoldBusinessType _businessType = ScaffoldBusinessType.RoomComponent;
        private string _namespacePrefix = string.Empty;
        private string _baseNamespace = DefaultBaseNamespace;
        private string _outputRoot = DefaultOutputRoot;
        private string _featureName = "NewFeature";
        private string _displayName = "新功能模块";
        private int _componentId = DefaultRoomComponentId;
        private int _c2sMsgId = DefaultRoomC2SMsgId;
        private int _s2cMsgId = DefaultRoomS2CMsgId;

        private Vector2 _createScrollPosition;
        private Vector2 _managedScrollPosition;

        private readonly List<ManagedManifestEntry> _managedEntries = new List<ManagedManifestEntry>(64);
        private int _selectedManagedIndex = -1;

        #endregion

        #region 菜单入口

        [MenuItem("StellarNetLite/业务脚手架生成器")]
        public static void Open()
        {
            var window = GetWindow<StellarNetScaffoldWindow>("业务脚手架生成器");
            if (window == null)
            {
                Debug.LogError("[StellarNetScaffoldWindow] 打开窗口失败");
                return;
            }

            window.minSize = new Vector2(860f, 720f);
            window.RefreshManagedEntries();
            window.Show();
        }

        #endregion

        #region 生命周期

        private void OnEnable()
        {
            _baseNamespace = DefaultBaseNamespace;
            RefreshManagedEntries();
            ResetDefaultsByBusinessType();
        }

        #endregion

        #region GUI

        private void OnGUI()
        {
            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("业务脚手架生成器", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "支持生成房间业务组件、全局模块，并通过 manifest 形成生成、删除、取消托管闭环。",
                MessageType.Info);

            EditorGUILayout.Space(8f);

            using (new EditorGUILayout.VerticalScope("box"))
            {
                DrawCreateSection();
            }

            EditorGUILayout.Space(8f);

            using (new EditorGUILayout.VerticalScope("box"))
            {
                DrawManagedSection();
            }
        }

        private void DrawCreateSection()
        {
            EditorGUILayout.LabelField("业务单元生成", EditorStyles.boldLabel);

            _createScrollPosition = EditorGUILayout.BeginScrollView(_createScrollPosition, GUILayout.Height(360f));

            DrawBusinessTypeSection();
            DrawNamespaceSection();
            DrawOutputSection();
            DrawBusinessMetaSection();
            DrawIdSection();
            DrawPreviewSection();
            DrawGenerateButtons();

            EditorGUILayout.EndScrollView();
        }

        private void DrawManagedSection()
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("已托管脚手架", EditorStyles.boldLabel);
            if (GUILayout.Button("刷新清单", GUILayout.Width(120f)))
            {
                RefreshManagedEntries();
            }

            EditorGUILayout.EndHorizontal();

            if (_managedEntries.Count == 0)
            {
                EditorGUILayout.HelpBox("当前未发现任何已托管脚手架清单。", MessageType.Info);
                return;
            }

            _managedScrollPosition = EditorGUILayout.BeginScrollView(_managedScrollPosition, GUILayout.Height(280f));

            for (int i = 0; i < _managedEntries.Count; i++)
            {
                DrawManagedEntryItem(i, _managedEntries[i]);
            }

            EditorGUILayout.EndScrollView();

            DrawManagedEntryActions();
        }

        private void DrawBusinessTypeSection()
        {
            EditorGUILayout.LabelField("业务类型", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            _businessType = (ScaffoldBusinessType)EditorGUILayout.EnumPopup(
                new GUIContent("脚手架类型"),
                _businessType);
            if (EditorGUI.EndChangeCheck())
            {
                ResetDefaultsByBusinessType();
            }

            EditorGUILayout.Space(6f);
        }

        private void DrawNamespaceSection()
        {
            EditorGUILayout.LabelField("命名空间配置", EditorStyles.boldLabel);

            _namespacePrefix = EditorGUILayout.TextField(
                new GUIContent("前置 Namespace"),
                _namespacePrefix);

            using (new EditorGUI.DisabledScope(true))
            {
                _baseNamespace = EditorGUILayout.TextField(
                    new GUIContent("业务根 Namespace"),
                    DefaultBaseNamespace);
            }

            string fullRootNamespace = ComposeFullRootNamespace();
            EditorGUILayout.LabelField("最终根 Namespace", string.IsNullOrEmpty(fullRootNamespace) ? "(非法)" : fullRootNamespace);

            EditorGUILayout.Space(6f);
        }

        private void DrawOutputSection()
        {
            EditorGUILayout.LabelField("输出目录配置", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            _outputRoot = EditorGUILayout.TextField(
                new GUIContent("输出根目录"),
                _outputRoot);

            if (GUILayout.Button("选择目录", GUILayout.Width(100f)))
            {
                SelectOutputFolder();
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(6f);
        }

        private void DrawBusinessMetaSection()
        {
            EditorGUILayout.LabelField("业务信息", EditorStyles.boldLabel);

            _featureName = EditorGUILayout.TextField(
                new GUIContent("模块名"),
                _featureName);

            _displayName = EditorGUILayout.TextField(
                new GUIContent("显示名"),
                _displayName);

            EditorGUILayout.Space(6f);
        }

        private void DrawIdSection()
        {
            EditorGUILayout.LabelField("协议与组件 Id 配置", EditorStyles.boldLabel);

            if (_businessType == ScaffoldBusinessType.RoomComponent)
            {
                _componentId = EditorGUILayout.IntField(
                    new GUIContent("组件 Id"),
                    _componentId);
            }
            else
            {
                EditorGUILayout.LabelField("组件 Id", "全局模块不需要");
            }

            _c2sMsgId = EditorGUILayout.IntField(new GUIContent("C2S MsgId"), _c2sMsgId);
            _s2cMsgId = EditorGUILayout.IntField(new GUIContent("S2C MsgId"), _s2cMsgId);

            EditorGUILayout.Space(6f);
        }

        private void DrawPreviewSection()
        {
            EditorGUILayout.LabelField("生成预览", EditorStyles.boldLabel);

            string featureToken = SanitizeFeatureToken(_featureName);
            string fullRootNamespace = ComposeFullRootNamespace();

            if (_businessType == ScaffoldBusinessType.RoomComponent)
            {
                EditorGUILayout.LabelField("协议 Namespace", $"{fullRootNamespace}.Shared.Protocol");
                EditorGUILayout.LabelField("服务端组件 Namespace", $"{fullRootNamespace}.Server.Components");
                EditorGUILayout.LabelField("客户端组件 Namespace", $"{fullRootNamespace}.Client.Components");
                EditorGUILayout.LabelField("协议文件", BuildProtocolFileAssetPath(featureToken));
                EditorGUILayout.LabelField("服务端组件文件", BuildServerRoomComponentFileAssetPath(featureToken));
                EditorGUILayout.LabelField("客户端组件文件", BuildClientRoomComponentFileAssetPath(featureToken));
            }
            else
            {
                EditorGUILayout.LabelField("协议 Namespace", $"{fullRootNamespace}.Shared.Protocol");
                EditorGUILayout.LabelField("服务端模块 Namespace", $"{fullRootNamespace}.Server.Modules");
                EditorGUILayout.LabelField("客户端模块 Namespace", $"{fullRootNamespace}.Client.Modules");
                EditorGUILayout.LabelField("协议文件", BuildProtocolFileAssetPath(featureToken));
                EditorGUILayout.LabelField("服务端模块文件", BuildServerModuleFileAssetPath(featureToken));
                EditorGUILayout.LabelField("客户端模块文件", BuildClientModuleFileAssetPath(featureToken));
            }

            EditorGUILayout.LabelField("生成协议 MsgIds 分片", BuildGeneratedProtocolMsgIdsFileAssetPath(featureToken));
            EditorGUILayout.LabelField("生成协议 Meta 分片", BuildGeneratedProtocolMetaFileAssetPath(featureToken));

            if (_businessType == ScaffoldBusinessType.RoomComponent)
            {
                EditorGUILayout.LabelField("生成组件常量分片", BuildGeneratedComponentIdsFileAssetPath(featureToken));
                EditorGUILayout.LabelField("生成房间 Binder 分片", BuildGeneratedRoomBinderFileAssetPath(featureToken));
            }
            else
            {
                EditorGUILayout.LabelField("生成全局模块 Binder 分片", BuildGeneratedGlobalModuleBinderFileAssetPath(featureToken));
            }

            EditorGUILayout.LabelField("Manifest 文件", BuildManifestAssetPath(featureToken));
            EditorGUILayout.Space(10f);
        }

        private void DrawGenerateButtons()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(!CanGenerate()))
                {
                    if (GUILayout.Button("生成并托管脚手架", GUILayout.Height(34f)))
                    {
                        GenerateScaffold();
                    }
                }

                using (new EditorGUI.DisabledScope(!CanGenerate()))
                {
                    if (GUILayout.Button("仅生成源码（不写清单）", GUILayout.Height(34f)))
                    {
                        GenerateScaffoldWithoutManifest();
                    }
                }
            }

            if (!CanGenerate())
            {
                EditorGUILayout.HelpBox("当前输入配置非法。", MessageType.Warning);
            }
        }

        private void DrawManagedEntryItem(int index, ManagedManifestEntry entry)
        {
            if (entry == null || entry.Manifest == null)
            {
                Debug.LogError("[StellarNetScaffoldWindow] 条目无效");
                return;
            }

            bool selected = _selectedManagedIndex == index;
            string title = $"{entry.Manifest.FeatureName}  [{entry.Manifest.BusinessType}]";

            using (new EditorGUILayout.VerticalScope(selected ? "window" : "box"))
            {
                if (GUILayout.Button(title, EditorStyles.label))
                {
                    _selectedManagedIndex = index;
                }

                EditorGUILayout.LabelField("显示名", entry.Manifest.DisplayName ?? string.Empty);
                EditorGUILayout.LabelField("根 Namespace", entry.Manifest.FullRootNamespace ?? string.Empty);
                EditorGUILayout.LabelField("输出目录", entry.Manifest.OutputRoot ?? string.Empty);
                EditorGUILayout.LabelField("源码文件数量", GetSourceFiles(entry.Manifest).Length.ToString());
                EditorGUILayout.LabelField("生成分片数量", GetGeneratedFiles(entry.Manifest).Length.ToString());
            }
        }

        private void DrawManagedEntryActions()
        {
            if (_selectedManagedIndex < 0 || _selectedManagedIndex >= _managedEntries.Count)
            {
                EditorGUILayout.HelpBox("请选择一个已托管脚手架条目后再执行操作。", MessageType.Info);
                return;
            }

            ManagedManifestEntry selectedEntry = _managedEntries[_selectedManagedIndex];
            if (selectedEntry == null || selectedEntry.Manifest == null)
            {
                Debug.LogError("[StellarNetScaffoldWindow] 当前选中条目无效");
                return;
            }

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("当前选中", $"{selectedEntry.Manifest.FeatureName} [{selectedEntry.Manifest.BusinessType}]");

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("删除脚手架产物", GUILayout.Height(30f)))
                {
                    DeleteManagedScaffold(selectedEntry);
                }

                if (GUILayout.Button("取消托管（保留源码）", GUILayout.Height(30f)))
                {
                    UntrackManagedScaffold(selectedEntry);
                }
            }
        }

        #endregion

        #region 业务生成

        private void GenerateScaffold()
        {
            GenerateInternal(true);
        }

        private void GenerateScaffoldWithoutManifest()
        {
            GenerateInternal(false);
        }

        private void GenerateInternal(bool writeManifest)
        {
            if (!CanGenerate())
            {
                Debug.LogError("[StellarNetScaffoldWindow] 生成失败");
                return;
            }

            string featureToken = SanitizeFeatureToken(_featureName);
            if (string.IsNullOrEmpty(featureToken))
            {
                Debug.LogError("[StellarNetScaffoldWindow] featureToken 无效");
                return;
            }

            if (HasFeatureConflict(featureToken))
            {
                Debug.LogError($"[StellarNetScaffoldWindow] 生成失败: 模块已存在, Feature:{featureToken}");
                return;
            }

            string fullRootNamespace = ComposeFullRootNamespace();
            if (string.IsNullOrEmpty(fullRootNamespace))
            {
                Debug.LogError("[StellarNetScaffoldWindow] fullRootNamespace 无效");
                return;
            }

            List<string> sourceFilePaths = BuildManagedSourceFilePaths(featureToken);
            if (sourceFilePaths == null || sourceFilePaths.Count == 0)
            {
                Debug.LogError("[StellarNetScaffoldWindow] sourceFilePaths 无效");
                return;
            }

            List<string> generatedFilePaths = BuildManagedGeneratedFilePaths(featureToken);
            if (generatedFilePaths == null || generatedFilePaths.Count == 0)
            {
                Debug.LogError("[StellarNetScaffoldWindow] generatedFilePaths 无效");
                return;
            }

            for (int i = 0; i < sourceFilePaths.Count; i++)
            {
                string filePath = sourceFilePaths[i];
                string directory = NormalizeAssetPath(Path.GetDirectoryName(filePath));
                if (!EnsureAssetDirectory(directory))
                {
                    Debug.LogError("[StellarNetScaffoldWindow] 创建源码目录失败");
                    return;
                }
            }

            string protocolContent = BuildProtocolFileContent(fullRootNamespace, featureToken);
            if (string.IsNullOrEmpty(protocolContent))
            {
                Debug.LogError("[StellarNetScaffoldWindow] 协议内容无效");
                return;
            }

            if (!WriteFile(BuildProtocolFileAssetPath(featureToken), protocolContent))
            {
                Debug.LogError("[StellarNetScaffoldWindow] 协议文件写入失败");
                return;
            }

            if (_businessType == ScaffoldBusinessType.RoomComponent)
            {
                string serverContent = BuildServerRoomComponentFileContent(fullRootNamespace, featureToken);
                string clientContent = BuildClientRoomComponentFileContent(fullRootNamespace, featureToken);

                if (string.IsNullOrEmpty(serverContent) || string.IsNullOrEmpty(clientContent))
                {
                    Debug.LogError("[StellarNetScaffoldWindow] 房间组件内容无效");
                    return;
                }

                if (!WriteFile(BuildServerRoomComponentFileAssetPath(featureToken), serverContent))
                {
                    Debug.LogError("[StellarNetScaffoldWindow] 服务端组件写入失败");
                    return;
                }

                if (!WriteFile(BuildClientRoomComponentFileAssetPath(featureToken), clientContent))
                {
                    Debug.LogError("[StellarNetScaffoldWindow] 客户端组件写入失败");
                    return;
                }
            }
            else
            {
                string serverContent = BuildServerModuleFileContent(fullRootNamespace, featureToken);
                string clientContent = BuildClientModuleFileContent(fullRootNamespace, featureToken);

                if (string.IsNullOrEmpty(serverContent) || string.IsNullOrEmpty(clientContent))
                {
                    Debug.LogError("[StellarNetScaffoldWindow] 全局模块内容无效");
                    return;
                }

                if (!WriteFile(BuildServerModuleFileAssetPath(featureToken), serverContent))
                {
                    Debug.LogError("[StellarNetScaffoldWindow] 服务端模块写入失败");
                    return;
                }

                if (!WriteFile(BuildClientModuleFileAssetPath(featureToken), clientContent))
                {
                    Debug.LogError("[StellarNetScaffoldWindow] 客户端模块写入失败");
                    return;
                }
            }

            if (writeManifest)
            {
                ScaffoldManifest manifest = BuildManifest(featureToken, fullRootNamespace, sourceFilePaths, generatedFilePaths);
                if (manifest == null)
                {
                    Debug.LogError("[StellarNetScaffoldWindow] manifest 无效");
                    return;
                }

                string manifestPath = BuildManifestAssetPath(featureToken);
                string manifestDirectory = NormalizeAssetPath(Path.GetDirectoryName(manifestPath));
                if (!EnsureAssetDirectory(manifestDirectory))
                {
                    Debug.LogError("[StellarNetScaffoldWindow] manifest 目录创建失败");
                    return;
                }

                string manifestJson = JsonUtility.ToJson(manifest, true);
                if (string.IsNullOrEmpty(manifestJson))
                {
                    Debug.LogError("[StellarNetScaffoldWindow] manifestJson 无效");
                    return;
                }

                if (!WriteFile(manifestPath, manifestJson))
                {
                    Debug.LogError("[StellarNetScaffoldWindow] manifest 写入失败");
                    return;
                }
            }

            AssetDatabase.Refresh();
            LiteProtocolScanner.ManualRun();
            RefreshManagedEntries();

            EditorUtility.DisplayDialog(
                "生成完成",
                $"业务脚手架生成成功。\n\n类型: {_businessType}\n最终根 Namespace: {fullRootNamespace}\n模块名: {featureToken}\n托管: {(writeManifest ? "是" : "否")}",
                "确定");
        }

        /// <summary>
        /// 我在真正写文件前做同名冲突拦截。
        /// 这里同时检查已托管 manifest、目标源码路径、目标 manifest 路径，是为了阻止覆盖式生成污染现有模块。
        /// </summary>
        private bool HasFeatureConflict(string featureToken)
        {
            if (string.IsNullOrEmpty(featureToken))
            {
                Debug.LogError("[StellarNetScaffoldWindow] HasFeatureConflict failed");
                return true;
            }

            for (int i = 0; i < _managedEntries.Count; i++)
            {
                ManagedManifestEntry entry = _managedEntries[i];
                if (entry == null || entry.Manifest == null)
                {
                    continue;
                }

                string existingFeature = entry.Manifest.FeatureName ?? string.Empty;
                if (string.Equals(existingFeature, featureToken, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            string manifestPath = BuildManifestAssetPath(featureToken);
            if (AssetFileExists(manifestPath))
            {
                return true;
            }

            List<string> sourceFilePaths = BuildManagedSourceFilePaths(featureToken);
            if (sourceFilePaths == null || sourceFilePaths.Count == 0)
            {
                Debug.LogError("[StellarNetScaffoldWindow] HasFeatureConflict failed");
                return true;
            }

            for (int i = 0; i < sourceFilePaths.Count; i++)
            {
                string filePath = sourceFilePaths[i];
                if (AssetFileExists(filePath))
                {
                    return true;
                }
            }

            return false;
        }

        #endregion

        #region 托管清单管理

        private void RefreshManagedEntries()
        {
            _managedEntries.Clear();
            _selectedManagedIndex = -1;

            string manifestFolder = BuildManifestFolderAssetPath();
            string manifestFolderFullPath = AssetPathToFullPath(manifestFolder);
            if (string.IsNullOrEmpty(manifestFolderFullPath))
            {
                Debug.LogError("[StellarNetScaffoldWindow] manifestFolder 无效");
                return;
            }

            if (!Directory.Exists(manifestFolderFullPath))
            {
                return;
            }

            string[] files = Directory.GetFiles(manifestFolderFullPath, "*.json", SearchOption.TopDirectoryOnly);
            if (files == null || files.Length == 0)
            {
                return;
            }

            for (int i = 0; i < files.Length; i++)
            {
                string fullPath = files[i];
                if (string.IsNullOrEmpty(fullPath))
                {
                    continue;
                }

                string json = File.ReadAllText(fullPath);
                if (string.IsNullOrEmpty(json))
                {
                    continue;
                }

                ScaffoldManifest manifest = JsonUtility.FromJson<ScaffoldManifest>(json);
                if (manifest == null)
                {
                    continue;
                }

                UpgradeLegacyManifestInMemory(manifest);

                string assetPath = FullPathToAssetPath(fullPath);
                if (string.IsNullOrEmpty(assetPath))
                {
                    continue;
                }

                _managedEntries.Add(new ManagedManifestEntry
                {
                    ManifestAssetPath = assetPath,
                    Manifest = manifest
                });
            }

            _managedEntries.Sort((a, b) =>
            {
                string aName = a?.Manifest?.FeatureName ?? string.Empty;
                string bName = b?.Manifest?.FeatureName ?? string.Empty;
                return string.Compare(aName, bName, StringComparison.Ordinal);
            });
        }

        private void DeleteManagedScaffold(ManagedManifestEntry entry)
        {
            if (entry == null || entry.Manifest == null)
            {
                Debug.LogError("[StellarNetScaffoldWindow] 删除失败");
                return;
            }

            string featureName = entry.Manifest.FeatureName ?? string.Empty;
            if (string.IsNullOrEmpty(featureName))
            {
                Debug.LogError("[StellarNetScaffoldWindow] FeatureName 无效");
                return;
            }

            bool confirm = EditorUtility.DisplayDialog(
                "删除脚手架产物",
                $"将删除业务单元 {featureName} 的源码文件、generated 分片文件与 manifest。\n\n是否继续？",
                "继续删除",
                "取消");

            if (!confirm)
            {
                return;
            }

            string[] sourceFiles = GetSourceFiles(entry.Manifest);
            for (int i = 0; i < sourceFiles.Length; i++)
            {
                string assetPath = sourceFiles[i];
                if (string.IsNullOrEmpty(assetPath))
                {
                    continue;
                }

                string fullPath = AssetPathToFullPath(assetPath);
                if (string.IsNullOrEmpty(fullPath))
                {
                    continue;
                }

                if (!File.Exists(fullPath))
                {
                    continue;
                }

                string content = File.ReadAllText(fullPath);
                if (string.IsNullOrEmpty(content))
                {
                    continue;
                }

                if (!content.Contains("// <auto-generated>") || !content.Contains($"// ScaffoldFeature: {featureName}"))
                {
                    Debug.LogError("[StellarNetScaffoldWindow] 源码已被人工接管，跳过删除");
                    continue;
                }

                File.Delete(fullPath);
                DeleteMetaFileIfExists(fullPath);
            }

            string[] generatedFiles = GetGeneratedFiles(entry.Manifest);
            for (int i = 0; i < generatedFiles.Length; i++)
            {
                string assetPath = generatedFiles[i];
                if (string.IsNullOrEmpty(assetPath))
                {
                    continue;
                }

                string fullPath = AssetPathToFullPath(assetPath);
                if (string.IsNullOrEmpty(fullPath))
                {
                    continue;
                }

                if (!File.Exists(fullPath))
                {
                    continue;
                }

                File.Delete(fullPath);
                DeleteMetaFileIfExists(fullPath);
            }

            string manifestFullPath = AssetPathToFullPath(entry.ManifestAssetPath);
            if (!string.IsNullOrEmpty(manifestFullPath) && File.Exists(manifestFullPath))
            {
                File.Delete(manifestFullPath);
                DeleteMetaFileIfExists(manifestFullPath);
            }

            AssetDatabase.Refresh();
            LiteProtocolScanner.ManualRun();
            RefreshManagedEntries();
        }

        private void UntrackManagedScaffold(ManagedManifestEntry entry)
        {
            if (entry == null || entry.Manifest == null)
            {
                Debug.LogError("[StellarNetScaffoldWindow] 取消托管失败");
                return;
            }

            string featureName = entry.Manifest.FeatureName ?? string.Empty;
            if (string.IsNullOrEmpty(featureName))
            {
                Debug.LogError("[StellarNetScaffoldWindow] FeatureName 无效");
                return;
            }

            bool confirm = EditorUtility.DisplayDialog(
                "取消托管",
                $"将取消业务单元 {featureName} 的脚手架托管。\n\n保留源码，删除 manifest，并清理 auto-generated 头。\n\n是否继续？",
                "继续取消托管",
                "取消");

            if (!confirm)
            {
                return;
            }

            string[] sourceFiles = GetSourceFiles(entry.Manifest);
            for (int i = 0; i < sourceFiles.Length; i++)
            {
                string assetPath = sourceFiles[i];
                if (string.IsNullOrEmpty(assetPath))
                {
                    continue;
                }

                string fullPath = AssetPathToFullPath(assetPath);
                if (string.IsNullOrEmpty(fullPath))
                {
                    continue;
                }

                if (!File.Exists(fullPath))
                {
                    continue;
                }

                string content = File.ReadAllText(fullPath);
                if (string.IsNullOrEmpty(content))
                {
                    continue;
                }

                string stripped = StripAutoGeneratedHeader(content);
                if (string.IsNullOrEmpty(stripped))
                {
                    continue;
                }

                File.WriteAllText(fullPath, stripped, new UTF8Encoding(false));
            }

            string manifestFullPath = AssetPathToFullPath(entry.ManifestAssetPath);
            if (!string.IsNullOrEmpty(manifestFullPath) && File.Exists(manifestFullPath))
            {
                File.Delete(manifestFullPath);
                DeleteMetaFileIfExists(manifestFullPath);
            }

            AssetDatabase.Refresh();
            LiteProtocolScanner.ManualRun();
            RefreshManagedEntries();
        }

        #endregion

        #region 文件内容构建

        private string BuildProtocolFileContent(string fullRootNamespace, string featureToken)
        {
            if (string.IsNullOrEmpty(fullRootNamespace) || string.IsNullOrEmpty(featureToken))
            {
                Debug.LogError("[StellarNetScaffoldWindow] BuildProtocolFileContent failed");
                return string.Empty;
            }

            string protocolNamespace = $"{fullRootNamespace}.Shared.Protocol";
            var sb = new StringBuilder(2048);

            AppendAutoGeneratedHeader(sb, featureToken);

            sb.AppendLine("using StellarNet.Lite.Shared.Core;");
            sb.AppendLine();
            sb.AppendLine($"namespace {protocolNamespace}");
            sb.AppendLine("{");
            sb.AppendLine($"    [NetMsg({_c2sMsgId}, {GetScopeCode()}, NetDir.C2S)]");
            sb.AppendLine($"    public sealed class C2S_{featureToken}Req");
            sb.AppendLine("    {");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine($"    [NetMsg({_s2cMsgId}, {GetScopeCode()}, NetDir.S2C)]");
            sb.AppendLine($"    public sealed class S2C_{featureToken}Sync");
            sb.AppendLine("    {");
            sb.AppendLine("    }");
            sb.AppendLine("}");

            return sb.ToString();
        }

        private string BuildServerRoomComponentFileContent(string fullRootNamespace, string featureToken)
        {
            if (string.IsNullOrEmpty(fullRootNamespace) || string.IsNullOrEmpty(featureToken))
            {
                Debug.LogError("[StellarNetScaffoldWindow] BuildServerRoomComponentFileContent failed");
                return string.Empty;
            }

            string protocolNamespace = $"{fullRootNamespace}.Shared.Protocol";
            string serverNamespace = $"{fullRootNamespace}.Server.Components";
            string safeDisplayName = EscapeString(_displayName);

            var sb = new StringBuilder(4096);

            AppendAutoGeneratedHeader(sb, featureToken);

            sb.AppendLine("using StellarNet.Lite.Server.Core;");
            sb.AppendLine("using StellarNet.Lite.Shared.Core;");
            sb.AppendLine("using StellarNet.Lite.Shared.Infrastructure;");
            sb.AppendLine($"using {protocolNamespace};");
            sb.AppendLine();
            sb.AppendLine($"namespace {serverNamespace}");
            sb.AppendLine("{");
            sb.AppendLine($"    [RoomComponent({_componentId}, \"{featureToken}\", \"{safeDisplayName}\")]");
            sb.AppendLine($"    public sealed class Server{featureToken}Component : RoomComponent");
            sb.AppendLine("    {");
            sb.AppendLine("        private readonly ServerApp _app;");
            sb.AppendLine();
            sb.AppendLine($"        public Server{featureToken}Component(ServerApp app)");
            sb.AppendLine("        {");
            sb.AppendLine("            _app = app;");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine("        public override void OnInit()");
            sb.AppendLine("        {");
            sb.AppendLine("            if (Room == null || _app == null)");
            sb.AppendLine("            {");
            sb.AppendLine($"                NetLogger.LogError(\"Server{featureToken}Component\", \"OnInit failed\");");
            sb.AppendLine("                return;");
            sb.AppendLine("            }");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine("        [NetHandler]");
            sb.AppendLine($"        public void OnC2S_{featureToken}Req(Session session, C2S_{featureToken}Req msg)");
            sb.AppendLine("        {");
            sb.AppendLine("            if (session == null || msg == null || Room == null)");
            sb.AppendLine("            {");
            sb.AppendLine($"                NetLogger.LogError(\"Server{featureToken}Component\", \"Handle failed\");");
            sb.AppendLine("                return;");
            sb.AppendLine("            }");
            sb.AppendLine();
            sb.AppendLine($"            var syncMsg = new S2C_{featureToken}Sync();");
            sb.AppendLine("            Room.BroadcastMessage(syncMsg);");
            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine("}");

            return sb.ToString();
        }

        private string BuildClientRoomComponentFileContent(string fullRootNamespace, string featureToken)
        {
            if (string.IsNullOrEmpty(fullRootNamespace) || string.IsNullOrEmpty(featureToken))
            {
                Debug.LogError("[StellarNetScaffoldWindow] BuildClientRoomComponentFileContent failed");
                return string.Empty;
            }

            string protocolNamespace = $"{fullRootNamespace}.Shared.Protocol";
            string clientNamespace = $"{fullRootNamespace}.Client.Components";
            string safeDisplayName = EscapeString(_displayName);

            var sb = new StringBuilder(4096);

            AppendAutoGeneratedHeader(sb, featureToken);

            sb.AppendLine("using StellarNet.Lite.Client.Core;");
            sb.AppendLine("using StellarNet.Lite.Shared.Core;");
            sb.AppendLine("using StellarNet.Lite.Shared.Infrastructure;");
            sb.AppendLine($"using {protocolNamespace};");
            sb.AppendLine();
            sb.AppendLine($"namespace {clientNamespace}");
            sb.AppendLine("{");
            sb.AppendLine($"    [RoomComponent({_componentId}, \"{featureToken}\", \"{safeDisplayName}\")]");
            sb.AppendLine($"    public sealed class Client{featureToken}Component : ClientRoomComponent");
            sb.AppendLine("    {");
            sb.AppendLine("        private readonly ClientApp _app;");
            sb.AppendLine();
            sb.AppendLine($"        public Client{featureToken}Component(ClientApp app)");
            sb.AppendLine("        {");
            sb.AppendLine("            _app = app;");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine("        public override void OnInit()");
            sb.AppendLine("        {");
            sb.AppendLine("            if (Room == null || _app == null)");
            sb.AppendLine("            {");
            sb.AppendLine($"                NetLogger.LogError(\"Client{featureToken}Component\", \"OnInit failed\");");
            sb.AppendLine("                return;");
            sb.AppendLine("            }");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine("        [NetHandler]");
            sb.AppendLine($"        public void OnS2C_{featureToken}Sync(S2C_{featureToken}Sync msg)");
            sb.AppendLine("        {");
            sb.AppendLine("            if (msg == null || Room == null)");
            sb.AppendLine("            {");
            sb.AppendLine($"                NetLogger.LogError(\"Client{featureToken}Component\", \"Handle failed\");");
            sb.AppendLine("                return;");
            sb.AppendLine("            }");
            sb.AppendLine();
            sb.AppendLine("            Room.NetEventSystem.Broadcast(msg);");
            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine("}");

            return sb.ToString();
        }

        private string BuildServerModuleFileContent(string fullRootNamespace, string featureToken)
        {
            if (string.IsNullOrEmpty(fullRootNamespace) || string.IsNullOrEmpty(featureToken))
            {
                Debug.LogError("[StellarNetScaffoldWindow] BuildServerModuleFileContent failed");
                return string.Empty;
            }

            string protocolNamespace = $"{fullRootNamespace}.Shared.Protocol";
            string serverNamespace = $"{fullRootNamespace}.Server.Modules";
            string safeDisplayName = EscapeString(_displayName);

            var sb = new StringBuilder(4096);

            AppendAutoGeneratedHeader(sb, featureToken);

            sb.AppendLine("using StellarNet.Lite.Server.Core;");
            sb.AppendLine("using StellarNet.Lite.Shared.Core;");
            sb.AppendLine("using StellarNet.Lite.Shared.Infrastructure;");
            sb.AppendLine($"using {protocolNamespace};");
            sb.AppendLine();
            sb.AppendLine($"namespace {serverNamespace}");
            sb.AppendLine("{");
            sb.AppendLine($"    [ServerModule(\"Server{featureToken}Module\", \"{safeDisplayName}\")]");
            sb.AppendLine($"    public sealed class Server{featureToken}Module");
            sb.AppendLine("    {");
            sb.AppendLine("        private readonly ServerApp _app;");
            sb.AppendLine();
            sb.AppendLine($"        public Server{featureToken}Module(ServerApp app)");
            sb.AppendLine("        {");
            sb.AppendLine("            _app = app;");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine("        [NetHandler]");
            sb.AppendLine($"        public void OnC2S_{featureToken}Req(Session session, C2S_{featureToken}Req msg)");
            sb.AppendLine("        {");
            sb.AppendLine("            if (session == null || msg == null || _app == null)");
            sb.AppendLine("            {");
            sb.AppendLine($"                NetLogger.LogError(\"Server{featureToken}Module\", \"Handle failed\");");
            sb.AppendLine("                return;");
            sb.AppendLine("            }");
            sb.AppendLine();
            sb.AppendLine($"            var syncMsg = new S2C_{featureToken}Sync();");
            sb.AppendLine("            _app.SendMessageToSession(session, syncMsg);");
            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine("}");

            return sb.ToString();
        }

        private string BuildClientModuleFileContent(string fullRootNamespace, string featureToken)
        {
            if (string.IsNullOrEmpty(fullRootNamespace) || string.IsNullOrEmpty(featureToken))
            {
                Debug.LogError("[StellarNetScaffoldWindow] BuildClientModuleFileContent failed");
                return string.Empty;
            }

            string protocolNamespace = $"{fullRootNamespace}.Shared.Protocol";
            string clientNamespace = $"{fullRootNamespace}.Client.Modules";
            string safeDisplayName = EscapeString(_displayName);

            var sb = new StringBuilder(4096);

            AppendAutoGeneratedHeader(sb, featureToken);

            sb.AppendLine("using StellarNet.Lite.Client.Core;");
            sb.AppendLine("using StellarNet.Lite.Client.Core.Events;");
            sb.AppendLine("using StellarNet.Lite.Shared.Core;");
            sb.AppendLine("using StellarNet.Lite.Shared.Infrastructure;");
            sb.AppendLine($"using {protocolNamespace};");
            sb.AppendLine();
            sb.AppendLine($"namespace {clientNamespace}");
            sb.AppendLine("{");
            sb.AppendLine($"    [ClientModule(\"Client{featureToken}Module\", \"{safeDisplayName}\")]");
            sb.AppendLine($"    public sealed class Client{featureToken}Module");
            sb.AppendLine("    {");
            sb.AppendLine("        private readonly ClientApp _app;");
            sb.AppendLine();
            sb.AppendLine($"        public Client{featureToken}Module(ClientApp app)");
            sb.AppendLine("        {");
            sb.AppendLine("            _app = app;");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine("        [NetHandler]");
            sb.AppendLine($"        public void OnS2C_{featureToken}Sync(S2C_{featureToken}Sync msg)");
            sb.AppendLine("        {");
            sb.AppendLine("            if (msg == null || _app == null)");
            sb.AppendLine("            {");
            sb.AppendLine($"                NetLogger.LogError(\"Client{featureToken}Module\", \"Handle failed\");");
            sb.AppendLine("                return;");
            sb.AppendLine("            }");
            sb.AppendLine();
            sb.AppendLine("            GlobalTypeNetEvent.Broadcast(msg);");
            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine("}");

            return sb.ToString();
        }

        #endregion

        #region Manifest 构建

        private ScaffoldManifest BuildManifest(string featureToken, string fullRootNamespace, List<string> sourceFiles, List<string> generatedFiles)
        {
            if (string.IsNullOrEmpty(featureToken) || string.IsNullOrEmpty(fullRootNamespace))
            {
                Debug.LogError("[StellarNetScaffoldWindow] BuildManifest failed");
                return null;
            }

            if (sourceFiles == null || sourceFiles.Count == 0 || generatedFiles == null || generatedFiles.Count == 0)
            {
                Debug.LogError("[StellarNetScaffoldWindow] BuildManifest failed");
                return null;
            }

            return new ScaffoldManifest
            {
                FeatureName = featureToken,
                DisplayName = _displayName ?? string.Empty,
                NamespacePrefix = NormalizeNamespace(_namespacePrefix),
                BaseNamespace = DefaultBaseNamespace,
                FullRootNamespace = fullRootNamespace,
                OutputRoot = NormalizeAssetPath(_outputRoot),
                BusinessType = _businessType.ToString(),
                ComponentId = _businessType == ScaffoldBusinessType.RoomComponent ? _componentId : 0,
                C2SMsgId = _c2sMsgId,
                S2CMsgId = _s2cMsgId,
                SourceFiles = sourceFiles.ToArray(),
                GeneratedFiles = generatedFiles.ToArray(),
                Files = null
            };
        }

        private void UpgradeLegacyManifestInMemory(ScaffoldManifest manifest)
        {
            if (manifest == null)
            {
                Debug.LogError("[StellarNetScaffoldWindow] manifest 无效");
                return;
            }

            if ((manifest.SourceFiles == null || manifest.SourceFiles.Length == 0) && manifest.Files != null && manifest.Files.Length > 0)
            {
                manifest.SourceFiles = manifest.Files;
            }

            if (manifest.GeneratedFiles == null || manifest.GeneratedFiles.Length == 0)
            {
                string featureToken = manifest.FeatureName ?? string.Empty;
                if (!string.IsNullOrEmpty(featureToken))
                {
                    manifest.GeneratedFiles = BuildManagedGeneratedFilePaths(featureToken).ToArray();
                }
                else
                {
                    manifest.GeneratedFiles = Array.Empty<string>();
                }
            }

            if (string.IsNullOrEmpty(manifest.BaseNamespace))
            {
                manifest.BaseNamespace = DefaultBaseNamespace;
            }
        }

        private string[] GetSourceFiles(ScaffoldManifest manifest)
        {
            if (manifest == null)
            {
                return Array.Empty<string>();
            }

            if (manifest.SourceFiles != null && manifest.SourceFiles.Length > 0)
            {
                return manifest.SourceFiles;
            }

            if (manifest.Files != null && manifest.Files.Length > 0)
            {
                return manifest.Files;
            }

            return Array.Empty<string>();
        }

        private string[] GetGeneratedFiles(ScaffoldManifest manifest)
        {
            if (manifest == null)
            {
                return Array.Empty<string>();
            }

            if (manifest.GeneratedFiles != null && manifest.GeneratedFiles.Length > 0)
            {
                return manifest.GeneratedFiles;
            }

            string featureToken = manifest.FeatureName ?? string.Empty;
            if (string.IsNullOrEmpty(featureToken))
            {
                return Array.Empty<string>();
            }

            return BuildManagedGeneratedFilePaths(featureToken).ToArray();
        }

        #endregion

        #region 路径构建

        private string BuildProtocolFileAssetPath(string featureToken)
        {
            return NormalizeAssetPath(PathCombineSafe(_outputRoot, "Shared/Protocol", $"{featureToken}Protocols.cs"));
        }

        private string BuildServerRoomComponentFileAssetPath(string featureToken)
        {
            return NormalizeAssetPath(PathCombineSafe(_outputRoot, "Server/Components", $"Server{featureToken}Component.cs"));
        }

        private string BuildClientRoomComponentFileAssetPath(string featureToken)
        {
            return NormalizeAssetPath(PathCombineSafe(_outputRoot, "Client/Components", $"Client{featureToken}Component.cs"));
        }

        private string BuildServerModuleFileAssetPath(string featureToken)
        {
            return NormalizeAssetPath(PathCombineSafe(_outputRoot, "Server/Modules", $"Server{featureToken}Module.cs"));
        }

        private string BuildClientModuleFileAssetPath(string featureToken)
        {
            return NormalizeAssetPath(PathCombineSafe(_outputRoot, "Client/Modules", $"Client{featureToken}Module.cs"));
        }

        private string BuildManifestFolderAssetPath()
        {
            return NormalizeAssetPath(PathCombineSafe(_outputRoot, ManifestFolderName));
        }

        private string BuildManifestAssetPath(string featureToken)
        {
            return NormalizeAssetPath(PathCombineSafe(BuildManifestFolderAssetPath(), $"{featureToken}.json"));
        }

        private string BuildGeneratedProtocolMsgIdsFileAssetPath(string featureToken)
        {
            return NormalizeAssetPath(PathCombineSafe(GeneratedProtocolMsgIdsFolderPath, $"Generated_{featureToken}_MsgIds.cs"));
        }

        private string BuildGeneratedProtocolMetaFileAssetPath(string featureToken)
        {
            return NormalizeAssetPath(PathCombineSafe(GeneratedProtocolMetaFolderPath, $"Generated_{featureToken}_MessageMeta.cs"));
        }

        private string BuildGeneratedComponentIdsFileAssetPath(string featureToken)
        {
            return NormalizeAssetPath(PathCombineSafe(GeneratedComponentIdsFolderPath, $"Generated_{featureToken}_ComponentIds.cs"));
        }

        private string BuildGeneratedRoomBinderFileAssetPath(string featureToken)
        {
            return NormalizeAssetPath(PathCombineSafe(GeneratedRoomBinderFolderPath, $"Generated_{featureToken}_RoomBinder.cs"));
        }

        private string BuildGeneratedGlobalModuleBinderFileAssetPath(string featureToken)
        {
            return NormalizeAssetPath(PathCombineSafe(GeneratedGlobalModuleBinderFolderPath, $"Generated_{featureToken}_ModuleBinder.cs"));
        }

        private List<string> BuildManagedSourceFilePaths(string featureToken)
        {
            var result = new List<string>(4)
            {
                BuildProtocolFileAssetPath(featureToken)
            };

            if (_businessType == ScaffoldBusinessType.RoomComponent)
            {
                result.Add(BuildServerRoomComponentFileAssetPath(featureToken));
                result.Add(BuildClientRoomComponentFileAssetPath(featureToken));
            }
            else
            {
                result.Add(BuildServerModuleFileAssetPath(featureToken));
                result.Add(BuildClientModuleFileAssetPath(featureToken));
            }

            return result;
        }

        private List<string> BuildManagedGeneratedFilePaths(string featureToken)
        {
            var result = new List<string>(4)
            {
                BuildGeneratedProtocolMsgIdsFileAssetPath(featureToken),
                BuildGeneratedProtocolMetaFileAssetPath(featureToken)
            };

            if (_businessType == ScaffoldBusinessType.RoomComponent)
            {
                result.Add(BuildGeneratedComponentIdsFileAssetPath(featureToken));
                result.Add(BuildGeneratedRoomBinderFileAssetPath(featureToken));
            }
            else
            {
                result.Add(BuildGeneratedGlobalModuleBinderFileAssetPath(featureToken));
            }

            return result;
        }

        #endregion

        #region 生成辅助

        private void ResetDefaultsByBusinessType()
        {
            _baseNamespace = DefaultBaseNamespace;

            if (_businessType == ScaffoldBusinessType.RoomComponent)
            {
                if (_componentId <= 0)
                {
                    _componentId = DefaultRoomComponentId;
                }

                if (_c2sMsgId <= 0)
                {
                    _c2sMsgId = DefaultRoomC2SMsgId;
                }

                if (_s2cMsgId <= 0)
                {
                    _s2cMsgId = DefaultRoomS2CMsgId;
                }

                if (string.IsNullOrWhiteSpace(_displayName))
                {
                    _displayName = "新功能模块";
                }
            }
            else
            {
                _componentId = 0;

                if (_c2sMsgId <= 0 || _c2sMsgId == DefaultRoomC2SMsgId)
                {
                    _c2sMsgId = DefaultGlobalC2SMsgId;
                }

                if (_s2cMsgId <= 0 || _s2cMsgId == DefaultRoomS2CMsgId)
                {
                    _s2cMsgId = DefaultGlobalS2CMsgId;
                }

                if (string.IsNullOrWhiteSpace(_displayName))
                {
                    _displayName = "新全局模块";
                }
            }
        }

        private string GetScopeCode()
        {
            return _businessType == ScaffoldBusinessType.RoomComponent ? "NetScope.Room" : "NetScope.Global";
        }

        private void AppendAutoGeneratedHeader(StringBuilder sb, string featureToken)
        {
            if (sb == null)
            {
                Debug.LogError("[StellarNetScaffoldWindow] AppendAutoGeneratedHeader failed");
                return;
            }

            sb.AppendLine("// <auto-generated>");
            sb.AppendLine("// 由 StellarNetScaffoldWindow 自动生成。");
            sb.AppendLine($"// ScaffoldFeature: {featureToken}");
            sb.AppendLine($"// ScaffoldType: {_businessType}");
            sb.AppendLine("// 若你准备把该文件转为正式业务实现，请先在脚手架窗口执行“取消托管”。");
            sb.AppendLine("// </auto-generated>");
            sb.AppendLine();
        }

        private string StripAutoGeneratedHeader(string content)
        {
            if (string.IsNullOrEmpty(content))
            {
                return string.Empty;
            }

            const string startTag = "// <auto-generated>";
            const string endTag = "// </auto-generated>";

            int startIndex = content.IndexOf(startTag, StringComparison.Ordinal);
            int endIndex = content.IndexOf(endTag, StringComparison.Ordinal);

            if (startIndex < 0 || endIndex < 0 || endIndex < startIndex)
            {
                return content;
            }

            int endLineIndex = content.IndexOf('\n', endIndex);
            if (endLineIndex < 0)
            {
                return string.Empty;
            }

            string remaining = content.Substring(endLineIndex + 1);
            return remaining.TrimStart('\r', '\n');
        }

        #endregion

        #region 校验与目录选择

        private bool CanGenerate()
        {
            _baseNamespace = DefaultBaseNamespace;

            if (!IsValidNamespacePath(_baseNamespace))
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(_namespacePrefix) && !IsValidNamespacePath(_namespacePrefix))
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(_outputRoot))
            {
                return false;
            }

            string normalizedOutput = NormalizeAssetPath(_outputRoot);
            if (string.IsNullOrEmpty(normalizedOutput) || !normalizedOutput.StartsWith("Assets", StringComparison.Ordinal))
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(_featureName))
            {
                return false;
            }

            string featureToken = SanitizeFeatureToken(_featureName);
            if (string.IsNullOrEmpty(featureToken))
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(_displayName))
            {
                return false;
            }

            if (_businessType == ScaffoldBusinessType.RoomComponent && _componentId <= 0)
            {
                return false;
            }

            if (_c2sMsgId <= 0 || _s2cMsgId <= 0)
            {
                return false;
            }

            if (_c2sMsgId == _s2cMsgId)
            {
                return false;
            }

            return true;
        }

        private void SelectOutputFolder()
        {
            string selected = EditorUtility.OpenFolderPanel("选择业务脚手架输出目录", Application.dataPath, string.Empty);
            if (string.IsNullOrEmpty(selected))
            {
                return;
            }

            DirectoryInfo projectRoot = Directory.GetParent(Application.dataPath);
            if (projectRoot == null)
            {
                Debug.LogError("[StellarNetScaffoldWindow] 项目根目录无效");
                return;
            }

            string projectRootPath = projectRoot.FullName.Replace("\\", "/");
            string selectedPath = selected.Replace("\\", "/");

            if (!selectedPath.StartsWith(projectRootPath, StringComparison.Ordinal))
            {
                Debug.LogError("[StellarNetScaffoldWindow] 目录不在项目内");
                return;
            }

            string relativePath = selectedPath.Substring(projectRootPath.Length).TrimStart('/');
            if (string.IsNullOrEmpty(relativePath) || !relativePath.StartsWith("Assets", StringComparison.Ordinal))
            {
                Debug.LogError("[StellarNetScaffoldWindow] 目录不在 Assets 下");
                return;
            }

            _outputRoot = relativePath;
            RefreshManagedEntries();
            Repaint();
        }

        #endregion

        #region 文件读写

        private bool EnsureAssetDirectory(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
            {
                Debug.LogError("[StellarNetScaffoldWindow] EnsureAssetDirectory failed");
                return false;
            }

            string fullPath = AssetPathToFullPath(assetPath);
            if (string.IsNullOrEmpty(fullPath))
            {
                Debug.LogError("[StellarNetScaffoldWindow] EnsureAssetDirectory failed");
                return false;
            }

            if (!Directory.Exists(fullPath))
            {
                Directory.CreateDirectory(fullPath);
            }

            return true;
        }

        private bool WriteFile(string assetPath, string content)
        {
            if (string.IsNullOrEmpty(assetPath) || string.IsNullOrEmpty(content))
            {
                Debug.LogError("[StellarNetScaffoldWindow] WriteFile failed");
                return false;
            }

            string fullPath = AssetPathToFullPath(assetPath);
            if (string.IsNullOrEmpty(fullPath))
            {
                Debug.LogError("[StellarNetScaffoldWindow] WriteFile failed");
                return false;
            }

            string directory = Path.GetDirectoryName(fullPath);
            if (string.IsNullOrEmpty(directory))
            {
                Debug.LogError("[StellarNetScaffoldWindow] WriteFile failed");
                return false;
            }

            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(fullPath, content, new UTF8Encoding(false));
            return true;
        }

        private bool AssetFileExists(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
            {
                Debug.LogError("[StellarNetScaffoldWindow] AssetFileExists failed");
                return false;
            }

            string fullPath = AssetPathToFullPath(assetPath);
            if (string.IsNullOrEmpty(fullPath))
            {
                Debug.LogError("[StellarNetScaffoldWindow] AssetFileExists failed");
                return false;
            }

            return File.Exists(fullPath);
        }

        private void DeleteMetaFileIfExists(string fullPath)
        {
            if (string.IsNullOrEmpty(fullPath))
            {
                Debug.LogError("[StellarNetScaffoldWindow] DeleteMetaFileIfExists failed");
                return;
            }

            string metaPath = fullPath + ".meta";
            if (File.Exists(metaPath))
            {
                File.Delete(metaPath);
            }
        }

        #endregion

        #region 路径与命名辅助

        private string ComposeFullRootNamespace()
        {
            string prefix = NormalizeNamespace(_namespacePrefix);
            string root = DefaultBaseNamespace;

            if (string.IsNullOrEmpty(prefix))
            {
                return root;
            }

            return $"{prefix}.{root}";
        }

        private string NormalizeNamespace(string rawNamespace)
        {
            if (string.IsNullOrWhiteSpace(rawNamespace))
            {
                return string.Empty;
            }

            string normalized = rawNamespace.Trim();

            while (normalized.Contains("..", StringComparison.Ordinal))
            {
                normalized = normalized.Replace("..", ".", StringComparison.Ordinal);
            }

            return normalized.Trim('.');
        }

        private bool IsValidNamespacePath(string rawNamespace)
        {
            string normalized = NormalizeNamespace(rawNamespace);
            if (string.IsNullOrEmpty(normalized))
            {
                return false;
            }

            string[] segments = normalized.Split('.');
            if (segments == null || segments.Length == 0)
            {
                return false;
            }

            for (int i = 0; i < segments.Length; i++)
            {
                if (!IsValidIdentifier(segments[i]))
                {
                    return false;
                }
            }

            return true;
        }

        private bool IsValidIdentifier(string token)
        {
            if (string.IsNullOrEmpty(token))
            {
                return false;
            }

            if (!(char.IsLetter(token[0]) || token[0] == '_'))
            {
                return false;
            }

            for (int i = 1; i < token.Length; i++)
            {
                char c = token[i];
                if (!(char.IsLetterOrDigit(c) || c == '_'))
                {
                    return false;
                }
            }

            return true;
        }

        private string SanitizeFeatureToken(string rawFeatureName)
        {
            if (string.IsNullOrWhiteSpace(rawFeatureName))
            {
                return string.Empty;
            }

            string trimmed = rawFeatureName.Trim();
            var sb = new StringBuilder(trimmed.Length);

            for (int i = 0; i < trimmed.Length; i++)
            {
                char c = trimmed[i];
                if (char.IsLetterOrDigit(c) || c == '_')
                {
                    sb.Append(c);
                }
            }

            string result = sb.ToString();
            if (string.IsNullOrEmpty(result))
            {
                return string.Empty;
            }

            if (char.IsDigit(result[0]))
            {
                result = "_" + result;
            }

            return result;
        }

        private string EscapeString(string raw)
        {
            if (string.IsNullOrEmpty(raw))
            {
                return string.Empty;
            }

            return raw.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        private string NormalizeAssetPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            string normalized = path.Replace("\\", "/").Trim();
            while (normalized.Contains("//", StringComparison.Ordinal))
            {
                normalized = normalized.Replace("//", "/", StringComparison.Ordinal);
            }

            return normalized.TrimEnd('/');
        }

        private string AssetPathToFullPath(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
            {
                Debug.LogError("[StellarNetScaffoldWindow] AssetPathToFullPath failed");
                return string.Empty;
            }

            string normalizedAssetPath = NormalizeAssetPath(assetPath);
            if (!normalizedAssetPath.StartsWith("Assets", StringComparison.Ordinal))
            {
                return string.Empty;
            }

            DirectoryInfo projectRoot = Directory.GetParent(Application.dataPath);
            if (projectRoot == null)
            {
                Debug.LogError("[StellarNetScaffoldWindow] AssetPathToFullPath failed");
                return string.Empty;
            }

            return NormalizeAssetPath($"{projectRoot.FullName.Replace("\\", "/")}/{normalizedAssetPath}");
        }

        private string FullPathToAssetPath(string fullPath)
        {
            if (string.IsNullOrEmpty(fullPath))
            {
                Debug.LogError("[StellarNetScaffoldWindow] FullPathToAssetPath failed");
                return string.Empty;
            }

            string normalizedFullPath = fullPath.Replace("\\", "/");
            DirectoryInfo projectRoot = Directory.GetParent(Application.dataPath);
            if (projectRoot == null)
            {
                Debug.LogError("[StellarNetScaffoldWindow] FullPathToAssetPath failed");
                return string.Empty;
            }

            string projectRootPath = projectRoot.FullName.Replace("\\", "/");
            if (!normalizedFullPath.StartsWith(projectRootPath, StringComparison.Ordinal))
            {
                return string.Empty;
            }

            string relative = normalizedFullPath.Substring(projectRootPath.Length).TrimStart('/');
            return NormalizeAssetPath(relative);
        }

        private string PathCombineSafe(string left, string right)
        {
            if (string.IsNullOrEmpty(left))
            {
                return NormalizeAssetPath(right);
            }

            if (string.IsNullOrEmpty(right))
            {
                return NormalizeAssetPath(left);
            }

            return NormalizeAssetPath($"{left}/{right}");
        }

        private string PathCombineSafe(string part1, string part2, string part3)
        {
            return PathCombineSafe(PathCombineSafe(part1, part2), part3);
        }

        #endregion
    }
}
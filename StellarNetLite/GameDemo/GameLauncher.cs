using System;
using StellarNet.UI;
using StellarNet.View;
using StellarNet.Lite.Game.Client.Infrastructure;
using StellarNet.Lite.Shared.Infrastructure;
using UnityEngine;

public enum ENetMode
{
    None,
    Client,
    Server,
    Host
}

public class GameLauncher : MonoSingleton<GameLauncher>
{
    // 场景内唯一的网络入口。
    [SerializeField] private StellarNetMirrorManager netManager;

    public static StellarNetMirrorManager NetManager => Instance != null ? Instance.netManager : null;

    // 当前启动模式。
    public ENetMode netMode = ENetMode.None;

    // 仅表示客户端物理链路是否已连接到服务端。
    public bool IsClientConnectedServer { get; private set; }

    protected override void Awake()
    {
        base.Awake();

        // 客户端模式才初始化 UI 根节点。
        if (netMode != ENetMode.Server)
        {
            UIKit.Instance.Init();
        }

        if (netManager == null)
        {
            NetLogger.LogError("GameLauncher", $"Awake 初始化失败: netManager 未绑定, Object:{name}, NetMode:{netMode}");
        }
    }

    private void Start()
    {
        StellarNetMirrorManager.OnClientConnectedEvent += OnClientConnected;
        StellarNetMirrorManager.OnClientDisconnectedEvent += OnClientDisconnected;

        // 启动时根据模式自动拉起客户端、服务端或 Host。
        LauncherNetAsync(netMode);
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();

        StellarNetMirrorManager.OnClientConnectedEvent -= OnClientConnected;
        StellarNetMirrorManager.OnClientDisconnectedEvent -= OnClientDisconnected;
    }

    private void OnClientConnected()
    {
        // 客户端连上服务端后，打开全局网络监控和登录页。
        GlobalUIRouter.Instance.Init();
        UIKit.OpenPanel<Panel_GlobalNetMonitor>();
        UIKit.OpenPanel<Panel_StellarNetLogin>();
        IsClientConnectedServer = true;
        NetLogger.LogInfo("GameLauncher", "客户端已连接服务端");
    }

    private void OnClientDisconnected()
    {
        // 物理断开时统一交给全局 Router 做 UI 回退。
        if (GlobalUIRouter.Instance != null)
        {
            GlobalUIRouter.Instance.HandlePhysicalDisconnect();
        }

        IsClientConnectedServer = false;
        NetLogger.LogError("GameLauncher", "客户端已断开服务端");
    }

    private async void LauncherNetAsync(ENetMode eNetMode)
    {
        if (eNetMode == ENetMode.None)
        {
            NetLogger.LogWarning("GameLauncher", "启动中止: ENetMode 为 None");
            return;
        }

        if (netManager == null)
        {
            NetLogger.LogError("GameLauncher", $"启动失败: netManager 为空, NetMode:{eNetMode}, Object:{name}");
            return;
        }

        // 先异步加载配置，再把配置应用到 MirrorManager。
        NetConfig config = await NetConfigLoader.LoadAsync(ConfigRootPath.StreamingAssets);
        if (config == null)
        {
            NetLogger.LogError("GameLauncher", $"配置加载失败: 返回 config 为空, NetMode:{eNetMode}");
            return;
        }

        netManager.ApplyConfig(config);

        // 根据枚举切到不同启动链路。
        switch (eNetMode)
        {
            case ENetMode.Client:
                StartClient();
                break;
            case ENetMode.Server:
                StartServer();
                break;
            case ENetMode.Host:
                StartHost();
                break;
            default:
                NetLogger.LogError("GameLauncher", $"启动失败: 未知 NetMode:{eNetMode}");
                break;
        }
    }

    [ContextMenu("启动客户端")]
    public void StartClient()
    {
        if (netManager == null)
        {
            NetLogger.LogError("GameLauncher", $"启动客户端失败: netManager 为空, Object:{name}");
            return;
        }

        netManager.StartClient();
    }

    private void StartServer()
    {
        if (netManager == null)
        {
            NetLogger.LogError("GameLauncher", $"启动服务端失败: netManager 为空, Object:{name}");
            return;
        }

        netManager.StartServer();
    }

    private void StartHost()
    {
        if (netManager == null)
        {
            NetLogger.LogError("GameLauncher", $"启动 Host 失败: netManager 为空, Object:{name}");
            return;
        }

        netManager.StartHost();
    }
}

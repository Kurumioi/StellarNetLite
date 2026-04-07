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
    [SerializeField] private StellarNetMirrorManager netManager;
    public static StellarNetMirrorManager NetManager => Instance != null ? Instance.netManager : null;

    public ENetMode netMode = ENetMode.None;
    public bool IsClientConnectedServer { get; private set; }

    protected override void Awake()
    {
        base.Awake();
        if (netMode != ENetMode.Server)
        {
            UIKit.Instance.Init();
        }

        if (netManager == null) NetLogger.LogError("GameLauncher", "Awake 初始化失败: netManager 未绑定");
    }

    private void Start()
    {
        StellarNetMirrorManager.OnClientConnectedEvent += OnClientConnected;
        StellarNetMirrorManager.OnClientDisconnectedEvent += OnClientDisconnected;
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
        GlobalUIRouter.Instance.Init();
        RoomViewManager.Instance.Init(); // 修正：使用新的表现层管理器

        UIKit.OpenPanel<Panel_GlobalNetMonitor>();
        UIKit.OpenPanel<Panel_StellarNetLogin>();
        IsClientConnectedServer = true;
        NetLogger.LogInfo("GameLauncher", "客户端已连接服务端");
    }

    private void OnClientDisconnected()
    {
        if (GlobalUIRouter.Instance != null) GlobalUIRouter.Instance.HandlePhysicalDisconnect();
        IsClientConnectedServer = false;
        NetLogger.LogError("GameLauncher", "客户端已断开服务端");
    }

    private async void LauncherNetAsync(ENetMode eNetMode)
    {
        if (eNetMode == ENetMode.None || netManager == null) return;
        NetConfig config = await NetConfigLoader.LoadAsync(ConfigRootPath.StreamingAssets);
        if (config == null) return;
        netManager.ApplyConfig(config);
        switch (eNetMode)
        {
            case ENetMode.Client: StartClient(); break;
            case ENetMode.Server: StartServer(); break;
            case ENetMode.Host: StartHost(); break;
        }
    }

    [ContextMenu("启动客户端")]
    public void StartClient()
    {
        if (netManager != null) netManager.StartClient();
    }

    private void StartServer()
    {
        if (netManager != null) netManager.StartServer();
    }

    private void StartHost()
    {
        if (netManager != null) netManager.StartHost();
    }
}
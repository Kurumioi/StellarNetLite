using System;
using StellarNet.UI;
using StellarNet.View;
using StellarNet.Lite.Game.Client.Infrastructure;
using StellarNet.Lite.Shared.Infrastructure;
using StellarNet.Lite.Runtime;
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
    [SerializeField] private StellarNetAppManager appManager;
    public static StellarNetAppManager AppManager => Instance != null ? Instance.appManager : null;

    public ENetMode netMode = ENetMode.None;

    public bool IsClientConnectedServer { get; private set; }

    protected override void Awake()
    {
        base.Awake();
        if (netMode != ENetMode.Server)
        {
            UIKit.Instance.Init();
        }

        if (appManager == null)
        {
            NetLogger.LogError("GameLauncher", "Awake 初始化失败: appManager 未绑定");
        }
    }

    private void Start()
    {
        // 核心修改：监听 AppManager 抛出的物理连接事件
        StellarNetAppManager.OnClientConnectedEvent += OnClientConnected;
        StellarNetAppManager.OnClientDisconnectedEvent += OnClientDisconnected;

        LauncherNetAsync(netMode);
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        StellarNetAppManager.OnClientConnectedEvent -= OnClientConnected;
        StellarNetAppManager.OnClientDisconnectedEvent -= OnClientDisconnected;
    }

    private void OnClientConnected()
    {
        // 核心修复：必须先修改状态事实，再通知 UI 刷新！
        IsClientConnectedServer = true;

        GlobalUIRouter.Instance.Init();
        RoomViewManager.Instance.Init();
        UIKit.OpenPanel<Panel_GlobalNetMonitor>();
        UIKit.OpenPanel<Panel_StellarNetLogin>();

        NetLogger.LogInfo("GameLauncher", "客户端已连接服务端");
    }

    private void OnClientDisconnected()
    {
        // 核心修复：先修改状态事实，再通知 UI 刷新！
        IsClientConnectedServer = false;

        if (GlobalUIRouter.Instance != null)
        {
            GlobalUIRouter.Instance.HandlePhysicalDisconnect();
        }

        NetLogger.LogError("GameLauncher", "客户端已断开服务端");
    }

    private async void LauncherNetAsync(ENetMode eNetMode)
    {
        if (eNetMode == ENetMode.None || appManager == null) return;

        NetConfig config = await NetConfigLoader.LoadAsync(ConfigRootPath.StreamingAssets);
        if (config == null) return;

        appManager.ApplyConfig(config);

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
        if (appManager != null && appManager.Transport != null)
        {
            appManager.Transport.StartClient();
        }
    }

    private void StartServer()
    {
        if (appManager != null && appManager.Transport != null)
        {
            appManager.Transport.StartServer();
        }
    }

    private void StartHost()
    {
        if (appManager != null && appManager.Transport != null)
        {
            appManager.Transport.StartHost();
        }
    }
}
using System;
using StellarFramework;
using StellarFramework.Event;
using StellarFramework.UI;
using StellarNet.Lite.Client.Core;
using StellarNet.Lite.Client.Core.Events;
using StellarNet.Lite.Shared.Infrastructure;
using StellarNet.Lite.Shared.Protocol;
using StellarNet.Lite.Game.Client.Infrastructure;
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
    public static StellarNetMirrorManager NetManager => Instance.netManager;

    public ENetMode netMode = ENetMode.None;

    protected override void Awake()
    {
        base.Awake();
        if (netMode != ENetMode.Server)
        {
            UIKit.Instance.Init();
        }
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
        // 全局路由依然需要常驻，负责大厅和登录的流转
        GlobalUIRouter.Instance.Init();

        // 核心重构：移除了所有 EnsureRouter 相关的硬编码挂载。
        // 现在业务 UI 路由和 3D 表现层全部由对应的 ClientRoomComponent 在 OnInit 中动态生成。

        UIKit.OpenPanel<Panel_GlobalNetMonitor>();
        UIKit.OpenPanel<Panel_StellarNetLogin>();
    }

    private void OnClientDisconnected()
    {
        if (GlobalUIRouter.Instance != null)
        {
            GlobalUIRouter.Instance.HandlePhysicalDisconnect();
        }
    }

    private async void LauncherNetAsync(ENetMode eNetMode)
    {
        if (eNetMode == ENetMode.None) return;

        try
        {
            var config = await NetConfigLoader.LoadAsync(ConfigRootPath.StreamingAssets);
            if (NetManager != null)
            {
                NetManager.ApplyConfig(config);
            }
        }
        catch (Exception e)
        {
            NetLogger.LogError("GameLauncher", $"异步加载网络配置失败: {e.Message}");
        }

        switch (eNetMode)
        {
            case ENetMode.Client: StartClient(); break;
            case ENetMode.Server: StartServer(); break;
            case ENetMode.Host: StartHost(); break;
        }
    }

    [ContextMenu("启动客户端")]
    private void StartClient()
    {
        NetManager.StartClient();
    }

    private void StartServer()
    {
        NetManager.StartServer();
    }

    private void StartHost()
    {
        NetManager.StartHost();
    }
}
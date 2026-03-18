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
        if (netMode == ENetMode.Client)
        {
            UIKit.Instance.Init();
        }
    }

    private void Start()
    {
        StellarNetMirrorManager.OnClientConnectedEvent += OnClientConnected;
        StellarNetMirrorManager.OnClientDisconnectedEvent += OnClientDisconnected;
        LauncherNet(netMode);
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        StellarNetMirrorManager.OnClientConnectedEvent -= OnClientConnected;
        StellarNetMirrorManager.OnClientDisconnectedEvent -= OnClientDisconnected;
    }

    private void OnClientConnected()
    {
        // 核心修复 P0-3：初始化全局 UI 路由
        ClientUIRouter.Instance.Init();

        UIKit.OpenPanel<Panel_GlobalNetMonitor>();
        UIKit.OpenPanel<Panel_StellarNetLogin>();
    }

    private void OnClientDisconnected()
    {
        // 核心修复 P0-3：将断线 UI 清理职责移交 Router
        if (ClientUIRouter.Instance != null)
        {
            ClientUIRouter.Instance.HandlePhysicalDisconnect();
        }
    }

    private void LauncherNet(ENetMode eNetMode)
    {
        if (eNetMode == ENetMode.None) return;
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
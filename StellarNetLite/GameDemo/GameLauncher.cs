using System;
using StellarFramework;
using StellarFramework.Event;
using StellarFramework.UI;
using StellarNet.Lite.Client.Core;
using StellarNet.Lite.Client.Core.Events;
using StellarNet.Lite.Shared.Infrastructure;
using StellarNet.Lite.Shared.Protocol;
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

    private ClientAppState _lastClientState = ClientAppState.InLobby;

    private void Start()
    {
        StellarNetMirrorManager.OnClientConnectedEvent += OnClientConnected;
        StellarNetMirrorManager.OnClientDisconnectedEvent += OnClientDisconnected;
        LauncherNet(netMode);
    }

    private void Update()
    {
        if (NetManager != null && NetManager.ClientApp != null)
        {
            var currentState = NetManager.ClientApp.State;

            // 核心防御：全面接管状态机跌落监控，实现绝对安全的 UI 路由回退
            bool isDroppedFromRoom = (_lastClientState == ClientAppState.OnlineRoom || _lastClientState == ClientAppState.ConnectionSuspended)
                                     && currentState == ClientAppState.InLobby;

            if (isDroppedFromRoom)
            {
                LogKit.LogWarning("[GameLauncher]", "检测到网络状态从房间/挂起态跌落，执行 UI 路由回退");
                UIKit.ClosePanel<Panel_StellarNetRoom>();

                if (NetManager.ClientApp.Session.IsLoggedIn)
                {
                    UIKit.OpenPanel<Panel_StellarNetLobby>();
                }
                else
                {
                    UIKit.ClosePanel<Panel_StellarNetLobby>();
                    UIKit.OpenPanel<Panel_StellarNetLogin>();
                }
            }

            _lastClientState = currentState;
        }
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        StellarNetMirrorManager.OnClientConnectedEvent -= OnClientConnected;
        StellarNetMirrorManager.OnClientDisconnectedEvent -= OnClientDisconnected;
    }

    private void OnClientConnected()
    {
        UIKit.Instance.Init();
        UIKit.OpenPanel<Panel_GlobalNetMonitor>();
        UIKit.OpenPanel<Panel_StellarNetLogin>();
    }

    /// <summary>
    ///  当客户端断开连接时触发
    /// </summary>
    private void OnClientDisconnected()
    {
        // 核心修复 1：物理断开（包含主动登出和被动断网）时，彻底重置 UI 栈，退回登录
        LogKit.LogWarning("[GameLauncher]", "客户端物理断开，执行全局 UI 清理并退回登录");
        UIKit.CloseAllPanels();

        // 重新拉起常驻监控和登录面板
        UIKit.OpenPanel<Panel_GlobalNetMonitor>();
        UIKit.OpenPanel<Panel_StellarNetLogin>();
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

    public static void ClientSendMessage<T>(T msg) where T : class
    {
        NetManager.ClientApp.SendMessage(msg);
    }
}
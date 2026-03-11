# StellarNet Lite 快速接入指南 (手把手实战版)
> 面向第一次接入 StellarNet Lite 的开发者  
> 目标：**从 0 到 1，手把手带你开发全局模块、房间业务组件，并摆脱 Demo 制作属于你自己的正式游戏启动器。**
---
## 目录
- [1. 核心心智与开发原则 (必读)](#1-核心心智与开发原则-必读)
- [2. 实战一：开发一个全局模块 (大厅广播)](#2-实战一开发一个全局模块-大厅广播)
- [3. 实战二：开发一个房间业务组件 (房间表情与统计)](#3-实战二开发一个房间业务组件-房间表情与统计)
- [4. 实战三：摆脱 DemoUI，制作你自己的游戏启动器](#4-实战三摆脱-demoui制作你自己的游戏启动器)
- [5. 回放与重连是怎么生效的？(原理解析)](#5-回放与重连是怎么生效的原理解析)
- [6. 避坑指南与终极排障手册](#6-避坑指南与终极排障手册)
---
## 1. 核心心智与开发原则 (必读)
在写下第一行代码前，请将以下三句话刻在脑子里：
1. **服务端才是真相**：客户端绝不能自己修改核心数据，只能“发请求 -> 等服务端广播 -> 刷新表现”。
2. **拒绝巨石类，拥抱组件化与自动装配**：新增房间玩法，绝对不要去改核心类，而是新建独立的 `RoomComponent` 并打上特性，让框架自动扫描装配。
3. **MSV 架构解耦**：View（MonoBehaviour）只负责播特效和点按钮，绝不能直接解析网络包。网络包必须由 ClientComponent 接收，并**直接将协议对象抛给 View**。
---
## 2. 实战一：开发一个全局模块 (大厅广播)
**需求描述**：玩家在大厅点击按钮，发送一条全服广播，所有在线玩家的控制台都会打印这条消息。

### 步骤 1：定义共享协议 (Shared)
在 `Assets/StellarNetLite/Runtime/Shared/Protocol/` 下新建 `GlobalBroadcastProtocols.cs`。
```csharp
using StellarNet.Lite.Shared.Core;
namespace StellarNet.Lite.Shared.Protocol
{
    [NetMsg(800, NetScope.Global, NetDir.C2S)]
    public sealed class C2S_GlobalBroadcastReq { public string Content; }

    [NetMsg(801, NetScope.Global, NetDir.S2C)]
    public sealed class S2C_GlobalBroadcastSync { public string SenderSessionId; public string Content; }
}
```

### 步骤 2：编写服务端模块 (Server)
在 `Assets/StellarNetLite/Runtime/Server/Modules/` 下新建 `ServerBroadcastModule.cs`。
**核心：必须打上 `[GlobalModule]` 特性，并提供接收 `ServerApp` 的单参构造函数。**
```csharp
using StellarNet.Lite.Shared.Core;
using StellarNet.Lite.Shared.Protocol;
using StellarNet.Lite.Server.Core;
using StellarNet.Lite.Shared.Infrastructure;

namespace StellarNet.Lite.Server.Modules
{
    [GlobalModule("ServerBroadcastModule", "全服广播模块")]
    public sealed class ServerBroadcastModule
    {
        private readonly ServerApp _app;
        public ServerBroadcastModule(ServerApp app) { _app = app; }

        [NetHandler]
        public void OnC2S_GlobalBroadcastReq(Session session, C2S_GlobalBroadcastReq msg)
        {
            if (session == null || msg == null || string.IsNullOrEmpty(msg.Content)) return;
            var syncMsg = new S2C_GlobalBroadcastSync { SenderSessionId = session.SessionId, Content = msg.Content };
            foreach (var kvp in _app.Sessions)
            {
                if (kvp.Value.IsOnline) _app.SendMessageToSession(kvp.Value, syncMsg);
            }
        }
    }
}
```

### 步骤 3：编写客户端模块 (Client)
在 `Assets/StellarNetLite/Runtime/Client/Modules/` 下新建 `ClientBroadcastModule.cs`。
```csharp
using StellarNet.Lite.Shared.Core;
using StellarNet.Lite.Shared.Protocol;
using StellarNet.Lite.Client.Core;
using StellarNet.Lite.Client.Core.Events;

namespace StellarNet.Lite.Client.Modules
{
    [GlobalModule("ClientBroadcastModule", "客户端全服广播")]
    public sealed class ClientBroadcastModule
    {
        private readonly ClientApp _app;
        public ClientBroadcastModule(ClientApp app) { _app = app; }

        [NetHandler]
        public void OnS2C_GlobalBroadcastSync(S2C_GlobalBroadcastSync msg)
        {
            if (msg == null) return;
            GlobalTypeNetEvent.Broadcast(msg); // 0GC 直抛
        }
    }
}
```

### 步骤 4：一键自动装配 (极度重要)
**绝对不要去修改 `StellarNetMirrorManager.cs`！**
回到 Unity 编辑器，点击顶部菜单：
`StellarNet/Lite 强制重新生成协议与组件常量表`
控制台会提示生成成功。此时，你的模块已经被自动装配到网络引擎中了！

### 步骤 5：表现层接入 (View)
在任意 UI 脚本中监听事件并发送请求。
```csharp
using UnityEngine;
using StellarNet.Lite.Shared.Protocol;
using StellarNet.Lite.Client.Core.Events;
using StellarNet.Lite.Shared.Infrastructure;

public class BroadcastView : MonoBehaviour
{
    private void Start()
    {
        GlobalTypeNetEvent.Register<S2C_GlobalBroadcastSync>(HandleGlobalBroadcast)
            .UnRegisterWhenGameObjectDestroyed(gameObject);
    }

    private void HandleGlobalBroadcast(S2C_GlobalBroadcastSync evt)
    {
        LiteLogger.LogInfo("BroadcastView", $"<color=cyan>[全服广播] {evt.SenderSessionId}: {evt.Content}</color>");
    }
}
```

---
## 3. 实战二：开发一个房间业务组件 (房间表情与统计)
**需求描述**：玩家在房间内发表情。要求回放可见，且重连能恢复表情总数。

### 步骤 1：定义协议 (Shared)
```csharp
using StellarNet.Lite.Shared.Core;
namespace StellarNet.Lite.Shared.Protocol
{
    [NetMsg(900, NetScope.Room, NetDir.C2S)]
    public sealed class C2S_SendEmojiReq { public int EmojiId; }
    
    [NetMsg(901, NetScope.Room, NetDir.S2C)]
    public sealed class S2C_EmojiSync { public string SessionId; public int EmojiId; }
    
    [NetMsg(902, NetScope.Room, NetDir.S2C)]
    public sealed class S2C_EmojiSnapshot { public int TotalEmojiCount; }
}
```

### 步骤 2：编写服务端组件 (Server - 含重连原理)
**核心：打上 `[RoomComponent]` 特性，声明组件 ID。**
```csharp
using StellarNet.Lite.Shared.Core;
using StellarNet.Lite.Shared.Protocol;
using StellarNet.Lite.Server.Core;

namespace StellarNet.Lite.Server.Components
{
    [RoomComponent(2, "RoomEmoji", "房间表情组件")]
    public sealed class ServerEmojiComponent : RoomComponent
    {
        private readonly ServerApp _app;
        private int _totalEmojiCount = 0;
        
        public ServerEmojiComponent(ServerApp app) { _app = app; }
        public override void OnInit() { _totalEmojiCount = 0; }

        [NetHandler]
        public void OnC2S_SendEmojiReq(Session session, C2S_SendEmojiReq msg)
        {
            if (session == null || msg == null) return;
            _totalEmojiCount++;
            var syncMsg = new S2C_EmojiSync { SessionId = session.SessionId, EmojiId = msg.EmojiId };
            Room.BroadcastMessage(syncMsg); // 广播会自动录入 Replay
        }

        public override void OnSendSnapshot(Session session)
        {
            if (session == null) return;
            var snapshot = new S2C_EmojiSnapshot { TotalEmojiCount = _totalEmojiCount };
            Room.SendMessageTo(session, snapshot); // 定向发送快照
        }
    }
}
```

### 步骤 3：编写客户端组件 (Client)
```csharp
using StellarNet.Lite.Shared.Core;
using StellarNet.Lite.Shared.Protocol;
using StellarNet.Lite.Client.Core;

namespace StellarNet.Lite.Client.Components
{
    [RoomComponent(2, "RoomEmoji", "房间表情组件")]
    public sealed class ClientEmojiComponent : ClientRoomComponent
    {
        private readonly ClientApp _app;
        public int LocalTotalCount { get; private set; }
        
        public ClientEmojiComponent(ClientApp app) { _app = app; }
        public override void OnInit() { LocalTotalCount = 0; }

        [NetHandler]
        public void OnS2C_EmojiSync(S2C_EmojiSync msg)
        {
            if (msg == null) return;
            LocalTotalCount++;
            Room.NetEventSystem.Broadcast(msg); // 房间内直抛协议
        }

        [NetHandler]
        public void OnS2C_EmojiSnapshot(S2C_EmojiSnapshot msg)
        {
            if (msg == null) return;
            LocalTotalCount = msg.TotalEmojiCount;
            Room.NetEventSystem.Broadcast(msg);
        }
    }
}
```

### 步骤 4：一键自动装配
点击顶部菜单：`StellarNet/Lite 强制重新生成协议与组件常量表`。
此时，`ComponentIdConst.RoomEmoji` 常量已被自动生成，且组件已注入工厂。

### 步骤 5：建房与表现层接入
在建房时，使用自动生成的常量挂载组件：
```csharp
var createReq = new C2S_CreateRoom 
{ 
    RoomName = "我的专属房间", 
    ComponentIds = new int[] 
    { 
        ComponentIdConst.RoomSettings, 
        ComponentIdConst.RoomEmoji // 挂载刚才写的表情组件
    } 
};
_manager.ClientApp.SendMessage(createReq);
```
在 View 层监听事件（记得使用 `_boundRoom.NetEventSystem.Register` 并收集令牌注销）。

---
## 4. 实战三：摆脱 DemoUI，制作你自己的游戏启动器
*(此部分内容保持不变，详见原文档)*

## 5. 回放与重连是怎么生效的？(原理解析)
*(此部分内容保持不变，详见原文档)*

## 6. 避坑指南与终极排障手册
### 坑 1：发了请求没反应？
- **检查 1**：协议类上有没有加 `[NetMsg]`？
- **检查 2**：Server 端的 Handler 方法有没有加 `[NetHandler]`？
- **检查 3**：**有没有点击菜单重新生成常量表？** 只有重新生成后，`AutoRegistry` 才会把你的模块装配进去！

### 坑 2：建房成功了，但进不去房间？
- **原因**：你写了 `RoomEmoji` 组件，但**忘记点击菜单重新生成常量表**了，导致客户端本地工厂里没有这个组件。
- **排查**：看客户端日志，一定会有一句红错：“`[ClientRoomFactory] 装配致命失败: 本地未注册 ComponentId X`”。
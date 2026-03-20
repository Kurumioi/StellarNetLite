# StellarNet Lite 快速接入指南（手把手实战版）

> 面向第一次接入 StellarNet Lite 的开发者  
> 目标：**从 0 到 1，手把手带你开发全局模块、房间业务组件，掌握高频协议序列化与 UI 生命周期管理，并摆脱 Demo 逻辑做出你自己的正式游戏启动器。**

---

## 目录

- [1. 核心心智与开发原则（必读）](#1-核心心智与开发原则必读)
- [2. 实战一：开发一个全局模块（大厅广播）](#2-实战一开发一个全局模块大厅广播)
- [3. 实战二：开发一个房间业务组件（房间表情与统计）](#3-实战二开发一个房间业务组件房间表情与统计)
- [4. 实战三：摆脱 DemoUI，制作你自己的游戏启动器](#4-实战三摆脱-demoui制作你自己的游戏启动器)
- [5. 回放与重连是怎么生效的（原理解析）](#5-回放与重连是怎么生效的原理解析)
- [6. 当前版本接入建议](#6-当前版本接入建议)
- [7. 避坑指南与终极排障手册](#7-避坑指南与终极排障手册)

---

## 1. 核心心智与开发原则（必读）

在写下第一行代码前，请先把下面三句话记牢：

### 1.1 服务端才是真相

客户端绝不能自己修改核心数据，只能：

**发请求 -> 等服务端广播 -> 刷新表现**

不管是：

- 房间成员状态
- 对局开始 / 结束
- 怪物 HP
- 玩家位置
- 动画状态

权威都必须在服务端。

### 1.2 拒绝巨石类，拥抱组件化与自动装配

新增房间玩法时，绝对不要去改核心类，而是：

- 新建独立 `RoomComponent`
- 打上特性
- 通过编辑器扫描生成装配代码

框架希望你优先横向扩展，而不是把逻辑堆进一个越来越大的 `Room` 或 `Manager`。

### 1.3 MSV 解耦与事件直抛

View（MonoBehaviour / UIPanel）只负责：

- 播特效
- 点按钮
- 刷新文本
- 响应输入

网络包必须由：

- `ClientModule`
- `ClientRoomComponent`

接收并抛给 View。

View 监听时务必使用：

- `.UnRegisterWhenMonoDisable(this)`
- 或 `.UnRegisterWhenGameObjectDestroyed(gameObject)`

否则 UI 隐藏后仍会在后台响应网络事件。

---

## 2. 实战一：开发一个全局模块（大厅广播）

**需求描述**：玩家在大厅点击按钮，发送一条全服广播，所有在线玩家的控制台都会打印这条消息。

---

### 步骤 1：定义共享协议（Shared）

在 `Assets/StellarNetLite/Runtime/Shared/Protocol/` 下新建：

`GlobalBroadcastProtocols.cs`

```csharp
using StellarNet.Lite.Shared.Core;

namespace StellarNet.Lite.Shared.Protocol
{
    [NetMsg(800, NetScope.Global, NetDir.C2S)]
    public sealed class C2S_GlobalBroadcastReq
    {
        public string Content;
    }

    [NetMsg(801, NetScope.Global, NetDir.S2C)]
    public sealed class S2C_GlobalBroadcastSync
    {
        public string SenderSessionId;
        public string Content;
    }
}
```

---

### 步骤 2：编写服务端模块（Server）

在 `Assets/StellarNetLite/Runtime/Server/Modules/` 下新建：

`ServerBroadcastModule.cs`

**关键要求**：

- 必须打上 `[ServerModule]`
- 必须提供接收 `ServerApp` 的单参构造函数
- Handler 方法必须打 `[NetHandler]`

```csharp
using StellarNet.Lite.Shared.Core;
using StellarNet.Lite.Shared.Protocol;
using StellarNet.Lite.Server.Core;
using StellarNet.Lite.Shared.Infrastructure;

namespace StellarNet.Lite.Server.Modules
{
    [ServerModule("ServerBroadcastModule", "全服广播模块")]
    public sealed class ServerBroadcastModule
    {
        private readonly ServerApp _app;

        public ServerBroadcastModule(ServerApp app)
        {
            _app = app;
        }

        [NetHandler]
        public void OnC2S_GlobalBroadcastReq(Session session, C2S_GlobalBroadcastReq msg)
        {
            if (session == null || msg == null || string.IsNullOrWhiteSpace(msg.Content))
            {
                NetLogger.LogError("ServerBroadcastModule", $"处理广播失败: 参数非法, Session:{session?.SessionId ?? "null"}");
                return;
            }

            var syncMsg = new S2C_GlobalBroadcastSync
            {
                SenderSessionId = session.SessionId,
                Content = msg.Content.Trim()
            };

            foreach (var kvp in _app.Sessions)
            {
                Session target = kvp.Value;
                if (target == null || !target.IsOnline)
                {
                    continue;
                }

                _app.SendMessageToSession(target, syncMsg);
            }
        }
    }
}
```

---

### 步骤 3：编写客户端模块（Client）

在 `Assets/StellarNetLite/Runtime/Client/Modules/` 下新建：

`ClientBroadcastModule.cs`

```csharp
using StellarNet.Lite.Shared.Core;
using StellarNet.Lite.Shared.Protocol;
using StellarNet.Lite.Client.Core;
using StellarNet.Lite.Client.Core.Events;

namespace StellarNet.Lite.Client.Modules
{
    [ClientModule("ClientBroadcastModule", "客户端全服广播")]
    public sealed class ClientBroadcastModule
    {
        private readonly ClientApp _app;

        public ClientBroadcastModule(ClientApp app)
        {
            _app = app;
        }

        [NetHandler]
        public void OnS2C_GlobalBroadcastSync(S2C_GlobalBroadcastSync msg)
        {
            if (msg == null)
            {
                return;
            }

            GlobalTypeNetEvent.Broadcast(msg);
        }
    }
}
```

---

### 步骤 4：生成静态装配表（极度重要）

回到 Unity 编辑器，点击顶部菜单：

- `StellarNetLite/强制重新生成协议与组件常量表`

这一步会驱动生成：

- `MsgIdConst`
- `AutoMessageMetaRegistry`
- `AutoRegistry`
- 对应的 Module Binder 分片

> **不要手动去改 `AutoRegistry.cs`。**  
> 这是生成产物，不是人工维护入口。

---

### 步骤 5：表现层接入（View）

在任意 UI 脚本中监听事件并发送请求：

```csharp
using UnityEngine;
using StellarNet.Lite.Shared.Protocol;
using StellarNet.Lite.Client.Core.Events;
using StellarNet.Lite.Shared.Infrastructure;
using StellarNet.Lite.Client.Core;

public class BroadcastView : MonoBehaviour
{
    private void OnEnable()
    {
        GlobalTypeNetEvent.Register<S2C_GlobalBroadcastSync>(HandleGlobalBroadcast)
            .UnRegisterWhenMonoDisable(this);
    }

    private void HandleGlobalBroadcast(S2C_GlobalBroadcastSync evt)
    {
        NetLogger.LogInfo("BroadcastView", $"[全服广播] {evt.SenderSessionId}: {evt.Content}");
    }

    public void SendBroadcast(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            Debug.LogError($"[BroadcastView] 发送失败: content 为空, Object:{name}");
            return;
        }

        NetClient.Send(new C2S_GlobalBroadcastReq
        {
            Content = content.Trim()
        });
    }
}
```

---

## 3. 实战二：开发一个房间业务组件（房间表情与统计）

**需求描述**：玩家在房间内发表情。要求：

- 高频发送时尽量轻量
- 回放可见
- 重连能恢复表情总数

---

### 步骤 1：定义协议（Shared）

由于表情可能高频发送，建议实现 `ILiteNetSerializable`。

```csharp
using System.IO;
using StellarNet.Lite.Shared.Core;
using StellarNet.Lite.Shared.Infrastructure;

namespace StellarNet.Lite.Shared.Protocol
{
    [NetMsg(900, NetScope.Room, NetDir.C2S)]
    public sealed class C2S_SendEmojiReq : ILiteNetSerializable
    {
        public int EmojiId;

        public void Serialize(BinaryWriter writer)
        {
            writer.Write(EmojiId);
        }

        public void Deserialize(BinaryReader reader)
        {
            EmojiId = reader.ReadInt32();
        }
    }

    [NetMsg(901, NetScope.Room, NetDir.S2C)]
    public sealed class S2C_EmojiSync : ILiteNetSerializable
    {
        public string SessionId;
        public int EmojiId;

        public void Serialize(BinaryWriter writer)
        {
            writer.Write(SessionId ?? string.Empty);
            writer.Write(EmojiId);
        }

        public void Deserialize(BinaryReader reader)
        {
            SessionId = reader.ReadString();
            EmojiId = reader.ReadInt32();
        }
    }

    [NetMsg(902, NetScope.Room, NetDir.S2C)]
    public sealed class S2C_EmojiSnapshot
    {
        public int TotalEmojiCount;
    }
}
```

---

### 步骤 2：编写服务端组件（Server）

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
        private int _totalEmojiCount;

        public ServerEmojiComponent(ServerApp app)
        {
            _app = app;
        }

        public override void OnInit()
        {
            _totalEmojiCount = 0;
        }

        [NetHandler]
        public void OnC2S_SendEmojiReq(Session session, C2S_SendEmojiReq msg)
        {
            if (session == null || msg == null)
            {
                return;
            }

            if (Room == null)
            {
                return;
            }

            _totalEmojiCount++;

            var syncMsg = new S2C_EmojiSync
            {
                SessionId = session.SessionId,
                EmojiId = msg.EmojiId
            };

            Room.BroadcastMessage(syncMsg);
        }

        public override void OnSendSnapshot(Session session)
        {
            if (session == null || Room == null)
            {
                return;
            }

            Room.SendMessageTo(session, new S2C_EmojiSnapshot
            {
                TotalEmojiCount = _totalEmojiCount
            });
        }
    }
}
```

---

### 步骤 3：编写客户端组件（Client）

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

        public ClientEmojiComponent(ClientApp app)
        {
            _app = app;
        }

        public override void OnInit()
        {
            LocalTotalCount = 0;
        }

        [NetHandler]
        public void OnS2C_EmojiSync(S2C_EmojiSync msg)
        {
            if (msg == null || Room == null)
            {
                return;
            }

            LocalTotalCount++;
            Room.NetEventSystem.Broadcast(msg);
        }

        [NetHandler]
        public void OnS2C_EmojiSnapshot(S2C_EmojiSnapshot msg)
        {
            if (msg == null || Room == null)
            {
                return;
            }

            LocalTotalCount = msg.TotalEmojiCount;
            Room.NetEventSystem.Broadcast(msg);
        }
    }
}
```

---

### 步骤 4：生成装配表

点击：

- `StellarNetLite/强制重新生成协议与组件常量表`

此时会生成：

- `ComponentIdConst.RoomEmoji`
- 对应 Room Binder 分片
- 协议元数据注册分片

---

### 步骤 5：建房接入

当前底层仍然是组件清单装配，但 UI 层建议通过房间模板组织。  
如果你只是临时测试，也可以先直接传组件列表：

```csharp
var createReq = new C2S_CreateRoom
{
    RoomName = "我的专属房间",
    ComponentIds = new int[]
    {
        ComponentIdConst.RoomSettings,
        ComponentIdConst.RoomEmoji
    }
};

NetClient.Send(createReq);
```

如果你希望正式接入到建房面板，建议把它封装进：

- `RoomTypeTemplateRegistry`

让建房界面看到的是“表情房间”“社交房间”这类业务模板，而不是裸组件列表。

---

## 4. 实战三：摆脱 DemoUI，制作你自己的游戏启动器

当前 Demo 里，入口主要依赖：

- `GameLauncher`
- `GlobalUIRouter`
- `Panel_StellarNetLogin`
- `Panel_StellarNetLobby`
- `Panel_StellarNetReplay`

如果你要换成自己的项目入口，建议按照下面思路处理：

### 4.1 保留底层，不保留外观

你可以完全替换 Demo 的：

- 登录面板
- 大厅面板
- 房间列表 UI
- 回放面板

但尽量保留这几个“底层事实入口”：

- `NetClient.Send(...)`
- `GlobalTypeNetEvent`
- `GlobalUIRouter`
- `Room.NetEventSystem`

### 4.2 你真正需要接管的，是“导航外壳”

自己项目里你通常只需要重新实现：

- 启动场景
- 登录界面
- 大厅界面
- 建房界面
- 回放列表界面

不需要去改：

- `ClientApp`
- `ServerApp`
- `AutoRegistry`
- `NetMessageMapper`

### 4.3 最佳实践

- 全局导航继续交给 `GlobalUIRouter`
- 房间内 UI 继续走组件路由
- 不要在你的新 UI 里直接硬连底层 `Transport`

---

## 5. 回放与重连是怎么生效的（原理解析）

### 5.1 流式回放

当前版本的回放不是简单的“把一串历史消息重播”。

它实际包含两层数据：

#### 第一层：普通消息帧

例如：

- 成员加入
- 游戏开始
- 气泡消息
- 各类业务同步

#### 第二层：对象关键帧

例如：

- 当前场景中所有对象的完整生成态
- 每个对象的 `PrefabHash`
- Transform / Animator 所需完整字段

这样做的原因是：

- 普通消息帧适合重演业务事件
- 对象关键帧适合 Seek 时快速恢复对象世界

所以当前回放播放器支持：

- 从头播放
- 暂停
- 倍速
- `Seek`
- 利用关键帧恢复对象世界，再补后续消息

### 5.2 重连

当前重连流程大致是：

1. 在线房间中物理断开
2. 客户端进入 `ConnectionSuspended`
3. 自动重试连接
4. 重连成功后重新发 `C2S_Login`
5. 服务端识别旧账号映射到旧 `Session`
6. 返回 `S2C_LoginResult.HasReconnectRoom = true`
7. 客户端确认是否接受重连
8. 服务端返回 `S2C_ReconnectResult`
9. 客户端重建房间组件
10. 客户端发 `C2S_ReconnectReady`
11. 服务端触发各组件 `OnSendSnapshot()`

---

## 6. 当前版本接入建议

### 6.1 所有新增协议后第一件事，不是运行，是生成

每次新增：

- 协议
- 房间组件
- 全局模块

后，请先点：

- `StellarNetLite/强制重新生成协议与组件常量表`

### 6.2 新增网络预制体后，必须生成 Hash 表

每次新增 `Resources/NetPrefabs/` 下的网络预制体后，请点：

- `StellarNetLite/生成网络预制体常量表 (Net Prefabs)`

### 6.3 高频逻辑优先二进制化

例如：

- 移动输入
- 对象同步
- 回放分块
- 高频战斗请求

建议优先实现 `ILiteNetSerializable`。

### 6.4 房间入口优先做成模板，不要直接裸露组件选择

当前工程已经有 `RoomTypeTemplateRegistry`。  
如果是正式项目，尽量让策划和 UI 操作的是：

- 交友房
- 棋牌房
- 副本房

而不是：

- 勾选 `ObjectSync`
- 勾选 `RoomSettings`
- 勾选 `SocialRoom`

---

## 7. 避坑指南与终极排障手册

### 坑 1：发了请求没反应

检查：

1. 协议类上有没有加 `[NetMsg]`
2. Handler 方法有没有加 `[NetHandler]`
3. 有没有点菜单重新生成常量表

只有重新生成后，静态装配器才知道你的新协议和模块。

---

### 坑 2：建房成功了，但进不去房间

典型原因：

- 你写了组件，但没重新生成装配表
- 客户端本地没注册该 `ComponentId`

客户端日志一般会直接报：

- `未注册的 ComponentId`
- `本地装配失败`

---

### 坑 3：关闭 UI 面板后后台疯狂报错

原因：

- UI 面板隐藏了
- 但没有注销事件监听
- 收到包时仍在刷新已隐藏对象

解决：

- `OnEnable` 注册事件
- 链式调用 `.UnRegisterWhenMonoDisable(this)`

---

### 坑 4：新增协议后 `NetMessageMapper` 找不到

原因通常不是协议本身错了，而是：

- 你忘了重新生成 `AutoMessageMetaRegistry`

请直接点击：

- `StellarNetLite/强制重新生成协议与组件常量表`

---

### 坑 5：新增网络预制体后客户端报未知 Hash

检查：

1. 预制体是否放在 `Resources/NetPrefabs/`
2. 是否点击了  
   `StellarNetLite/生成网络预制体常量表 (Net Prefabs)`

---

### 坑 6：回放 Seek 后对象世界不对

检查：

1. 对象相关玩法是否接入了 `ServerObjectSyncComponent`
2. 房间是否支持对象关键帧录制
3. 客户端是否存在 `ClientObjectSyncComponent`
4. 对象生成与恢复是否都依赖统一的 `ObjectSpawnState`

---

### 坑 7：在线 UI 跑到了回放里

检查：

1. `ClientRoomComponent.OnInit()` 里是不是根据 `ClientAppState` 正确挂接 Router
2. 是否误把 `OnlineUIRouter` 复用到了 `ReplayRoom`

---

## 总结

这份快速接入指南真正想传达的不是“把 Demo 跑起来”，而是：

- **你应该怎么按框架口径接入**
- **你应该怎么扩展，而不是怎么乱改**
- **你应该怎么让项目后续还能维护**

如果你记住下面三句，这份指南的核心就已经吸收了：

1. **所有真相留在服务端**
2. **所有新增业务优先做成独立组件 / 模块**
3. **所有新增协议、组件、预制体后，都先生成静态表再运行**
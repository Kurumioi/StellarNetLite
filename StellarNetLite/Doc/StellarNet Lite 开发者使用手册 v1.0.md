# StellarNet Lite 开发者使用手册

2026年3月31日修订

> 面向中小型 Unity 商业项目的轻量级房间式网络框架  
> 核心目标：**服务端绝对权威、协议事件驱动、房间组件化、流式回放沙盒、客户端表现解耦、底层传输防腐**

---

## 目录

- [框架定位](#框架定位)
- [核心特性](#核心特性)
- [适用边界与承载预估](#适用边界与承载预估)
- [总体架构](#总体架构)
- [目录结构](#目录结构)
- [快速启动](#快速启动)
- [全流程阅读指引](#全流程阅读指引)
- [基础 API 调用指引（核心流转）](#基础-api-调用指引核心流转)
- [核心开发流程](#核心开发流程)
- [通信与状态流转](#通信与状态流转)
- [房间与回放机制](#房间与回放机制)
- [当前版本开发建议（极度重要）](#当前版本开发建议极度重要)

---

## 框架定位

StellarNet Lite 不是一个追求“黑盒自动同步”的网络方案。  
它的设计目标非常明确：

1. **保证服务端权威**
2. **保证协议流转清晰可控**
3. **保证房间作用域隔离**
4. **保证回放、重连、断线恢复这些高风险场景可控**
5. **保证业务功能可以横向扩展，不把代码堆进巨石类**
6. **保证低 GC 开销与底层网络库的无缝替换**

这套框架的核心哲学只有一句话：

> **客户端只发请求和播放结果，服务端才是真相。**

---

## 核心特性

### 1. 强类型发送器与内存池发包

业务层不直接手拼 `Packet`，统一通过强类型发送入口完成发包。

- 客户端：`NetClient.Send<T>()`
- 服务端：`ServerApp.SendMessageToSession<T>()`
- 房间内广播：`Room.BroadcastMessage<T>()`
- 房间内单播：`Room.SendMessageTo<T>()`

**底层优化**：

- 发送时自动从 `System.Buffers.ArrayPool` 借用内存
- 高频协议建议实现 `ILiteNetSerializable`
- 支持 `Packet.PayloadOffset` 与底层切片解析
- 房间广播可选择是否录入 Replay

---

### 2. NetMessageMapper 静态元数据驱动

当前版本不再依赖 Runtime 反射扫描协议。

实际流程是：

- Editor 扫描所有带 `[NetMsg]` 的协议类型
- 生成 `AutoMessageMetaRegistry`
- Runtime 调用 `NetMessageMapper.Initialize()`
- 直接读取静态生成表

这意味着业务层只需要关心“发送什么对象”，不需要关心“这个对象应该用哪个协议号”。

---

### 3. Shared / Server / Client 物理分层与防腐层

框架严格分为三层：

- **Shared**
    - 共享协议
    - 基础结构
    - 混合序列化抽象
    - `INetworkTransport` 传输层防腐接口
    - 回放关键帧共享结构

- **Server**
    - 服务端权威逻辑
    - 房间容器
    - 会话
    - 流式录制
    - GC
    - 重连

- **Client**
    - 状态机
    - 协议接入
    - 轻状态缓存
    - 回放控制
    - 表现桥接
    - 事件系统

---

### 4. Global / Room 作用域分离

框架内部把所有网络消息严格分成两种作用域：

- **Global**
    - 不依赖房间上下文的逻辑
    - 如登录、大厅列表、建房、录像下载

- **Room**
    - 必须依赖某个房间上下文的逻辑
    - 如战斗同步、房间聊天、准备状态、对象同步

---

### 5. 房间组件化装配

房间不是靠一个巨大的 `RoomLogic.cs` 处理所有事情，而是采用 **Room Component 横向扩展模式**。

通过在类上标记：

- `[RoomComponent(Id, Name)]`

框架会自动生成常量表和装配器。建房时通过传入 `ComponentIds` 数组动态装配。

当前工程还额外把“建房入口”收敛成了 **房间模板语义**：

- UI 侧优先面向 `RoomTypeTemplateRegistry`
- 模板内部再映射成一组组件清单
- 避免最终用户直接接触底层组件拼装细节

---

### 6. 两阶段装配与失败回滚

服务端和客户端房间组件装配都采用统一原则：

- 先校验
- 再创建组件
- 再绑定
- 最后初始化

任一阶段失败，整体回滚，避免产生半残房间实例。

---

### 7. 无侵入式事件直抛与沙盒隔离

客户端内部采用两类事件系统，支持直接抛出协议对象：

- `GlobalTypeNetEvent`
    - 大厅、登录、录像列表等全局事件
- `RoomNetEventSystem`
    - 挂在 `ClientRoom` 实例上的房间级事件总线

这能彻底避免回放房间和在线房间串线。

同时提供生命周期绑定：

- `.UnRegisterWhenGameObjectDestroyed(gameObject)`
- `.UnRegisterWhenMonoDisable(this)`

用于防止 UI 隐藏后继续响应网络事件。

---

### 8. 流式 Replay 沙盒回放

回放是一个**客户端本地沙盒房间**，不是在线房间附加模式。

当前回放系统包含三部分：

#### 录制

- 服务端按房间广播录制协议帧
- 使用 `GZipStream` 边录边压
- 写入回放文件
- 支持对象关键帧写入

#### 下载

- 客户端发起分块下载请求
- 服务端使用 `FileStream` 按块读取
- 客户端写入 `.tmp`
- 支持断点续传
- 下载完成后转为 `.replay`

#### 播放

- 客户端创建本地 `ReplayRoom`
- `.replay` 解压出 `.raw`
- `BinaryReader` 读取消息帧
- 支持对象关键帧 `ReplayObjectSnapshotFrame`
- 支持 `Seek` / 倍速 / 暂停

这样做的核心收益是：

- 回放与在线房间绝对隔离
- Seek 不用总是从头补播
- 对象世界可以快速恢复
- 大文件播放更稳定

---

### 9. Seq 防重放机制

客户端每次发包自动递增 `Seq`，服务端按 `Session` 记录 `LastReceivedSeq`。  
收到旧包或重复包会直接拦截，防止重复点击或网络重试污染。

---

## 适用边界与承载预估

**承载预估（单服节点）**：

- **并发规模**：100 ~ 300 CCU
- **房间规模**：
    - 4 ~ 16 人：高频空间 / 动画同步
    - 50 人以内：低频状态同步

这套框架更适合：

- 中小型房间制联机项目
- 社交 / 棋牌 / 休闲竞技
- 需要长期维护与多人协作的商业项目

不适合直接拿来承载：

- MMO 级超大地图同步
- 回滚格斗
- 强预测纠正型 FPS / MOBA 底座
- 强反作弊大规模服务治理

---

## 总体架构

### 服务端主链路

`INetworkTransport` 抛出底层字节流  
-> `ServerApp.OnReceivePacket()`  
-> `Seq` 防重放  
-> 按 `Scope` 路由  
-> 对应 `Module / RoomComponent` 处理  
-> 修改权威状态  
-> 服务端申请 `ArrayPool` 发送 `S2C`  
-> 同步写入 Replay 流

### 客户端主链路

`INetworkTransport` 抛出底层字节流  
-> `ClientApp.OnReceivePacket()`  
-> 按 `Scope` 路由  
-> 对应 `ClientModule / ClientRoomComponent` 处理  
-> 通过 `GlobalTypeNetEvent` 或 `Room.NetEventSystem` 抛出协议对象  
-> View 层监听并刷新表现

---

## 目录结构

```text
Assets/StellarNetLite/
├── Runtime/
│   ├── Shared/
│   │   ├── Core/
│   │   ├── Protocol/
│   │   ├── Infrastructure/
│   │   ├── Replay/
│   │   └── Binders/
│   ├── Server/
│   ├── Client/
│   └── StellarNetMirrorManager.cs
├── Editor/
│   ├── LiteProtocolScanner.cs
│   ├── NetPrefabScanner.cs
│   ├── NetConfigEditorWindow.cs
│   └── StellarNetScaffoldWindow.cs
├── GameDemo/
└── Doc/
```

---

## 快速启动

### 1. 运行前准备

确保工程内已安装：

- Mirror
- Newtonsoft.Json

并确保测试场景中存在：

- `GameLauncher`
- `StellarNetMirrorManager`

### 2. 协议与装配表生成

每次新增：

- 协议
- 房间组件
- 全局模块

后，都必须点击菜单：

- `StellarNetLite/强制重新生成协议与组件常量表`

如果新增了网络预制体，则还要点击：

- `StellarNetLite/生成网络预制体常量表 (Net Prefabs)`

否则你会遇到：

- 本地未注册 `ComponentId`
- `NetMessageMapper` 找不到协议元数据
- 自动装配缺失
- 未知 `PrefabHash`

### 3. 网络配置

通过菜单：

- `StellarNetLite/网络配置 (NetConfig)`

可以编辑 `NetConfig`。

当前支持的典型配置包括：

- IP
- Port
- MaxConnections
- TickRate
- 房间最大寿命
- 录像保留数
- 离线 GC 时间
- 最低客户端版本

---

## 全流程阅读指引

如果你现在最需要的是：

1. 从空工程视角理解 Editor 生成链
2. 看懂客户端和服务端消息如何一步步流转
3. 了解建房、进房、快照、开局、回放、重连是怎么串起来的

请继续阅读：

- `Doc/StellarNet Lite 从0到1全流程开发说明.md`

这份文档专门按时间顺序讲整条链路，适合新同事入场、项目交接、二次开发前统一认知。

---

## 基础 API 调用指引（核心流转）

### 1. 建立物理连接与发起登录

```csharp
var manager = GameLauncher.NetManager;
if (manager == null) return;

manager.StartClient();

var loginReq = new C2S_Login
{
    AccountId = "Player_001",
    ClientVersion = Application.version
};

NetClient.Send(loginReq);
```

### 2. 创建与加入房间

当前底层依然是 `ComponentIds` 驱动装配，但推荐业务入口通过**房间模板**组织。

示例：

```csharp
var createReq = new C2S_CreateRoom
{
    RoomName = "我的专属房间",
    ComponentIds = new int[]
    {
        ComponentIdConst.RoomSettings,
        ComponentIdConst.SocialRoom,
        ComponentIdConst.ObjectSync
    }
};

NetClient.Send(createReq);

var joinReq = new C2S_JoinRoom
{
    RoomId = "Room_123456"
};

NetClient.Send(joinReq);
```

### 3. 离开房间

```csharp
if (NetClient.State == ClientAppState.OnlineRoom)
{
    NetClient.Send(new C2S_LeaveRoom());
}
```

---

## 核心开发流程

### 新增一个房间玩法功能

标准步骤如下：

1. 定义 Shared 协议：`[NetMsg]`
2. 高频协议优先实现 `ILiteNetSerializable`
3. 编写服务端 `RoomComponent`
4. 编写客户端 `ClientRoomComponent`
5. 客户端收到协议后，按需 `Room.NetEventSystem.Broadcast(msg)`
6. 点击菜单 `StellarNetLite/强制重新生成协议与组件常量表`
7. 若该玩法需要从建房入口暴露，则把组件加入 `RoomTypeTemplateRegistry`
8. 视图层监听事件做表现
9. 若玩法有 UI，优先同时补 `OnlineUIRouter / ReplayUIRouter`

### 使用脚手架生成模板

菜单入口：

- `StellarNetLite/业务脚手架生成器`

可生成：

- Shared 协议
- Server 组件 / 模块
- Client 组件 / 模块
- manifest 托管信息
- 对应生成分片文件

脚手架不是终局实现，它的价值在于：

- 帮你遵守当前工程口径
- 避免手工漏写 Attribute
- 降低新业务接入成本

---

## 通信与状态流转

### 客户端发包链

View 采集输入  
-> `NetClient.Send<T>()`  
-> 查元数据校验方向  
-> 递增 `Seq`  
-> 申请 `ArrayPool` 序列化  
-> 组装 `Packet`  
-> 交给 `INetworkTransport`

### 服务端收包链

`INetworkTransport`  
-> `ServerApp.OnReceivePacket()`  
-> `Session.TryConsumeSeq()`  
-> Global / Room 分发  
-> 权威业务处理  
-> 发 `S2C`

### 客户端收包链

`INetworkTransport`  
-> `ClientApp.OnReceivePacket()`  
-> Global / Room 分发  
-> 对应 Module / Component  
-> 事件直抛  
-> View 刷新

### 一个标准房间业务流转示例

`View` 点击按钮  
-> `NetClient.Send(C2S_xxx)`  
-> 服务端 Handler 校验  
-> 服务端修改状态  
-> `Room.BroadcastMessage(S2C_xxx)`  
-> 客户端组件接收  
-> `Room.NetEventSystem.Broadcast(msg)`  
-> UI / View 刷新表现

---

## 房间与回放机制

### 1. 在线房间

服务端建房 / 加房成功后，不会立刻下发快照，而是：

1. 服务端返回 `RoomId + ComponentIds`
2. 客户端本地装配房间组件
3. 客户端发送 `C2S_RoomSetupReady`
4. 服务端正式加入成员并下发快照

这样做的好处是：

- 不会出现“客户端组件还没装好，快照先到了”的问题
- 房间初始化链路更稳定
- 更适合组件化房间装配

### 2. 断线重连

重连链路大致如下：

1. 客户端物理断连
2. 若当前在在线房间，则进入 `ConnectionSuspended`
3. 自动重试物理连接
4. 重新发送登录鉴权
5. 服务端识别旧会话并返回可恢复房间
6. 客户端确认是否接受重连
7. 客户端重建在线房间组件
8. 客户端发送 `C2S_ReconnectReady`
9. 服务端触发 `OnSendSnapshot()` 定向恢复

### 3. 回放房间

回放不是在线房间加标记，而是：

- `ClientReplayPlayer` 创建本地回放沙盒
- `ClientApp.EnterReplayRoom`
- `ClientRoomFactory.BuildComponents`
- 播放历史消息帧
- 必要时通过对象关键帧恢复对象世界

也就是说：

> **回放是另一套本地房间实例，不和在线房间共用事件上下文。**

---

## 当前版本开发建议（极度重要）

### 1. 高频协议必须优先二进制化

对于：

- 摇杆移动
- 坐标同步
- 动画同步
- 录像分块

这类高频协议，建议实现 `ILiteNetSerializable`。  
否则 JSON 序列化会带来明显 GC 压力与体积膨胀。

### 2. 优先横向扩展 RoomComponent

新增房间功能时，优先新增独立组件，不要把功能堆进现有巨石类。

### 3. UI 必须防泄漏

View 层监听协议事件时，务必使用：

- `.UnRegisterWhenMonoDisable(this)`  
  或
- `.UnRegisterWhenGameObjectDestroyed(gameObject)`

### 4. 表现层坐标解耦

UI 气泡、血条等需要跟随 3D 物体的表现，优先通过：

- `UGUIFollowTarget`

处理，不要把 `WorldToScreenPoint` 写进业务逻辑。

### 5. 所有高风险入口先做前置拦截

例如：

- 判空
- RoomId 校验
- 状态机校验
- Session 合法性校验
- Payload 边界校验

统一原则：

> **先拦截，先报错，先 return，绝不带病继续。**

### 6. 不要绕过静态生成链

新增协议、组件、模块后，不要手改 `AutoRegistry`、`MsgIdConst`、`ComponentIdConst`。  
正确做法永远是：

- 写特性
- 点菜单生成

### 7. 回放功能开发要区分“消息帧”和“关键帧”

如果你的玩法依赖对象世界恢复，就要理解：

- 普通协议帧负责还原事件流
- 对象关键帧负责快速恢复对象完整态

这两者职责不同，不要混在一起设计。

### 8. 建房入口优先面向业务模板，而不是暴露底层组件

当前工程已经有 `RoomTypeTemplateRegistry`。  
面向产品或 UI 时，应优先讲“交友房间”“打怪房间”这种业务模板，而不是让用户直接勾选底层组件列表。

---

## 总结

StellarNet Lite 当前已经形成了一套比较完整的中小型房间制联机底座，它真正的价值不在某一个单点功能，而在于以下工程链已经能闭环：

- **服务端权威**
- **协议事件驱动**
- **房间组件化**
- **Editor 扫描生成**
- **Runtime 静态装配**
- **客户端事件直抛**
- **UI 路由分层**
- **流式 Replay 沙盒**
- **对象关键帧恢复**
- **断线重连**
- **GC 与治理工具**

如果你后续继续扩展这个框架，请始终守住三条底线：

1. **不要把真相放到客户端**
2. **不要把业务堆进巨石类**
3. **不要让回放、在线、UI 生命周期互相串线**

只要这三条不破，框架就能长期健康演进。

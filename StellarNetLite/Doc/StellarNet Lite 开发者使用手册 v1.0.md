# StellarNet Lite 开发者使用手册
2026年3月11日修订
> 面向中小型 Unity 商业项目的轻量级房间式网络框架  
> 核心目标：**服务端绝对权威、协议事件驱动、房间组件化、回放沙盒化、客户端表现解耦**
---
## 目录
- [框架定位](#框架定位)
- [核心特性](#核心特性)
- [适用边界](#适用边界)
- [总体架构](#总体架构)
- [目录结构](#目录结构)
- [快速启动](#快速启动)
- [基础 API 调用指引 (核心流转)](#基础-api-调用指引-核心流转)
- [核心开发流程](#核心开发流程)
- [通信与状态流转](#通信与状态流转)
- [房间与回放机制](#房间与回放机制)
- [当前版本开发建议](#当前版本开发建议)
- [文档入口](#文档入口)
---
## 框架定位
StellarNet Lite 不是一个追求“黑盒自动同步”的网络方案。  
它的设计目标非常明确：
1. **保证服务端权威**
2. **保证协议流转清晰可控**
3. **保证房间作用域隔离**
4. **保证回放、重连、断线恢复这些高风险场景可控**
5. **保证业务功能可以横向扩展，不把代码堆进巨石类**

这套框架的核心哲学只有一句话：
> **客户端只发请求和播放结果，服务端才是真相。**

---
## 核心特性

### 1. 强类型协议发送
业务层不直接手拼 `Packet`，统一通过强类型发送入口完成发包。
- 客户端：`ClientApp.SendMessage<T>()`
- 服务端：`ServerApp.SendMessageToSession<T>()`
- 房间内广播：`Room.BroadcastMessage<T>()`
- 房间内单播：`Room.SendMessageTo<T>()`

这套机制会自动处理：`MsgId`、`Scope`、`RoomId`、`Seq`、协议方向校验、房间上下文校验。

### 2. NetMessageMapper 元数据驱动
框架启动时扫描所有带 `[NetMsg]` 的协议类型，建立类型到协议元数据的映射。这意味着业务层只需要关心“发送什么对象”，不需要关心“这个对象应该用哪个魔数协议号”。

### 3. Shared / Server / Client 物理分层
框架严格分为三层：
- **Shared**：共享协议、基础结构、序列化抽象、工具 (绝对纯净，无表现层事件)
- **Server**：服务端权威逻辑、房间容器、会话、录制、GC、重连
- **Client**：客户端状态机、协议接入、轻状态缓存、回放控制、表现桥接、事件系统

### 4. Global / Room 作用域分离
框架内部把所有网络消息严格分成两种作用域：
- **Global**：不依赖房间上下文的逻辑（如登录、大厅列表、建房）。
- **Room**：必须依赖某个房间上下文的逻辑（如战斗同步、房间聊天）。

### 5. 房间组件化装配
房间不是靠一个巨大的 `RoomLogic.cs` 处理所有事情，而是采用 **Room Component** 横向扩展模式。
通过在类上标记 `[RoomComponent(Id, Name)]` 特性，框架会自动生成常量表。建房时通过传入 `ComponentIds` 数组动态装配。

### 6. 两阶段装配与失败回滚
服务端和客户端房间组件装配都采用统一原则：先全量校验 -> 再统一挂载与绑定 -> 最后统一初始化。任一阶段失败，整体回滚，保证不会产生“半残房间实例”。

### 7. 无侵入式事件直抛与沙盒隔离
客户端内部采用两类事件系统，支持直接抛出协议对象，0GC 且无需 DTO 转换：
- `GlobalTypeEvent`：大厅、登录、录像列表等全局事件，基于泛型静态类，极速派发。
- `RoomEventSystem`：挂载在 `ClientRoom` 实例上的房间级事件总线。彻底避免回放房间和在线房间串线。

两者均支持 `.UnRegisterWhenGameObjectDestroyed(gameObject)` 链式调用，实现优雅的生命周期管理。

### 8. Replay 沙盒回放
回放是一个**客户端本地沙盒房间**。回放时：不接收真实在线房间广播、不发送真实网络包、使用录像文件本地装配房间组件、按 Tick 顺序重放历史房间消息。

### 9. Seq 防重放机制
客户端每次发包自动递增 `Seq`，服务端按 Session 记录 `LastReceivedSeq`。收到旧包或重复包直接拦截，防止重复点击或网络重试污染。

### 10. 结构化日志
提供统一日志入口 `LiteLogger`，统一输出模块名、RoomId、SessionId 等上下文，便于多人联机、回放、断线等场景快速排查。

---
## 适用边界
**适合：**
- 轻中量级多人合作游戏 / 房间制 PVE / 桌游棋牌
- 100~200 连接规模的中小型商业项目
- 需要回放、断线重连、房间组件化管理的项目

**不适合：**
- 高竞技强对抗 PVP / 强预测回滚 / 严苛帧级命中公平判定

---
## 总体架构

### 服务端主链路
客户端请求 -> `MirrorPacketMsg` -> `ServerApp.OnReceivePacket()` -> 按 `Scope` 路由 -> 对应 `Module / RoomComponent` 处理 -> 修改权威状态 -> 服务端发送 `S2C` -> 客户端接收同步。

### 客户端主链路
收到服务端协议 -> `ClientApp.OnReceivePacket()` -> 按 `Scope` 路由 -> 对应 `ClientModule / ClientRoomComponent` 处理 -> 直接通过 `GlobalTypeEvent` 或 `Room.EventSystem` 抛出协议对象 -> View 层监听协议并刷新表现。

---
## 目录结构
```text
Assets/StellarNetLite
├── Runtime
│   ├── Shared        (共享协议、核心契约)
│   ├── Server        (权威逻辑、房间生命周期、GC治理)
│   ├── Client        (状态机、事件系统、回放沙盒)
│   └── StellarNetMirrorManager.cs (网络总入口)
├── Editor            (协议扫描器、配置面板、脚手架)
└── GameDemo          (示例业务代码与测试 UI)
```

---
## 快速启动
1. **导入依赖**：确保工程内已安装 Mirror 与 Newtonsoft.Json。
2. **场景挂载**：在启动场景中挂载 `StellarNetMirrorManager` 脚本。
3. **配置网络**：点击菜单 `StellarNet/Lite 网络配置 (NetConfig)`，配置 IP、端口、TickRate 等参数并保存。
4. **运行测试**：使用项目内置的 `StellarNetDemoUI`，可直接在 Editor 中测试 Host、Server Only 或 Client Only 模式。
5. **生成常量表**：新增协议或组件后，点击菜单 `StellarNet/Lite 强制重新生成协议与组件常量表`。

---
## 基础 API 调用指引 (核心流转)
在正式脱离 Demo UI 进行业务开发时，你需要通过代码直接驱动框架。以下是客户端最核心的网络流转 API 调用范例。

### 1. 建立物理连接与发起登录
网络连接建立后，必须发送登录协议进行会话鉴权。
```csharp
// 获取核心网络管理器引用
var manager = NetworkManager.singleton as StellarNetMirrorManager;
if (manager == null) return;

// 1. 建立物理连接 (通常绑定在 UI 按钮上)
manager.StartClient();

// 2. 发送登录请求 (必须携带 ClientVersion 供服务端进行版本拦截)
var loginReq = new C2S_Login 
{ 
    AccountId = "Player_001", 
    ClientVersion = Application.version 
};
manager.ClientApp.SendMessage(loginReq);
```

### 2. 状态轮询与界面跳转
客户端不应直接监听底层的登录成功协议，而是通过轮询 `ClientApp.Session.IsLoggedIn` 状态机来驱动 UI 跳转。
```csharp
private void Update()
{
    var app = _manager?.ClientApp;
    if (app != null && app.Session.IsLoggedIn)
    {
        // 登录成功，关闭登录面板，打开大厅面板
        // ...
    }
}
```

### 3. 创建与加入房间
建房时，通过传入 `ComponentIds` 数组来声明该房间需要挂载哪些业务组件。
```csharp
// 创建房间：使用自动生成的常量表装配组件，拒绝魔法数字
var createReq = new C2S_CreateRoom 
{ 
    RoomName = "我的专属房间", 
    ComponentIds = new int[] 
    { 
        ComponentIdConst.RoomSettings, 
        ComponentIdConst.DemoGame 
    } 
};
manager.ClientApp.SendMessage(createReq);

// 加入已有房间
var joinReq = new C2S_JoinRoom { RoomId = "Room_123456" };
manager.ClientApp.SendMessage(joinReq);
```

### 4. 离开房间
离开房间同样通过强类型发送器完成，底层会自动处理状态机回滚与本地实例销毁。
```csharp
// 前置拦截：确保当前处于在线房间状态
if (manager.ClientApp.State == ClientAppState.OnlineRoom)
{
    manager.ClientApp.SendMessage(new C2S_LeaveRoom());
}
```

*(注：关于如何从 0 到 1 编写完整的 Launcher 启动器，请查阅《StellarNet Lite 快速接入指南》的实战三章节。)*

---
## 核心开发流程

### 新增一个房间玩法功能
标准步骤如下：
1. 定义 Shared 协议 (`[NetMsg]`)
2. 编写服务端 `RoomComponent` (`[RoomComponent]`)
3. 编写客户端 `ClientRoomComponent` (`[RoomComponent]`)，收到协议直接 `Room.EventSystem.Broadcast(msg)`
4. 在 `StellarNetMirrorManager` 中注册双端组件工厂
5. 在建房时把 `ComponentId` 加入 `ComponentIds`
6. View 层监听 `Room.EventSystem` 抛出的协议事件并做表现

### 使用脚手架生成业务模板
打开菜单：`StellarNet/Lite 业务脚手架 (Scaffold)`
可一键生成：Shared 协议、Server 组件/模块、Client 组件/模块，默认遵循强类型发送器与事件总线规范。

---
## 通信与状态流转

### 标准请求-同步流程
标准业务链路必须遵守：
客户端发 `C2S` -> 服务端校验 -> 服务端修改权威状态 -> 服务端发 `S2C` -> 客户端接收同步 -> 客户端直接抛出协议事件 -> View 刷新表现。

### 为什么拒绝自动同步
框架明确不依赖 SyncVar 或自动字段镜像。因为这些方案在多人房间、重连、回放、局部恢复场景下很容易失去可追踪性。框架坚持：**所有状态变化必须通过显式协议事件流转。**

---
## 房间与回放机制

### 房间加入为什么分两段握手
建房/加房成功后，服务端不会立刻下发快照，而是：
1. 返回房间信息和 `ComponentIds`
2. 客户端本地装配房间组件
3. 客户端发送 `C2S_RoomSetupReady`
4. 服务端再正式加入成员并下发快照

目的：防止客户端组件还没绑完，服务端房间快照已经下发，导致进入半初始化状态。

### 回放为什么必须单独状态机隔离
回放房间和在线房间必须物理隔离（`ClientAppState.ReplayRoom`），底层阻断回放中接收真实房间包或发送在线请求，防止状态互相覆盖。

### 回放为什么要“销毁重建 + 快进”
当前同步模型是**事件流重演**。Seek 回退时不能安全逆推状态，正确做法是：销毁当前回放沙盒 -> 重建本地房间 -> 从 Tick 0 极速快进到目标 Tick，保证状态绝对纯净。

---
## 当前版本开发建议
1. **新业务统一走强类型发送器**：绝对不要手写 `new Packet(...)`，统一使用 `ClientApp.SendMessage<T>()` 和 `Room.BroadcastMessage<T>()`。
2. **优先横向扩展 RoomComponent**：新增房间功能时，优先新增独立组件，不要把功能堆进现有的巨石类中。
3. **View 享受直抛红利**：View 直接监听 `S2C_` 协议，并务必使用 `.UnRegisterWhenGameObjectDestroyed(gameObject)` 确保生命周期安全。
4. **所有高风险入口先做前置拦截**：例如判空、RoomId 校验、状态机校验。统一原则：**先拦截，先报错，先 return，绝不带病继续。**

---
## 文档入口
- **了解框架能力边界**：请阅读 `Docs/StellarNet Literal 功能说明文档.md`
- **新手实战与代码编写**：请重点阅读 `Docs/StellarNet Lite 快速接入指南.md`，内含从 0 到 1 的完整组件开发实战。
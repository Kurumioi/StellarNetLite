# StellarNet Lite 开发者使用手册
2026年3月18日修订
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
- [基础 API 调用指引 (核心流转)](#基础-api-调用指引-核心流转)
- [核心开发流程](#核心开发流程)
- [通信与状态流转](#通信与状态流转)
- [房间与回放机制](#房间与回放机制)
- [当前版本开发建议 (极度重要)](#当前版本开发建议-极度重要)

---

## 框架定位
StellarNet Lite 不是一个追求“黑盒自动同步”的网络方案。  
它的设计目标非常明确：
1. **保证服务端权威**
2. **保证协议流转清晰可控**
3. **保证房间作用域隔离**
4. **保证回放、重连、断线恢复这些高风险场景可控**
5. **保证业务功能可以横向扩展，不把代码堆进巨石类**
6. **保证极低的 GC 开销与底层网络库的无缝替换**

这套框架的核心哲学只有一句话：
> **客户端只发请求和播放结果，服务端才是真相。**

---

## 核心特性

### 1. 强类型发送器与 0GC 内存池
业务层不直接手拼 `Packet`，统一通过强类型发送入口完成发包。
- 客户端：`NetClient.Send<T>()`
- 服务端：`ServerApp.SendMessageToSession<T>()`
- 房间内广播：`Room.BroadcastMessage<T>()`
- 房间内单播：`Room.SendMessageTo<T>()`

**底层优化**：发送时自动从 `System.Buffers.ArrayPool` 借用内存。对于高频协议，强烈建议实现 `ILiteNetSerializable` 接口，配合底层的 `INetSerializer` 完成纯二进制的 0GC 序列化，彻底消除高频发包带来的 JSON 字符串分配与内存抖动。

### 2. NetMessageMapper 元数据驱动
框架启动时扫描所有带 `[NetMsg]` 的协议类型，建立类型到协议元数据的映射。这意味着业务层只需要关心“发送什么对象”，不需要关心“这个对象应该用哪个魔数协议号”。

### 3. Shared / Server / Client 物理分层与防腐层
框架严格分为三层：
- **Shared**：共享协议、基础结构、混合序列化抽象、**INetworkTransport 传输层防腐接口**。
- **Server**：服务端权威逻辑、房间容器、会话、流式录制、GC、重连。
- **Client**：客户端状态机、协议接入、轻状态缓存、回放控制、表现桥接、事件系统。

### 4. Global / Room 作用域分离
框架内部把所有网络消息严格分成两种作用域：
- **Global**：不依赖房间上下文的逻辑（如登录、大厅列表、建房、录像下载）。
- **Room**：必须依赖某个房间上下文的逻辑（如战斗同步、房间聊天）。

### 5. 房间组件化装配
房间不是靠一个巨大的 `RoomLogic.cs` 处理所有事情，而是采用 **Room Component** 横向扩展模式。
通过在类上标记 `[RoomComponent(Id, Name)]` 特性，框架会自动生成常量表。建房时通过传入 `ComponentIds` 数组动态装配。

### 6. 两阶段装配与失败回滚
服务端和客户端房间组件装配都采用统一原则：先全量校验 -> 再统一挂载与绑定 -> 最后统一初始化。任一阶段失败，整体回滚，保证不会产生“半残房间实例”。

### 7. 无侵入式事件直抛与沙盒隔离
客户端内部采用两类事件系统，支持直接抛出协议对象，0GC 且无需 DTO 转换：
- `GlobalTypeNetEvent`：大厅、登录、录像列表等全局事件，基于泛型静态类，极速派发。
- `RoomNetEventSystem`：挂载在 `ClientRoom` 实例上的房间级事件总线。彻底避免回放房间和在线房间串线。
- **生命周期防泄漏**：提供 `.UnRegisterWhenGameObjectDestroyed(gameObject)` 与 `.UnRegisterWhenMonoDisable(this)`，彻底杜绝 UI 隐藏后的幽灵响应。

### 8. 流式 Replay 沙盒回放
回放是一个**客户端本地沙盒房间**。
**底层优化**：服务端采用 `GZipStream` 边打边压直接落盘；客户端采用 `FileStream` 分块下载（支持断点续传），播放时通过 `BinaryReader` 直接读取解压流，杜绝大文件 OOM。支持房主通过 `C2S_RenameReplay` 重命名录像。

### 9. Seq 防重放机制
客户端每次发包自动递增 `Seq`，服务端按 Session 记录 `LastReceivedSeq`。收到旧包或重复包直接拦截，防止重复点击或网络重试污染。

---

## 适用边界与承载预估
**承载预估 (单服节点)：**
- **并发规模**：100 ~ 300 CCU（得益于 0GC 优化，主要受限于 Unity 主线程逻辑开销）。
- **房间规模**：4 ~ 16 人（高频空间/动画同步，如交友房间），或 50 人以内（低频状态同步，如棋牌/回合制）。

---

## 总体架构

### 服务端主链路
`INetworkTransport` 抛出底层字节流 -> `ServerApp.OnReceivePacket()` 0GC 切片解析 -> 按 `Scope` 路由 -> 对应 `Module / RoomComponent` 处理 -> 修改权威状态 -> 服务端申请 `ArrayPool` 发送 `S2C` -> 客户端接收同步。

### 客户端主链路
`INetworkTransport` 抛出底层字节流 -> `ClientApp.OnReceivePacket()` 0GC 切片解析 -> 按 `Scope` 路由 -> 对应 `ClientModule / ClientRoomComponent` 处理 -> 直接通过 `GlobalTypeNetEvent` 或 `Room.NetEventSystem` 抛出协议对象 -> View 层监听协议并刷新表现。

---

## 基础 API 调用指引 (核心流转)

### 1. 建立物理连接与发起登录
```csharp
// 获取核心网络管理器引用 (以默认的 Mirror 实现为例)
var manager = GameLauncher.NetManager;
if (manager == null) return;

// 1. 建立物理连接
manager.StartClient();

// 2. 发送登录请求 (必须携带 ClientVersion 供服务端进行版本拦截)
var loginReq = new C2S_Login 
{ 
    AccountId = "Player_001", 
    ClientVersion = Application.version 
};
NetClient.Send(loginReq);
```

### 2. 创建与加入房间
建房时，通过传入 `ComponentIds` 数组来声明该房间需要挂载哪些业务组件。
```csharp
// 创建房间：使用自动生成的常量表装配组件，拒绝魔法数字
var createReq = new C2S_CreateRoom 
{ 
    RoomName = "我的专属房间", 
    ComponentIds = new int[] 
    { 
        ComponentIdConst.RoomSettings, 
        ComponentIdConst.SocialRoom 
    } 
};
NetClient.Send(createReq);

// 加入已有房间
var joinReq = new C2S_JoinRoom { RoomId = "Room_123456" };
NetClient.Send(joinReq);
```

### 3. 离开房间
```csharp
// 前置拦截：确保当前处于在线房间状态
if (NetClient.State == ClientAppState.OnlineRoom)
{
    NetClient.Send(new C2S_LeaveRoom());
}
```

---

## 核心开发流程

### 新增一个房间玩法功能
标准步骤如下：
1. 定义 Shared 协议 (`[NetMsg]`)。如果是高频同步协议，**必须实现 `ILiteNetSerializable` 接口**以启用二进制 0GC 序列化。
2. 编写服务端 `RoomComponent` (`[RoomComponent]`)。
3. 编写客户端 `ClientRoomComponent` (`[RoomComponent]`)，收到协议直接 `Room.NetEventSystem.Broadcast(msg)`。
4. **点击顶部菜单 `StellarNet/Lite 强制重新生成协议与组件常量表`**。
5. 在建房时把 `ComponentId` 加入 `ComponentIds`。
6. View 层监听 `Room.NetEventSystem` 抛出的协议事件并做表现。

---

## 当前版本开发建议 (极度重要)

1. **高频协议必须 0GC**：对于摇杆移动、坐标同步等每秒发送多次的协议，必须实现 `ILiteNetSerializable`，否则 JSON 序列化会迅速撑爆 GC。
2. **优先横向扩展 RoomComponent**：新增房间功能时，优先新增独立组件，不要把功能堆进现有的巨石类中。
3. **UI 必须防泄漏**：View 层监听 `S2C_` 协议时，**务必使用 `.UnRegisterWhenMonoDisable(this)`**。如果 UI 被隐藏但未销毁，不注销事件会导致后台疯狂响应网络包并报错。
4. **表现层坐标解耦**：UI 气泡、血条等需要跟随 3D 物体的表现，必须使用 `UGUIFollowTarget` 组件，严禁在业务逻辑中手写 `Camera.main.WorldToScreenPoint`。
5. **所有高风险入口先做前置拦截**：例如判空、RoomId 校验、状态机校验。统一原则：**先拦截，先报错，先 return，绝不带病继续。**
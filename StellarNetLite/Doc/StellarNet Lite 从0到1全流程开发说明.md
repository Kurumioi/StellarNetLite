# StellarNet Lite 从0到1全流程开发说明

2026年3月31日补充

> 这份文档专门回答一个问题：  
> **一个开发者第一次接触 StellarNet Lite 时，服务端、客户端、编辑器生成链、UI、房间、回放、重连，到底是怎么一步一步串起来的？**

---

## 1. 先建立整体心智

先记住一句话：

> **客户端发请求，服务端改真相，客户端收广播后刷新表现。**

整个框架都围绕这条主线展开：

1. `Shared` 定义协议、基础结构、传输抽象
2. `Editor` 扫描特性并生成静态注册表
3. `Server` 维护权威状态
4. `Client` 负责状态接入、事件分发、回放播放
5. `GameDemo` 只是一个示例外壳，帮助你理解接入方式

---

## 2. 第 0 步：你第一次接入工程时要先做什么

### 2.1 确认场景里有两个入口对象

运行前，场景里至少要有：

1. `GameLauncher`
2. `StellarNetMirrorManager`

它们分别负责：

1. `GameLauncher`
   负责 Demo 初始化、启动 UI、持有全局入口
2. `StellarNetMirrorManager`
   负责启动服务端 / 客户端、接入底层传输、把数据交给 `ServerApp` 和 `ClientApp`

### 2.2 新增协议或组件后，先生成，再运行

每次你新增这些内容：

1. `[NetMsg]` 协议
2. `[RoomComponent]` 房间组件
3. `[ServerModule]` / `[ClientModule]` 全局模块

都必须先点 Unity 菜单：

1. `StellarNetLite/强制重新生成协议与组件常量表`

如果你新增了 `Resources/NetPrefabs/` 下的网络预制体，还要再点：

1. `StellarNetLite/生成网络预制体常量表 (Net Prefabs)`

这是因为 Runtime 不靠反射临时扫描，而是依赖 Editor 生成的静态表。

---

## 3. 第 1 步：Editor 生成链到底做了什么

### 3.1 协议扫描

`Editor/LiteProtocolScanner.cs` 会扫描所有带 `[NetMsg]` 的类型，生成协议元数据表。

结果会被 `Runtime/Shared/Core/NetMessageMapper.cs` 在运行时读取。

作用是：

1. 建立 `Type -> MsgId`
2. 建立 `MsgId -> Type`
3. 校验方向和作用域

### 3.2 组件与模块扫描

Editor 还会扫描：

1. `[RoomComponent]`
2. `[ServerModule]`
3. `[ClientModule]`

然后生成自动装配代码，让 `ServerApp`、`ClientApp`、`RoomFactory` 能按约定自动创建实例。

### 3.3 网络预制体扫描

`Editor/NetPrefabScanner.cs` 会扫描 `Resources/NetPrefabs/`，生成：

1. `PrefabHash -> 资源路径`
2. 常量名 -> Hash

这样对象同步组件只需要发 `PrefabHash`，客户端就能还原正确预制体。

---

## 4. 第 2 步：运行时启动顺序

### 4.1 Mirror Manager 创建底层通道

`Runtime/StellarNetMirrorManager.cs` 会负责：

1. 启动服务端监听
2. 启动客户端连接
3. 接收底层网络字节流
4. 包装成 `Packet`
5. 转发给 `ServerApp` 或 `ClientApp`

### 4.2 ServerApp / ClientApp 初始化

启动后会完成这些动作：

1. 初始化 `NetMessageMapper`
2. 初始化模块分发器
3. 初始化房间工厂
4. 初始化会话或客户端状态机
5. 挂接底层 `INetworkTransport`

这一步完成后，框架才知道：

1. 某个 `MsgId` 对应哪个协议类
2. 某个协议该分发给哪个模块或房间组件
3. 某个组件 Id 该怎么被装配

---

## 5. 第 3 步：客户端从登录开始的完整链路

### 5.1 View 发起登录

典型入口来自：

1. `Panel_StellarNetLogin`
2. 你自己的登录 UI

View 只做一件事：

1. 收集输入
2. 调 `NetClient.Send(new C2S_Login { ... })`

### 5.2 Client 发送链

发送时的完整路径是：

1. View 调 `NetClient.Send`
2. `ClientApp` 查 `NetMessageMapper`
3. 校验该协议是否允许 `C2S`
4. 递增 `Seq`
5. 序列化载荷
6. 组装 `Packet`
7. 交给 `INetworkTransport.SendToServer`

### 5.3 Server 收到登录包

服务端主链路是：

1. 传输层收到字节流
2. `ServerApp.OnReceivePacket`
3. 根据连接找到 `Session`
4. 做 `Seq` 防重放校验
5. 按 `Scope = Global` 分发到全局模块
6. 进入 `ServerUserModule`
7. 校验账号、版本、旧连接映射等
8. 回发 `S2C_LoginResult`

### 5.4 Client 收到登录结果

客户端收到 `S2C_LoginResult` 后：

1. `ClientUserModule` 处理结果
2. 通过 `GlobalTypeNetEvent.Broadcast(msg)` 抛出
3. `GlobalUIRouter` 监听后决定开哪个面板
4. 成功则进入大厅 UI

这一步非常关键：

> **模块处理协议，UI 不直接处理网络原始包。**

---

## 6. 第 4 步：大厅阶段完整链路

### 6.1 拉取房间列表

大厅打开后通常会发：

1. `C2S_GetRoomList`

服务端流程：

1. `ServerLobbyModule.OnC2S_GetRoomList`
2. 汇总 `ServerApp.Rooms`
3. 组装 `S2C_RoomListResponse`
4. 回发给请求方

客户端流程：

1. `ClientLobbyModule.OnS2C_RoomListResponse`
2. `GlobalTypeNetEvent.Broadcast`
3. `Panel_StellarNetLobby` 刷新列表项

### 6.2 建房

客户端建房时会发：

1. `C2S_CreateRoom`

请求里最重要的是：

1. `RoomName`
2. `ComponentIds`

也就是说，建房的本质是：

> **告诉服务端，这个房间要装哪些能力组件。**

服务端收到后：

1. `ServerRoomModule` 校验请求
2. `ServerRoomFactory` 按 `ComponentIds` 创建组件
3. 两阶段装配
   先校验
   再创建
   再绑定
   再初始化
4. 创建成功后返回房间信息给客户端

### 6.3 加房

加入已有房间时也类似：

1. 客户端发送 `C2S_JoinRoom`
2. 服务端校验房间存在、人数、状态
3. 如果允许进入，则把房间组件清单返回给客户端

---

## 7. 第 5 步：为什么进房后不是立刻收到快照

这是框架里非常重要的一环。

### 7.1 客户端先本地装房间

服务端不会在“刚同意进房”时立刻推快照，而是先把：

1. `RoomId`
2. `ComponentIds`

告诉客户端。

然后客户端会：

1. 创建 `ClientRoom`
2. 调 `ClientRoomFactory`
3. 按 `ComponentIds` 装配 `ClientRoomComponent`
4. 每个组件执行 `OnInit`

### 7.2 客户端确认自己准备好了

当本地房间组件都装好后，客户端才发送：

1. `C2S_RoomSetupReady`

这一步的意义是：

> **保证客户端已经具备接收房间快照的能力，再让服务端正式发同步。**

### 7.3 服务端正式下发快照

服务端收到 `C2S_RoomSetupReady` 后，才会：

1. 把成员正式加入房间
2. 调用各个 `RoomComponent.OnMemberJoined`
3. 调用各个 `RoomComponent.OnSendSnapshot`

于是客户端就会陆续收到：

1. `S2C_RoomSnapshot`
2. 各业务组件自己的快照
3. 如果房间已经在游戏中，还可能补发 `S2C_GameStarted`

---

## 8. 第 6 步：房间内业务是怎么流转的

以一个标准房间功能为例，完整链路如下：

1. 玩家点击按钮
2. View 调 `NetClient.Send(C2S_xxx)`
3. `ClientApp` 发包给服务端
4. `ServerApp` 收包并分发给对应 `RoomComponent`
5. 服务端校验状态
6. 服务端修改权威数据
7. 服务端调用 `Room.BroadcastMessage(S2C_xxx)`
8. 客户端对应 `ClientRoomComponent` 收到消息
9. 客户端组件更新本地轻状态缓存
10. `Room.NetEventSystem.Broadcast(msg)`
11. 房间内 UI / 表现层监听事件并刷新

这就是框架最推荐的写法。

### 8.1 以房间设置组件为例

`RoomSettings` 这一组脚本就是标准模板：

1. 服务端 `ServerRoomSettingsComponent`
   维护成员列表、房主、准备状态、开局/结算逻辑
2. 客户端 `ClientRoomSettingsComponent`
   维护本地成员缓存
3. UI Router / Panel
   只监听事件并刷新界面

你以后写新房间业务，优先照这个结构去扩。

---

## 9. 第 7 步：对象同步链路是怎么工作的

如果房间里有动态实体，比如玩家、怪物、子弹，那么一般会接 `ObjectSync`。

### 9.1 服务端负责生成权威实体

流程如下：

1. 服务端业务组件调用 `ServerObjectSyncComponent.SpawnObject`
2. 创建 `ServerSyncEntity`
3. 分配 `NetId`
4. 记录位置、旋转、缩放、动画态、Owner
5. 广播 `S2C_ObjectSpawn`

### 9.2 客户端负责生成表现对象

客户端收到 `S2C_ObjectSpawn` 后：

1. `ClientObjectSyncComponent` 写入本地同步缓存
2. 抛出本地事件
3. `ObjectSpawnerView` 监听后实例化预制体
4. 给实例挂 `NetIdentity`
5. 挂 `NetTransformView` / `NetAnimatorView`

### 9.3 后续增量同步

服务端每 Tick：

1. 汇总脏数据
2. 广播 `S2C_ObjectSync`

客户端每帧：

1. 从同步缓存读取预测数据
2. `NetTransformView` 平滑驱动位置/旋转/缩放
3. `NetAnimatorView` 驱动动画表现

---

## 10. 第 8 步：开局、结算、回放是怎么串起来的

### 10.1 开局

房主点击开始：

1. 客户端发 `C2S_StartGame`
2. 服务端 `ServerRoomSettingsComponent` 校验
3. 判断是否房主、房间状态是否合法、其他成员是否已准备
4. 调 `Room.StartGame()`
5. 广播 `S2C_GameStarted`

### 10.2 游戏中广播为什么会被录像记录

房间广播默认可以录入 Replay。

所以大部分关键消息在发送时会同时：

1. 发给在线客户端
2. 写入服务端回放流

这就是为什么回放能还原游戏过程。

### 10.3 结算

结束时通常会：

1. 服务端调用 `Room.EndGame()`
2. 关闭本轮玩法态
3. 结束录制
4. 生成 replay 文件
5. 广播 `S2C_GameEnded`

如果有录像可用，还会把 `ReplayId` 一并下发。

---

## 11. 第 9 步：录像下载和本地回放完整链路

### 11.1 大厅请求录像列表

大厅点刷新后：

1. 客户端发送 `C2S_GetReplayList`
2. 服务端 `ServerReplayModule` 扫描回放目录
3. 回发 `S2C_ReplayList`
4. 大厅生成录像列表项

### 11.2 下载录像

点击下载后：

1. 客户端 `ClientReplayModule.RequestDownload`
2. 如果本地已有 `.replay`，直接广播成功结果
3. 如果只有 `.tmp`，带偏移发断点续传
4. 服务端 `ServerReplayModule` 建立下载任务
5. 回发 `S2C_DownloadReplayStart`
6. 服务器发送 `S2C_DownloadReplayChunk`
7. 客户端落盘并回 `C2S_DownloadReplayChunkAck`
8. 直到全部完成

### 11.3 播放录像

下载完成后：

1. `GlobalUIRouter` 监听 `S2C_DownloadReplayResult`
2. 打开 `Panel_StellarNetReplay`
3. 内部创建 `ClientReplayPlayer`
4. 读取 `.replay`
5. 进入本地 `ReplayRoom`
6. 回放消息帧和对象关键帧

这里最重要的认知是：

> **回放房间是本地沙盒房间，不是在线房间的一个模式开关。**

---

## 12. 第 10 步：断线重连链路

如果客户端在在线房间里断线：

1. 底层连接断开
2. 客户端进入 `ConnectionSuspended`
3. 重试物理连接
4. 再次发送登录
5. 服务端识别这是旧账号恢复，不是新用户
6. 返回存在可恢复房间的信息
7. 客户端选择接受重连
8. 本地重建房间组件
9. 发 `C2S_ReconnectReady`
10. 服务端对该玩家执行 `OnSendSnapshot`

于是客户端就会重新收到：

1. 房间基础快照
2. 各业务组件快照
3. 如果房间正在游戏中，还会收到运行态恢复消息

---

## 13. 第 11 步：开发者新增一个业务功能时的标准动作

以后你每次新增功能，都尽量按这个顺序：

### 13.1 如果是大厅功能

1. 在 `Shared` 定义协议
2. 写 `ServerModule`
3. 写 `ClientModule`
4. 重新生成静态表
5. View 监听 `GlobalTypeNetEvent`

### 13.2 如果是房间功能

1. 在 `Shared` 定义协议和快照结构
2. 写 `Server RoomComponent`
3. 写 `Client RoomComponent`
4. 如果需要 UI，就补 `OnlineUIRouter / ReplayUIRouter`
5. 重新生成静态表
6. 把组件接入 `RoomTypeTemplateRegistry` 或建房组件清单

### 13.3 如果功能依赖场景实体

1. 把预制体放进 `Resources/NetPrefabs/`
2. 重新生成 `NetPrefab` 常量表
3. 服务端通过 `ServerObjectSyncComponent` 生成实体
4. 客户端通过 `ObjectSpawnerView` 还原表现

---

## 14. 最后给新开发者的阅读顺序建议

如果你第一次读这个工程，推荐按这个顺序：

1. 先看 `Doc/StellarNet Lite 开发者使用手册 v1.0.md`
2. 再看这份《从0到1全流程开发说明》
3. 再读下面这些核心代码
   `Runtime/StellarNetMirrorManager.cs`
   `Runtime/Shared/Core/NetMessageMapper.cs`
   `Runtime/Server/Core/ServerApp.cs`
   `Runtime/Client/Core/ClientApp.cs`
   `Runtime/Server/Core/RoomInstance/Room.cs`
   `Runtime/Client/Core/ClientRoom.cs`
4. 然后看这两组标准业务模板
   `Runtime/Server/Components/ServerRoomSettingsComponent.cs`
   `Runtime/Client/Components/ClientRoomSettingsComponent.cs`
5. 最后再看 Demo
   `GameDemo/GameLauncher.cs`
   `GameDemo/Client/Infrastructure/GlobalUIRouter.cs`
   `GameDemo/Client/View/UIPanel/*`

---

## 15. 一句话总结整套框架

如果你只想记住一句最核心的话，那就是：

> **Editor 负责生成静态注册，Server 负责真相，Client 负责接包和抛事件，UI 只负责表现，Replay 和 Online 永远分房间隔离。**

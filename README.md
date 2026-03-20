# StellarNet Lite

> 一个面向 Unity 中小型多人联机项目的轻量级房间网络框架  
> 核心理念：**服务端权威、协议事件驱动、房间组件化、流式回放沙盒、Editor 扫描生成 + Runtime 0反射静态装配**

---
![Uploading image.png…]()

## 项目简介

**StellarNet Lite** 是一套面向 Unity 的轻量级房间式联网框架，适用于：

- 房间制 / 对局制游戏
- 中小型商业项目
- 需要长期维护的多人联机玩法
- 需要录像、重连、房间治理、弱网处理的多人房间业务

**承载规模预估（单服节点）**：

基于 Unity 主线程 Tick、`ArrayPool` 0GC 发包、`ArraySegment` 0GC 切片解析、二进制 / JSON 混合序列化（`ILiteNetSerializable`），以及当前房间组件化架构，单服理论承载约为：

- **100 ~ 300 CCU**
- 单房间 **4 ~ 16 人**：高频空间 / 动画同步（如社交房间、轻竞技）
- 单房间 **50+ 人**：低频状态同步（如棋牌、回合制、低频交互房间）

若需扩展至更大规模，需自行引入：

- 网关层
- 多节点房间调度
- 跨服路由
- 分布式状态治理

它不追求“黑盒自动同步”，而是强调：

- **数据流转清晰可追踪**
- **服务端绝对权威**
- **客户端表现与业务彻底解耦**
- **按房间组件横向扩展玩法**
- **支持流式回放、断线重连、GC、配置化等生产级基础能力**
- **传输层彻底解耦（`INetworkTransport`），可无缝切换 Mirror / KCP / LiteNetLib 等底层实现**
- **Editor 扫描生成、Runtime 静态装配，避免运行时协议反射扫描**

如果你正在开发这类项目：

- 房间交友 / 社交元宇宙
- 休闲联机
- 棋牌游戏 / 桌游
- 小型竞技
- 带录像与断线重连的房间游戏

那么 StellarNet Lite 会比“到处 SyncVar”“逻辑全堆一个房间类里”的方案更适合长期演进。

---

## 核心特性

### 1. 服务端绝对权威

客户端只负责：

- 采集输入
- 发送请求
- 接收权威同步
- 播放表现结果

所有核心业务状态都由服务端决定。

一句话：

> **客户端可以有状态，但不能有权威。**

---

### 2. 显式协议事件驱动

框架拒绝黑盒自动同步。所有状态变化都必须通过清晰协议流转：

**客户端请求 -> 服务端校验 -> 服务端修改状态 -> 服务端同步 -> 客户端刷新表现**

---

### 3. 强类型发送器与 0GC 发包

业务层不再手拼 `Packet`，统一通过强类型入口发送。底层采用 `System.Buffers.ArrayPool` 实现低分配发包，并支持通过 `ILiteNetSerializable` 接口实现纯二进制高频序列化：

- `NetClient.Send<T>()`
- `ServerApp.SendMessageToSession<T>()`
- `Room.BroadcastMessage<T>()`
- `Room.SendMessageTo<T>()`

这样可以把协议号、作用域、方向校验统一收口到框架层，业务只关心“发什么对象”，而不是“这个对象该拼成什么底层包”。

---

### 4. 房间组件化与 Editor 自动装配

每个房间玩法都可以拆成独立 `Room Component`。

你只需要：

- 给协议打上 `[NetMsg]`
- 给服务端房间组件打上 `[RoomComponent]`
- 给客户端房间组件打上 `[RoomComponent]`
- 给全局逻辑打上 `[ServerModule]` / `[ClientModule]`

然后通过编辑器菜单生成：

- `MsgIdConst`
- `ComponentIdConst`
- `AutoMessageMetaRegistry`
- `AutoRegistry`

核心收益是：

> **新增一个玩法，优先横向加一个组件，不需要去修改核心网络管理类，天然符合开闭原则。**

---

### 5. Runtime 0反射协议元数据使用

当前版本已经从“Runtime 扫描反射读取协议特性”升级为：

- **Editor 阶段扫描**
- **自动生成静态元数据注册表**
- **Runtime 直接读取生成结果**

也就是说：

- 协议扫描和冲突发现发生在编辑器阶段
- 运行时不再为协议映射做程序集反射扫描
- `NetMessageMapper` 在 Runtime 只负责读取 `AutoMessageMetaRegistry`

这能显著提升启动确定性、减少运行时反射风险，并让协议错误更早暴露在开发阶段。

---

### 6. 客户端 Service / View 解耦与无侵入事件直抛

客户端房间组件只负责：

- 接收网络同步
- 更新轻状态
- 抛出事件

View 层只负责：

- 监听协议事件
- 刷新 UI
- 播放表现
- 响应输入

通过无侵入式的事件总线：

- `GlobalTypeNetEvent`
- `Room.NetEventSystem`

实现协议对象直抛，并支持 `.UnRegisterWhenGameObjectDestroyed` 和 `.UnRegisterWhenMonoDisable` 进行极简的生命周期防泄漏绑定。

---

### 7. 客户端全局 UI 路由与房间 UI 路由解耦

当前工程已经形成两层 UI 路由结构：

#### 全局 UI 路由

由 `GlobalUIRouter` 负责：

- 登录 / 大厅 / 回放面板
- 全局状态跌落处理
- 下载录像后的全局跳转
- 被踢下线 / 物理断连后的全局 UI 回退

#### 房间组件 UI 路由

由 `RoomUIRouterBase<T>` 及其子类负责：

- 在线房间态 UI
- 回放房间态 UI
- 按组件粒度管理进入 / 离开房间时的局部 UI 生命周期

这让全局导航逻辑和房间业务表现逻辑彻底分层，避免巨石 UI 管理器。

---

### 8. 流式回放沙盒模式

内置 `ReplayRoom` 概念。回放不是“在线房间加一个回放标记”，而是客户端本地重建出的**独立沙盒房间**。

当前回放链路包含：

- 服务端 `GZipStream` 边录边压
- 客户端 `FileStream` 分块下载
- 客户端 `.raw` 解压缓存
- `BinaryReader` 顺序读取消息帧
- `ReplayObjectSnapshotFrame` 对象关键帧恢复
- 本地回放房间 Seek / 倍速 / 暂停控制
- 支持房主对录像进行重命名（`C2S_RenameReplay`）

收益：

- 回放与真实在线房间彻底隔离
- 不会把历史消息串进真实房间
- Seek 不需要从头补播到目标 Tick
- 对象世界可以通过关键帧快速恢复
- 避免大文件整包读入导致 OOM

---

### 9. 断线重连与状态恢复

框架支持：

- 登录态恢复
- 房间重连确认
- 客户端先装配组件，再进入快照恢复
- 服务端按房间上下文定向下发快照
- 在线房间异常断开后的 `ConnectionSuspended` 软挂起流程

整个重连链路明确可追踪，不依赖隐式同步。

---

### 10. 生产级治理能力

除了联网基础功能外，框架还包含：

- 协议 ID 冲突扫描
- 组件 ID / 常量名冲突扫描
- 预制体 Hash 冲突扫描
- 房间生命周期 GC
- 空房间熔断
- 离线 Session GC
- 录像文件滚动清理
- RTT 弱网监测与主动熔断（基于 `INetworkTransport.GetRTT()`）
- 编辑器配置窗口
- 业务脚手架生成器

---

## 当前架构要点

### Shared / Server / Client 分层

#### Shared

放双端共享的基础契约与底座能力：

- 协议定义（支持 `ILiteNetSerializable`）
- `Packet`（支持 `PayloadOffset` 0GC 切片）
- `NetMessageMeta`
- 序列化接口（`INetSerializer`）
- 传输层防腐接口（`INetworkTransport`）
- 配置结构
- 日志工具
- 自动生成常量表
- 回放关键帧共享结构（如 `ReplayObjectSnapshotFrame`）

#### Server

放服务端权威逻辑：

- `ServerApp`
- `Session`
- `Room`
- `RoomComponent`
- `ServerModule`
- Replay 录制与重命名
- GC 与治理
- 重连恢复
- 对象关键帧录制

#### Client

放客户端表现桥接与轻状态：

- `ClientApp`
- `ClientRoom`
- `ClientRoomComponent`
- `ClientModule`
- 回放播放器
- 全局 / 房间事件系统
- UI 路由器
- 表现层视图（如 `NetTransformView`, `UGUIFollowTarget`）

---

### Global / Room 作用域分层

#### Global

不依赖房间上下文的协议：

- 登录
- 大厅房间列表
- 建房
- 加房
- 回放列表
- 回放下载与重命名
- 重连协商

#### Room

依赖房间上下文的协议：

- 房间快照
- 成员进出
- 准备状态
- 游戏开始 / 结束
- 空间同步
- 社交房移动 / 动作 / 气泡

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

## 快速开始

### 1. 获取项目

确保工程内已安装：

- Mirror（默认底层实现）
- Newtonsoft.Json

---

### 2. 打开示例场景

运行带有以下对象的测试场景：

- `GameLauncher`
- `StellarNetMirrorManager`

当前全局 UI 导航由 `GlobalUIRouter` 接管。

---

### 3. 协议与组件常量表生成（极度重要）

新增协议或组件后，必须点击顶部菜单：

- `StellarNetLite/强制重新生成协议与组件常量表`
- `StellarNetLite/生成网络预制体常量表 (Net Prefabs)`

这会驱动自动生成：

- `MsgIdConst`
- `ComponentIdConst`
- `NetPrefabConsts`
- `AutoMessageMetaRegistry`
- `AutoRegistry`

如果你新增了协议或组件却没有重新生成，运行时很可能出现：

- 本地未注册 `ComponentId`
- 类型未注册到静态协议表
- 自动装配缺失
- 预制体 Hash 无法识别

---

## 典型开发流程

### 扩展一个房间玩法

以“房间发表情”功能为例，标准步骤如下：

1. 在 `Shared/Protocol` 中定义协议，打上 `[NetMsg]`。若为高频协议，建议实现 `ILiteNetSerializable`
2. 在 `Server/Components` 中编写服务端组件，打上 `[RoomComponent]`
3. 在 `Client/Components` 中编写客户端组件，打上 `[RoomComponent]`
4. 在客户端组件收到协议后，按需执行 `Room.NetEventSystem.Broadcast(msg)`
5. 点击顶部菜单 `StellarNetLite/强制重新生成协议与组件常量表`
6. 若该玩法需要作为建房模板接入，则把对应组件加入 `RoomTypeTemplateRegistry`
7. 在 View 或 UI Router 中监听协议并刷新表现

---

### 使用脚手架生成模板

打开菜单：

`StellarNetLite/业务脚手架生成器`

可以一键生成符合现行架构口径的业务骨架，并自动打好：

- 协议特性
- 房间组件特性
- ServerModule / ClientModule 特性
- 基础防御性结构
- ScaffoldFeature 标记头

---

## 文档入口

建议阅读顺序：

1. `README.md`
2. `StellarNet Literal 功能说明文档.md`
3. `StellarNet Lite 开发者使用手册 v1.0.md`
4. `StellarNet Lite 快速接入指南.md`
5. `StellarNet Lite 实体同步与业务开发指南.md`
6. `StellarNet Lite UI路由与回放路由说明.md`

> 注意：当前仓库中的第二份文档物理文件名为 `StellarNet Literal 功能说明文档.md`。  
> 如果后续你准备统一命名，建议把物理文件名也改回 `StellarNet Lite 功能说明文档.md`，避免团队检索时产生误解。

---

## 当前版本边界

当前框架已经覆盖：

- 房间制联机基础链路
- 房间组件动态装配
- 全局 / 房间协议分流
- 强类型发包与高频二进制序列化
- 静态协议元数据
- 回放下载、回放沙盒、对象关键帧恢复与录像重命名
- 重连恢复
- 弱网熔断
- 编辑器治理工具

当前尚未覆盖的大型能力包括：

- 回滚同步
- 预测纠正型竞技底座
- 强反作弊体系
- MMO 级大地图同步
- 分布式大规模节点调度

因此它更适合：

> **中小型、房间制、长期维护、强调结构清晰和工程可控的 Unity 商业联机项目。**

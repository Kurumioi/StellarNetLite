# StellarNet Lite
2026年3月11日17:00:19修订
> 一个面向 Unity 中小型多人联机项目的轻量级房间网络框架  
> 核心理念：**服务端权威、协议事件驱动、房间组件化、回放沙盒化、0反射自动装配**
---
## 项目简介
**StellarNet Lite** 是一套面向 Unity 的轻量级房间式联网框架，适用于：
- 房间制
- 对局制
- 中小型商业项目
- 需要长期维护的多人联机玩法

它不追求“黑盒自动同步”，而是强调：
- **数据流转清晰可追踪**
- **服务端绝对权威**
- **客户端表现与业务彻底解耦**
- **按房间组件横向扩展玩法**
- **支持回放、重连、GC、配置化等生产级基础能力**

如果你正在开发这类项目：
- 房间对战
- 休闲联机
- 棋牌游戏 / 桌游
- 小型竞技
- 带录像与断线重连的房间游戏
- 需要多人协作长期迭代的中小型联网项目

那么 StellarNet Lite 会比“到处 SyncVar”“逻辑全堆一个房间类里”的方案更适合长期演进。

---
## 核心特性

### 1. 服务端绝对权威
客户端只负责：采集输入、发送请求、接收权威同步、播放表现结果。
所有核心业务状态都由服务端决定。
一句话：> **客户端可以有状态，但不能有权威。**

### 2. 显式协议事件驱动
框架拒绝黑盒自动同步，所有状态变化都必须通过清晰协议流转：
客户端请求 -> 服务端校验 -> 服务端修改状态 -> 服务端同步 -> 客户端刷新表现

### 3. 强类型发送器统一发包
业务层不再手拼 `Packet`，统一通过强类型入口发送：
- `ClientApp.SendMessage<T>()`
- `ServerApp.SendMessageToSession<T>()`
- `Room.BroadcastMessage<T>()`
- `Room.SendMessageTo<T>()`

### 4. 房间组件化与 0 反射自动装配 (Auto Registry)
每个房间玩法都可以拆成独立 `Room Component`。
只需为类打上 `[RoomComponent]` 或 `[GlobalModule]` 特性，点击编辑器菜单，框架会自动生成装配代码，彻底消灭硬编码注册。
核心收益是：> **新增一个玩法，优先横向加一个组件，绝对不需要修改核心网络管理类，完美符合开闭原则（OCP）。**

### 5. 客户端 Service / View 解耦
客户端房间组件只负责接收网络同步并抛出事件。View 层只负责监听协议事件并刷新 UI。
通过 `GlobalTypeEvent` 和 `Room.EventSystem` 实现 0GC 协议对象直抛。

### 6. 回放沙盒模式
内置 `ReplayRoom` 概念。回放是客户端本地重建出来的独立沙盒房间，自动隔离真实在线房间消息，保证状态纯净。

### 7. 断线重连与状态恢复
框架支持：登录态恢复、房间重连确认、客户端先装配组件再接收快照、服务端按房间上下文定向恢复。

### 8. 生产级治理能力
除了联网基础功能外，框架还包含：
- 协议 ID 冲突扫描
- 房间生命周期 GC 与空房间熔断
- 离线 Session GC
- 录像文件滚动清理
- 编辑器配置窗口与业务脚手架生成器

---
## 目录结构
```text
Assets/StellarNetLite/
├── Runtime/
│   ├── Shared/
│   │   ├── Core/
│   │   ├── Protocol/
│   │   ├── Infrastructure/
│   │   └── Binders/       (AutoRegistry 自动生成目录)
│   ├── Server/
│   ├── Client/
│   └── StellarNetMirrorManager.cs
├── Editor/
│   ├── LiteProtocolScanner.cs
│   ├── NetConfigEditorWindow.cs
│   └── StellarNetScaffoldWindow.cs
└── GameDemo/
```

---
## 快速开始

### 1. 获取项目
确保工程内已安装 Mirror 与 Newtonsoft.Json。

### 2. 打开示例场景
运行带有 `StellarNetMirrorManager` 和 `StellarNetDemoUI` 的测试场景。

### 3. 协议与组件常量表生成 (极度重要)
新增协议或组件后，必须点击顶部菜单：
- `StellarNet/Lite 强制重新生成协议与组件常量表`
这会驱动底层自动生成 `MsgIdConst`、`ComponentIdConst` 以及 `AutoRegistry` 装配代码。

---
## 典型开发流程

### 扩展一个房间玩法
以“房间发表情”功能为例，标准步骤如下：
1. 在 `Shared/Protocol` 中定义协议，打上 `[NetMsg]` 特性。
2. 在 `Server/Components` 中编写服务端组件，打上 `[RoomComponent]` 特性。
3. 在 `Client/Components` 中编写客户端组件，打上 `[RoomComponent]` 特性，收到协议后直接 `Room.EventSystem.Broadcast(msg)`。
4. **点击顶部菜单 `StellarNet/Lite 强制重新生成协议与组件常量表`，完成自动装配。**
5. 建房时把对应 `ComponentId` 加入组件清单。
6. 在 View 层注册监听 `S2C_` 协议并做表现。

### 使用脚手架生成模板
打开菜单：`StellarNet/Lite 业务脚手架 (Scaffold)`
可以一键生成符合现行架构口径的业务骨架，并自动打好装配特性。

---
## 文档入口
建议阅读顺序：
1. `README.md`
2. `Docs/框架功能说明文档.md`
3. `Docs/开发者使用手册.md`
4. `Docs/StellarNet Lite 快速接入指南.md`

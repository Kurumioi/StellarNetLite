# StellarNet Lite UI路由与回放路由说明
> 面向客户端主程、玩法开发者、UI 开发者  
> 核心目标：**讲清楚客户端全局 UI 路由、房间组件运行时 UI 路由、回放时 UI 路由三者的职责边界与协作方式，以及基于 UGUIFollow 的表现层彻底解耦。**

---

## 目录
- [1. 文档定位](#1-文档定位)
- [2. 为什么必须单独设计 UI 路由层](#2-为什么必须单独设计-ui-路由层)
- [3. 当前工程中的 UI 路由总体结构](#3-当前工程中的-ui-路由总体结构)
- [4. 客户端全局 UI 路由：GlobalUIRouter](#4-客户端全局-ui-路由globaluirouter)
- [5. 房间组件运行时 UI 路由：RoomUIRouterBase<T>](#5-房间组件运行时-ui-路由roomuirouterbaset)
- [6. 在线态 UI 路由与回放态 UI 路由的差异](#6-在线态-ui-路由与回放态-ui-路由的差异)
- [7. 当前工程中的实际 Router 列表](#7-当前工程中的实际-router-列表)
- [8. 全局事件与房间事件在 UI 路由中的分工](#8-全局事件与房间事件在-ui-路由中的分工)
- [9. 回放时 UI 路由的真实工作方式](#9-回放时-ui-路由的真实工作方式)
- [10. Component、Router、UIPanel 的职责边界 (极度重要)](#10-componentrouteruipanel-的职责边界-极度重要)
- [11. 表现层坐标解耦：UGUIFollow 的引入](#11-表现层坐标解耦uguifollow-的引入)
- [12. 常见错误写法](#12-常见错误写法)
- [13. 排障建议](#13-排障建议)
- [14. 总结](#14-总结)

---

## 1. 文档定位
这份文档不是介绍协议，也不是介绍房间组件实现细节。  
它只解决一件事：
> **客户端 UI 到底该由谁控制，什么时候控制，在线房间和回放房间的 UI 为什么不能混在一起。**

当前工程已经形成了比较清晰的路由分层，但如果没有文档约束，后续多人协作时很容易出现：
- 把全局 UI 跳转写进 `ClientRoomComponent`
- 把房间 UI 打开关闭写进全局 Router
- 把回放 UI 复用到在线态面板
- 把业务事件直接在 Panel 里硬接到底层网络
- 房间销毁或 UI 隐藏后不清理，残留旧事件监听（幽灵响应）
- 把 3D 坐标跟随逻辑硬写在 UI 业务脚本里

所以这份文档的核心价值是：**统一团队写 UI 的责任边界与生命周期规范。**

---

## 2. 为什么必须单独设计 UI 路由层
在小 Demo 阶段，很多项目会这样写：
- 登录成功后，直接在 `Panel_Login` 里打开大厅
- 进房成功后，直接在 `ClientRoomComponent` 里打开房间面板
- 游戏结束后，直接在 `Panel_SocialRoomView` 里关闭自己再打开结算
- 回放时复用在线房间面板，加一堆 `if (isReplay)` 分支

这种写法短期看很快，长期一定变成灾难，原因有三个。

### 2.1 UI 跳转会和网络状态强耦合
一旦网络状态发生变化（断线、重连、被踢、房间销毁、回放开始），你会发现 UI 逻辑散落在 Panel、Module、Component 各处，最后没人知道某个面板到底该由谁负责打开和关闭。

### 2.2 在线房间与回放房间的 UI 责任完全不同
在线房间里，UI 常常要支持输入、操作按钮、网络状态反馈、房主权限。
回放房间里，UI 常常只需要播放控制、时间轴、倍速、只读表现。
如果不拆路由层，最后就会变成一个 Panel 里到处写 `if (isReplay)` 分支，快速失控。

### 2.3 房间组件横向扩展时，UI 也必须横向扩展
StellarNet Lite 的核心思想之一是：**新增业务优先横向扩展一个房间组件，而不是修改核心类。**
既然房间逻辑是按组件拆的，那么 UI 路由也应该跟着组件拆，而不是重新搞一个巨石 UIManager 接管所有业务。

---

## 3. 当前工程中的 UI 路由总体结构
当前客户端 UI 路由分为两层：

### 第一层：全局 UI 路由
负责跨房间、跨状态的大导航（如登录、大厅、回放面板、全局网络监控面板、状态跌落回退）。
当前实现类：`GlobalUIRouter`

### 第二层：房间组件 UI 路由
负责单个房间组件在不同状态下的局部 UI 生命周期（在线要开什么、回放要开什么、离房要关什么）。
当前实现基类：`RoomUIRouterBase<T>`
当前子类示例：`RoomSettingsOnlineUIRouter`、`SocialOnlineUIRouter`

---

## 4. 客户端全局 UI 路由：GlobalUIRouter
### 4.1 定位
`GlobalUIRouter` 是客户端全局导航总线。它只关心大状态的跃迁：
- 我现在是不是在大厅
- 我是不是掉出了房间
- 我是不是被踢下线
- 我是不是下载完录像

### 4.2 核心职责
- **登录成功后切到大厅**：监听 `S2C_LoginResult`。
- **离开房间后回退大厅**：监听 `Local_RoomLeft`。
- **被踢下线时回退登录**：监听 `S2C_KickOut`。
- **下载完录像后切回放面板**：监听 `S2C_DownloadReplayResult`。
- **物理断连后的 UI 处理**：`HandlePhysicalDisconnect()`。

它**不应该**直接处理房间内某个组件的局部 UI 切换（如社交房开始游戏后打开 `Panel_SocialRoomView`），这些属于房间组件 Router 的责任。

---

## 5. 房间组件运行时 UI 路由：RoomUIRouterBase<T>
### 5.1 定位
让每个 `ClientRoomComponent` 都可以有自己的 UI 生命周期控制器，并且在线态和回放态可以分别实现。

### 5.2 核心职责
- **绑定组件**：`Bind(T component)`，记录状态并调用 `OnBind`。
- **解绑组件**：`Unbind()`，调用 `OnUnbind`，负责把**自己开的 UI 收干净**。
- **生命周期收尾**：`OnDestroy()` 自动调用 `Unbind()`。

Router **只负责**把“当前房间组件已进入/离开/处于在线/处于回放”的事实，映射成 UI 打开关闭动作。它**不负责**收网络包、修改房间模型或业务状态计算。

---

## 6. 在线态 UI 路由与回放态 UI 路由的差异
当前工程明确把房间组件路由拆成两类：`XXXOnlineUIRouter` 和 `XXXReplayUIRouter`。

### 6.1 在线态 Router
关心交互、输入、房主权限、按钮响应、结束对局。例如 `SocialOnlineUIRouter`。

### 6.2 回放态 Router
关心只读展示、与时间轴同步、屏蔽在线业务请求。例如 `SocialReplayUIRouter`（当前为空实现，明确表示社交房在线 UI 不应自动复用到回放态）。

---

## 7. 当前工程中的实际 Router 列表
- **全局 Router**：`GlobalUIRouter`
- **房间设置组件 Router**：`RoomSettingsOnlineUIRouter` (打开房间面板、结算面板)、`RoomSettingsReplayUIRouter`
- **社交房组件 Router**：`SocialOnlineUIRouter` (监听 `S2C_GameStarted` 打开 `Panel_SocialRoomView`，监听 `S2C_GameEnded` 关闭它)、`SocialReplayUIRouter`

---

## 8. 全局事件与房间事件在 UI 路由中的分工
- **`GlobalTypeNetEvent`**：处理“跨房间、跨状态”的导航和全局交互（如登录、大厅、弱网）。适合 `GlobalUIRouter`、大厅 Panel。
- **`Room.NetEventSystem`**：处理“当前房间实例内”的业务表现与局部 UI（如房间快照、游戏开始、气泡）。适合组件级 Router、房间内 Panel。

---

## 9. 回放时 UI 路由的真实工作方式
回放不是给在线房间加一个标记位。回放是：
1. `ClientReplayPlayer` 创建本地沙盒房间 (`ClientAppState.ReplayRoom`)。
2. `ClientRoomFactory` 再装一套组件。
3. 组件决定自己在 Replay 模式使用哪个 Router。

> **回放的 UI 路由本质上是“ReplayRoom 的 UI 路由”，不是“在线房间 UI 上加回放分支”。**

---

## 10. Component、Router、UIPanel 的职责边界 (极度重要)

### 10.1 `ClientRoomComponent` 负责什么
接网络、持轻状态、抛事件、决定当前模式下挂哪个 Router。**不负责做全局导航或管理 Panel 生命周期。**

### 10.2 Router 负责什么
接状态、管开关、收生命周期。**不负责处理网络底层或修改业务状态。**
*注意：Panel 绝不能自己决定“我什么时候该关闭”，比如 `Panel_SocialRoomView` 不能监听 `S2C_GameEnded` 来关闭自己，必须由 `SocialOnlineUIRouter` 来关闭它。*

### 10.3 UIPanel 负责什么
做显示、做交互、做输入。
**核心规范**：UIPanel 监听事件时，必须使用 `.UnRegisterWhenMonoDisable(this)` 或 `.UnRegisterWhenGameObjectDestroyed(gameObject)`。
特别是对于会被对象池回收或频繁隐藏的 UI，**必须**使用 `UnRegisterWhenMonoDisable`，防止 UI 隐藏后仍在后台响应网络事件（幽灵响应）。

---

## 11. 表现层坐标解耦：UGUIFollow 的引入
在早期的 Demo 中，UI 气泡往往会把 3D 坐标跟随逻辑写在业务脚本里。当前架构已彻底禁止这种做法。

### 规范做法 (以 `Panel_SocialRoomView` 为例)：
1. 实例化 UI 预制体。
2. 动态挂载 `SocialRoomBubbleItem`（纯业务表现，只管文本更新和倒计时销毁）。
3. 动态挂载 `UGUIFollowTarget`（纯底层基建，只管 3D 到 2D 的坐标映射与平滑跟随）。
4. 业务脚本不再持有 Transform 引用，彻底解耦。

---

## 12. 常见错误写法
- **错误 1**：在 `ClientRoomComponent` 里直接管理所有 Panel。
- **错误 2**：在 `Panel` 里决定进房 / 离房的大导航。
- **错误 3**：在线态和回放态共用同一个交互面板（充满 `if(isReplay)` 分支）。
- **错误 4**：房间销毁后不解绑 Router，导致面板不关、串 UI。
- **错误 5**：UI 监听事件忘记调用 `UnRegisterWhenMonoDisable`，导致隐藏状态下疯狂报错。
- **错误 6**：在 UI 业务脚本里写 `Camera.main.WorldToScreenPoint`。

---

## 13. 排障建议
- **UI 没打开**：检查 `ClientRoomComponent.OnInit()` 是否正确挂了 `OnlineUIRouter`，`OnBind()` 是否执行。
- **旧 UI 残留**：检查 `Router.Unbind()` 是否执行，`OnUnbind()` 里是否关闭了对应 Panel。
- **回放打开了在线交互 UI**：检查组件 `OnInit()` 是否误复用了 `OnlineUIRouter`。
- **UI 隐藏后后台疯狂报错**：检查事件注册是否漏写了 `.UnRegisterWhenMonoDisable(this)`。
- **UI 气泡不跟随或坐标错乱**：检查气泡父节点是否误挂了 `LayoutGroup`（会强制覆盖坐标），检查是否正确配置了 `UGUIFollowTarget`。

---

## 14. 总结
当前工程的 UI 路由已经形成了非常健康的结构：
- **`GlobalUIRouter` 管大导航**
- **`RoomUIRouterBase<T>` 管局部开关**
- **在线/回放 Router 明确分离**
- **Panel 只做展示，靠 `UnRegisterWhenMonoDisable` 防泄漏**
- **`UGUIFollow` 剥离坐标映射**

如果后续继续扩展新的房间业务，请始终保持：**一个房间组件，如果需要 UI，就优先同时补一对 OnlineUIRouter / ReplayUIRouter。**
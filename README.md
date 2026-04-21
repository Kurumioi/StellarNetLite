# StellarNet Lite

**StellarNet Lite** 是一款专为 Unity 商业化项目打造的**工程级、模块化、高可维护性** C# 网络联机框架。

框架核心设计遵循 **服务端绝对权威 (Server Authoritative)** 与 **客户端表现层解耦** 原则，严格贯彻 **Component Pattern** 与 **MSV (Model-Service-View)** 架构。框架拒绝使用 `SyncVar` 等黑盒自动同步方案，严禁在运行时使用反射。所有状态同步、指令处理与广播均通过显式的协议事件与静态生成的注册代码完成，确保数据流清晰、可控、可审计。

---

## 🎮 适用场景分析

### ✅ 最佳适用场景
StellarNet Lite 采用**房间制 (Room-Based)** 架构，特别适合以下类型的游戏：
- **社交/派对游戏**：如《糖豆人》、《鹅鸭杀》、社交聊天室。框架内置了完善的社交气泡、大厅聊天与房间管理能力。
- **中轻度竞技/合作**：如《Among Us》、回合制卡牌、塔防合作、轻度 ARPG。
- **教育/模拟训练**：多人同步教学、工业模拟仿真。
- **独立游戏商业化**：需要高性能、高可扩展性且易于长期维护的中小型联机项目。

### 📈 负载量参考
- **单房间容量**：建议 1 - 64 人。
- **单服承载**：在 1 核 2G 的 Linux 无头服务器上，配合 KCP 传输层，可稳定承载 500+ 同时在线玩家（取决于业务逻辑复杂度）。
- **同步频率**：支持 10Hz - 60Hz 的高频位置同步。

### ❌ 不建议使用的场景
- **大型多人在线 (MMORPG)**：框架基于房间隔离，不具备“无缝大地图”或“AOI 九宫格”裁剪算法，不适合千人同屏的开放世界。
- **极高实时性竞技 (FPS/格斗)**：虽然 KCP 延迟极低，但本框架目前**未内置**“客户端预预测 (Client-Side Prediction)”与“延迟补偿 (Lag Compensation)”的物理回滚机制。对于《守望先锋》级别的极高实时性要求，需要开发者自行扩展物理回滚逻辑。
- **纯单机游戏**：框架设计为服务端权威，单机使用会增加不必要的异步通信开销。

---

## ✨ 核心特性

- **服务端权威与表现解耦**：服务端维护真实状态机，客户端仅负责输入采集、状态缓存、预测平滑与表现层渲染。
- **零反射运行时**：通过静态生成的注册表与绑定器，保障 Runtime 极致性能与极低 GC 压力。
- **原生回放与断线重连**：基于同一套快照逻辑 (`IReplaySnapshotProvider`)，完美支持录像 Seek 跳转与断线重连。
- **可插拔传输层**：内置生产级 KCP、教学级 TCP 与实验级 UDP。
- **轻量级异步持久化**：提供非侵入式的异步任务追踪与停机排空机制，自由接入 JSON、MySQL 或 WebAPI。

---

## 📂 目录结构

```text
Assets/StellarNetLite/
├── Runtime/          # 框架核心骨架 (AppManager, Session, Room, Transport)
├── Extensions/       # 官方默认扩展 (DefaultGameFlow, ObjectSync, Replay, NetworkMonitoring)
├── Editor/           # 编辑器工具链 (代码生成器, 配置面板, 压测工具)
├── Doc/              # 详尽的工程级教学文档
└── Samples/          # 示例工程 (SocialDemo)
```

---

## 🚀 快速上手

1. **运行 Demo**：打开 `Assets/StellarNetLite/Samples/SocialDemo/Client/Scenes/KCP/KCPHostScene.unity` 并 Play。
2. **生成代码**：修改协议或组件后，点击菜单 `StellarNetLite -> 重新生成协议与组件常量表`。
3. **网络配置**：点击菜单 `StellarNetLite -> 网络配置 (NetConfig)` 可视化修改 IP 和端口。

---

## 🛠️ 开发规范

- **Early Return**：统一采用 Early Return 拦截非法状态，拒绝深层嵌套。
- **严禁反射**：运行时业务逻辑严禁使用反射。
- **显式通信**：拒绝 `SyncVar`，统一使用基于协议事件的显式通信。
- **表现层隔离**：Runtime 严禁直接操作 UI。必须通过事件总线抛出事件，由 UI Router 监听。

---

## ⚖️ 许可协议
本项目遵循 **MIT License**。详情请参阅项目根目录下的 [LICENSE](LICENSE) 文件。

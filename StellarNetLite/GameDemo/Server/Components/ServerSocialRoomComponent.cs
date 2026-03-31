using System.Collections.Generic;
using StellarNet.Lite.Game.Shared.Protocol;
using StellarNet.Lite.Server.Components;
using StellarNet.Lite.Server.Core;
using StellarNet.Lite.Shared.Core;
using StellarNet.Lite.Shared.Infrastructure;
using StellarNet.Lite.Shared.Protocol;
using UnityEngine;

namespace StellarNet.Lite.Game.Server.Components
{
    // Demo 交友房服务端组件。
    [RoomComponent(102, "SocialRoom", "简易交友房间")]
    public sealed class ServerSocialRoomComponent : RoomComponent, ITickableComponent
    {
        private readonly ServerApp _app;
        // 对象同步服务，负责网络实体生成与同步。
        private ServerObjectSyncComponent _syncService;

        // SessionId -> NetId 映射。
        private readonly Dictionary<string, int> _sessionToNetId = new Dictionary<string, int>();
        // 临时动作结束时间表，用于自动回 Idle。
        private readonly Dictionary<int, float> _actionEndTimes = new Dictionary<int, float>();

        // Demo 玩家预制体 Hash。
        private const int PlayerPrefabHash = NetPrefabConsts.NetPrefabs_SocialPlayer;
        // Demo 用到的动画状态 Hash。
        private static readonly int AnimHash_Idle = Animator.StringToHash("Idle");
        private static readonly int AnimHash_Walk = Animator.StringToHash("Walk");
        private static readonly int AnimHash_Wave = Animator.StringToHash("Wave");
        private static readonly int AnimHash_Dance = Animator.StringToHash("Dance");
        // 玩家移动速度。
        private const float PlayerMoveSpeed = 4.0f;

        public ServerSocialRoomComponent(ServerApp app)
        {
            _app = app;
        }

        public override void OnInit()
        {
            // 组件初始化时清空运行态缓存。
            _sessionToNetId.Clear();
            _actionEndTimes.Clear();

            if (Room == null)
            {
                NetLogger.LogError("ServerSocialRoomComponent", "初始化失败: Room 为空");
                return;
            }

            // 交友房依赖对象同步组件承载玩家实体。
            _syncService = Room.GetComponent<ServerObjectSyncComponent>();
            if (_syncService == null)
            {
                NetLogger.LogError("ServerSocialRoomComponent", $"初始化失败: 缺失 ServerObjectSyncComponent, RoomId:{Room.RoomId}");
            }
        }

        public override void OnGameStart()
        {
            if (Room == null)
            {
                NetLogger.LogError("ServerSocialRoomComponent", "开始游戏失败: Room 为空");
                return;
            }

            if (_syncService == null)
            {
                NetLogger.LogError("ServerSocialRoomComponent", $"开始游戏失败: _syncService 为空, RoomId:{Room.RoomId}");
                return;
            }

            // 开局时为房间内每个成员生成一个玩家实体。
            _sessionToNetId.Clear();
            _actionEndTimes.Clear();

            foreach (var kvp in Room.Members)
            {
                SpawnPlayerForSession(kvp.Value);
            }
        }

        public override void OnMemberJoined(Session session)
        {
            if (session == null)
            {
                NetLogger.LogError("ServerSocialRoomComponent", $"成员加入失败: session 为空, RoomId:{Room?.RoomId ?? "-"}");
                return;
            }

            if (Room == null)
            {
                NetLogger.LogError("ServerSocialRoomComponent", $"成员加入失败: Room 为空, SessionId:{session.SessionId}");
                return;
            }

            if (Room.State == RoomState.Playing)
            {
                // 游戏中途加入时需要即时补生成实体。
                SpawnPlayerForSession(session);
            }
        }

        public override void OnMemberLeft(Session session)
        {
            if (session == null)
            {
                NetLogger.LogError("ServerSocialRoomComponent", $"成员离开失败: session 为空, RoomId:{Room?.RoomId ?? "-"}");
                return;
            }

            if (_sessionToNetId.TryGetValue(session.SessionId, out int netId))
            {
                // 离房时销毁该成员对应的网络实体。
                _syncService?.DestroyObject(netId);
                _sessionToNetId.Remove(session.SessionId);
                _actionEndTimes.Remove(netId);
            }
        }

        public override void OnGameEnd()
        {
            if (_syncService != null)
            {
                // 结算时统一清理所有玩家实体。
                foreach (var kvp in _sessionToNetId)
                {
                    _syncService.DestroyObject(kvp.Value);
                }
            }

            _sessionToNetId.Clear();
            _actionEndTimes.Clear();
        }

        private void SpawnPlayerForSession(Session session)
        {
            if (session == null)
            {
                NetLogger.LogError("ServerSocialRoomComponent", $"生成玩家失败: session 为空, RoomId:{Room?.RoomId ?? "-"}");
                return;
            }

            if (Room == null)
            {
                NetLogger.LogError("ServerSocialRoomComponent", $"生成玩家失败: Room 为空, SessionId:{session.SessionId}");
                return;
            }

            if (_syncService == null)
            {
                NetLogger.LogError("ServerSocialRoomComponent", $"生成玩家失败: _syncService 为空, RoomId:{Room.RoomId}, SessionId:{session.SessionId}");
                return;
            }

            if (_sessionToNetId.ContainsKey(session.SessionId))
            {
                // 已存在实体映射则不重复生成。
                return;
            }

            // 在房间中心附近随机一个出生点。
            Vector2 randomCircle = Random.insideUnitCircle * 3f;
            Vector3 spawnPos = new Vector3(randomCircle.x, 0f, randomCircle.y);

            ServerSyncEntity syncEntity = _syncService.SpawnObject(
                PlayerPrefabHash,
                EntitySyncMask.All,
                spawnPos,
                Vector3.zero,
                Vector3.zero,
                session.SessionId);

            if (syncEntity == null)
            {
                NetLogger.LogError("ServerSocialRoomComponent", $"生成玩家失败: SpawnObject 返回 null, RoomId:{Room.RoomId}, SessionId:{session.SessionId}");
                return;
            }

            // 初始动画默认站立。
            syncEntity.AnimStateHash = AnimHash_Idle;
            syncEntity.AnimNormalizedTime = 0f;
            _sessionToNetId.Add(session.SessionId, syncEntity.NetId);
        }

        public void OnTick()
        {
            if (Room == null || _app == null)
            {
                return;
            }

            if (Room.State != RoomState.Playing || _syncService == null)
            {
                return;
            }

            // 用固定 Tick 驱动服务端权威移动和动画切换。
            float deltaTime = 1f / _app.Config.TickRate;
            float currentTime = Time.realtimeSinceStartup;

            foreach (var kvp in _sessionToNetId)
            {
                ServerSyncEntity playerSync = _syncService.GetEntity(kvp.Value);
                if (playerSync == null)
                {
                    continue;
                }

                if (playerSync.Velocity.sqrMagnitude > 0.01f)
                {
                    // 有速度时推进位置并切到 Walk。
                    playerSync.Position += playerSync.Velocity * deltaTime;

                    if (playerSync.AnimStateHash != AnimHash_Walk)
                    {
                        playerSync.AnimStateHash = AnimHash_Walk;
                        playerSync.AnimNormalizedTime = 0f;
                    }
                }
                else
                {
                    // 停止移动后回 Idle，或等待动作播放结束。
                    if (playerSync.AnimStateHash == AnimHash_Walk)
                    {
                        playerSync.AnimStateHash = AnimHash_Idle;
                        playerSync.AnimNormalizedTime = 0f;
                    }
                    else if (playerSync.AnimStateHash == AnimHash_Wave || playerSync.AnimStateHash == AnimHash_Dance)
                    {
                        if (_actionEndTimes.TryGetValue(playerSync.NetId, out float endTime) && currentTime > endTime)
                        {
                            playerSync.AnimStateHash = AnimHash_Idle;
                            playerSync.AnimNormalizedTime = 0f;
                        }
                    }
                }
            }
        }

        [NetHandler]
        public void OnC2S_SocialMoveReq(Session session, C2S_SocialMoveReq msg)
        {
            if (session == null || msg == null)
            {
                NetLogger.LogError("ServerSocialRoomComponent", $"处理移动失败: session 或 msg 为空, Session:{session?.SessionId ?? "null"}");
                return;
            }

            if (Room == null || Room.State != RoomState.Playing || _syncService == null)
            {
                NetLogger.LogWarning("ServerSocialRoomComponent",
                    $"处理移动被拦截: 房间未处于可移动状态, RoomId:{Room?.RoomId ?? "-"}, State:{Room?.State.ToString() ?? "null"}, SessionId:{session.SessionId}");
                return;
            }

            if (!_sessionToNetId.TryGetValue(session.SessionId, out int netId))
            {
                NetLogger.LogWarning("ServerSocialRoomComponent", $"处理移动失败: 找不到玩家实体映射, SessionId:{session.SessionId}, RoomId:{Room.RoomId}");
                return;
            }

            ServerSyncEntity playerSync = _syncService.GetEntity(netId);
            if (playerSync == null)
            {
                NetLogger.LogError("ServerSocialRoomComponent", $"处理移动失败: 玩家实体不存在, NetId:{netId}, SessionId:{session.SessionId}, RoomId:{Room.RoomId}");
                return;
            }

            // 输入方向超过 1 时归一化，避免斜向更快。
            Vector3 inputDir = new Vector3(msg.DirX, 0f, msg.DirZ);
            if (inputDir.sqrMagnitude > 1f)
            {
                inputDir.Normalize();
            }

            // 服务端写入权威速度和朝向。
            playerSync.Velocity = inputDir * PlayerMoveSpeed;
            if (inputDir.sqrMagnitude > 0.01f)
            {
                playerSync.Rotation = Quaternion.LookRotation(inputDir).eulerAngles;
            }
        }

        [NetHandler]
        public void OnC2S_SocialActionReq(Session session, C2S_SocialActionReq msg)
        {
            if (session == null || msg == null)
            {
                NetLogger.LogError("ServerSocialRoomComponent", $"处理动作失败: session 或 msg 为空, Session:{session?.SessionId ?? "null"}");
                return;
            }

            if (Room == null || Room.State != RoomState.Playing || _syncService == null)
            {
                NetLogger.LogWarning("ServerSocialRoomComponent",
                    $"处理动作被拦截: 房间未处于可操作状态, RoomId:{Room?.RoomId ?? "-"}, State:{Room?.State.ToString() ?? "null"}, SessionId:{session.SessionId}");
                return;
            }

            if (!_sessionToNetId.TryGetValue(session.SessionId, out int netId))
            {
                NetLogger.LogWarning("ServerSocialRoomComponent", $"处理动作失败: 找不到玩家实体映射, SessionId:{session.SessionId}, RoomId:{Room.RoomId}");
                return;
            }

            ServerSyncEntity playerSync = _syncService.GetEntity(netId);
            if (playerSync == null)
            {
                NetLogger.LogError("ServerSocialRoomComponent", $"处理动作失败: 玩家实体不存在, NetId:{netId}, SessionId:{session.SessionId}, RoomId:{Room.RoomId}");
                return;
            }

            // 动作期间强制停下，避免边走边播动作。
            playerSync.Velocity = Vector3.zero;

            if (msg.ActionId == 1)
            {
                // 挥手动作。
                playerSync.AnimStateHash = AnimHash_Wave;
                playerSync.AnimNormalizedTime = 0f;
                _actionEndTimes[netId] = Time.realtimeSinceStartup + 2.5f;
            }
            else if (msg.ActionId == 2)
            {
                // 跳舞动作。
                playerSync.AnimStateHash = AnimHash_Dance;
                playerSync.AnimNormalizedTime = 0f;
                _actionEndTimes[netId] = Time.realtimeSinceStartup + 4.0f;
            }
            else
            {
                NetLogger.LogWarning("ServerSocialRoomComponent", $"未知动作 ID，已忽略, ActionId:{msg.ActionId}, SessionId:{session.SessionId}, RoomId:{Room.RoomId}");
            }
        }

        [NetHandler]
        public void OnC2S_SocialBubbleReq(Session session, C2S_SocialBubbleReq msg)
        {
            if (session == null || msg == null || string.IsNullOrWhiteSpace(msg.Content))
            {
                NetLogger.LogError("ServerSocialRoomComponent", $"处理聊天气泡失败: 参数非法, Session:{session?.SessionId ?? "null"}, RoomId:{Room?.RoomId ?? "-"}");
                return;
            }

            if (Room == null || Room.State != RoomState.Playing)
            {
                NetLogger.LogWarning("ServerSocialRoomComponent",
                    $"处理聊天气泡被拦截: 房间未在游戏中, RoomId:{Room?.RoomId ?? "-"}, State:{Room?.State.ToString() ?? "null"}, SessionId:{session.SessionId}");
                return;
            }

            if (!_sessionToNetId.TryGetValue(session.SessionId, out int netId))
            {
                NetLogger.LogWarning("ServerSocialRoomComponent", $"处理聊天气泡失败: 找不到玩家实体映射, SessionId:{session.SessionId}, RoomId:{Room.RoomId}");
                return;
            }

            // 聊天气泡只做长度裁剪，不录入回放。
            string safeContent = msg.Content.Length > 30 ? msg.Content.Substring(0, 30) + "..." : msg.Content;
            Room.BroadcastMessage(new S2C_SocialBubbleSync
            {
                NetId = netId,
                Content = safeContent
            }, false);
        }
    }
}

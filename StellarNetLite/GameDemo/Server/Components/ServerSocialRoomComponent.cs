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
    [RoomComponent(102, "SocialRoom", "简易交友房间")]
    public sealed class ServerSocialRoomComponent : RoomComponent, ITickableComponent
    {
        private readonly ServerApp _app;
        private ServerObjectSyncComponent _syncService;

        private readonly Dictionary<string, int> _sessionToNetId = new Dictionary<string, int>();
        private readonly Dictionary<int, float> _actionEndTimes = new Dictionary<int, float>();

        private const int PlayerPrefabHash = NetPrefabConsts.NetPrefabs_SocialPlayer;
        private static readonly int AnimHash_Idle = Animator.StringToHash("Idle");
        private static readonly int AnimHash_Walk = Animator.StringToHash("Walk");
        private static readonly int AnimHash_Wave = Animator.StringToHash("Wave");
        private static readonly int AnimHash_Dance = Animator.StringToHash("Dance");
        private const float PlayerMoveSpeed = 4.0f;

        public ServerSocialRoomComponent(ServerApp app)
        {
            _app = app;
        }

        public override void OnInit()
        {
            _sessionToNetId.Clear();
            _actionEndTimes.Clear();

            if (Room == null)
            {
                NetLogger.LogError("ServerSocialRoomComponent", "初始化失败: Room 为空");
                return;
            }

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
                _syncService?.DestroyObject(netId);
                _sessionToNetId.Remove(session.SessionId);
                _actionEndTimes.Remove(netId);
            }
        }

        public override void OnGameEnd()
        {
            if (_syncService != null)
            {
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
                return;
            }

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
                    playerSync.Position += playerSync.Velocity * deltaTime;

                    if (playerSync.AnimStateHash != AnimHash_Walk)
                    {
                        playerSync.AnimStateHash = AnimHash_Walk;
                        playerSync.AnimNormalizedTime = 0f;
                    }
                }
                else
                {
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

            Vector3 inputDir = new Vector3(msg.DirX, 0f, msg.DirZ);
            if (inputDir.sqrMagnitude > 1f)
            {
                inputDir.Normalize();
            }

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

            playerSync.Velocity = Vector3.zero;

            if (msg.ActionId == 1)
            {
                playerSync.AnimStateHash = AnimHash_Wave;
                playerSync.AnimNormalizedTime = 0f;
                _actionEndTimes[netId] = Time.realtimeSinceStartup + 2.5f;
            }
            else if (msg.ActionId == 2)
            {
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

            string safeContent = msg.Content.Length > 30 ? msg.Content.Substring(0, 30) + "..." : msg.Content;
            Room.BroadcastMessage(new S2C_SocialBubbleSync
            {
                NetId = netId,
                Content = safeContent
            }, false);
        }
    }
}
using System.Collections.Generic;
using StellarNet.Lite.Shared.Protocol;

namespace StellarNet.Lite.Shared.Binders
{
    /// <summary>
    /// 房间类型模板定义。
    /// 我把“一个房间类型应该挂哪些组件”集中定义在这里，
    /// 是为了让建房 UI 面向业务模板，而不是把底层组件拼装细节暴露给用户或策划。
    /// </summary>
    public static class RoomTypeTemplateRegistry
    {
        /// <summary>
        /// 房间类型模板元数据。
        /// 我保留最小字段，是为了让建房面板只关心“名称 + 组件列表”，避免现在阶段把模板系统做得过重。
        /// </summary>
        public sealed class RoomTypeTemplate
        {
            public string TypeName;
            public int[] ComponentIds;
        }

        /// <summary>
        /// 当前所有房间类型模板。
        /// 我采用手写静态表，是为了先用最低复杂度把“房间类型 -> 组件清单”的关系收口，
        /// 后续如果模板数量增多，再升级成配置文件或扫描器生成也不会推翻当前调用面。
        /// </summary>
        private static readonly List<RoomTypeTemplate> Templates = new List<RoomTypeTemplate>
        {
            new RoomTypeTemplate
            {
                TypeName = "交友房间",
                ComponentIds = new[]
                {
                    ComponentIdConst.SocialRoom,
                    ComponentIdConst.RoomSettings,
                    ComponentIdConst.ObjectSync
                }
            },

            // 这里给你预留一个打怪房间示例。
            // 等你后面真正有 MonsterRoom / BattleRoom 对应组件常量后，把下面这一段放开即可。
            //
            // new RoomTypeTemplate
            // {
            //     TypeName = "打怪房间",
            //     ComponentIds = new[]
            //     {
            //         ComponentIdConst.MonsterRoom,
            //         ComponentIdConst.RoomSettings,
            //         ComponentIdConst.ObjectSync
            //     }
            // }
        };

        /// <summary>
        /// 获取全部房间类型模板。
        /// 我返回只读列表，是为了防止 UI 层直接改写模板定义，保证模板注册表始终是单向只读事实源。
        /// </summary>
        public static IReadOnlyList<RoomTypeTemplate> GetAllTemplates()
        {
            return Templates;
        }

        /// <summary>
        /// 根据名称查找模板。
        /// 我保留这个入口，是为了让 UI 层和后续调试工具都能通过统一方式取模板，而不是各处重复遍历。
        /// </summary>
        public static RoomTypeTemplate GetByTypeName(string typeName)
        {
            if (string.IsNullOrEmpty(typeName))
            {
                return null;
            }

            for (int i = 0; i < Templates.Count; i++)
            {
                RoomTypeTemplate template = Templates[i];
                if (template == null)
                {
                    continue;
                }

                if (template.TypeName == typeName)
                {
                    return template;
                }
            }

            return null;
        }

        /// <summary>
        /// 根据索引取模板。
        /// 我提供索引访问，是为了让建房面板可以直接按列表顺序渲染并拿回选中模板，减少额外映射代码。
        /// </summary>
        public static RoomTypeTemplate GetByIndex(int index)
        {
            if (index < 0 || index >= Templates.Count)
            {
                return null;
            }

            return Templates[index];
        }
    }
}
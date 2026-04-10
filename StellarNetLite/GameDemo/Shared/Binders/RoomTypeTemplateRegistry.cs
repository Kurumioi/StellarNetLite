using System.Collections.Generic;
using StellarNet.Lite.Shared.Protocol;

namespace StellarNet.Lite.Shared.Binders
{
    /// <summary>
    /// 房间类型模板注册表。
    /// </summary>
    public static class RoomTypeTemplateRegistry
    {
        /// <summary>
        /// 房间类型模板。
        /// </summary>
        public sealed class RoomTypeTemplate
        {
            /// <summary>
            /// 模板名称。
            /// </summary>
            public string TypeName;

            /// <summary>
            /// 模板对应的组件 Id 列表。
            /// </summary>
            public int[] ComponentIds;
        }

        // 当前可选的房间类型模板列表。
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
            }

            // 如需新增模板，可继续在此追加。
        };

        /// <summary>
        /// 获取全部房间类型模板。
        /// </summary>
        public static IReadOnlyList<RoomTypeTemplate> GetAllTemplates()
        {
            return Templates;
        }

        /// <summary>
        /// 按名称查找房间类型模板。
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
        /// 按索引获取房间类型模板。
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

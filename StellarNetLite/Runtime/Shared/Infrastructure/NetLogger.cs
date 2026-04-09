using System.Diagnostics;
using Debug = UnityEngine.Debug;

namespace StellarNet.Lite.Shared.Infrastructure
{
    /// <summary>
    /// 生产级结构化日志记录器。
    /// 核心修复：移除 LogError 的宏裁剪，确保任何环境下 Error 都能被捕获。
    /// </summary>
    public static class NetLogger
    {
        // 普通信息日志，可被宏关闭。
        [Conditional("ENABLE_LOG")]
        public static void LogInfo(string module, string message, string roomId = "-", string sessionId = "-", string extraContext = "")
        {
            Debug.Log(FormatMessage("INFO", module, message, roomId, sessionId, extraContext));
        }

        // 警告日志，可被宏关闭。
        [Conditional("ENABLE_LOG")]
        public static void LogWarning(string module, string message, string roomId = "-", string sessionId = "-", string extraContext = "")
        {
            Debug.LogWarning(FormatMessage("WARN", module, message, roomId, sessionId, extraContext));
        }

        // 核心修复：Error 级别绝不允许被宏裁剪
        public static void LogError(string module, string message, string roomId = "-", string sessionId = "-", string extraContext = "")
        {
            Debug.LogError(FormatMessage("ERROR", module, message, roomId, sessionId, extraContext));
        }

        // 统一拼装结构化日志文本。
        private static string FormatMessage(string level, string module, string message, string roomId, string sessionId, string extraContext)
        {
            int fontSize = 14;
            string colorTag;
            switch (level)
            {
                case "Error": colorTag = "#FF0000"; break; // 红色
                case "Warning": colorTag = "#FFFF00"; break; // 黄色
                default: colorTag = "#FFFFFF"; break; // 白色
            }

            string roomStr = string.IsNullOrEmpty(roomId) ? "-" : roomId;
            string sessionStr = string.IsNullOrEmpty(sessionId) ? "-" : sessionId;
            string contextStr = string.IsNullOrEmpty(extraContext) ? "" : $" | Context: {extraContext}";
            return
                $"<color={colorTag}><size={fontSize}><b>[{level}][{module}]</b></size></color> [Room:{roomStr}][Session:{sessionStr}] {message}{contextStr}";
        }
    }
}
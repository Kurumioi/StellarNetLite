using System.Diagnostics;
using Debug = UnityEngine.Debug;

namespace StellarNet.Lite.Shared.Infrastructure
{
    /// <summary>
    /// 统一输出运行时日志。
    /// </summary>
    public static class NetLogger
    {
        /// <summary>
        /// 输出普通流程日志。
        /// </summary>
        [Conditional("ENABLE_LOG")]
        public static void LogInfo(string module, string message, string roomId = "-", string sessionId = "-", string extraContext = "")
        {
            Debug.Log(FormatMessage("INFO", module, message, roomId, sessionId, extraContext));
        }

        /// <summary>
        /// 输出非致命告警日志。
        /// </summary>
        [Conditional("ENABLE_LOG")]
        public static void LogWarning(string module, string message, string roomId = "-", string sessionId = "-", string extraContext = "")
        {
            Debug.LogWarning(FormatMessage("WARN", module, message, roomId, sessionId, extraContext));
        }

        /// <summary>
        /// 输出致命或非法状态日志。
        /// </summary>
        public static void LogError(string module, string message, string roomId = "-", string sessionId = "-", string extraContext = "")
        {
            Debug.LogError(FormatMessage("ERROR", module, message, roomId, sessionId, extraContext));
        }

        /// <summary>
        /// 拼装统一日志文本。
        /// </summary>
        private static string FormatMessage(string level, string module, string message, string roomId, string sessionId, string extraContext)
        {
            // 统一把不同级别日志包装成同一格式，方便运行期直接筛选。
            const int FontSize = 14;
            string colorTag = level switch
            {
                "ERROR" => "#FF0000",
                "WARN" => "#FFFF00",
                _ => "#FFFFFF"
            };

            string roomStr = string.IsNullOrEmpty(roomId) ? "-" : roomId;
            string sessionStr = string.IsNullOrEmpty(sessionId) ? "-" : sessionId;
            string contextStr = string.IsNullOrEmpty(extraContext) ? string.Empty : $" | {extraContext}";
            return
                $"<color={colorTag}><size={FontSize}><b>[{level}][{module}]</b></size></color> [Room:{roomStr}][Session:{sessionStr}] {message}{contextStr}";
        }
    }
}

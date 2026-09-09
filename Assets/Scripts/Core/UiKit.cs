using UnityEngine;

namespace BattleFortress
{
    /// <summary>
    /// UI 运行时公共工具：统一内置字体获取等，避免在多个 UI 组件里各写一份。
    /// </summary>
    public static class UiKit
    {
        private static Font _font;

        /// <summary>Unity 内置运行时字体（2022 版优先 LegacyRuntime，旧版回退 Arial）</summary>
        public static Font RuntimeFont()
        {
            if (_font != null) return _font;
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (_font == null) _font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            return _font;
        }
    }
}

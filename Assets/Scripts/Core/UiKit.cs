using UnityEngine;
using UnityEngine.UI;

namespace BattleFortress
{
    /// <summary>
    /// UI 运行时公共工具：统一字体获取/安装，避免在多个 UI 组件里各写一份。
    /// 字体随包发布（Resources/Fonts/NotoSansSC，OFL 开源中文字体），不依赖目标机器是否装了中文字体；
    /// 加载失败时回退 Unity 内置字体，保证任何平台都不会因为缺字体而崩。
    /// </summary>
    public static class UiKit
    {
        /// <summary>随包内嵌的中文字体 Resources 路径（不含扩展名）</summary>
        public const string BundledFontPath = "Fonts/NotoSansSC";

        private static Font _font;

        /// <summary>统一运行时字体：优先用随包中文字体，其次 Unity 内置 LegacyRuntime / Arial</summary>
        public static Font RuntimeFont()
        {
            if (_font != null) return _font;

            _font = Resources.Load<Font>(BundledFontPath);
            if (_font == null) _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (_font == null) _font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            return _font;
        }

        /// <summary>
        /// 场景加载后自动执行一次：把场景里（含未激活对象）在编辑器中写死为内置 Arial 的 Text
        /// 全部换成随包中文字体。代码动态创建的文本本就走 RuntimeFont()，这里只兜底序列化文本，
        /// 用 RuntimeInitializeOnLoadMethod 无需改动场景/预制。
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void InstallSceneFonts()
        {
            Font font = RuntimeFont();
            if (font == null) return;

            var texts = Resources.FindObjectsOfTypeAll<Text>();
            for (int i = 0; i < texts.Length; i++)
            {
                var t = texts[i];
                if (t == null || t.gameObject == null) continue;
                if (!t.gameObject.scene.IsValid()) continue; // 只改场景对象，跳过资源/prefab 资产
                if (t.font == font) continue;
                t.font = font;
                t.SetAllDirty();
            }
        }
    }
}

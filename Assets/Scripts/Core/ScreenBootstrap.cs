using UnityEngine;

namespace BattleFortress
{
    /// <summary>
    /// 运行时显示引导：游戏为横屏 16:9（设计分辨率 1334x750）。
    /// 用 RuntimeInitializeOnLoadMethod 在任意场景加载前自动执行，无需手动挂到场景，
    /// 避免编辑器里忘记挂组件导致竖屏。
    ///  - 移动端：锁定横屏，禁止自动转竖屏。
    ///  - 编辑器 / Standalone：把游戏窗口分辨率设为横屏 1334x750，方便在编辑器按横屏调试。
    /// </summary>
    public static class ScreenBootstrap
    {
        public const int DesignWidth = 1334;
        public const int DesignHeight = 750;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Init()
        {
#if UNITY_ANDROID || UNITY_IOS
            Screen.autorotateToPortrait = false;
            Screen.autorotateToPortraitUpsideDown = false;
            Screen.autorotateToLandscapeLeft = true;
            Screen.autorotateToLandscapeRight = true;
            Screen.orientation = ScreenOrientation.LandscapeLeft;
#else
            // 编辑器 / PC：以横屏窗口运行，保证所见即所得
            Screen.SetResolution(DesignWidth, DesignHeight, FullScreenMode.Windowed);
#endif
            Application.targetFrameRate = 60;
            QualitySettings.vSyncCount = 0;
        }
    }
}

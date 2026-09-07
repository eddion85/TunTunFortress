using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace BattleFortress.EditorTools
{
    /// <summary>
    /// 把编辑器 Game 视图切到横屏固定分辨率 1334x750（16:9）。
    /// 运行时真机的横屏由 ScreenBootstrap 负责；编辑器 Game 视图的面板比例不受
    /// Screen.SetResolution 控制，只能通过 GameView 的尺寸组设置，这里用反射完成。
    /// 打开工程编译后会自动尝试一次，也可点菜单「吞吞堡垒/横屏预览 1334x750」。
    /// </summary>
    [InitializeOnLoad]
    public static class GameViewLandscape
    {
        public const int W = 1334;
        public const int H = 750;
        private const string Label = "BattleFortress Landscape 1334x750";

        static GameViewLandscape()
        {
            EditorApplication.delayCall += AutoSet;
        }

        private static void AutoSet()
        {
            // batchmode/无界面时不处理
            if (Application.isBatchMode) return;
            TrySet();
        }

        [MenuItem("吞吞堡垒/横屏预览 1334x750")]
        public static void TrySet()
        {
            if (Application.isBatchMode) return;
            try
            {
                var asm = typeof(Editor).Assembly;
                var sizesType = asm.GetType("UnityEditor.GameViewSizes");
                var sizeType = asm.GetType("UnityEditor.GameViewSize");
                var sizeEnumType = asm.GetType("UnityEditor.GameViewSizeType");
                if (sizesType == null || sizeType == null || sizeEnumType == null)
                {
                    Debug.LogWarning("[横屏] 未找到 GameView 尺寸内部类型，当前 Unity 版本可能不兼容");
                    return;
                }

                var singleton = typeof(ScriptableSingleton<>).MakeGenericType(sizesType);
                var instanceProp = singleton.GetProperty("instance",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                var sizesInstance = instanceProp.GetValue(null, null);

                var groupProp = sizesType.GetProperty("currentGroup",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                var group = groupProp.GetValue(sizesInstance, null);
                var groupType = group.GetType();
                var mTotal = groupType.GetMethod("GetTotalCount");
                var mGet = groupType.GetMethod("GetGameViewSize");

                int FindIndex()
                {
                    int total = (int)mTotal.Invoke(group, null);
                    for (int i = 0; i < total; i++)
                    {
                        var sz = mGet.Invoke(group, new object[] { i });
                        int w = Convert.ToInt32(sizeType.GetProperty("width")?.GetValue(sz));
                        int h = Convert.ToInt32(sizeType.GetProperty("height")?.GetValue(sz));
                        if (w == W && h == H) return i;
                    }
                    return -1;
                }

                int idx = FindIndex();
                if (idx < 0)
                {
                    var ctor = sizeType.GetConstructor(new[] { sizeEnumType, typeof(int), typeof(int), typeof(string) });
                    var fixedRes = Enum.Parse(sizeEnumType, "FixedResolution");
                    var newSize = ctor.Invoke(new object[] { fixedRes, W, H, Label });
                    groupType.GetMethod("AddCustomSize").Invoke(group, new[] { newSize });
                    idx = FindIndex();
                }

                if (idx < 0)
                {
                    Debug.LogWarning("[横屏] 已添加尺寸但未能选中，请在 Game 视图顶部 Aspect 下拉手动选 " + Label);
                    return;
                }

                var gvType = asm.GetType("UnityEditor.GameView");
                var gv = EditorWindow.GetWindow(gvType, false, null, false);
                if (gv != null)
                {
                    var sel = gvType.GetProperty("selectedSizeIndex",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (sel != null) sel.SetValue(gv, idx, null);
                    var zoom = gvType.GetProperty("zoomScale",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    gv.Repaint();
                    Debug.Log("[横屏] Game 视图已切为 1334x750（16:9）");
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[横屏] 自动设置失败，可在 Game 视图顶部 Aspect 下拉手动添加 Fixed 1334x750。原因: " + e.Message);
            }
        }
    }
}

using System.Collections.Generic;
using UnityEngine;

namespace BattleFortress
{
    /// <summary>
    /// 弹体（敌人炮弹 / 玩家炮弹）外观的统一来源：danyao 模型路径、原始尺寸、纯黑染色、模板缓存。
    /// 玩家 AutoWeapon 与敌人 EnemyCombat 都从这里取，避免路径/材质/尺寸换算散落两处。
    /// </summary>
    public static class ProjectileVisual
    {
        // danyao 模型的 Resources 路径
        public const string SideShellPath = "art/models/proj/danyao1"; // 球形：侧炮
        public const string ShellPath = "art/models/proj/danyao2";     // 弹壳形：其余大炮
        // danyao 模型原始最长边（GLB 单位）：把“期望世界直径”换算成 localScale 用
        public const float SideShellRawSize = 2f;
        public const float ShellRawSize = 0.03f;

        private static readonly Dictionary<string, GameObject> _tplCache = new Dictionary<string, GameObject>();
        // 炮弹统一棕色（金属弹壳/弹丸的暖棕），玩家与敌人共用
        private static readonly Color ShellColor = new Color(0.42f, 0.27f, 0.15f);
        private static Material _shellMat;

        /// <summary>按 Resources 路径取弹体模板（带缓存）</summary>
        public static GameObject Load(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            if (_tplCache.TryGetValue(path, out var tpl)) return tpl;
            tpl = Resources.Load<GameObject>(path);
            _tplCache[path] = tpl;
            return tpl;
        }

        /// <summary>期望世界直径 + 模型原始尺寸 → 实例 localScale（三轴一致）</summary>
        public static float ToLocalScale(float worldDiameter, float rawSize)
        {
            return worldDiameter / Mathf.Max(1e-4f, rawSize);
        }

        /// <summary>把弹体所有渲染器材质换成统一棕色（共享一个材质，避免每发泄漏）</summary>
        public static void TintShell(GameObject go)
        {
            if (go == null) return;
            if (_shellMat == null)
            {
                var shader = Shader.Find("Standard")
                    ?? Shader.Find("Universal Render Pipeline/Lit")
                    ?? Shader.Find("Diffuse");
                _shellMat = new Material(shader) { color = ShellColor };
            }
            var renderers = go.GetComponentsInChildren<Renderer>();
            for (int i = 0; i < renderers.Length; i++)
                if (renderers[i] != null) renderers[i].sharedMaterial = _shellMat;
        }
    }
}

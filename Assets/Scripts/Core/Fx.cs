using UnityEngine;

namespace BattleFortress
{
    /// <summary>
    /// 表现层快捷入口（对应 LayaAir 版 config.ts 里的 playVfx / popDmg / shake）。
    /// 只负责发事件，实际播放由 VfxLayer / CameraFollow 监听执行，
    /// 这样战斗逻辑不用持有 UI 与相机引用。
    /// </summary>
    public static class Fx
    {
        /// <summary>在指定世界坐标播放一张特效贴图；life/rise/grow/alpha 可选覆盖默认表现</summary>
        public static void PlayVfx(string url, Vector3 pos, float scale = 1f,
            float life = -1f, float rise = -1f, float grow = -1f, float alpha = -1f, bool trail = false)
        {
            if (string.IsNullOrEmpty(url)) return;
            GameBus.Emit(GameEvents.Vfx, new VfxPayload
            {
                url = url,
                x = pos.x, y = pos.y, z = pos.z,
                scale = scale,
                life = life, rise = rise, grow = grow, alpha = alpha,
                trail = trail
            });
        }

        public static void PlayVfx(string url, float x, float y, float z, float scale = 1f)
        {
            PlayVfx(url, new Vector3(x, y, z), scale);
        }

        /// <summary>移动拖尾烟团：走 VfxLayer 独立拖尾池，参数全部来自 GameConfig</summary>
        public static void PlayTrailSmoke(Vector3 pos, float scale)
        {
            PlayVfx(VfxKeys.SmokePuff, pos, scale,
                GameConfig.TrailLife, GameConfig.TrailRise, GameConfig.TrailGrow, GameConfig.TrailAlpha, true);
        }

        /// <summary>世界空间飘伤害数字（在敌人头顶弹出，比固定屏幕位置更有打击感）</summary>
        public static void PopDmg(Vector3 pos, float dmg, bool big = false)
        {
            GameBus.Emit(GameEvents.Dmg, new DmgPayload
            {
                x = pos.x, y = pos.y + 1.2f, z = pos.z,
                text = Mathf.RoundToInt(dmg).ToString(),
                big = big
            });
        }

        /// <summary>相机震屏，power 越大越猛</summary>
        public static void Shake(float power)
        {
            GameBus.Emit(GameEvents.Shake, power);
        }
    }
}

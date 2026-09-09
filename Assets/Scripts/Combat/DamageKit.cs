using UnityEngine;

namespace BattleFortress
{
    /// <summary>
    /// 统一伤害结算入口：体积压制系数、飘字、命中/爆炸特效与音效都在这里收口，
    /// 武器（AutoWeapon）、地雷（SubWeapons）以及以后的新装备都调用本类，避免每个装备各写一套循环。
    /// </summary>
    public static class DamageKit
    {
        /// <summary>
        /// 单体命中：对一个敌人结算一次伤害（自动乘体积压制），并播命中特效/飘字。
        /// </summary>
        /// <param name="target">受击敌人</param>
        /// <param name="baseDamage">未乘体积压制的基础伤害（已含全局攻击加成）</param>
        /// <param name="fxPos">命中特效世界坐标</param>
        /// <param name="big">大号飘字</param>
        /// <param name="hitFxScale">命中贴片尺寸</param>
        /// <param name="ringScale">冲击波环尺寸，&lt;=0 不显示</param>
        /// <param name="shake">震屏强度，&lt;=0 不震</param>
        /// <param name="sfx">命中音效（SfxKeys），空 = 默认 HitEnemy</param>
        public static void DirectHit(EnemyUnit target, float baseDamage, Vector3 fxPos,
            bool big = false, float hitFxScale = 1.5f, float ringScale = 0f, float shake = 0f,
            string sfx = null)
        {
            if (target == null || target.Dead) return;

            float dmg = baseDamage * GS.SizeFactor(target.Size());
            target.Hp -= dmg;
            Fx.PopDmg(fxPos, dmg, big);

            AudioKit.PlaySfx(string.IsNullOrEmpty(sfx) ? SfxKeys.HitEnemy : sfx);
            Fx.PlayVfx(VfxKeys.HitSheet, fxPos.x, fxPos.y + 1f, fxPos.z, hitFxScale);
            if (ringScale > 0f) Fx.PlayVfx(VfxKeys.RingWave, fxPos.x, fxPos.y + 0.6f, fxPos.z, ringScale);
            if (shake > 0f) Fx.Shake(shake);
        }

        /// <summary>
        /// 范围爆炸：对落点半径内所有存活敌人各结算一次伤害（每个敌人单独算体积压制）。
        /// 同时播放地面爆炸序列帧 + 冲击波环 + 音效 + 震屏。
        /// </summary>
        /// <returns>本次爆炸实际命中的敌人数量</returns>
        public static int Explode(Vector3 center, float radius, float baseDamage,
            bool big = true, float explosionFxScale = 3f, float ringScale = 2.2f,
            float shake = 0.6f, string sfx = null)
        {
            // 落地爆炸特效贴地播放
            Fx.PlayVfx(VfxKeys.ExplosionSheet, center.x, 0.6f, center.z, explosionFxScale);
            if (ringScale > 0f) Fx.PlayVfx(VfxKeys.RingWave, center.x, 0.2f, center.z, ringScale);
            if (shake > 0f) Fx.Shake(shake);
            if (!string.IsNullOrEmpty(sfx)) AudioKit.PlaySfx(sfx);

            int hits = 0;
            float r2 = radius * radius;
            for (int i = 0; i < Registry.Enemies.Count; i++)
            {
                var e = Registry.Enemies[i];
                if (e == null || e.Dead || !e.gameObject.activeInHierarchy) continue;

                Vector3 ep = e.transform.position;
                float dx = ep.x - center.x;
                float dz = ep.z - center.z;
                if (dx * dx + dz * dz > r2) continue;

                float dmg = baseDamage * GS.SizeFactor(e.Size());
                e.Hp -= dmg;
                Fx.PopDmg(ep, dmg, big);
                hits++;
            }
            return hits;
        }
    }
}

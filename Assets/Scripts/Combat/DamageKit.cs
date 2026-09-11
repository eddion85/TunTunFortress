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
        /// <summary>
        /// 近战扇形突刺（攻城锤）：以 origin 为顶点、forward 为中轴、射程 range、左右各半角的扇形，
        /// 贴身处保留最小半宽避免尖点漏判；扇形内每个敌人各结算一次伤害，存活敌沿 forward 退一个车长。
        /// 与爆炸不同：这是有朝向的扇形区域，不是圆形。
        /// </summary>
        /// <returns>本次命中的敌人数量</returns>
        public static int Thrust(Vector3 origin, Vector3 forward, float range,
            float baseDamage, WeaponDef def)
        {
            int hits = 0;
            float fx = forward.x, fz = forward.z;
            float fl = Mathf.Sqrt(fx * fx + fz * fz);
            if (fl < 0.0001f) return 0;
            fx /= fl; fz /= fl;
            float rx = -fz, rz = fx; // 攻击带的右法向（横向半宽判定）

            for (int i = 0; i < Registry.Enemies.Count; i++)
            {
                var e = Registry.Enemies[i];
                if (e == null || e.Dead || !e.gameObject.activeInHierarchy) continue;

                Vector3 ep = e.transform.position;
                float ox = ep.x - origin.x, oz = ep.z - origin.z;
                float along = ox * fx + oz * fz;              // 纵向投影：0..range 才在带内
                float side = Mathf.Abs(ox * rx + oz * rz);   // 横向偏移
                // 扇形判定：纵向 0..range，横向宽度随距离张开（半角 tan），贴身保留最小半宽
                float fanSide = along * def.ThrustFanTan;
                float allowSide = Mathf.Max(def.ThrustMinWidth, fanSide);
                if (along < 0f || along > range || side > allowSide) continue;

                float dmg = baseDamage * GS.SizeFactor(e.Size());
                e.Hp -= dmg;
                if (e.IsBoss)
                {
                    Debug.Log($"[RamThrust] 命中 Boss：伤害={dmg:F1} 剩余血={e.Hp:F1}/{e.MaxHp:F1} 纵距={along:F2} 横距={side:F2} 射程={range:F1} 扇形半宽={allowSide:F2}");
                }
                Fx.PopDmg(ep, dmg, def.BigDamageNumber);
                Fx.PlayVfx(VfxKeys.HitSheet, ep.x, ep.y + 1f, ep.z, def.ImpactFxScale);
                hits++;

                // 一击已死不做击退；存活敌沿突刺方向整体后退一个自身体长
                if (!e.Dead && e.Hp > 0f)
                {
                    float speed = Mathf.Sqrt(2f * def.KnockDecel * e.WorldLength()); // v=√(2ad)，恰好退一个车长
                    e.ApplyKnockback(fx, fz, speed);
                }
            }

            if (hits > 0)
            {
                if (!string.IsNullOrEmpty(def.ImpactSfx)) AudioKit.PlaySfx(def.ImpactSfx);
                if (def.ImpactShake > 0f) Fx.Shake(def.ImpactShake);
            }
            return hits;
        }
        
    }
}

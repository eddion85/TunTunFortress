using System.Collections.Generic;

namespace BattleFortress
{
    /// <summary>
    /// 全局单位登记表（对应 LayaAir 版的 ENEMIES / PROJECTILES / DROPS 三个全局数组）。
    /// 各系统都从这里读写场上单位，避免互相持有引用。
    /// </summary>
    public static class Registry
    {
        public static readonly List<EnemyUnit> Enemies = new List<EnemyUnit>();
        public static readonly List<Projectile> Projectiles = new List<Projectile>();
        public static readonly List<DropItem> Drops = new List<DropItem>();

        /// <summary>重开局清场：销毁全部单位实例并清空列表</summary>
        public static void ClearAll()
        {
            for (int i = 0; i < Enemies.Count; i++)
                if (Enemies[i] != null && Enemies[i].gameObject != null)
                    ObjectPool.Despawn(Enemies[i].gameObject);
            Enemies.Clear();

            for (int i = 0; i < Projectiles.Count; i++)
                if (Projectiles[i] != null && Projectiles[i].gameObject != null)
                    ObjectPool.Despawn(Projectiles[i].gameObject);
            Projectiles.Clear();

            for (int i = 0; i < Drops.Count; i++)
                if (Drops[i] != null && Drops[i].gameObject != null)
                    ObjectPool.Despawn(Drops[i].gameObject);
            Drops.Clear();
        }

        /// <summary>统计场上存活（未标记死亡）的敌人数</summary>
        public static int AliveEnemyCount()
        {
            int n = 0;
            for (int i = 0; i < Enemies.Count; i++)
            {
                var e = Enemies[i];
                if (e != null && !e.Dead && e.gameObject != null) n++;
            }
            return n;
        }
    }
}

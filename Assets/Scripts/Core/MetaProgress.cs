using System;
using System.Collections.Generic;
using UnityEngine;

namespace BattleFortress
{
    [Serializable]
    public class StarEntry
    {
        public int level;
        public int star;
    }

    /// <summary>局外永久养成存档数据</summary>
    [Serializable]
    public class MetaData
    {
        public int gold = 0;
        public int atkLv = 0;
        public int hpLv = 0;
        public int devourLv = 0;
        public int unlocked = 1;
        public List<StarEntry> stars = new List<StarEntry>();
    }

    /// <summary>
    /// 局外养成 [设计：GDD 只留"接口"，本轮实现三线永久强化]
    /// 攻击 / 生命 / 吞噬各 5 级，消耗累计金币，存 PlayerPrefs。
    /// </summary>
    public static class Meta
    {
        private const string SaveKey = "bf_meta_v1";
        public static MetaData Data = new MetaData();

        public static void Load()
        {
            try
            {
                if (!PlayerPrefs.HasKey(SaveKey)) return;
                string raw = PlayerPrefs.GetString(SaveKey);
                if (string.IsNullOrEmpty(raw)) return;
                var d = JsonUtility.FromJson<MetaData>(raw);
                if (d != null)
                {
                    Data = d;
                    if (Data.stars == null) Data.stars = new List<StarEntry>();
                }
            }
            catch (Exception e)
            {
                // 存档损坏时用默认值继续，不阻断游戏
                Debug.LogWarning("[Meta] 存档读取失败，使用默认存档: " + e.Message);
                Data = new MetaData();
            }
        }

        public static void Save()
        {
            try
            {
                PlayerPrefs.SetString(SaveKey, JsonUtility.ToJson(Data));
                PlayerPrefs.Save();
            }
            catch (Exception)
            {
                // 写入失败可忽略
            }
        }

        public static int Cost(int lv)
        {
            return 80 + lv * 60;
        }

        /// <summary>升级一条养成线（atkLv / hpLv / devourLv），最高 5 级</summary>
        public static bool Upgrade(string line)
        {
            int lv = LevelOf(line);
            if (lv >= 5) return false;
            int c = Cost(lv);
            if (Data.gold < c) return false;
            Data.gold -= c;
            SetLevel(line, lv + 1);
            Save();
            GameBus.Emit(GameEvents.Meta, null);
            return true;
        }

        public static int LevelOf(string line)
        {
            switch (line)
            {
                case "atkLv": return Data.atkLv;
                case "hpLv": return Data.hpLv;
                case "devourLv": return Data.devourLv;
                default: return 0;
            }
        }

        private static void SetLevel(string line, int v)
        {
            switch (line)
            {
                case "atkLv": Data.atkLv = v; break;
                case "hpLv": Data.hpLv = v; break;
                case "devourLv": Data.devourLv = v; break;
            }
        }

        // 每级 +8% 攻击 / +10% 生命 / +6% 吞噬圈
        public static float AtkMul() { return 1f + Data.atkLv * 0.08f; }
        public static float HpMul() { return 1f + Data.hpLv * 0.1f; }
        public static float DevourMul() { return 1f + Data.devourLv * 0.06f; }

        public static int StarOf(int levelId)
        {
            for (int i = 0; i < Data.stars.Count; i++)
                if (Data.stars[i].level == levelId) return Data.stars[i].star;
            return 0;
        }

        public static void SetStar(int levelId, int star)
        {
            int cur = StarOf(levelId);
            if (star > cur)
            {
                bool found = false;
                for (int i = 0; i < Data.stars.Count; i++)
                {
                    if (Data.stars[i].level == levelId) { Data.stars[i].star = star; found = true; break; }
                }
                if (!found) Data.stars.Add(new StarEntry { level = levelId, star = star });
            }
            if (star > 0 && levelId + 1 > Data.unlocked) Data.unlocked = levelId + 1;
            Save();
        }
    }
}

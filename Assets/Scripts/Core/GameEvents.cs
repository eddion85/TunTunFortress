using System;
using System.Collections.Generic;

namespace BattleFortress
{
    /// <summary>全局事件名（对应 LayaAir 版 EV）</summary>
    public static class GameEvents
    {
        public const string LevelUp = "bf_levelup";
        public const string Result = "bf_result";
        public const string Float = "bf_float";
        public const string Evolve = "bf_evolve";
        public const string Restart = "bf_restart";
        public const string Skill = "bf_skill";
        public const string Sfx = "bf_sfx";
        public const string Vfx = "bf_vfx";
        public const string Combo = "bf_combo";
        public const string Boss = "bf_boss";
        public const string Pause = "bf_pause";
        public const string Revive = "bf_revive";   // 死亡后原地复活（payload=bool 是否清空普通兵）
        public const string Meta = "bf_meta";
        public const string Hurt = "bf_hurt";
        public const string Dmg = "bf_dmg";      // 世界空间飘伤害数字
        public const string Shake = "bf_shake";  // 相机震屏
    }

    /// <summary>特效播放请求</summary>
    public class VfxPayload
    {
        public string url;
        public float x, y, z;
        public float scale = 1f;
        // 以下为可选表现覆盖项，<0 表示沿用 VfxLayer 默认值
        public float life = -1f;      // 存活秒数
        public float rise = -1f;      // 屏幕上飘像素
        public float grow = -1f;      // 存活期扩散倍率（末态 = 1 + grow）
        public float alpha = -1f;     // 起始透明度
        public bool trail;            // true = 移动拖尾，走独立对象池与上限
    }

    /// <summary>世界空间伤害数字请求</summary>
    public class DmgPayload
    {
        public float x, y, z;
        public string text;
        public bool big;
    }

    /// <summary>
    /// 全局事件总线（对应 LayaAir 版 GameBus）。
    /// 用字典实现，语义与 Laya.EventDispatcher 一致：On / Off / Emit。
    /// </summary>
    public static class GameBus
    {
        private static readonly Dictionary<string, List<Action<object>>> _map =
            new Dictionary<string, List<Action<object>>>();

        public static void On(string evt, Action<object> cb)
        {
            if (cb == null) return;
            List<Action<object>> list;
            if (!_map.TryGetValue(evt, out list))
            {
                list = new List<Action<object>>();
                _map[evt] = list;
            }
            if (!list.Contains(cb)) list.Add(cb);
        }

        public static void Off(string evt, Action<object> cb)
        {
            List<Action<object>> list;
            if (!_map.TryGetValue(evt, out list)) return;
            list.Remove(cb);
        }

        public static void Emit(string evt, object payload = null)
        {
            List<Action<object>> list;
            if (!_map.TryGetValue(evt, out list)) return;
            // 复制一份再遍历：回调里可能会 Off/On，直接遍历原列表会出问题
            var snapshot = new List<Action<object>>(list);
            for (int i = 0; i < snapshot.Count; i++)
            {
                try
                {
                    snapshot[i](payload);
                }
                catch (Exception e)
                {
                    UnityEngine.Debug.LogException(e);
                }
            }
        }

        /// <summary>清空所有监听（重开局时调用，防止重复注册）</summary>
        public static void Clear()
        {
            _map.Clear();
        }
    }
}

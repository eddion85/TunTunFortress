using UnityEngine;

namespace BattleFortress
{
    /// <summary>
    /// Boss 骨骼动画控制：只负责播放 CH_Boss_Tank 的 idle / run / attack / hit / die
    /// 五个片段，不含任何战斗数值。对应 LayaAir 版 components/BossMotion.ts。
    /// 其他敌人仍是无骨骼静态模型，不受影响。
    /// </summary>
    public static class BossMotion
    {
        /// <summary>在克隆出的 Boss 节点子树下查找 Animator</summary>
        public static Animator FindAnimator(GameObject node)
        {
            if (node == null) return null;
            var direct = node.GetComponent<Animator>();
            if (direct != null) return direct;
            return node.GetComponentInChildren<Animator>(true);
        }

        /// <summary>初始化：播 idle。返回 Animator 或 null</summary>
        public static Animator Setup(GameObject node, string idleClip = "idle")
        {
            var anim = FindAnimator(node);
            if (anim == null) return null;
            anim.Play(string.IsNullOrEmpty(idleClip) ? "idle" : idleClip, 0, 0f);
            return anim;
        }

        /// <summary>切换片段（带状态名缓存，避免重复触发同一片段）</summary>
        public static void Play(EnemyUnit e, string clip)
        {
            if (e == null || e.AnimClip == clip) return;
            var anim = e.Animator;
            if (anim == null) return;
            e.AnimClip = clip;
            anim.Play(clip, 0, 0f);
        }
    }
}

using UnityEngine;

namespace BattleFortress
{
    /// <summary>
    /// 全局调度：驱动局内计时、BGM 切换、低血警告（无尽生存，难度由存活时间驱动）。
    /// 对应 LayaAir 版 components/GameDirector.ts，是唯一驱动 GS.Tick 的地方。
    /// </summary>
    public class GameDirector : MonoBehaviour
    {
        private string _bgm = "";
        private float _lowHpCd;
        private bool _started;

        private void Start()
        {
            Meta.Load();
            GS.Reset();
            PlayBgm(BgmKeys.Farm);
            _started = true;

            GameBus.On(GameEvents.Restart, OnRestart);
            GameBus.On(GameEvents.Result, OnResult);
            GameBus.On(GameEvents.Revive, OnRevive);
            GameBus.On(GameEvents.Boss, OnBoss);
        }

        private void OnDestroy()
        {
            GameBus.Off(GameEvents.Restart, OnRestart);
            GameBus.Off(GameEvents.Result, OnResult);
            GameBus.Off(GameEvents.Revive, OnRevive);
            GameBus.Off(GameEvents.Boss, OnBoss);
        }

        private void PlayBgm(string url)
        {
            if (_bgm == url) return;
            _bgm = url;
            AudioKit.PlayMusic(url);
        }

        private void OnBoss(object payload)
        {
            bool active = payload is bool && (bool)payload;
            // Boss BGM 由 EnemySpawner 触发播放，这里只在 Boss 被击杀后切回战斗曲
            if (!active) PlayBgm(BgmKeys.Battle);
        }

        private void OnResult(object payload)
        {
            PlayBgm(BgmKeys.Result);
        }

        /// <summary>重新开局：状态已由 ResultPanel 重置，这里只负责把 BGM 切回开局曲</summary>
        private void OnRestart(object payload)
        {
            PlayBgm(BgmKeys.Farm);
        }

        /// <summary>复活：Boss 还在就继续 Boss 曲，否则回到战斗曲</summary>
        private void OnRevive(object payload)
        {
            PlayBgm(GS.BossAlive ? BgmKeys.Boss : BgmKeys.Battle);
        }

        private void Update()
        {
            if (!_started) return;
            float dt = Mathf.Min(Time.deltaTime, GameConfig.MaxDelta);
            GS.Tick(dt);
            if (GS.Over || GS.Paused) return;

            // 中期切换到战斗 BGM，增强推进感
            if (!GS.BossAlive && GS.Elapsed > 55f && _bgm == BgmKeys.Farm)
                PlayBgm(BgmKeys.Battle);

            // 低血循环警告音
            _lowHpCd -= dt;
            if (GS.Hp > 0f && GS.Hp < GS.MaxHp * 0.25f && _lowHpCd <= 0f)
            {
                _lowHpCd = 2.2f;
                AudioKit.PlaySfx(SfxKeys.LowHp);
            }
        }
    }
}

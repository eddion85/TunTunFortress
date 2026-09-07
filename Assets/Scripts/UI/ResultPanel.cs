using UnityEngine;
using UnityEngine.UI;

namespace BattleFortress
{
    /// <summary>
    /// 结算面板（无尽生存）：玩家死亡后弹出，二选一——
    /// 「复活」（复用原下一关按钮，次数/费用走 GameConfig.Survival 配置）或「重新开始」。
    /// 不存在通关：游戏一直进行到玩家死亡为止。
    /// </summary>
    public class ResultPanel : MonoBehaviour
    {
        [Header("文字")]
        [SerializeField] private Text title;
        [SerializeField] private Text info;

        [Header("按钮")]
        [SerializeField] private Button restart;
        [SerializeField] private Button nextBtn;       // 复用为「复活」按钮，场景接线不变
        [SerializeField] private Text nextLabel;       // 复活按钮上的文字

        [Header("星级（无尽模式不评星，死亡时统一置灰）")]
        [SerializeField] private Image star0;
        [SerializeField] private Image star1;
        [SerializeField] private Image star2;

        private const string StarOff = "art/ui/img_star_off";

        // 单按钮时居中、双按钮时左右分开的横坐标（与场景横板布局一致）
        private const float DoubleX = 150f;

        private void Start()
        {
            gameObject.SetActive(false);
            if (restart != null) restart.onClick.AddListener(OnRestart);
            if (nextBtn != null) nextBtn.onClick.AddListener(OnRevive);

            GameBus.On(GameEvents.Result, Show);
            GameBus.On(GameEvents.Revive, OnRevived);
        }

        private void OnDestroy()
        {
            GameBus.Off(GameEvents.Result, Show);
            GameBus.Off(GameEvents.Revive, OnRevived);
        }

        private void Show(object payload)
        {
            // 结算时统一解除暂停（可能在商店/强化/暂停面板打开的瞬间死亡）
            GS.Paused = false;

            if (title != null) title.text = "堡垒被击毁 · 存活 " + GS.TimeText();
            if (info != null)
            {
                info.text =
                    "吞噬 " + GS.Kills + " 个目标 · 最高连击 x" + GS.BestCombo + "\n" +
                    "等级 Lv." + GS.Level + " · 进化 " + GS.Stage + " 阶\n" +
                    "本局金币 " + GS.Coins + " · 累计 " + Meta.Data.gold;
            }

            // 无尽模式不评星，星位统一置灰
            var stars = new[] { star0, star1, star2 };
            for (int i = 0; i < stars.Length; i++)
            {
                if (stars[i] == null) continue;
                var sp = Resources.Load<Sprite>(StarOff);
                if (sp != null) stars[i].sprite = sp;
            }

            // 复活按钮：次数/费用由配置决定（主标签是按钮内第一个 Text，副标签 nextLabel 显示剩余次数）
            bool canRevive = GS.CanRevive();
            if (nextBtn != null)
            {
                nextBtn.gameObject.SetActive(canRevive);
                if (canRevive)
                {
                    var labels = nextBtn.GetComponentsInChildren<Text>(true);
                    for (int i = 0; i < labels.Length; i++)
                        if (labels[i] != nextLabel) labels[i].text = "复活";
                }
            }
            if (nextLabel != null)
            {
                nextLabel.gameObject.SetActive(canRevive);
                if (canRevive)
                {
                    int left = GameConfig.Survival.ReviveMax - GS.ReviveUsed;
                    nextLabel.text = GameConfig.Survival.ReviveCoinCost > 0
                        ? ReviveCoinText(left)
                        : "剩 " + left + " 次";
                }
            }

            // 只有「重新开始」时把它居中，避免偏在一侧
            if (restart != null)
            {
                var rt = restart.GetComponent<RectTransform>();
                var p = rt.anchoredPosition;
                p.x = canRevive ? -DoubleX : 0f;
                rt.anchoredPosition = p;
            }

            gameObject.SetActive(true);
        }

        /// <summary>复活成功后收起面板</summary>
        private void OnRevived(object payload)
        {
            gameObject.SetActive(false);
        }

        private void OnRestart()
        {
            gameObject.SetActive(false);
            AudioKit.PlaySfx(SfxKeys.Click);
            // 放弃本局：先把局内金币按比例结算进局外存档，再清空局内状态
            GS.SettleRun();
            GS.Reset();
            GameBus.Emit(GameEvents.Restart, null);
        }

        private void OnRevive()
        {
            AudioKit.PlaySfx(SfxKeys.Click);
            GS.Revive();   // 成功会广播 Revive 事件，由 OnRevived 收起面板并通知各系统
        }

        private static string ReviveCoinText(int left)
        {
            return GameConfig.Survival.ReviveCoinCost + " 金 · 剩" + left + "次";
        }
    }
}

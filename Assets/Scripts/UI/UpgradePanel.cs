using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace BattleFortress
{
    /// <summary>
    /// 三选一强化面板。对应 LayaAir 版 ui/UpgradePanel.ts。
    /// 卡池 [策划书 4.1]：武器获取/升星、HP+15%、移速+10%、吞噬圈+15%
    /// </summary>
    public class UpgradePanel : MonoBehaviour
    {
        private class Card
        {
            public string title;
            public string desc;
            public int rare;
            public string icon;
            public Func<bool> can;
            public Action run;
        }

        [Header("面板")]
        [SerializeField] private GameObject panel;      // 三选一面板根节点
        [SerializeField] private Button openBtn;        // HUD 上的强化入口
        [SerializeField] private Text badge;            // 按钮上的可用点数角标
        [SerializeField] private Text pointLabel;
        [SerializeField] private Button closeBtn;       // 收牌继续

        [Header("卡片")]
        [SerializeField] private Button[] cards = new Button[3];
        [SerializeField] private Text[] texts = new Text[3];
        [SerializeField] private Image[] icons = new Image[3];
        [SerializeField] private Image[] glows = new Image[3];

        [Header("卡面底图（common / rare / legend）")]
        [SerializeField] private Sprite[] cardSkins = new Sprite[3];

        private static readonly List<Card> Pool = new List<Card>
        {
            new Card { title = "正面直射炮", desc = "解锁正面高伤主炮", rare = 2, icon = "art/ui/img_icon_weapon_front",
                       can = () => !GS.HasFrontCannon, run = () => { GS.HasFrontCannon = true; } },
            new Card { title = "回旋连枷", desc = "两枚连枷环绕堡垒持续碾压", rare = 2, icon = "art/ui/img_icon_weapon_flail",
                       can = () => !GS.HasFlail, run = () => { GS.HasFlail = true; } },
            new Card { title = "地雷投放舱", desc = "行进途中自动布雷", rare = 2, icon = "art/ui/img_icon_weapon_mine",
                       can = () => !GS.HasMine, run = () => { GS.HasMine = true; } },
            new Card { title = "侧炮升星", desc = "武器伤害 +30%", rare = 1, icon = "art/ui/img_icon_dmg_up",
                       run = () => { GS.DmgMul *= 1.3f; } },
            new Card { title = "火力全开", desc = "武器攻速 +20%", rare = 1, icon = "art/ui/img_icon_atkspeed_up",
                       run = () => { GS.CdMul *= 0.8f; } },
            new Card { title = "加固木墙", desc = "最大生命 +15%", rare = 0, icon = "art/ui/img_icon_hp_up",
                       run = () => {
                           GS.HpBonusMul *= 1.15f;
                           GS.MaxHp = Mathf.RoundToInt(GS.MaxHp * 1.15f);
                           GS.Heal(GS.MaxHp * 0.15f);
                       } },
            new Card { title = "强化轮轴", desc = "移动速度 +10%", rare = 0, icon = "art/ui/img_icon_speed_up",
                       run = () => { GS.SpeedMul *= 1.1f; } },
            new Card { title = "吞噬扩张", desc = "吞噬圈 +15%", rare = 0, icon = "art/ui/img_icon_devour_up",
                       run = () => { GS.DevourMul *= 1.15f; } },
            new Card { title = "经验熔炉", desc = "经验获取 +20%", rare = 1, icon = "art/ui/img_icon_exp_up",
                       run = () => { GS.ExpMul *= 1.2f; } },
            new Card { title = "紧急修复", desc = "立即回复 30 点生命", rare = 0, icon = "art/ui/img_icon_repair",
                       run = () => { GS.Heal(30f); } }
        };

        private readonly List<Card> _picks = new List<Card>();

        private void Start()
        {
            SetPanelVisible(false);

            for (int i = 0; i < cards.Length; i++)
            {
                if (cards[i] == null) continue;
                int idx = i;
                cards[i].onClick.AddListener(() => Choose(idx));
            }
            if (openBtn != null) openBtn.onClick.AddListener(OnOpen);
            if (closeBtn != null) closeBtn.onClick.AddListener(OnClose);

            GameBus.On(GameEvents.LevelUp, OnLevelUp);
            GameBus.On(GameEvents.Restart, CloseOnModal);
            GameBus.On(GameEvents.Result, CloseOnModal);
        }

        private void OnDestroy()
        {
            if (openBtn != null) openBtn.onClick.RemoveListener(OnOpen);
            if (closeBtn != null) closeBtn.onClick.RemoveListener(OnClose);
            GameBus.Off(GameEvents.LevelUp, OnLevelUp);
            GameBus.Off(GameEvents.Restart, CloseOnModal);
            GameBus.Off(GameEvents.Result, CloseOnModal);
        }

        // 重开 / 结算时强制收起三选一面板，避免多层叠加
        private void CloseOnModal(object payload)
        {
            SetPanelVisible(false);
        }

        private void SetPanelVisible(bool v)
        {
            var target = panel != null ? panel : gameObject;
            target.SetActive(v);
        }

        private bool IsPanelVisible()
        {
            var target = panel != null ? panel : gameObject;
            return target.activeSelf;
        }

        /// <summary>玩家自行决定用几点：可随时收牌，剩余点数保留待下次</summary>
        private void OnClose()
        {
            if (!IsPanelVisible()) return;
            AudioKit.PlaySfx(SfxKeys.Click);
            SetPanelVisible(false);
            GS.Paused = false;
            if (GS.UpgradePoints > 0)
                GameBus.Emit(GameEvents.Float, "剩余 " + GS.UpgradePoints + " 点已保留");
        }

        /// <summary>升级只提示，不打断战斗</summary>
        private void OnLevelUp(object payload)
        {
            int lv = payload is int ? (int)payload : 0;
            GameBus.Emit(GameEvents.Float, "Lv." + lv + " 强化点 +1");
        }

        /// <summary>玩家主动点开：有点数才能开</summary>
        private void OnOpen()
        {
            if (GS.Over || GS.Paused) return;
            if (GS.UpgradePoints <= 0)
            {
                GameBus.Emit(GameEvents.Float, "暂无强化点数");
                return;
            }
            AudioKit.PlaySfx(SfxKeys.Click);
            Show();
        }

        private void Show()
        {
            if (GS.Over) return;

            // 过滤掉已失效的一次性卡（如已解锁的正面炮）
            var bag = new List<Card>();
            for (int i = 0; i < Pool.Count; i++)
            {
                var c = Pool[i];
                if (c.can != null && !c.can()) continue;
                bag.Add(c);
            }

            _picks.Clear();
            for (int i = 0; i < 3 && bag.Count > 0; i++)
            {
                int idx = UnityEngine.Random.Range(0, bag.Count);
                _picks.Add(bag[idx]);
                bag.RemoveAt(idx);
            }

            for (int i = 0; i < texts.Length; i++)
            {
                var pick = i < _picks.Count ? _picks[i] : null;
                if (texts[i] != null) texts[i].text = pick != null ? pick.title + "\n" + pick.desc : "";
                if (cards[i] != null)
                {
                    cards[i].gameObject.SetActive(pick != null);
                    if (pick != null)
                    {
                        var bg = cards[i].GetComponent<Image>();
                        int rare = Mathf.Clamp(pick.rare, 0, cardSkins.Length - 1);
                        if (bg != null && cardSkins.Length > rare && cardSkins[rare] != null) bg.sprite = cardSkins[rare];
                    }
                }
                if (i < glows.Length && glows[i] != null)
                    glows[i].gameObject.SetActive(pick != null && pick.rare >= 2);
                if (i < icons.Length && icons[i] != null)
                {
                    bool has = pick != null && !string.IsNullOrEmpty(pick.icon);
                    icons[i].gameObject.SetActive(has);
                    if (has)
                    {
                        var sp = Resources.Load<Sprite>(pick.icon);
                        if (sp != null) icons[i].sprite = sp;
                    }
                }
            }

            GS.Paused = true;
            SetPanelVisible(true);
        }

        private void Choose(int index)
        {
            if (!IsPanelVisible()) return;
            if (index < 0 || index >= _picks.Count) return;
            var pick = _picks[index];
            if (pick == null) return;

            pick.run();
            AudioKit.PlaySfx(SfxKeys.CardPick);
            GS.UpgradePoints = Mathf.Max(0, GS.UpgradePoints - 1);
            GameBus.Emit(GameEvents.Float, pick.title);

            // 还有剩余点数就重新发牌，支持一次性连续强化
            if (GS.UpgradePoints > 0) Show();
            else
            {
                SetPanelVisible(false);
                GS.Paused = false;
            }
        }

        private void Update()
        {
            // 按钮角标与呼吸提示：有点数可用时才高亮
            int n = GS.UpgradePoints;
            if (badge != null)
            {
                badge.gameObject.SetActive(n > 0);
                badge.text = n.ToString();
            }
            if (openBtn != null)
            {
                var cg = openBtn.GetComponent<CanvasGroup>();
                if (cg == null) cg = openBtn.gameObject.AddComponent<CanvasGroup>();
                cg.alpha = n > 0 ? 0.95f + Mathf.Sin(Time.realtimeSinceStartup * 6f) * 0.05f : 0.4f;
            }
            if (pointLabel != null)
                pointLabel.text = n > 0 ? "选择一项强化（还剩 " + n + " 点，可随时收牌）" : "选择一项强化";
        }
    }
}

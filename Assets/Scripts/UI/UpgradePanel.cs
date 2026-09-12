using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace BattleFortress
{
    /// <summary>
    /// 击杀里程碑自动奖励面板（左右轮播版）。
    /// 规则：
    /// 1) 不允许局内手动购买/点开：只由 GameEvents.Offer（击杀数达到配置阈值）自动弹出；
    /// 2) 列出当前「真正可用」的全部选项，左右箭头轮播查看；已拥有的同一把武器、满级项、
    ///    满血时的回复卡等不满足条件的项不出现；
    /// 3) 每次里程碑最多选择 OfferMaxPicks 次，每选一次立即重新过滤，保证下一次选择仍满足条件；
    /// 4) 点「选择」先弹详情确认框：完整说明效果/金币消耗/替换关系等接下来发生的事，确认后才生效；
    /// 5) 同槽位武器允许替换（选新武器自动下掉旧武器），只是不会重复给已装备的同一把。
    /// 轮播 UI 全部运行时生成并挂到场景里的 panel 根节点下（旧三卡布局运行时隐藏，场景不改动）。
    /// 类名与 SerializeField 保持不变以兼容场景接线。
    /// </summary>
    public class UpgradePanel : MonoBehaviour
    {
        private class Card
        {
            public string title;
            public string desc;        // 卡面简述
            public string detail;      // 确认框里的完整说明（不填则用 desc）
            public int rare;
            public string icon;
            public int cost;           // 金币消耗（0=免费）
            public Func<bool> can;
            public Action run;
        }

        [Header("面板（场景接线保持不变）")]
        [SerializeField] private GameObject panel;
        [SerializeField] private Button openBtn;
        [SerializeField] private Text badge;
        [SerializeField] private Text pointLabel;
        [SerializeField] private Button closeBtn;

        [Header("旧三卡布局（运行时隐藏，保留接线）")]
        [SerializeField] private Button[] cards = new Button[3];
        [SerializeField] private Text[] texts = new Text[3];
        [SerializeField] private Image[] icons = new Image[3];
        [SerializeField] private Image[] glows = new Image[3];
        [SerializeField] private Sprite[] cardSkins = new Sprite[3];

        // ---------------- 运行时轮播状态 ----------------
        private readonly List<Card> _options = new List<Card>();
        private int _index;
        private int _picksUsed;
        private int _picksMax = 3;
        private bool _active;
        private Card _confirming;

        // ---------------- 运行时生成的控件 ----------------
        private RectTransform _carouselRoot;
        private Text _headerText;
        private Image _cardIcon;
        private Text _cardTitle;
        private Text _cardDesc;
        private Text _cardCost;
        private Text _pageText;
        private Image _centerBg;

        private RectTransform _confirmRoot;
        private Text _confirmTitle;
        private Text _confirmDetail;
        private Button _confirmOk;
        private Text _confirmOkText;

        private static Sprite _whiteSprite;
        private static readonly Color BoxColor = new Color(0.14f, 0.16f, 0.22f, 0.98f);
        private static readonly Color OkColor = new Color(0.23f, 0.62f, 0.34f, 1f);
        private static readonly Color CancelColor = new Color(0.38f, 0.40f, 0.46f, 1f);
        private static readonly Color RareColor = new Color(0.95f, 0.78f, 0.30f, 1f);

        private void Start()
        {
            // 自动奖励模式：隐藏旧的手动入口与三卡布局
            if (openBtn != null) openBtn.gameObject.SetActive(false);
            if (closeBtn != null) closeBtn.gameObject.SetActive(false);
            if (badge != null) badge.gameObject.SetActive(false);
            if (pointLabel != null) pointLabel.gameObject.SetActive(false);
            for (int i = 0; i < cards.Length; i++) if (cards[i] != null) cards[i].gameObject.SetActive(false);

            BuildCarousel();
            BuildConfirm();
            SetCarouselVisible(false);
            SetConfirmVisible(false);
            SetPanelVisible(false);   // 场景面板根节点默认激活，开局必须先关掉

            GameBus.On(GameEvents.Offer, OnOffer);
            GameBus.On(GameEvents.LevelUp, OnLevelUp);
            GameBus.On(GameEvents.Restart, CloseOnModal);
            GameBus.On(GameEvents.Result, CloseOnModal);
        }

        private void OnDestroy()
        {
            GameBus.Off(GameEvents.Offer, OnOffer);
            GameBus.Off(GameEvents.LevelUp, OnLevelUp);
            GameBus.Off(GameEvents.Restart, CloseOnModal);
            GameBus.Off(GameEvents.Result, CloseOnModal);
        }

        private void CloseOnModal(object payload)
        {
            EndOffer();
        }

        private void OnOffer(object payload)
        {
            if (GS.Over) return;
            BeginOffer();
        }

        private void OnLevelUp(object payload)
        {
            int lv = payload is int ? (int)payload : 0;
            GameBus.Emit(GameEvents.Float, "Lv." + lv);
        }

        // ============================ 卡池 ============================
        /// <summary>组装当前时刻真正可用的全部选项（每次选择后重新调用，保证条件最新）</summary>
        private static List<Card> BuildPool()
        {
            var pool = new List<Card>();
            AddWeaponCards(pool);
            AddBuffCards(pool);
            AddHealCard(pool);
            AssignCosts(pool); // 统一按类别赋金币售价（配置驱动）
            return pool;
        }

        /// <summary>装备卡：顶部主炮位（火箭炮↔正面炮可替换）、车头攻城锤、独立副武器与其升星</summary>
        private static void AddWeaponCards(List<Card> pool)
        {
            // ---- 顶部/主炮位：火箭炮 与 正面直射炮可互相替换（选新自动下掉旧），不重复给已装备的同一把 ----
            pool.Add(new Card
            {
                rare = 2, icon = "art/ui/img_icon_weapon_front", cost = 0,
                title = "车载火箭炮",
                desc = "车顶挂载火箭炮，抛射范围轰炸",
                detail = "在车顶 Slot_Top 挂载火箭炮，按冷却自动抛射炮弹，弹道为抛物线，落点范围爆炸伤害。" +
                         (GS.HasFrontCannon ? "\n\n注意：将替换当前的正面直射炮（正面炮会被卸下）。" : "\n\n当前顶部为空位，直接装备。"),
                can = () => !GS.HasRocketLauncher,
                run = () => { GS.HasFrontCannon = false; GS.HasRocketLauncher = true; GS.RocketLevel = 0; }
            });
            pool.Add(new Card
            {
                rare = 2, icon = "art/ui/img_icon_weapon_front", cost = 0,
                title = "火箭炮升星",
                desc = "升到 " + (GS.RocketLevel + 2) + " 阶，伤害提升",
                detail = "车载火箭炮从 " + (GS.RocketLevel + 1) + " 阶升到 " + (GS.RocketLevel + 2) +
                         " 阶，外观模型与伤害数值同步提升。",
                can = () => GS.HasRocketLauncher && GS.RocketLevel < 3,
                run = () => { GS.RocketLevel += 1; }
            });
            pool.Add(new Card
            {
                rare = 2, icon = "art/ui/img_icon_weapon_front", cost = 0,
                title = "正面直射炮",
                desc = "正面高伤主炮，落地范围爆炸",
                detail = "启用模型内的正面直射炮，自动向正前方开火，炮弹落地造成范围爆炸伤害。" +
                         (GS.HasRocketLauncher ? "\n\n注意：将替换当前的车载火箭炮（火箭炮会被卸下）。" : "\n\n当前顶部为空位，直接装备。"),
                can = () => !GS.HasFrontCannon,
                run = () => { GS.HasRocketLauncher = false; GS.RocketLevel = 0; GS.HasFrontCannon = true; }
            });

            // ---- 车头位：攻城锤 ----
            pool.Add(new Card
            {
                rare = 2, icon = "art/ui/img_icon_weapon_mine", cost = 0,
                title = "攻城锤",
                desc = "车头挂载攻城锤，冲撞按钮手动扇形突刺",
                detail = "在车头 Slot_Front 挂载攻城锤。不会自动攻击，点击「冲撞」按钮时向前突刺，" +
                         "前方 90° 扇形内敌人受伤，存活敌人被整体顶退一个身位。主要用于打 Boss。",
                can = () => !GS.HasBatteringRam,
                run = () => { GS.HasBatteringRam = true; GS.RamLevel = 0; }
            });
            pool.Add(new Card
            {
                rare = 2, icon = "art/ui/img_icon_weapon_mine", cost = 0,
                title = "攻城锤升星",
                desc = "升到 " + (GS.RamLevel + 2) + " 阶，伤害提升",
                detail = "攻城锤从 " + (GS.RamLevel + 1) + " 阶升到 " + (GS.RamLevel + 2) + " 阶，外观与伤害同步提升。",
                can = () => GS.HasBatteringRam && GS.RamLevel < 3,
                run = () => { GS.RamLevel += 1; }
            });

            // ---- 独立副武器 ----
            pool.Add(new Card
            {
                rare = 2, icon = "art/ui/img_icon_weapon_flail", cost = 0,
                title = "回旋连枷", desc = "两枚连枷环绕堡垒持续碾压",
                detail = "获得两枚围绕堡垒旋转的连枷，近身敌人会被持续碾压，无需操作自动生效。",
                can = () => !GS.HasFlail,
                run = () => { GS.HasFlail = true; }
            });
            pool.Add(new Card
            {
                rare = 2, icon = "art/ui/img_icon_weapon_side", cost = 0,
                title = "车侧弩箭", desc = "左右各装一把弩箭，朝外自动射击",
                detail = "在车体左右侧挂点各装一把弩箭，自动向两侧敌人射击。不再随进化自动获得，只能通过奖励解锁。",
                can = () => !GS.HasCrossbow,
                run = () => { GS.HasCrossbow = true; GS.CrossbowLevel = 1; }
            });
            pool.Add(new Card
            {
                rare = 2, icon = "art/ui/img_icon_weapon_side", cost = 0,
                title = "弩箭升星", desc = "左右各再加一把弩箭（每侧两把）",
                detail = "每侧弩箭从 1 把增加到 2 把，沿车体前后排列，火力翻倍。",
                can = () => GS.HasCrossbow && GS.CrossbowLevel < 2,
                run = () => { GS.CrossbowLevel += 1; }
            });
            pool.Add(new Card
            {
                rare = 2, icon = "art/ui/img_icon_weapon_mine", cost = 0,
                title = "地雷投放舱", desc = "行进途中自动布雷",
                detail = "移动时自动在身后投放地雷，敌人踩中后爆炸造成范围伤害。",
                can = () => !GS.HasMine,
                run = () => { GS.HasMine = true; }
            });
        }

        /// <summary>可重复数值词条（永远可用，兜底卡池不会空）</summary>
        private static void AddBuffCards(List<Card> pool)
        {
            pool.Add(new Card
            {
                rare = 1, icon = "art/ui/img_icon_dmg_up", cost = 0,
                title = "侧炮升星", desc = "全部武器伤害 +30%",
                detail = "全局伤害倍率 ×1.3，对所有武器（主炮/火箭炮/攻城锤/弩箭/连枷等）立即生效，可叠加。",
                can = null, run = () => { GS.DmgMul *= 1.3f; }
            });
            pool.Add(new Card
            {
                rare = 1, icon = "art/ui/img_icon_atkspeed_up", cost = 0,
                title = "火力全开", desc = "全部武器攻速 +20%",
                detail = "全局冷却倍率 ×0.8，所有自动武器开火间隔缩短 20%，可叠加。",
                can = null, run = () => { GS.CdMul *= 0.8f; }
            });
            pool.Add(new Card
            {
                rare = 0, icon = "art/ui/img_icon_hp_up", cost = 0,
                title = "加固木墙", desc = "最大生命 +15% 并回复对应血量",
                detail = "最大生命值 ×1.15，并立即回复新增部分的血量，可叠加。",
                can = null,
                run = () =>
                {
                    GS.HpBonusMul *= 1.15f;
                    GS.MaxHp = Mathf.RoundToInt(GS.MaxHp * 1.15f);
                    GS.Heal(GS.MaxHp * 0.15f);
                }
            });
            pool.Add(new Card
            {
                rare = 0, icon = "art/ui/img_icon_speed_up", cost = 0,
                title = "强化轮轴", desc = "移动速度 +10%",
                detail = "移动速度倍率 ×1.1，立即生效，可叠加。",
                can = null, run = () => { GS.SpeedMul *= 1.1f; }
            });
            pool.Add(new Card
            {
                rare = 0, icon = "art/ui/img_icon_devour_up", cost = 0,
                title = "吞噬扩张", desc = "吞噬圈 +15%",
                detail = "吞噬圈半径 ×1.15，可吞到的范围更大，可叠加。",
                can = null, run = () => { GS.DevourMul *= 1.15f; }
            });
            pool.Add(new Card
            {
                rare = 1, icon = "art/ui/img_icon_exp_up", cost = 0,
                title = "经验熔炉", desc = "经验获取 +20%",
                detail = "击杀获得的经验 ×1.2，升级/进化更快，可叠加。",
                can = null, run = () => { GS.ExpMul *= 1.2f; }
            });
        }

        /// <summary>回复卡：缺血才出现</summary>
        private static void AddHealCard(List<Card> pool)
        {
            pool.Add(new Card
            {
                rare = 0, icon = "art/ui/img_icon_repair", cost = 0,
                title = "紧急修复", desc = "立即回复 30 点生命",
                detail = "立即回复 30 点生命（不会超过最大生命）。当前生命 " +
                         Mathf.CeilToInt(GS.Hp) + "/" + Mathf.RoundToInt(GS.MaxHp) + "。",
                can = () => GS.Hp > 0f && GS.Hp < GS.MaxHp * GameConfig.Survival.OfferHealBelowRatio,
                run = () => { GS.Heal(30f); }
            });
        }

        /// <summary>按类别赋金币售价（个别卡已单独指定 cost 则保留）</summary>
        private static void AssignCosts(List<Card> pool)
        {
            for (int i = 0; i < pool.Count; i++)
            {
                var c = pool[i];
                if (c.cost != 0) continue;
                if (c.title.Contains("升星")) c.cost = GameConfig.Survival.OfferCostUpgrade;
                else if (c.title == "紧急修复") c.cost = GameConfig.Survival.OfferCostHeal;
                else c.cost = c.rare == 2 ? GameConfig.Survival.OfferCostWeapon : GameConfig.Survival.OfferCostBuff;
            }
        }

        /// <summary>重新过滤可用项（每次选择后调用）</summary>
        private void RebuildOptions()
        {
            _options.Clear();
            var all = BuildPool();
            for (int i = 0; i < all.Count; i++)
            {
                var c = all[i];
                if (c.can != null && !c.can()) continue;
                _options.Add(c);
            }
            if (_index >= _options.Count) _index = 0;
        }

        // ============================ 流程 ============================
        private void BeginOffer()
        {
            _picksMax = GameConfig.Survival.OfferMaxPicks;
            _picksUsed = 0;
            _index = 0;
            RebuildOptions();
            if (_options.Count == 0) { EndOffer(); return; }

            _active = true;
            GS.Paused = true;
            SetPanelVisible(true);
            SetConfirmVisible(false);
            SetCarouselVisible(true);
            RenderCurrent();
        }

        private void EndOffer()
        {
            _active = false;
            _confirming = null;
            SetConfirmVisible(false);
            SetCarouselVisible(false);
            SetPanelVisible(false);
            if (!GS.Over) GS.Paused = false;
        }

        private void SetPanelVisible(bool v)
        {
            var target = panel != null ? panel : gameObject;
            target.SetActive(v);
        }

        private void Shift(int dir)
        {
            if (!_active || _options.Count == 0) return;
            _index = (_index + dir + _options.Count) % _options.Count;
            AudioKit.PlaySfx(SfxKeys.Click);
            RenderCurrent();
        }

        /// <summary>点「选择」：先弹详情确认，不直接生效</summary>
        private void AskConfirm()
        {
            if (!_active || _index < 0 || _index >= _options.Count) return;
            _confirming = _options[_index];
            AudioKit.PlaySfx(SfxKeys.Click);
            RenderConfirm();
            SetConfirmVisible(true);
        }

        private void CancelConfirm()
        {
            _confirming = null;
            AudioKit.PlaySfx(SfxKeys.Click);
            SetConfirmVisible(false);
        }

        /// <summary>确认后才真正生效；随后重新过滤，次数用完或无可选项则结束</summary>
        private void ConfirmPick()
        {
            if (_confirming == null) return;
            var pick = _confirming;

            if (pick.cost > 0 && GS.Coins < pick.cost)
            {
                GameBus.Emit(GameEvents.Float, "金币不足");
                return;
            }

            if (pick.cost > 0) GS.Coins -= pick.cost;
            pick.run();
            _picksUsed += 1;
            AudioKit.PlaySfx(SfxKeys.CardPick);
            GameBus.Emit(GameEvents.Float, pick.title);

            SetConfirmVisible(false);
            _confirming = null;
            RebuildOptions();

            if (_picksUsed >= _picksMax || _options.Count == 0)
            {
                EndOffer();
                return;
            }
            RenderCurrent();
        }

        private void RenderCurrent()
        {
            if (_options.Count == 0) { EndOffer(); return; }
            var c = _options[_index];

            if (_headerText != null)
                _headerText.text = "选择强化（剩余 " + (_picksMax - _picksUsed) + " 次）";
            if (_cardTitle != null) _cardTitle.text = c.title;
            if (_cardDesc != null) _cardDesc.text = c.desc;
            if (_cardCost != null)
            {
                bool afford = GS.Coins >= c.cost;
                _cardCost.text = c.cost > 0 ? ("金币  " + c.cost + "（持有 " + GS.Coins + "）") : "免费";
                _cardCost.color = c.cost > 0 && !afford ? new Color(0.93f, 0.42f, 0.40f, 1f) : RareColor;
            }
            if (_pageText != null) _pageText.text = (_index + 1) + " / " + _options.Count;
            if (_centerBg != null) _centerBg.color = c.rare >= 2
                ? new Color(0.22f, 0.18f, 0.10f, 0.98f) : BoxColor;

            if (_cardIcon != null)
            {
                bool has = !string.IsNullOrEmpty(c.icon);
                _cardIcon.gameObject.SetActive(has);
                if (has)
                {
                    var sp = Resources.Load<Sprite>(c.icon);
                    if (sp != null) _cardIcon.sprite = sp;
                    _cardIcon.color = Color.white;
                }
            }
        }

        private void RenderConfirm()
        {
            var c = _confirming;
            if (c == null) return;
            if (_confirmTitle != null) _confirmTitle.text = c.title;
            if (_confirmDetail != null)
            {
                string costLine = c.cost > 0
                    ? ("\n消耗金币：" + c.cost + "（当前持有 " + GS.Coins + "）")
                    : "\n消耗金币：无";
                _confirmDetail.text = (string.IsNullOrEmpty(c.detail) ? c.desc : c.detail) + costLine;
            }
            if (_confirmOk != null)
            {
                bool ok = c.cost <= 0 || GS.Coins >= c.cost;
                _confirmOk.interactable = ok;
                if (_confirmOkText != null) _confirmOkText.text = ok ? "确认选择" : "金币不足";
            }
        }

        // ============================ 运行时 UI 搭建 ============================
        private static Sprite WhiteSprite()
        {
            if (_whiteSprite != null) return _whiteSprite;
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            var px = new Color32[4];
            for (int i = 0; i < 4; i++) px[i] = new Color32(255, 255, 255, 255);
            tex.SetPixels32(px);
            tex.Apply();
            _whiteSprite = Sprite.Create(tex, new Rect(0, 0, 2, 2), new Vector2(0.5f, 0.5f), 2f);
            return _whiteSprite;
        }

        private RectTransform NewNode(string name, RectTransform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            return rt;
        }

        private Image NewImage(RectTransform rt, Color color)
        {
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = WhiteSprite();
            img.color = color;
            img.raycastTarget = true;
            return img;
        }

        private Text NewText(RectTransform rt, string content, int size, Color color, TextAnchor anchor)
        {
            var t = rt.gameObject.AddComponent<Text>();
            t.font = UiKit.RuntimeFont();
            t.fontSize = size;
            t.color = color;
            t.alignment = anchor;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            t.text = content;
            return t;
        }

        private Button NewButton(RectTransform rt, Color bg, string label, int size, Action onClick)
        {
            var img = NewImage(rt, bg);
            var btn = rt.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            var colors = btn.colors;
            colors.highlightedColor = new Color(1.1f, 1.1f, 1.1f, 1f);
            colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            btn.colors = colors;

            var labelRt = NewNode("Label", rt);
            Stretch(labelRt);
            var t = NewText(labelRt, label, size, Color.white, TextAnchor.MiddleCenter);
            t.raycastTarget = false;
            btn.onClick.AddListener(() => onClick());
            return btn;
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        private static void Place(RectTransform rt, float x, float y, float w, float h)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(x, y);
            rt.sizeDelta = new Vector2(w, h);
        }

        private void BuildCarousel()
        {
            var parent = panel != null ? (RectTransform)panel.transform : (RectTransform)transform;
            _carouselRoot = NewNode("AutoOfferCarousel", parent);
            Stretch(_carouselRoot);

            // 全屏遮罩（挡住背后游戏点击）
            var dim = NewNode("Dim", _carouselRoot);
            Stretch(dim);
            NewImage(dim, new Color(0f, 0f, 0f, 0.78f)).raycastTarget = true;

            // 中央盒子
            var box = NewNode("Box", _carouselRoot);
            Place(box, 0, 0, 820, 470);
            NewImage(box, BoxColor);

            var headerRt = NewNode("Header", box);
            Place(headerRt, 0, 195, 780, 46);
            _headerText = NewText(headerRt, "选择强化", 26, Color.white, TextAnchor.MiddleCenter);
            _headerText.fontStyle = FontStyle.Bold;

            // 左右箭头
            var leftRt = NewNode("BtnLeft", box);
            Place(leftRt, -330, 30, 92, 210);
            NewButton(leftRt, new Color(0.20f, 0.24f, 0.33f, 1f), "‹", 56, () => Shift(-1));

            var rightRt = NewNode("BtnRight", box);
            Place(rightRt, 330, 30, 92, 210);
            NewButton(rightRt, new Color(0.20f, 0.24f, 0.33f, 1f), "›", 56, () => Shift(1));

            // 中央卡面
            var cardRt = NewNode("CardView", box);
            Place(cardRt, 0, 30, 470, 300);
            _centerBg = NewImage(cardRt, BoxColor);

            var iconRt = NewNode("Icon", cardRt);
            Place(iconRt, 0, 92, 92, 92);
            _cardIcon = iconRt.gameObject.AddComponent<Image>();
            _cardIcon.raycastTarget = false;

            var titleRt = NewNode("Title", cardRt);
            Place(titleRt, 0, 18, 440, 40);
            _cardTitle = NewText(titleRt, "", 26, RareColor, TextAnchor.MiddleCenter);
            _cardTitle.fontStyle = FontStyle.Bold;

            var descRt = NewNode("Desc", cardRt);
            Place(descRt, 0, -62, 430, 120);
            _cardDesc = NewText(descRt, "", 20, new Color(0.88f, 0.90f, 0.95f, 1f), TextAnchor.UpperCenter);

            var costRt = NewNode("Cost", cardRt);
            Place(costRt, 0, -132, 430, 32);
            _cardCost = NewText(costRt, "", 20, RareColor, TextAnchor.MiddleCenter);
            _cardCost.fontStyle = FontStyle.Bold;

            var pageRt = NewNode("Page", box);
            Place(pageRt, 350, 195, 110, 36);
            _pageText = NewText(pageRt, "", 18, new Color(0.65f, 0.70f, 0.80f, 1f), TextAnchor.MiddleRight);

            // 选择按钮 + 提前完成按钮（最多选 3 次，也可以不选满直接退出）
            var okRt = NewNode("BtnPick", box);
            Place(okRt, -120, -195, 260, 60);
            NewButton(okRt, OkColor, "选 择", 24, AskConfirm);

            var doneRt = NewNode("BtnDone", box);
            Place(doneRt, 170, -195, 220, 60);
            NewButton(doneRt, CancelColor, "完 成", 24, EndOffer);
        }

        private void BuildConfirm()
        {
            var parent = panel != null ? (RectTransform)panel.transform : (RectTransform)transform;
            _confirmRoot = NewNode("AutoOfferConfirm", parent);
            Stretch(_confirmRoot);

            var dim = NewNode("Dim", _confirmRoot);
            Stretch(dim);
            NewImage(dim, new Color(0f, 0f, 0f, 0.70f));

            var box = NewNode("Box", _confirmRoot);
            Place(box, 0, 0, 620, 420);
            NewImage(box, BoxColor);

            var titleRt = NewNode("Title", box);
            Place(titleRt, 0, 165, 560, 44);
            _confirmTitle = NewText(titleRt, "", 26, RareColor, TextAnchor.MiddleCenter);
            _confirmTitle.fontStyle = FontStyle.Bold;

            var detailRt = NewNode("Detail", box);
            Place(detailRt, 0, 30, 560, 210);
            _confirmDetail = NewText(detailRt, "", 20, new Color(0.90f, 0.92f, 0.96f, 1f), TextAnchor.UpperLeft);

            var cancelRt = NewNode("BtnCancel", box);
            Place(cancelRt, -150, -165, 220, 58);
            NewButton(cancelRt, CancelColor, "取 消", 22, CancelConfirm);

            var okRt = NewNode("BtnOk", box);
            Place(okRt, 150, -165, 220, 58);
            _confirmOk = NewButton(okRt, OkColor, "确认选择", 22, ConfirmPick);
            _confirmOkText = okRt.GetComponentInChildren<Text>();
        }

        private void SetCarouselVisible(bool v)
        {
            if (_carouselRoot != null) _carouselRoot.gameObject.SetActive(v);
        }

        private void SetConfirmVisible(bool v)
        {
            if (_confirmRoot != null) _confirmRoot.gameObject.SetActive(v);
        }
    }
}

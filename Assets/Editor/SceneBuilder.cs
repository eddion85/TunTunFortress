using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using BattleFortress;

namespace BattleFortress.EditorTools
{
    /// <summary>
    /// 一键搭建游戏运行场景：地/光/相机/玩家 + GameDirector + 全部 UI。
    /// 流程：① 配置资源导入 ② 生成 Prefab ③ 跑本类 → GameScene 直接可运行。
    /// </summary>
    public static class SceneBuilder
    {
        public const string ScenePath = "Assets/Scenes/GameScene.unity";
        public static string Pf(string n) => FortressPrefabs.PrefabRoot + n + ".prefab";

        [MenuItem("吞吞堡垒/3-搭建 GameScene")]
        public static void Run()
        {
            BuildScene();
        }

        public static void BuildScene()
        {
            EnsureDir("Assets/Scenes");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var playerGo = SpawnPlayer();
            playerGo.transform.position = Vector3.zero;

            CreateGround();
            CreateLights();
            var camGo = CreateCamera(playerGo.transform);

            if (Object.FindObjectOfType<EventSystem>() == null)
            {
                var es = new GameObject("EventSystem");
                es.AddComponent<EventSystem>();
                es.AddComponent<StandaloneInputModule>();
            }

            BuildDirectorAndSystems(playerGo);
            BuildUi(playerGo, camGo);

            EditorSceneManager.SaveScene(scene, ScenePath);
            AddToBuildSettings(ScenePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[吞吞堡垒] GameScene 已搭建：" + ScenePath);
        }

        // ---------------- 地 ----------------
        private static void CreateGround()
        {
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.localScale = new Vector3(20f, 1f, 20f);
            var r = ground.GetComponent<Renderer>();
            if (r != null)
            {
                var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                var mat = new Material(sh);
                mat.color = new Color(0.55f, 0.72f, 0.35f);
                r.sharedMaterial = mat;
            }
        }

        // ---------------- 灯 ----------------
        private static void CreateLights()
        {
            var dl = new GameObject("Sun");
            var light = dl.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.1f;
            light.color = new Color(1f, 0.96f, 0.86f);
            light.shadows = LightShadows.Soft;
            dl.transform.rotation = Quaternion.Euler(45f, 30f, 0f);
        }

        // ---------------- 相机 ----------------
        private static GameObject CreateCamera(Transform target)
        {
            var camGo = new GameObject("MainCamera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.Skybox;
            cam.backgroundColor = new Color(0.6f, 0.78f, 0.95f);
            cam.fieldOfView = 55f;
            cam.nearClipPlane = 0.5f;
            cam.farClipPlane = 600f;
            camGo.transform.position = new Vector3(0f, 18f, -22f);
            camGo.transform.rotation = Quaternion.Euler(45f, 0f, 0f);
            camGo.AddComponent<AudioListener>();

            var follow = camGo.AddComponent<CameraFollow>();
            var so = new SerializedObject(follow);
            so.FindProperty("target").objectReferenceValue = target;
            so.ApplyModifiedPropertiesWithoutUndo();

            return camGo;
        }

        // ---------------- 玩家 ----------------
        private static GameObject SpawnPlayer()
        {
            var pf = AssetDatabase.LoadAssetAtPath<GameObject>(Pf("Player"));
            if (pf == null)
            {
                Debug.LogError("[吞吞堡垒] 找不到 Player.prefab，请先跑步骤2");
                return new GameObject("Player_Stub");
            }
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(pf);
            inst.name = "Player";
            return inst;
        }

        // ---------------- 调度 ----------------
        private static GameObject BuildDirectorAndSystems(GameObject player)
        {
            var root = new GameObject("GameDirector");
            root.AddComponent<GameDirector>();

            var spawner = root.AddComponent<EnemySpawner>();
            var drops = root.AddComponent<DropSystem>();

            // Spawner：玩家 + 5种敌人 + 箭 + Boss
            var so = new SerializedObject(spawner);
            so.FindProperty("player").objectReferenceValue = player.transform;
            so.FindProperty("tplSheep").objectReferenceValue = Load("Enemy_Sheep");
            so.FindProperty("tplCow").objectReferenceValue = Load("Enemy_Cow");
            so.FindProperty("tplFarmer").objectReferenceValue = Load("Enemy_Farmer");
            so.FindProperty("tplArcher").objectReferenceValue = Load("Enemy_Archer");
            so.FindProperty("tplRider").objectReferenceValue = Load("Enemy_Rider");
            so.FindProperty("tplArrow").objectReferenceValue = Load("Proj_Arrow");
            so.FindProperty("tplBoss").objectReferenceValue = Load("Enemy_Boss");
            so.ApplyModifiedPropertiesWithoutUndo();

            // DropSystem：玩家 + 3种掉落
            var so2 = new SerializedObject(drops);
            so2.FindProperty("player").objectReferenceValue = player.transform;
            so2.FindProperty("tplCoin").objectReferenceValue = Load("Item_Coin");
            so2.FindProperty("tplHealth").objectReferenceValue = Load("Item_HealthSmall");
            so2.FindProperty("tplMagnet").objectReferenceValue = Load("Item_Magnet");
            so2.ApplyModifiedPropertiesWithoutUndo();

            return root;
        }

        // ---------------- UI 总装 ----------------
        private static void BuildUi(GameObject player, GameObject camGo)
        {
            // 1) Canvas
            var canvas = UiFactory.CreateCanvas("UI_Canvas");
            var cv = canvas.transform;

            // 2) VFX 层（在 Canvas 下，但要走世界坐标投影）
            var vfxStretch = UiFactory.Stretch("VfxLayer", cv);
            var vfx = vfxStretch.gameObject.AddComponent<VfxLayer>();
            // 受击屏幕红晕：铺满全屏、默认全透明、不收射线，置于特效层最上
            var hurt = UiFactory.Image("HurtVignette", vfxStretch, UiFactory.Ui("img_vignette_hurt"), new Color(1f, 1f, 1f, 0f));
            FullStretch(hurt.rectTransform);
            hurt.raycastTarget = false;
            hurt.transform.SetAsLastSibling();
            var soVfx = new SerializedObject(vfx);
            soVfx.FindProperty("cam").objectReferenceValue = camGo.GetComponent<Camera>();
            soVfx.FindProperty("container").objectReferenceValue = vfxStretch;
            soVfx.FindProperty("hurtVignette").objectReferenceValue = hurt;
            soVfx.FindProperty("playerNode").objectReferenceValue = player.transform;
            soVfx.FindProperty("rootCanvas").objectReferenceValue = canvas;
            soVfx.ApplyModifiedPropertiesWithoutUndo();

            // 3) HUD（同时创建装备/强化/暂停入口按钮）
            var refs = BuildHud(cv, player);

            // 4) 摇杆（左下）
            BuildJoystick(cv);

            // 5) 技能/吞噬按钮（底部：强吞矩形 + 冲撞大圆）
            BuildSkillButtons(cv, player);

            // 6) 小地图（左上，关卡名下方）
            BuildMiniMap(cv, player);

            // 7) 弹窗最后创建：永远盖在 HUD / 摇杆 / 按钮 / 小地图 最上层
            BuildPanels(cv, refs);
        }

        /// <summary>HUD 创建的、需要转交给各弹窗面板的入口按钮</summary>
        private class HudRefs
        {
            public Button equipBtn;     // 装备 → ShopPanel
            public Button upgradeBtn;   // 强化 → UpgradePanel
            public Text upgradeBadge;   // 强化可用点数红点
            public Button pauseBtn;     // 暂停 → PausePanel
        }

        // ---------------- HUD（竖屏：顶部三横条 + 第二行统计 + 右侧入口） ----------------
        private static HudRefs BuildHud(Transform cv, GameObject player)
        {
            var refs = new HudRefs();
            var hudGo = UiFactory.Stretch("HUD", cv).gameObject;
            var hud = hudGo.AddComponent<HUDView>();

            // 浮动文字层（铺满，飘字出现在屏幕中部）
            var floatRoot = UiFactory.Stretch("FloatRoot", hudGo.transform);

            // ===== 顶部三条全宽进度条 =====
            // 1) 血条（全宽，数值居中）
            var hpFrame = UiFactory.Image("HP_Frame", hudGo.transform, UiFactory.Ui("img_hud_bar_frame"), Color.white);
            UiFactory.Place(hpFrame.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -18f), new Vector2(718f, 42f));
            var hpFill = UiFactory.Image("HP_Fill", hpFrame.transform, UiFactory.Ui("img_hud_bar_fill_hp"), Color.white);
            UiFactory.Place(hpFill.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(6f, 0f), new Vector2(706f, 32f));
            SetFilled(hpFill, Image.FillMethod.Horizontal, (int)Image.OriginHorizontal.Left);
            var hpLabel = UiFactory.Text("HpLabel", hpFrame.transform, "", 24, TextAnchor.MiddleCenter, Color.white, new Vector2(706f, 32f));
            UiFactory.Place(hpLabel.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(706f, 32f));

            // 2) 经验条（全宽，EXP 数值居中）
            var expFrame = UiFactory.Image("EXP_Frame", hudGo.transform, UiFactory.Ui("img_hud_bar_frame"), new Color(1f, 1f, 1f, 0.92f));
            UiFactory.Place(expFrame.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -66f), new Vector2(718f, 36f));
            var expFill = UiFactory.Image("EXP_Fill", expFrame.transform, UiFactory.Ui("img_hud_bar_fill_exp"), Color.white);
            UiFactory.Place(expFill.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(6f, 0f), new Vector2(706f, 26f));
            SetFilled(expFill, Image.FillMethod.Horizontal, (int)Image.OriginHorizontal.Left);
            var expLabel = UiFactory.Text("ExpLabel", expFrame.transform, "", 22, TextAnchor.MiddleCenter, Color.white, new Vector2(706f, 26f));
            UiFactory.Place(expLabel.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(706f, 26f));

            // 3) 消灭敌人进度条（靠左，给右侧“装备”按钮留位）
            var progFrame = UiFactory.Image("PROG_Frame", hudGo.transform, UiFactory.Ui("img_hud_bar_frame"), new Color(1f, 1f, 1f, 0.92f));
            UiFactory.Place(progFrame.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(18f, -108f), new Vector2(584f, 36f));
            var progFill = UiFactory.Image("PROG_Fill", progFrame.transform, UiFactory.Ui("img_hud_bar_fill_prog"), Color.white);
            UiFactory.Place(progFill.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(6f, 0f), new Vector2(572f, 26f));
            SetFilled(progFill, Image.FillMethod.Horizontal, (int)Image.OriginHorizontal.Left);
            var progLabel = UiFactory.Text("ProgLabel", progFrame.transform, "", 22, TextAnchor.MiddleCenter, new Color(1f, 1f, 1f), new Vector2(572f, 26f));
            UiFactory.Place(progLabel.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(572f, 26f));

            // ===== 第二行：左侧横排统计（金币 / Lv阶 / 吞噬数）=====
            var coinIcon = UiFactory.Image("CoinIcon", hudGo.transform, UiFactory.Ui("img_hud_icon_coin"), Color.white);
            UiFactory.Place(coinIcon.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(24f, -158f), new Vector2(38f, 38f));
            var goldText = UiFactory.Text("GoldText", hudGo.transform, "0", 28, TextAnchor.MiddleLeft, new Color(1f, 0.9f, 0.4f), new Vector2(120f, 38f));
            UiFactory.Place(goldText.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(68f, -158f), new Vector2(120f, 38f));

            var lvIcon = UiFactory.Image("LvIcon", hudGo.transform, UiFactory.Ui("img_hud_icon_level"), Color.white);
            UiFactory.Place(lvIcon.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(196f, -158f), new Vector2(38f, 38f));
            var lvText = UiFactory.Text("LvText", hudGo.transform, "Lv.1 阶0", 26, TextAnchor.MiddleLeft, Color.white, new Vector2(220f, 38f));
            UiFactory.Place(lvText.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(240f, -158f), new Vector2(220f, 38f));

            var killIcon = UiFactory.Image("KillIcon", hudGo.transform, UiFactory.Ui("img_hud_icon_kill"), Color.white);
            UiFactory.Place(killIcon.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(408f, -158f), new Vector2(38f, 38f));
            var killText = UiFactory.Text("KillText", hudGo.transform, "吞噬 0", 26, TextAnchor.MiddleLeft, Color.white, new Vector2(170f, 38f));
            UiFactory.Place(killText.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(452f, -158f), new Vector2(170f, 38f));

            // 关卡名（小地图上方标题）
            var levelName = UiFactory.Text("LevelName", hudGo.transform, "农场草原 · 初醒", 26, TextAnchor.MiddleLeft, new Color(1f, 0.914f, 0.659f), new Vector2(420f, 34f));
            UiFactory.Place(levelName.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(24f, -206f), new Vector2(420f, 34f));

            // 连击提示（中上，HUDView 按连击数控制显隐）
            var combo = UiFactory.Text("ComboLabel", hudGo.transform, "", 34, TextAnchor.MiddleCenter, new Color(1f, 0.85f, 0.3f), new Vector2(420f, 50f));
            UiFactory.Place(combo.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -430f), new Vector2(420f, 50f));
            combo.gameObject.SetActive(false);

            // ===== 右侧竖排入口：装备 / 强化（带红点）=====
            var equipBtn = UiFactory.Button("EquipBtn", hudGo.transform, UiFactory.Ui("btn_secondary"), new Vector2(108f, 56f), Color.white);
            UiFactory.Place(equipBtn.transform as RectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-18f, -104f), new Vector2(108f, 56f));
            UiFactory.Text("Label", equipBtn.transform, "装备", 26, TextAnchor.MiddleCenter, Color.white, new Vector2(108f, 56f));
            refs.equipBtn = equipBtn;

            var upgradeBtn = UiFactory.Button("UpgradeBtn", hudGo.transform, UiFactory.Ui("btn_secondary"), new Vector2(108f, 56f), new Color(0.62f, 0.72f, 0.86f, 1f));
            UiFactory.Place(upgradeBtn.transform as RectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-18f, -166f), new Vector2(108f, 56f));
            UiFactory.Text("Label", upgradeBtn.transform, "强化", 26, TextAnchor.MiddleCenter, Color.white, new Vector2(108f, 56f));
            // 红点角标：Text 为父（显示数字），红色底为其子级并压到文字下层，父级统一显隐
            var badge = UiFactory.Text("Badge", upgradeBtn.transform, "", 22, TextAnchor.MiddleCenter, Color.white, new Vector2(38f, 38f));
            UiFactory.Place(badge.rectTransform, new Vector2(1f, 1f), new Vector2(0.5f, 0.5f), new Vector2(8f, 8f), new Vector2(38f, 38f));
            var badgeBgRt = UiFactory.Stretch("BadgeBg", badge.transform);
            var badgeBg = badgeBgRt.gameObject.AddComponent<Image>();
            badgeBg.color = new Color(0.88f, 0.29f, 0.23f);
            badgeBg.raycastTarget = false;
            badgeBg.transform.SetAsFirstSibling();
            badge.gameObject.SetActive(false);
            refs.upgradeBtn = upgradeBtn;
            refs.upgradeBadge = badge;

            // 暂停（血条右上角圆形）
            var pauseBtn = UiFactory.Button("PauseBtn", hudGo.transform, UiFactory.Ui("btn_pause"), new Vector2(58f, 58f), Color.white);
            UiFactory.Place(pauseBtn.transform as RectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-86f, -14f), new Vector2(58f, 58f));
            refs.pauseBtn = pauseBtn;

            // 写回 HUDView
            var so = new SerializedObject(hud);
            so.FindProperty("hpFill").objectReferenceValue = hpFill;
            so.FindProperty("expFill").objectReferenceValue = expFill;
            so.FindProperty("progFill").objectReferenceValue = progFill;
            so.FindProperty("hpLabel").objectReferenceValue = hpLabel;
            so.FindProperty("expLabel").objectReferenceValue = expLabel;
            so.FindProperty("progLabel").objectReferenceValue = progLabel;
            so.FindProperty("levelLabel").objectReferenceValue = lvText;
            so.FindProperty("killLabel").objectReferenceValue = killText;
            so.FindProperty("coinLabel").objectReferenceValue = goldText;
            so.FindProperty("comboLabel").objectReferenceValue = combo;
            so.FindProperty("levelNameLabel").objectReferenceValue = levelName;
            so.FindProperty("floatRoot").objectReferenceValue = floatRoot;
            so.ApplyModifiedPropertiesWithoutUndo();

            return refs;
        }

        private static void SetFilled(Image img, Image.FillMethod m, int origin)
        {
            img.type = Image.Type.Filled;
            img.fillMethod = m;
            img.fillOrigin = origin;
        }

        // ---------------- 弹窗 ----------------
        private static void BuildPanels(Transform cv, HudRefs refs)
        {
            // 关键：面板组件所在的「容器根」必须常驻 active，否则 Start 不执行，
            // 按钮监听与通关事件都不会注册。真正的可视面板放内部 View，由各 Panel 的 Start 自行隐藏。

            // UpgradePanel（强化入口 + 红点角标）：View 全屏，内含全屏遮罩 + 居中板
            var upRoot = UiFactory.Stretch("UpgradePanel", cv).gameObject;
            var up = upRoot.AddComponent<UpgradePanel>();
            var upView = UiFactory.Stretch("View", upRoot.transform).gameObject;
            BuildCardPanel(upView, up, UiFactory.Ui("img_card_common"), UiFactory.Ui("img_card_rare"), UiFactory.Ui("img_card_legend"), refs.upgradeBtn, refs.upgradeBadge);

            // ShopPanel（装备入口）
            var shopRoot = UiFactory.Stretch("ShopPanel", cv).gameObject;
            var shop = shopRoot.AddComponent<ShopPanel>();
            var shopView = UiFactory.Stretch("View", shopRoot.transform).gameObject;
            BuildShopPanel(shopView, shop, refs.equipBtn);

            // PausePanel（暂停入口）
            var pauseRoot = UiFactory.Stretch("PausePanel", cv).gameObject;
            var pause = pauseRoot.AddComponent<PausePanel>();
            var pauseView = UiFactory.Stretch("View", pauseRoot.transform).gameObject;
            BuildPausePanel(pauseView, pause, refs.pauseBtn);

            // ResultPanel：全屏，其 Start 会先订阅通关事件再把自己隐藏，Show 时再激活
            var resRoot = UiFactory.Stretch("ResultPanel", cv).gameObject;
            var res = resRoot.AddComponent<ResultPanel>();
            BuildResultPanel(resRoot, res);
        }

        /// <summary>把一个 RectTransform 设为四向拉伸铺满父级</summary>
        private static void FullStretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        /// <summary>弹层通用：全屏压暗遮罩（拦截背后交互）+ 居中木质内容板，返回内容板 Transform</summary>
        private static RectTransform ModalShell(GameObject view, Vector2 boardSize, string titleText)
        {
            // 纯色全屏遮罩（不依赖任何贴图，避免小图 Simple 拉伸异常导致只压暗中间）
            var dim = UiFactory.Image("Dim", view.transform, null, new Color(0.04f, 0.03f, 0.02f, 0.8f));
            FullStretch(dim.rectTransform);
            dim.raycastTarget = true;
            dim.type = Image.Type.Simple;

            var board = UiFactory.Image("Board", view.transform, UiFactory.Ui("img_panel_result"), new Color(0.55f, 0.43f, 0.39f, 1f));
            UiFactory.Place(board.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, boardSize);
            board.raycastTarget = true;

            if (!string.IsNullOrEmpty(titleText))
            {
                var title = UiFactory.Text("Title", board.transform, titleText, 38, TextAnchor.MiddleCenter, new Color(1f, 0.93f, 0.66f), new Vector2(boardSize.x - 60f, 70f));
                UiFactory.Place(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -56f), new Vector2(boardSize.x - 60f, 70f));
            }
            return board.rectTransform;
        }

        private static void BuildCardPanel(GameObject root, UpgradePanel up, Sprite cCard, Sprite rCard, Sprite lCard, Button openBtn, Text badge)
        {
            var board = ModalShell(root, new Vector2(680f, 940f), "选择强化");
            Transform b = board;

            // 剩余点数提示（UpgradePanel.pointLabel，运行时刷新文案）
            var pointLabel = UiFactory.Text("PointLabel", b, "选择一项强化", 24, TextAnchor.MiddleCenter, new Color(1f, 0.93f, 0.66f), new Vector2(600f, 40f));
            UiFactory.Place(pointLabel.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -118f), new Vector2(600f, 40f));

            var buttons = new Button[3];
            var texts = new Text[3];
            var icons = new Image[3];
            var glows = new Image[3];
            Sprite[] skins = { cCard, rCard, lCard };

            for (int i = 0; i < 3; i++)
            {
                // 卡片物体自身带底图与按钮（与装备商店同一可靠结构），图标/文字为其子级
                var cardGo = new GameObject("Card" + i, typeof(RectTransform));
                cardGo.transform.SetParent(b, false);
                UiFactory.Place(cardGo.transform as RectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 200f - i * 220f), new Vector2(580f, 190f));

                var face = cardGo.AddComponent<Image>();
                face.sprite = skins[i];
                face.color = Color.white;
                face.raycastTarget = true;
                buttons[i] = cardGo.AddComponent<Button>();
                buttons[i].targetGraphic = face;

                var glow = UiFactory.Image("Glow", cardGo.transform, skins[i], new Color(1f, 0.9f, 0.4f, 0.3f));
                UiFactory.Place(glow.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(600f, 210f));
                glow.transform.SetAsFirstSibling();
                glows[i] = glow;

                var ic = UiFactory.Image("Icon", cardGo.transform, UiFactory.Ui("img_icon_atkspeed_up"), Color.white);
                UiFactory.Place(ic.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(56f, 0f), new Vector2(108f, 108f));
                icons[i] = ic;

                var lb = UiFactory.Text("Label", cardGo.transform, "强化 " + (i + 1), 26, TextAnchor.MiddleLeft, Color.white, new Vector2(400f, 180f));
                UiFactory.Place(lb.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(140f, 0f), new Vector2(420f, 180f));
                texts[i] = lb;
            }

            // 点击遮罩空白处 = 收牌继续（剩余点数保留）
            Button dimClose = null;
            var dimTr = root.transform.Find("Dim");
            if (dimTr != null)
            {
                var dimImg = dimTr.GetComponent<Image>();
                dimClose = dimTr.gameObject.AddComponent<Button>();
                if (dimImg != null) dimClose.targetGraphic = dimImg;
            }

            var so = new SerializedObject(up);
            so.FindProperty("panel").objectReferenceValue = root;
            so.FindProperty("openBtn").objectReferenceValue = openBtn;
            so.FindProperty("badge").objectReferenceValue = badge;
            so.FindProperty("pointLabel").objectReferenceValue = pointLabel;
            so.FindProperty("closeBtn").objectReferenceValue = dimClose;
            var ar = so.FindProperty("cards"); ar.arraySize = 3;
            for (int i = 0; i < 3; i++) ar.GetArrayElementAtIndex(i).objectReferenceValue = buttons[i];
            var ar2 = so.FindProperty("texts"); ar2.arraySize = 3;
            for (int i = 0; i < 3; i++) ar2.GetArrayElementAtIndex(i).objectReferenceValue = texts[i];
            var ar3 = so.FindProperty("icons"); ar3.arraySize = 3;
            for (int i = 0; i < 3; i++) ar3.GetArrayElementAtIndex(i).objectReferenceValue = icons[i];
            var ar4 = so.FindProperty("glows"); ar4.arraySize = 3;
            for (int i = 0; i < 3; i++) ar4.GetArrayElementAtIndex(i).objectReferenceValue = glows[i];
            var ar5 = so.FindProperty("cardSkins"); ar5.arraySize = 3;
            for (int i = 0; i < 3; i++) ar5.GetArrayElementAtIndex(i).objectReferenceValue = skins[i];
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void BuildShopPanel(GameObject root, ShopPanel shop, Button shopBtn)
        {
            var board = ModalShell(root, new Vector2(688f, 1080f), "装备商店");
            Transform b = board;

            var closeBtn = UiFactory.Button("Close", b, UiFactory.Ui("btn_secondary"), new Vector2(72f, 72f), Color.white);
            UiFactory.Place(closeBtn.transform as RectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-28f, -24f), new Vector2(72f, 72f));
            UiFactory.Text("Label", closeBtn.transform, "×", 40, TextAnchor.MiddleCenter, Color.white, new Vector2(72f, 72f));

            var coinLabel = UiFactory.Text("CoinLabel", b, "局内金币 0", 28, TextAnchor.MiddleCenter, new Color(1f, 0.9f, 0.4f), new Vector2(420f, 48f));
            UiFactory.Place(coinLabel.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -128f), new Vector2(420f, 48f));

            var slots = new Button[4];
            var icons = new Image[4];
            var texts = new Text[4];
            for (int i = 0; i < 4; i++)
            {
                var slotGo = new GameObject("Slot" + i, typeof(RectTransform));
                slotGo.transform.SetParent(b, false);
                UiFactory.Place(slotGo.transform as RectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 176f - i * 200f), new Vector2(600f, 184f));

                var card = UiFactory.Image("Card", slotGo.transform, UiFactory.Ui("img_card_common"), Color.white);
                UiFactory.Place(card.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(600f, 188f));
                card.raycastTarget = true;
                icons[i] = UiFactory.Image("Icon", slotGo.transform, UiFactory.Ui("img_icon_atkspeed_up"), Color.white);
                UiFactory.Place(icons[i].rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(56f, 0f), new Vector2(120f, 120f));
                texts[i] = UiFactory.Text("Label", slotGo.transform, "商品 " + (i + 1), 24, TextAnchor.MiddleLeft, Color.white, new Vector2(400f, 160f));
                UiFactory.Place(texts[i].rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(132f, 0f), new Vector2(440f, 160f));
                slots[i] = card.gameObject.AddComponent<Button>();
                slots[i].targetGraphic = card;
            }

            var so = new SerializedObject(shop);
            so.FindProperty("panel").objectReferenceValue = root;
            so.FindProperty("shopBtn").objectReferenceValue = shopBtn;
            so.FindProperty("closeBtn").objectReferenceValue = closeBtn;
            so.FindProperty("coinLabel").objectReferenceValue = coinLabel;
            var ar = so.FindProperty("slots"); ar.arraySize = 4;
            for (int i = 0; i < 4; i++) ar.GetArrayElementAtIndex(i).objectReferenceValue = slots[i];
            var ar2 = so.FindProperty("icons"); ar2.arraySize = 4;
            for (int i = 0; i < 4; i++) ar2.GetArrayElementAtIndex(i).objectReferenceValue = icons[i];
            var ar3 = so.FindProperty("texts"); ar3.arraySize = 4;
            for (int i = 0; i < 4; i++) ar3.GetArrayElementAtIndex(i).objectReferenceValue = texts[i];
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void BuildPausePanel(GameObject root, PausePanel pause, Button pauseBtn)
        {
            var board = ModalShell(root, new Vector2(640f, 840f), "暂停");
            Transform b = board;

            var resumeBtn = UiFactory.Button("Resume", b, UiFactory.Ui("btn_primary"), new Vector2(440f, 96f), Color.white);
            UiFactory.Place(resumeBtn.transform as RectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 214f), new Vector2(440f, 96f));
            UiFactory.Text("Label", resumeBtn.transform, "继续游戏", 30, TextAnchor.MiddleCenter, new Color(0.36f, 0.22f, 0.08f), new Vector2(440f, 96f));

            var atkBtn = UiFactory.Button("AtkUp", b, UiFactory.Ui("btn_secondary"), new Vector2(460f, 84f), Color.white);
            UiFactory.Place(atkBtn.transform as RectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 96f), new Vector2(460f, 84f));
            var atkLabel = UiFactory.Text("Label", atkBtn.transform, "攻击力 Lv 0", 24, TextAnchor.MiddleCenter, Color.white, new Vector2(460f, 84f));

            var hpBtn = UiFactory.Button("HpUp", b, UiFactory.Ui("btn_secondary"), new Vector2(460f, 84f), Color.white);
            UiFactory.Place(hpBtn.transform as RectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 4f), new Vector2(460f, 84f));
            var hpLabel = UiFactory.Text("Label", hpBtn.transform, "生命值 Lv 0", 24, TextAnchor.MiddleCenter, Color.white, new Vector2(460f, 84f));

            var devourBtn = UiFactory.Button("DevourUp", b, UiFactory.Ui("btn_secondary"), new Vector2(460f, 84f), Color.white);
            UiFactory.Place(devourBtn.transform as RectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -88f), new Vector2(460f, 84f));
            var devourLabel = UiFactory.Text("Label", devourBtn.transform, "吞噬半径 Lv 0", 24, TextAnchor.MiddleCenter, Color.white, new Vector2(460f, 84f));

            var goldLabel = UiFactory.Text("Gold", b, "累计金币 0", 24, TextAnchor.MiddleCenter, new Color(1f, 0.9f, 0.4f), new Vector2(460f, 48f));
            UiFactory.Place(goldLabel.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -190f), new Vector2(460f, 48f));

            var so = new SerializedObject(pause);
            so.FindProperty("panel").objectReferenceValue = root;
            so.FindProperty("pauseBtn").objectReferenceValue = pauseBtn;
            so.FindProperty("resumeBtn").objectReferenceValue = resumeBtn;
            so.FindProperty("atkBtn").objectReferenceValue = atkBtn;
            so.FindProperty("hpBtn").objectReferenceValue = hpBtn;
            so.FindProperty("devourBtn").objectReferenceValue = devourBtn;
            so.FindProperty("atkLabel").objectReferenceValue = atkLabel;
            so.FindProperty("hpLabel").objectReferenceValue = hpLabel;
            so.FindProperty("devourLabel").objectReferenceValue = devourLabel;
            so.FindProperty("goldLabel").objectReferenceValue = goldLabel;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void BuildResultPanel(GameObject root, ResultPanel res)
        {
            var board = ModalShell(root, new Vector2(680f, 960f), null);
            Transform b = board;

            var title = UiFactory.Text("Title", b, "胜利！", 54, TextAnchor.MiddleCenter, new Color(1f, 0.9f, 0.4f), new Vector2(600f, 100f));
            UiFactory.Place(title.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 330f), new Vector2(600f, 100f));

            var info = UiFactory.Text("Info", b, "击杀 0 · 时间 00:00", 28, TextAnchor.MiddleCenter, new Color(1f, 0.95f, 0.85f), new Vector2(600f, 60f));
            UiFactory.Place(info.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 226f), new Vector2(600f, 60f));

            var stars = new Image[3];
            for (int i = 0; i < 3; i++)
            {
                stars[i] = UiFactory.Image("Star" + i, b, UiFactory.Ui("img_star_off"), Color.white);
                UiFactory.Place(stars[i].rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2((i - 1) * 130f, 110f), new Vector2(110f, 110f));
            }

            var restart = UiFactory.Button("Restart", b, UiFactory.Ui("btn_secondary"), new Vector2(220f, 96f), Color.white);
            UiFactory.Place(restart.transform as RectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-150f, -300f), new Vector2(220f, 96f));
            UiFactory.Text("Label", restart.transform, "重新开始", 26, TextAnchor.MiddleCenter, Color.white, new Vector2(220f, 96f));

            var nextBtn = UiFactory.Button("Next", b, UiFactory.Ui("btn_primary"), new Vector2(220f, 96f), Color.white);
            UiFactory.Place(nextBtn.transform as RectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(150f, -300f), new Vector2(220f, 96f));
            UiFactory.Text("Label", nextBtn.transform, "下一关", 26, TextAnchor.MiddleCenter, new Color(0.36f, 0.22f, 0.08f), new Vector2(220f, 60f));
            var nextLabel = UiFactory.Text("NextLabel", nextBtn.transform, "+10 金币", 18, TextAnchor.MiddleCenter, new Color(0.36f, 0.22f, 0.08f, 0.85f), new Vector2(220f, 30f));
            UiFactory.Place(nextLabel.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, -52f), new Vector2(220f, 30f));

            var so = new SerializedObject(res);
            so.FindProperty("title").objectReferenceValue = title;
            so.FindProperty("info").objectReferenceValue = info;
            so.FindProperty("restart").objectReferenceValue = restart;
            so.FindProperty("nextBtn").objectReferenceValue = nextBtn;
            so.FindProperty("nextLabel").objectReferenceValue = nextLabel;
            var ar = so.FindProperty("stars") ?? so.FindProperty("star");
            // ResultPanel 三个独立字段
            so.FindProperty("star0").objectReferenceValue = stars[0];
            so.FindProperty("star1").objectReferenceValue = stars[1];
            so.FindProperty("star2").objectReferenceValue = stars[2];
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ---------------- 摇杆 ----------------
        private static void BuildJoystick(Transform cv)
        {
            var jsGo = new GameObject("Joystick", typeof(RectTransform));
            jsGo.transform.SetParent(cv, false);
            var js = jsGo.AddComponent<Joystick>();

            // 左下大输入热区：这块透明区域内任意位置按下都能拖动摇杆（不必精确按在圆盘上），
            // 事件冒泡到父级 Joystick；宽度收在 0.52 以内，避免压住右下的强吞/冲撞按钮
            var touch = UiFactory.Image("TouchArea", jsGo.transform, null, new Color(1f, 1f, 1f, 0f));
            touch.rectTransform.anchorMin = new Vector2(0f, 0f);
            touch.rectTransform.anchorMax = new Vector2(0.52f, 0.62f);
            touch.rectTransform.offsetMin = Vector2.zero;
            touch.rectTransform.offsetMax = Vector2.zero;
            touch.raycastTarget = true;

            // 大圆盘摇杆，贴左下角；Pad 是视觉底盘，Thumb 是摇柄
            var pad = UiFactory.Image("Pad", jsGo.transform, UiFactory.Ui("img_joystick_base"), Color.white);
            UiFactory.Place(pad.rectTransform, new Vector2(0f, 0f), new Vector2(0.5f, 0.5f), new Vector2(150f, 182f), new Vector2(284f, 284f));
            // 底盘也接收拖拽事件（默认 Image.raycastTarget=false）
            pad.raycastTarget = true;
            pad.transform.SetAsLastSibling();

            // 摇柄挂在底盘下、以盘心为锚点：Joystick 每帧写入的是相对盘心的偏移
            var thumb = UiFactory.Image("Thumb", pad.transform, UiFactory.Ui("img_joystick_thumb"), Color.white);
            UiFactory.Place(thumb.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(112f, 112f));

            UiFactory.Place(jsGo.transform as RectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), Vector2.zero, new Vector2(1f, 1f));

            var so = new SerializedObject(js);
            so.FindProperty("pad").objectReferenceValue = pad.rectTransform;
            so.FindProperty("thumb").objectReferenceValue = thumb.rectTransform;
            so.FindProperty("radius").floatValue = 112f;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ---------------- 技能按钮 ----------------
        private static void BuildSkillButtons(Transform cv, GameObject player)
        {
            // 强吞：黄色圆角矩形，底部中偏右。根节点自带 Image+Button（否则点不动）
            var devourGo = new GameObject("DevourButton", typeof(RectTransform));
            devourGo.transform.SetParent(cv, false);
            var devImg = devourGo.AddComponent<Image>();
            devImg.sprite = UiFactory.Ui("btn_primary");
            devImg.color = new Color(1f, 0.92f, 0.5f);
            devImg.raycastTarget = true;
            var devBtn = devourGo.AddComponent<Button>();
            devBtn.targetGraphic = devImg;
            var devour = devourGo.AddComponent<DevourButton>();
            UiFactory.Place(devourGo.transform as RectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-266f, 150f), new Vector2(168f, 120f));
            var devourLabel = UiFactory.Text("CD", devourGo.transform, "强吞\n30金", 26, TextAnchor.MiddleCenter, new Color(0.36f, 0.22f, 0.08f), new Vector2(168f, 110f));
            UiFactory.Place(devourLabel.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(168f, 110f));
            var devSo = new SerializedObject(devour);
            devSo.FindProperty("cdLabel").objectReferenceValue = devourLabel;
            devSo.FindProperty("playerNode").objectReferenceValue = player.transform;
            devSo.ApplyModifiedPropertiesWithoutUndo();

            // 冲撞：右下最大圆形按钮。根节点自带 Image+Button
            var skillGo = new GameObject("SkillButton", typeof(RectTransform));
            skillGo.transform.SetParent(cv, false);
            var skImg = skillGo.AddComponent<Image>();
            skImg.sprite = UiFactory.Ui("btn_skill_dash");
            skImg.color = new Color(0.80f, 0.63f, 0.52f);
            skImg.raycastTarget = true;
            var skBtn = skillGo.AddComponent<Button>();
            skBtn.targetGraphic = skImg;
            var skill = skillGo.AddComponent<SkillButton>();
            UiFactory.Place(skillGo.transform as RectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-36f, 116f), new Vector2(196f, 196f));
            // CD 遮罩贴按钮顶部，高度随冷却自上而下退去
            var cdMask = UiFactory.Image("CDMask", skillGo.transform, null, new Color(0f, 0f, 0f, 0.55f));
            UiFactory.Place(cdMask.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(196f, 196f));
            var skillLabel = UiFactory.Text("CD", skillGo.transform, "冲撞", 34, TextAnchor.MiddleCenter, Color.white, new Vector2(196f, 110f));
            UiFactory.Place(skillLabel.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 16f), new Vector2(196f, 110f));
            var sub = UiFactory.Text("Sub", skillGo.transform, "冲进敌群", 20, TextAnchor.MiddleCenter, new Color(1f, 0.93f, 0.8f), new Vector2(196f, 34f));
            UiFactory.Place(sub.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 22f), new Vector2(196f, 34f));
            var skSo = new SerializedObject(skill);
            skSo.FindProperty("cdLabel").objectReferenceValue = skillLabel;
            skSo.FindProperty("cdMask").objectReferenceValue = cdMask;
            skSo.FindProperty("playerNode").objectReferenceValue = player.transform;
            skSo.FindProperty("maskHeight").floatValue = 196f;
            skSo.ApplyModifiedPropertiesWithoutUndo();
        }

        // ---------------- 小地图 ----------------
        private static void BuildMiniMap(Transform cv, GameObject player)
        {
            var mmGo = new GameObject("MiniMap", typeof(RectTransform));
            mmGo.transform.SetParent(cv, false);
            var mm = mmGo.AddComponent<MiniMap>();
            // 竖屏：小地图在左上角、关卡名下方（左下是摇杆，不能放那里）
            UiFactory.Place(mmGo.transform as RectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(24f, -246f), new Vector2(200f, 200f));

            var board = UiFactory.Image("Board", mmGo.transform, null, new Color(0.18f, 0.26f, 0.16f, 0.55f));
            UiFactory.Place(board.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(200f, 200f));

            // 玩家点与敌人点统一挂在 Board 下、以底板左下角为原点（MiniMap 每帧写入该坐标）
            var marker = UiFactory.Image("Marker", board.transform, null, new Color(1f, 0.86f, 0.28f));
            UiFactory.Place(marker.rectTransform, new Vector2(0f, 0f), new Vector2(0.5f, 0.5f), new Vector2(100f, 100f), new Vector2(14f, 14f));
            marker.transform.SetAsLastSibling();

            // 坐标在底板下方
            var coordLabel = UiFactory.Text("Coord", mmGo.transform, "0, 0", 18, TextAnchor.MiddleCenter, new Color(1f, 0.914f, 0.659f), new Vector2(200f, 26f));
            UiFactory.Place(coordLabel.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 1f), new Vector2(0f, -6f), new Vector2(200f, 26f));

            var so = new SerializedObject(mm);
            so.FindProperty("board").objectReferenceValue = board.rectTransform;
            so.FindProperty("marker").objectReferenceValue = marker;
            so.FindProperty("coordLabel").objectReferenceValue = coordLabel;
            so.FindProperty("playerNode").objectReferenceValue = player.transform;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ---------------- 工具 ----------------
        private static GameObject Load(string n)
        {
            return AssetDatabase.LoadAssetAtPath<GameObject>(Pf(n));
        }

        private static void EnsureDir(string dir)
        {
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
                AssetDatabase.Refresh();
            }
        }

        private static void AddToBuildSettings(string path)
        {
            var list = new List<EditorBuildSettingsScene>();
            foreach (var s in EditorBuildSettings.scenes) list.Add(s);
            if (!list.Exists(s => s.path == path))
            {
                list.Add(new EditorBuildSettingsScene(path, true));
                EditorBuildSettings.scenes = list.ToArray();
            }
        }
    }
}

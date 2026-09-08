using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace BattleFortress.EditorTools
{
    /// <summary>
    /// 一键生成全部游戏 Prefab：堡垒四阶、五种敌人、Boss 坦克、炮弹/箭矢/地雷/连枷、掉落道具。
    /// 每个 Prefab 根节点保持 scale=1（由代码运行时控制缩放），
    /// 模型放在子节点上并归一化到目标高度 + 底部贴地，避免不同模型单位不一致造成的大小错乱。
    /// </summary>
    public static class FortressPrefabs
    {
        public const string PrefabRoot = "Assets/Prefabs/";
        private const string M = FortressImport.ModelRoot;

        // 模型路径
        private const string Sheep = M + "enemy/CH_Sheep_01.glb";
        private const string Cow = M + "enemy/CH_Cow_01.glb";
        private const string Farmer = M + "enemy/CH_Farmer_01.glb";
        private const string Archer = M + "enemy/CH_Archer_01.glb";
        private const string Rider = M + "enemy/CH_Rider_01.glb";
        private const string BossTank = M + "boss/CH_Boss_Tank_01.glb";

        private const string Fort0 = M + "fort/SM_Fort_Tier0_01.glb";
        private const string Fort1 = M + "fort/SM_Fort_Tier1_01.glb";
        private const string Fort2 = M + "fort/SM_Fort_Tier2_01.glb";
        private const string Fort3 = M + "fort/SM_Fort_Tier3_01.glb";

        private const string CannonBall = M + "prop/SM_Proj_CannonBall_01.glb";
        private const string Arrow = M + "prop/SM_Proj_Arrow_01.glb";
        private const string Mine = M + "prop/SM_Proj_Mine_01.glb";
        private const string Flail = M + "prop/SM_Weapon_Flail_01.glb";
        private const string MineBay = M + "prop/SM_Weapon_MineBay_01.glb";
        private const string MagnetCoil = M + "prop/SM_Weapon_MagnetCoil_01.glb";

        private const string Coin = M + "prop/SM_Item_Coin_01.glb";
        private const string HealthSmall = M + "prop/SM_Item_HealthSmall_01.glb";
        private const string HealthBig = M + "prop/SM_Item_HealthBig_01.glb";
        private const string Magnet = M + "prop/SM_Item_Magnet_01.glb";

        [MenuItem("吞吞堡垒/2-生成全部 Prefab")]
        public static void Run()
        {
            BuildAll();
        }

        public static void BuildAll()
        {
            EnsureDir(PrefabRoot);

            // ---- 投射物 / 副武器（要先建，Player 会引用） ----
            var pfCannonBall = Simple("Proj_CannonBall", CannonBall, 0.5f);
            var pfArrow = Simple("Proj_Arrow", Arrow, 0.5f);
            var pfMine = Simple("Proj_Mine", Mine, 0.4f);
            var pfFlail = Simple("Weapon_Flail", Flail, 0.9f);

            // ---- 掉落道具（扁平/不规则物体按「最大边」归一化，否则薄圆盘会按高度被错误放大） ----
            Simple("Item_Coin", Coin, 0.55f, true);
            Simple("Item_HealthSmall", HealthSmall, 0.5f, true);
            Simple("Item_HealthBig", HealthBig, 0.75f, true);
            Simple("Item_Magnet", Magnet, 0.55f, true);

            // ---- 敌人 ----
            Enemy("Enemy_Sheep", Sheep, 1.1f);
            Enemy("Enemy_Cow", Cow, 1.5f);
            Enemy("Enemy_Farmer", Farmer, 1.7f);
            Enemy("Enemy_Archer", Archer, 1.7f);
            Enemy("Enemy_Rider", Rider, 2.0f);

            // ---- Boss（带骨骼动画） ----
            var pfBoss = Enemy("Enemy_Boss", BossTank, 2.4f);
            SetupBossAnimator(pfBoss);

            // ---- 玩家堡垒 ----
            BuildPlayer(pfCannonBall, pfFlail, pfMine, MineBay, MagnetCoil);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[吞吞堡垒] Prefab 生成完成");
        }

        // ---------------- 通用构建 ----------------
        private static void EnsureDir(string dir)
        {
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
                AssetDatabase.Refresh();
            }
        }

        /// <summary>计算包含所有 Renderer 的世界包围盒</summary>
        private static bool TryBounds(GameObject go, out Bounds bounds)
        {
            var renderers = go.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                bounds = new Bounds();
                return false;
            }
            bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            return true;
        }

        /// <summary>把模型子节点归一化到目标高度，并让底部贴地（y=0）。适用于直立物体（角色/堡垒）</summary>
        private static void Normalize(GameObject model, float targetHeight)
        {
            Bounds b;
            if (!TryBounds(model, out b)) return;
            if (b.size.y <= 0.001f) return;

            float s = targetHeight / b.size.y;
            model.transform.localScale = Vector3.one * s;
            Ground(model);
        }

        /// <summary>按包围盒「最大边」归一化到目标尺寸。适用于扁平/横躺物体（金币圆盘、小瓶、磁铁），
        /// 避免薄圆盘按很薄的高度归一化时被等比放大成巨型。</summary>
        private static void NormalizeMax(GameObject model, float targetSize)
        {
            Bounds b;
            if (!TryBounds(model, out b)) return;
            float m = Mathf.Max(b.size.x, b.size.y, b.size.z);
            if (m <= 0.001f) return;

            model.transform.localScale = Vector3.one * (targetSize / m);
            Ground(model);
        }

        /// <summary>缩放后让模型底部贴地（y=0）</summary>
        private static void Ground(GameObject model)
        {
            Bounds b;
            if (TryBounds(model, out b))
            {
                Vector3 pos = model.transform.localPosition;
                pos.y -= b.min.y;
                model.transform.localPosition = pos;
            }
        }

        /// <summary>创建一个 root + 模型子节点 的简单 Prefab。byMaxDim=true 时按最大边而非高度归一化</summary>
        private static GameObject Simple(string name, string modelPath, float targetHeight, bool byMaxDim = false)
        {
            var root = new GameObject(name);
            AddModel(root, modelPath, targetHeight, byMaxDim);
            return Save(root, name);
        }

        private static GameObject AddModel(GameObject parent, string modelPath, float targetHeight, bool byMaxDim = false)
        {
            if (string.IsNullOrEmpty(modelPath)) return null;
            var src = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            if (src == null)
            {
                Debug.LogWarning("[吞吞堡垒] 模型缺失: " + modelPath);
                return null;
            }
            var model = PrefabUtility.InstantiatePrefab(src) as GameObject;
            if (model == null) model = UnityEngine.Object.Instantiate(src);
            model.name = "Model";
            model.transform.SetParent(parent.transform, false);
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;
            if (targetHeight > 0f)
            {
                if (byMaxDim) NormalizeMax(model, targetHeight);
                else Normalize(model, targetHeight);
            }
            return model;
        }

        private static GameObject Save(GameObject root, string name)
        {
            EnsureDir(PrefabRoot);
            string path = PrefabRoot + name + ".prefab";
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            UnityEngine.Object.DestroyImmediate(root);
            if (prefab == null) Debug.LogError("[吞吞堡垒] Prefab 保存失败: " + path);
            return prefab;
        }

        // ---------------- 敌人 ----------------
        private static GameObject Enemy(string name, string modelPath, float targetHeight)
        {
            var root = new GameObject(name);
            AddModel(root, modelPath, targetHeight);
            root.AddComponent<EnemyUnit>();
            return Save(root, name);
        }

        /// <summary>
        /// Boss 需要 Animator + AnimatorController，才能播放 idle/run/attack/hit/die。
        /// glb 内部虽含 5 个 AnimationClip 子资产，但不会自动生成状态机；
        /// 没有 controller 时运行时 Animator.Play("idle" 等) 会因找不到状态而静默失败，
        /// 因此这里从 glb 子资产取出 clip，代码构建一个同名五状态的 controller 并赋给 Animator。
        /// </summary>
        private static void SetupBossAnimator(GameObject prefab)
        {
            if (prefab == null) return;
            var anim = prefab.GetComponentInChildren<Animator>(true);
            if (anim == null)
            {
                // 模型可能没自动生成 Animator，挂一个
                var model = prefab.transform.Find("Model");
                var target = model != null ? model.gameObject : prefab;
                anim = target.GetComponent<Animator>();
                if (anim == null) anim = target.AddComponent<Animator>();
            }
            anim.applyRootMotion = false;
            anim.cullingMode = AnimatorCullingMode.CullUpdateTransforms;

            // 取出 glb 导入后生成的动画片段子资产
            var clips = AssetDatabase.LoadAllAssetsAtPath(BossTank)
                .OfType<AnimationClip>()
                .Where(c => !c.name.StartsWith("__", StringComparison.Ordinal))
                .ToArray();

            if (clips.Length == 0)
            {
                Debug.LogWarning("[吞吞堡垒] Boss glb 内未找到 AnimationClip，Boss 骨骼动画将不可用：" + BossTank);
            }
            else
            {
                const string ctrlPath = PrefabRoot + "BossAnimator.controller";
                var ac = AnimatorController.CreateAnimatorControllerAtPath(ctrlPath);
                // AnimatorController 没有 stateMachine 属性，状态机挂在各 Layer 下；
                // CreateAnimatorControllerAtPath 会自动创建一个 Base Layer
                var sm = ac.layers[0].stateMachine;
                AnimatorState defaultState = null;
                foreach (var clip in clips)
                {
                    // 状态名严格等于片段名（idle/run/attack/hit/die），供 BossMotion.Play 使用
                    var st = sm.AddState(clip.name);
                    st.motion = clip;
                    if (clip.name == "idle" && defaultState == null) defaultState = st;
                }
                if (defaultState != null) sm.defaultState = defaultState;
                EditorUtility.SetDirty(ac);
                AssetDatabase.SaveAssets();
                anim.runtimeAnimatorController = ac;
                Debug.Log("[吞吞堡垒] Boss AnimatorController 已生成，含片段：" +
                          string.Join("/", clips.Select(c => c.name).ToArray()));
            }

            EditorUtility.SetDirty(prefab);
        }

        // ---------------- 玩家堡垒 ----------------
        private static void BuildPlayer(GameObject pfShell, GameObject pfFlail, GameObject pfMine,
                                        string mineBayPath, string magnetCoilPath)
        {
            var root = new GameObject("Player");

            // 四阶外观（都在同一节点下，运行时只显示当前阶）
            var t0 = AddModel(root, Fort0, 2.0f); if (t0 != null) t0.name = "Tier0";
            var t1 = AddModel(root, Fort1, 2.0f); if (t1 != null) t1.name = "Tier1";
            var t2 = AddModel(root, Fort2, 2.0f); if (t2 != null) t2.name = "Tier2";
            var t3 = AddModel(root, Fort3, 2.0f); if (t3 != null) t3.name = "Tier3";

            // 炮口（空节点，AutoWeapon 从这里取世界坐标）
            var ml = new GameObject("MuzzleL"); ml.transform.SetParent(root.transform, false);
            ml.transform.localPosition = new Vector3(-1.2f, 1.1f, 0f);
            var mr = new GameObject("MuzzleR"); mr.transform.SetParent(root.transform, false);
            mr.transform.localPosition = new Vector3(1.2f, 1.1f, 0f);
            var mf = new GameObject("MuzzleF"); mf.transform.SetParent(root.transform, false);
            mf.transform.localPosition = new Vector3(0f, 1.1f, 1.8f);

            // 副武器挂载外观
            GameObject mineBay = AddModel(root, mineBayPath, 0.6f);
            if (mineBay != null)
            {
                mineBay.name = "MineBayVis";
                mineBay.transform.localPosition = new Vector3(0f, 0.5f, -1.2f);
            }
            GameObject coil = AddModel(root, magnetCoilPath, 0.7f);
            if (coil != null)
            {
                coil.name = "MagnetVis";
                coil.transform.localPosition = new Vector3(0f, 1.6f, -0.6f);
            }

            // 正面直射炮外观已在模型内合成（Tier3 的 dbdp 节点），运行时由 PlayerFortress 按 GS.HasFrontCannon 显隐

            // 组件
            var pf = root.AddComponent<PlayerFortress>();
            var aw = root.AddComponent<AutoWeapon>();
            var sw = root.AddComponent<SubWeapons>();

            // 用 SerializedObject 写私有 [SerializeField] 字段
            var so = new SerializedObject(pf);
            so.FindProperty("tier0").objectReferenceValue = t0;
            so.FindProperty("tier1").objectReferenceValue = t1;
            so.FindProperty("tier2").objectReferenceValue = t2;
            so.FindProperty("tier3").objectReferenceValue = t3;
            so.FindProperty("cam").objectReferenceValue = null;   // 运行时用 Camera.main
            so.ApplyModifiedPropertiesWithoutUndo();

            var soAw = new SerializedObject(aw);
            soAw.FindProperty("shellTpl").objectReferenceValue = pfShell;
            soAw.FindProperty("muzzleL").objectReferenceValue = ml.transform;
            soAw.FindProperty("muzzleR").objectReferenceValue = mr.transform;
            soAw.FindProperty("muzzleF").objectReferenceValue = mf.transform;
            soAw.FindProperty("cam").objectReferenceValue = null;
            soAw.ApplyModifiedPropertiesWithoutUndo();

            var soSw = new SerializedObject(sw);
            soSw.FindProperty("flailTpl").objectReferenceValue = pfFlail;
            soSw.FindProperty("mineTpl").objectReferenceValue = pfMine;
            soSw.FindProperty("mineBayVis").objectReferenceValue = mineBay;
            soSw.FindProperty("magnetVis").objectReferenceValue = coil;
            soSw.ApplyModifiedPropertiesWithoutUndo();

            Save(root, "Player");
        }
    }
}

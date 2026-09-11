using System;
using System.Collections.Generic;
using UnityEngine;

namespace BattleFortress
{
    /// <summary>
    /// 单个武器挂点：负责把「任意 Resources 路径的武器模型」挂到「任意父节点」下。
    ///
    /// 约定：
    /// - 一个 WeaponMount 同一时刻只持有一件武器实例（同一位置只能有一个武器）；
    /// - Equip(新路径) 会自动卸载旧武器，因此可在游戏中任意换武器；
    /// - 武器贴挂点原点：localPosition / localRotation 全部清零，安装位置完全由父节点决定；
    /// - 尺寸自动：按「武器宽度 = 尺寸参考根宽度 × widthRatio」反推统一缩放，
    ///   不接受任何手动缩放，换模型、换形态都按同一比例自适应；
    /// - 父节点允许运行时变化（如四个形态各有一个同名挂点）：通过两个 getter 委托实时解析，
    ///   形态切换后调 Rebind() 把实例平移到新父节点并重算尺寸。
    /// </summary>
    public sealed class WeaponMount
    {
        // 全工程共享的 Resources 预制品缓存（同一武器可能挂在多个挂点上，只加载一次）
        private static readonly Dictionary<string, GameObject> PrefabCache = new Dictionary<string, GameObject>();
        // 每个尺寸参考根只测一次宽度（Renderer 包围盒，世界 X 宽度）
        private static readonly Dictionary<Transform, float> WidthCache = new Dictionary<Transform, float>();

        private readonly Func<Transform> _socketGetter;   // 当前挂点父节点
        private readonly Func<Transform> _sizeRootGetter; // 测量尺寸用的根（一般是当前形态模型根）
        private readonly float _widthRatio;               // 武器宽度占参考根宽度的比例
        private readonly Quaternion _localRot;            // 挂点内局部朝向（如左右武器朝外的 ±90°）
        private readonly int _measureAxis;                // 自动缩放按模型哪根局部轴量长度（0=X 1=Y 2=Z）
        private readonly Vector3 _axisScale;              // 自动适配后再叠加的各轴倍率（用于只加粗不加长等）

        private GameObject _instance;
        private string _currentPath;

        /// <summary>当前挂载的武器 Resources 路径；null/空 表示空挂点</summary>
        public string CurrentPath => _currentPath;
        public bool HasWeapon => _instance != null;
        /// <summary>当前挂载的武器实例根节点（无武器时 null），供战斗层取炮口/播放后坐动画</summary>
        public Transform Instance => _instance != null ? _instance.transform : null;

        public WeaponMount(Func<Transform> socketGetter, Func<Transform> sizeRootGetter,
            float widthRatio = 0.5f, Vector3 localEuler = default, int measureAxis = 0,
            Vector3 axisScale = default)
        {
            _socketGetter = socketGetter ?? throw new ArgumentNullException(nameof(socketGetter));
            _sizeRootGetter = sizeRootGetter ?? socketGetter; // 不单独给尺寸根时，直接以挂点所在层级测量
            _widthRatio = Mathf.Clamp(widthRatio, 0.01f, 5f);
            _localRot = Quaternion.Euler(localEuler);
            _measureAxis = Mathf.Clamp(measureAxis, 0, 2);
            // default(0,0,0) 视为不额外缩放；允许某一轴为 0 时该轴回退为 1
            _axisScale = new Vector3(
                axisScale.x <= 0f ? 1f : axisScale.x,
                axisScale.y <= 0f ? 1f : axisScale.y,
                axisScale.z <= 0f ? 1f : axisScale.z);
        }

        /// <summary>
        /// 装备指定路径武器；路径相同则什么都不做，路径不同先卸载旧武器再挂新武器（天然保证挂点唯一）。
        /// 返回 true 表示本次确实发生了（卸装或）换装。
        /// </summary>
        public bool Equip(string resourcesPath)
        {
            if (string.IsNullOrEmpty(resourcesPath)) { Unequip(); return false; }
            if (_currentPath == resourcesPath && _instance != null) return false;

            UnequipInternal();
            var prefab = LoadPrefab(resourcesPath);
            if (prefab == null)
            {
                Debug.LogWarning($"[WeaponMount] Resources 加载失败：{resourcesPath}");
                return false;
            }

            var socket = _socketGetter();
            if (socket == null)
            {
                Debug.LogWarning($"[WeaponMount] 挂点父节点不存在，无法装备：{resourcesPath}");
                return false;
            }

            _instance = UnityEngine.Object.Instantiate(prefab);
            _instance.name = $"Wpn_{socket.name}_{System.IO.Path.GetFileName(resourcesPath)}";
            _currentPath = resourcesPath;
            Attach(socket);
            return true;
        }

        /// <summary>卸载当前武器（挂点清空）；返回 true 表示本次确实卸掉了一件武器</summary>
        public bool Unequip() => UnequipInternal();

        /// <summary>父节点发生切换（如进化换形态）：把现有武器平移到新挂点并重算尺寸</summary>
        public void Rebind()
        {
            if (_instance == null) return;
            var socket = _socketGetter();
            if (socket != null) Attach(socket);
        }

        /// <summary>彻底释放（对象销毁时调用）</summary>
        public void Dispose() => UnequipInternal();

        // ---------------- 内部实现 ----------------

        private bool UnequipInternal()
        {
            bool had = _instance != null;
            if (_instance != null)
            {
                UnityEngine.Object.Destroy(_instance);
                _instance = null;
            }
            _currentPath = null;
            return had;
        }

        /// <summary>贴到挂点原点并自动算尺寸</summary>
        private void Attach(Transform socket)
        {
            var t = _instance.transform;
            t.SetParent(socket, false);
            t.localPosition = Vector3.zero;
            t.localRotation = Quaternion.identity; // 量尺寸时先不转，保证按模型自身 X 宽度缩放
            t.localScale = Vector3.one;

            float bodyWidth = MeasureBodyWidth(_sizeRootGetter(), _instance.transform);
            float weaponWidth = CombinedAxisLength(_instance.transform, _measureAxis);
            if (bodyWidth > 0.000001f && weaponWidth > 0.000001f)
            {
                float fit = bodyWidth * _widthRatio / weaponWidth;
                t.localScale = Vector3.Scale(Vector3.one * fit, _axisScale); // 先等比适配，再叠加轴向倍率
            }
            t.localRotation = _localRot; // 尺寸算完再转朝向外
        }

        /// <summary>测量某根节点下所有 Renderer 在指定世界轴（0=X 1=Y 2=Z）上的总长度</summary>
        public static float CombinedAxisLength(Transform root, int axis)
        {
            if (root == null) return 0f;
            var renderers = root.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return 0f;
            float mn = float.MaxValue, mx = float.MinValue;
            foreach (var r in renderers)
            {
                mn = Mathf.Min(mn, r.bounds.min[axis]);
                mx = Mathf.Max(mx, r.bounds.max[axis]);
            }
            return Mathf.Max(0f, mx - mn);
        }

        /// <summary>参考根所有激活 Renderer 的世界 X 宽度（排除武器自身，避免换装后越换越大）</summary>
        public static float MeasureBodyWidth(Transform root, Transform excludeWeapon = null)
        {
            if (root == null) return 0f;
            if (WidthCache.TryGetValue(root, out float cached)) return cached;

            var renderers = root.GetComponentsInChildren<Renderer>(false);
            float minX = float.MaxValue, maxX = float.MinValue;
            bool any = false;
            foreach (var r in renderers)
            {
                if (excludeWeapon != null && r.transform.IsChildOf(excludeWeapon)) continue;
                minX = Mathf.Min(minX, r.bounds.min.x);
                maxX = Mathf.Max(maxX, r.bounds.max.x);
                any = true;
            }
            float width = any ? Mathf.Max(0f, maxX - minX) : 0f;
            WidthCache[root] = width;
            return width;
        }

        private static GameObject LoadPrefab(string resourcesPath)
        {
            if (PrefabCache.TryGetValue(resourcesPath, out var cached) && cached != null) return cached;
            cached = Resources.Load<GameObject>(resourcesPath);
            PrefabCache[resourcesPath] = cached;
            return cached;
        }
    }
}

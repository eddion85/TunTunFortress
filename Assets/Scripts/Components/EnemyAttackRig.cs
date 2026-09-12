using System.Collections.Generic;
using UnityEngine;

namespace BattleFortress
{
    /// <summary>
    /// 敌人攻击挂点（AttackModule）的程序化表现，不依赖动画片段：
    /// 1) 按节点名收集炮口（同名全部收集，如弓箭塔 4 个、侧炮车 8 个）、炮塔、炮管；
    /// 2) 炮塔每帧本地 yaw 转向玩家（AimAtPlayer；侧向炮固定朝外时关闭）；
    /// 3) 炮口按相对根节点的本地 X 正负分左右组，侧向开火只用朝玩家那一侧；
    /// 4) 开火时炮管沿本地 -Z 后坐并回位；
    /// 5) 隐藏模型自带 UI / 状态特效节点。
    /// 左右侧炮 vs 普通朝炮的区别只在 EnemyAttackDef.FireMode，本组件两种都支持。
    /// </summary>
    public class EnemyAttackRig : MonoBehaviour
    {
        private Transform[] _muzzles;
        private Transform[] _leftMuzzles;
        private Transform[] _rightMuzzles;
        private Transform[] _turrets;
        private Transform[] _barrels;

        private bool _aimAtPlayer;
        private Transform _player;

        private float[] _recoilT;
        private float _recoilTime;
        private float _recoilDist;
        private Vector3[] _barrelBase;

        public int MuzzleCount => _muzzles == null ? 0 : _muzzles.Length;
        public Transform GetMuzzle(int i) => (_muzzles != null && i >= 0 && i < _muzzles.Length) ? _muzzles[i] : null;

        /// <summary>取车身一侧（left=true 左侧 / false 右侧）的炮口世界坐标</summary>
        public void GetSideMuzzles(bool left, List<Vector3> result)
        {
            var arr = left ? _leftMuzzles : _rightMuzzles;
            if (arr == null) return;
            for (int i = 0; i < arr.Length; i++)
                if (arr[i] != null) result.Add(arr[i].position);
        }

        public void Setup(EnemyAttackDef def)
        {
            _aimAtPlayer = def.AimAtPlayer;
            _muzzles = RigNodes.FindAll(transform, def.MuzzleNode);
            _turrets = RigNodes.FindAll(transform, def.TurretNode);
            _barrels = RigNodes.FindAll(transform, def.BarrelNode);

            // 炮口按相对根节点的本地 X 正负分左右（模型 forward +Z 时左侧为 +X）
            var leftList = new List<Transform>();
            var rightList = new List<Transform>();
            if (_muzzles != null)
            {
                for (int i = 0; i < _muzzles.Length; i++)
                {
                    var m = _muzzles[i];
                    if (m == null) continue;
                    Vector3 lp = transform.InverseTransformPoint(m.position);
                    if (lp.x >= 0f) leftList.Add(m); else rightList.Add(m);
                }
            }
            _leftMuzzles = leftList.ToArray();
            _rightMuzzles = rightList.ToArray();

            if (def.HideNodes != null)
            {
                for (int i = 0; i < def.HideNodes.Length; i++)
                {
                    var found = RigNodes.FindFirst(transform, def.HideNodes[i]);
                    if (found != null) found.gameObject.SetActive(false);
                }
            }

            _recoilDist = def.RecoilDist;
            _recoilTime = Mathf.Max(0.01f, def.RecoilTime);
            if (_barrels != null)
            {
                _recoilT = new float[_barrels.Length];
                _barrelBase = new Vector3[_barrels.Length];
                for (int i = 0; i < _barrels.Length; i++)
                    if (_barrels[i] != null) _barrelBase[i] = _barrels[i].localPosition;
            }
        }

        private void Update()
        {
            if (_aimAtPlayer && _turrets != null && _turrets.Length > 0)
            {
                if (_player == null)
                {
                    var pf = FindObjectOfType<PlayerFortress>();
                    if (pf != null) _player = pf.transform;
                }
                if (_player != null) AimTurrets();
            }
            UpdateRecoil();
        }

        /// <summary>每个炮塔只绕本地 Y 轴转向玩家（普通朝炮：炮管始终对着玩家）</summary>
        private void AimTurrets()
        {
            for (int i = 0; i < _turrets.Length; i++)
            {
                var t = _turrets[i];
                if (t == null) continue;
                Vector3 world = _player.position - t.position;
                Vector3 local = t.parent != null ? t.parent.InverseTransformDirection(world) : world;
                local.y = 0f;
                if (local.sqrMagnitude < 0.0001f) continue;
                float yaw = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
                t.localRotation = Quaternion.Euler(0f, yaw, 0f);
            }
        }

        /// <summary>开火瞬间调用：所有炮管开始一次后坐</summary>
        public void Recoil(float dist, float time)
        {
            if (_barrels == null || dist <= 0f) return;
            _recoilDist = dist;
            _recoilTime = Mathf.Max(0.01f, time);
            for (int i = 0; i < _recoilT.Length; i++) _recoilT[i] = 0f;
        }

        private void UpdateRecoil()
        {
            if (_barrels == null) return;
            for (int i = 0; i < _barrels.Length; i++)
            {
                var b = _barrels[i];
                if (b == null) continue;
                if (_recoilT[i] < _recoilTime)
                {
                    _recoilT[i] += Time.deltaTime;
                    float k = Mathf.Clamp01(_recoilT[i] / _recoilTime);
                    float back = Mathf.Sin(k * Mathf.PI); // 0→1→0
                    b.localPosition = _barrelBase[i] + new Vector3(0f, 0f, -_recoilDist * back);
                }
                else b.localPosition = _barrelBase[i];
            }
        }
    }
}

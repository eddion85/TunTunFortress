using UnityEngine;

namespace BattleFortress
{
    /// <summary>
    /// 车轮表现（程序化，不需要动画片段）：
    /// 1) 找到 Wheels 节点下所有带网格的轮子，每帧按车身水平位移/轮半径绕本地车轴旋转；
    /// 2) 移动时在左右轮位置落土黄车辙印（复用玩家 Fx.PlayTrackMark，按距离间隔）。
    /// 任何带 Wheels 节点的模型都能直接挂：Setup 一次，之后自己 Update。
    /// </summary>
    public class EnemyWheelRig : MonoBehaviour
    {
        private Transform[] _wheels;
        private float[] _radius;
        private Vector3 _lastPos;
        private bool _hasLast;
        private int _spinSign = 1;
        private float _trackScale = GameConfig.Ai.EnemyTrackScale;
        private float _trackAcc; // 距离累计器

        public void Setup(string wheelsNode, int spinSign = 1, float trackScaleMul = 1f)
        {
            _spinSign = spinSign;
            _trackScale = GameConfig.Ai.EnemyTrackScale * Mathf.Max(0.05f, trackScaleMul);
            _wheels = RigNodes.CollectRendered(transform, wheelsNode);
            if (_wheels != null)
            {
                _radius = new float[_wheels.Length];
                for (int i = 0; i < _wheels.Length; i++)
                {
                    var mr = _wheels[i].GetComponent<MeshRenderer>();
                    float r = mr != null ? mr.bounds.size.y * 0.5f : 1f;
                    _radius[i] = Mathf.Max(0.05f, r);
                }
            }
            _hasLast = false;
            _trackAcc = 0f;
        }

        private void Update()
        {
            if (_wheels == null || GS.Paused || GS.Over) return;
            Vector3 p = transform.position;
            if (!_hasLast) { _lastPos = p; _hasLast = true; return; }

            Vector3 d = p - _lastPos;
            d.y = 0f;
            float dist = d.magnitude;
            _lastPos = p;
            if (dist < 0.00001f) return;

            // 1) 轮子滚动
            for (int i = 0; i < _wheels.Length; i++)
            {
                if (_wheels[i] == null) continue;
                float angle = dist / _radius[i] * Mathf.Rad2Deg * _spinSign;
                _wheels[i].Rotate(Vector3.right, angle, Space.Self);
            }

            // 2) 车轮轨迹：按行驶距离间隔，在左右轮位置各落一印
            if (!GameConfig.Ai.EnemyTrackEnabled) return;
            _trackAcc += dist;
            if (_trackAcc < GameConfig.Ai.EnemyTrackDist) return;
            _trackAcc = 0f;

            Vector3 moveDir = d / dist;
            float screenAng = ScreenAngle(p, moveDir);
            Vector3 left, right;
            WheelGroupPositions(out left, out right);
            Emit(left, screenAng);
            Emit(right, screenAng);
        }

        /// <summary>按轮子相对车身的本地 X 正负分左右两组，取各组平均世界位置</summary>
        private void WheelGroupPositions(out Vector3 left, out Vector3 right)
        {
            Vector3 ls = Vector3.zero, rs = Vector3.zero;
            int ln = 0, rn = 0;
            for (int i = 0; i < _wheels.Length; i++)
            {
                var w = _wheels[i];
                if (w == null) continue;
                Vector3 lp = transform.InverseTransformPoint(w.position);
                if (lp.x >= 0f) { ls += w.position; ln++; } else { rs += w.position; rn++; }
            }
            left = ln > 0 ? ls / ln : transform.position;
            right = rn > 0 ? rs / rn : transform.position;
        }

        private void Emit(Vector3 ground, float screenAng)
        {
            ground.y = GameConfig.Ai.EnemyTrackY;
            Fx.PlayTrackMark(ground, screenAng, _trackScale, GameConfig.Ai.EnemyTrackStretch);
        }

        /// <summary>世界水平方向 → UI 屏幕角度（车辙长条沿行驶方向），与玩家车辙同一算法</summary>
        private static float ScreenAngle(Vector3 basePos, Vector3 worldDir)
        {
            Camera c = Camera.main;
            if (c == null || worldDir.sqrMagnitude < 0.0001f) return 0f;
            Vector2 s0 = c.WorldToScreenPoint(basePos);
            Vector2 s1 = c.WorldToScreenPoint(basePos + worldDir);
            return Vector2.SignedAngle(Vector2.up, s1 - s0);
        }
    }
}

using UnityEngine;

namespace BattleFortress
{
    /// <summary>
    /// 掉落物：金币 / 小血包 / 大血包 / 磁铁。
    /// 对应 LayaAir 版 DROPS 数组里的元素。
    /// </summary>
    public class DropItem : MonoBehaviour
    {
        public const string TypeCoin = "coin";
        public const string TypeHealthSmall = "healthSmall";
        public const string TypeHealthBig = "healthBig";
        public const string TypeMagnet = "magnet";

        public string Type = TypeCoin;
        public float Life = GameConfig.Drop.Life;
        public int Value;
        public float Spin;

        public void Init(string type, int value)
        {
            Type = type;
            Value = value;
            Life = GameConfig.Drop.Life;
            Spin = Random.value * 6f;
        }
    }
}

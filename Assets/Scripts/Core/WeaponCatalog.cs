namespace BattleFortress
{
    /// <summary>
    /// 武器外观资源目录：所有挂装模型的 Resources 路径集中在这里，
    /// 业务代码只认武器 id / 等级，不允许在各处散落字符串路径。
    /// 新增武器：把 glb 放进 Resources 后，在这里登记路径并提供查询方法。
    /// </summary>
    public static class WeaponCatalog
    {
        // 车顶火箭炮（4 个等级模型）
        public const string RocketL0 = "art/models/equip/SM_Weapon_Rocket_L0";
        public const string RocketL1 = "art/models/equip/SM_Weapon_Rocket_L1";
        public const string RocketL2 = "art/models/equip/SM_Weapon_Rocket_L2";
        public const string RocketL3 = "art/models/equip/SM_Weapon_Rocket_L3";

        private static readonly string[] Rockets = { RocketL0, RocketL1, RocketL2, RocketL3 };

        /// <summary>按等级取火箭炮模型路径（等级自动夹取到合法范围）</summary>
        public static string Rocket(int level)
        {
            if (level < 0) return null;
            if (level >= Rockets.Length) level = Rockets.Length - 1;
            return Rockets[level];
        }

        // 以后新增武器示例：
        // public const string LaserGun = "art/models/equip/SM_Weapon_Laser";
    }
}

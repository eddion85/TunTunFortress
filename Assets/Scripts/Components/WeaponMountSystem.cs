using System;
using System.Collections.Generic;
using UnityEngine;

namespace BattleFortress
{
    /// <summary>
    /// 武器挂点注册表：游戏内所有「可换装挂点」的统一入口。
    ///
    /// 用法（任意角色、任意位置都一样）：
    /// 1) Bind 一次：给挂点起 id，提供「父节点解析委托」和「尺寸参考根委托」；
    /// 2) Equip(id, Resources路径) 装武器；对同一 id 再 Equip 别的路径 = 直接换武器，旧的自动卸载；
    /// 3) Unequip(id) 卸下；RebindAll() 在宿主换模型/换形态后平移所有武器；UnequipAll() 重开清空。
    ///
    /// 保证：同一个 id 下永远最多一件武器（WeaponMount 内部），同一个 id 只会 Bind 出一个挂点对象。
    /// </summary>
    public sealed class WeaponMountSystem
    {
        private readonly Dictionary<string, WeaponMount> _mounts = new Dictionary<string, WeaponMount>();

        /// <summary>注册（或取回已注册的）挂点。</summary>
        /// <param name="socketId">挂点唯一 id（如 "Slot_Top"）</param>
        /// <param name="socketGetter">当前父节点（允许随形态切换返回不同 Transform）</param>
        /// <param name="sizeRootGetter">自动量尺寸的参考根；为 null 时用挂点自身链</param>
        public WeaponMount Bind(string socketId, Func<Transform> socketGetter,
            Func<Transform> sizeRootGetter = null, float widthRatio = 0.5f)
        {
            if (string.IsNullOrEmpty(socketId)) throw new ArgumentException("socketId 不能为空");
            if (_mounts.TryGetValue(socketId, out var exists)) return exists;
            var mount = new WeaponMount(socketGetter, sizeRootGetter, widthRatio);
            _mounts[socketId] = mount;
            return mount;
        }

        /// <summary>在指定挂点装备/更换武器（同路径不重复处理，换路径自动卸旧装新）</summary>
        public bool Equip(string socketId, string resourcesPath)
        {
            return TryGet(socketId, out var mount) && mount.Equip(resourcesPath);
        }

        /// <summary>卸下指定挂点的武器</summary>
        public void Unequip(string socketId)
        {
            if (TryGet(socketId, out var mount)) mount.Unequip();
        }

        /// <summary>某挂点当前武器路径（空 = 没装）</summary>
        public string CurrentPath(string socketId)
            => TryGet(socketId, out var mount) ? mount.CurrentPath : null;

        public bool HasWeapon(string socketId)
            => TryGet(socketId, out var mount) && mount.HasWeapon;

        /// <summary>宿主换模型/形态后，把所有挂点武器平移到新父节点</summary>
        public void RebindAll()
        {
            foreach (var kv in _mounts) kv.Value.Rebind();
        }

        /// <summary>清空全部挂点（重开关卡时调用）</summary>
        public void UnequipAll()
        {
            foreach (var kv in _mounts) kv.Value.Unequip();
        }

        /// <summary>宿主销毁时释放全部实例</summary>
        public void Dispose()
        {
            foreach (var kv in _mounts) kv.Value.Dispose();
            _mounts.Clear();
        }

        private bool TryGet(string socketId, out WeaponMount mount)
        {
            if (!string.IsNullOrEmpty(socketId) && _mounts.TryGetValue(socketId, out mount)) return true;
            mount = null;
            return false;
        }
    }
}

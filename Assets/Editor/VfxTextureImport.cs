using UnityEditor;
using UnityEngine;

namespace BattleFortress.EditorTools
{
    /// <summary>
    /// 特效贴图统一导入设置：
    /// - 关闭 Mipmap：UI 特效不需要 mip，mip 会把边缘外的黑色混进来形成细黑边；
    /// - WrapMode 改 Clamp：避免采样越界平铺出黑线；
    /// 只改这两项，textureType / spriteMode（单图 vs 序列帧多 Sprite）保持原值不动。
    /// 对 Resources/art/vfx 下的贴图在导入前自动生效，新图丢进来也不用手设。
    /// </summary>
    public sealed class VfxTextureImport : AssetPostprocessor
    {
        private bool IsVfx => assetPath != null &&
                              assetPath.Replace('\\', '/').Contains("Resources/art/vfx/");

        private void OnPreprocessTexture()
        {
            if (!IsVfx) return;
            var ti = (TextureImporter)assetImporter;
            ti.mipmapEnabled = false;
            ti.wrapMode = TextureWrapMode.Clamp;
        }
    }
}

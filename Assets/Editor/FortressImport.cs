using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace BattleFortress.EditorTools
{
    /// <summary>
    /// 一键配置美术资源的 Unity 导入设置：
    /// 模型（FBX/GLB）、UI 贴图（Sprite）、VFX 序列帧（自动切片）、音频。
    /// 首次打开工程后先执行这一步，后面的 Prefab / 场景才能拿到正确格式的资源。
    /// </summary>
    public static class FortressImport
    {
        public const string ArtRoot = "Assets/Resources/art/";
        public const string ModelRoot = ArtRoot + "models/";
        public const string UiRoot = ArtRoot + "ui/";
        public const string VfxRoot = ArtRoot + "vfx/";

        /// <summary>序列帧特效的横向帧数（对应 LayaAir 版 VFX_FRAMES）</summary>
        private static readonly Dictionary<string, int> SheetFrames = new Dictionary<string, int>
        {
            { "T_FX_Hit_Sheet.png", 4 },
            { "T_FX_Explosion_Sheet.png", 4 },
            { "T_FX_Evolve_Sheet.png", 8 }
        };

        [MenuItem("吞吞堡垒/1-配置资源导入设置")]
        public static void Run()
        {
            ConfigureAll();
        }

        public static void ConfigureAll()
        {
            int models = ConfigureModels();
            int ui = ConfigureUiSprites();
            int vfx = ConfigureVfxSheets();
            int audio = ConfigureAudio();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[吞吞堡垒] 导入配置完成：模型 {models} / UI {ui} / 特效 {vfx} / 音频 {audio}");
        }

        // ---------------- 模型 ----------------
        private static int ConfigureModels()
        {
            int n = 0;
            if (!Directory.Exists(ModelRoot)) return 0;

            var files = Directory.GetFiles(ModelRoot, "*.*", SearchOption.AllDirectories);
            foreach (var f in files)
            {
                string p = f.Replace("\\", "/");
                string ext = Path.GetExtension(p).ToLower();
                if (ext != ".glb" && ext != ".gltf" && ext != ".fbx") continue;

                var imp = AssetImporter.GetAtPath(p) as ModelImporter;
                if (imp == null) continue;

                bool dirty = false;
                if (imp.importVisibility != true) { imp.importVisibility = true; dirty = true; }
                if (imp.importBlendShapes != false) { imp.importBlendShapes = false; dirty = true; }
                // 移动端关闭读写，省一半内存
                if (imp.isReadable != false) { imp.isReadable = false; dirty = true; }
                if (imp.meshCompression != ModelImporterMeshCompression.Off)
                { imp.meshCompression = ModelImporterMeshCompression.Off; dirty = true; }
                // 小游戏用不到的动画压缩：保留骨骼动画（Boss 需要）
                if (imp.animationCompression != ModelImporterAnimationCompression.Optimal)
                { imp.animationCompression = ModelImporterAnimationCompression.Optimal; dirty = true; }

                if (dirty)
                {
                    imp.SaveAndReimport();
                    n++;
                }
            }
            return n;
        }

        // ---------------- UI 贴图 ----------------
        private static int ConfigureUiSprites()
        {
            int n = 0;
            if (!Directory.Exists(UiRoot)) return 0;

            foreach (var f in Directory.GetFiles(UiRoot, "*.png", SearchOption.AllDirectories))
            {
                string p = f.Replace("\\", "/");
                var imp = AssetImporter.GetAtPath(p) as TextureImporter;
                if (imp == null) continue;
                if (imp.textureType != TextureImporterType.Sprite) { imp.textureType = TextureImporterType.Sprite; }
                if (imp.spriteImportMode != SpriteImportMode.Single) imp.spriteImportMode = SpriteImportMode.Single;
                if (imp.mipmapEnabled) imp.mipmapEnabled = false;
                if (imp.alphaIsTransparency != true) imp.alphaIsTransparency = true;
                if (imp.spritePivot != new Vector2(0.5f, 0.5f)) imp.spritePivot = new Vector2(0.5f, 0.5f);
                imp.wrapMode = TextureWrapMode.Clamp;
                imp.SaveAndReimport();
                n++;
            }
            return n;
        }

        // ---------------- VFX 序列帧 ----------------
        private static int ConfigureVfxSheets()
        {
            int n = 0;
            if (!Directory.Exists(VfxRoot)) return 0;

            foreach (var f in Directory.GetFiles(VfxRoot, "*.png", SearchOption.AllDirectories))
            {
                string p = f.Replace("\\", "/");
                string name = Path.GetFileName(p);

                var imp = AssetImporter.GetAtPath(p) as TextureImporter;
                if (imp == null) continue;

                imp.textureType = TextureImporterType.Sprite;
                imp.mipmapEnabled = false;
                imp.alphaIsTransparency = true;
                imp.wrapMode = TextureWrapMode.Clamp;

                int frames;
                if (SheetFrames.TryGetValue(name, out frames) && frames > 1)
                {
                    // 先以单图方式导入一次，拿到真实尺寸
                    imp.spriteImportMode = SpriteImportMode.Single;
                    imp.SaveAndReimport();

                    var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(p);
                    if (tex != null && tex.width > 0)
                    {
                        float fw = tex.width / (float)frames;
                        var meta = new SpriteMetaData[frames];
                        for (int i = 0; i < frames; i++)
                        {
                            meta[i] = new SpriteMetaData
                            {
                                name = name + "_" + i,
                                rect = new Rect(i * fw, 0f, fw, tex.height),
                                alignment = (int)SpriteAlignment.Center,
                                pivot = new Vector2(0.5f, 0.5f)
                            };
                        }
                        imp.spriteImportMode = SpriteImportMode.Multiple;
#pragma warning disable 0618 // spritesheet 已过时但仍是编辑切片唯一稳定路径
                        imp.spritesheet = meta;
#pragma warning restore 0618
                        n++;
                    }
                }
                else
                {
                    imp.spriteImportMode = SpriteImportMode.Single;
                }

                imp.SaveAndReimport();
            }
            return n;
        }

        // ---------------- 音频 ----------------
        private static int ConfigureAudio()
        {
            int n = 0;
            string audioRoot = ArtRoot + "audio/";
            if (!Directory.Exists(audioRoot)) return 0;

            foreach (var f in Directory.GetFiles(audioRoot, "*.*", SearchOption.AllDirectories))
            {
                string p = f.Replace("\\", "/");
                string ext = Path.GetExtension(p).ToLower();
                if (ext != ".ogg" && ext != ".mp3" && ext != ".wav") continue;

                var imp = AssetImporter.GetAtPath(p) as AudioImporter;
                if (imp == null) continue;

                bool isBgm = p.Contains("/bgm/");
                var sample = imp.defaultSampleSettings;
                // 移动端推荐：BGM 用 Streaming（省内存），短音效压缩解码（省 CPU 抖动）
                var wantLoad = isBgm ? AudioClipLoadType.Streaming : AudioClipLoadType.DecompressOnLoad;
                var wantQuality = isBgm ? 0.7f : 0.5f;

                if (sample.loadType != wantLoad || sample.quality != wantQuality)
                {
                    sample.loadType = wantLoad;
                    sample.quality = wantQuality;
                    imp.defaultSampleSettings = sample;
                    imp.SaveAndReimport();
                    n++;
                }
            }
            return n;
        }
    }
}

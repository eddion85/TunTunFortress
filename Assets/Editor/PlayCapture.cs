using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BattleFortress.EditorTools
{
    /// <summary>
    /// 批处理运行时验证：打开 GameScene → 进入 PlayMode → 跑若干帧 →
    /// 用主相机按横屏 1334x750 离屏渲染截图，并打印玩家模型/敌人诊断，最后退出。
    /// 命令行：-executeMethod BattleFortress.EditorTools.PlayCapture.Go
    /// </summary>
    public static class PlayCapture
    {
        private const string OutPng = "C:/Users/Administrator/Doubao/chats/2026-09-05/new-chat/laya_assets/verify_play.png";
        private static int _frames;
        private static bool _shot;

        public static void Go()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/GameScene.unity", OpenSceneMode.Single);
            EditorApplication.playModeStateChanged += OnPlayChanged;
            EditorApplication.EnterPlaymode();
        }

        private static void OnPlayChanged(PlayModeStateChange st)
        {
            if (st == PlayModeStateChange.EnteredPlayMode)
            {
                _frames = 0; _shot = false;
                EditorApplication.update += Tick;
            }
        }

        private static void Tick()
        {
            if (!Application.isPlaying) return;
            _frames++;
            // 等 180 帧：让 Spawner 刷怪、相机跟随就位、进化/UI 初始化完成
            if (_frames < 180 || _shot) return;
            _shot = true;

            var player = GameObject.Find("Player");
            int rends = 0;
            Bounds b = new Bounds();
            bool first = true;
            if (player != null)
            {
                foreach (var r in player.GetComponentsInChildren<Renderer>(true))
                {
                    if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
                    if (first) { b = r.bounds; first = false; }
                    else b.Encapsulate(r.bounds);
                    rends++;
                }
            }
            Debug.Log($"[VERIFY] frame={_frames} screen={Screen.width}x{Screen.height} player={(player==null?"NULL":player.name)} activeRenderers={rends} boundsSize={(first?"none":b.size.ToString())} center={(first?"none":b.center.ToString())} enemies={Registry.Enemies.Count} stage={GS.Stage} over={GS.Over}");

            var cam = Camera.main;
            if (cam != null)
            {
                var rt = new RenderTexture(1334, 750, 24, RenderTextureFormat.ARGB32);
                var prev = cam.targetTexture;
                cam.targetTexture = rt;
                cam.Render();
                RenderTexture.active = rt;
                var tex = new Texture2D(1334, 750, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, 1334, 750), 0, 0);
                tex.Apply();
                File.WriteAllBytes(OutPng, tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                cam.targetTexture = prev;
                RenderTexture.active = null;
                rt.Release(); Object.DestroyImmediate(rt);
                Debug.Log("[VERIFY] screenshot saved -> " + OutPng);
            }
            else
            {
                Debug.LogError("[VERIFY] no MainCamera");
            }

            EditorApplication.update -= Tick;
            EditorApplication.ExitPlaymode();
            // 退出 Play 后再关进程
            EditorApplication.playModeStateChanged += WaitStop;
        }

        private static void WaitStop(PlayModeStateChange st)
        {
            if (st == PlayModeStateChange.EnteredEditMode)
            {
                EditorApplication.playModeStateChanged -= WaitStop;
                EditorApplication.Exit(0);
            }
        }
    }
}

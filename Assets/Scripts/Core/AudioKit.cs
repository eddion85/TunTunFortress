using System.Collections.Generic;
using UnityEngine;

namespace BattleFortress
{
    /// <summary>
    /// 音频播放封装（对应 LayaAir 的 Laya.SoundManager）。
    /// 音频资源放在 Assets/Resources/ 下，传相对路径（不含扩展名）。
    /// 资源缺失时静默失败，不影响游戏逻辑。
    /// </summary>
    public static class AudioKit
    {
        private static GameObject _root;
        private static AudioSource _sfx;
        private static AudioSource _music;
        private static string _curMusic = "";
        private static readonly Dictionary<string, AudioClip> _cache = new Dictionary<string, AudioClip>();

        public static bool SfxEnabled = true;
        public static bool MusicEnabled = true;

        private static void Ensure()
        {
            if (_root != null) return;
            _root = new GameObject("[AudioKit]");
            _root.hideFlags = HideFlags.HideAndDontSave;
            Object.DontDestroyOnLoad(_root);
            _sfx = _root.AddComponent<AudioSource>();
            _sfx.playOnAwake = false;
            _sfx.spatialBlend = 0f;
            _music = _root.AddComponent<AudioSource>();
            _music.playOnAwake = false;
            _music.loop = true;
            _music.spatialBlend = 0f;
        }

        private static AudioClip Load(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            AudioClip clip;
            if (_cache.TryGetValue(path, out clip)) return clip;
            clip = Resources.Load<AudioClip>(path);
            _cache[path] = clip;   // 缓存 null，避免重复 IO
            if (clip == null) Debug.LogWarning("[AudioKit] 音频缺失: Resources/" + path);
            return clip;
        }

        public static void PlaySfx(string path)
        {
            if (!SfxEnabled) return;
            Ensure();
            var clip = Load(path);
            if (clip == null) return;
            _sfx.PlayOneShot(clip);
        }

        /// <summary>播放 BGM，同一首不会重复启动</summary>
        public static void PlayMusic(string path)
        {
            if (!MusicEnabled) return;
            Ensure();
            if (_curMusic == path && _music.isPlaying) return;
            _curMusic = path;
            var clip = Load(path);
            if (clip == null) return;
            _music.clip = clip;
            _music.Play();
        }

        public static void StopMusic()
        {
            if (_music == null) return;
            _music.Stop();
            _curMusic = "";
        }
    }
}

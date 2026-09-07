using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BattleFortress.EditorTools
{
    /// <summary>Editor 下批量搭建 UGUI 的小工具库</summary>
    public static class UiFactory
    {
        public const float RefW = 750f;
        public const float RefH = 1334f;

        private static Font _font;

        public static Font Font()
        {
            if (_font != null) return _font;
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (_font == null) _font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            return _font;
        }

        public static RectTransform Rect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            if (parent != null) go.transform.SetParent(parent, false);
            return go.transform as RectTransform;
        }

        /// <summary>四向拉伸铺满父级</summary>
        public static RectTransform Stretch(string name, Transform parent)
        {
            var rt = Rect(name, parent);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            return rt;
        }

        /// <summary>
        /// 按锚点摆放。
        /// </summary>
        public static RectTransform Place(RectTransform rt, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.pivot = pivot;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            return rt;
        }

        public static Image Image(string name, Transform parent, Sprite sprite, Color color)
        {
            var rt = Rect(name, parent);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        public static Text Text(string name, Transform parent, string content, int fontSize,
                                TextAnchor anchor, Color color, Vector2 size)
        {
            var rt = Rect(name, parent);
            var lb = rt.gameObject.AddComponent<Text>();
            lb.font = Font();
            lb.text = content;
            lb.fontSize = fontSize;
            lb.color = color;
            lb.alignment = anchor;
            lb.raycastTarget = false;
            lb.horizontalOverflow = HorizontalWrapMode.Overflow;
            lb.verticalOverflow = VerticalWrapMode.Overflow;
            rt.sizeDelta = size;
            return lb;
        }

        public static Button Button(string name, Transform parent, Sprite sprite, Vector2 size, Color color)
        {
            var rt = Rect(name, parent);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            img.raycastTarget = true;
            rt.sizeDelta = size;
            var btn = rt.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            return btn;
        }

        /// <summary>给 Image 加轮廓描边</summary>
        public static Outline WithOutline(Graphic g, Color color, Vector2 distance)
        {
            var o = g.gameObject.GetComponent<Outline>();
            if (o == null) o = g.gameObject.AddComponent<Outline>();
            o.effectColor = color;
            o.effectDistance = distance;
            return o;
        }

        /// <summary>创建 Canvas（竖屏 750x1334 参考分辨率）</summary>
        public static Canvas CreateCanvas(string name)
        {
            var go = new GameObject(name);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;

            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(RefW, RefH);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            // 竖屏游戏：以设计宽度 750 为基准缩放（match=0），保证横向 UI 不被裁切
            scaler.matchWidthOrHeight = 0f;

            go.AddComponent<GraphicRaycaster>();

            // 事件系统
            if (Object.FindObjectOfType<EventSystem>() == null)
            {
                var es = new GameObject("EventSystem");
                es.AddComponent<EventSystem>();
                es.AddComponent<StandaloneInputModule>();
            }
            return canvas;
        }

        /// <summary>加载 UI 贴图（不存在返回 null，不报错）</summary>
        public static Sprite Ui(string file)
        {
            var sp = AssetDatabase.LoadAssetAtPath<Sprite>(FortressImport.UiRoot + file + ".png");
            return sp;
        }

        public static Sprite Vfx(string file)
        {
            return AssetDatabase.LoadAssetAtPath<Sprite>(FortressImport.VfxRoot + file + ".png");
        }

        public static GameObject Prefab(string name)
        {
            return AssetDatabase.LoadAssetAtPath<GameObject>(FortressPrefabs.PrefabRoot + name + ".prefab");
        }
    }
}

using System;
using UnityEngine;
using UnityEngine.UI;

namespace LuckyCiv.UI
{
    /// <summary>Builds uGUI elements from code so the scene needs no hand-made prefabs.</summary>
    public static class UIFactory
    {
        public static readonly Color PanelColor = new Color(0.07f, 0.09f, 0.13f, 0.9f);
        public static readonly Color RowColor = new Color(1f, 1f, 1f, 0.06f);
        public static readonly Color TextColor = new Color(0.93f, 0.93f, 0.9f);
        public static readonly Color MutedColor = new Color(0.7f, 0.72f, 0.75f);
        public static readonly Color DarkText = new Color(0.1f, 0.09f, 0.07f);
        public static readonly Color Accent = new Color(0.96f, 0.76f, 0.26f);
        public static readonly Color Good = new Color(0.45f, 0.85f, 0.5f);
        public static readonly Color Bad = new Color(0.95f, 0.4f, 0.35f);
        public static readonly Color ButtonColor = new Color(0.22f, 0.32f, 0.48f);
        public static readonly Color ChipColor = new Color(0.3f, 0.35f, 0.42f);
        public static readonly Color SelectedColor = new Color(0.85f, 0.62f, 0.18f);

        static Font defaultFont;

        public static Font DefaultFont
        {
            get
            {
                if (defaultFont == null) defaultFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                return defaultFont;
            }
        }

        public static RectTransform Container(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        public static RectTransform MakePanel(Transform parent, string name, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = color;
            return (RectTransform)go.transform;
        }

        public static Text MakeLabel(Transform parent, string text, int size, Color color,
            TextAnchor anchor = TextAnchor.MiddleLeft, FontStyle style = FontStyle.Normal, bool wrap = true)
        {
            var go = new GameObject("Label", typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var label = go.GetComponent<Text>();
            label.font = DefaultFont;
            label.fontSize = size;
            label.color = color;
            label.alignment = anchor;
            label.fontStyle = style;
            label.text = text;
            label.horizontalOverflow = wrap ? HorizontalWrapMode.Wrap : HorizontalWrapMode.Overflow;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            label.raycastTarget = false;
            return label;
        }

        public static Button MakeButton(Transform parent, string text, Color background, Action onClick,
            int fontSize = 20, float height = 44, Color? textColor = null)
        {
            var rt = MakePanel(parent, "Button", background);
            var button = rt.gameObject.AddComponent<Button>();
            button.targetGraphic = rt.GetComponent<Image>();
            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.15f, 1.15f, 1.15f);
            colors.pressedColor = new Color(0.8f, 0.8f, 0.8f);
            colors.selectedColor = Color.white;
            colors.disabledColor = new Color(1f, 1f, 1f, 0.35f);
            button.colors = colors;

            var label = MakeLabel(rt, text, fontSize, textColor ?? TextColor, TextAnchor.MiddleCenter);
            Stretch(label.rectTransform, 10, 4, 10, 4);
            Size(rt, minHeight: height, preferredHeight: height);
            if (onClick != null) button.onClick.AddListener(() => onClick());
            return button;
        }

        public static void Stretch(RectTransform rt, float left = 0, float bottom = 0, float right = 0, float top = 0)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(left, bottom);
            rt.offsetMax = new Vector2(-right, -top);
        }

        public static void Anchor(RectTransform rt, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
        {
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = offsetMin;
            rt.offsetMax = offsetMax;
        }

        public static VerticalLayoutGroup VStack(RectTransform rt, float spacing, int padding)
        {
            var group = rt.gameObject.AddComponent<VerticalLayoutGroup>();
            group.spacing = spacing;
            group.padding = new RectOffset(padding, padding, padding, padding);
            group.childControlWidth = true;
            group.childControlHeight = true;
            group.childForceExpandWidth = true;
            group.childForceExpandHeight = false;
            return group;
        }

        public static HorizontalLayoutGroup HStack(RectTransform rt, float spacing, int padding)
        {
            var group = rt.gameObject.AddComponent<HorizontalLayoutGroup>();
            group.spacing = spacing;
            group.padding = new RectOffset(padding, padding, padding, padding);
            group.childControlWidth = true;
            group.childControlHeight = true;
            group.childForceExpandWidth = false;
            group.childForceExpandHeight = true;
            group.childAlignment = TextAnchor.MiddleLeft;
            return group;
        }

        public static GridLayoutGroup Grid(RectTransform rt, Vector2 cell, float spacing, int columns)
        {
            var grid = rt.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = cell;
            grid.spacing = new Vector2(spacing, spacing);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = columns;
            grid.childAlignment = TextAnchor.UpperLeft;
            return grid;
        }

        public static LayoutElement Size(Component c, float minHeight = -1, float preferredHeight = -1,
            float minWidth = -1, float preferredWidth = -1, float flexibleWidth = -1, float flexibleHeight = -1)
        {
            var element = c.gameObject.GetComponent<LayoutElement>();
            if (element == null) element = c.gameObject.AddComponent<LayoutElement>();
            element.minHeight = minHeight;
            element.preferredHeight = preferredHeight;
            element.minWidth = minWidth;
            element.preferredWidth = preferredWidth;
            element.flexibleWidth = flexibleWidth;
            element.flexibleHeight = flexibleHeight;
            return element;
        }

        /// <summary>Vertical scroll view. Returns the content container (a vertical stack that grows with its children).</summary>
        public static RectTransform ScrollView(Transform parent, out ScrollRect scroll)
        {
            var root = MakePanel(parent, "Scroll", new Color(0f, 0f, 0f, 0.25f));
            scroll = root.gameObject.AddComponent<ScrollRect>();

            var viewport = Container(root, "Viewport");
            Stretch(viewport);
            viewport.gameObject.AddComponent<RectMask2D>();

            var content = Container(viewport, "Content");
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.offsetMin = Vector2.zero;
            content.offsetMax = Vector2.zero;
            VStack(content, 6, 8);
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            scroll.content = content;
            scroll.viewport = viewport;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 30f;
            return content;
        }

        /// <summary>Removes all children now (deactivated immediately so layout ignores them this frame).</summary>
        public static void Clear(Transform parent)
        {
            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                var child = parent.GetChild(i).gameObject;
                child.SetActive(false);
                UnityEngine.Object.Destroy(child);
            }
        }
    }
}

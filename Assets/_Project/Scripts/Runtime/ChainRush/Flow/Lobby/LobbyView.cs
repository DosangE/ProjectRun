using System.Collections.Generic;
using ProtoHarness.ChainRush.Visuals;
using UnityEngine;

namespace ProtoHarness.ChainRush.Flow.Lobby
{
    // The lobby's IMGUI look, in its units (1600 x 900, scaled like ChainRushHud): the fonts and styles, the frame every
    // page shares (backdrop, brand, heading, back button, notice, key help), the row list and the description panel.
    // Pages draw their own parts with its styles. Made inside OnGUI (styles need GUI.skin); Release frees the fonts.
    public sealed class LobbyView
    {
        public static readonly Color Ink = new Color32(30, 38, 48, 255);
        internal static readonly Color Text = new Color32(34, 38, 46, 255);
        public static readonly Color Muted = new Color32(94, 102, 115, 255);
        public static readonly Color Slate = new Color32(230, 234, 238, 255);
        internal static readonly Color Gold = new Color32(154, 106, 0, 255);
        internal static readonly Color Yellow = new Color32(232, 201, 58, 255);
        private static readonly Color Background = new Color32(242, 244, 247, 255);
        private static readonly Color LockedText = new Color32(95, 103, 116, 255);
        private const float TransitionDuration = .35f, BounceRise = .12f, BouncePeak = 1.08f;

        private readonly struct RunnerColors
        {
            public RunnerColors(Color stripe, Color inner, Color accent)
            {
                Stripe = stripe;
                Inner = inner;
                Accent = accent;
            }

            public Color Stripe { get; }
            public Color Inner { get; }
            public Color Accent { get; }

            public static RunnerColors Lerp(RunnerColors from, RunnerColors to, float t) =>
                new RunnerColors(Color.Lerp(from.Stripe, to.Stripe, t), Color.Lerp(from.Inner, to.Inner, t),
                    Color.Lerp(from.Accent, to.Accent, t));
        }

        private int shownRunner = -1;
        private RunnerColors previousColors;
        private RunnerColors targetColors;
        private RunnerColors drawnColors;
        private float transitionStart;
        private bool transitioning;
        private float stripeScale = 1f;
        // Each locked label is formatted once per lobby.
        private readonly Dictionary<string, string> lockedLabels = new Dictionary<string, string>();
        private readonly Font font;
        private readonly Font displayFont;
        private readonly GUIStyle brand;
        private readonly GUIStyle pageHeading;
        private readonly GUIStyle lockedItem;
        private readonly GUIStyle item;
        private readonly GUIStyle backButton;
        private readonly GUIStyle infoTitle;
        private readonly GUIStyle infoBody;
        private readonly GUIStyle lockTag;
        private readonly GUIStyle helpLine;
        private readonly GUIStyle footLine;

        public LobbyView(Texture2D runnerArtwork)
        {
            RunnerArtwork = runnerArtwork;
            Palette = CreateBrightPalette(Ink);
            font = Font.CreateDynamicFontFromOSFont(new[] { "Malgun Gothic", "Arial" }, 24);
            displayFont = NightUi.DisplayFont();
            if (!Ready) return;
            Small = Style(15, Muted);
            Body = Style(21, Muted);
            Heading = NightUi.Style(displayFont, 44, Ink, FontStyle.BoldAndItalic);
            brand = NightUi.Style(displayFont, 34, Ink, FontStyle.BoldAndItalic);
            pageHeading = NightUi.Style(displayFont, 52, Ink, FontStyle.BoldAndItalic);
            TileLabel = NightUi.Style(displayFont, 23, Text, FontStyle.Bold);
            TileLabel.alignment = TextAnchor.MiddleCenter;
            item = Style(26, Text, FontStyle.Bold);
            item.alignment = TextAnchor.MiddleLeft;
            lockedItem = Style(26, LockedText);
            lockedItem.alignment = TextAnchor.MiddleLeft;
            // A row's value at its right end, or a record under a description on the left.
            RowValue = Style(18, Muted);
            RowValue.alignment = TextAnchor.MiddleRight;
            SettingValue = Style(18, Text);
            SettingValue.alignment = TextAnchor.MiddleRight;
            footLine = Style(18, Muted);
            footLine.alignment = TextAnchor.MiddleLeft;
            EnterButton = Style(28, Text, FontStyle.Bold);
            EnterButton.alignment = TextAnchor.MiddleCenter;
            backButton = Style(22, Text, FontStyle.Bold);
            backButton.alignment = TextAnchor.MiddleCenter;
            infoTitle = Style(38, Text, FontStyle.Bold);
            infoBody = Style(21, Muted);
            infoBody.wordWrap = true;
            lockTag = NightUi.Style(displayFont, 16, Gold, FontStyle.Bold);
            lockTag.alignment = TextAnchor.MiddleRight;
            helpLine = Style(15, Muted);
            helpLine.alignment = TextAnchor.MiddleRight;
        }

        // False when a font could not be made; the styles are then missing and nothing may be drawn.
        public bool Ready => font != null && displayFont != null;
        public Texture2D RunnerArtwork { get; }
        public GUIStyle Small { get; }
        public GUIStyle Body { get; }
        public GUIStyle Heading { get; }
        public GUIStyle TileLabel { get; }
        public GUIStyle RowValue { get; }
        public GUIStyle SettingValue { get; }
        public GUIStyle EnterButton { get; }
        internal NightUi.Palette Palette { get; }

        internal static NightUi.Palette CreateBrightPalette(Color accent)
        {
            Color edge = new Color32(220, 225, 231, 255);
            Color shadow = new Color(Ink.r, Ink.g, Ink.b, .10f);
            return new NightUi.Palette(fill: Color.white, selectedFill: Color.Lerp(Color.white, accent, .16f),
                lockedFill: new Color32(233, 236, 240, 255), edge: edge, selectedEdge: accent, lockedEdge: edge,
                shadow: shadow, shadowOffset: new Vector2(0, 4), hover: new Color(Ink.r, Ink.g, Ink.b, .05f),
                held: shadow, accent: accent, panelTop: Ink, action: NightUi.Action);
        }

        // The lobby's units (1600 x 900) to screen pixels, as OnGUI scales them.
        public static float GuiScale() => Mathf.Min(Screen.width / 1600f, Screen.height / 900f);

        // Sample only on repaint, so a new transition starts at the last colors actually drawn.
        internal void UpdateRunnerColors(int runnerIndex, RunnerProfile runner)
        {
            if (Event.current.type != EventType.Repaint) return;
            float now = Time.unscaledTime;
            if (shownRunner != runnerIndex)
            {
                targetColors = new RunnerColors(runner.LobbyColor, runner.LobbyStripeColor, runner.LobbyAccentColor);
                if (shownRunner < 0)
                {
                    shownRunner = runnerIndex;
                    ApplyColors(targetColors);
                    return;
                }

                shownRunner = runnerIndex;
                previousColors = drawnColors;
                transitionStart = now;
                transitioning = true;
            }
            if (!transitioning) return;

            float elapsed = now - transitionStart;
            if (elapsed >= TransitionDuration)
            {
                ApplyColors(targetColors);
                stripeScale = 1f;
                transitioning = false;
                return;
            }

            ApplyColors(RunnerColors.Lerp(previousColors, targetColors, elapsed / TransitionDuration));
            if (elapsed < BounceRise)
            {
                float remaining = 1f - elapsed / BounceRise;
                stripeScale = Mathf.Lerp(1f, BouncePeak, 1f - remaining * remaining);
            }
            else
            {
                stripeScale = Mathf.SmoothStep(BouncePeak, 1f, (elapsed - BounceRise) / (TransitionDuration - BounceRise));
            }
        }

        private void ApplyColors(RunnerColors colors)
        {
            drawnColors = colors;
            Palette.SetAccent(colors.Accent, Color.Lerp(Color.white, colors.Accent, .16f));
        }

        internal static void DrawBackdrop(float width, float height, Color stripe, Color innerStripe, float stripeScale = 1f)
        {
            NightUi.Block(new Rect(0, 0, width, height), Background);
            DrawBackdropStripe(new Rect(width - 525, -200, 375, 1375), stripe, stripeScale);
            DrawBackdropStripe(new Rect(width - 312, -200, 65, 1375), innerStripe, stripeScale);
            NightUi.Block(new Rect(0, height - 10, width, 10), Ink);
        }

        private static void DrawBackdropStripe(Rect rect, Color color, float scale)
        {
            Matrix4x4 canvas = GUI.matrix;
            Vector2 center = rect.center;
            GUI.matrix = canvas * Matrix4x4.TRS(center, Quaternion.Euler(0, 0, 22f), new Vector3(scale, scale, 1f))
                * Matrix4x4.Translate(-center);
            NightUi.Block(rect, color);
            GUI.matrix = canvas;
        }

        // The backdrop, the brand, the page's breadcrumb and heading, and the back button. Returns whether back was pressed.
        public bool DrawFrame(LobbyPage page, float width, float height)
        {
            if (Event.current.type == EventType.Repaint)
                DrawBackdrop(width, height, drawnColors.Stripe, drawnColors.Inner, stripeScale);
            GUI.Label(new Rect(64, 34, 400, 54), "CHAIN RUSH", brand);
            GUI.Label(new Rect(64, 108, 900, 28), page.Breadcrumb, Small);
            GUI.Label(new Rect(61, 145, 1050, 68), page.Heading, pageHeading);
            // Back sits bottom left, the key help bottom right on its line (user 2026-10-21).
            var backRect = new Rect(64, height - 84, 186, 48);
            return NightUi.CardButton(backRect, "←  이전   ESC", backButton, palette: Palette);
        }

        // The last refusal over the back button, and the page's key help.
        public void DrawFooter(string notice, string help, float width, float height)
        {
            GUI.Label(new Rect(64, height - 140, 1000, 28), notice, Body);
            GUI.Label(new Rect(width * .5f, height - 74, width * .5f - 64, 28), help, helpLine);
        }

        // The page's rows as pressable cards. Returns the row clicked, or -1: the click is acted on after drawing, since
        // choosing opens another page and the rest of the loop would read that page with this page's rows (a click on
        // 싱글 레이스 threw, 2026-10-20).
        public int DrawList(LobbyPage page, int cursor, float width, float height)
        {
            int clicked = -1;
            int count = page.Count;
            for (int i = 0; i < count; i++)
            {
                Rect row = page.RowRect(i, count, width, height);
                NightUi.CardLook look = page.IsLocked(i) ? NightUi.CardLook.Locked
                    : i == cursor ? NightUi.CardLook.Selected : NightUi.CardLook.Plain;
                // The row's content follows the card's face, which sinks while pressed.
                row = NightUi.PressableCard(row, look, out bool pressed, Palette);
                if (pressed) clicked = i;
                page.DrawRow(this, row, i);
            }
            return clicked;
        }

        // A row's name, greyed with (잠김) when locked.
        public void RowLabel(Rect face, string label, bool locked)
        {
            if (locked)
            {
                if (!lockedLabels.TryGetValue(label, out string lockedLabel))
                {
                    lockedLabel = label + "   (잠김)";
                    lockedLabels.Add(label, lockedLabel);
                }
                label = lockedLabel;
            }
            GUI.Label(new Rect(face.x + 26, face.y, face.width - 26, face.height), label, locked ? lockedItem : item);
        }

        // The highlighted row: its page's tag, its name, LOCKED when it is, its description and its foot line.
        public void DrawDescription(LobbyPage page, int cursor, float width)
        {
            var panel = new Rect(width - 620, 238, 556, 330);
            NightUi.PanelCard(panel, Palette);
            float left = panel.x + 34, inner = panel.width - 68;
            GUI.Label(new Rect(left, panel.y + 26, inner, 28), page.DescriptionTag, Small);
            if (page.IsLocked(cursor)) GUI.Label(new Rect(left, panel.y + 26, inner, 28), "LOCKED  ·  준비 중", lockTag);
            GUI.Label(new Rect(left, panel.y + 62, inner, 52), page.Label(cursor), infoTitle);
            NightUi.Block(new Rect(left, panel.y + 126, 48, 3), Yellow);
            GUI.Label(new Rect(left, panel.y + 148, inner, 120), page.Description(cursor), infoBody);
            string foot = page.DescriptionFoot(cursor);
            if (foot.Length > 0) GUI.Label(new Rect(left, panel.yMax - 54, inner, 28), foot, footLine);
        }

        public void Release()
        {
            if (font != null) Object.Destroy(font);
            if (displayFont != null) Object.Destroy(displayFont);
        }

        private GUIStyle Style(int size, Color color, FontStyle weight = FontStyle.Normal) => NightUi.Style(font, size, color, weight);
    }
}

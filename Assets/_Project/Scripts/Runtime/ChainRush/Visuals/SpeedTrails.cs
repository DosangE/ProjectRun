using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace ProtoHarness.ChainRush.Visuals
{
    // Presentation only: what follows the runner at speed (user 2026-11-06, "스윙꼬리 따로 부스터 단일꼬리 따로 스윙+부스터 꼬리
    // 따로, 부스터존에 의한 부스터 꼬리 따로"). One tail at a time: thin amber for a swing, mint for a booster, wide gold for
    // both, green for a boost pad's carry, fading with it. The swing and the booster tails drawn one on the other did not
    // read as a third tail (user 2026-11-06, "부스터 + 스윙 의 꼬리가 안보인다"), so both has its own. Under any booster, white
    // wind lines cut back past the runner (user 2026-11-06, "날카롭게 바람을 가르는 듯한 흰색 뾰족한 선들"): thin spindles,
    // pointed at both ends, shot from all round the runner and fanning back ("여러 방향으로"), read from
    // the user's reference clip. Loose particle streaks were too irregular and sonic rings too round and too many. All of
    // it hangs on the runner's root, so it does not depend on the runner's temporary model (DECISIONS 2026-11-06).
    // Under a booster the tail is layered, a white core and a thin tail off each shoulder, and the wind lines all shoot
    // out at once as it starts (user 2026-11-22 "이펙트가 너무 심심하다").
    public sealed class SpeedTrails : IDisposable
    {
        public enum Style { None, Swing, Booster, SwingBooster, BoostZone }

        private const int LinePoints = 7;
        // Below this share of a pad's speed no new line starts; the tail fades on.
        private const float WeakestLine = 0.05f;
        // Each line's band round the runner, in degrees from its right over the top to its left, none into the road. Every
        // time a line starts it takes a direction within WindLineJitter of its band, a fan back, a reach and a width from a
        // hash of how many lines have started, so no two shots look alike and the sides are not mirrors (user 2026-11-23
        // "선이 굵고 대칭이라 보기가 살짝 불편하다"), yet the bands keep them spread round the runner rather than loose (fully
        // loose streaks were too irregular, 2026-11-06).
        private static readonly float[] LineAround = { -10f, 10f, 30f, 50f, 70f, 85f, 95f, 110f, 130f, 150f, 170f, 190f };
        // The order the lines fire in after the first burst, so the next line jumps about the runner.
        private static readonly int[] LineOrder = { 0, 7, 3, 10, 5, 1, 8, 11, 4, 2, 9, 6 };
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
        private readonly Transform runner;
        private readonly TrailRenderer boostTail;
        private readonly TrailRenderer swingTail;
        private readonly TrailRenderer swingBoostTail;
        // The booster's layers (Booster, SwingBooster, BoostZone): a white core and a tail off each shoulder.
        private readonly TrailRenderer coreTail;
        private readonly TrailRenderer leftTail;
        private readonly TrailRenderer rightTail;
        private bool wasWindy;
        private bool wasDrafting;
        private readonly LineRenderer[] lines = new LineRenderer[LineAround.Length];
        private readonly float[] lineBornAt = new float[LineAround.Length];
        // The shape each line took when it started: direction round and fan back in radians, reach in metres.
        private readonly float[] lineAround = new float[LineAround.Length];
        private readonly float[] lineFan = new float[LineAround.Length];
        private readonly float[] lineReach = new float[LineAround.Length];
        private readonly Vector3[] points = new Vector3[LinePoints];
        private readonly MaterialPropertyBlock tint = new MaterialPropertyBlock();
        private int nextLine;
        private int linesStarted;
        private float nextLineAt;
        private float lineStrength;

        public Style Current { get; private set; }
        public bool BoostTailEmitting => boostTail != null && boostTail.emitting;
        public bool SwingTailEmitting => swingTail != null && swingTail.emitting;
        public bool SwingBoostTailEmitting => swingBoostTail != null && swingBoostTail.emitting;
        // The core and both shoulder tails, which go together.
        public bool LayerTailsEmitting => coreTail != null && coreTail.emitting && leftTail.emitting && rightTail.emitting;
        public static int WindLineCount => LineAround.Length;

        public bool WindLinesShown => WindLinesOn > 0;

        public int WindLinesOn
        {
            get
            {
                int on = 0;
                for (int i = 0; i < lines.Length; i++)
                    if (lines[i] != null && lines[i].enabled) on++;
                return on;
            }
        }

        // A booster outranks a boost pad: the pad's carry ends when a booster fires, and a swing hooked on a carry ends it.
        public static Style StyleOf(bool swinging, bool booster, bool boostZone)
        {
            if (swinging) return booster ? Style.SwingBooster : Style.Swing;
            if (booster) return Style.Booster;
            return boostZone ? Style.BoostZone : Style.None;
        }

        // material is ControlFeedback's, which owns it; the tint on each renderer gives these their colours.
        public SpeedTrails(Transform runner, Material material)
        {
            if (runner == null) throw new ArgumentNullException(nameof(runner));
            if (material == null) throw new ArgumentNullException(nameof(material));
            this.runner = runner;
            boostTail = MakeTail("Boost trail", material, FeedbackLook.BoostTailWidth, FeedbackLook.BoostTailTime);
            swingTail = MakeTail("Swing trail", material, FeedbackLook.SwingTailWidth, FeedbackLook.SwingTailTime);
            swingBoostTail = MakeTail("Swing boost trail", material, FeedbackLook.SwingBoostTailWidth, FeedbackLook.BoostTailTime);
            Color swingColor = FeedbackLook.SwingAmber;
            swingColor.a = 0.6f;
            SetTint(swingTail, swingColor, FeedbackLook.TailGlow);
            Color bothColor = FeedbackLook.SwingBoostGold;
            bothColor.a = 0.8f;
            SetTint(swingBoostTail, bothColor, FeedbackLook.TailGlow);
            coreTail = MakeTail("Boost trail core", material, FeedbackLook.CoreTailWidth, FeedbackLook.BoostTailTime);
            leftTail = MakeTail("Boost trail left", material, FeedbackLook.ShoulderTailWidth, FeedbackLook.BoostTailTime);
            rightTail = MakeTail("Boost trail right", material, FeedbackLook.ShoulderTailWidth, FeedbackLook.BoostTailTime);
            // Pointed at both ends, fullest a little behind the leading tip.
            var spindle = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(0.35f, 1f), new Keyframe(1f, 0f));
            for (int i = 0; i < lines.Length; i++) lines[i] = MakeLine(i, material, spindle);
        }

        private TrailRenderer MakeTail(string name, Material material, float width, float time)
        {
            var holder = new GameObject(name);
            holder.transform.SetParent(runner, false);
            holder.transform.localPosition = new Vector3(0f, -0.2f, 0f);
            var tail = holder.AddComponent<TrailRenderer>();
            tail.sharedMaterial = material;
            tail.shadowCastingMode = ShadowCastingMode.Off;
            tail.receiveShadows = false;
            tail.time = time;
            tail.minVertexDistance = 0.15f;
            tail.startWidth = width;
            tail.endWidth = 0f;
            tail.autodestruct = false;
            tail.emitting = false;
            tail.enabled = false;
            return tail;
        }

        private LineRenderer MakeLine(int index, Material material, AnimationCurve spindle)
        {
            var holder = new GameObject("Wind line " + index);
            holder.transform.SetParent(runner, false);
            var line = holder.AddComponent<LineRenderer>();
            line.sharedMaterial = material;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.useWorldSpace = true;
            line.positionCount = LinePoints;
            line.widthCurve = spindle;
            line.widthMultiplier = FeedbackLook.WindLineWidth;
            line.enabled = false;
            return line;
        }

        // Each frame of a run. padShare is how much of a boost pad's speed its carry still holds (FeedbackLook.PadShare);
        // forward is where the runner faces, since its root does not turn.
        // drafting: a draft's boost is running, which shoots the wind lines on its own, with no tail.
        public void Present(Style style, float padShare, Vector3 forward, float now, bool drafting = false)
        {
            Current = style;
            bool zone = style == Style.BoostZone;
            bool both = style == Style.SwingBooster;
            bool boost = zone || style == Style.Booster;
            float strength = zone ? padShare : 1f;
            bool windy = boost || both;
            boostTail.enabled = swingTail.enabled = swingBoostTail.enabled = true;
            coreTail.enabled = leftTail.enabled = rightTail.enabled = true;
            boostTail.emitting = boost;
            swingTail.emitting = style == Style.Swing;
            swingBoostTail.emitting = both;
            coreTail.emitting = leftTail.emitting = rightTail.emitting = windy;
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            Vector3 shoulder = runner.position + Vector3.up * FeedbackLook.ShoulderTailHeight;
            leftTail.transform.position = shoulder - right * FeedbackLook.ShoulderTailSide;
            rightTail.transform.position = shoulder + right * FeedbackLook.ShoulderTailSide;
            float tailTime = FeedbackLook.BoostTailTime * Mathf.Lerp(0.5f, 1f, strength);
            Color tailColor = both ? FeedbackLook.SwingBoostGold : zone ? FeedbackLook.BoostZoneGreen : NightUi.Accent;
            tailColor.a = Mathf.Lerp(0.25f, 0.6f, strength);
            if (boost)
            {
                boostTail.time = tailTime;
                SetTint(boostTail, tailColor, FeedbackLook.TailGlow);
            }
            if (windy)
            {
                coreTail.time = leftTail.time = rightTail.time = tailTime;
                Color core = Color.white;
                core.a = FeedbackLook.CoreTailAlpha * Mathf.Lerp(0.4f, 1f, strength);
                SetTint(coreTail, core, 1.5f);
                Color side = tailColor;
                side.a = Mathf.Min(1f, tailColor.a + 0.2f);
                SetTint(leftTail, side, FeedbackLook.TailGlow);
                SetTint(rightTail, side, FeedbackLook.TailGlow);
            }
            if (windy || drafting)
            {
                float blow = drafting ? 1f : strength;
                lineStrength = blow;
                // As a booster or a draft's boost starts every line shoots out at once, then one at a time at uneven gaps.
                if (((windy && !wasWindy) || (drafting && !wasDrafting)) && blow > WeakestLine) StartAll(now);
                else if (blow > WeakestLine && now >= nextLineAt) StartNext(now);
            }
            wasWindy = windy;
            wasDrafting = drafting;
            UpdateLines(forward, right, now);
        }

        // The first burst's lines start a little apart, so they do not all close at the same instant either.
        private void StartAll(float now)
        {
            for (int i = 0; i < lines.Length; i++)
                StartLine(i, now - Hash01(linesStarted, 4) * FeedbackLook.WindLineBurstSpread);
            nextLineAt = now + FeedbackLook.WindLineEvery;
        }

        private void StartNext(float now)
        {
            StartLine(LineOrder[nextLine], now);
            nextLine = (nextLine + 1) % LineOrder.Length;
            nextLineAt = now + FeedbackLook.WindLineEvery * Mathf.Lerp(0.5f, 1.5f, Hash01(linesStarted, 5));
        }

        private void StartLine(int index, float bornAt)
        {
            int n = linesStarted++;
            lineBornAt[index] = bornAt;
            lineAround[index] = (LineAround[index] + (Hash01(n, 0) * 2f - 1f) * FeedbackLook.WindLineJitter) * Mathf.Deg2Rad;
            lineFan[index] = Mathf.Lerp(FeedbackLook.WindLineFanMin, FeedbackLook.WindLineFanMax, Hash01(n, 1)) * Mathf.Deg2Rad;
            lineReach[index] = FeedbackLook.WindLineReach * Mathf.Lerp(FeedbackLook.WindLineReachMin, FeedbackLook.WindLineReachMax, Hash01(n, 2));
            lines[index].widthMultiplier = FeedbackLook.WindLineWidth * Mathf.Lerp(0.6f, 1.4f, Hash01(n, 3));
            lines[index].enabled = true;
        }

        // A fixed 0..1 value for the n-th line's salt-th choice, so the look is the same from one run to the next.
        private static float Hash01(int n, int salt)
        {
            unchecked
            {
                uint h = (uint)n * 0x9E3779B1u + (uint)salt * 0x85EBCA77u;
                h ^= h >> 15;
                h *= 0x2C1B3C6Du;
                h ^= h >> 12;
                h *= 0x297A2D39u;
                h ^= h >> 15;
                return (h & 0xFFFFFF) / (float)0x1000000;
            }
        }

        private void UpdateLines(Vector3 forward, Vector3 right, float now)
        {
            for (int i = 0; i < lines.Length; i++)
                DrawLine(i, (now - lineBornAt[i]) / FeedbackLook.WindLineTime, forward, right);
        }

        // The leading tip shoots out fast and slows; the trailing tip catches it up, so the line stretches, then closes to
        // nothing at its end. It runs along a fixed curve bending outward, so the line slides round it as it goes.
        private void DrawLine(int index, float age, Vector3 forward, Vector3 right)
        {
            LineRenderer line = lines[index];
            if (!line.enabled) return;
            if (age >= 1f)
            {
                line.enabled = false;
                return;
            }
            float around = lineAround[index];
            float fan = lineFan[index];
            Vector3 outward = right * Mathf.Cos(around) + Vector3.up * Mathf.Sin(around);
            Vector3 way = outward * Mathf.Sin(fan) - forward * Mathf.Cos(fan);
            // Square to way, leaning out: the side the line bends to.
            Vector3 aside = outward * Mathf.Cos(fan) + forward * Mathf.Sin(fan);
            Vector3 origin = runner.position + Vector3.up * FeedbackLook.WindLineHeight + forward * FeedbackLook.WindLineAhead
                + outward * FeedbackLook.WindLineRadius;
            float reach = lineReach[index];
            float bend = FeedbackLook.WindLineBend / reach;
            float lead = reach * (1f - (1f - age) * (1f - age));
            float trail = reach * age * age;
            for (int i = 0; i < LinePoints; i++)
            {
                float back = Mathf.Lerp(lead, trail, i / (float)(LinePoints - 1));
                points[i] = origin + way * back + aside * (bend * back * back);
            }
            line.SetPositions(points);
            Color color = Color.white;
            color.a = FeedbackLook.WindLineAlpha * lineStrength * (1f - age * age);
            SetTint(line, color, 1.5f);
        }

        // Out of a run, paused or on a respawn's jump: nothing shows, and no tail keeps points to join across the jump.
        public void Hide()
        {
            Current = Style.None;
            nextLineAt = 0f;
            wasWindy = wasDrafting = false;
            for (int i = 0; i < lines.Length; i++)
                if (lines[i] != null) lines[i].enabled = false;
            HideTail(boostTail);
            HideTail(swingTail);
            HideTail(swingBoostTail);
            HideTail(coreTail);
            HideTail(leftTail);
            HideTail(rightTail);
        }

        private static void HideTail(TrailRenderer tail)
        {
            if (tail == null) return;
            tail.emitting = false;
            if (tail.enabled) tail.Clear();
            tail.enabled = false;
        }

        // SetPropertyBlock copies the values, so one block serves every renderer here.
        private void SetTint(Renderer renderer, Color color, float glow)
        {
            tint.SetColor(BaseColorId, color);
            tint.SetColor(EmissionColorId, new Color(color.r * glow, color.g * glow, color.b * glow, 1f));
            renderer.SetPropertyBlock(tint);
        }

        public void Dispose()
        {
            if (boostTail != null) UnityEngine.Object.Destroy(boostTail.gameObject);
            if (swingTail != null) UnityEngine.Object.Destroy(swingTail.gameObject);
            if (swingBoostTail != null) UnityEngine.Object.Destroy(swingBoostTail.gameObject);
            if (coreTail != null) UnityEngine.Object.Destroy(coreTail.gameObject);
            if (leftTail != null) UnityEngine.Object.Destroy(leftTail.gameObject);
            if (rightTail != null) UnityEngine.Object.Destroy(rightTail.gameObject);
            for (int i = 0; i < lines.Length; i++)
                if (lines[i] != null) UnityEngine.Object.Destroy(lines[i].gameObject);
        }
    }
}

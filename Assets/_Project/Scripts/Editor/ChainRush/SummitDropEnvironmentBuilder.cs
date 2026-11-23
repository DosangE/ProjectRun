using System;
using System.Collections.Generic;
using System.IO;
using ProtoHarness.ChainRush.Track;
using ProtoHarness.ChainRush.Visuals;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using static ProtoHarness.Editor.ChainRush.ExteriorKit;

namespace ProtoHarness.Editor.ChainRush
{
    // Original downhill scenery: the ground follows the road from the summit to the village.
    // Like Palm Coast, only assets are saved; Create opens an unsaved decoration scene last.
    public static class SummitDropEnvironmentBuilder
    {
        public const string PrefabPath = "Assets/_Project/Prefabs/SummitDropExterior.prefab";
        public const string ScenePath = "Assets/_Project/Scenes/ChainRushTrackSummitDrop.unity";
        public const string LookPath = "Assets/_Project/Data/TrackLook_SummitDrop.asset";

        [MenuItem("ProtoHarness/Chain Rush/Create Summit Drop Exterior")]
        public static void CreateExterior()
        {
            RequireIdle();
            if (File.Exists(ScenePath)) throw new InvalidOperationException("Summit Drop scene already exists; use Rebuild Summit Drop Exterior.");
            // BuildAssets allows inspection and iteration before opening the one unsaved scene.
            if (!File.Exists(PrefabPath)) BuildAssets();
            ExteriorAssets.Require<ProtoHarness.ChainRush.Race.TrackLook>(LookPath);
            GameObject instance = ExteriorAssets.OpenDecorationScene(PrefabPath, "ChainRushTrackSummitDrop");
            Debug.Log($"Summit Drop exterior created. Decoration scene is UNSAVED: save as {ScenePath}; use {LookPath} for SUMMIT DROP.", instance);
        }

        [MenuItem("ProtoHarness/Chain Rush/Rebuild Summit Drop Exterior")]
        public static void RebuildExterior()
        {
            if (!File.Exists(PrefabPath)) throw new InvalidOperationException("Build Summit Drop assets first.");
            BuildAssets();
        }

        // Asset-only entry point: no scene is opened, modified or saved during iteration.
        public static void BuildAssets()
        {
            RequireIdle();
            var layout = new Layout(ExteriorAssets.RequireDefinition(ChainRushCircuitSceneBuilder.SummitDropPath));
            ExteriorAssets.SavePrefab(PrefabPath, layout.Build, layout.ValidateClearance);
            // Last, so a layout that fails its checks leaves the look as it was.
            BuildLook();
        }

        private static void RequireIdle()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play mode before building Summit Drop.");
            ExteriorAssets.RequireIdle();
        }

        [MenuItem("ProtoHarness/Chain Rush/Rebuild Summit Drop Look")]
        public static void BuildLook()
        {
            RequireIdle();
            ExteriorAssets.BuildLook(LookPath,
                ExteriorAssets.Surface("Summit", "Road", new Color(0.39f, 0.29f, 0.18f), ExteriorAssets.Grain.Asphalt, 2.8f, 0.02f),
                ExteriorAssets.Surface("Summit", "Wall", new Color(0.34f, 0.25f, 0.16f), ExteriorAssets.Grain.Concrete, 3f, 0.02f),
                Palette("Marking", new Color(0.49f, 0.40f, 0.27f)), Palette("Accent", new Color(0.67f, 0.51f, 0.24f)),
                new ExteriorAssets.Lanes(7.2f, 20f, false, false),
                ExteriorAssets.BuildSky("Summit", new Color(0.19f, 0.43f, 0.70f), new Color(0.73f, 0.80f, 0.82f),
                    new Color(0.93f, 0.94f, 0.92f), 0.23f, false, 230f, 850f));
        }

        private static Material Palette(string name, Color colour) => ExteriorAssets.Palette("Summit", name, colour);

        private sealed class Layout
        {
            private const float GroundStep = 2f;
            private const float PoleRadius = 3f;
            private readonly TrackDefinition definition;
            private readonly Centerline line;
            private readonly RoadCorridor corridor;
            private readonly List<TrackFrame> samples = new List<TrackFrame>();
            private readonly List<float> widths = new List<float>();
            private readonly List<Vector3> poles = new List<Vector3>();
            private readonly Dictionary<(int, int), float> heights = new Dictionary<(int, int), float>();
            private readonly List<(double Start, double End)> gaps;
            private readonly Material earth = Palette("Earth", new Color(0.31f, 0.30f, 0.20f));
            private readonly Material grass = Palette("Moss", new Color(0.28f, 0.35f, 0.22f));
            private readonly Material pineA = Palette("PineA", new Color(0.10f, 0.23f, 0.15f));
            private readonly Material pineB = Palette("PineB", new Color(0.16f, 0.29f, 0.18f));
            private readonly Material pineC = Palette("PineC", new Color(0.20f, 0.32f, 0.22f));
            private readonly Material rockA = Palette("RockA", new Color(0.39f, 0.40f, 0.37f));
            private readonly Material rockB = Palette("RockB", new Color(0.48f, 0.44f, 0.34f));
            private readonly Material rockC = Palette("RockC", new Color(0.31f, 0.34f, 0.33f));
            private readonly Material wood = Palette("Wood", new Color(0.33f, 0.23f, 0.14f));
            private readonly Material cutWood = Palette("CutWood", new Color(0.61f, 0.49f, 0.31f));
            private readonly Material ink = Palette("Ink", new Color(0.12f, 0.15f, 0.15f));
            private readonly Material cream = Palette("Cream", new Color(0.75f, 0.71f, 0.55f));
            private readonly Material amber = Palette("Amber", new Color(0.70f, 0.48f, 0.18f));
            private readonly Material chevron = Palette("Chevron", new Color(0.92f, 0.78f, 0.30f));
            private readonly Material water = Palette("Water", new Color(0.31f, 0.49f, 0.52f));
            private readonly Material foam = Palette("Foam", new Color(0.71f, 0.79f, 0.76f));
            private readonly Material roof = Palette("Roof", new Color(0.29f, 0.26f, 0.21f));
            private Mesh pineTier, pineTrunk;
            // The corner chevron board from the roadside kit (docs/ART_REQUEST_PROPS.md K1).
            private Mesh chevronPanel, chevronArrow;
            // The board's front (0.079) plus half the arrow's thickness (0.063).
            private const float ArrowOut = 0.142f;
            // The valley cabins' kit (docs/ART_REQUEST_PROPS.md K11), loaded when the build starts.
            private readonly Dictionary<string, Mesh> cabinKit = new Dictionary<string, Mesh>();
            private const string CabinKitName = "CabinKit";
            // Cabin sizes (K11): round logs 0.45 thick laid in nine courses from the footing's top at 0.8 m, the side walls
            // half a course higher; façade bays 2.4 m wide over the whole 4.05 m wall with 0.5 m of logs at each corner;
            // a 27 degree roof of 0.35 m panels reaching 0.5 m past the side walls; logs 0.3 m past the corners.
            private const float CabinLog = 0.45f, CabinFloor = 0.8f, CabinBay = 2.4f, CabinCorner = 0.5f, CabinLogOut = 0.3f;
            private const float CabinPitch = 27f, CabinRoof = 0.35f, CabinEave = 0.5f, CabinChimneyAboveRoof = 1.8f;
            private const int CabinCourses = 9;
            private const float CabinWall = CabinCourses * CabinLog;
            // The porch kit (measured, checked in LoadCabinKit): a 0.2 m step under a 0.2 m deck, the step 0.3 m deep at
            // the porch's front edge. The porch sits 1 cm up so its deck does not share the footing lip's top face.
            private const float PorchDeckOverFoot = 0.4f, PorchDeck = 0.2f, PorchStepDepth = 0.3f, PorchLift = 0.01f;
            private Mesh[] crags;
            // The roadside props and summit markers' kit (docs/ART_REQUEST_PROPS.md K12), loaded when the build starts.
            private const string RoadsideKitName = "RoadsideNatureKit";
            private Mesh[] bushes, tufts, stones;
            private Mesh logBark, logEnds, stumpBark, stumpTop, cairn, trailPost, trailBoard;
            // K12 ④: only the bushes carry UVs (1 = 1 m), so only they take a texture, one tile a metre.
            private readonly Material leaf = ExteriorAssets.Surface("Summit", "Leaf", new Color(0.16f, 0.29f, 0.18f), ExteriorAssets.Grain.Leaf, 1f, 0.05f);
            // K12 ①: Moss is the ground's own colour, so a tuft in it vanished; a drier yellow-green stands out.
            private readonly Material tuft = Palette("Grass", new Color(0.45f, 0.47f, 0.25f));
            private Vector3 min = Vector3.positiveInfinity, max = Vector3.negativeInfinity;
            private int terrainLeft, terrainRight, terrainBottom, terrainTop;
            private int trees, rocks, groundedRocks, skippedRocks;
            private float longestCragReach, lastCragReach;

            internal Layout(TrackDefinition definition)
            {
                this.definition = definition;
                line = definition.BuildCenterline(Vector3.zero, 0f);
                gaps = definition.Gaps();
                if (!definition.IsOpenCourse || gaps.Count != 1 || definition.Shortcuts.Count != 1)
                    throw new InvalidOperationException("Summit Drop layout changed: expected an open course, one gap and one shortcut.");
                corridor = new RoadCorridor(definition, line, 0.5d, CorridorKeepOut);
                line.GetPoles(poles);
                for (double s = -definition.StartRunway; s < definition.LapLength + definition.FinishRunout; s += 4d)
                    AddSample(line.FrameAt(s), definition.HalfWidthAt(Math.Max(0d, Math.Min(definition.LapLength, s))));
                AddSample(line.FrameAt(definition.LapLength + definition.FinishRunout), definition.HalfWidthAt(definition.LapLength));
                foreach (TrackDefinition.Shortcut shortcut in definition.Shortcuts)
                {
                    Centerline branch = TrackDefinition.ShortcutLine(line, shortcut);
                    for (double s = 0d; s <= shortcut.Length; s += 2d)
                    {
                        TrackFrame f = branch.FrameAt(s);
                        AddSample(new TrackFrame(shortcut.FromS + s / shortcut.Length * (shortcut.ToS - shortcut.FromS),
                            f.Position, f.Forward, f.Right, f.Grade, f.Curvature), shortcut.HalfWidth);
                    }
                }
                foreach (Vector3 pole in poles) corridor.Reserve(pole, PoleRadius + 3f);
            }

            private void AddSample(TrackFrame frame, float width)
            {
                samples.Add(frame);
                widths.Add(width);
                min = Vector3.Min(min, frame.Position);
                max = Vector3.Max(max, frame.Position);
            }

            internal GameObject Build(Scene scene)
            {
                var root = new GameObject("Summit Drop Exterior");
                SceneManager.MoveGameObjectToScene(root, scene);
                pineTier = KitMesh("TreeKit", "PineCrownTier"); pineTrunk = KitMesh("TreeKit", "TreeTrunk");
                chevronPanel = KitMesh("RoadsideKit", "ChevronPanel"); chevronArrow = KitMesh("RoadsideKit", "ChevronArrow");
                crags = new[] { KitMesh("RockKit", "CragA"), KitMesh("RockKit", "CragB"), KitMesh("RockKit", "CragC") };
                LoadCabinKit();
                LoadRoadsideKit();
                Terrain(Group("01 Descending mountain and valley", root.transform));
                MountainLedges(Group("09 Mountain overhangs below upper road", root.transform));
                // Reserve the four required corner landmarks before scattering optional scenery.
                Corners(Group("05 Zigzag rocks and chevrons", root.transform));
                RockWalls(Group("02 Summit and ledge rock walls", root.transform));
                Jump(Group("03 Ravine waterfall and broken bridge", root.transform));
                Fork(Group("04 Forest clearing shortcut", root.transform));
                Tunnel(Group("06 Rock tunnel", root.transform));
                Village(Group("07 Valley village and finish", root.transform));
                Transform forest = Group("08 Irregular pine forest", root.transform);
                Forest(forest);
                // In the same group, so it merges into the forest's meshes and materials (no new renderers or assets).
                Groves(forest);
                PoleSpires(Group("10 Rock spires under corner poles", root.transform));
                // Last, so every pine, crag, sign and cabin keeps its spot and the small props take what is left.
                Transform roadside = Group("11 Roadside props and summit markers", root.transform);
                SummitMarkers(roadside);
                Roadside(roadside);
                // Validate bounds before merging; afterwards they span the whole mountain. A spire's tall bounds swell
                // toward a road beside it, so 10 is left to the vertex check in ValidateClearance, with 01, 06 and 09.
                // So are 02 and 05 (05's zigzag boards with its crags): a tilted crag whose foot follows the slope has
                // bounds that swell over a road below it (2026-11-10).
                foreach (Transform group in root.transform)
                    if (!group.name.StartsWith("01", StringComparison.Ordinal) && !group.name.StartsWith("06", StringComparison.Ordinal)
                        && !group.name.StartsWith("09", StringComparison.Ordinal) && !group.name.StartsWith("10", StringComparison.Ordinal)
                        && !group.name.StartsWith("02", StringComparison.Ordinal) && !group.name.StartsWith("05", StringComparison.Ordinal))
                        corridor.ValidateClearance(group.gameObject, float.NegativeInfinity);
                var transient = new HashSet<Mesh>();
                foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
                    if (!EditorUtility.IsPersistent(filter.sharedMesh)) transient.Add(filter.sharedMesh);
                foreach (Transform group in root.transform) MergeByMaterial(group, "SummitDropExterior", group.name.Substring(0, 2));
                foreach (Mesh mesh in transient) UnityEngine.Object.DestroyImmediate(mesh);
                Debug.Log($"Summit Drop placed {trees} pines and {rocks} crags ({groundedRocks} with a foot reaching down the slope, longest {longestCragReach:F2} m; {skippedRocks} spots skipped at a drop); terrain follows main road, shortcut, runway and runout.", root);
                return root;
            }

            internal void ValidateClearance(GameObject root)
            {
                var groups = new List<Transform>();
                foreach (Transform child in root.transform) groups.Add(child);
                corridor.ValidateClearance(root, float.NegativeInfinity, null, groups, true);
                ValidatePoles(root);
                int renderers = root.GetComponentsInChildren<Renderer>(true).Length, vertices = 0;
                foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true)) vertices += filter.sharedMesh.vertexCount;
                if (renderers > 1500 || vertices > 2000000)
                    throw new InvalidOperationException($"Summit Drop budget exceeded: {renderers} renderers, {vertices} vertices.");
                Debug.Log($"Summit Drop validation PASS: main road and shortcut clear; {poles.Count} pole shafts clear; {renderers} renderers; {vertices} vertices; decoration physics=0.", root);
            }

            // Samples use a projected grade, not a constant summit height. The minimum constraint from
            // EVERY nearby road also protects the lower leg of switchbacks and the shortcut.
            private float Height(float x, float z)
            {
                float nearest = float.PositiveInfinity, ceiling = float.PositiveInfinity;
                float weightedHeight = 0f, totalWeight = 0f;
                int closest = 0;
                for (int i = 0; i < samples.Count; i++)
                {
                    TrackFrame f = samples[i];
                    float dx = x - f.Position.x, dz = z - f.Position.z, d2 = dx * dx + dz * dz;
                    if (d2 < nearest) { nearest = d2; closest = i; }
                    float localAlong = Mathf.Clamp(dx * f.Forward.x + dz * f.Forward.z, -2f, 2f);
                    float localS = (float)f.S + localAlong;
                    float localSide = dx * f.Right.x + dz * f.Right.z;
                    float localEdge = Mathf.Max(0f, Mathf.Sqrt(d2) - widths[i]);
                    bool cliffSide = localS < 420f || (localS >= 800f && localS < 1060f);
                    float localSlope = cliffSide ? (localSide < 0f ? -1.25f : 0.75f)
                        : (localS >= 1190f && localS < 1540f ? 0.025f : 0.16f);
                    float candidate = f.Position.y + f.Grade * localAlong - definition.RoadThickness - 0.18f
                        + Mathf.Clamp(Mathf.Max(0f, localEdge - 4f) * localSlope, -95f, 65f);
                    float weight = 1f / (d2 + 25f);
                    weight = weight * weight * weight;
                    weightedHeight += candidate * weight;
                    totalWeight += weight;
                    float reach = widths[i] + 6f;
                    if (d2 <= reach * reach)
                        ceiling = Mathf.Min(ceiling, f.Position.y + f.Grade * (dx * f.Forward.x + dz * f.Forward.z) - definition.RoadThickness - 0.18f);
                }
                TrackFrame at = samples[closest];
                Vector3 delta = new Vector3(x - at.Position.x, 0f, z - at.Position.z);
                float along = Vector3.Dot(delta, at.Forward);
                float s = (float)at.S + along;
                float edge = Mathf.Max(0f, Mathf.Sqrt(nearest) - widths[closest]);
                // Blending removes the height jumps at nearest-road Voronoi boundaries, while the
                // road ceiling below still guarantees support without ever raising land into a slab.
                float land = weightedHeight / totalWeight;
                float noise = (Mathf.PerlinNoise(x * 0.026f + 11f, z * 0.026f + 17f) - 0.5f) * Mathf.Min(edge * 0.35f, 14f);
                land += noise * Mathf.Clamp01((edge - 6f) / 14f);
                // Below the jump the ground opens into a deep, continuous ravine rather than a flat bridge support.
                foreach (var gap in gaps)
                {
                    float opening = Mathf.SmoothStep(0f, 1f, (s - (float)gap.Start + 6f) / 12f)
                        * Mathf.SmoothStep(0f, 1f, ((float)gap.End + 6f - s) / 12f);
                    land -= opening * 66f * Mathf.Clamp01(1f - edge / 140f);
                }
                // The distant skirts descend beneath the horizon fog, over 900 m beyond the course.
                land = Mathf.Lerp(land, -265f + noise, Mathf.SmoothStep(0f, 1f, (edge - 140f) / 480f));
                foreach (Vector3 pole in poles)
                    // An upper road's terrain can pass above a lower pole's entire shaft. Cutting
                    // that surface down to the lower pole excavates a false hole under the upper road.
                    if (land < pole.y + 23f && (new Vector2(x - pole.x, z - pole.z)).sqrMagnitude < 36f)
                        land = Mathf.Min(land, pole.y - 0.5f);
                return Mathf.Min(land, ceiling);
            }

            private float GridHeight(int x, int z)
            {
                var key = (x, z);
                if (!heights.TryGetValue(key, out float y))
                {
                    float px = x * GroundStep, pz = z * GroundStep;
                    // Match the coarse skirt's straight boundary exactly, avoiding T-junction cracks.
                    if ((px == terrainLeft || px == terrainRight) && z % 8 != 0)
                    {
                        float a = Mathf.Floor(pz / 16f) * 16f;
                        y = Mathf.Lerp(Height(px, a), Height(px, a + 16f), (pz - a) / 16f);
                    }
                    else if ((pz == terrainBottom || pz == terrainTop) && x % 8 != 0)
                    {
                        float a = Mathf.Floor(px / 16f) * 16f;
                        y = Mathf.Lerp(Height(a, pz), Height(a + 16f, pz), (px - a) / 16f);
                    }
                    else y = Height(px, pz);
                    heights[key] = y;
                }
                return y;
            }

            // Interpolate the actual terrain triangles so props sit on the saved mesh, even on steep slopes.
            // Outside the two-metre patch the skirts are 16 m faces (Terrain), split the same way, so a prop there follows them
            // (the groves reach that far, 2026-11-20; before them nothing stood outside the patch).
            private const int SkirtStep = 16, SkirtReach = 928;

            private float GroundAt(Vector3 point)
            {
                if (terrainRight <= terrainLeft) throw new InvalidOperationException("GroundAt before Terrain: the patch bounds are not set.");
                bool fine = point.x >= terrainLeft && point.x <= terrainRight && point.z >= terrainBottom && point.z <= terrainTop;
                float step = fine ? GroundStep : SkirtStep;
                int cells = fine ? 1 : (int)(SkirtStep / GroundStep);
                float x = point.x / step, z = point.z / step;
                int ix = Mathf.FloorToInt(x), iz = Mathf.FloorToInt(z);
                float u = x - ix, v = z - iz;
                float a = GridHeight(ix * cells, iz * cells), b = GridHeight((ix + 1) * cells, iz * cells);
                float c = GridHeight(ix * cells, (iz + 1) * cells), d = GridHeight((ix + 1) * cells, (iz + 1) * cells);
                return u + v <= 1f ? a + (b - a) * u + (c - a) * v : d + (c - d) * (1f - u) + (b - d) * (1f - v);
            }

            private void Terrain(Transform parent)
            {
                int left = Mathf.FloorToInt((min.x - 96f) / 32f) * 32, right = Mathf.CeilToInt((max.x + 96f) / 32f) * 32;
                int bottom = Mathf.FloorToInt((min.z - 96f) / 32f) * 32, top = Mathf.CeilToInt((max.z + 96f) / 32f) * 32;
                terrainLeft = left; terrainRight = right; terrainBottom = bottom; terrainTop = top;
                // Two-metre faces close to the road; outer skirts are outside every road's footprint.
                Patch(parent, "Mountain", left, right, bottom, top, 2, grass);
                Patch(parent, "West skirt", left - SkirtReach, left, bottom - SkirtReach, top + SkirtReach, SkirtStep, earth);
                Patch(parent, "East skirt", right, right + SkirtReach, bottom - SkirtReach, top + SkirtReach, SkirtStep, earth);
                Patch(parent, "South skirt", left, right, bottom - SkirtReach, bottom, SkirtStep, earth);
                Patch(parent, "North skirt", left, right, top, top + SkirtReach, SkirtStep, earth);
            }

            private void Patch(Transform parent, string name, int x0, int x1, int z0, int z1, int step, Material material)
            {
                int nx = (x1 - x0) / step + 1, nz = (z1 - z0) / step + 1;
                var points = new List<Vector3>(nx * nz);
                var triangles = new List<int>((nx - 1) * (nz - 1) * 6);
                var cliffs = new List<int>();
                for (int z = z0; z <= z1; z += step)
                    for (int x = x0; x <= x1; x += step) points.Add(new Vector3(x, GridHeight(x / 2, z / 2), z));
                for (int z = 0; z < nz - 1; z++)
                    for (int x = 0; x < nx - 1; x++)
                    {
                        int a = z * nx + x, b = a + 1, c = a + nx, d = c + 1;
                        Face(a, c, b);
                        Face(b, c, d);
                    }
                TerrainPart(name, parent, points, triangles, material);
                if (cliffs.Count != 0) TerrainPart(name + " exposed rock", parent, points, cliffs, rockA);

                void Face(int a, int b, int c)
                {
                    Vector3 normal = Vector3.Cross(points[b] - points[a], points[c] - points[a]).normalized;
                    List<int> face = normal.y < 0.64f ? cliffs : triangles;
                    face.Add(a); face.Add(b); face.Add(c);
                }
            }

            private static void TerrainPart(string name, Transform parent, List<Vector3> vertices, List<int> triangles, Material material)
            {
                if (triangles.Count == 0) return;
                var used = new bool[vertices.Count];
                foreach (int index in triangles) used[index] = true;
                var remap = new int[vertices.Count];
                var compact = new List<Vector3>();
                for (int i = 0; i < used.Length; i++)
                    if (used[i]) { remap[i] = compact.Count; compact.Add(vertices[i]); }
                for (int i = 0; i < triangles.Count; i++) triangles[i] = remap[triangles[i]];
                MeshObject(name, parent, compact, triangles, material);
            }

            // How far below the ground a placed thing's base sits.
            private const float GroundSink = 0.45f;

            // reserve false leaves the spot free for the caller to Reserve once it has really placed something there:
            // a crag turned down at a drop must not block the next candidate 12 m on (Codex review 2026-11-10).
            private bool Place(double s, int side, float offset, float radius, float height, out Vector3 p, bool reserve = true)
            {
                TrackFrame f = line.FrameAt(s);
                p = f.Position + f.Right * side * (definition.HalfWidthAt(s) + offset);
                p.y = GroundAt(p) - GroundSink;
                if (!corridor.Available(p, radius * 1.45f)) return false;
                foreach (Vector3 pole in poles)
                    if (Vector2.Distance(new Vector2(p.x, p.z), new Vector2(pole.x, pole.z)) < radius + PoleRadius + 4f
                        && p.y + height > pole.y && p.y < pole.y + 21f) return false;
                if (reserve) corridor.Reserve(p, radius * 1.15f);
                return true;
            }

            // The upper road and the lower zigzag overlap in plan by more than 100 m in height.
            // A heightfield alone must follow the lower surface. Rock overhangs join the upper road's
            // shoulders to the mountain without filling the lower road or a lower pole's airspace.
            private void MountainLedges(Transform parent)
            {
                var tops = new List<Vector3>();
                var topTriangles = new List<int>();
                var stone = new List<Vector3>();
                var stoneTriangles = new List<int>();
                int ledges = 0;
                for (double s = -definition.StartRunway; s < line.EndS; s += 1d)
                {
                    double end = Math.Min(s + 1d, line.EndS);
                    bool gap = false;
                    foreach (var opening in gaps)
                        if (s < opening.End + 0.5d && end > opening.Start - 0.5d) gap = true;
                    if (gap) continue;
                    TrackFrame a = line.FrameAt(s), b = line.FrameAt(end);
                    float aw = definition.HalfWidthAt(Math.Max(0d, Math.Min(s, definition.LapLength)));
                    float bw = definition.HalfWidthAt(Math.Max(0d, Math.Min(end, definition.LapLength)));
                    bool needed = false;
                    for (int side = -1; side <= 1; side++)
                    {
                        needed |= a.Position.y - definition.RoadThickness - GroundAt(a.Position + a.Right * side * (aw - 0.1f)) > 0.4f;
                        needed |= b.Position.y - definition.RoadThickness - GroundAt(b.Position + b.Right * side * (bw - 0.1f)) > 0.4f;
                    }
                    if (!needed) continue;
                    ledges++;
                    float half = Mathf.Max(aw, bw) + 4f;
                    int across = Mathf.CeilToInt(half * 2f);
                    float ay = a.Position.y - definition.RoadThickness - 0.1f, by = b.Position.y - definition.RoadThickness - 0.1f;
                    for (int x = 0; x < across; x++)
                    {
                        float left = -half + half * 2f * x / across, right = -half + half * 2f * (x + 1) / across;
                        Vector3 al = a.Position + a.Right * left, ar = a.Position + a.Right * right;
                        Vector3 bl = b.Position + b.Right * left, br = b.Position + b.Right * right;
                        al.y = ar.y = ay; bl.y = br.y = by;
                        Face(al, bl, ar, br, tops, topTriangles, false);
                        Face(al - Vector3.up * 6f, bl - Vector3.up * 6f, ar - Vector3.up * 6f, br - Vector3.up * 6f, stone, stoneTriangles, true);
                        for (int tier = 0; tier < 3; tier++)
                        {
                            Vector3 drop = Vector3.up * (tier * 2f), next = drop + Vector3.up * 2f;
                            Face(al - drop, ar - drop, al - next, ar - next, stone, stoneTriangles, false);
                            Face(bl - drop, br - drop, bl - next, br - next, stone, stoneTriangles, true);
                            if (x == 0) Face(al - drop, bl - drop, al - next, bl - next, stone, stoneTriangles, true);
                            if (x == across - 1) Face(ar - drop, br - drop, ar - next, br - next, stone, stoneTriangles, false);
                        }
                    }
                }
                if (tops.Count != 0)
                {
                    MeshObject("Mountain ledge shoulders", parent, tops, topTriangles, grass);
                    MeshObject("Mountain ledge strata", parent, stone, stoneTriangles, rockC);
                }
                Debug.Log($"Summit Drop mountain overhangs: {ledges} one-metre road sections supported; jump left open.");

                void Face(Vector3 a, Vector3 b, Vector3 c, Vector3 d, List<Vector3> v, List<int> t, bool reverse)
                {
                    int first = v.Count;
                    v.Add(a); v.Add(b); v.Add(c); v.Add(d);
                    t.Add(first); t.Add(first + (reverse ? 2 : 1)); t.Add(first + (reverse ? 1 : 2));
                    t.Add(first + 2); t.Add(first + (reverse ? 3 : 1)); t.Add(first + (reverse ? 1 : 3));
                }
            }

            private void Forest(Transform parent)
            {
                int index = 1;
                for (double s = 8d; s < definition.LapLength + 35d; s += 7d)
                    for (int side = -1; side <= 1; side += 2)
                    {
                        if (side < 0 && s < 420d || side < 0 && s >= 800d && s < 1190d || s >= 1840d && s < 1920d) continue;
                        bool dense = s >= 420d && s < 800d;
                        int rows = dense ? 4 : 2;
                        for (int row = 0; row < rows; row++)
                        {
                            int seed = index++;
                            if (!dense && Hash(seed * 13) < (s < 420d ? 0.65f : 0.35f)) continue;
                            double at = s + (Hash(seed * 17) - 0.5f) * 6d;
                            float radius = 2.3f + Hash(seed * 19) * 1.6f;
                            float height = (dense ? 16f : 11f) + Hash(seed * 23) * 9f;
                            if (!Place(at, side, 5f + radius * 1.5f + row * 11f + Hash(seed * 29) * 7f, radius, height, out Vector3 p)) continue;
                            Pine(parent, seed, p, radius, height);
                            trees++;
                        }
                    }
            }

            private void Pine(Transform parent, int seed, Vector3 p, float radius, float height)
            {
                Transform tree = Group("Pine " + seed, parent);
                tree.SetPositionAndRotation(p, Quaternion.Euler(0f, Hash(seed * 31) * 360f, 0f));
                Shape("Trunk", tree, Vector3.up * height * 0.35f, new Vector3(0.6f, height * 0.7f, 0.6f), wood, pineTrunk);
                Material needles = Pick(seed * 37, pineA, pineB, pineC);
                for (int tier = 0; tier < 3; tier++)
                    Shape("Needle crown", tree, Vector3.up * height * (0.36f + tier * 0.20f),
                        new Vector3(radius * 2f * (1f - tier * 0.23f), height * 0.46f, radius * 2f * (1f - tier * 0.23f)), needles, pineTier);
            }

            // The two stretches with a drop on the left (Height's cliffSide, user 2026-11-20 "옆에 낭떠러지인 곳이 너무
            // 공허해보인다"): Forest leaves their left side bare and plants only within about 50 m of a road, so the valley
            // floor seen from them was empty. Pine clumps 40-250 m out, where Perlin noise is high, so they read as groves.
            private static readonly (double From, double To)[] DropStretches = { (0d, 420d), (800d, 1190d) };
            // First pass (6 tries, threshold 0.5, 10-25 m pines) read as a thin scatter from the road, not as groves: one try
            // per ~100 m² left pines ~18 m apart inside a clump. More tries over smaller clumps keep the count, packed closer.
            private const float GroveNear = 40f, GroveFar = 250f, GroveStep = 5f, GroveNoise = 0.012f, GroveThreshold = 0.63f;
            private const int GroveTries = 24;
            // The terrain's cliff test (Patch): a face steeper than this is painted rock, so no tree stands on it.
            private const float GroveSteepest = 0.64f;

            private void Groves(Transform parent)
            {
                int planted = 0, tried = 0, sparse = 0, steep = 0, outside = 0, taken = 0;
                int seed = 3000;
                foreach (var stretch in DropStretches)
                    for (double s = stretch.From; s < stretch.To; s += GroveStep)
                        for (int k = 0; k < GroveTries; k++, seed++)
                        {
                            tried++;
                            double at = s + Hash(seed * 7) * GroveStep;
                            float offset = Mathf.Lerp(GroveNear, GroveFar, Hash(seed * 11));
                            TrackFrame f = line.FrameAt(at);
                            Vector3 spot = f.Position - f.Right * (definition.HalfWidthAt(at) + offset);
                            // The skirts reach SkirtReach past the patch (Terrain); keep a face's width inside them.
                            if (spot.x < terrainLeft - SkirtReach + SkirtStep || spot.x > terrainRight + SkirtReach - SkirtStep
                                || spot.z < terrainBottom - SkirtReach + SkirtStep || spot.z > terrainTop + SkirtReach - SkirtStep) { outside++; continue; }
                            if (Mathf.PerlinNoise(spot.x * GroveNoise + 41f, spot.z * GroveNoise + 59f) < GroveThreshold) { sparse++; continue; }
                            if (GroundNormalY(spot) < GroveSteepest) { steep++; continue; }
                            float radius = 2.4f + Hash(seed * 19) * 2.2f;
                            float height = 12f + Hash(seed * 23) * 16f;
                            // Place's pole test only looks at a pole's own heights; a spire (10) reaches from it down to the ground.
                            bool bySpire = false;
                            foreach (Vector3 pole in poles)
                                if (Vector2.Distance(new Vector2(spot.x, spot.z), new Vector2(pole.x, pole.z)) < radius + 6f) bySpire = true;
                            if (bySpire || !Place(at, -1, offset, radius, height, out Vector3 p)) { taken++; continue; }
                            Pine(parent, seed, p, radius, height);
                            planted++;
                        }
                trees += planted;
                Debug.Log($"Summit Drop groves below the drops: {planted} pines from {tried} tries ({sparse} outside a clump, {steep} on rock faces, {outside} beyond the skirts, {taken} by a road, pole or tree).", parent);
            }

            private float GroundNormalY(Vector3 p) => GroundNormal(p).y;

            private Vector3 GroundNormal(Vector3 p)
            {
                float dx = GroundAt(p + Vector3.right * GroundStep) - GroundAt(p - Vector3.right * GroundStep);
                float dz = GroundAt(p + Vector3.forward * GroundStep) - GroundAt(p - Vector3.forward * GroundStep);
                return new Vector3(-dx, GroundStep * 2f, -dz).normalized;
            }

            private void LoadRoadsideKit()
            {
                bushes = new[] { KitMesh(RoadsideKitName, "BushA"), KitMesh(RoadsideKitName, "BushB") };
                tufts = new[] { KitMesh(RoadsideKitName, "GrassTuftA"), KitMesh(RoadsideKitName, "GrassTuftB") };
                stones = new[] { KitMesh(RoadsideKitName, "StoneA"), KitMesh(RoadsideKitName, "StoneB"), KitMesh(RoadsideKitName, "StoneC") };
                logBark = KitMesh(RoadsideKitName, "FallenLog_Bark");
                stumpBark = KitMesh(RoadsideKitName, "Stump_Bark");
                cairn = KitMesh(RoadsideKitName, "Cairn");
                trailPost = KitMesh(RoadsideKitName, "TrailPost");
                trailBoard = KitMesh(RoadsideKitName, "TrailBoard");
                // A log's ends and a stump's cut top share the bark's origin (K12: the two pieces' joint bounds centre), so
                // they are placed by origin with the bark; one outside the bark's bounds was exported from another origin.
                logEnds = KitPiece(RoadsideKitName, "FallenLog_Ends");
                stumpTop = KitPiece(RoadsideKitName, "Stump_Top");
                foreach ((Mesh piece, Mesh bark) in new[] { (logEnds, logBark), (stumpTop, stumpBark) })
                {
                    Bounds outer = bark.bounds;
                    outer.Expand(0.02f);
                    if (!outer.Contains(piece.bounds.min) || !outer.Contains(piece.bounds.max))
                        throw new InvalidOperationException($"Kit mesh {piece.name} in {RoadsideKitName}.fbx lies outside {bark.name} ({piece.bounds} in {bark.bounds}); export the two pieces from their joint bounds centre.");
                }
            }

            // Small props along the whole road (user 2026-11-20 ① "길가 전 구간의 작은 소품", docs/ART_REQUEST_PROPS.md K12):
            // clumps of one to three every RoadsideStep on both sides, from just past the road's keep-out to RoadsideSpread
            // beyond it. Not in the tunnel, over the gap or at the finish gate; nowhere the ground drops away (PlaceSmall).
            private const float RoadsideStep = 7f, RoadsideSpread = 5f;
            private const double RoadsideFrom = 10d, TunnelFrom = 1840d, TunnelTo = 1920d, GapMargin = 12d, FinishMargin = 10d;

            private void Roadside(Transform parent)
            {
                var placed = new int[5];
                int tried = 0, taken = 0, seed = 5000;
                double gapStart = gaps[0].Start, gapEnd = gaps[0].End;
                for (double s = RoadsideFrom; s < definition.LapLength + definition.FinishRunout - 5d; s += RoadsideStep)
                {
                    if (s >= TunnelFrom && s < TunnelTo || s > gapStart - GapMargin && s < gapEnd + GapMargin
                        || Math.Abs(s - definition.LapLength) < FinishMargin) continue;
                    for (int side = -1; side <= 1; side += 2)
                    {
                        int clump = 1 + Mathf.Min(2, (int)(Hash(seed * 3) * 3f));
                        for (int k = 0; k < clump; k++, seed++)
                        {
                            tried++;
                            double at = s + (Hash(seed * 5) - 0.5f) * RoadsideStep;
                            float pick = Hash(seed * 11);
                            int kind = pick < 0.35f ? 0 : pick < 0.60f ? 1 : pick < 0.85f ? 2 : pick < 0.93f ? 3 : 4;
                            if (RoadsideProp(parent, kind, at, side, seed)) placed[kind]++;
                            else taken++;
                        }
                    }
                }
                Debug.Log($"Summit Drop roadside: {placed[0]} grass tufts, {placed[1]} stones, {placed[2]} bushes, {placed[3]} stumps, {placed[4]} fallen logs from {tried} tries ({taken} by a road, pole, drop, steep ground or something already there).", parent);
            }

            // K12 ③: a prop's base goes this far under the ground (the bushes' open bottom must not show), plus its
            // footprint's half-width times the ground's slope, so its downhill edge does not float either.
            private static readonly float[] RoadsideSink = { 0.06f, 0.08f, 0.12f, 0.06f, 0.10f };

            private bool RoadsideProp(Transform parent, int kind, double at, int side, int seed)
            {
                Mesh mesh = kind switch
                {
                    0 => tufts[Mathf.Min(tufts.Length - 1, (int)(Hash(seed * 13) * tufts.Length))],
                    1 => stones[Mathf.Min(stones.Length - 1, (int)(Hash(seed * 13) * stones.Length))],
                    2 => bushes[Mathf.Min(bushes.Length - 1, (int)(Hash(seed * 13) * bushes.Length))],
                    3 => stumpBark,
                    _ => logBark
                };
                // K12: the kit is at real size; a copy varies by 0.7-1.3.
                float scale = 0.7f + Hash(seed * 17) * 0.6f;
                Vector3 size = mesh.bounds.size * scale;
                float radius = Mathf.Max(size.x, size.z) * 0.5f;
                float offset = CorridorKeepOut + radius * 1.45f + 0.2f + Hash(seed * 19) * RoadsideSpread;
                if (!PlaceSmall(at, side, offset, radius, out Vector3 p, out Vector3 normal)) return false;
                // K12 ②: a log lies along the slope's level line, so both ends touch; across the slope only its thickness
                // rises. On level ground it lies any way.
                Vector3 level = Vector3.Cross(normal, Vector3.up);
                Quaternion turn = kind == 4 && level.sqrMagnitude > 1e-4f
                    ? Quaternion.LookRotation(Vector3.Cross(level.normalized, Vector3.up)) * Quaternion.Euler(0f, Hash(seed * 23) < 0.5f ? 0f : 180f, 0f)
                    : Quaternion.Euler(0f, Hash(seed * 23) * 360f, 0f);
                // Stones settle half-way toward the slope; plants and wood stand upright.
                if (kind == 1) turn = Quaternion.FromToRotation(Vector3.up, Vector3.Slerp(Vector3.up, normal, 0.5f)) * turn;
                float foot = kind == 4 ? size.y * 0.5f : radius;
                float slope = Mathf.Sqrt(Mathf.Max(0f, 1f - normal.y * normal.y)) / normal.y;
                float sink = Mathf.Min(RoadsideSink[kind] * scale + foot * slope, size.y * 0.45f);
                Transform prop = Group(RoadsideNames[kind] + " " + seed, parent);
                prop.SetPositionAndRotation(p + Vector3.up * (size.y * 0.5f - sink), turn);
                prop.localScale = Vector3.one * scale;
                switch (kind)
                {
                    case 0: Piece("Tuft", prop, Vector3.zero, tuft, mesh); break;
                    case 1: Piece("Stone", prop, Vector3.zero, Pick(seed * 29, rockA, rockB, rockC), mesh); break;
                    case 2: Piece("Bush", prop, Vector3.zero, leaf, mesh); break;
                    case 3: Piece("Bark", prop, Vector3.zero, wood, stumpBark); Piece("Cut top", prop, Vector3.zero, cutWood, stumpTop); break;
                    default: Piece("Bark", prop, Vector3.zero, wood, logBark); Piece("Cut ends", prop, Vector3.zero, cutWood, logEnds); break;
                }
                return true;
            }

            private static readonly string[] RoadsideNames = { "Grass tuft", "Stone", "Bush", "Stump", "Fallen log" };
            // The corridor's keep-out past a road's edge.
            private const float CorridorKeepOut = 1.8f;
            // Ground this far under the road's slab is a drop or under a ledge's shoulder (MountainLedges): a prop there
            // would be out of sight. Steeper than RoadsideSteepest (about 45 degrees) a prop would cling to a face.
            private const float RoadsideBelowSlab = 0.6f, RoadsideSteepest = 0.7f;

            // Where a small prop of footprint radius stands at (s, side, offset): clear of every road, reserved spot and
            // pole (with the spire under it, 10), on ground neither dropping away nor too steep. p is on the ground there;
            // the spot is reserved.
            private bool PlaceSmall(double s, int side, float offset, float radius, out Vector3 p, out Vector3 normal)
                => SmallSpot(s, side, offset, radius, true, out p, out normal) == null;

            // PlaceSmall, saying why a spot was turned down (null when it is free); reserve false only looks.
            private string SmallSpot(double s, int side, float offset, float radius, bool reserve, out Vector3 p, out Vector3 normal)
            {
                TrackFrame f = line.FrameAt(s);
                p = f.Position + f.Right * side * (definition.HalfWidthAt(s) + offset);
                normal = Vector3.up;
                if (!corridor.Clear(p, radius * 1.45f)) return "by a road";
                if (!corridor.Available(p, radius * 1.45f)) return "taken";
                foreach (Vector3 pole in poles)
                    if (Vector2.Distance(new Vector2(p.x, p.z), new Vector2(pole.x, pole.z)) < radius + PoleRadius + 6f) return "by a pole";
                float ground = GroundAt(p);
                float below = f.Position.y - definition.RoadThickness - ground;
                if (below > RoadsideBelowSlab) return "drop";
                normal = GroundNormal(p);
                if (normal.y < RoadsideSteepest) return "steep";
                p.y = ground;
                if (reserve) corridor.Reserve(p, radius * 1.15f);
                return null;
            }

            // The summit start's markers (user 2026-11-20 ② "정상 출발 구간", K12): cairns and trail signs over the first
            // 420 m. Fixed S failed there: S 110-190 drops away on the right too, or is a ledge over the lower zigzag
            // (2026-11-21, ground 26-102 m under the slab). So the stretch is scanned every MarkerScan on both sides for
            // free spots, and the markers spread over them evenly, at least MarkerSpacing apart. Each is a landmark: fewer
            // free spots than markers fails the build.
            private const double MarkerFrom = 4d, MarkerTo = 420d, MarkerScan = 4d, MarkerSpacing = 60d;
            private static readonly float[] MarkerOffsets = { 0.4f, 1f, 2f, 3f };
            // Cairn, sign, cairn, sign, cairn, down the road.
            private static readonly bool[] MarkerIsSign = { false, true, false, true, false };
            // K12 ⑥ (the mock-up's assembly): two boards on the post's road face, 0.3 and 0.75 m under its top, one
            // pointing on down the road and one back; a board's tip is its -x end.
            private const float BoardBelowTop1 = 0.3f, BoardBelowTop2 = 0.75f, BoardReach = 0.4f, PostSink = 0.3f;

            private void SummitMarkers(Transform parent)
            {
                // The sign's footprint is the larger, so a spot free for it is free for a cairn.
                float signRadius = trailBoard.bounds.size.x * 0.5f + BoardReach;
                Vector3 heap = cairn.bounds.size;
                float cairnRadius = Mathf.Max(heap.x, heap.z) * 0.5f;
                if (cairnRadius > signRadius) throw new InvalidOperationException("The cairn's footprint outgrew the trail sign's; scan with the larger.");
                var spots = new List<(double S, int Side, float Offset)>();
                var refused = new Dictionary<string, int>();
                for (double s = MarkerFrom; s <= MarkerTo; s += MarkerScan)
                    for (int side = 1; side >= -1; side -= 2)
                    {
                        string why = null;
                        foreach (float extra in MarkerOffsets)
                        {
                            float offset = CorridorKeepOut + signRadius * 1.45f + extra;
                            why = SmallSpot(s, side, offset, signRadius, false, out _, out _);
                            if (why != null) continue;
                            spots.Add((s, side, offset));
                            break;
                        }
                        if (why != null) refused[why] = refused.TryGetValue(why, out int n) ? n + 1 : 1;
                    }
                var chosen = new List<(double S, int Side, float Offset)>();
                if (spots.Count != 0)
                {
                    double first = spots[0].S, last = spots[spots.Count - 1].S;
                    for (int i = 0; i < MarkerIsSign.Length; i++)
                    {
                        double target = first + (last - first) * i / (MarkerIsSign.Length - 1);
                        (double S, int Side, float Offset)? best = null;
                        foreach (var spot in spots)
                        {
                            bool apart = true;
                            foreach (var taken in chosen) apart &= Math.Abs(taken.S - spot.S) >= MarkerSpacing;
                            if (apart && (best == null || Math.Abs(spot.S - target) < Math.Abs(best.Value.S - target))) best = spot;
                        }
                        if (best != null) chosen.Add(best.Value);
                    }
                }
                var reasons = new List<string>();
                foreach (KeyValuePair<string, int> pair in refused) reasons.Add(pair.Value + " " + pair.Key);
                string scan = $"{spots.Count} free of {(int)((MarkerTo - MarkerFrom) / MarkerScan + 1) * 2} spots scanned (refused: {string.Join(", ", reasons)})";
                if (chosen.Count < MarkerIsSign.Length)
                    throw new InvalidOperationException($"Summit markers: only {chosen.Count} of {MarkerIsSign.Length} placeable {MarkerSpacing} m apart over S{MarkerFrom}-{MarkerTo}; {scan}.");
                chosen.Sort((a, b) => a.S.CompareTo(b.S));
                var placed = new List<string>();
                for (int i = 0; i < chosen.Count; i++)
                {
                    (double s, int side, float offset) = chosen[i];
                    bool sign = MarkerIsSign[i];
                    if (!PlaceSmall(s, side, offset, sign ? signRadius : cairnRadius, out Vector3 p, out Vector3 normal))
                        throw new InvalidOperationException($"Summit marker {i} lost its scanned spot at S{s:F0}.");
                    if (sign) TrailSign(parent, i, s, side, p);
                    else Cairn(parent, i, p, normal);
                    placed.Add($"{(sign ? "sign" : "cairn")} S{s:F0}{(side > 0 ? "R" : "L")}");
                }
                Debug.Log($"Summit Drop summit markers ({RoadsideKitName}): {string.Join(", ", placed)}; {scan}.", parent);
            }

            private void Cairn(Transform parent, int index, Vector3 p, Vector3 normal)
            {
                Vector3 size = cairn.bounds.size;
                float slope = Mathf.Sqrt(Mathf.Max(0f, 1f - normal.y * normal.y)) / normal.y;
                float sink = Mathf.Min(0.12f + size.x * 0.5f * slope, size.y * 0.3f);
                Transform heap = Group("Summit cairn " + index, parent);
                heap.SetPositionAndRotation(p + Vector3.up * (size.y * 0.5f - sink), Quaternion.Euler(0f, Hash(index * 61 + 7) * 360f, 0f));
                Piece("Cairn", heap, Vector3.zero, rockA, cairn);
            }

            private void TrailSign(Transform parent, int index, double s, int side, Vector3 p)
            {
                TrackFrame f = line.FrameAt(s);
                Transform sign = Group("Summit trail sign " + index, parent);
                // Local +z faces the road; down the road is +x on the right side, -x on the left.
                sign.SetPositionAndRotation(p, Flat(-f.Right * side));
                float post = trailPost.bounds.size.y, half = trailPost.bounds.size.z * 0.5f + trailBoard.bounds.size.z * 0.5f;
                Piece("Post", sign, Vector3.up * (post * 0.5f - PostSink), wood, trailPost);
                float top = post - PostSink;
                // The tip is -x; a board turned about its normal (z) points +x (K12: the board reads the same upside down).
                Quaternion pointPlusX = Quaternion.Euler(0f, 0f, 180f);
                Piece("Board on", sign, new Vector3(BoardReach * side, top - BoardBelowTop1, half), cutWood, trailBoard)
                    .transform.localRotation = side > 0 ? pointPlusX : Quaternion.identity;
                Piece("Board back", sign, new Vector3(-BoardReach * side, top - BoardBelowTop2, half), cutWood, trailBoard)
                    .transform.localRotation = side > 0 ? Quaternion.identity : pointPlusX;
            }


            private void RockWalls(Transform parent)
            {
                int index = 0;
                for (double s = 12d; s < 1060d; s += 10d)
                {
                    bool ledge = s >= 800d;
                    if (s >= 420d && !ledge && Hash(index + 67) < 0.55f) { index++; continue; }
                    float radius = ledge ? 6f : 5f + Hash(index * 7) * 3f;
                    // A wall crag at a drop is left out (Rock returns false); the wall has more along it.
                    if (Place(s, 1, radius * 1.6f + 3f, radius, 24f, out Vector3 p, false))
                        Rock(parent, p, radius, ledge ? 25f + Hash(index * 11) * 8f : 12f + Hash(index * 11) * 14f, index);
                    index++;
                }
            }

            // Place reads the ground under the centre only, and a crag's flat bottom 10-16 m wide on a mountain slope
            // floated 3-12 m at its downhill side, up to 33 m at a drop (playtest 2026-11-10, docs/ART_REQUEST_PROPS.md
            // K4). Moving or stretching the whole rock buries its uphill side or reaches into the road below, so each crag
            // gets its own copy of the kit mesh whose lowest part follows the slope: a vertex in the bottom CragFoot of the
            // height comes down by how far the bottom floats over the ground under it, fully at the bottom and less going
            // up, so the top keeps its shape and the foot spreads down the slope. Where the ground falls away more than
            // CragReach of the height it is a drop, not a slope: no crag there (false).
            // A placed crag reserves its spot here, not in Place, so a spot turned down at a drop stays free.
            private const float CragFoot = 0.4f, CragReach = 0.6f;

            private bool Rock(Transform parent, Vector3 p, float radius, float height, int seed, bool evenAtADrop = false)
            {
                var size = new Vector3(radius * 2f, height, radius * 1.7f);
                Quaternion tilt = Quaternion.Euler(Hash(seed * 43) * 9f, Hash(seed * 47) * 360f, Hash(seed * 53) * 8f);
                Vector3 centre = p + Vector3.up * height * 0.42f;
                Mesh source = crags[Mathf.Min(crags.Length - 1, (int)(Hash(seed * 59) * crags.Length))];
                Bounds b = source.bounds;
                var scale = new Vector3(size.x / b.size.x, size.y / b.size.y, size.z / b.size.z);
                var vertices = new List<Vector3>();
                source.GetVertices(vertices);
                float reach = 0f;
                for (int i = 0; i < vertices.Count; i++)
                {
                    Vector3 local = Vector3.Scale(vertices[i] - b.center, scale);
                    Vector3 world = centre + tilt * local;
                    float up = (local.y + size.y * 0.5f) / size.y;
                    if (up < CragFoot)
                    {
                        float bottom = world.y - up * size.y;
                        float drop = Mathf.Max(0f, bottom - (GroundAt(world) - GroundSink));
                        reach = Mathf.Max(reach, drop);
                        world.y -= drop * (1f - up / CragFoot);
                    }
                    vertices[i] = world - centre;
                }
                if (reach > height * CragReach && !evenAtADrop) { skippedRocks++; return false; }
                var mesh = new Mesh { name = "SummitCrag_" + seed };
                mesh.SetVertices(vertices);
                mesh.SetTriangles(source.triangles, 0);
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                // The tilt and size are in the vertices, so the shape stands at scale 1 and no rotation.
                Shape("Crag " + seed, parent, centre, mesh.bounds.size, Pick(seed * 41, rockA, rockB, rockC), mesh);
                corridor.Reserve(p, radius * 1.15f);
                rocks++;
                lastCragReach = reach;
                longestCragReach = Mathf.Max(longestCragReach, reach);
                if (reach > 0.05f) groundedRocks++;
                return true;
            }

            private void Jump(Transform parent)
            {
                var gap = gaps[0];
                for (int end = 0; end < 2; end++)
                {
                    double s = end == 0 ? gap.Start - 4d : gap.End + 4d;
                    TrackFrame f = line.FrameAt(s);
                    for (int side = -1; side <= 1; side += 2)
                    {
                        if (!Place(s, side, 4.4f, 1.5f, 6f, out Vector3 p)) continue;
                        Transform remnant = Group("Broken bridge abutment", parent);
                        remnant.SetPositionAndRotation(p, Flat(f.Forward));
                        Box("Broken post", remnant, Vector3.up * 2.6f, new Vector3(0.6f, 6f, 0.7f), wood);
                        for (int plank = 0; plank < 4; plank++)
                        {
                            GameObject board = Box("Splintered plank", remnant, new Vector3(0f, 1.8f - plank * 0.45f, (plank - 1) * 0.7f), new Vector3(1.5f, 0.16f, 3f), cutWood);
                            board.transform.localRotation = Quaternion.Euler(15f + plank * 19f, plank * 8f, side * 14f);
                        }
                    }
                }
                TrackFrame mid = line.FrameAt((gap.Start + gap.End) * 0.5d);
                Transform falls = Group("Small ravine waterfall", parent);
                Vector3 at = mid.Position + mid.Right * 32f;
                at.y = GroundAt(at);
                falls.SetPositionAndRotation(at, Flat(mid.Right));
                // All water lies far below the jump. Narrow ribbons and staggered shelves read as a cascade.
                for (int tier = 0; tier < 4; tier++)
                {
                    Box("Water ribbon", falls, new Vector3(0f, 4f + tier * 6f, tier * 2.4f), new Vector3(2.2f, 7f, 0.22f), water);
                    Box("White water", falls, new Vector3(0f, 1f + tier * 6f, tier * 2.4f - 0.3f), new Vector3(3.4f, 0.35f, 2f), foam);
                    Box("Cascade ledge", falls, new Vector3(0f, 0.4f + tier * 6f, tier * 2.4f), new Vector3(5f, 1f, 3f), rockC);
                }
                Box("Ravine pool", falls, new Vector3(0f, 0.1f, -2f), new Vector3(12f, 0.18f, 15f), water);
            }

            private void Fork(Transform parent)
            {
                TrackDefinition.Shortcut shortcut = definition.Shortcuts[0];
                TrackFrame f = line.FrameAt(shortcut.FromS);
                // 1.6 times the first sign so it reads from farther up the road (user 2026-11-09 "1.5배 이상").
                const float k = 1.6f;
                if (!Place(shortcut.FromS - 8d, -1, 5.3f, 1.4f * k, 4f * k, out Vector3 p))
                    throw new InvalidOperationException("The shortcut's wooden direction sign has no clear placement.");
                Transform sign = Group("Shortcut wooden direction sign", parent);
                sign.SetPositionAndRotation(p, Flat(f.Forward));
                // The read face is -Z, toward the runner coming down the road. The post stands behind the board (+Z)
                // and stops below its top edge, so no part of it shows on the arrow face (user 2026-11-09).
                float boardY = 3f * k, boardTop = boardY + 0.5f * k, boardHalfDepth = 0.1f * k, postWidth = 0.3f * k;
                float postTop = boardTop - 0.15f * k;
                Box("Post", sign, new Vector3(0f, (postTop - 0.3f) * 0.5f, boardHalfDepth + postWidth * 0.5f),
                    new Vector3(postWidth, postTop + 0.3f, postWidth), wood);
                Box("Sign board", sign, new Vector3(0f, boardY, 0f), new Vector3(2.8f * k, 1f * k, boardHalfDepth * 2f), cutWood);
                // The head sits on +X: seen from the -Z side that is the runner's right, the side the shortcut leaves on
                // (user 2026-11-09, a render showed the old -X head pointing away from the shortcut).
                float inkZ = -(boardHalfDepth + 0.05f * k), inkWidth = 0.14f * k;
                Vector3 tip = new Vector3(0.9f * k, boardY, inkZ);
                Beam("Arrow", sign, tip, new Vector3(-0.9f * k, boardY, inkZ), inkWidth, ink);
                Beam("Arrow", sign, tip, new Vector3(0.2f * k, boardY + 0.35f * k, inkZ), inkWidth, ink);
                Beam("Arrow", sign, tip, new Vector3(0.2f * k, boardY - 0.35f * k, inkZ), inkWidth, ink);
                for (double s = 1196d; s < 1525d; s += 14d)
                    for (int side = -1; side <= 1; side += 2)
                    {
                        if (!Place(s, side, 4f, 2.8f, 2f, out Vector3 at)) continue;
                        Transform fence = Group("Clearing log fence", parent);
                        fence.SetPositionAndRotation(at, Flat(line.FrameAt(s).Forward));
                        for (int end = -1; end <= 1; end += 2)
                            Box("Fence stake", fence, new Vector3(0f, 0.8f, end * 2.2f), new Vector3(0.3f, 2f, 0.3f), wood);
                        for (int rail = 0; rail < 2; rail++) Beam("Log rail", fence, new Vector3(0f, 0.5f + rail * 0.7f, -2.5f), new Vector3(0f, 0.5f + rail * 0.7f, 2.5f), 0.28f, cutWood);
                    }
            }

            private void Corners(Transform parent)
            {
                double s = 0d;
                int index = 0;
                foreach (TrackDefinition.Segment segment in definition.Segments)
                {
                    double start = s;
                    s += segment.Length;
                    if (start < 1540d || start >= 1840d || !segment.IsArc) continue;
                    double middle = start + segment.Length * 0.5d;
                    int outside = segment.Degrees > 0f ? -1 : 1;
                    TrackFrame f = line.FrameAt(middle);
                    Vector3 p = default;
                    bool placed = false;
                    foreach (float offset in new[] { 7f, 12f, 18f, 25f })
                    {
                        if (!Place(middle, outside, offset, 2.6f, 5f, out p)) continue;
                        placed = true;
                        break;
                    }
                    if (!placed)
                        throw new InvalidOperationException("Zigzag corner board has no clear placement at S" + middle);
                    Transform board = Group("Zigzag chevrons " + index, parent);
                    board.SetPositionAndRotation(p, Flat(-f.Right * outside));
                    for (int leg = -1; leg <= 1; leg += 2) Box("Board leg", board, new Vector3(leg * 1.5f, 1.5f, 0f), new Vector3(0.18f, 3.5f, 0.18f), wood);
                    Shape("Board", board, new Vector3(0f, 3.4f, 0f), new Vector3(4f, 1.474f, 0.158f), ink, chevronPanel);
                    float direction = segment.Degrees > 0f ? -1f : 1f;
                    // The kit arrow's tip is +x and is the same upside down: reverse it around the board's normal.
                    Quaternion turn = direction > 0f ? Quaternion.identity : Quaternion.Euler(0f, 0f, 180f);
                    for (int arrow = -1; arrow <= 1; arrow++)
                        Shape("Chevron", board, new Vector3(arrow * 1.1f, 3.4f, ArrowOut), new Vector3(0.948f, 1.053f, 0.126f), chevron, chevronArrow).transform.localRotation = turn;
                    bool rockPlaced = false;
                    Vector3? firstClear = null;
                    foreach (float offset in new[] { 20f, 28f, 38f, 50f, 65f, 80f, 100f })
                    {
                        if (!Place(middle, outside, offset, 6f, 20f, out Vector3 r, false)) continue;
                        if (firstClear == null) firstClear = r;
                        if (!Rock(parent, r, 6f, 19f, index + 700)) continue; // at a drop: try further out
                        rockPlaced = true;
                        break;
                    }
                    // A required landmark: where every clear spot is at a drop, the first one takes a crag whose foot
                    // reaches all the way down. Said aloud, since its long foot is checked by vertex only.
                    if (!rockPlaced && firstClear.HasValue)
                    {
                        rockPlaced = Rock(parent, firstClear.Value, 6f, 19f, index + 700, true);
                        Debug.LogWarning($"Zigzag outside crag at S{middle:F0}: every clear spot is at a drop, so its foot reaches {lastCragReach:F1} m down.");
                    }
                    if (!rockPlaced) throw new InvalidOperationException("Zigzag outside crag has no clear placement at S" + middle);
                    index++;
                }
                if (index != 4) throw new InvalidOperationException("Expected four zigzag corner boards, got " + index);
            }

            private void Tunnel(Transform parent)
            {
                const double Start = 1848d, End = 1910d;
                const int Around = 40;
                // An inner and outer shell with two-metre longitudinal faces and sub-two-metre arch faces.
                // The spring line is 10.5 m above the road, higher than the 9 m corridor exemption.
                for (int layer = 0; layer < 2; layer++)
                {
                    var vertices = new List<Vector3>();
                    var triangles = new List<int>();
                    for (double s = Start; s <= End; s += 2d)
                    {
                        TrackFrame f = line.FrameAt(s);
                        float half = definition.HalfWidthAt(s) + 4.5f + layer * 4f;
                        for (int a = 0; a <= Around; a++)
                        {
                            float angle = a * Mathf.PI / Around;
                            float x = Mathf.Cos(angle) * half;
                            float y = 10.5f + Mathf.Sin(angle) * (7f + layer * 4f);
                            vertices.Add(f.TransformPoint(new Vector3(x, y, 0f)));
                        }
                    }
                    int rows = vertices.Count / (Around + 1);
                    for (int row = 0; row < rows - 1; row++)
                        for (int a = 0; a < Around; a++)
                        {
                            int v = row * (Around + 1) + a, next = v + Around + 1;
                            QuadIndices(triangles, v, v + 1, next, next + 1, layer != 0);
                        }
                    MeshObject(layer == 0 ? "Dark tunnel lining" : "Outer rock roof", parent, vertices, triangles, layer == 0 ? ink : rockC);
                }
                for (double s = Start; s < End; s += 2d)
                {
                    TrackFrame f = line.FrameAt(s + 1d);
                    float half = definition.HalfWidthAt(s) + 6.5f;
                    for (int side = -1; side <= 1; side += 2)
                    {
                        Vector3 foot = f.Position + f.Right * side * half;
                        float bottom = GroundAt(foot) - f.Position.y - 1f;
                        int tiers = Mathf.CeilToInt((10.8f - bottom) / 2f);
                        float rise = (10.8f - bottom) / tiers;
                        for (int tier = 0; tier < tiers; tier++)
                        {
                            GameObject wall = Box("Tunnel rock side", parent, foot + Vector3.up * (bottom + rise * (tier + 0.5f)),
                                new Vector3(4f, rise + 0.04f, 2.3f), rockC);
                            wall.transform.localRotation = Flat(f.Forward);
                        }
                    }
                }
                // Both portal rims are made of short blocks; no large unsampled face bridges the driving space.
                foreach (double s in new[] { Start, End })
                {
                    TrackFrame f = line.FrameAt(s);
                    float half = definition.HalfWidthAt(s) + 6.5f;
                    for (int a = 0; a < 32; a++)
                    {
                        float angle = (a + 0.5f) * Mathf.PI / 32f;
                        Vector3 p = f.TransformPoint(new Vector3(Mathf.Cos(angle) * half, 12.4f + Mathf.Sin(angle) * 8.9f, 0f));
                        GameObject block = Box("Portal stone", parent, p, new Vector3(1.7f, 4f, 2.6f), a % 2 == 0 ? rockA : rockB);
                        block.transform.localRotation = Flat(f.Forward) * Quaternion.Euler(0f, 0f, angle * Mathf.Rad2Deg - 90f);
                    }
                }
            }

            private void Village(Transform parent)
            {
                var built = new List<string>();
                for (int i = 0; i < 6; i++)
                {
                    double s = 2148d + i * 19d;
                    int side = i % 2 == 0 ? -1 : 1;
                    // 10 m high: a chimney on the ridge of a four-bay cabin tops out at 9.7 m.
                    if (!Place(s, side, 17f + Hash(i + 91) * 7f, 7f, 10f, out Vector3 p)) continue;
                    Transform hut = Group("Valley cabin " + i, parent);
                    // The front (local -z) faces the road (user 2026-11-20 "앞면 길 쪽으로 돌려줘"; it used to face away).
                    hut.SetPositionAndRotation(p, Flat(line.FrameAt(s).Right * side));
                    built.Add(i + ": " + Cabin(hut, i));
                }
                Debug.Log($"Summit Drop valley: {built.Count} cabins ({CabinKitName}) — {string.Join("; ", built)}.", parent);
                TrackFrame finish = line.FrameAt(definition.LapLength);
                Transform gate = Group("Timber finish gate", parent);
                gate.SetPositionAndRotation(finish.Position, Flat(finish.Forward));
                float half = definition.HalfWidthAt(definition.LapLength) + 3.4f;
                for (int side = -1; side <= 1; side += 2)
                {
                    Box("Timber gate post", gate, new Vector3(side * half, 5.5f, 0f), new Vector3(1f, 12.8f, 1f), wood);
                    Box("Flag mast", gate, new Vector3(side * (half + 2.3f), 5.7f, 0f), new Vector3(0.15f, 13f, 0.15f), cutWood);
                    Box("Mountain pennant", gate, new Vector3(side * (half + 2.3f), 11.5f, 1.1f), new Vector3(0.08f, 1.7f, 2.2f), side < 0 ? amber : cream);
                }
                Box("Finish beam", gate, new Vector3(0f, 11.2f, 0f), new Vector3(half * 2f + 1.5f, 1.8f, 1.3f), cutWood);
                // Solid glyphs, as on Palm Coast's arch, with this course's own finish wording.
                Lettering(gate, "SUMMIT DROP", 15f, 11.2f, -0.72f);
            }

            // The cabin kit's meshes, checked where their origins lie: a bay's pieces share the bay's origin (middle of its
            // width and height, on the wall's face, standing out to +z), the porch's two pieces the middle of the porch,
            // the rest their bounds centre (KitMesh). A re-export with another pivot would stand a bay off the wall.
            private void LoadCabinKit()
            {
                cabinKit.Clear();
                foreach (string name in new[] { "CabinWindow_Trim", "CabinWindow_Glass", "CabinWindow_Accent", "CabinDoor_Trim", "CabinDoor_Accent" })
                {
                    Mesh mesh = KitPiece(CabinKitName, name);
                    Bounds b = mesh.bounds;
                    // Every piece starts at the wall (the glass 3 cm out): a piece standing further off would float in
                    // front of its frame although it stays inside the bay (Codex review 2026-11-20).
                    if (b.min.z < -0.01f || b.min.z > 0.035f || b.max.z > 0.3f || b.min.x < -CabinBay * 0.5f - 0.01f || b.max.x > CabinBay * 0.5f + 0.01f
                        || b.min.y < -CabinWall * 0.5f - 0.01f || b.max.y > CabinWall * 0.5f + 0.01f)
                        throw new InvalidOperationException($"Kit mesh {name} in {CabinKitName} has bounds {b}, outside its {CabinBay} x {CabinWall} m bay or off the wall's face from the bay's origin; export it with the bay's pivot.");
                    cabinKit[name] = mesh;
                }
                Mesh porchTrim = KitPiece(CabinKitName, "CabinPorch_Trim"), porchRoof = KitPiece(CabinKitName, "CabinPorch_Roof");
                Bounds porch = porchTrim.bounds;
                porch.Encapsulate(porchRoof.bounds);
                if (porch.center.sqrMagnitude > 1e-4f || Vector3.Distance(porch.size, new Vector3(CabinBay, 3f, 1.8f)) > 0.02f)
                    throw new InvalidOperationException($"Kit porch in {CabinKitName} spans {porch}; it should be {CabinBay} x 3 x 1.8 m round its origin.");
                // The step and the deck the footings below are cut to: only the step reaches the porch's foot, at its
                // front edge (+z in the kit), and the deck's top is PorchDeckOverFoot up.
                float foot = porch.min.y, deckTop = foot + PorchDeckOverFoot;
                bool deckFound = false;
                foreach (Vector3 v in porchTrim.vertices)
                {
                    if (v.y < foot + 0.01f && v.z < porch.max.z - PorchStepDepth - 0.01f)
                        throw new InvalidOperationException($"Kit porch in {CabinKitName} reaches its foot at z {v.z:F2}, behind its {PorchStepDepth} m step; the porch footings would not fit it.");
                    if (Mathf.Abs(v.y - deckTop) < 0.01f) deckFound = true;
                }
                if (!deckFound)
                    throw new InvalidOperationException($"Kit porch in {CabinKitName} has no deck top {PorchDeckOverFoot} m over its foot; the deck would not meet the cabin's floor.");
                cabinKit["CabinPorch_Trim"] = porchTrim;
                cabinKit["CabinPorch_Roof"] = porchRoof;
                foreach (string name in new[] { "CabinLog", "CabinGable", "RoofRidge", "StoneChimney", "WoodPile_Wood", "WoodPile_Ends" })
                    cabinKit[name] = KitMesh(CabinKitName, name);
            }

            // One valley cabin of the kit (K11), its front to local -z (toward the road). Per cabin: three or four
            // bays with the door in the middle bay or the next, 6.4-8.0 m deep, a side window or none, a porch or none, the
            // chimney on the left or right slope or on the ridge at the back, a wood pile by one side or none. The footing
            // and the roof panels are boxes (their sizes differ per cabin). Returns what it chose, for the build log.
            private string Cabin(Transform hut, int index)
            {
                int seed = 700 + index * 11;
                int bays = Hash(seed) < 0.5f ? 3 : 4;
                float width = bays * CabinBay + 2f * CabinCorner;
                float depth = 6.4f + Hash(seed + 1) * 1.6f;
                int middle = (bays - 1) / 2;
                int door = Hash(seed + 2) < 0.5f ? middle : middle + 1;
                int sideWindow = Hash(seed + 3) < 0.5f ? 0 : Hash(seed + 4) < 0.5f ? -1 : 1;
                bool porch = Hash(seed + 5) < 0.5f;
                int chimney = Mathf.Min(2, (int)(Hash(seed + 6) * 3f));
                int pile = Hash(seed + 7) < 0.5f ? 0 : Hash(seed + 8) < 0.5f ? -1 : 1;

                Box("Cabin footing", hut, Vector3.up * (CabinFloor - 0.5f), new Vector3(width + 0.4f, 1f, depth + 0.4f), rockB);
                // Front and back logs run the width, side logs the depth, each 0.3 m past the corner; the side walls sit half
                // a course up, so the logs cross in turn at every corner, with a sill under the side walls' lowest logs.
                Mesh log = cabinKit["CabinLog"];
                for (int course = 0; course < CabinCourses; course++)
                {
                    float y = CabinFloor + (course + 0.5f) * CabinLog;
                    for (int end = -1; end <= 1; end += 2)
                    {
                        Shape("Log", hut, new Vector3(0f, y, end * (depth - CabinLog) * 0.5f), new Vector3(width + 2f * CabinLogOut, CabinLog, CabinLog), wood, log);
                        // 1 cm short of the roof panels' ends at each end: the top side logs reach into the panels, and
                        // ends in the panels' end planes would flicker (reviewer 2026-11-20).
                        GameObject sideLog = Shape("Log", hut, new Vector3(end * (width - CabinLog) * 0.5f, y + CabinLog * 0.5f, 0f), new Vector3(depth + 2f * CabinLogOut - 0.02f, CabinLog, CabinLog), wood, log);
                        sideLog.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
                    }
                }
                for (int end = -1; end <= 1; end += 2)
                    Box("Side wall sill", hut, new Vector3(end * (width - CabinLog) * 0.5f, CabinFloor + CabinLog * 0.25f, 0f), new Vector3(CabinLog, CabinLog * 0.5f, depth), rockB);

                // The gable fills the front and back walls up to the ridge; the panels' undersides meet on its apex and
                // stop there, so they do not overlap (a filler under the ridge closes the notch between their upper ends).
                float rad = CabinPitch * Mathf.Deg2Rad, cos = Mathf.Cos(rad), sin = Mathf.Sin(rad), pitch = Mathf.Tan(rad);
                float wallTop = CabinFloor + CabinWall, rise = width * 0.5f * pitch, apex = wallTop + rise;
                float top = apex + CabinRoof / cos, slope = (width * 0.5f + CabinEave) / cos, roofLength = depth + 2f * CabinLogOut;
                Shape("Gable", hut, new Vector3(0f, wallTop + rise * 0.5f, 0f), new Vector3(width, rise, depth), wood, cabinKit["CabinGable"]);
                for (int sideRoof = -1; sideRoof <= 1; sideRoof += 2)
                {
                    var centre = new Vector3(sideRoof * (slope * 0.5f * cos + CabinRoof * 0.5f * sin), apex - slope * 0.5f * sin + CabinRoof * 0.5f * cos, 0f);
                    GameObject panel = Box("Pitched timber roof", hut, centre, new Vector3(slope, CabinRoof, roofLength), roof);
                    panel.transform.localRotation = Quaternion.Euler(0f, 0f, -sideRoof * CabinPitch);
                }
                // 1 cm short of the panels' ends, so no face lies in their end planes.
                Box("Ridge filler", hut, new Vector3(0f, (apex + top) * 0.5f, 0f), new Vector3(2f * CabinRoof * sin, top - apex, roofLength - 0.02f), roof);
                Mesh ridgeMesh = cabinKit["RoofRidge"];
                Vector3 ridgeSize = ridgeMesh.bounds.size;
                // The ridge's underside is a V at the roof's pitch, its apex this far over the mesh's middle.
                float ridgeUnder = -ridgeSize.y * 0.5f + ridgeSize.z * 0.5f * pitch;
                GameObject ridge = Shape("Roof ridge", hut, new Vector3(0f, top - ridgeUnder, 0f), new Vector3(roofLength, ridgeSize.y, ridgeSize.z), roof, ridgeMesh);
                ridge.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);

                float frontZ = -depth * 0.5f, bayY = CabinFloor + CabinWall * 0.5f, doorX = 0f;
                for (int b = 0; b < bays; b++)
                {
                    // Turned to face -z, so bay 0 is on the cabin's +x side.
                    float x = width * 0.5f - CabinCorner - (b + 0.5f) * CabinBay;
                    if (b == door) doorX = x;
                    CabinFacadeBay(hut, b == door ? "CabinDoor" : "CabinWindow", new Vector3(x, bayY, frontZ), Quaternion.Euler(0f, 180f, 0f));
                }
                if (sideWindow != 0)
                    CabinFacadeBay(hut, "CabinWindow", new Vector3(sideWindow * width * 0.5f, bayY + CabinLog * 0.5f, 0f), Quaternion.Euler(0f, sideWindow * 90f, 0f));
                if (porch)
                {
                    // Its wall side (-z in the kit) against the front, the deck level with the bays' floor (1 cm over it).
                    Mesh porchTrim = cabinKit["CabinPorch_Trim"];
                    Bounds porchBounds = porchTrim.bounds;
                    porchBounds.Encapsulate(cabinKit["CabinPorch_Roof"].bounds);
                    float deckTop = CabinFloor + PorchLift, porchDepth = porchBounds.size.z;
                    Vector3 at = new Vector3(doorX, deckTop - PorchDeckOverFoot - porchBounds.min.y, frontZ - porchDepth * 0.5f);
                    Piece("Porch", hut, at, cutWood, porchTrim).transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
                    Piece("Porch roof", hut, at, roof, cabinKit["CabinPorch_Roof"]).transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
                    // The cabin stands on the ground at its middle; on a slope the porch would hang over it (Codex review
                    // 2026-11-20: up to 1.4 m), so stone footings reach down under the deck and under the step.
                    PorchFooting(hut, doorX, frontZ, frontZ - porchDepth + PorchStepDepth, deckTop - PorchDeck);
                    PorchFooting(hut, doorX, frontZ - porchDepth + PorchStepDepth, frontZ - porchDepth, deckTop - PorchDeckOverFoot);
                }

                Mesh chimneyMesh = cabinKit["StoneChimney"];
                float chimneyX = chimney == 2 ? 0f : (chimney == 0 ? -0.25f : 0.25f) * width;
                float chimneyZ = chimney == 2 ? depth * 0.5f - 1f : depth * 0.25f;
                float roofOver = top - Mathf.Abs(chimneyX) * pitch;
                Piece("Stone chimney", hut, new Vector3(chimneyX, roofOver + CabinChimneyAboveRoof - chimneyMesh.bounds.extents.y, chimneyZ), rockC, chimneyMesh);

                if (pile != 0)
                {
                    // On the ground beside the footing (its top is 0.8 m up, too high to lean on), along the side wall.
                    Mesh pileWood = cabinKit["WoodPile_Wood"];
                    var at = new Vector3(pile * (width * 0.5f + 0.2f + pileWood.bounds.extents.z + 0.05f), 0f, (Hash(seed + 9) < 0.5f ? -0.25f : 0.25f) * depth);
                    at.y = GroundAt(hut.TransformPoint(at)) - hut.position.y + pileWood.bounds.extents.y - 0.05f;
                    Piece("Wood pile", hut, at, wood, pileWood).transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
                    Piece("Wood pile ends", hut, at, cutWood, cabinKit["WoodPile_Ends"]).transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
                }
                string chimneySpot = chimney == 0 ? "left" : chimney == 1 ? "right" : "ridge";
                return $"{bays} bays x {depth:F1} m, door bay {door}, side window {sideWindow}, porch {(porch ? "yes" : "no")}, chimney {chimneySpot}, wood pile {pile}";
            }

            // A stone footing under part of a porch (z from near to far in front of the cabin), from its top down 0.1 m into
            // the lowest ground at its corners. None where the ground already reaches the top.
            private void PorchFooting(Transform hut, float x, float near, float far, float top)
            {
                float lowest = float.PositiveInfinity;
                for (int cx = -1; cx <= 1; cx += 2)
                    foreach (float z in new[] { near, far })
                        lowest = Mathf.Min(lowest, GroundAt(hut.TransformPoint(new Vector3(x + cx * CabinBay * 0.5f, 0f, z))) - hut.position.y);
                float bottom = lowest - 0.1f;
                if (top - bottom < 0.05f) return;
                Box("Porch footing", hut, new Vector3(x, (top + bottom) * 0.5f, (near + far) * 0.5f), new Vector3(CabinBay, top - bottom, Mathf.Abs(near - far)), rockB);
            }

            // A façade bay of the cabin kit: its Trim, Glass and Accent pieces (those it has) at one origin.
            private void CabinFacadeBay(Transform hut, string bay, Vector3 origin, Quaternion facing)
            {
                foreach (var (slot, colour) in new[] { ("_Trim", cutWood), ("_Glass", ink), ("_Accent", cutWood) })
                    if (cabinKit.TryGetValue(bay + slot, out Mesh mesh))
                        Piece(bay + slot, hut, origin, colour, mesh).transform.localRotation = facing;
            }

            private void Lettering(Transform parent, string words, float width, float y, float z)
            {
                var glyphs = new Dictionary<char, string>
                {
                    { 'S', "111100111001111" }, { 'U', "101101101101111" }, { 'M', "101111111101101" },
                    { 'I', "111010010010111" }, { 'T', "111010010010010" }, { 'D', "110101101101110" },
                    { 'R', "110101110101101" }, { 'O', "111101101101111" }, { 'P', "110101110100100" }, { ' ', "000000000000000" }
                };
                float step = width / words.Length, cell = step / 4f;
                for (int i = 0; i < words.Length; i++)
                    for (int row = 0; row < 5; row++)
                        for (int col = 0; col < 3; col++)
                            if (glyphs[words[i]][row * 3 + col] == '1')
                                Box("Solid letter " + words[i], parent, new Vector3(-width * 0.5f + i * step + (col + 0.5f) * cell, y + (2 - row) * cell * 0.9f, z), new Vector3(cell * 0.85f, cell * 0.8f, 0.12f), ink);
            }

            // A pole's shaft ends SwingLook.PoleDepth below its point. Inside a turn over a cliff the mountain falls further
            // than that, so a rock spire carries the shaft down to the ground (user 2026-11-06 "봉이 공중에 떠있는것이").
            private void PoleSpires(Transform parent)
            {
                const int Sides = 8;
                const float Overlap = 1f, TopRadius = 1.1f, BaseRadius = 5f, Taper = 0.05f, RingEvery = 10f, Rough = 0.2f, ThinnestRoom = 0.3f;
                var points = new List<Vector3>();
                var triangles = new List<int>();
                int spires = 0;
                for (int i = 0; i < poles.Count; i++)
                {
                    Vector3 pole = poles[i];
                    float top = pole.y - SwingLook.PoleDepth + Overlap, bottom = GroundAt(pole) - Overlap;
                    // The shaft reaches the ground, or the ground stands over it.
                    if (top - bottom <= Overlap * 2f) continue;
                    float room = RoomBesideRoads(pole, bottom, top);
                    if (room < ThinnestRoom) throw new InvalidOperationException($"No room for a spire under the pole at {pole}: {room:F2} m.");
                    float wide = Mathf.Min(BaseRadius, TopRadius + (top - bottom) * Taper, room), narrow = Mathf.Min(TopRadius, room);
                    int rings = Mathf.Max(2, Mathf.CeilToInt((top - bottom) / RingEvery) + 1);
                    int first = points.Count;
                    for (int ring = 0; ring < rings; ring++)
                    {
                        float along = ring / (rings - 1f);
                        for (int side = 0; side < Sides; side++)
                        {
                            float angle = side * Mathf.PI * 2f / Sides;
                            float radius = Mathf.Lerp(wide, narrow, along) * (1f - Hash(i * 97 + ring * 13 + side) * Rough);
                            points.Add(new Vector3(pole.x + Mathf.Cos(angle) * radius, Mathf.Lerp(bottom, top, along), pole.z + Mathf.Sin(angle) * radius));
                        }
                    }
                    for (int ring = 0; ring + 1 < rings; ring++)
                        for (int side = 0; side < Sides; side++)
                        {
                            int a = first + ring * Sides + side, b = first + ring * Sides + (side + 1) % Sides;
                            QuadIndices(triangles, a, b, a + Sides, b + Sides, false);
                        }
                    int cap = points.Count, last = first + (rings - 1) * Sides;
                    points.Add(new Vector3(pole.x, top, pole.z));
                    for (int side = 0; side < Sides; side++)
                    {
                        triangles.Add(cap);
                        triangles.Add(last + (side + 1) % Sides);
                        triangles.Add(last + side);
                    }
                    spires++;
                }
                if (spires == 0) throw new InvalidOperationException("Summit Drop layout changed: no pole hangs over the ground, so 10 has nothing to hold.");
                MeshObject("Pole rock spires", parent, points, triangles, rockC);
                Debug.Log($"Summit Drop raised {spires} rock spires under corner poles.", parent);
            }

            // How far out from pole a spire may reach and stay clear of any road at its heights (RoadCorridor's edge margin).
            private float RoomBesideRoads(Vector3 pole, float bottom, float top)
            {
                const float EdgeMargin = 2f, Reach = 2f;
                float room = float.PositiveInfinity;
                for (int i = 0; i < samples.Count; i++)
                {
                    Vector3 road = samples[i].Position;
                    if (road.y < bottom - Reach || road.y > top + Reach) continue;
                    room = Mathf.Min(room, new Vector2(road.x - pole.x, road.z - pole.z).magnitude - widths[i] - EdgeMargin);
                }
                return room;
            }

            private void ValidatePoles(GameObject root)
            {
                int checks = 0;
                foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
                {
                    Vector3[] v = filter.sharedMesh.vertices;
                    int[] t = filter.sharedMesh.triangles;
                    Matrix4x4 world = filter.transform.localToWorldMatrix;
                    for (int i = 0; i < t.Length; i += 3)
                    {
                        Vector3 a = world.MultiplyPoint3x4(v[t[i]]), b = world.MultiplyPoint3x4(v[t[i + 1]]), c = world.MultiplyPoint3x4(v[t[i + 2]]);
                        foreach (Vector3 pole in poles)
                        {
                            if (Mathf.Max(a.y, b.y, c.y) < pole.y - 0.05f || Mathf.Min(a.y, b.y, c.y) > pole.y + 21f) continue;
                            if (pole.x < Mathf.Min(a.x, b.x, c.x) - PoleRadius || pole.x > Mathf.Max(a.x, b.x, c.x) + PoleRadius
                                || pole.z < Mathf.Min(a.z, b.z, c.z) - PoleRadius || pole.z > Mathf.Max(a.z, b.z, c.z) + PoleRadius) continue;
                            int n = Mathf.Max(1, Mathf.CeilToInt(Mathf.Max((b - a).magnitude, (c - a).magnitude, (c - b).magnitude)));
                            for (int u = 0; u <= n; u++)
                                for (int w = 0; w <= n - u; w++)
                                {
                                    Vector3 p = a + (b - a) * ((float)u / n) + (c - a) * ((float)w / n);
                                    checks++;
                                    if (p.y < pole.y - 0.05f || p.y > pole.y + 21f) continue;
                                    if ((new Vector2(p.x - pole.x, p.z - pole.z)).sqrMagnitude < PoleRadius * PoleRadius)
                                        throw new InvalidOperationException($"{filter.name} obscures a pole shaft at {pole}, surface at {p}.");
                                }
                        }
                    }
                }
                Debug.Log($"Summit Drop pole clearance PASS: {poles.Count} poles, 3 m shaft envelope, {checks} triangle probes.", root);
            }

            private static void QuadIndices(List<int> triangles, int a, int b, int c, int d, bool reverse)
            {
                triangles.Add(a); triangles.Add(reverse ? b : c); triangles.Add(reverse ? c : b);
                triangles.Add(b); triangles.Add(reverse ? d : c); triangles.Add(reverse ? c : d);
            }

            private static GameObject MeshObject(string name, Transform parent, List<Vector3> vertices, List<int> triangles, Material material)
            {
                var mesh = new Mesh { name = "SummitSource_" + name, indexFormat = IndexFormat.UInt32 };
                mesh.SetVertices(vertices);
                mesh.SetTriangles(triangles, 0);
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                var part = new GameObject(name);
                part.transform.SetParent(parent, false);
                part.AddComponent<MeshFilter>().sharedMesh = mesh;
                MeshRenderer renderer = part.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                return part;
            }

        }
    }
}

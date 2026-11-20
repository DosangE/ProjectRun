using System;
using System.Collections.Generic;
using UnityEngine;
using static ProtoHarness.Editor.ChainRush.ExteriorKit;

namespace ProtoHarness.Editor.ChainRush
{
    // The city tower kit (docs/ART_REQUEST_PROPS.md K9), shared by PALM COAST's city towers and SKYWAY's flats by the
    // viaduct (user 2026-11-20 "ㄱㄱ"). Façade bays are 3.0 m wide, 4.8 m on the ground floor and 3.2 m above, a tier
    // 0.6 m wider than its bays at either end. A bay's slot objects share the bay's origin, as the town house kit's: the
    // middle of its width and height, on the wall's face; a bay stands out at most 0.3 m (upper), 1.3 m (balcony) or
    // 0.4 m (ground floor). Each track keeps its own placement and colours.
    public static class CityTowerAssembly
    {
        public const string KitName = "CityTowerKit";
        public const float BayWidth = 3f, GroundStorey = 4.8f, UpperStorey = 3.2f, Margin = 0.6f, CorniceDepth = 0.5f;
        private static readonly string[] BaySlots = { "Trim", "Glass", "Accent", "Metal" };
        private static readonly string[] BayMeshes =
        {
            "TowerBand_Trim", "TowerBand_Glass", "TowerColumn_Trim", "TowerColumn_Glass", "TowerBalcony_Trim", "TowerBalcony_Glass",
            "TowerBalcony_Metal", "TowerShop_Trim", "TowerShop_Glass", "TowerShop_Accent", "TowerEntry_Trim", "TowerEntry_Glass",
        };

        public static float Width(int bays) => bays * BayWidth + Margin * 2f;

        private static float BayX(int bay, int bays) => (bay - (bays - 1) * 0.5f) * BayWidth;

        // The colours a track gives a tower: its walls, the trim (bays, cornices, plant room), the windows' glass, the
        // shop front's dark glass, the railings and roof metal, the shops' blank sign, the awning and the roof units.
        public readonly struct Colours
        {
            public readonly Material Facade, Trim, Glass, ShopGlass, Metal, Sign, Awning, RoofUnit;

            public Colours(Material facade, Material trim, Material glass, Material shopGlass, Material metal, Material sign, Material awning, Material roofUnit)
            {
                if (facade == null || trim == null || glass == null || shopGlass == null || metal == null || sign == null || awning == null || roofUnit == null)
                    throw new ArgumentException("Every tower colour is required.");
                Facade = facade; Trim = trim; Glass = glass; ShopGlass = shopGlass; Metal = metal; Sign = sign; Awning = awning; RoofUnit = roofUnit;
            }
        }

        // The kit's meshes, loaded once per build and checked like the town house kit's: every bay inside its 3.0 m bay
        // from an origin on the wall's face, the cornice, railing and roof clutter placed by their bounds centre.
        public sealed class Kit
        {
            private readonly Dictionary<string, Mesh> meshes = new Dictionary<string, Mesh>();

            public Mesh this[string name] => meshes[name];

            public bool TryGet(string name, out Mesh mesh) => meshes.TryGetValue(name, out mesh);

            public static Kit Load()
            {
                const float Slack = 1e-3f;
                var kit = new Kit();
                foreach (string name in BayMeshes)
                {
                    Mesh mesh = KitPiece(KitName, name);
                    bool ground = name.StartsWith("TowerShop", StringComparison.Ordinal) || name.StartsWith("TowerEntry", StringComparison.Ordinal);
                    float storey = ground ? GroundStorey : UpperStorey;
                    float reach = ground ? 0.4f : name.StartsWith("TowerBalcony", StringComparison.Ordinal) ? 1.3f : 0.3f;
                    Bounds b = mesh.bounds;
                    if (b.min.z < -0.01f || b.max.z > reach || b.min.x < -BayWidth * 0.5f - Slack || b.max.x > BayWidth * 0.5f + Slack
                        || b.min.y < -storey * 0.5f - Slack || b.max.y > storey * 0.5f + Slack)
                        throw new InvalidOperationException($"Kit mesh {name} in {KitName} has bounds {b}, outside its {BayWidth} x {storey} m bay (at most {reach} m out) from an origin on the wall's face; export it with the bay's pivot.");
                    kit.meshes[name] = mesh;
                }
                foreach (string name in new[] { "TowerCornice", "RoofRail", "WaterTank", "Antenna", "RoofUnit" }) kit.meshes[name] = KitMesh(KitName, name);
                // The cornices meet at the corners by this depth (TierCornice).
                if (Mathf.Abs(kit.meshes["TowerCornice"].bounds.size.z - CorniceDepth) > Slack)
                    throw new InvalidOperationException($"Kit mesh TowerCornice in {KitName} is {kit.meshes["TowerCornice"].bounds.size.z} m deep, not {CorniceDepth}; update CorniceDepth.");
                return kit;
            }
        }

        // A tower named name, of the kit's façade bays on its front (local +Z) (user 2026-11-19): one to three tiers, each
        // a bay narrower (at least two) and 0.8 as deep as the one below, the floors above the shops shared out between
        // them; one kind of window for the whole tower (bands, columns, or bands with balconies every other floor), a
        // cornice round every tier, a shop front of one entry bay and shop bays with blank signs under one awning, and on
        // the roof a water tank, an antenna and a plant room, or three plant boxes, behind a railing. The tiers' bodies,
        // the plant room and the awning stay boxes. Everything varies by index (user 2026-10-30: the same boxes repeated
        // read as a ghost town).
        public static Transform Tower(Kit kit, Transform parent, string name, Vector3 position, Quaternion facing, int bays, float depth, int floors, int index, in Colours colours)
        {
            int tiers = 1 + (int)(Hash(index * 7 + 3) * 3f);
            if (bays < 2 || floors < tiers)
                throw new ArgumentException($"A city tower needs at least 2 bays and a floor per tier ({bays} bays, {floors} floors, {tiers} tiers).");
            Transform tower = Group(name, parent);
            tower.SetPositionAndRotation(position, facing);
            bool columns = Hash(index * 11 + 4) > 0.5f;
            bool balconies = !columns && Hash(index * 13 + 5) > 0.45f;
            float y = 0f, d = depth, w = 0f;
            int floor = 0, tierBays = bays;
            for (int t = 0; t < tiers; t++)
            {
                if (t > 0) d *= 0.8f;
                tierBays = Mathf.Max(2, bays - t);
                w = Width(tierBays);
                int tierFloors = floors / tiers + (t < floors % tiers ? 1 : 0);
                float floorsFrom = t == 0 ? GroundStorey : y, top = floorsFrom + tierFloors * UpperStorey;
                Box("Tier", tower, new Vector3(0f, (y + top) * 0.5f, 0f), new Vector3(w, top - y, d), colours.Facade);
                TierCornice(kit, tower, w, d, top, colours.Trim);
                // 1 cm off the wall so the bays' backs do not flicker against it.
                float face = d * 0.5f + 0.01f;
                for (int f = 0; f < tierFloors; f++, floor++)
                {
                    string kind = columns ? "TowerColumn" : balconies && floor % 2 == 0 ? "TowerBalcony" : "TowerBand";
                    float at = floorsFrom + (f + 0.5f) * UpperStorey;
                    for (int bay = 0; bay < tierBays; bay++) Bay(kit, kind, tower, new Vector3(BayX(bay, tierBays), at, face), colours);
                }
                y = top;
            }
            // The shop front: one entry bay, the rest shop bays with a blank sign each, one awning over them all.
            float shopFace = depth * 0.5f + 0.01f;
            int entry = Mathf.Min(bays - 1, (int)(Hash(index * 29 + 10) * bays));
            for (int bay = 0; bay < bays; bay++)
                Bay(kit, bay == entry ? "TowerEntry" : "TowerShop", tower, new Vector3(BayX(bay, bays), GroundStorey * 0.5f, shopFace), colours);
            Box("Shop awning", tower, new Vector3(0f, 3.7f, depth * 0.5f + 1f), new Vector3(bays * BayWidth, 0.25f, 2f), colours.Awning);
            // Roof clutter on the top tier (its own width and depth), standing on the roof by its bounds' bottom.
            float roof = y;
            float r = Hash(index * 23 + 8);
            if (r < 0.4f)
                Piece("Water tank", tower, new Vector3(w * 0.2f, roof + kit["WaterTank"].bounds.extents.y, -d * 0.1f), colours.Metal, kit["WaterTank"]);
            else if (r < 0.7f)
            {
                Piece("Antenna", tower, new Vector3(-w * 0.2f, roof + kit["Antenna"].bounds.extents.y, 0f), colours.Metal, kit["Antenna"]);
                Box("Roof plant room", tower, new Vector3(w * 0.15f, roof + 1.2f, 0f), new Vector3(w * 0.35f, 2.4f, d * 0.4f), colours.Trim);
            }
            else
            {
                for (int box = 0; box < 3; box++)
                    Piece("Roof unit", tower, new Vector3(-w * 0.3f + box * w * 0.3f, roof + kit["RoofUnit"].bounds.extents.y, d * 0.15f), colours.RoofUnit, kit["RoofUnit"]);
            }
            // A railing bay over every façade bay, on the roof's front edge.
            Mesh rail = kit["RoofRail"];
            for (int bay = 0; bay < tierBays; bay++)
                Piece("Roof railing", tower, new Vector3(BayX(bay, tierBays), roof + rail.bounds.extents.y, d * 0.5f - rail.bounds.extents.z), colours.Metal, rail);
            return tower;
        }

        // Every slot object of one kind of tower bay at the bay's origin: trim, glass (the shop front's dark), the shop's
        // sign and the balcony's metal railing.
        private static void Bay(Kit kit, string kind, Transform tower, Vector3 at, in Colours colours)
        {
            bool shopFront = kind == "TowerShop" || kind == "TowerEntry";
            foreach (string slot in BaySlots)
                if (kit.TryGet(kind + "_" + slot, out Mesh mesh))
                {
                    Material material = slot == "Trim" ? colours.Trim : slot == "Glass" ? (shopFront ? colours.ShopGlass : colours.Glass) : slot == "Metal" ? colours.Metal : colours.Sign;
                    Piece(kind + " " + slot, tower, at, material, mesh);
                }
        }

        // A cornice round a tier just under its top, its back on the wall. The front and back pieces run past the sides by
        // the cornice's depth and the side pieces fit between them, so no two pieces share a face. The kit's cornice faces
        // +Z, so each piece is turned to face out. SKYWAY's skyline towers use it too (docs/ART_REQUEST_PROPS.md K10).
        public static void TierCornice(Kit kit, Transform tower, float w, float d, float top, Material trim)
        {
            Mesh cornice = kit["TowerCornice"];
            float height = cornice.bounds.size.y, at = top - height * 0.5f, half = CorniceDepth * 0.5f;
            for (int side = -1; side <= 1; side += 2)
            {
                Shape("Tier cornice", tower, new Vector3(0f, at, side * (d * 0.5f + half)), new Vector3(w + CorniceDepth * 2f, height, CorniceDepth), trim, cornice)
                    .transform.localRotation = Quaternion.Euler(0f, side > 0 ? 0f : 180f, 0f);
                Shape("Tier cornice", tower, new Vector3(side * (w * 0.5f + half), at, 0f), new Vector3(d, height, CorniceDepth), trim, cornice)
                    .transform.localRotation = Quaternion.Euler(0f, side * 90f, 0f);
            }
        }
    }
}

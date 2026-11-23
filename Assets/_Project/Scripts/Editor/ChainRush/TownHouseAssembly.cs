using System;
using System.Collections.Generic;
using UnityEngine;
using static ProtoHarness.Editor.ChainRush.ExteriorKit;

namespace ProtoHarness.Editor.ChainRush
{
    // The town house kit's façade (docs/ART_REQUEST_PROPS.md K8), shared by the tracks whose villages are built of it
    // (PALM COAST, FINGER LANES, SKYWAY; user 2026-11-20 "진행하라"). Each track keeps its own body, roof, colours and
    // extras; this lays the kit's parts on them: the gable, the chimney, the cornice, the floor bands, the bays and their
    // flower boxes. Façade bays are 2.6 m wide, 3.6 m on the ground floor and 3.2 m above, the house 0.3 m wider than its
    // bays. A bay's slot objects share the bay's origin: the middle of its width and height, on the wall's face, the bay
    // standing out in front (+Z).
    public static class TownHouseAssembly
    {
        public const string KitName = "TownHouseKit";
        public const float BayWidth = 2.6f, GroundStorey = 3.6f, UpperStorey = 3.2f, HouseMargin = 0.3f;
        private static readonly string[] BaySlots = { "Trim", "Glass", "Accent", "Metal" };
        private static readonly string[] BayMeshes =
        {
            "UpperShutter_Trim", "UpperShutter_Glass", "UpperShutter_Accent", "UpperTall_Trim", "UpperTall_Glass", "UpperTall_Metal",
            "UpperPair_Trim", "UpperPair_Glass", "GroundDoor_Trim", "GroundDoor_Accent", "GroundShop_Trim", "GroundShop_Glass",
            "GroundWindow_Trim", "GroundWindow_Glass", "GroundWindow_Accent",
        };
        private static readonly string[] UpperBays = { "UpperShutter", "UpperTall", "UpperPair" };

        public static float Width(int bays) => bays * BayWidth + HouseMargin;

        public static float Height(int storeys) => GroundStorey + (storeys - 1) * UpperStorey;

        // The colours a track gives a house's façade: trim round the windows and the bands, the glass, the shutters, the
        // door, the railings, the flower boxes and the flowers to pick from.
        public readonly struct Colours
        {
            public readonly Material Trim, Glass, Shutters, Door, Metal, Wood;
            public readonly Material[] Flowers;

            public Colours(Material trim, Material glass, Material shutters, Material door, Material metal, Material wood, params Material[] flowers)
            {
                if (trim == null || glass == null || shutters == null || door == null || metal == null || wood == null)
                    throw new ArgumentException("Every façade colour is required.");
                if (flowers == null || flowers.Length == 0) throw new ArgumentException("At least one flower colour is required.", nameof(flowers));
                Trim = trim; Glass = glass; Shutters = shutters; Door = door; Metal = metal; Wood = wood; Flowers = flowers;
            }
        }

        // The kit's meshes, loaded once per build and checked where their origins lie: a re-export with another pivot
        // would stand every bay off the wall or the floor, and the gable, bands and chimney are placed by their bounds
        // centre.
        public sealed class Kit
        {
            private readonly Dictionary<string, Mesh> meshes = new Dictionary<string, Mesh>();

            public Mesh this[string name] => meshes[name];

            public bool TryGet(string name, out Mesh mesh) => meshes.TryGetValue(name, out mesh);

            public static Kit Load()
            {
                var kit = new Kit();
                foreach (string name in BayMeshes)
                {
                    Mesh mesh = KitPiece(KitName, name);
                    float storey = name.StartsWith("Ground", StringComparison.Ordinal) ? GroundStorey : UpperStorey;
                    Bounds b = mesh.bounds;
                    // 0.05 m below: the door's step reaches 0.015 under its bay into the plinth.
                    if (b.min.z < -0.01f || b.max.z > 0.5f || b.min.x < -BayWidth * 0.5f || b.max.x > BayWidth * 0.5f
                        || b.min.y < -storey * 0.5f - 0.05f || b.max.y > storey * 0.5f)
                        throw new InvalidOperationException($"Kit mesh {name} in {KitName} has bounds {b}, outside its {BayWidth} x {storey} m bay from an origin on the wall's face; export it with the bay's pivot.");
                    kit.meshes[name] = mesh;
                }
                // The flower box and its flowers share one origin, the middle of the box's depth.
                foreach (string name in new[] { "FlowerBox", "Flowers" })
                {
                    Mesh mesh = KitPiece(KitName, name);
                    if (Mathf.Abs(mesh.bounds.center.x) > 0.05f || Mathf.Abs(mesh.bounds.center.z) > 0.05f)
                        throw new InvalidOperationException($"Kit mesh {name} in {KitName} has its origin off the box's middle ({mesh.bounds.center}).");
                    kit.meshes[name] = mesh;
                }
                foreach (string name in new[] { "Cornice", "FloorBand", "GableEnd", "Chimney" }) kit.meshes[name] = KitMesh(KitName, name);
                return kit;
            }
        }

        // The gable under a pitched roof of this rise: its slopes run along the middles of the roof panels, which cover them.
        public static void Gable(Kit kit, Transform house, float width, float depth, float height, float rise, Material walls)
            => Shape("Gable", house, new Vector3(0f, height + rise * 0.5f, 0f), new Vector3(width, rise, depth), walls, kit["GableEnd"]);

        // A chimney halfway down one slope (side -1 or 1), toward the back: there the roof is rise / 2 up, and 0.35 m above
        // that the 2 m chimney is buried 0.65 m.
        public static void Chimney(Kit kit, Transform house, float width, float depth, float height, float rise, float side, Material stone)
            => Piece("Chimney", house, new Vector3(side * width * 0.25f, height + rise * 0.5f + 0.35f, -depth * 0.2f), stone, kit["Chimney"]);

        // The front (local +Z) of a house of this many bays and storeys: a cornice under the roof, on some a band between
        // floors, on the ground floor a door bay and shop or shuttered-window bays, above one kind of bay per floor
        // (shuttered, tall with a railing, or a narrow pair), and flower boxes under the shuttered windows. Varies by index.
        public static void Facade(Kit kit, Transform house, int bays, float depth, int storeys, int index, in Colours colours)
        {
            if (bays < 1) throw new ArgumentException("A house has at least one bay.", nameof(bays));
            if (storeys < 2) throw new ArgumentException("A house has at least two storeys.", nameof(storeys));
            float width = Width(bays);
            float height = Height(storeys);
            float wall = depth * 0.5f;
            // The cornice's and the band's backs (z -0.25, -0.06 from their centres) on the wall.
            Shape("Cornice", house, new Vector3(0f, height - 0.2f, wall + 0.25f), new Vector3(width + 0.2f, 0.4f, 0.5f), colours.Trim, kit["Cornice"]);
            if (Hash(index * 59 + 20) > 0.5f)
                for (int floor = 1; floor < storeys; floor++)
                    Shape("Floor band", house, new Vector3(0f, GroundStorey + (floor - 1) * UpperStorey, wall + 0.06f), new Vector3(width, 0.2f, 0.12f), colours.Trim, kit["FloorBand"]);
            // 1 cm off the wall so the bays' backs do not flicker against it.
            float face = wall + 0.01f;
            int door = Mathf.Min(bays - 1, (int)(Hash(index * 61 + 21) * bays));
            string ground = Hash(index * 67 + 22) < 0.5f ? "GroundShop" : "GroundWindow";
            for (int bay = 0; bay < bays; bay++)
            {
                var at = new Vector3((bay - (bays - 1) * 0.5f) * BayWidth, GroundStorey * 0.5f, face);
                string kind = bay == door ? "GroundDoor" : ground;
                Bay(kit, kind, house, at, colours, bay == door ? colours.Door : colours.Shutters);
                if (kind == "GroundWindow") FlowerBox(kit, house, at, kind, colours.Wood, Pick(index * 31 + bay + 14, colours.Flowers));
            }
            for (int floor = 1; floor < storeys; floor++)
            {
                string kind = UpperBays[Mathf.Min(UpperBays.Length - 1, (int)(Hash(index * 71 + floor * 13 + 23) * UpperBays.Length))];
                float y = GroundStorey + (floor - 1) * UpperStorey + UpperStorey * 0.5f;
                for (int bay = 0; bay < bays; bay++)
                {
                    var at = new Vector3((bay - (bays - 1) * 0.5f) * BayWidth, y, face);
                    Bay(kit, kind, house, at, colours, colours.Shutters);
                    if (kind == "UpperShutter") FlowerBox(kit, house, at, kind, colours.Wood, Pick(index * 31 + floor * 7 + bay + 14, colours.Flowers));
                }
            }
        }

        // Every slot object of one kind of bay at the bay's origin: trim, glass, accent (shutters or the door) and metal.
        private static void Bay(Kit kit, string kind, Transform house, Vector3 at, in Colours colours, Material accent)
        {
            foreach (string slot in BaySlots)
                if (kit.TryGet(kind + "_" + slot, out Mesh mesh))
                    Piece(kind + " " + slot, house, at, slot == "Trim" ? colours.Trim : slot == "Glass" ? colours.Glass : slot == "Accent" ? accent : colours.Metal, mesh);
        }

        // A flower box under the window of a bay, its top 5 mm under the sill and its back 0.02 m off the wall.
        private static void FlowerBox(Kit kit, Transform house, Vector3 at, string kind, Material wood, Material flowers)
        {
            Mesh box = kit["FlowerBox"];
            float top = kit[kind + "_Trim"].bounds.min.y - 0.005f;
            var place = new Vector3(at.x, at.y + top - box.bounds.max.y, at.z + 0.02f - box.bounds.min.z);
            Piece("Flower box", house, place, wood, box);
            Piece("Flowers", house, place, flowers, kit["Flowers"]);
        }
    }
}

using System.Collections.Generic;
using UnityEngine;

namespace Entregador.Editor
{
    /// <summary>Quarteirões: calçada, miolo, colliders, fileiras de lotes (sobrados, comércio, prédios, restaurantes) e o largo.</summary>
    public static partial class CityBuilder
    {
        private static readonly string[] RestaurantNames = { "Pastelaria do Largo", "Pizzaria Bella Amaro", "Marmitaria da Dona Cida", "Hamburgueria Zona Sul" };

        /// <summary>Uma borda de quarteirão voltada para a rua (as quatro fileiras de fachadas).</summary>
        private sealed class Edge
        {
            public Vector3 Start;
            public Vector3 Along;
            public Vector3 Facing;
            public float Length;
            public int Block;
            public int Key;
            public bool Horizontal;
            public bool Commercial;
            public bool AvenueRow;
            public bool Lite;
        }

        private static readonly List<Edge> Edges = new List<Edge>();
        private static readonly Dictionary<int, MeshBatch> BlockBatches = new Dictionary<int, MeshBatch>();
        private static Transform _signsRoot;
        private static Transform _detailsRoot;
        private static bool _borrachariaDone;
        public static readonly int[] KindStats = new int[6];
        public static readonly int[] KindCount = new int[6];

        private static bool IsCommercialBlock(int i, int j) =>
            i == AvenueIndex - 1 || i == AvenueIndex || (j >= LargoBlockZ - 1 && j <= LargoBlockZ + 1 && i >= AvenueIndex - 2 && i <= AvenueIndex + 1);

        private static void BuildBlocks(Transform blocks, Transform buildings, Transform props, Materials m, System.Random random, List<EdgeSpot> restaurantSpots, out Vector3 largoCenter)
        {
            largoCenter = Vector3.zero;
            Edges.Clear();
            BlockBatches.Clear();
            _borrachariaDone = false;
            _signsRoot = Group(buildings, "Letreiros");
            _detailsRoot = props;
            _blocksRoot = blocks;

            for (int i = 0; i < BlockCount; i++)
            {
                for (int j = 0; j < BlockCount; j++)
                {
                    float x0 = BlockX0(i);
                    float x1 = BlockX1(i);
                    float z0 = BlockZ0(j);
                    float z1 = BlockZ1(j);
                    var center = new Vector3((x0 + x1) * 0.5f, 0f, (z0 + z1) * 0.5f);
                    var batch = new MeshBatch();
                    BlockBatches[i * 100 + j] = batch;

                    if (i == LargoBlockX && j == LargoBlockZ)
                    {
                        largoCenter = center;
                        BuildLargo(blocks, props, m, batch, x0, x1, z0, z1);

                        continue;
                    }

                    bool commercialBlock = IsCommercialBlock(i, j);
                    PavementVisual(batch, x0, x1, z0, z1, CurbHeight, CityStyle.Sidewalk());
                    PavementColliders(blocks, $"Calcada_{i}_{j}", x0, x1, z0, z1, CurbHeight);

                    // Miolo (quintais ou pátio) visto de cima, escondido atrás das fileiras de prédios.
                    Floor(batch, commercialBlock ? CityStyle.Floor() : CityStyle.Grass(), x0 + 7f, z0 + 7f, x1 - 7f, z1 - 7f, CurbHeight + 0.01f);

                    // Miolo sólido: o que há atrás da frente dos prédios. Um collider por quarteirão.
                    var solid = new GameObject($"Predios_{i}_{j}");
                    solid.transform.SetParent(blocks, false);
                    solid.transform.position = new Vector3(center.x, CurbHeight + 4f, center.z);
                    solid.AddComponent<BoxCollider>().size = new Vector3(x1 - x0 - 2f * FrontInset, 8f, z1 - z0 - 2f * FrontInset);

                    bool lite = Mathf.Abs(i - LargoBlockX) > 2 || Mathf.Abs(j - LargoBlockZ) > 1;
                    FillInterior(batch, new System.Random(5000 + i * 17 + j), x0, x1, z0, z1);
                    var context = new BuildingContext { B = batch, Rnd = new System.Random(1000 + i * 31 + j), Signs = _signsRoot, Lite = lite };
                    float sideLength = z1 - z0 - 2f * FrontInset - 2f * RowDepth;
                    float rowLength = x1 - x0 - 2f * FrontInset;

                    // Quatro fileiras de fachadas voltadas para as ruas.
                    var edges = new[]
                    {
                        MakeEdge(i, j, true, 1, new Vector3(x0 + FrontInset, 0f, z0 + FrontInset), Vector3.right, Vector3.back, rowLength, commercialBlock, false, j, 1),
                        MakeEdge(i, j, true, -1, new Vector3(x1 - FrontInset, 0f, z1 - FrontInset), Vector3.left, Vector3.forward, rowLength, commercialBlock, false, j + 1, -1),
                        MakeEdge(i, j, false, 1, new Vector3(x0 + FrontInset, 0f, z0 + FrontInset + RowDepth), Vector3.forward, Vector3.left, sideLength, commercialBlock, i == AvenueIndex, i, 1),
                        MakeEdge(i, j, false, -1, new Vector3(x1 - FrontInset, 0f, z1 - FrontInset - RowDepth), Vector3.back, Vector3.right, sideLength, commercialBlock, i + 1 == AvenueIndex, i + 1, -1),
                    };
                    int[] restaurant =
                    {
                        FindRestaurant(restaurantSpots, true, j, i, 1),
                        FindRestaurant(restaurantSpots, true, j + 1, i, -1),
                        FindRestaurant(restaurantSpots, false, i, j, 1),
                        FindRestaurant(restaurantSpots, false, i + 1, j, -1),
                    };

                    for (int k = 0; k < 4; k++)
                    {
                        edges[k].Lite = lite;
                        Edges.Add(edges[k]);
                        FillRow(context, edges[k], restaurant[k], m);
                    }
                }
            }
        }

        /// <summary>Fundos do quarteirão: casinhas e galpões baixos com laje, para o miolo nunca parecer um vazio visto de cima.</summary>
        private static void FillInterior(MeshBatch b, System.Random r, float x0, float x1, float z0, float z1)
        {
            float inset = FrontInset + RowDepth + 0.5f;
            const float cell = 14f;

            for (float x = x0 + inset; x < x1 - inset - 6f; x += cell)
            {
                for (float z = z0 + inset; z < z1 - inset - 6f; z += cell)
                {
                    if (r.NextDouble() < 0.12)
                    {
                        continue;
                    }

                    float w = Mathf.Min(cell - 1.5f, x1 - inset - x);
                    float d = Mathf.Min(cell - 1.5f, z1 - inset - z);
                    float h = 4f + 5f * (float)r.NextDouble();
                    b.AxisBox(CityStyle.Plaster(r), new Vector3(x + w * 0.5f, CurbHeight + h * 0.5f, z + d * 0.5f), new Vector3(w, h, d));
                }
            }
        }

        private static Edge MakeEdge(int i, int j, bool horizontal, int side, Vector3 start, Vector3 along, Vector3 facing, float length, bool commercial, bool avenueRow, int street, int sideKey)
        {
            return new Edge
            {
                Start = start,
                Along = along,
                Facing = facing,
                Length = length,
                Block = i * 100 + j,
                Key = (horizontal ? 0 : 1) * 1000 + street * 10 + (side > 0 ? 1 : 0),
                Horizontal = horizontal,
                Commercial = commercial,
                AvenueRow = avenueRow,
            };
        }

        /// <summary>Índice do restaurante cujo ponto está nesta borda do quarteirão, ou -1.</summary>
        private static int FindRestaurant(List<EdgeSpot> spots, bool horizontal, int street, int segment, int side)
        {
            for (int k = 0; k < spots.Count; k++)
            {
                EdgeSpot s = spots[k];

                if (s.Horizontal == horizontal && s.Street == street && s.Segment == segment && s.Side == side)
                {
                    return k;
                }
            }

            return -1;
        }

        /// <summary>Preenche a borda com lotes; um restaurante (índice >= 0) ocupa o meio com prédio próprio.</summary>
        private static void FillRow(BuildingContext context, Edge e, int restaurant, Materials m)
        {
            if (restaurant < 0)
            {
                FillSegment(context, e, 0f, e.Length);

                return;
            }

            const float slot = 26f;
            float half = (e.Length - slot) * 0.5f;
            FillSegment(context, e, 0f, half);
            FillSegment(context, e, half + slot, half);
            BuildRestaurantFront(context, e.Start + e.Along * (half + slot * 0.5f), e.Facing, restaurant, m);
        }

        private static void FillSegment(BuildingContext context, Edge e, float offset, float length)
        {
            System.Random r = context.Rnd;
            Vector3 right = Vector3.Cross(e.Facing, Vector3.up);
            bool alongIsRight = Vector3.Dot(e.Along, right) > 0f;
            var lots = new List<(float width, int kind)>();
            float remaining = length;

            while (remaining > 0.5f)
            {
                int kind;
                float w;

                if (!e.Commercial)
                {
                    kind = 0;
                    w = 9f + 5f * (float)r.NextDouble();
                }
                else if (e.AvenueRow && remaining >= 26f && r.NextDouble() < 0.34)
                {
                    kind = 2;
                    w = 19f + 7f * (float)r.NextDouble();
                }
                else
                {
                    kind = 1;
                    w = 8f + 6f * (float)r.NextDouble();
                }

                if (remaining - w < 7f)
                {
                    w = remaining;
                }

                if (kind == 2 && w > 32f)
                {
                    kind = 1;
                }

                if (kind != 2 && w > 15f)
                {
                    lots.Add((w * 0.5f, kind));
                    lots.Add((w * 0.5f, kind));
                }
                else
                {
                    lots.Add((w, kind));
                }

                remaining -= w;
            }

            float cursor = 0f;

            for (int index = 0; index < lots.Count; index++)
            {
                (float w, int kind) = lots[index];
                Vector3 o = e.Start + e.Along * (offset + (alongIsRight ? cursor : cursor + w));
                o.y = CurbHeight;
                var frame = new Frame(o, e.Facing);
                cursor += w;
                context.ExposeLeft = alongIsRight ? index == 0 : index == lots.Count - 1;
                context.ExposeRight = alongIsRight ? index == lots.Count - 1 : index == 0;

                int before = context.B.TriangleCount;
                if (kind == 0)
                {
                    CityBuildings.Sobrado(context, frame, w, RowDepth - 0.5f);
                }
                else if (kind == 1)
                {
                    CityBuildings.Shop shop = CityBuildings.RandomShop(r, e.AvenueRow);
                    int floors = 2 + r.Next(3);

                    if (!_borrachariaDone && e.Commercial && e.AvenueRow && e.Block / 100 == AvenueIndex - 1 && e.Block % 100 == LargoBlockZ - 1 && index >= 1)
                    {
                        shop = new CityBuildings.Shop { Name = "Borracharia 24h", Board = 3, Kind = 0, Awning = false, Tires = true };
                        _borrachariaDone = true;
                        var marker = new GameObject("Borracharia");
                        marker.transform.SetParent(_detailsRoot, false);
                        marker.transform.position = frame.Pt(w * 0.5f, 1.5f, 0f);
                        marker.transform.rotation = Quaternion.LookRotation(frame.F, Vector3.up);
                    }

                    CityBuildings.Commerce(context, frame, w, RowDepth - 1f, shop, floors);
                }
                else
                {
                    CityBuildings.Tower(context, frame, w, RowDepth - 1f, CityBuildings.RandomShop(r, true), 6 + r.Next(7));
                }

                KindStats[kind * 2 + (context.Lite ? 1 : 0)] += context.B.TriangleCount - before;
                KindCount[kind * 2 + (context.Lite ? 1 : 0)]++;
            }
        }

        /// <summary>Restaurante: prédio largo recuado do meio-fio (terraço de 3 m) com toldos, porta aberta, letreiro e mesas.</summary>
        private static void BuildRestaurantFront(BuildingContext context, Vector3 slotCenterOnFrontLine, Vector3 facing, int index, Materials m)
        {
            const float terrace = 3f;
            const float width = 24f;
            Vector3 right = Vector3.Cross(facing, Vector3.up).normalized;
            Vector3 corner = slotCenterOnFrontLine - facing * terrace - right * (width * 0.5f);
            corner.y = CurbHeight;
            Color accent = m.Restaurants[index % m.Restaurants.Length].GetColor("_BaseColor");
            CityBuildings.Restaurant(context, new Frame(corner, facing), width, RowDepth - terrace - 0.5f, RestaurantNames[index], index, accent);
        }

        // ---------- Largo ----------

        private static void BuildLargo(Transform blocks, Transform props, Materials m, MeshBatch b, float x0, float x1, float z0, float z1)
        {
            var center = new Vector3((x0 + x1) * 0.5f, 0f, (z0 + z1) * 0.5f);
            const float height = 0.1f;
            PavementVisual(b, x0, x1, z0, z1, height, CityStyle.Paving());
            PavementColliders(blocks, "LargoPiso", x0, x1, z0, z1, height);

            // Fonte de concreto com água.
            Material concrete = CityStyle.ConcreteWall();
            Vector3 c0 = center + Vector3.up * height;
            b.Frustum(concrete, c0, 4.2f, 4.0f, 0.9f, 20, false);
            b.Frustum(concrete, c0, 3.7f, 3.7f, 0.82f, 20, false);
            b.Fan(CityStyle.Water(), c0 + Vector3.up * 0.8f, Fill(20, 3.7f));
            b.Frustum(concrete, c0, 0.7f, 0.45f, 2.4f, 12, false);
            b.Frustum(concrete, c0 + Vector3.up * 2.4f, 0.4f, 1.5f, 0.45f, 16, true);
            b.Fan(CityStyle.Water(), c0 + Vector3.up * 2.82f, Fill(16, 1.3f));

            var fountain = new GameObject("LargoFonte");
            fountain.transform.SetParent(props, false);
            fountain.transform.position = center + Vector3.up * 0.6f;
            fountain.AddComponent<BoxCollider>().size = new Vector3(7.6f, 1.2f, 7.6f);

            // Bancos de concreto e madeira em volta, voltados para a fonte.
            Material wood = PbrMaterials.Flat("MadeiraBanco", new Color(0.4f, 0.26f, 0.15f), 0.3f);

            for (int k = 0; k < 8; k++)
            {
                float angle = k * 45f + 22.5f;
                var direction = new Vector3(Mathf.Cos(angle * Mathf.Deg2Rad), 0f, Mathf.Sin(angle * Mathf.Deg2Rad));
                Vector3 p = center + direction * 14f;
                Vector3 facing = -direction;
                var frame = new Frame(p - Vector3.Cross(facing, Vector3.up) * 1f + Vector3.up * height, facing);
                b.Box(wood, frame.Pt(0f, 0.42f, 0f), frame.F, 2f, 0.07f, 0.5f);
                b.Box(wood, frame.Pt(0f, 0.7f, 0.5f), frame.F, 2f, 0.3f, 0.06f);
                b.Box(concrete, frame.Pt(0.1f, 0f, 0.05f), frame.F, 0.1f, 0.42f, 0.4f);
                b.Box(concrete, frame.Pt(1.8f, 0f, 0.05f), frame.F, 0.1f, 0.42f, 0.4f);
            }

            // Árvores em volta, longe das ruas e do caminho do meio.
            var spots = new[]
            {
                new Vector2(-32f, -32f), new Vector2(32f, -32f), new Vector2(-32f, 32f), new Vector2(32f, 32f),
                new Vector2(0f, 36f), new Vector2(0f, -36f), new Vector2(-36f, 0f), new Vector2(36f, 0f),
                new Vector2(-50f, -20f), new Vector2(-50f, 20f), new Vector2(50f, -20f), new Vector2(50f, 20f),
                new Vector2(-20f, 50f), new Vector2(20f, 50f), new Vector2(-20f, -50f), new Vector2(20f, -50f),
            };

            for (int k = 0; k < spots.Length; k++)
            {
                Vector3 position = new Vector3(center.x + spots[k].x, height, center.z + spots[k].y);
                Tree(b, position, 8f + (k % 3), 900 + k, true);
                var trunk = new GameObject("LargoArvoreTronco" + k);
                trunk.transform.SetParent(props, false);
                trunk.transform.position = position + Vector3.up * 1.5f;
                var capsule = trunk.AddComponent<CapsuleCollider>();
                capsule.radius = 0.25f;
                capsule.height = 3f;
            }

            // Postes de luz do largo.
            foreach (var s in new[] { new Vector2(-20f, -20f), new Vector2(20f, -20f), new Vector2(-20f, 20f), new Vector2(20f, 20f) })
            {
                Vector3 p = center + new Vector3(s.x, height, s.y);
                StreetLamp(b, p, new Vector3(-s.x, 0f, -s.y).normalized);
                var pole = new GameObject("LargoPoste");
                pole.transform.SetParent(props, false);
                pole.transform.position = p + Vector3.up * 1.5f;
                var capsule = pole.AddComponent<CapsuleCollider>();
                capsule.radius = 0.2f;
                capsule.height = 3f;
            }
        }

        private static float[] Fill(int count, float radius)
        {
            var radii = new float[count];

            for (int i = 0; i < count; i++)
            {
                radii[i] = radius;
            }

            return radii;
        }
    }
}

using Entregador.Game;
using System.Collections.Generic;
using UnityEngine;

namespace Entregador.Editor
{
    /// <summary>
    /// Mobiliário urbano de São Paulo: postes de concreto com fios, luminárias, lixeiras laranja, pontos de ônibus, bueiros,
    /// árvores, semáforos, placas de PARE, caçambas, lombadas, carros parados e o trânsito. Tudo vira malha fundida por pedaço do mapa.
    /// </summary>
    public static partial class CityBuilder
    {
        /// <summary>Carros parados: Uno e Onix reais (em geral) e, de vez em quando, uma van do Kenney (não temos van real).</summary>
        private static readonly string[] ParkedKenneyVans = { "van", "delivery" };

        private sealed class PoleInfo
        {
            public Vector3 Pos;
            public int Key;
            public float Axis;
            public MeshBatch Batch;
            public Vector3 Facing;
        }

        private static readonly List<PoleInfo> Poles = new List<PoleInfo>();

        /// <summary>Posições das lombadas (carros parados e mobiliário as evitam).</summary>
        private static readonly List<Vector3> BumpPositions = new List<Vector3>();

        private static Transform _signalRoot;

        // ---------- Peças ----------

        private static bool NearAny(Vector3 position, List<Vector3> points, float distance)
        {
            foreach (Vector3 other in points)
            {
                Vector3 d = position - other;
                d.y = 0f;

                if (d.magnitude < distance)
                {
                    return true;
                }
            }

            return false;
        }

        private static void SolidCapsule(Transform parent, string name, Vector3 groundPosition, float radius, float height)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = groundPosition + Vector3.up * (height * 0.5f);
            var capsule = go.AddComponent<CapsuleCollider>();
            capsule.radius = radius;
            capsule.height = height;
        }

        /// <summary>Árvore de copa densa: tronco afilado e 5 blobs irregulares com folhagem texturizada.</summary>
        private static void Tree(MeshBatch b, Vector3 basePosition, float height, int seed, bool large, bool lite = false)
        {
            var r = new System.Random(seed);
            float trunkHeight = height * 0.42f;
            b.Frustum(CityStyle.Bark(), basePosition, 0.17f * height / 6f, 0.09f * height / 6f, trunkHeight, 6, false);
            float crown = height * (large ? 0.3f : 0.25f);
            Material leaves = CityStyle.Leaves(seed);
            Vector3 center = basePosition + Vector3.up * (trunkHeight + crown * 0.55f);
            b.Blob(leaves, center, new Vector3(crown, crown * 0.85f, crown), seed * 7 + 1, true);

            for (int k = 0; k < (lite ? 2 : 3); k++)
            {
                float a = (k * 90f + r.Next(0, 50)) * Mathf.Deg2Rad;
                Vector3 offset = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * crown * 0.65f + Vector3.up * crown * (0.1f + 0.35f * (float)r.NextDouble());
                b.Blob(leaves, center + offset, new Vector3(crown * 0.7f, crown * 0.62f, crown * 0.7f), seed * 7 + 2 + k, true);
            }
        }

        /// <summary>Braço da luminária no topo de um poste, saindo para a rua, com a cabeça de luz.</summary>
        private static void LampArm(MeshBatch b, Vector3 top, Vector3 toward)
        {
            Vector3 tip = top + toward * 2.2f + Vector3.up * 0.25f;
            b.Wire(CityStyle.PoleConcrete(), top, tip, 0.1f);
            var frame = new Frame(tip - Vector3.Cross(toward, Vector3.up) * 0.16f - Vector3.up * 0.12f, toward);
            b.Box(CityStyle.AcUnit(), frame.O, toward, 0.32f, 0.12f, 0.7f);
        }

        private static void StreetLamp(MeshBatch b, Vector3 basePosition, Vector3 toward)
        {
            b.Frustum(CityStyle.PoleConcrete(), basePosition, 0.1f, 0.06f, 7.6f, 8, true);
            LampArm(b, basePosition + Vector3.up * 7.5f, toward);
        }

        /// <summary>Poste de concreto com duas cruzetas, luminária e, às vezes, transformador.</summary>
        private static void UtilityPole(MeshBatch b, Vector3 p, Vector3 facing, System.Random r)
        {
            b.Frustum(CityStyle.PoleConcrete(), p, 0.17f, 0.1f, 9.3f, 6, false);
            Material wood = CityStyle.Bark();
            b.Wire(wood, p + Vector3.up * 8.5f - facing * 0.95f, p + Vector3.up * 8.5f + facing * 0.95f, 0.13f);
            b.Wire(wood, p + Vector3.up * 7.7f - facing * 0.65f, p + Vector3.up * 7.7f + facing * 0.65f, 0.11f);

            foreach (float lateral in new[] { -0.8f, 0f, 0.8f })
            {
                b.Frustum(CityStyle.Aluminum(), p + Vector3.up * 8.55f + facing * lateral, 0.04f, 0.03f, 0.14f, 5, false);
            }

            if (r.NextDouble() < 0.22)
            {
                b.Frustum(PbrMaterials.Flat("Transformador", new Color(0.45f, 0.47f, 0.48f), 0.4f, 0.5f), p + Vector3.up * 6.3f + facing * 0.32f, 0.27f, 0.27f, 0.8f, 8, true);
            }

            if (r.NextDouble() < 0.65)
            {
                LampArm(b, p + Vector3.up * 8.9f, facing);
            }
        }

        private static void Wires(MeshBatch b, Vector3 a, Vector3 c, float sag, int segments, float thickness, Vector3 mid)
        {
            Material wire = CityStyle.Wire();
            Vector3 previous = a;

            for (int k = 1; k <= segments; k++)
            {
                float t = k / (float)segments;
                Vector3 p = Vector3.Lerp(a, c, t) + Vector3.down * (sag * 4f * t * (1f - t)) + mid * (4f * t * (1f - t));
                b.Wire(wire, previous, p, thickness);
                previous = p;
            }
        }

        /// <summary>Fios entre dois postes da mesma linha: cinco cabos nas cruzetas, o feixe de telecom e cabos soltos pendurados.</summary>
        private static void SpanWires(PoleInfo a, PoleInfo c, System.Random r)
        {
            float length = Vector3.Distance(a.Pos, c.Pos);
            float sag = 0.028f * length;
            MeshBatch b = a.Batch;
            Vector3 lat = a.Facing;
            var offsets = new[] { (8.5f, -0.8f), (8.5f, 0.8f), (7.7f, 0f) };

            foreach ((float y, float l) in offsets)
            {
                Wires(b, a.Pos + Vector3.up * y + lat * l, c.Pos + Vector3.up * y + lat * l, sag, 4, 0.035f, Vector3.zero);
            }

            Wires(b, a.Pos + Vector3.up * 6.4f + lat * 0.3f, c.Pos + Vector3.up * 6.4f + lat * 0.3f, sag * 1.3f, 4, 0.07f, Vector3.zero);

            // Cabos soltos, emaranhados: pendem mais e saem de lado.
            int extra = 1;

            for (int k = 0; k < extra; k++)
            {
                float lateral = ((float)r.NextDouble() - 0.5f) * 1.4f;
                Wires(b, a.Pos + Vector3.up * (6.9f + 0.4f * k) + lat * 0.2f, c.Pos + Vector3.up * (6.2f + 0.5f * k) - lat * 0.2f, sag * (2.2f + 0.5f * k), 4, 0.03f, lat * lateral);
            }
        }

        private static void Bin(MeshBatch b, Vector3 p)
        {
            b.Frustum(CityStyle.IronBlack(), p, 0.03f, 0.03f, 0.95f, 5, false);
            b.Frustum(CityStyle.Orange(), p + Vector3.up * 0.8f, 0.2f, 0.27f, 0.55f, 8, true);
        }

        private static void Drain(MeshBatch b, Vector3 p, Vector3 along)
        {
            Vector3 across = Vector3.Cross(along, Vector3.up);
            var frame = new Frame(p - along * 0.45f - across * 0.2f, across);
            Vector3 right = along;
            b.Rect(CityStyle.IronBlack(), p - along * 0.45f - across * 0.2f + Vector3.up * 0.022f, along, across, 0.9f, 0.4f);

            for (int k = 0; k < 7; k++)
            {
                b.Rect(CityStyle.Aluminum(), p - along * 0.45f + along * (0.06f + k * 0.13f) - across * 0.2f + Vector3.up * 0.026f, along, across, 0.04f, 0.4f);
            }
        }

        /// <summary>Ponto de ônibus: cobertura de alumínio, painel de fundo, banco e painel de propaganda. facing aponta para a rua.</summary>
        private static void BusStop(Transform colliders, MeshBatch b, Vector3 center, Vector3 facing, int variant)
        {
            Vector3 right = Vector3.Cross(facing, Vector3.up);
            const float width = 4.2f;
            const float depth = 1.6f;
            var frame = new Frame(center - right * (width * 0.5f) + facing * (depth * 0.5f), facing);
            Material alu = CityStyle.Aluminum();
            b.Box(alu, frame.Pt(-0.1f, 2.55f, 0.3f), facing, width + 0.2f, 0.1f, depth + 0.3f);
            b.Box(alu, frame.Pt(0.1f, 0f, 0.1f), facing, 0.08f, 2.55f, 0.08f);
            b.Box(alu, frame.Pt(width - 0.18f, 0f, 0.1f), facing, 0.08f, 2.55f, 0.08f);
            b.Rect(CityStyle.Glass(), frame.Pt(0f, 0.3f, depth - 0.1f), right, Vector3.up, width, 2.25f);
            b.Rect(CityStyle.Board(variant), frame.Pt(width - 1.3f, 0.5f, depth - 0.12f), right, Vector3.up, 1.15f, 1.7f);
            b.Box(CityStyle.IronBlack(), frame.Pt(0.3f, 0.42f, depth - 0.75f), facing, 2.4f, 0.06f, 0.4f);
            b.Box(CityStyle.IronBlack(), frame.Pt(0.4f, 0f, depth - 0.7f), facing, 0.06f, 0.42f, 0.3f);
            b.Box(CityStyle.IronBlack(), frame.Pt(2.5f, 0f, depth - 0.7f), facing, 0.06f, 0.42f, 0.3f);

            var go = new GameObject("PontoDeOnibus");
            go.transform.SetParent(colliders, false);
            go.transform.position = center - facing * (depth * 0.5f - 0.35f) + Vector3.up * 1.1f;
            go.transform.rotation = Quaternion.LookRotation(facing, Vector3.up);
            go.AddComponent<BoxCollider>().size = new Vector3(width, 2.2f, 0.5f);
        }

        private static void TrafficLight(MeshBatch b, Vector3 corner, Vector3 armDirection, Vector3 headFacing, int seed)
        {
            b.Frustum(CityStyle.IronBlack(), corner, 0.1f, 0.08f, 5.2f, 8, true);
            Vector3 armStart = corner + Vector3.up * 5f;
            Vector3 armEnd = armStart + armDirection * 5.5f;
            b.Wire(CityStyle.IronBlack(), armStart, armEnd, 0.1f);
            AddSignalHead(b, armEnd + Vector3.down * 0.55f, headFacing, seed);
            AddSignalHead(b, corner + Vector3.up * 3.3f + headFacing * 0.2f, headFacing, seed + 1);
        }

        private static void AddSignalHead(MeshBatch b, Vector3 center, Vector3 facing, int seed)
        {
            var frame = new Frame(center - Vector3.Cross(facing, Vector3.up) * 0.2f - Vector3.up * 0.5f, facing);
            b.Box(CityStyle.IronBlack(), frame.O, facing, 0.4f, 1.0f, 0.3f);
            Material[] lamps =
            {
                CityMaterials.Unlit("SemaforoVermelho", new Color(1f, 0.1f, 0.05f)),
                CityMaterials.Unlit("SemaforoAmarelo", new Color(1f, 0.8f, 0.1f)),
                CityMaterials.Unlit("SemaforoVerde", new Color(0.1f, 0.95f, 0.3f)),
            };
            int lit = (seed % 3 + 3) % 3;
            Vector3 up = Vector3.up;

            for (int k = 0; k < 3; k++)
            {
                Material m = k == lit ? lamps[k] : CityStyle.Interior();
                b.Disc(m, frame.Pt(0.2f, 0.82f - k * 0.32f, -0.01f), frame.R, up, 0.11f, 8);
            }
        }

        private static void StopSign(MeshBatch b, Vector3 corner, Vector3 facing)
        {
            b.Frustum(CityStyle.IronBlack(), corner, 0.04f, 0.04f, 2.3f, 5, false);
            Vector3 right = Vector3.Cross(facing, Vector3.up);
            Vector3 center = corner + Vector3.up * 2.1f + facing * 0.06f;
            b.Disc(CityStyle.PaintWhite(), center, right, Vector3.up, 0.34f, 8);
            b.Disc(CityStyle.Board(0), center + facing * 0.005f, right, Vector3.up, 0.3f, 8);
            b.Rect(CityStyle.PaintWhite(), center + facing * 0.01f - right * 0.2f - Vector3.up * 0.04f, right, Vector3.up, 0.4f, 0.08f);
        }

        // ---------- Montagem ----------

        private delegate void EdgeVisitor(Block block, int index, Vector2 a, Vector2 dir, Vector2 front, float length, RoadClass cls);

        /// <summary>Visita as arestas de quarteirão que encaram uma rua: a é o começo, dir segue a aresta e front aponta para a rua.</summary>
        private static void ForEachEdge(EdgeVisitor visit)
        {
            foreach (Block block in Blocks)
            {
                int n = block.Poly.Count;

                for (int e = 0; e < n; e++)
                {
                    Vector2 a = block.Poly[e];
                    Vector2 b = block.Poly[(e + 1) % n];
                    float length = Vector2.Distance(a, b);

                    if (block.EdgeClass[e] == RoadClass.None || length < 3f)
                    {
                        continue;
                    }

                    Vector2 dir = (b - a) / length;
                    visit(block, e, a, dir, Geo.Right(dir), length, block.EdgeClass[e]);
                }
            }
        }

        private static void BuildProps(Transform parent, System.Random random, List<Vector3> avoid, OsmMap map)
        {
            Transform posts = Group(parent, "Postes");
            _signalRoot = Group(parent, "Placas");
            Transform cars = Group(parent, "CarrosParados");
            Transform dumpsters = Group(parent, "Cacambas");
            Transform bus = Group(parent, "PontosDeOnibus");
            Poles.Clear();
            avoid.AddRange(BumpPositions);
            var placed = new List<Vector3>();

            BuildLargoProps(parent, avoid);
            BuildSignals(map);

            // Postes, lixeiras, árvores de calçada e bueiros ao longo de cada frente de quarteirão.
            ForEachEdge((block, e, a, dir, front, length, cls) =>
            {
                var r = new System.Random(block.Label * 131 + e);
                bool commercial = cls >= RoadClass.Tertiary || cls == RoadClass.Pedestrian || Vector2.Distance(a, LargoCenter) < 160f;
                bool lite = Vector2.Distance(a, LargoCenter) > LiteRadius;
                int key = block.Label * 1000 + e;
                Vector3 front3 = Geo.V3(front);

                for (float t = 6f + (float)r.NextDouble() * 10f; t < length - 4f; t += 31f + (float)r.NextDouble() * 6f)
                {
                    Vector2 p2 = a + dir * t - front * 0.75f;
                    Vector3 p = Geo.V3(p2, CurbHeight);

                    if (NearAny(p, avoid, 6.5f) || NearAny(p, placed, 5f) || _raster.Occ[_raster.At(p2)] != 0)
                    {
                        continue;
                    }

                    MeshBatch b = BuildBatch(p);
                    UtilityPole(b, p, front3, r);
                    SolidCapsule(posts, "Poste", p, 0.2f, 3f);
                    placed.Add(p);
                    Poles.Add(new PoleInfo { Pos = p, Key = key, Axis = t, Batch = b, Facing = front3 });
                }

                if (commercial)
                {
                    for (float t = 14f + (float)r.NextDouble() * 10f; t < length - 6f; t += 38f)
                    {
                        Vector2 p2 = a + dir * t - front * 0.6f;
                        Vector3 p = Geo.V3(p2, CurbHeight);

                        if (!NearAny(p, avoid, 6f) && !NearAny(p, placed, 3.5f) && _raster.Occ[_raster.At(p2)] == 0)
                        {
                            Bin(BuildBatch(p), p);
                            placed.Add(p);
                        }
                    }
                }

                int trees = commercial ? Mathf.RoundToInt(length / 60f) : Mathf.Max(1, Mathf.RoundToInt(length / 30f));

                for (int k = 0; k < trees; k++)
                {
                    float at = length * (k + 0.5f + ((float)r.NextDouble() - 0.5f) * 0.4f) / trees;
                    Vector2 p2 = a + dir * at - front * 1.4f;
                    Vector3 p = Geo.V3(p2, CurbHeight);

                    if (_raster.DistAt(p2) > 0.7f && _raster.ZoneAt(p2) == 0 && _raster.Occ[_raster.At(p2)] == 0 && !NearAny(p, avoid, 8f) && !NearAny(p, placed, 4.5f))
                    {
                        Tree(BuildBatch(p), p, 5f + 1.6f * (float)r.NextDouble(), r.Next(1000), false, lite);
                        placed.Add(p);
                    }
                }

                int drains = 1 + r.Next(2);

                for (int k = 0; k < drains; k++)
                {
                    float t = length * (0.15f + 0.7f * (float)r.NextDouble());
                    Vector2 p2 = a + dir * t + front * 0.35f;

                    if (!NearJunction(p2, 0f))
                    {
                        Drain(GroundBatch(Geo.V3(p2)), Geo.V3(p2), Geo.V3(dir));
                    }
                }
            });

            BuildBusStops(map, bus, avoid, placed);
            BuildTrees(map, posts, avoid, placed);

            // Caçambas de entulho na calçada.
            Material skip = PbrMaterials.Surface("rust", "Cacamba", new Color(0.9f, 0.7f, 0.1f));
            var skipBatch = new MeshBatch();
            int skips = 0;

            for (int tries = 0; tries < 300 && skips < 9; tries++)
            {
                Block block = Blocks[random.Next(Blocks.Count)];
                int e = random.Next(block.Poly.Count);

                if (block.EdgeClass[e] < RoadClass.Residential)
                {
                    continue;
                }

                Vector2 a = block.Poly[e];
                Vector2 b = block.Poly[(e + 1) % block.Poly.Count];
                float length = Vector2.Distance(a, b);

                if (length < 18f)
                {
                    continue;
                }

                Vector2 dir = (b - a) / length;
                Vector2 front = Geo.Right(dir);
                Vector2 center2 = a + dir * (length * (0.25f + 0.5f * (float)random.NextDouble())) - front * 1.5f;
                Vector3 position = Geo.V3(center2, CurbHeight);

                if (NearAny(position, avoid, 14f) || NearAny(position, placed, 8f) || _raster.Occ[_raster.At(center2)] != 0 || _raster.DistAt(center2) < 1.1f)
                {
                    continue;
                }

                skipBatch.Box(skip, position + Geo.V3(dir * -2.1f + front * 0.9f), Geo.V3(front), 4.2f, 1.1f, 1.8f);
                var go = new GameObject("Cacamba");
                go.transform.SetParent(dumpsters, false);
                go.transform.SetPositionAndRotation(position + Vector3.up * 0.55f, Quaternion.LookRotation(Geo.V3(front), Vector3.up));
                go.AddComponent<BoxCollider>().size = new Vector3(4.2f, 1.1f, 1.8f);
                placed.Add(position);
                skips++;
            }

            skipBatch.Build(dumpsters, "CacambasVisual");

            BuildBumps(random);
            BuildParkedCars(cars, random, avoid, placed);
            BuildWires(random);
            BuildLimits(map, parent);
            Debug.Log($"[CityBuilder] Postes: {Poles.Count}; carros estacionados: {cars.childCount}; pontos de ônibus: {bus.childCount}.");
        }

        // ---------- Fios ----------

        private static void BuildWires(System.Random random)
        {
            var groups = new Dictionary<int, List<PoleInfo>>();

            foreach (PoleInfo pole in Poles)
            {
                if (!groups.TryGetValue(pole.Key, out List<PoleInfo> list))
                {
                    list = new List<PoleInfo>();
                    groups[pole.Key] = list;
                }

                list.Add(pole);
            }

            foreach (List<PoleInfo> list in groups.Values)
            {
                list.Sort((x, y) => x.Axis.CompareTo(y.Axis));

                for (int k = 0; k + 1 < list.Count; k++)
                {
                    if (Vector3.Distance(list[k].Pos, list[k + 1].Pos) < 75f)
                    {
                        SpanWires(list[k], list[k + 1], random);
                    }
                }
            }

            // Travessias: de um lado da rua ao outro, onde há postes quase de frente um para o outro.
            var used = new HashSet<PoleInfo>();

            foreach (PoleInfo a in Poles)
            {
                if (used.Contains(a))
                {
                    continue;
                }

                foreach (PoleInfo c in Poles)
                {
                    float distance = Vector3.Distance(a.Pos, c.Pos);

                    if (c.Key == a.Key || used.Contains(c) || distance < 6f || distance > 26f || Vector3.Dot(a.Facing, c.Facing) > -0.8f)
                    {
                        continue;
                    }

                    // Os dois postes estão um de frente para o outro: o vetor entre eles é quase paralelo ao Facing.
                    Vector3 between = (c.Pos - a.Pos).normalized;

                    if (Mathf.Abs(Vector3.Dot(between, a.Facing)) < 0.9f)
                    {
                        continue;
                    }

                    for (int k = 0; k < 2; k++)
                    {
                        float y = 8.5f - 0.6f * k;
                        Wires(a.Batch, a.Pos + Vector3.up * y, c.Pos + Vector3.up * y, 0.02f * distance, 5, 0.035f, Vector3.zero);
                    }

                    used.Add(a);
                    used.Add(c);

                    break;
                }
            }
        }

        // ---------- Pontos de ônibus e árvores ----------

        private static void BuildBusStops(OsmMap map, Transform colliders, List<Vector3> avoid, List<Vector3> placed)
        {
            var stops = new List<Vector2>();
            var nodes = new List<OsmNode>();

            foreach (OsmNode node in map.Nodes)
            {
                if (node.Tag("highway") == "bus_stop" && map.InsideBox(node.Pos))
                {
                    nodes.Add(node);
                }
            }

            // Primeiro os das avenidas (primária e terciária), depois os das ruas menores.
            for (int pass = 0; pass < 2; pass++)
            {
                foreach (OsmNode node in nodes)
                {
                    Block bestBlock = null;
                    int bestEdge = -1;
                    float best = 30f;

                    foreach (Block block in Blocks)
                    {
                        for (int e = 0; e < block.Poly.Count; e++)
                        {
                            if (block.EdgeClass[e] < RoadClass.Residential)
                            {
                                continue;
                            }

                            float d = Geo.DistanceToSegment(node.Pos, block.Poly[e], block.Poly[(e + 1) % block.Poly.Count], out _, out _);

                            if (d < best)
                            {
                                best = d;
                                bestBlock = block;
                                bestEdge = e;
                            }
                        }
                    }

                    if (bestBlock == null || (pass == 0) != (bestBlock.EdgeClass[bestEdge] >= RoadClass.Tertiary))
                    {
                        continue;
                    }

                    Vector2 a = bestBlock.Poly[bestEdge];
                    Vector2 b = bestBlock.Poly[(bestEdge + 1) % bestBlock.Poly.Count];
                    Vector2 dir = (b - a).normalized;
                    Vector2 front = Geo.Right(dir);
                    Geo.DistanceToSegment(node.Pos, a, b, out Vector2 onEdge, out float t);
                    float length = Vector2.Distance(a, b);
                    Vector2 p2 = a + dir * Mathf.Clamp(t * length, 3f, length - 3f) - front * 1.2f;
                    Vector3 p = Geo.V3(p2, CurbHeight);

                    if (stops.Exists(s => Vector2.Distance(s, p2) < 25f) || NearAny(p, avoid, 9f) || NearAny(p, placed, 6f) || _raster.Occ[_raster.At(p2)] != 0 || _raster.DistAt(p2) < 0.8f || stops.Count >= 26)
                    {
                        continue;
                    }

                    BusStop(colliders, BuildBatch(p), p, Geo.V3(front), stops.Count % 7);
                    stops.Add(p2);
                    placed.Add(p);
                }
            }
        }

        private static void BuildTrees(OsmMap map, Transform posts, List<Vector3> avoid, List<Vector3> placed)
        {
            // Árvores do OSM (natural=tree).
            int seed = 4000;

            foreach (OsmNode node in map.Nodes)
            {
                if (node.Tag("natural") != "tree" || !map.InsideBox(node.Pos))
                {
                    continue;
                }

                var p = Geo.V3(node.Pos, CurbHeight);

                if (_raster.BlockAt(node.Pos) == 0 || _raster.Occ[_raster.At(node.Pos)] != 0 || _raster.DistAt(node.Pos) < 0.7f || NearAny(p, placed, 3f))
                {
                    continue;
                }

                Tree(BuildBatch(p), p, 7f + (seed % 3), seed++, true);
                SolidCapsule(posts, "TroncoOSM", p, 0.25f, 3f);
                placed.Add(p);
            }

            // Parques e cemitério: árvores espalhadas.
            var r = new System.Random(88);
            int count = 0;

            for (float x = -map.HalfX; x < map.HalfX && count < 450; x += 8f)
            {
                for (float z = -map.HalfZ; z < map.HalfZ && count < 450; z += 8f)
                {
                    var p2 = new Vector2(x + (float)r.NextDouble() * 6f, z + (float)r.NextDouble() * 6f);
                    byte zone = _raster.ZoneAt(p2);

                    if ((zone != ZonePark && zone != ZoneCemetery) || _raster.DistAt(p2) < 2f || _raster.Occ[_raster.At(p2)] != 0)
                    {
                        continue;
                    }

                    var p = Geo.V3(p2, CurbHeight);

                    if (NearAny(p, avoid, 8f) || NearAny(p, placed, 6f))
                    {
                        continue;
                    }

                    Tree(BuildBatch(p), p, 6.5f + 2.5f * (float)r.NextDouble(), seed++, true, Vector2.Distance(p2, LargoCenter) > LiteRadius);
                    SolidCapsule(posts, "TroncoParque", p, 0.22f, 3f);
                    placed.Add(p);
                    count++;
                }
            }
        }

        // ---------- Lombadas e carros estacionados ----------

        /// <summary>Lombadas amarelas e pretas em alguns trechos de rua residencial (só visuais: a moto não é afetada).</summary>
        private static void BuildBumps(System.Random random)
        {
            Material asphalt = CityStyle.Asphalt();
            Material yellow = CityStyle.PaintYellow();
            Material black = CityStyle.PaintBlack();
            int made = 0;
            BumpPositions.Clear();

            for (int tries = 0; tries < 400 && made < 10; tries++)
            {
                GraphEdge edge = GraphEdges[random.Next(GraphEdges.Count)];
                Road r = edge.Road;

                if (r.Kind != RoadKind.Residential)
                {
                    continue;
                }

                Vector2 a = r.Pts[edge.Segment];
                Vector2 b = r.Pts[edge.Segment + 1];
                float length = Vector2.Distance(a, b);

                if (length < 40f)
                {
                    continue;
                }

                Vector2 dir = (b - a) / length;
                Vector2 center = a + dir * (length * 0.5f);

                if (NearJunction(center, 8f) || NearAny(Geo.V3(center), BumpPositions, 60f))
                {
                    continue;
                }

                float width = r.ExtL[edge.Segment] + r.ExtR[edge.Segment];
                Vector3 tangent = Geo.V3(dir);
                Vector3 across = Vector3.Cross(tangent, Vector3.up);
                Vector3 origin = Geo.V3(center) + tangent * 0.7f - across * (width * 0.5f);
                MeshBatch batch = GroundBatch(Geo.V3(center));
                batch.Box(asphalt, origin, tangent, width, 0.06f, 1.4f);
                int stripes = Mathf.RoundToInt(width / 0.5f);

                for (int k = 0; k < stripes; k++)
                {
                    batch.Rect(k % 2 == 0 ? yellow : black, origin + across * (k * 0.5f) + Vector3.up * 0.062f, across, -tangent, 0.5f, 1.4f);
                }

                BumpPositions.Add(Geo.V3(center));
                made++;
            }
        }

        private static float RoadWidthFrom(Vector2 boundaryPoint, Vector2 outward)
        {
            for (float d = 0.5f; d < 14f; d += 0.5f)
            {
                if (_raster.BlockAt(boundaryPoint + outward * d) != 0)
                {
                    return d;
                }
            }

            return 14f;
        }

        /// <summary>Carros estacionados rente à guia das ruas residenciais, no sentido do tráfego (mão direita).</summary>
        private static void BuildParkedCars(Transform parent, System.Random random, List<Vector3> avoid, List<Vector3> placed)
        {
            var parked = new List<Vector3>();
            List<Vector3> trafficPath = LoopPath();

            ForEachEdge((block, e, a, dir, front, length, cls) =>
            {
                if (cls < RoadClass.Residential || cls > RoadClass.Tertiary || length < 16f || parked.Count >= 70)
                {
                    return;
                }

                int count = random.Next(0, 3) * Mathf.Max(1, Mathf.RoundToInt(length / 50f));

                for (int n = 0; n < count; n++)
                {
                    float t = 5f + (length - 10f) * (float)random.NextDouble();
                    Vector2 boundary = a + dir * t;
                    Vector2 p2 = boundary + front * 1.55f;
                    Vector3 position = Geo.V3(p2, 0.02f);

                    if (NearJunction(p2, 6f) || RoadWidthFrom(boundary, front) < 6.8f || NearAny(position, trafficPath, 5.5f) || NearAny(position, avoid, 16f) || NearAny(position, parked, 8f) || NearAny(position, placed, 4f))
                    {
                        continue;
                    }

                    double kind = random.NextDouble();
                    int paint = CarPaint.Sorteio(random);
                    float yaw = Mathf.Atan2(dir.x, dir.y) * Mathf.Rad2Deg + ((float)random.NextDouble() - 0.5f) * 4f;
                    parked.Add(position);

                    if (kind < 0.09)
                    {
                        CityPieces.Car(parent, ParkedKenneyVans[random.Next(ParkedKenneyVans.Length)], position, yaw, true, paint);
                    }
                    else
                    {
                        CarrosReais.Modelo real = kind < 0.45 ? CarrosReais.Modelo.Uno : CarrosReais.Modelo.Onix;
                        CarrosReais.Instanciar(parent, real, position, yaw, paint, random.Next(CarrosReais.PlacaCount), true);
                    }
                }
            });
        }

        // ---------- Limite do mapa ----------

        /// <summary>Meia-largura da área jogável: as paredes invisíveis ficam um pouco para dentro da caixa do OSM.</summary>
        public const float PlayableHalf = 475f;

        /// <summary>Paredes invisíveis na borda jogável e uma barreira de obra em cada rua que cruza a borda (a cidade continua além dela).</summary>
        private static void BuildLimits(OsmMap map, Transform parent)
        {
            Transform limits = Group(parent, "Limites");
            const float height = 6f;
            float p = PlayableHalf;
            Wall(limits, "LimiteNorte", new Vector3(0f, height * 0.5f, p + 1f), new Vector3(2f * p + 8f, height, 2f));
            Wall(limits, "LimiteSul", new Vector3(0f, height * 0.5f, -p - 1f), new Vector3(2f * p + 8f, height, 2f));
            Wall(limits, "LimiteLeste", new Vector3(p + 1f, height * 0.5f, 0f), new Vector3(2f, height, 2f * p + 8f));
            Wall(limits, "LimiteOeste", new Vector3(-p - 1f, height * 0.5f, 0f), new Vector3(2f, height, 2f * p + 8f));

            var placed = new List<Vector2>();
            Material red = CityStyle.Awning(0);
            Material white = CityStyle.PaintWhite();

            foreach (Road r in Roads)
            {
                if (r.Kind == RoadKind.Pedestrian)
                {
                    continue;
                }

                for (int k = 0; k + 1 < r.Pts.Count; k++)
                {
                    Vector2 a = r.Pts[k];
                    Vector2 b = r.Pts[k + 1];

                    for (int axis = 0; axis < 2; axis++)
                    {
                        foreach (float side in new[] { -1f, 1f })
                        {
                            float limit = side * p;
                            float va = axis == 0 ? a.x : a.y;
                            float vb = axis == 0 ? b.x : b.y;

                            if ((va - limit) * (vb - limit) >= 0f || Mathf.Abs(va - vb) < 0.01f)
                            {
                                continue;
                            }

                            Vector2 at = Vector2.Lerp(a, b, (limit - va) / (vb - va));

                            if (placed.Exists(q => Vector2.Distance(q, at) < 12f))
                            {
                                continue;
                            }

                            placed.Add(at);
                            Vector2 inward = axis == 0 ? new Vector2(-side, 0f) : new Vector2(0f, -side);

                            if (r.Class >= RoadClass.Tertiary && BorderViews.Count == 0)
                            {
                                BorderViews.Add(View("borda", at + inward * 17f, -inward));
                            }

                            Barrier(limits, at + inward * 1.2f, inward, Mathf.Max(r.ExtL[k], r.ExtR[k]), red, white);
                        }
                    }
                }
            }
        }

        /// <summary>Barreira de obra de lado a lado da rua: tábuas listradas, cones e uma placa "OBRA". inward aponta para dentro do mapa.</summary>
        private static void Barrier(Transform parent, Vector2 center, Vector2 inward, float halfWidth, Material red, Material white)
        {
            Vector3 inward3 = Geo.V3(inward);
            Vector3 sideways = Vector3.Cross(inward3, Vector3.up);
            var frame = new Frame(Geo.V3(center) - sideways * halfWidth, inward3);
            var blocker = new GameObject("Bloqueio_Obra");
            blocker.transform.SetParent(parent, false);
            blocker.transform.position = Geo.V3(center);
            blocker.AddComponent<ViaBloqueada>();
            MeshBatch b = BuildBatch(Geo.V3(center));
            float total = halfWidth * 2f;
            int boards = Mathf.CeilToInt(total / 1.2f);
            float step = total / boards;

            for (int i = 0; i < boards; i++)
            {
                for (int s = 0; s < 4; s++)
                {
                    float x0 = i * step + s * step / 4f;
                    b.Box(s % 2 == 0 ? red : white, frame.Pt(x0, 0.75f, 0f), frame.F, step / 4f, 0.32f, 0.1f);
                }

                b.Box(CityStyle.IronBlack(), frame.Pt(i * step + 0.1f, 0f, 0f), frame.F, 0.08f, 0.9f, 0.08f);
                b.Box(CityStyle.IronBlack(), frame.Pt(i * step + step - 0.18f, 0f, 0f), frame.F, 0.08f, 0.9f, 0.08f);
            }

            b.Box(CityStyle.Board(3), frame.Pt(total * 0.5f - 0.7f, 1.2f, 0.02f), frame.F, 1.4f, 0.7f, 0.05f);
            var context = new BuildingContext { B = b, Rnd = new System.Random(1), Signs = _signsRoot };
            CityBuildings.Sign(context, frame, total * 0.5f, 1.55f, 1.3f, 0.6f, "OBRA", CityStyle.BoardText(3), -0.08f);

            for (int c = 0; c < 4; c++)
            {
                Vector3 p = frame.Pt(total * (c + 0.5f) / 4f, 0f, -1.2f);
                CityPieces.Model(parent, Kit.CityKitRoads, "construction-cone", p + Vector3.up * 0.02f, 0f, 9f).name = "Cone";
            }
        }

        // ---------- Trânsito ----------

        private const int TrafficCars = 8;
        private static List<List<int>> _trafficLoops;
        private static Vector3 _avenueAnchor;
        private static bool _avenueAnchorSet;

        /// <summary>Pontos de vista na borda do mapa (barreiras de obra), usados pelas capturas.</summary>
        private static readonly List<ViewPoint> BorderViews = new List<ViewPoint>();

        /// <summary>Posições (plano) de árvores, postes e da fonte do largo: o ponto de vista do largo evita ficar atrás deles.</summary>
        private static readonly List<Vector2> LargoSolids = new List<Vector2>();

        /// <summary>Trechos de rua (rua, índice do trecho) percorridos pelos circuitos do trânsito.</summary>
        private static readonly HashSet<(Road road, int segment)> LoopSegments = new HashSet<(Road road, int segment)>();

        /// <summary>
        /// Oito carros do kit dando voltas em circuitos fechados reais de ruas (sentido do trânsito, pista da direita), a 7 a 9 m/s,
        /// cinemáticos. Os circuitos não se cruzam (nós disjuntos) e há ao menos dois carros por circuito, afastados.
        /// </summary>
        private static void BuildTraffic(Transform parent, Transform player)
        {
            List<List<int>> loops = _trafficLoops;
            // Uno e Onix reais; a van de entrega ainda é do Kenney.
            string[] models = { "uno", "onix", "onix", "uno", "onix", "delivery", "uno", "onix" };
            float[] speeds = { 8f, 7f, 9f, 8f, 7.5f, 8.5f, 7f, 9f };
            int[] paints = { 0, 2, 1, 3, 0, 1, 4, 5 };
            var cars = new List<(List<int> loop, float fraction)>();

            for (int k = 0; k < TrafficCars; k++)
            {
                List<int> loop = loops[k % loops.Count];
                int round = k / loops.Count;
                int onLoop = Mathf.CeilToInt((TrafficCars - (k % loops.Count)) / (float)loops.Count);
                cars.Add((loop, (round + (k % loops.Count) * 0.13f) / Mathf.Max(2, onLoop)));
            }

            bool anchored = false;

            for (int k = 0; k < TrafficCars; k++)
            {
                Vector3[] corners = LoopWaypoints(cars[k].loop);
                float perimeter = 0f;

                for (int c = 0; c < corners.Length; c++)
                {
                    perimeter += Vector3.Distance(corners[c], corners[(c + 1) % corners.Length]);
                }

                float distance = Mathf.Repeat(cars[k].fraction, 1f) * perimeter;

                // Um carro nasce logo à frente do ponto de vista da avenida (as capturas pegam o trânsito do corredor de ônibus).
                if (_avenueAnchorSet && !anchored && AlongLoop(corners, _avenueAnchor, out float along, out float gap) && gap < 8f)
                {
                    distance = along;
                    anchored = true;
                }

                int segment = 0;

                while (distance > Vector3.Distance(corners[segment], corners[(segment + 1) % corners.Length]))
                {
                    distance -= Vector3.Distance(corners[segment], corners[(segment + 1) % corners.Length]);
                    segment++;
                }

                Vector3 a = corners[segment];
                Vector3 b = corners[(segment + 1) % corners.Length];
                Vector3 direction = (b - a).normalized;
                Vector3 start = a + direction * distance;
                float yaw = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;

                var car = new GameObject("Transito_" + models[k] + "_" + k);
                car.transform.SetParent(parent, false);
                car.transform.SetPositionAndRotation(start, Quaternion.Euler(0f, yaw, 0f));
                var box = car.AddComponent<BoxCollider>();
                car.AddComponent<Rigidbody>().isKinematic = true;

                if (models[k] == "delivery")
                {
                    Bounds bounds = CityPieces.ModelBounds(Kit.CarKit, models[k]);
                    box.center = bounds.center * CityPieces.CarScale;
                    box.size = bounds.size * CityPieces.CarScale;
                    CityPieces.Car(car.transform, models[k], start, yaw, false, paints[k]).name = "Carro";
                }
                else
                {
                    CarrosReais.Modelo real = models[k] == "uno" ? CarrosReais.Modelo.Uno : CarrosReais.Modelo.Onix;
                    Bounds bounds = CarrosReais.Limites(real);
                    box.center = bounds.center;
                    box.size = bounds.size;
                    CarrosReais.Instanciar(car.transform, real, start, yaw, paints[k], k, false).name = "Carro";
                }
                car.AddComponent<Entregador.Game.TrafficLoop>().Configure(corners, speeds[k], player, segment);
            }
        }

        /// <summary>Posição (m, ao longo do circuito) do ponto do circuito mais perto de p, e a distância até ele.</summary>
        private static bool AlongLoop(Vector3[] corners, Vector3 p, out float along, out float gap)
        {
            along = 0f;
            gap = float.MaxValue;
            float travelled = 0f;

            for (int i = 0; i < corners.Length; i++)
            {
                Vector3 a = corners[i];
                Vector3 b = corners[(i + 1) % corners.Length];
                float d = Geo.DistanceToSegment(Geo.V2(p), Geo.V2(a), Geo.V2(b), out _, out float t);

                if (d < gap)
                {
                    gap = d;
                    along = travelled + t * Vector3.Distance(a, b);
                }

                travelled += Vector3.Distance(a, b);
            }

            return gap < float.MaxValue;
        }

        /// <summary>Todos os pontos dos circuitos do trânsito, de 5 em 5 m (os carros estacionados ficam longe deles).</summary>
        private static List<Vector3> LoopPath()
        {
            var path = new List<Vector3>();

            foreach (List<int> loop in _trafficLoops)
            {
                Vector3[] points = LoopWaypoints(loop);

                for (int i = 0; i < points.Length; i++)
                {
                    Vector3 a = points[i];
                    Vector3 b = points[(i + 1) % points.Length];
                    int steps = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(a, b) / 5f));

                    for (int s = 0; s < steps; s++)
                    {
                        path.Add(Vector3.Lerp(a, b, s / (float)steps));
                    }
                }
            }

            return path;
        }

        /// <summary>Pontos do circuito: nós do grafo, deslocados para a pista da direita onde a rua tem mão dupla.</summary>
        private static Vector3[] LoopWaypoints(List<int> loop)
        {
            var points = new List<Vector3>();
            int n = loop.Count;

            for (int i = 0; i < n; i++)
            {
                Vector2 prev = Geo.V2(_graph.NodePosition(loop[(i + n - 1) % n]));
                Vector2 cur = Geo.V2(_graph.NodePosition(loop[i]));
                Vector2 next = Geo.V2(_graph.NodePosition(loop[(i + 1) % n]));
                Vector2 d = ((cur - prev).normalized + (next - cur).normalized).normalized;

                if (d.sqrMagnitude < 0.01f)
                {
                    d = (next - cur).normalized;
                }

                bool twoWay = !ArcOneway(loop[(i + n - 1) % n], loop[i]) || !ArcOneway(loop[i], loop[(i + 1) % n]);
                Vector2 p = cur + Geo.Right(d) * (twoWay ? 1.9f : 0f);

                if (points.Count == 0 || Vector2.Distance(Geo.V2(points[points.Count - 1]), p) > 4f)
                {
                    points.Add(Geo.V3(p, 0.02f));
                }
            }

            return points.ToArray();
        }

        private static bool ArcOneway(int from, int to)
        {
            foreach (GraphEdge e in GraphEdges)
            {
                if ((e.From == from && e.To == to) || (e.From == to && e.To == from))
                {
                    return e.Road.Oneway;
                }
            }

            return false;
        }

        /// <summary>Procura circuitos fechados de 450 a 1.400 m nas ruas principais (depois nas demais), com nós que não se repetem entre circuitos.</summary>
        private static List<List<int>> FindTrafficLoops()
        {
            var result = new List<List<int>>();
            var usedNodes = new HashSet<int>();

            for (int pass = 0; pass < 2 && result.Count < 4; pass++)
            {
                RoadKind minimum = pass == 0 ? RoadKind.Tertiary : RoadKind.Service;
                var adjacency = new Dictionary<int, List<(int to, float length)>>();

                foreach (GraphEdge e in GraphEdges)
                {
                    if (e.Road.Kind < minimum || e.Road.Kind == RoadKind.Busway)
                    {
                        continue;
                    }

                    float length = Vector3.Distance(_graph.NodePosition(e.From), _graph.NodePosition(e.To));
                    AddArc(adjacency, e.From, e.To, length);

                    if (!e.Road.Oneway)
                    {
                        AddArc(adjacency, e.To, e.From, length);
                    }
                }

                var candidates = new List<(List<int> nodes, float length)>();

                foreach (int start in new List<int>(adjacency.Keys))
                {
                    var path = new List<int> { start };
                    var onPath = new HashSet<int> { start };
                    int budget = 12000;
                    int before = candidates.Count;
                    Search(adjacency, start, start, 0f, path, onPath, candidates, ref budget, before + 2);
                }

                // Os que passam pelo corredor de ônibus primeiro (a avenida precisa de trânsito), depois os mais longos; sem nós em comum.
                var score = new Dictionary<List<int>, float>();

                foreach ((List<int> nodes, float length) in candidates)
                {
                    float bonus = 0f;

                    foreach (GraphEdge e in GraphEdges)
                    {
                        if (e.Road.BusLeft + e.Road.BusRight > 0.5f && nodes.Contains(e.From) && nodes.Contains(e.To))
                        {
                            bonus = 600f;

                            break;
                        }
                    }

                    score[nodes] = length + bonus;
                }

                candidates.Sort((x, y) => score[y.nodes].CompareTo(score[x.nodes]));

                foreach ((List<int> nodes, float length) in candidates)
                {
                    if (result.Count >= 4)
                    {
                        break;
                    }

                    bool shared = false;

                    foreach (int node in nodes)
                    {
                        shared |= usedNodes.Contains(node);
                    }

                    if (shared)
                    {
                        continue;
                    }

                    result.Add(nodes);
                    usedNodes.UnionWith(nodes);
                }
            }

            if (result.Count == 0)
            {
                throw new System.InvalidOperationException("Nenhum circuito fechado de ruas para o trânsito.");
            }

            foreach (List<int> loop in result)
            {
                for (int i = 0; i < loop.Count; i++)
                {
                    int a = loop[i];
                    int b = loop[(i + 1) % loop.Count];

                    foreach (GraphEdge e in GraphEdges)
                    {
                        if ((e.From == a && e.To == b) || (e.From == b && e.To == a))
                        {
                            LoopSegments.Add((e.Road, e.Segment));

                            break;
                        }
                    }
                }

                var names = new List<string>();

                foreach (GraphEdge e in GraphEdges)
                {
                    if (loop.Contains(e.From) && loop.Contains(e.To) && !string.IsNullOrEmpty(e.Road.Name) && !names.Contains(e.Road.Name))
                    {
                        names.Add(e.Road.Name);
                    }
                }

                Debug.Log($"[CityBuilder] Circuito de trânsito com {loop.Count} nós: {string.Join(", ", names)}.");
            }

            return result;
        }

        private static void AddArc(Dictionary<int, List<(int to, float length)>> adjacency, int from, int to, float length)
        {
            if (!adjacency.TryGetValue(from, out List<(int to, float length)> list))
            {
                list = new List<(int to, float length)>();
                adjacency[from] = list;
            }

            list.Add((to, length));
        }

        private static void Search(Dictionary<int, List<(int to, float length)>> adjacency, int start, int node, float length, List<int> path, HashSet<int> onPath, List<(List<int> nodes, float length)> found, ref int budget, int foundLimit)
        {
            if (budget-- <= 0 || found.Count >= foundLimit || !adjacency.TryGetValue(node, out List<(int to, float length)> arcs))
            {
                return;
            }

            foreach ((int to, float arc) in arcs)
            {
                float total = length + arc;

                if (to == start && path.Count >= 4 && total >= 450f)
                {
                    found.Add((new List<int>(path), total));

                    continue;
                }

                if (onPath.Contains(to) || total > 1400f)
                {
                    continue;
                }

                path.Add(to);
                onPath.Add(to);
                Search(adjacency, start, to, total, path, onPath, found, ref budget, foundLimit);
                path.RemoveAt(path.Count - 1);
                onPath.Remove(to);
            }
        }
    }
}

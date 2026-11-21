using System.Collections.Generic;
using UnityEngine;

namespace Entregador.Editor
{
    /// <summary>
    /// Mobiliário urbano de São Paulo: postes de concreto com fios, luminárias, lixeiras laranja, pontos de ônibus, bueiros,
    /// árvores, semáforos, placas de PARE, caçambas, carros parados e o trânsito. Tudo vira malha fundida no quarteirão.
    /// </summary>
    public static partial class CityBuilder
    {
        private static readonly string[] ParkedCarModels = { "sedan", "hatchback-sports", "suv", "taxi", "van", "delivery", "truck", "sedan", "hatchback-sports" };

        private sealed class PoleInfo
        {
            public Vector3 Pos;
            public int Key;
            public float Axis;
            public MeshBatch Batch;
            public Vector3 Facing;
        }

        private static readonly List<PoleInfo> Poles = new List<PoleInfo>();
        private static Transform _blocksRoot;

        /// <summary>Distância do centro da rua até o meio de um carro estacionado (rente ao meio-fio).</summary>
        private static float ParkOffset(float halfWidth) => halfWidth - 1.6f;

        /// <summary>Faixa de trânsito (distância do centro da rua): na avenida a pista externa.</summary>
        private static float LaneOffset(bool avenue) => avenue ? 8f : 2f;

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

        // ---------- Peças ----------

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

        private static void BuildWires()
        {
            var random = new System.Random(77);
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
                list.Sort((a, b) => a.Axis.CompareTo(b.Axis));

                for (int k = 0; k + 1 < list.Count; k++)
                {
                    float distance = Vector3.Distance(list[k].Pos, list[k + 1].Pos);

                    if (distance < 75f)
                    {
                        SpanWires(list[k], list[k + 1], random);
                    }
                }
            }

            // Travessias: de um lado da rua ao outro, onde há postes quase de frente um para o outro.
            foreach (PoleInfo a in Poles)
            {
                if ((a.Key & 1) == 0)
                {
                    continue;
                }

                foreach (PoleInfo c in Poles)
                {
                    if (c.Key == a.Key - 1 + 0 && c.Key / 10 == a.Key / 10 && Mathf.Abs(c.Axis - a.Axis) < 6f)
                    {
                        float length = Vector3.Distance(a.Pos, c.Pos);

                        for (int k = 0; k < 2; k++)
                        {
                            float y = 8.5f - 0.6f * k;
                            Wires(a.Batch, a.Pos + Vector3.up * y, c.Pos + Vector3.up * y, 0.02f * length, 5, 0.035f, Vector3.zero);
                        }

                        break;
                    }
                }
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

        // ---------- Montagem ----------

        private static void PlaceEdgeFurniture(Edge e, List<Vector3> avoid, Transform postsRoot, Transform busRoot)
        {
            MeshBatch b = BlockBatches[e.Block];
            var r = new System.Random(e.Block * 13 + e.Key);
            var placed = new List<Vector3>();
            float poleOffset = FrontInset - 0.75f;

            // Postes (concreto, a 0,75 m do meio-fio) a cada ~34 m.
            for (float t = 8f + (float)r.NextDouble() * 12f; t < e.Length - 5f; t += 31f + (float)r.NextDouble() * 6f)
            {
                Vector3 p = e.Start + e.Along * t + e.Facing * poleOffset;
                p.y = CurbHeight;

                if (NearAny(p, avoid, 6.5f))
                {
                    continue;
                }

                UtilityPole(b, p, e.Facing, r);
                SolidCapsule(postsRoot, "Poste", p, 0.2f, 3f);
                placed.Add(p);
                Poles.Add(new PoleInfo { Pos = p, Key = e.Key, Axis = e.Horizontal ? p.x : p.z, Batch = b, Facing = e.Facing });
            }

            // Lixeiras laranja nas fachadas comerciais.
            if (e.Commercial)
            {
                for (float t = 14f + (float)r.NextDouble() * 10f; t < e.Length - 6f; t += 38f)
                {
                    Vector3 p = e.Start + e.Along * t + e.Facing * (FrontInset - 0.6f);
                    p.y = CurbHeight;

                    if (!NearAny(p, avoid, 6f) && !NearAny(p, placed, 3.5f))
                    {
                        Bin(b, p);
                        placed.Add(p);
                    }
                }
            }

            // Árvores de calçada nas ruas residenciais (e poucas nas comerciais).
            int trees = e.Commercial ? Mathf.RoundToInt(e.Length / 60f) : Mathf.Max(1, Mathf.RoundToInt(e.Length / 30f));

            for (int k = 0; k < trees; k++)
            {
                float at = e.Length * (k + 0.5f + ((float)r.NextDouble() - 0.5f) * 0.4f) / trees;
                Vector3 p = e.Start + e.Along * at + e.Facing * 1.6f;
                p.y = CurbHeight;

                if (!NearAny(p, avoid, 8f) && !NearAny(p, placed, 4.5f))
                {
                    Tree(b, p, 5f + 1.6f * (float)r.NextDouble(), r.Next(1000), false, e.Lite);
                    placed.Add(p);
                }
            }

            // Bueiros na sarjeta.
            int drains = 1 + r.Next(2);

            for (int k = 0; k < drains; k++)
            {
                float t = e.Length * (0.15f + 0.7f * (float)r.NextDouble());
                Vector3 p = e.Start + e.Along * t + e.Facing * (FrontInset + 0.35f);
                p.y = 0f;
                Drain(b, p, e.Along);
            }

            // Pontos de ônibus nos dois lados da avenida.
            if (e.AvenueRow && e.Length > 60f && e.Commercial)
            {
                float t = e.Length * (0.3f + 0.4f * (float)r.NextDouble());
                Vector3 p = e.Start + e.Along * t + e.Facing * (FrontInset - 1.1f);
                p.y = CurbHeight;

                if (!NearAny(p, placed, 6f) && !NearAny(p, avoid, 12f))
                {
                    BusStop(busRoot, b, p, e.Facing, r.Next(7));
                }
            }
        }

        private static void BuildProps(Transform parent, System.Random random, List<Vector3> avoid)
        {
            Transform posts = Group(parent, "Postes");
            Transform signs = Group(parent, "Placas");
            Transform cars = Group(parent, "CarrosParados");
            Transform dumpsters = Group(parent, "Cacambas");
            Transform bus = Group(parent, "PontosDeOnibus");
            var parked = new List<Vector3>();
            Poles.Clear();
            avoid.AddRange(BumpPositions);

            int Sum()
            {
                int n = 0;

                foreach (MeshBatch mb in BlockBatches.Values)
                {
                    n += mb.TriangleCount;
                }

                return n;
            }

            int buildingsTris = Sum();
            Debug.Log("[CityBuilder] Tris por tipo (sobrado completo/lite, comércio c/l, torre c/l): " + string.Join(",", KindStats) + " lotes " + string.Join(",", KindCount));

            foreach (Edge e in Edges)
            {
                PlaceEdgeFurniture(e, avoid, posts, bus);
            }

            int furnitureTris = Sum();
            BuildWires();
            Debug.Log($"[CityBuilder] Triângulos: prédios+calçadas {buildingsTris}, mobiliário {furnitureTris - buildingsTris}, fios {Sum() - furnitureTris}");

            // Semáforos nos cruzamentos da avenida e placas de PARE nas ruas pequenas.
            var signals = new MeshBatch();

            for (int j = 0; j < StreetCount; j++)
            {
                Vector3 center = new Vector3(_cx[AvenueIndex], CurbHeight, _cz[j]);

                foreach (int sign in new[] { 1, -1 })
                {
                    Vector3 corner = center + new Vector3(sign * (AvenueHalfWidth + 0.5f), 0f, sign * (StreetHalfWidth + 0.5f));
                    TrafficLight(signals, corner, new Vector3(-sign, 0f, 0f), new Vector3(0f, 0f, sign), j + sign);
                    SolidCapsule(signs, "Semaforo", corner, 0.2f, 3f);
                }
            }

            for (int k = 0; k < 14; k++)
            {
                int i = random.Next(StreetCount);

                if (i == AvenueIndex || i == AvenueIndex - 1 || i == AvenueIndex + 1)
                {
                    continue;
                }

                int j = random.Next(StreetCount);
                Vector3 corner = new Vector3(_cx[i] + StreetHalfWidth + 0.6f, CurbHeight, _cz[j] + StreetHalfWidth + 0.6f);
                StopSign(signals, corner, new Vector3(0f, 0f, 1f));
                SolidCapsule(signs, "PlacaPare", corner, 0.15f, 3f);
            }

            // Caçambas de entulho na calçada.
            Material skip = PbrMaterials.Surface("rust", "Cacamba", new Color(0.9f, 0.7f, 0.1f));
            var skipBatch = new MeshBatch();

            for (int k = 0; k < 9; k++)
            {
                int i = random.Next(BlockCount);
                int j = random.Next(BlockCount);

                if (i == LargoBlockX && j == LargoBlockZ)
                {
                    continue;
                }

                float along = 0.2f + 0.6f * (float)random.NextDouble();
                bool west = random.Next(2) == 0;
                float x = west ? BlockX0(i) + 2.0f : BlockX1(i) - 2.0f;
                float z = Mathf.Lerp(BlockZ0(j), BlockZ1(j), along);
                var position = new Vector3(x, CurbHeight, z);

                if (NearAny(position, avoid, 14f))
                {
                    continue;
                }

                skipBatch.AxisBox(skip, position + Vector3.up * 0.55f, new Vector3(1.8f, 1.1f, 4.2f));
                var go = new GameObject("Cacamba");
                go.transform.SetParent(dumpsters, false);
                go.transform.position = position + Vector3.up * 0.55f;
                go.AddComponent<BoxCollider>().size = new Vector3(1.8f, 1.1f, 4.2f);
            }

            skipBatch.Build(dumpsters, "CacambasVisual");

            // Carros estacionados, rente ao meio-fio, no sentido do trânsito (mão direita).
            for (int i = 0; i < StreetCount; i++)
            {
                for (int j = 0; j < BlockCount; j++)
                {
                    ParkAlong(cars, random, avoid, parked, true, i, j);
                }
            }

            for (int j = 0; j < StreetCount; j++)
            {
                for (int i = 0; i < BlockCount; i++)
                {
                    ParkAlong(cars, random, avoid, parked, false, j, i);
                }
            }

            signals.Build(signs, "SinalizacaoVisual");
            FinishBlocks();
        }

        /// <summary>Grava a malha de cada quarteirão e registra os renderers que molham na chuva.</summary>
        private static void FinishBlocks()
        {
            int total = 0;

            foreach (var pair in BlockBatches)
            {
                total += pair.Value.TriangleCount;
                GameObject go = pair.Value.Build(_blocksRoot, $"Quarteirao_{pair.Key / 100}_{pair.Key % 100}");

                if (go != null)
                {
                    GroundRenderers.Add(go.GetComponent<Renderer>());
                }
            }

            Debug.Log($"[CityBuilder] Triângulos nos quarteirões: {total}; postes: {Poles.Count}");
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

        private static void ParkAlong(Transform parent, System.Random random, List<Vector3> avoid, List<Vector3> parked, bool vertical, int street, int segment)
        {
            int count = random.Next(0, 3);
            float halfWidth = vertical ? _hwx[street] : _hwz[street];

            for (int n = 0; n < count; n++)
            {
                float t = 0.2f + 0.6f * (float)random.NextDouble();
                int side = random.Next(2) == 0 ? 1 : -1;
                string model = ParkedCarModels[random.Next(ParkedCarModels.Length)];
                int paint = random.Next(CarPaint.Colors.Length);
                Vector3 position;
                float yaw;

                if (vertical)
                {
                    position = new Vector3(_cx[street] + side * ParkOffset(halfWidth), 0.02f, Mathf.Lerp(_cz[segment], _cz[segment + 1], t));
                    yaw = side > 0 ? 0f : 180f;
                }
                else
                {
                    position = new Vector3(Mathf.Lerp(_cx[segment], _cx[segment + 1], t), 0.02f, _cz[street] + side * ParkOffset(halfWidth));
                    yaw = side < 0 ? 90f : 270f;
                }

                if (NearAny(position, avoid, 16f) || NearAny(position, parked, 8f))
                {
                    continue;
                }

                parked.Add(position);
                CityPieces.Car(parent, model, position, yaw + ((float)random.NextDouble() - 0.5f) * 4f, true, paint);
            }
        }

        /// <summary>Oito carros do kit dando voltas em quarteirões (sentido horário), a 7 a 9 m/s, cinemáticos.</summary>
        private static void BuildTraffic(Transform parent, Transform player)
        {
            var blocks = new[]
            {
                new Vector2Int(3, 3), new Vector2Int(4, 3), new Vector2Int(3, 4), new Vector2Int(4, 4),
                new Vector2Int(3, 5), new Vector2Int(4, 5), new Vector2Int(2, 4), new Vector2Int(5, 4),
            };
            string[] models = { "taxi", "sedan", "van", "suv", "hatchback-sports", "delivery", "sedan", "truck" };
            float[] speeds = { 8f, 7f, 9f, 8f, 7.5f, 8.5f, 7f, 9f };
            float[] starts = { 0.1f, 0.6f, 0.35f, 0.85f, 0.2f, 0.7f, 0.5f, 0.05f };
            int[] paints = { 0, 1, 2, 3, 4, 5, 0, 1 };

            for (int k = 0; k < blocks.Length; k++)
            {
                int i = blocks[k].x;
                int j = blocks[k].y;
                float west = LaneOffset(i == AvenueIndex);
                float east = LaneOffset(i + 1 == AvenueIndex);
                var corners = new[]
                {
                    new Vector3(_cx[i] + west, 0.02f, _cz[j] + 2f),
                    new Vector3(_cx[i] + west, 0.02f, _cz[j + 1] - 2f),
                    new Vector3(_cx[i + 1] - east, 0.02f, _cz[j + 1] - 2f),
                    new Vector3(_cx[i + 1] - east, 0.02f, _cz[j] + 2f),
                };

                // Posição inicial: fração do perímetro.
                float perimeter = 0f;

                for (int c = 0; c < 4; c++)
                {
                    perimeter += Vector3.Distance(corners[c], corners[(c + 1) % 4]);
                }

                float distance = starts[k] * perimeter;
                int segment = 0;

                while (distance > Vector3.Distance(corners[segment], corners[(segment + 1) % 4]))
                {
                    distance -= Vector3.Distance(corners[segment], corners[(segment + 1) % 4]);
                    segment++;
                }

                Vector3 a = corners[segment];
                Vector3 b = corners[(segment + 1) % 4];
                Vector3 direction = (b - a).normalized;
                Vector3 start = a + direction * distance;
                float yaw = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;

                var car = new GameObject("Transito_" + models[k] + "_" + k);
                car.transform.SetParent(parent, false);
                car.transform.SetPositionAndRotation(start, Quaternion.Euler(0f, yaw, 0f));
                Bounds bounds = CityPieces.ModelBounds(Kit.CarKit, models[k]);
                var box = car.AddComponent<BoxCollider>();
                box.center = bounds.center * CityPieces.CarScale;
                box.size = bounds.size * CityPieces.CarScale;
                car.AddComponent<Rigidbody>().isKinematic = true;
                CityPieces.Car(car.transform, models[k], start, yaw, false, paints[k]).name = "Carro";
                car.AddComponent<Entregador.Game.TrafficLoop>().Configure(corners, speeds[k], player, segment);
            }
        }
    }
}

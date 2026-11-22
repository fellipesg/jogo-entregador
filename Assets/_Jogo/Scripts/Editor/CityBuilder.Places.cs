using System.Collections.Generic;
using Entregador.Game;
using UnityEngine;

namespace Entregador.Editor
{
    /// <summary>Lugares do jogo: marcadores de restaurante e cliente, buracos, ponto de partida, o largo e os pontos de vista das capturas.</summary>
    public static partial class CityBuilder
    {
        /// <summary>Distância mínima de rota (m) do primeiro endereço de cliente ao primeiro restaurante.</summary>
        private const float MinCustomerMeters = 220f;

        private static readonly string[] FallbackStreets =
        {
            "Rua das Jabuticabeiras", "Rua Padre Anselmo Duarte", "Rua dos Ipês Roxos", "Travessa Dona Zulmira",
            "Rua Capitão Nogueira", "Rua das Hortênsias", "Rua Mestre Valentim", "Rua Tia Ciata",
            "Rua do Cambuci Velho", "Rua Joaquim Pinheiro", "Rua das Paineiras", "Rua Professora Lúcia",
            "Rua Seu Antônio Barbeiro", "Rua dos Tamoios", "Rua Santa Rita de Cássia", "Rua do Mercado Novo",
            "Rua Coronel Ribeiro", "Rua das Mangueiras", "Rua Dona Benta", "Rua Menino Jesus",
        };

        private const int CustomerCount = 20;

        /// <summary>Centro livre do largo (onde fica a fonte), para o ponto de vista do largo.</summary>
        private static Vector2 _fountain = LargoCenter;

        /// <summary>O ponto está dentro de uma faixa de ônibus (corredor vermelho)?</summary>
        private static bool InBusLane(Vector3 p)
        {
            foreach (BusLane lane in BusLanes)
            {
                if (lane.DistanceTo(p) < lane.HalfWidth + 0.3f)
                {
                    return true;
                }
            }

            return false;
        }

        private static void Wall(Transform parent, string name, Vector3 center, Vector3 size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = center;
            go.AddComponent<BoxCollider>().size = size;
        }

        // ---------- Marcadores ----------

        /// <summary>Marcador no chão (disco) com um feixe de luz alto, que só acende quando é o alvo.</summary>
        private static GameObject BuildMarker(Transform parent, string name, Vector3 position, Material padMaterial, Materials m)
        {
            Transform holder = Group(parent, "Marcador_" + name);
            holder.position = position;
            CityPieces.Cylinder(holder, "Disco", position + Vector3.up * 0.06f, 3.5f, 0.08f, padMaterial);
            GameObject beam = CityPieces.Cylinder(holder, "Feixe", position + Vector3.up * 15f, 0.7f, 30f, m.Beam);
            beam.SetActive(false);

            return beam;
        }

        /// <summary>
        /// Vinte endereços de cliente nas ruas residenciais (e terciárias), rente à guia, de perto a longe do largo: o alvo de cada um sobe
        /// de MinCustomerMeters à maior distância de rota (do primeiro restaurante), para existir rota para qualquer distância de pedido.
        /// </summary>
        private static Place[] BuildCustomers(Transform markers, Materials m, Place[] restaurants)
        {
            var random = new System.Random(7);
            var pool = new List<(Vector3 position, string street, float distance)>();

            for (int edgeIndex = 0; edgeIndex < GraphEdges.Count; edgeIndex++)
            {
                GraphEdge edge = GraphEdges[edgeIndex];
                Road r = edge.Road;

                if (_graph.IsBlocked(edgeIndex) || r.Kind == RoadKind.Primary || r.Kind == RoadKind.Service || r.BusLeft + r.BusRight > 0.5f)
                {
                    continue;
                }

                int k = edge.Segment;
                Vector2 a = r.Pts[k];
                Vector2 b = r.Pts[k + 1];
                float length = Vector2.Distance(a, b);

                if (length < 20f)
                {
                    continue;
                }

                Vector2 dir = (b - a) / length;
                Vector2 mid = (a + b) * 0.5f;

                foreach (int side in new[] { -1, 1 })
                {
                    float ext = side > 0 ? (r.ExtL[k] + r.ExtL[k + 1]) * 0.5f : (r.ExtR[k] + r.ExtR[k + 1]) * 0.5f;
                    Vector2 p2 = mid + Geo.Left(dir) * (side * (ext - 1.6f));

                    if (!_raster.IsRoad(p2) || NearJunction(p2, 4f) || Vector2.Distance(p2, LargoCenter) < 70f || InBusLane(Geo.V3(p2)))
                    {
                        continue;
                    }

                    Vector3 position = Geo.V3(p2);
                    bool close = Mathf.Max(Mathf.Abs(position.x), Mathf.Abs(position.z)) > GraphLimit - 1f;

                    foreach (Place restaurant in restaurants)
                    {
                        close |= Vector3.Distance(restaurant.Position, position) < 80f;
                    }

                    if (!close)
                    {
                        pool.Add((position, r.Name, _graph.RouteLength(restaurants[0].Position, position)));
                    }
                }
            }

            float maxDistance = 0f;

            foreach (var entry in pool)
            {
                maxDistance = Mathf.Max(maxDistance, entry.distance);
            }

            var chosen = new List<Vector3>();
            var places = new List<Place>();

            for (int k = 0; k < CustomerCount; k++)
            {
                float target = Mathf.Lerp(MinCustomerMeters, maxDistance, k / (float)(CustomerCount - 1));
                int best = -1;
                float bestError = float.MaxValue;

                for (int c = 0; c < pool.Count; c++)
                {
                    bool tooClose = false;

                    foreach (Vector3 other in chosen)
                    {
                        tooClose |= Vector3.Distance(pool[c].position, other) < 60f;
                    }

                    float error = Mathf.Abs(pool[c].distance - target);

                    if (!tooClose && error < bestError)
                    {
                        bestError = error;
                        best = c;
                    }
                }

                if (best < 0)
                {
                    break;
                }

                Vector3 position = pool[best].position;
                chosen.Add(position);
                string street = string.IsNullOrEmpty(pool[best].street) ? FallbackStreets[k] : pool[best].street;
                string name = $"{street}, {random.Next(20, 900)}";
                GameObject beam = BuildMarker(markers, name, position, m.CustomerPad, m);
                places.Add(new Place(name, position, beam));
            }

            if (places.Count < CustomerCount)
            {
                throw new System.InvalidOperationException($"Só foi possível posicionar {places.Count} endereços de cliente.");
            }

            Debug.Log($"[CityBuilder] Endereços: rota do primeiro restaurante de {places[0].Position} a até {maxDistance:0} m.");

            return places.ToArray();
        }

        // ---------- Buracos ----------

        private static List<Vector3> BuildPotholes(Transform parent, System.Random random, Place[] restaurants, Place[] customers)
        {
            var positions = new List<Vector3>();
            int made = 0;
            int guard = 0;

            while (made < 14 && guard++ < 2000)
            {
                GraphEdge edge = GraphEdges[random.Next(GraphEdges.Count)];
                Road r = edge.Road;

                if (r.Kind == RoadKind.Primary || r.Kind == RoadKind.Service)
                {
                    continue;
                }

                Vector2 a = r.Pts[edge.Segment];
                Vector2 b = r.Pts[edge.Segment + 1];
                float length = Vector2.Distance(a, b);

                if (length < 25f)
                {
                    continue;
                }

                Vector2 dir = (b - a) / length;
                float t = 0.3f + 0.4f * (float)random.NextDouble();
                float lateral = ((float)random.NextDouble() - 0.5f) * 3f;
                Vector2 p2 = a + dir * (length * t) + Geo.Left(dir) * lateral;
                Vector3 position = Geo.V3(p2);

                if (NearJunction(p2, 6f) || NearAny(position, positions, 40f))
                {
                    continue;
                }

                bool nearMarker = false;

                foreach (Place place in restaurants)
                {
                    nearMarker |= Vector3.Distance(place.Position, position) < 25f;
                }

                foreach (Place place in customers)
                {
                    nearMarker |= Vector3.Distance(place.Position, position) < 25f;
                }

                if (nearMarker)
                {
                    continue;
                }

                // Buraco: remendo de asfalto escuro, borda esfarelada e a cratera quase preta no meio (irregulares).
                var patch = new MeshBatch();
                float spin = (float)random.NextDouble() * 6f;
                float[] outer = new float[10];
                float[] mid = new float[10];
                float[] pit = new float[10];

                for (int v = 0; v < 10; v++)
                {
                    float jitter = 0.78f + 0.4f * (float)random.NextDouble();
                    outer[v] = 1.55f * jitter;
                    mid[v] = 1.15f * (0.8f + 0.4f * (float)random.NextDouble());
                    pit[v] = 0.7f * (0.7f + 0.5f * (float)random.NextDouble());
                }

                patch.Fan(CityStyle.AsphaltPatch(), position + Vector3.up * 0.026f, outer, spin);
                patch.Fan(PbrMaterials.Flat("BuracoBorda", new Color(0.09f, 0.09f, 0.1f), 0.1f), position + Vector3.up * 0.03f, mid, spin + 0.3f);
                patch.Fan(PbrMaterials.Flat("BuracoFundo", new Color(0.025f, 0.025f, 0.03f), 0.05f), position + Vector3.up * 0.034f, pit, spin + 0.7f);
                GameObject hole = patch.Build(parent, "Buraco" + made, false);
                var zone = new GameObject("ZonaBuraco" + made);
                zone.transform.SetParent(hole.transform.parent, false);
                zone.transform.position = position + Vector3.up * 1f;
                var box = zone.AddComponent<BoxCollider>();
                box.isTrigger = true;
                box.size = new Vector3(3.4f, 2f, 3.4f);
                zone.AddComponent<PotholeZone>();
                positions.Add(position);

                // Cones de obra em volta do buraco.
                for (int c = 0; c < 3; c++)
                {
                    float angle = (c * 120f + 40f * (float)random.NextDouble()) * Mathf.Deg2Rad;
                    Vector3 offset = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * 2.6f;
                    CityPieces.Model(hole.transform.parent, Kit.CityKitRoads, "construction-cone", position + offset + Vector3.up * 0.04f, 0f, 9f).name = "Cone";
                }

                made++;
            }

            return positions;
        }

        // ---------- Partida ----------

        /// <summary>Ponto de partida: no meio de um trecho reto e longo de rua perto do largo, virado no sentido do tráfego.</summary>
        private static Vector3 ChooseSpawn(out float yaw)
        {
            GraphEdge best = default;
            float bestScore = float.MaxValue;
            bool found = false;

            foreach (GraphEdge edge in GraphEdges)
            {
                Road r = edge.Road;

                if (r.Kind != RoadKind.Tertiary && r.Kind != RoadKind.Residential)
                {
                    continue;
                }

                Vector2 a = r.Pts[edge.Segment];
                Vector2 b = r.Pts[edge.Segment + 1];
                float length = Vector2.Distance(a, b);

                if (length < 55f || NearJunction((a + b) * 0.5f, 12f))
                {
                    continue;
                }

                float score = Vector2.Distance((a + b) * 0.5f, LargoCenter) - length * 0.4f;

                if (score < bestScore)
                {
                    bestScore = score;
                    best = edge;
                    found = true;
                }
            }

            if (!found)
            {
                throw new System.InvalidOperationException("Nenhum trecho reto de rua para a partida.");
            }

            Road road = best.Road;
            Vector2 p0 = road.Pts[best.Segment];
            Vector2 p1 = road.Pts[best.Segment + 1];
            Vector2 dir = (p1 - p0).normalized;
            Vector2 flow = road.Oneway ? road.FlowDirection(best.Segment) : dir;
            Vector2 position = (p0 + p1) * 0.5f + Geo.Right(flow) * (road.Oneway ? 0f : 1.6f);
            yaw = Mathf.Atan2(flow.x, flow.y) * Mathf.Rad2Deg;

            return Geo.V3(position, 0.1f);
        }

        // ---------- O largo ----------

        /// <summary>Fonte de concreto com água, bancos, árvores e postes de luz em cima do piso do largo (só onde há piso livre).</summary>
        private static void BuildLargoProps(Transform props, List<Vector3> avoid)
        {
            CityRaster r = _raster;

            bool Free(Vector2 p, float clearance = 1f) => r.ZoneAt(p) == ZoneLargo && r.At(p) >= 0 && r.Occ[r.At(p)] == 0 && r.DistAt(p) > clearance;

            // Centro: o ponto livre mais próximo do centro do largo que tenha folga para a fonte.
            bool placedFountain = false;
            float bestDistance = float.MaxValue;

            for (float dx = -34f; dx <= 34f; dx += 2f)
            {
                for (float dz = -34f; dz <= 34f; dz += 2f)
                {
                    var c = LargoCenter + new Vector2(dx, dz);
                    float d = Mathf.Sqrt(dx * dx + dz * dz);

                    if (d >= bestDistance || !Free(c))
                    {
                        continue;
                    }

                    bool clear = true;

                    for (int k = 0; k < 12 && clear; k++)
                    {
                        float a = k * Mathf.PI / 6f;
                        clear = Free(c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 7f, 0.5f);
                    }

                    if (clear)
                    {
                        bestDistance = d;
                        _fountain = c;
                        placedFountain = true;
                    }
                }
            }

            const float height = CurbHeight + 0.01f;
            MeshBatch b = BuildBatch(Geo.V3(_fountain));
            Material concrete = CityStyle.ConcreteWall();

            if (placedFountain)
            {
                Vector3 c0 = Geo.V3(_fountain, height);
                b.Frustum(concrete, c0, 4.2f, 4.0f, 0.9f, 20, false);
                b.Frustum(concrete, c0, 3.7f, 3.7f, 0.82f, 20, false);
                b.Fan(CityStyle.Water(), c0 + Vector3.up * 0.8f, Fill(20, 3.7f));
                b.Frustum(concrete, c0, 0.7f, 0.45f, 2.4f, 12, false);
                b.Frustum(concrete, c0 + Vector3.up * 2.4f, 0.4f, 1.5f, 0.45f, 16, true);
                b.Fan(CityStyle.Water(), c0 + Vector3.up * 2.82f, Fill(16, 1.3f));

                var fountain = new GameObject("LargoFonte");
                fountain.transform.SetParent(props, false);
                fountain.transform.position = c0 + Vector3.up * 0.6f;
                fountain.AddComponent<BoxCollider>().size = new Vector3(7.6f, 1.2f, 7.6f);
                avoid.Add(c0);
                LargoSolids.Add(_fountain);

                // Bancos de concreto e madeira em volta, voltados para a fonte.
                Material wood = PbrMaterials.Flat("MadeiraBanco", new Color(0.4f, 0.26f, 0.15f), 0.3f);

                for (int k = 0; k < 8; k++)
                {
                    float angle = k * 45f + 22.5f;
                    var direction = new Vector2(Mathf.Cos(angle * Mathf.Deg2Rad), Mathf.Sin(angle * Mathf.Deg2Rad));
                    Vector2 p2 = _fountain + direction * 12f;

                    if (!Free(p2, 1.5f))
                    {
                        continue;
                    }

                    Vector3 facing = Geo.V3(-direction);
                    var frame = new Frame(Geo.V3(p2, height) - Vector3.Cross(facing, Vector3.up) * 1f, facing);
                    b.Box(wood, frame.Pt(0f, 0.42f, 0f), frame.F, 2f, 0.07f, 0.5f);
                    b.Box(wood, frame.Pt(0f, 0.7f, 0.5f), frame.F, 2f, 0.3f, 0.06f);
                    b.Box(concrete, frame.Pt(0.1f, 0f, 0.05f), frame.F, 0.1f, 0.42f, 0.4f);
                    b.Box(concrete, frame.Pt(1.8f, 0f, 0.05f), frame.F, 0.1f, 0.42f, 0.4f);
                }
            }

            // Árvores em volta, afastadas da fonte e das ruas.
            int made = 0;

            for (int k = 0; k < 40 && made < 18; k++)
            {
                float angle = k * 2.399f;
                float radius = 16f + 26f * ((k * 0.618f) % 1f);
                Vector2 p2 = _fountain + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
                var p = Geo.V3(p2, height);

                if (!Free(p2, 3f) || NearAny(p, avoid, 7f))
                {
                    continue;
                }

                Tree(b, p, 8f + (made % 3), 900 + made, true);
                SolidCapsule(props, "LargoArvoreTronco" + made, p, 0.25f, 3f);
                avoid.Add(p);
                LargoSolids.Add(p2);
                made++;
            }

            // Postes de luz do largo.
            foreach (Vector2 s in new[] { new Vector2(-20f, -20f), new Vector2(20f, -20f), new Vector2(-20f, 20f), new Vector2(20f, 20f) })
            {
                Vector2 p2 = _fountain + s;

                if (!Free(p2, 2f))
                {
                    continue;
                }

                var p = Geo.V3(p2, height);
                StreetLamp(b, p, Geo.V3(-s).normalized);
                SolidCapsule(props, "LargoPoste", p, 0.2f, 3f);
                avoid.Add(p);
                LargoSolids.Add(p2);
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

        // ---------- Pontos de vista das capturas ----------

        /// <summary>Ponto a meters de distância (ao longo da rua) do vértice k, para trás (step -1) ou para a frente (step 1). Falso se a rua acaba antes.</summary>
        private static bool TryAlong(Road r, int k, float meters, int step, out Vector2 point)
        {
            float remaining = meters;
            int i = k;
            point = r.Pts[k];

            while (i + step >= 0 && i + step < r.Pts.Count)
            {
                Vector2 from = r.Pts[i];
                Vector2 to = r.Pts[i + step];
                float length = Vector2.Distance(from, to);

                if (length >= remaining)
                {
                    point = Vector2.Lerp(from, to, remaining / Mathf.Max(0.001f, length));

                    return true;
                }

                remaining -= length;
                i += step;
            }

            return false;
        }

        private static ViewPoint View(string name, Vector2 position, Vector2 lookDirection)
        {
            return new ViewPoint(name, Geo.V3(position, 0.1f), Mathf.Atan2(lookDirection.x, lookDirection.y) * Mathf.Rad2Deg);
        }

        /// <summary>Pontos de vista que os testes de captura usam: rua residencial, o largo, a avenida do corredor, um cruzamento com semáforo, uma curva e os restaurantes.</summary>
        private static ViewPoint[] BuildViewPoints(OsmMap map)
        {
            var points = new List<ViewPoint>();

            // Rua residencial: o trecho reto mais longo, longe do largo.
            Road residential = null;
            int segment = 0;
            float bestLength = 0f;

            foreach (Road r in Roads)
            {
                if (r.Kind != RoadKind.Residential)
                {
                    continue;
                }

                for (int k = 0; k + 1 < r.Pts.Count; k++)
                {
                    float length = Vector2.Distance(r.Pts[k], r.Pts[k + 1]);

                    if (length > bestLength && length > 60f && Vector2.Distance(r.Pts[k], LargoCenter) > 120f && !NearJunction((r.Pts[k] + r.Pts[k + 1]) * 0.5f, 12f))
                    {
                        bestLength = length;
                        residential = r;
                        segment = k;
                    }
                }
            }

            if (residential != null)
            {
                Vector2 dir = (residential.Pts[segment + 1] - residential.Pts[segment]).normalized;
                Vector2 flow = residential.Reverse ? -dir : dir;
                Vector2 at = Vector2.Lerp(residential.Pts[segment], residential.Pts[segment + 1], residential.Reverse ? 0.75f : 0.25f);
                points.Add(View("residencial", at + Geo.Right(flow) * (residential.Oneway ? 0f : 1.6f), flow));
            }

            // Avenida com corredor de ônibus: a pista de sentido único com faixa vermelha mais longa.
            Road avenue = null;
            int avenueSegment = 0;
            bestLength = 0f;

            foreach (Road r in Roads)
            {
                if (r.Kind != RoadKind.Primary || r.BusLeft + r.BusRight < 0.5f)
                {
                    continue;
                }

                for (int k = 0; k + 1 < r.Pts.Count; k++)
                {
                    float length = Vector2.Distance(r.Pts[k], r.Pts[k + 1]);

                    float score = length + (LoopSegments.Contains((r, k)) ? 1000f : 0f);

                    if (score > bestLength && length > 30f && !NearJunction((r.Pts[k] + r.Pts[k + 1]) * 0.5f, 10f))
                    {
                        bestLength = score;
                        avenue = r;
                        avenueSegment = k;
                    }
                }
            }

            if (avenue != null)
            {
                Vector2 dir = (avenue.Pts[avenueSegment + 1] - avenue.Pts[avenueSegment]).normalized;
                Vector2 flow = avenue.Reverse ? -dir : dir;
                Vector2 at = Vector2.Lerp(avenue.Pts[avenueSegment], avenue.Pts[avenueSegment + 1], avenue.Reverse ? 0.8f : 0.2f);
                points.Add(View("avenida", at, flow));
                _avenueAnchor = Geo.V3(at + flow * 24f);
                _avenueAnchorSet = true;
            }

            // Largo: da borda do piso olhando para a fonte, num ponto livre e sem árvore, poste ou banco no caminho.
            Vector2 largoPosition = _fountain + new Vector2(2f, -22f);

            for (int k = 0; k < 24; k++)
            {
                float angle = (-90f + (k % 2 == 0 ? 1 : -1) * (k / 2) * 15f) * Mathf.Deg2Rad;
                Vector2 candidate = _fountain + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * 21f;
                bool clear = _raster.ZoneAt(candidate) == ZoneLargo && _raster.DistAt(candidate) > 2f && _raster.Occ[_raster.At(candidate)] == 0;

                foreach (Vector2 solid in LargoSolids)
                {
                    clear &= Vector2.Distance(solid, candidate) > 4.5f && (Vector2.Distance(solid, _fountain) < 1f || Geo.DistanceToSegment(solid, candidate, _fountain, out _, out _) > 3.5f);
                }

                if (clear)
                {
                    largoPosition = candidate;

                    break;
                }
            }

            points.Add(View("largo", largoPosition, _fountain - largoPosition));

            // Cruzamento com semáforo: 40 m antes do cruzamento, na pista de quem chega.
            foreach (OsmNode node in map.Nodes)
            {
                if (node.Tag("highway") != "traffic_signals" || !map.InsideBox(node.Pos) || points.Exists(p => p.Name == "cruzamento"))
                {
                    continue;
                }

                foreach (SignalArm arm in SignalArms(node.Pos))
                {
                    if (arm.Approach && arm.Length > 55f && arm.Road.Class >= RoadClass.Tertiary)
                    {
                        points.Add(View("cruzamento", arm.Node + arm.Dir * 38f, -arm.Dir));

                        break;
                    }
                }
            }

            // Curva: vértice de rua em que a direção muda de 28 a 75 graus entre os pontos a 28 m antes e a 28 m depois.
            float bestError = float.MaxValue;
            ViewPoint curve = null;

            foreach (Road r in Roads)
            {
                if (r.Kind != RoadKind.Residential && r.Kind != RoadKind.Tertiary)
                {
                    continue;
                }

                for (int k = 1; k + 1 < r.Pts.Count; k++)
                {
                    if (!TryAlong(r, k, 28f, -1, out Vector2 before) || !TryAlong(r, k, 28f, 1, out Vector2 after) || NearJunction(r.Pts[k], 12f))
                    {
                        continue;
                    }

                    float turn = Vector2.Angle(r.Pts[k] - before, after - r.Pts[k]);
                    float error = Mathf.Abs(turn - 45f);

                    if (turn < 28f || turn > 75f || error >= bestError)
                    {
                        continue;
                    }

                    bestError = error;
                    Vector2 approach = r.Reverse ? (r.Pts[k] - after).normalized : (r.Pts[k] - before).normalized;
                    curve = View("curva", (r.Reverse ? after : before) + Geo.Right(approach) * (r.Oneway ? 0f : 1.6f), approach);
                }
            }

            if (curve != null)
            {
                points.Add(curve);
            }

            // Restaurantes: da rua, olhando para a fachada de viés.
            for (int k = 0; k < RestaurantSites.Count; k++)
            {
                RestaurantSite site = RestaurantSites[k];
                Vector2 position = site.Marker - site.Dir * 14f + site.Front * 1.5f;
                Vector2 target = site.Marker - site.Front * 7f;
                points.Add(View("restaurante" + k, position, target - position));
            }

            points.AddRange(BorderViews);

            return points.ToArray();
        }
    }
}

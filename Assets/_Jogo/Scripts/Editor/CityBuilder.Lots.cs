using Entregador.Game;
using System.Collections.Generic;
using UnityEngine;

namespace Entregador.Editor
{
    /// <summary>
    /// Prédios: as pegadas do OpenStreetMap viram prédios procedurais (CityBuildings) e o resto da frente dos quarteirões é preenchido
    /// com lotes voltados para a rua mais próxima, para toda rua ter fachada contínua; os fundos ganham casinhas e galpões baixos.
    /// A grade de ocupação (CityRaster.Occ) impede que dois prédios se sobreponham.
    /// </summary>
    public static partial class CityBuilder
    {
        /// <summary>Quarteirões a mais de 250 m do largo usam detalhe reduzido (BuildingContext.Lite).</summary>
        private const float LiteRadius = 250f;

        private static readonly string[] RestaurantNames = { "Pastelaria do Largo", "Pizzaria Bella Amaro", "Marmitaria da Dona Cida", "Hamburgueria Zona Sul" };

        private static Transform _signsRoot;
        private static Transform _detailsRoot;
        private static bool _borrachariaDone;
        private static Material _footprintMaterial;
        public static readonly int[] KindStats = new int[6];
        public static readonly int[] KindCount = new int[6];

        // ---------- Retângulos na grade ----------

        /// <summary>Retângulo anti-horário: o começa na frente (esquerda de quem olha a rua), segue dir por w e entra no quarteirão por depth.</summary>
        private static Vector2[] Rect4(Vector2 o, Vector2 dir, Vector2 front, float w, float depth)
        {
            Vector2 p1 = o + dir * w;

            return new[] { o, p1, p1 - front * depth, o - front * depth };
        }

        private static Vector2[] Shrink(Vector2[] q, float margin)
        {
            Vector2 center = (q[0] + q[1] + q[2] + q[3]) * 0.25f;
            var result = new Vector2[4];

            for (int i = 0; i < 4; i++)
            {
                result[i] = q[i] + (center - q[i]).normalized * (margin * 1.4142f);
            }

            return result;
        }

        private static bool Fits(int label, Vector2[] q, float minDist, bool allowZone)
        {
            CityRaster r = _raster;
            Vector2 center = (q[0] + q[1] + q[2] + q[3]) * 0.25f;
            Vector2[] probes = { q[0], q[1], q[2], q[3], center, (q[0] + q[1]) * 0.5f, (q[2] + q[3]) * 0.5f, (q[1] + q[2]) * 0.5f, (q[3] + q[0]) * 0.5f };

            foreach (Vector2 p in probes)
            {
                int i = r.At(p);

                if (i < 0 || r.Block[i] != label || r.Dist[i] < minDist || r.Occ[i] != 0 || (!allowZone && r.Zone[i] != 0))
                {
                    return false;
                }
            }

            bool free = true;
            r.FillPolygon(Shrink(q, 0.2f), i =>
            {
                if (r.Occ[i] != 0 || r.Block[i] != label || (!allowZone && r.Zone[i] != 0))
                {
                    free = false;
                }
            });

            return free;
        }

        private static void PaintOccupancy(Vector2[] q, byte value)
        {
            CityRaster r = _raster;
            r.FillPolygon(q, i =>
            {
                if (r.Block[i] != 0 && r.Occ[i] < value)
                {
                    r.Occ[i] = value;
                }
            });

            // Pegada escura em cima da calçada: só aparece no minimapa e na visão de cima (os prédios ficam na camada oculta).
            if (_footprintMaterial == null)
            {
                _footprintMaterial = PbrMaterials.Flat("PegadaPredio", new Color(0.78f, 0.72f, 0.6f), 0.1f);
            }

            Vector2 center = (q[0] + q[1] + q[2] + q[3]) * 0.25f;
            const float y = CurbHeight + 0.004f;
            GroundBatch(Geo.V3(center)).FlatQuad(_footprintMaterial, Geo.V3(q[0], y), Geo.V3(q[1], y), Geo.V3(q[2], y), Geo.V3(q[3], y));
        }

        // ---------- Prédios do OSM ----------

        private static int FloorsOf(OsmWay way, float area, bool commercial, System.Random r)
        {
            if (int.TryParse(way.Tag("building:levels"), out int levels) && levels > 0)
            {
                return Mathf.Clamp(levels, 1, 16);
            }

            if (float.TryParse(way.Tag("height"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float height) && height > 2f)
            {
                return Mathf.Clamp(Mathf.RoundToInt(height / 3.2f), 1, 16);
            }

            if (!commercial)
            {
                return r.NextDouble() < 0.5 ? 2 : 3;
            }

            if (area > 1500f)
            {
                return 3 + r.Next(3);
            }

            return r.NextDouble() < 0.12 ? 5 + r.Next(4) : 2 + r.Next(3);
        }

        private static void PlaceOsmBuildings(OsmMap map)
        {
            var random = new System.Random(321);
            int placed = 0;
            int skipped = 0;

            foreach (OsmWay way in map.Ways)
            {
                string kind = way.Tag("building");

                if (kind == null || kind == "roof" || !way.Closed || way.Tag("amenity") == "bicycle_parking")
                {
                    continue;
                }

                List<Vector2> poly = Geo.ClipPolygon(way.Points, map.HalfX, map.HalfZ);

                if (poly.Count < 3)
                {
                    continue;
                }

                float area = Geo.Area(poly);

                if (area < 0f)
                {
                    poly.Reverse();
                    area = -area;
                }

                if (area < 25f)
                {
                    continue;
                }

                if (PlaceFootprint(poly, area, way, random))
                {
                    placed++;
                }
                else
                {
                    skipped++;
                }
            }

            Debug.Log($"[CityBuilder] Prédios do OSM: {placed} colocados, {skipped} fora das ruas ou sobrepostos.");
        }

        private static bool PlaceFootprint(List<Vector2> poly, float area, OsmWay way, System.Random rnd)
        {
            CityRaster r = _raster;
            Vector2 c = Geo.Centroid(poly);
            int label = r.BlockAt(c);

            if (label == 0)
            {
                foreach (Vector2 v in poly)
                {
                    label = r.BlockAt(v);

                    if (label != 0)
                    {
                        break;
                    }
                }
            }

            if (label == 0 || !BlocksByLabel.TryGetValue(label, out List<Block> candidates))
            {
                return false;
            }

            // A fachada olha para a aresta de quarteirão mais próxima.
            Block block = null;
            int edge = -1;
            float best = float.MaxValue;

            foreach (Block candidate in candidates)
            {
                for (int i = 0; i < candidate.Poly.Count; i++)
                {
                    float d = Geo.DistanceToSegment(c, candidate.Poly[i], candidate.Poly[(i + 1) % candidate.Poly.Count], out _, out _);

                    if (d < best)
                    {
                        best = d;
                        block = candidate;
                        edge = i;
                    }
                }
            }

            Vector2 dir = (block.Poly[(edge + 1) % block.Poly.Count] - block.Poly[edge]).normalized;
            Vector2 front = Geo.Right(dir);
            float smin = float.MaxValue;
            float smax = float.MinValue;
            float tmin = float.MaxValue;
            float tmax = float.MinValue;

            foreach (Vector2 v in poly)
            {
                float s = Vector2.Dot(v - c, dir);
                float t = Vector2.Dot(v - c, -front);
                smin = Mathf.Min(smin, s);
                smax = Mathf.Max(smax, s);
                tmin = Mathf.Min(tmin, t);
                tmax = Mathf.Max(tmax, t);
            }

            float w = smax - smin;
            float depth = tmax - tmin;
            float sc = (smin + smax) * 0.5f;
            float tc = (tmin + tmax) * 0.5f;
            float ratio = area / Mathf.Max(1f, w * depth);

            if (ratio < 0.8f)
            {
                float k = Mathf.Clamp(Mathf.Sqrt(ratio / 0.8f), 0.6f, 1f);
                w *= k;
                depth *= k;
            }

            for (int attempt = 0; attempt < 5; attempt++)
            {
                if (w < 4f || depth < 4f)
                {
                    return false;
                }

                Vector2 o = c + dir * (sc - w * 0.5f) - front * (tc - depth * 0.5f);
                Vector2[] q = Rect4(o, dir, front, w, depth);

                if (Fits(label, q, 0.5f, true))
                {
                    BuildFootprint(block, edge, o, dir, front, w, depth, area, way, rnd);
                    PaintOccupancy(q, 2);

                    return true;
                }

                w *= 0.88f;
                depth *= 0.88f;
            }

            return false;
        }

        private static void BuildFootprint(Block block, int edge, Vector2 o, Vector2 dir, Vector2 front, float w, float depth, float area, OsmWay way, System.Random rnd)
        {
            RoadClass cls = block.EdgeClass[edge];
            Vector2 mid = o + dir * (w * 0.5f) - front * (depth * 0.5f);
            float toLargo = Vector2.Distance(mid, LargoCenter);
            bool commercial = cls >= RoadClass.Tertiary || cls == RoadClass.Pedestrian || toLargo < 160f || way.Tag("shop") != null || way.Tag("amenity") != null;
            int floors = FloorsOf(way, area, commercial, rnd);
            var frame = new Frame(Geo.V3(o, CurbHeight), Geo.V3(front));
            var context = new BuildingContext { B = BuildBatch(Geo.V3(mid)), Rnd = rnd, Signs = _signsRoot, Lite = toLargo > LiteRadius, ExposeLeft = true, ExposeRight = true };
            CityBuildings.Shop shop = CityBuildings.RandomShop(rnd, cls >= RoadClass.Tertiary);

            if (floors >= 5)
            {
                CityBuildings.Tower(context, frame, w, depth, shop, floors - 1, depth);
            }
            else if (commercial || w > 16f || depth > 16f || area > 300f)
            {
                CityBuildings.Commerce(context, frame, w, depth, shop, Mathf.Max(1, floors), depth);
            }
            else
            {
                CityBuildings.Sobrado(context, frame, w, depth);
            }
        }

        // ---------- Restaurantes ----------

        private sealed class RestaurantSite
        {
            public Block Block;
            public int Edge;
            public Vector2 FrontStart;
            public Vector2 Dir;
            public Vector2 Front;
            public Vector2 Marker;
            public Vector2[] Rect;
        }

        private const float RestaurantWidth = 24f;
        private static readonly List<RestaurantSite> RestaurantSites = new List<RestaurantSite>();

        /// <summary>
        /// Quatro pontos de restaurante em frentes de rua reais perto do largo e das avenidas: trechos de calçada com 24 m livres, com o
        /// marcador na rua, rente à guia, ao alcance do grafo. Distâncias-alvo ao largo espalham os quatro.
        /// </summary>
        private static void ChooseRestaurantSites(RoadGraph graph)
        {
            RestaurantSites.Clear();
            var candidates = new List<RestaurantSite>();
            var costs = new List<float>();
            float[] targets = { 75f, 150f, 220f, 290f };

            foreach (Block block in Blocks)
            {
                int n = block.Poly.Count;

                for (int e = 0; e < n; e++)
                {
                    RoadClass cls = block.EdgeClass[e];

                    if (cls < RoadClass.Residential)
                    {
                        continue;
                    }

                    Vector2 a = block.Poly[e];
                    Vector2 b = block.Poly[(e + 1) % n];
                    float length = Vector2.Distance(a, b);

                    if (length < RestaurantWidth + 8f)
                    {
                        continue;
                    }

                    Vector2 dir = (b - a) / length;
                    Vector2 front = Geo.Right(dir);

                    for (float s = 4f; s + RestaurantWidth + 4f < length; s += 5f)
                    {
                        Vector2 o = a + dir * s - front * FrontInset;
                        Vector2[] q = Rect4(o, dir, front, RestaurantWidth, FrontInset + 11.5f);
                        Vector2 boundary = a + dir * (s + RestaurantWidth * 0.5f);
                        Vector2 marker = boundary + front * 1.6f;

                        if (!_raster.IsRoad(marker) || NearJunction(marker, 4f) || InBusLane(Geo.V3(marker)) || !Fits(block.Label, q, 2.2f, false))
                        {
                            continue;
                        }

                        graph.NearestEdge(Geo.V3(marker), out Vector3 snapped);

                        if (Vector2.Distance(Geo.V2(snapped), marker) > 11f)
                        {
                            continue;
                        }

                        candidates.Add(new RestaurantSite { Block = block, Edge = e, FrontStart = o, Dir = dir, Front = front, Marker = marker, Rect = q });
                        costs.Add(cls >= RoadClass.Tertiary ? -35f : 0f);
                    }
                }
            }

            for (int k = 0; k < 4; k++)
            {
                int best = -1;
                float bestScore = float.MaxValue;

                for (int i = 0; i < candidates.Count; i++)
                {
                    float d = Vector2.Distance(candidates[i].Marker, LargoCenter);
                    float score = Mathf.Abs(d - targets[k]) + costs[i];

                    foreach (RestaurantSite taken in RestaurantSites)
                    {
                        float apart = Vector2.Distance(taken.Marker, candidates[i].Marker);

                        if (apart < 130f)
                        {
                            score += (130f - apart) * 3f;
                        }
                    }

                    if (score < bestScore)
                    {
                        bestScore = score;
                        best = i;
                    }
                }

                if (best < 0)
                {
                    throw new System.InvalidOperationException("Sem frente de rua livre para o restaurante " + k);
                }

                RestaurantSites.Add(candidates[best]);
                Debug.Log($"[CityBuilder] Restaurante {k}: {candidates[best].Marker} a {Vector2.Distance(candidates[best].Marker, LargoCenter):0} m do largo.");
                PaintOccupancy(candidates[best].Rect, 1);
                candidates.RemoveAt(best);
                costs.RemoveAt(best);
            }
        }

        /// <summary>Marcadores e letreiros dos restaurantes. O prédio, os toldos e as mesas saem em BuildRestaurantBuildings.</summary>
        private static Place[] BuildRestaurants(Transform markers, Materials m)
        {
            var places = new Place[RestaurantSites.Count];

            for (int k = 0; k < places.Length; k++)
            {
                Vector3 position = Geo.V3(RestaurantSites[k].Marker);
                GameObject beam = BuildMarker(markers, RestaurantNames[k], position, m.RestaurantPad, m);
                places[k] = new Place(RestaurantNames[k], position, beam);
            }

            return places;
        }

        /// <summary>Restaurante: prédio largo recuado do meio-fio (terraço de 3 m) com toldos, porta aberta, letreiro e mesas.</summary>
        private static void BuildRestaurantBuildings(Materials m)
        {
            const float terrace = 3f;

            for (int k = 0; k < RestaurantSites.Count; k++)
            {
                RestaurantSite site = RestaurantSites[k];
                Vector2 corner = site.FrontStart - site.Front * terrace;
                Color accent = m.Restaurants[k % m.Restaurants.Length].GetColor("_BaseColor");
                var context = new BuildingContext
                {
                    B = BuildBatch(Geo.V3(corner)),
                    Rnd = new System.Random(77 + k),
                    Signs = _signsRoot,
                    Lite = false,
                    ExposeLeft = true,
                    ExposeRight = true,
                };
                CityBuildings.Restaurant(context, new Frame(Geo.V3(corner, CurbHeight), Geo.V3(site.Front)), RestaurantWidth, 11.5f, RestaurantNames[k], k, accent);
            }
        }

        // ---------- Frente dos quarteirões ----------

        private static void FillFrontage()
        {
            foreach (Block block in Blocks)
            {
                var rnd = new System.Random(1000 + block.Label * 31 + block.Poly.Count);
                int n = block.Poly.Count;

                for (int e = 0; e < n; e++)
                {
                    RoadClass cls = block.EdgeClass[e];
                    Vector2 a = block.Poly[e];
                    Vector2 b = block.Poly[(e + 1) % n];
                    float length = Vector2.Distance(a, b);

                    if (cls == RoadClass.None || length < 5f)
                    {
                        continue;
                    }

                    float toLargo = Vector2.Distance((a + b) * 0.5f, LargoCenter);
                    bool avenue = cls >= RoadClass.Tertiary;
                    bool commercial = avenue || cls == RoadClass.Pedestrian || toLargo < 160f;
                    FillEdge(block, a, (b - a) / length, length, commercial, avenue, toLargo > LiteRadius, rnd);
                }
            }

            Debug.Log($"[CityBuilder] Lotes decididos: {PendingLots.Count}.");
        }

        private static void FillEdge(Block block, Vector2 a, Vector2 dir, float length, bool commercial, bool avenue, bool lite, System.Random r)
        {
            Vector2 front = Geo.Right(dir);
            float s = 0f;
            int guard = 0;

            while (length - s > 4.5f && guard++ < 300)
            {
                float remaining = length - s;
                int kind;
                float w;

                if (!commercial)
                {
                    kind = 0;
                    w = 9f + 5f * (float)r.NextDouble();
                }
                else if (avenue && remaining >= 26f && r.NextDouble() < 0.34)
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
                    w *= 0.5f;
                }

                // Tenta o lote; se não couber (esquina, lote vizinho, fundo curto), desliza um pouco, estreita e, por fim, pula.
                float[] shifts = { 0f, 1.5f, 3f, 0f, 4.5f, 7.5f, 0f, 10.5f, 13.5f, 16.5f };
                float[] scales = { 1f, 1f, 1f, 0.7f, 0.7f, 0.7f, 0.45f, 0.7f, 0.7f, 0.7f };
                bool placed = false;

                for (int attempt = 0; attempt < shifts.Length && !placed; attempt++)
                {
                    float start = s + shifts[attempt];
                    float width = Mathf.Max(4.5f, w * scales[attempt]);

                    if (start + width > length + 0.5f)
                    {
                        continue;
                    }

                    if (TryPlaceLot(block, a, dir, front, start, width, kind, avenue, lite, r))
                    {
                        s = start + width;
                        placed = true;
                    }
                }

                if (!placed)
                {
                    s += 2.5f;
                }
            }
        }

        /// <summary>Lote decidido na frente de um quarteirão; o prédio só é gerado depois (BuildLots), quando se sabe quais paredes laterais ficam expostas.</summary>
        private sealed class PendingLot
        {
            public Vector2[] Rect;
            public Vector2 Origin;
            public Vector2 Dir;
            public Vector2 Front;
            public float Width;
            public float Depth;
            public int Kind;
            public bool Avenue;
            public bool Lite;
            public System.Random Rnd;
        }

        private static readonly List<PendingLot> PendingLots = new List<PendingLot>();

        private static bool TryPlaceLot(Block block, Vector2 a, Vector2 dir, Vector2 front, float s, float w, int kind, bool avenue, bool lite, System.Random r)
        {
            Vector2 o = a + dir * s - front * FrontInset;
            float[] depths = kind == 2 ? new[] { RowDepth, 12f } : new[] { RowDepth, 11f, 8f, 5.5f };

            foreach (float depth in depths)
            {
                Vector2[] q = Rect4(o, dir, front, w, depth);

                if (!Fits(block.Label, q, 2.2f, false))
                {
                    continue;
                }

                PendingLots.Add(new PendingLot { Rect = q, Origin = o, Dir = dir, Front = front, Width = w, Depth = depth, Kind = kind, Avenue = avenue, Lite = lite, Rnd = r });
                PaintOccupancy(q, 1);

                return true;
            }

            return false;
        }

        private static bool Occupied(Vector2 p)
        {
            int i = _raster.At(p);

            return i >= 0 && _raster.Block[i] != 0 && _raster.Occ[i] != 0;
        }

        /// <summary>Gera os prédios dos lotes decididos. Uma parede lateral ganha janelas só se o que está colado nela está livre (esquina ou vão).</summary>
        private static void BuildLots()
        {
            foreach (PendingLot lot in PendingLots)
            {
                Vector2[] q = lot.Rect;
                Vector2 mid = (q[0] + q[2]) * 0.5f;
                Vector2 leftSide = (q[0] + q[3]) * 0.5f - lot.Dir * 0.6f;
                Vector2 rightSide = (q[1] + q[2]) * 0.5f + lot.Dir * 0.6f;
                var frame = new Frame(Geo.V3(lot.Origin, CurbHeight), Geo.V3(lot.Front));
                var context = new BuildingContext
                {
                    B = BuildBatch(Geo.V3(mid)),
                    Rnd = lot.Rnd,
                    Signs = _signsRoot,
                    Lite = lot.Lite,
                    ExposeLeft = !Occupied(leftSide),
                    ExposeRight = !Occupied(rightSide),
                };
                int before = context.B.TriangleCount;
                float w = lot.Width;
                float depth = lot.Depth;
                System.Random r = lot.Rnd;

                if (lot.Kind == 0)
                {
                    CityBuildings.Sobrado(context, frame, w, depth - 0.5f);
                }
                else if (lot.Kind == 1)
                {
                    CityBuildings.Shop shop = CityBuildings.RandomShop(r, lot.Avenue);
                    int floors = 2 + r.Next(3);
                    float toLargo = Vector2.Distance(mid, LargoCenter);

                    if (!_borrachariaDone && lot.Avenue && w >= 9f && toLargo > 90f && toLargo < 320f)
                    {
                        shop = new CityBuildings.Shop { Name = "Borracharia 24h", Board = 3, Kind = 0, Awning = false, Tires = true };
                        _borrachariaDone = true;
                        context.Lite = false;
                        var marker = new GameObject("Borracharia");
                        marker.transform.SetParent(_detailsRoot, false);
                        marker.transform.position = frame.Pt(w * 0.5f, 1.5f, 0f);
                        marker.transform.rotation = Quaternion.LookRotation(frame.F, Vector3.up);
                    }

                    CityBuildings.Commerce(context, frame, w, depth - 1f, shop, floors);
                }
                else
                {
                    CityBuildings.Tower(context, frame, w, depth - 1f, CityBuildings.RandomShop(r, true), 6 + r.Next(7));
                }

                int index = lot.Kind * 2 + (lot.Lite ? 1 : 0);
                KindStats[index] += context.B.TriangleCount - before;
                KindCount[index]++;
            }

            Debug.Log("[CityBuilder] Lotes (sobrado completo/lite, comércio c/l, torre c/l): " + string.Join(",", KindCount) + " tris " + string.Join(",", KindStats));
        }

        // ---------- Fundos ----------

        /// <summary>Fundos do quarteirão: casinhas e galpões baixos com laje onde sobrou espaço, para nunca haver um vazio visto de cima.</summary>
        private static void FillInterior()
        {
            foreach (Block block in Blocks)
            {
                var r = new System.Random(5000 + block.Label * 17 + block.Poly.Count);
                float minX = float.MaxValue;
                float maxX = float.MinValue;
                float minZ = float.MaxValue;
                float maxZ = float.MinValue;

                foreach (Vector2 p in block.Poly)
                {
                    minX = Mathf.Min(minX, p.x);
                    maxX = Mathf.Max(maxX, p.x);
                    minZ = Mathf.Min(minZ, p.y);
                    maxZ = Mathf.Max(maxZ, p.y);
                }

                foreach (float cell in new[] { 12f, 6f })
                {
                    for (float x = Mathf.Floor(minX / cell) * cell; x < maxX; x += cell)
                    {
                        for (float z = Mathf.Floor(minZ / cell) * cell; z < maxZ; z += cell)
                        {
                            if (_raster.BlockAt(new Vector2(x + cell * 0.5f, z + cell * 0.5f)) != block.Label || (cell > 8f && r.NextDouble() < 0.1))
                            {
                                continue;
                            }

                            float w = cell - 1.2f;
                            Vector2[] q = Rect4(new Vector2(x + 0.6f, z + 0.6f), Vector2.right, Vector2.down, w, w);

                            if (!Fits(block.Label, q, 9f, false))
                            {
                                continue;
                            }

                            float h = 4f + 5f * (float)r.NextDouble();
                            Vector2 center = (q[0] + q[2]) * 0.5f;
                            BuildBatch(Geo.V3(center)).AxisBox(CityStyle.Plaster(r), new Vector3(center.x, CurbHeight + h * 0.5f, center.y), new Vector3(w, h, w));
                            PaintOccupancy(q, 1);
                        }
                    }
                }
            }
        }
    }
}

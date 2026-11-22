using Entregador.Game;
using System.Collections.Generic;
using UnityEngine;

namespace Entregador.Editor
{
    /// <summary>
    /// Malha viária do OpenStreetMap: ruas, largura por tipo (ou pelas tags lanes/width), corredores de ônibus, ruas de pedestres,
    /// pintura das ruas na grade (de onde saem os quarteirões), faixas, faixas de pedestre, semáforos e o grafo de rotas.
    /// O asfalto em si é um único chão: tudo que não é quarteirão é rua, então os cruzamentos nunca têm vãos nem z-fighting.
    /// </summary>
    public static partial class CityBuilder
    {
        private enum RoadKind
        {
            Service,
            Residential,
            Tertiary,
            Primary,
            Busway,
            Pedestrian,
        }

        /// <summary>Uma rua (um pedaço de way do OSM, já recortado pela caixa) com as larguras em cada vértice.</summary>
        private sealed class Road
        {
            public long WayId;
            public string Name;
            public RoadKind Kind;
            public List<Vector2> Pts;
            public List<long> Ids;

            /// <summary>Sentido único; Reverse quando o sentido é contrário à ordem dos pontos (oneway=-1).</summary>
            public bool Oneway;
            public bool Reverse;
            public bool Roundabout;
            public float LaneWidth;

            /// <summary>Limite de velocidade em km/h (tag maxspeed do OSM; 0 se não há).</summary>
            public int MaxSpeed;

            /// <summary>Meia-largura das faixas comuns (o eixo do grafo é o centro delas) e largura das faixas de ônibus de cada lado.</summary>
            public float GenHalf;
            public float BusLeft;
            public float BusRight;

            /// <summary>Extensão total até a borda, à esquerda e à direita do eixo, em cada vértice (inclui as faixas de ônibus).</summary>
            public float[] ExtL;
            public float[] ExtR;
            public Vector2[] Normal;
            public float[] Miter;

            public bool Drivable => Kind != RoadKind.Busway && Kind != RoadKind.Pedestrian;

            public RoadClass Class
            {
                get
                {
                    switch (Kind)
                    {
                        case RoadKind.Primary: return RoadClass.Primary;
                        case RoadKind.Tertiary: return RoadClass.Tertiary;
                        case RoadKind.Residential: return RoadClass.Residential;
                        case RoadKind.Busway: return RoadClass.Residential;
                        case RoadKind.Pedestrian: return RoadClass.Pedestrian;
                        default: return RoadClass.Service;
                    }
                }
            }

            /// <summary>Ponto deslocado lateralmente do vértice k (positivo = esquerda do sentido dos pontos).</summary>
            public Vector2 At(int k, float lateral) => Pts[k] + Normal[k] * (lateral * Miter[k]);

            public Vector2 Tangent(int k)
            {
                int a = Mathf.Max(0, k - 1);
                int b = Mathf.Min(Pts.Count - 1, k + 1);

                return (Pts[b] - Pts[a]).normalized;
            }

            /// <summary>Direção do tráfego no trecho k (para sentido único); sem sentido único devolve a direção dos pontos.</summary>
            public Vector2 FlowDirection(int k)
            {
                Vector2 d = (Pts[Mathf.Min(k + 1, Pts.Count - 1)] - Pts[Mathf.Min(k, Pts.Count - 2)]).normalized;

                return Reverse ? -d : d;
            }

            public float Length
            {
                get
                {
                    float total = 0f;

                    for (int i = 1; i < Pts.Count; i++)
                    {
                        total += Vector2.Distance(Pts[i - 1], Pts[i]);
                    }

                    return total;
                }
            }

            public float Width(int k) => ExtL[k] + ExtR[k];

            public float ExtToward(int k, Vector2 side) => Vector2.Dot(side, Geo.Left(Tangent(k))) > 0f ? ExtL[k] : ExtR[k];
        }

        private struct Junction
        {
            public Vector2 Pos;
            public float Radius;
            public int Degree;
        }

        private static readonly List<Road> Roads = new List<Road>();
        private static readonly List<Junction> Junctions = new List<Junction>();
        private static readonly Dictionary<long, List<(Road road, int index)>> NodeUsers = new Dictionary<long, List<(Road road, int index)>>();

        // ---------- Leitura das ruas ----------

        private static bool RealService(OsmWay way)
        {
            string service = way.Tag("service");
            string access = way.Tag("access");

            if (service == "parking_aisle" || service == "driveway" || service == "alley" || service == "building_passage" || service == "emergency_access")
            {
                return false;
            }

            return access != "private" && access != "no" && access != "customers";
        }

        private static void CollectRoads(OsmMap map)
        {
            Roads.Clear();

            foreach (OsmWay way in map.Ways)
            {
                string highway = way.Tag("highway");

                if (highway == null || way.Tag("tunnel") == "yes" || way.Is("area", "yes"))
                {
                    continue;
                }

                RoadKind kind;

                switch (highway)
                {
                    case "primary":
                    case "primary_link":
                    case "secondary":
                    case "secondary_link":
                    case "trunk":
                    case "trunk_link":
                        kind = RoadKind.Primary;
                        break;
                    case "tertiary":
                    case "tertiary_link":
                        kind = RoadKind.Tertiary;
                        break;
                    case "residential":
                    case "living_street":
                    case "unclassified":
                        kind = RoadKind.Residential;
                        break;
                    case "service":
                        if (!RealService(way))
                        {
                            continue;
                        }

                        kind = RoadKind.Service;
                        break;
                    case "busway":
                        kind = RoadKind.Busway;
                        break;
                    case "pedestrian":
                        kind = RoadKind.Pedestrian;
                        break;
                    default:
                        continue;
                }

                foreach (OsmWay piece in map.ClipLine(way))
                {
                    if (piece.Points.Count >= 2)
                    {
                        Roads.Add(MakeRoad(piece, kind, highway.EndsWith("_link")));
                    }
                }
            }
        }

        private static string[] LaneList(OsmWay way, params string[] keys)
        {
            foreach (string key in keys)
            {
                string value = way.Tag(key);

                if (!string.IsNullOrEmpty(value))
                {
                    return value.Split('|');
                }
            }

            return null;
        }

        /// <summary>Limite da rua em km/h: a tag do OSM, ou o costume de São Paulo (avenida 50, rua coletora 40, residencial 30).</summary>
        private static int SpeedLimitOf(Road r)
        {
            switch (r.Kind)
            {
                case RoadKind.Primary: return Entregador.Core.LimitesDeVelocidade.Resolver(r.MaxSpeed, Entregador.Core.LimitesDeVelocidade.Avenida);
                case RoadKind.Tertiary: return Entregador.Core.LimitesDeVelocidade.Resolver(r.MaxSpeed, Entregador.Core.LimitesDeVelocidade.Coletora);
                default: return Entregador.Core.LimitesDeVelocidade.Resolver(r.MaxSpeed, Entregador.Core.LimitesDeVelocidade.Local);
            }
        }

        private static Road MakeRoad(OsmWay way, RoadKind kind, bool link)
        {
            string oneway = way.Tag("oneway");
            bool isOneway = oneway == "yes" || oneway == "true" || oneway == "1" || oneway == "-1" || way.Is("junction", "roundabout") || kind == RoadKind.Busway;
            float laneWidth = kind == RoadKind.Primary ? 3.4f : kind == RoadKind.Tertiary ? 3.2f : 3.0f;
            int.TryParse(way.Tag("lanes"), out int lanes);
            float.TryParse(way.Tag("width"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float widthTag);
            float total;

            if (widthTag > 1f)
            {
                total = widthTag;
            }
            else if (lanes > 0 && kind != RoadKind.Busway && kind != RoadKind.Pedestrian)
            {
                total = lanes * laneWidth;
            }
            else
            {
                switch (kind)
                {
                    case RoadKind.Primary: total = link ? 5.5f : isOneway ? 7f : 14f; break;
                    case RoadKind.Tertiary: total = link ? 5.5f : isOneway ? 6.5f : 10f; break;
                    case RoadKind.Residential: total = isOneway ? 6f : 7.5f; break;
                    case RoadKind.Service: total = 5f; break;
                    case RoadKind.Busway: total = 3.5f; break;
                    default: total = 7f; break;
                }
            }

            // Faixas exclusivas de ônibus (psv:lanes / bus:lanes = designated) só em sentido único: da esquerda para a direita.
            int busLeft = 0;
            int busRight = 0;
            string[] lanesList = isOneway && kind != RoadKind.Busway ? LaneList(way, "psv:lanes", "bus:lanes") : null;

            if (lanesList != null)
            {
                int i = 0;

                while (i < lanesList.Length && lanesList[i] == "designated")
                {
                    busLeft++;
                    i++;
                }

                int j = lanesList.Length - 1;

                while (j >= i && lanesList[j] == "designated")
                {
                    busRight++;
                    j--;
                }
            }

            float busWidth = (busLeft + busRight) * laneWidth;
            float general = Mathf.Max(total - busWidth, 2.8f);

            var road = new Road
            {
                WayId = way.Id,
                Name = way.Tag("name"),
                Kind = kind,
                Pts = way.Points,
                Ids = way.NodeIds,
                Oneway = isOneway && kind != RoadKind.Pedestrian,
                Reverse = oneway == "-1",
                Roundabout = way.Is("junction", "roundabout"),
                MaxSpeed = Entregador.Core.LimitesDeVelocidade.LerTag(way.Tag("maxspeed")),
                LaneWidth = laneWidth,
                GenHalf = general * 0.5f,
                BusLeft = busLeft * laneWidth,
                BusRight = busRight * laneWidth,
            };

            int n = road.Pts.Count;
            road.ExtL = new float[n];
            road.ExtR = new float[n];
            road.Normal = new Vector2[n];
            road.Miter = new float[n];

            for (int k = 0; k < n; k++)
            {
                road.ExtL[k] = road.GenHalf + road.BusLeft;
                road.ExtR[k] = road.GenHalf + road.BusRight;
            }

            return road;
        }

        private static void ComputeNormals(Road r)
        {
            int n = r.Pts.Count;

            for (int k = 0; k < n; k++)
            {
                Vector2 before = k > 0 ? (r.Pts[k] - r.Pts[k - 1]).normalized : (r.Pts[1] - r.Pts[0]).normalized;
                Vector2 after = k < n - 1 ? (r.Pts[k + 1] - r.Pts[k]).normalized : before;
                Vector2 tangent = (before + after).normalized;

                if (tangent.sqrMagnitude < 0.01f)
                {
                    tangent = after;
                }

                r.Normal[k] = Geo.Left(tangent);
                float cos = Mathf.Max(0.55f, Vector2.Dot(r.Normal[k], Geo.Left(after)));
                r.Miter[k] = 1f / cos;
            }
        }

        /// <summary>
        /// Duas pistas de sentido único lado a lado (avenida de pista dupla mapeada como dois ways) não podem se sobrepor: onde as bordas
        /// se cruzam, cada lado divide a distância entre os eixos na proporção das suas extensões.
        /// </summary>
        private static void CapParallelRoads()
        {
            foreach (Road r in Roads)
            {
                if (r.Kind == RoadKind.Pedestrian)
                {
                    continue;
                }

                ComputeNormals(r);
            }

            foreach (Road r in Roads)
            {
                if (r.Kind == RoadKind.Pedestrian || r.Kind == RoadKind.Busway)
                {
                    continue;
                }

                for (int k = 0; k < r.Pts.Count; k++)
                {
                    Vector2 p = r.Pts[k];
                    Vector2 t = r.Tangent(k);

                    foreach (Road o in Roads)
                    {
                        if (o == r || o.Kind == RoadKind.Pedestrian || o.Kind == RoadKind.Busway)
                        {
                            continue;
                        }

                        for (int s = 0; s + 1 < o.Pts.Count; s++)
                        {
                            Vector2 a = o.Pts[s];
                            Vector2 b = o.Pts[s + 1];

                            if (Mathf.Abs(p.x - a.x) > 34f && Mathf.Abs(p.x - b.x) > 34f)
                            {
                                continue;
                            }

                            float d = Geo.DistanceToSegment(p, a, b, out Vector2 q, out float u);

                            if (d > 34f || d < 1.2f)
                            {
                                continue;
                            }

                            Vector2 ds = (b - a).normalized;

                            if (Mathf.Abs(Vector2.Dot(t, ds)) < 0.88f || o.Ids[s] == r.Ids[k] || o.Ids[s + 1] == r.Ids[k])
                            {
                                continue;
                            }

                            bool onLeft = Geo.Cross(t, q - p) > 0f;
                            bool meOnOtherLeft = Geo.Cross(ds, p - q) > 0f;
                            float mine = onLeft ? r.ExtL[k] : r.ExtR[k];
                            float theirs = meOnOtherLeft ? Mathf.Lerp(o.ExtL[s], o.ExtL[s + 1], u) : Mathf.Lerp(o.ExtR[s], o.ExtR[s + 1], u);

                            if (d < mine + theirs)
                            {
                                float cap = Mathf.Max(2f, d * mine / (mine + theirs));

                                if (onLeft)
                                {
                                    r.ExtL[k] = Mathf.Min(r.ExtL[k], cap);
                                }
                                else
                                {
                                    r.ExtR[k] = Mathf.Min(r.ExtR[k], cap);
                                }
                            }
                        }
                    }

                    // A faixa comum nunca fica menor que uma faixa.
                    r.ExtL[k] = Mathf.Max(r.ExtL[k], 2.2f);
                    r.ExtR[k] = Mathf.Max(r.ExtR[k], 2.2f);
                }
            }
        }

        // ---------- Nós e cruzamentos ----------

        private static void IndexNodes()
        {
            NodeUsers.Clear();
            Junctions.Clear();

            foreach (Road r in Roads)
            {
                for (int k = 0; k < r.Pts.Count; k++)
                {
                    if (!NodeUsers.TryGetValue(r.Ids[k], out List<(Road road, int index)> list))
                    {
                        list = new List<(Road road, int index)>();
                        NodeUsers[r.Ids[k]] = list;
                    }

                    list.Add((r, k));
                }
            }

            foreach (KeyValuePair<long, List<(Road road, int index)>> pair in NodeUsers)
            {
                int degree = 0;
                float radius = 0f;
                Vector2 pos = pair.Value[0].road.Pts[pair.Value[0].index];

                foreach ((Road road, int index) in pair.Value)
                {
                    degree += (index > 0 ? 1 : 0) + (index < road.Pts.Count - 1 ? 1 : 0);

                    if (road.Kind != RoadKind.Pedestrian)
                    {
                        radius = Mathf.Max(radius, Mathf.Max(road.ExtL[index], road.ExtR[index]));
                    }
                }

                if (degree >= 3)
                {
                    Junctions.Add(new Junction { Pos = pos, Radius = radius + 1.5f, Degree = degree });
                }
            }
        }

        private static bool NearJunction(Vector2 p, float extra)
        {
            foreach (Junction j in Junctions)
            {
                float r = j.Radius + extra;

                if (Mathf.Abs(p.x - j.Pos.x) < r && Mathf.Abs(p.y - j.Pos.y) < r && Vector2.Distance(p, j.Pos) < r)
                {
                    return true;
                }
            }

            return false;
        }

        // ---------- Pintura na grade ----------

        private static void PaintRoads(CityRaster raster, OsmMap map)
        {
            foreach (Road r in Roads)
            {
                byte cls = (byte)r.Class;

                for (int k = 0; k + 1 < r.Pts.Count; k++)
                {
                    var quad = new[] { r.At(k, r.ExtL[k]), r.At(k, -r.ExtR[k]), r.At(k + 1, -r.ExtR[k + 1]), r.At(k + 1, r.ExtL[k + 1]) };
                    raster.FillPolygon(quad, i =>
                    {
                        if (raster.Road[i] < cls)
                        {
                            raster.Road[i] = cls;
                        }
                    });
                }
            }

            // Fora da caixa do OSM há uma "rua" virtual de 8 m (a margem da grade), que fecha os quarteirões da borda.
            for (int iz = 0; iz < raster.H; iz++)
            {
                for (int ix = 0; ix < raster.W; ix++)
                {
                    Vector2 c = raster.Center(iz * raster.W + ix);

                    if (Mathf.Abs(c.x) > map.HalfX || Mathf.Abs(c.y) > map.HalfZ)
                    {
                        raster.Road[iz * raster.W + ix] = (byte)RoadClass.Service;
                    }
                }
            }
        }

        // ---------- Superfícies das ruas (sobre o chão de asfalto) ----------

        private const float RedHeight = 0.012f;
        private const float PaintHeight = 0.02f;
        private const float PedestrianHeight = 0.1f;

        private static readonly List<BusLane> BusLanes = new List<BusLane>();

        private static void BuildRoadSurfaces(OsmMap map)
        {
            BusLanes.Clear();
            // O asfalto: um chão só, do tamanho da caixa mais a margem. Os quarteirões (calçadas) ficam por cima.
            Material asphalt = CityStyle.Asphalt();
            float hx = map.HalfX + CityRaster.Margin;
            float hz = map.HalfZ + CityRaster.Margin;
            var floor = new MeshBatch();
            floor.FlatQuad(asphalt, new Vector3(-hx, 0f, -hz), new Vector3(hx, 0f, -hz), new Vector3(hx, 0f, hz), new Vector3(-hx, 0f, hz));
            GameObject ground = floor.Build(_roadsRoot, "Asfalto", false);
            GroundRenderers.Add(ground.GetComponent<Renderer>());

            Material red = CityStyle.AsphaltRed();
            Material paving = CityStyle.Paving();

            foreach (Road r in Roads)
            {
                for (int k = 0; k + 1 < r.Pts.Count; k++)
                {
                    MeshBatch b = GroundBatch(Geo.V3((r.Pts[k] + r.Pts[k + 1]) * 0.5f));

                    if (r.Kind == RoadKind.Busway)
                    {
                        Strip(b, red, r, k, -r.ExtR[k], r.ExtL[k], -r.ExtR[k + 1], r.ExtL[k + 1], RedHeight);
                        BusLanes.Add(new BusLane(Geo.V3(r.At(k, (r.ExtL[k] - r.ExtR[k]) * 0.5f)), Geo.V3(r.At(k + 1, (r.ExtL[k + 1] - r.ExtR[k + 1]) * 0.5f)), (r.ExtL[k] + r.ExtR[k]) * 0.5f));
                    }
                    else if (r.Kind == RoadKind.Pedestrian)
                    {
                        // Rua de pedestres: calçadão de piso hexagonal; 1 m a mais de cada lado fica escondido sob a calçada dos quarteirões.
                        Strip(b, paving, r, k, -r.ExtR[k] - 1f, r.ExtL[k] + 1f, -r.ExtR[k + 1] - 1f, r.ExtL[k + 1] + 1f, PedestrianHeight);
                    }
                    else
                    {
                        if (r.BusLeft > 0.5f)
                        {
                            Strip(b, red, r, k, r.GenHalf, r.ExtL[k], r.GenHalf, r.ExtL[k + 1], RedHeight);
                            BusLanes.Add(new BusLane(Geo.V3(r.At(k, (r.GenHalf + r.ExtL[k]) * 0.5f)), Geo.V3(r.At(k + 1, (r.GenHalf + r.ExtL[k + 1]) * 0.5f)), (r.ExtL[k] - r.GenHalf) * 0.5f));
                        }

                        if (r.BusRight > 0.5f)
                        {
                            Strip(b, red, r, k, -r.ExtR[k], -r.GenHalf, -r.ExtR[k + 1], -r.GenHalf, RedHeight);
                            BusLanes.Add(new BusLane(Geo.V3(r.At(k, -(r.GenHalf + r.ExtR[k]) * 0.5f)), Geo.V3(r.At(k + 1, -(r.GenHalf + r.ExtR[k + 1]) * 0.5f)), (r.ExtR[k] - r.GenHalf) * 0.5f));
                        }
                    }
                }
            }
        }

        /// <summary>Faixa do trecho k entre dois deslocamentos laterais (em cada ponta), no chão, em y.</summary>
        private static void Strip(MeshBatch b, Material m, Road r, int k, float from0, float to0, float from1, float to1, float y)
        {
            if (to0 - from0 < 0.05f && to1 - from1 < 0.05f)
            {
                return;
            }

            // Anti-horário visto de cima: pela direita no começo, direita no fim, esquerda no fim, esquerda no começo.
            b.FlatQuad(m, Geo.V3(r.At(k, from0), y), Geo.V3(r.At(k + 1, from1), y), Geo.V3(r.At(k + 1, to1), y), Geo.V3(r.At(k, to0), y));
        }

        // ---------- Faixas e travessias ----------

        private static void BuildMarkings(OsmMap map)
        {
            Material white = CityStyle.PaintWhite();
            Material yellow = CityStyle.PaintYellow();

            foreach (Road r in Roads)
            {
                if (!r.Drivable || r.Kind == RoadKind.Service || r.Length < 12f)
                {
                    continue;
                }

                float general = r.GenHalf * 2f;

                if (!r.Oneway)
                {
                    if (r.Kind == RoadKind.Residential)
                    {
                        MarkLine(r, yellow, 0f, 0.15f, 3.5f, 4f);
                    }
                    else
                    {
                        MarkLine(r, yellow, 0.14f, 0.1f, 0f, 0f);
                        MarkLine(r, yellow, -0.14f, 0.1f, 0f, 0f);

                        if (general >= 12f)
                        {
                            MarkLine(r, white, general * 0.25f, 0.12f, 3f, 5f);
                            MarkLine(r, white, -general * 0.25f, 0.12f, 3f, 5f);
                        }
                    }
                }
                else
                {
                    int lanes = Mathf.RoundToInt(general / r.LaneWidth);

                    for (int i = 1; i < lanes; i++)
                    {
                        MarkLine(r, white, -r.GenHalf + general * i / lanes, 0.12f, 3f, 5f);
                    }

                    if (r.BusLeft > 0.5f)
                    {
                        MarkLine(r, white, r.GenHalf, 0.18f, 0f, 0f);
                    }

                    if (r.BusRight > 0.5f)
                    {
                        MarkLine(r, white, -r.GenHalf, 0.18f, 0f, 0f);
                    }
                }
            }

            BuildCrosswalks(map);
        }

        /// <summary>Linha ao longo da rua a um deslocamento lateral, tracejada (dash/gap) ou contínua, cortada perto dos cruzamentos.</summary>
        private static void MarkLine(Road r, Material material, float lateral, float width, float dash, float gap)
        {
            float period = dash + gap;
            float travelled = 0f;

            for (int k = 0; k + 1 < r.Pts.Count; k++)
            {
                Vector2 a = r.At(k, lateral);
                Vector2 b = r.At(k + 1, lateral);
                float length = Vector2.Distance(a, b);

                if (length < 0.01f)
                {
                    continue;
                }

                Vector2 dir = (b - a) / length;
                float t = 0f;

                while (t < length - 0.001f)
                {
                    bool inDash = gap <= 0f || Mathf.Repeat(travelled + t, period) < dash;
                    float remain = gap <= 0f ? length - t : (inDash ? dash - Mathf.Repeat(travelled + t, period) : period - Mathf.Repeat(travelled + t, period));
                    float step = Mathf.Clamp(remain, 0.01f, length - t);

                    if (inDash && step > 0.2f)
                    {
                        Vector2 from = a + dir * t;
                        Vector2 to = a + dir * (t + step);

                        if (!NearJunction((from + to) * 0.5f, 0.5f) && !NearJunction(from, 0f) && !NearJunction(to, 0f))
                        {
                            Quad2D(GroundBatch(Geo.V3(from)), material, from, to, width, PaintHeight);
                        }
                    }

                    t += step;
                }

                travelled += length;
            }
        }

        private static void Quad2D(MeshBatch b, Material m, Vector2 from, Vector2 to, float width, float y)
        {
            Vector2 dir = (to - from).normalized;
            Vector2 right = Geo.Right(dir) * (width * 0.5f);
            Vector2 left = -right;
            b.FlatQuad(m, Geo.V3(from + right, y), Geo.V3(to + right, y), Geo.V3(to + left, y), Geo.V3(from + left, y));
        }

        /// <summary>Faixa de pedestre: barras paralelas ao tráfego, de borda a borda da rua, em torno de um ponto do eixo.</summary>
        private static void Zebra(Road r, int k, Vector2 center, Vector2 tangent)
        {
            Material white = CityStyle.PaintWhite();
            Vector2 left = Geo.Left(tangent);
            float extL = Mathf.Max(2f, r.ExtL[k]);
            float extR = Mathf.Max(2f, r.ExtR[k]);
            MeshBatch b = GroundBatch(Geo.V3(center));

            for (float lateral = -extR + 0.7f; lateral < extL - 0.4f; lateral += 1.1f)
            {
                Vector2 mid = center + left * lateral;
                Quad2D(b, white, mid - tangent * 1.5f, mid + tangent * 1.5f, 0.55f, PaintHeight);
            }
        }

        private static void BuildCrosswalks(OsmMap map)
        {
            var placed = new List<Vector2>();

            foreach (OsmNode node in map.Nodes)
            {
                if (node.Tag("highway") != "crossing" || !map.InsideBox(node.Pos))
                {
                    continue;
                }

                if (!NearestRoadSegment(node.Pos, 3.5f, out Road r, out int k, out Vector2 q) || r.Kind == RoadKind.Service)
                {
                    continue;
                }

                if (placed.Exists(p => Vector2.Distance(p, q) < 6f))
                {
                    continue;
                }

                placed.Add(q);
                Zebra(r, k, q, (r.Pts[k + 1] - r.Pts[k]).normalized);
            }

            // Cruzamentos com semáforo ganham faixa em cada braço (onde não há uma travessia do OSM logo ali).
            foreach (OsmNode node in map.Nodes)
            {
                if (node.Tag("highway") != "traffic_signals" || !map.InsideBox(node.Pos))
                {
                    continue;
                }

                foreach (SignalArm arm in SignalArms(node.Pos))
                {
                    Vector2 c = arm.Node + arm.Dir * (arm.StopDistance + 2.2f);

                    if (placed.Exists(p => Vector2.Distance(p, c) < 6f) || arm.Length < arm.StopDistance + 6f)
                    {
                        continue;
                    }

                    placed.Add(c);
                    Zebra(arm.Road, arm.Index, c, arm.Dir);
                }
            }
        }

        /// <summary>Trecho de rua dirigível mais próximo (até maxDistance), com o ponto projetado no eixo.</summary>
        private static bool NearestRoadSegment(Vector2 p, float maxDistance, out Road road, out int index, out Vector2 closest)
        {
            road = null;
            index = 0;
            closest = p;
            float best = maxDistance;

            foreach (Road r in Roads)
            {
                if (!r.Drivable)
                {
                    continue;
                }

                for (int k = 0; k + 1 < r.Pts.Count; k++)
                {
                    float d = Geo.DistanceToSegment(p, r.Pts[k], r.Pts[k + 1], out Vector2 c, out _);

                    if (d < best)
                    {
                        best = d;
                        road = r;
                        index = k;
                        closest = c;
                    }
                }
            }

            return road != null;
        }

        // ---------- Semáforos e placas de PARE ----------

        /// <summary>Um braço de um cruzamento: a direção que sai do nó, a rua e o trecho.</summary>
        private struct SignalArm
        {
            public Vector2 Node;
            public Vector2 Dir;
            public Road Road;
            public int Index;
            public float Length;
            public float StopDistance;
            public bool Approach;
        }

        /// <summary>Os braços de rua que chegam ao ponto (nó do grafo ou meio de quarteirão), com o tráfego chegando ou não.</summary>
        private static List<SignalArm> SignalArms(Vector2 p)
        {
            var arms = new List<SignalArm>();

            if (!NearestRoadSegment(p, 14f, out Road first, out int firstIndex, out Vector2 snap))
            {
                return arms;
            }

            // Nó compartilhado perto do ponto: todos os braços dele. Senão, os dois sentidos da rua ali.
            long nodeId = 0;
            bool hasNode = false;

            foreach (KeyValuePair<long, List<(Road road, int index)>> pair in NodeUsers)
            {
                Vector2 pos = pair.Value[0].road.Pts[pair.Value[0].index];

                if (Vector2.Distance(pos, snap) < 9f && pair.Value.Count > 0)
                {
                    int degree = 0;

                    foreach ((Road road, int index) in pair.Value)
                    {
                        degree += (index > 0 ? 1 : 0) + (index < road.Pts.Count - 1 ? 1 : 0);
                    }

                    if (degree >= 3)
                    {
                        nodeId = pair.Key;
                        hasNode = true;

                        break;
                    }
                }
            }

            if (hasNode)
            {
                foreach ((Road road, int index) in NodeUsers[nodeId])
                {
                    if (!road.Drivable)
                    {
                        continue;
                    }

                    Vector2 node = road.Pts[index];

                    if (index > 0)
                    {
                        AddArm(arms, road, index - 1, node, road.Pts[index - 1]);
                    }

                    if (index < road.Pts.Count - 1)
                    {
                        AddArm(arms, road, index, node, road.Pts[index + 1]);
                    }
                }
            }
            else
            {
                Vector2 a = first.Pts[firstIndex];
                Vector2 b = first.Pts[firstIndex + 1];
                AddArm(arms, first, firstIndex, snap, b);
                AddArm(arms, first, firstIndex, snap, a);
            }

            return arms;
        }

        private static void AddArm(List<SignalArm> arms, Road road, int segment, Vector2 node, Vector2 toward)
        {
            Vector2 offset = toward - node;
            float length = offset.magnitude;

            if (length < 2f)
            {
                return;
            }

            Vector2 dir = offset / length;
            Vector2 flow = road.FlowDirection(segment);
            bool approach = !road.Oneway || Vector2.Dot(flow, dir) < 0f;
            float ext = Mathf.Max(road.ExtL[segment], road.ExtR[segment]);
            arms.Add(new SignalArm { Node = node, Dir = dir, Road = road, Index = segment, Length = length, StopDistance = 6f + ext, Approach = approach });
        }

        private static void BuildSignals(OsmMap map)
        {
            var signalPositions = new List<Vector2>();

            // Semáforos de verdade (ciclo, lentes acesas, linha de retenção, fotossensor): ver CityBuilder.Semaforos.
            signalPositions.AddRange(BuildSemaforos(map));

            // Placas de PARE: rua pequena que encontra uma maior, no braço que chega ao cruzamento.
            int stops = 0;

            foreach (Junction j in Junctions)
            {
                if (stops >= 22 || signalPositions.Exists(p => Vector2.Distance(p, j.Pos) < 30f))
                {
                    continue;
                }

                foreach (SignalArm arm in SignalArms(j.Pos))
                {
                    if (arm.Road.Kind != RoadKind.Residential || !arm.Approach || arm.Length < arm.StopDistance + 12f)
                    {
                        continue;
                    }

                    bool bigger = false;

                    foreach (SignalArm other in SignalArms(j.Pos))
                    {
                        bigger |= other.Road.Class > RoadClass.Residential;
                    }

                    if (!bigger)
                    {
                        continue;
                    }

                    Vector2 rightSide = Geo.Right(-arm.Dir);
                    float ext = arm.Road.ExtToward(arm.Index, rightSide);
                    Vector2 corner = PushToSidewalk(arm.Node + arm.Dir * (arm.StopDistance - 1f) + rightSide * (ext + 0.5f), rightSide);

                    if (signalPositions.Exists(p => Vector2.Distance(p, corner) < 12f))
                    {
                        continue;
                    }

                    StopSign(BuildBatch(Geo.V3(corner)), Geo.V3(corner, SidewalkHeightAt(corner)), Geo.V3(arm.Dir));
                    SolidCapsule(_signalRoot, "PlacaPare", Geo.V3(corner, SidewalkHeightAt(corner)), 0.15f, 3f);
                    signalPositions.Add(corner);
                    stops++;
                }
            }
        }

        /// <summary>Altura do chão no ponto: meio-fio em cima da calçada, zero na rua.</summary>
        private static float SidewalkHeightAt(Vector2 p) => _raster.BlockAt(p) != 0 ? CurbHeight : 0f;

        /// <summary>Empurra o ponto para fora da rua (na direção side) até cair em cima de uma calçada, por até 3 m.</summary>
        private static Vector2 PushToSidewalk(Vector2 p, Vector2 side)
        {
            for (int i = 0; i < 6 && _raster.BlockAt(p) == 0; i++)
            {
                p += side * 0.5f;
            }

            return p;
        }

        // ---------- Grafo de rotas ----------

        private struct GraphEdge
        {
            public Road Road;
            public int Segment;
            public int From;
            public int To;
        }

        private static readonly List<GraphEdge> GraphEdges = new List<GraphEdge>();
        private static RoadGraph _graph;

        /// <summary>
        /// Grafo das ruas dirigíveis (sem corredores de ônibus nem ruas de pedestres, ilegais para moto). Cada vértice das ruas é um nó;
        /// o sentido único vira aresta com direção (andar contra a mão custa mais no GPS). Só a maior componente conexa fica.
        /// </summary>
        private static RoadGraph BuildGraph(GameObject world)
        {
            var index = new Dictionary<long, int>();
            var positions = new List<Vector3>();
            var from = new List<int>();
            var to = new List<int>();
            var oneway = new List<bool>();
            var roadOf = new List<(Road road, int segment)>();

            int Node(Road r, int k)
            {
                if (!index.TryGetValue(r.Ids[k], out int id))
                {
                    id = positions.Count;
                    index[r.Ids[k]] = id;
                    positions.Add(Geo.V3(r.Pts[k]));
                }

                return id;
            }

            foreach (Road r in Roads)
            {
                if (!r.Drivable)
                {
                    continue;
                }

                for (int k = 0; k + 1 < r.Pts.Count; k++)
                {
                    int a = Node(r, k);
                    int b = Node(r, k + 1);

                    if (a == b)
                    {
                        continue;
                    }

                    from.Add(r.Reverse ? b : a);
                    to.Add(r.Reverse ? a : b);
                    oneway.Add(r.Oneway);
                    roadOf.Add((r, k));
                }
            }

            bool[] keep = LargestComponent(positions.Count, from, to);
            var remap = new int[positions.Count];
            var nodes = new List<Vector3>();

            for (int i = 0; i < positions.Count; i++)
            {
                remap[i] = keep[i] ? nodes.Count : -1;

                if (keep[i])
                {
                    nodes.Add(positions[i]);
                }
            }

            var edgeFrom = new List<int>();
            var edgeTo = new List<int>();
            var edgeOneway = new List<bool>();
            var edgeLimit = new List<int>();
            GraphEdges.Clear();

            for (int e = 0; e < from.Count; e++)
            {
                if (remap[from[e]] < 0 || remap[to[e]] < 0)
                {
                    continue;
                }

                edgeFrom.Add(remap[from[e]]);
                edgeTo.Add(remap[to[e]]);
                edgeOneway.Add(oneway[e]);
                edgeLimit.Add(SpeedLimitOf(roadOf[e].road));
                GraphEdges.Add(new GraphEdge { Road = roadOf[e].road, Segment = roadOf[e].segment, From = remap[from[e]], To = remap[to[e]] });
            }

            var graph = world.AddComponent<RoadGraph>();
            graph.Configure(nodes.ToArray(), edgeFrom.ToArray(), edgeTo.ToArray(), edgeOneway.ToArray(), edgeLimit.ToArray());
            Debug.Log($"[CityBuilder] Grafo: {nodes.Count} nós, {edgeFrom.Count} trechos ({positions.Count - nodes.Count} nós descartados fora da componente principal).");

            return graph;
        }

        /// <summary>Marca os nós da maior componente conexa do grafo (ignorando o sentido único: a mão errada só custa mais, não é proibida).</summary>
        private static bool[] LargestComponent(int count, List<int> from, List<int> to)
        {
            var parent = new int[count];

            for (int i = 0; i < count; i++)
            {
                parent[i] = i;
            }

            int Find(int x)
            {
                while (parent[x] != x)
                {
                    parent[x] = parent[parent[x]];
                    x = parent[x];
                }

                return x;
            }

            for (int e = 0; e < from.Count; e++)
            {
                parent[Find(from[e])] = Find(to[e]);
            }

            var size = new Dictionary<int, int>();
            int best = -1;
            int bestSize = 0;

            for (int i = 0; i < count; i++)
            {
                int root = Find(i);
                size.TryGetValue(root, out int n);
                size[root] = ++n;

                if (n > bestSize)
                {
                    bestSize = n;
                    best = root;
                }
            }

            var keep = new bool[count];

            for (int i = 0; i < count; i++)
            {
                keep[i] = Find(i) == best;
            }

            return keep;
        }
    }
}

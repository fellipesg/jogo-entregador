using Entregador.Game;
using System.Collections.Generic;
using UnityEngine;

namespace Entregador.Editor
{
    /// <summary>
    /// Quarteirões: as regiões entre as ruas na grade (abertura morfológica de 3,5 m arredonda as esquinas), polígonos simplificados,
    /// calçada com guia, zonas (largo, parques, estacionamentos) e colliders. Os lotes e os prédios estão em CityBuilder.Lots.
    /// </summary>
    public static partial class CityBuilder
    {
        /// <summary>Altura do meio-fio das calçadas, em metros. A moto sobe por uma rampa de colisão.</summary>
        private const float CurbHeight = 0.15f;

        /// <summary>Quanto a rampa avança sobre a rua (m). 0,15 m em 0,8 m dá cerca de 10,6 graus.</summary>
        private const float CurbRun = 0.8f;

        /// <summary>Largura da calçada: a frente dos prédios fica a esta distância da guia.</summary>
        private const float FrontInset = 3f;

        /// <summary>Fundo máximo de um lote da fileira (m).</summary>
        private const float RowDepth = 15f;

        /// <summary>Raio (m) da abertura que arredonda as esquinas dos quarteirões.</summary>
        private const float OpenRadius = 3.5f;

        /// <summary>Camada dos prédios e do mobiliário alto: o minimapa não desenha esta camada (só as ruas e os quarteirões).</summary>
        public const int BuildingLayer = 8;

        // O Largo Treze de Maio: o piso hexagonal ocupa os quarteirões num disco em torno deste ponto (metros locais).
        private static readonly Vector2 LargoCenter = new Vector2(10f, -75f);
        private const float LargoRadius = 45f;

        private const byte ZoneLargo = 1;
        private const byte ZonePark = 2;
        private const byte ZonePatio = 3;
        private const byte ZoneCemetery = 4;

        /// <summary>Renderers com materiais de chão que trocam para a versão molhada na chuva (ver WeatherLook).</summary>
        private static readonly List<Renderer> GroundRenderers = new List<Renderer>();

        private sealed class Block
        {
            public int Label;
            public List<Vector2> Poly;
            public RoadClass[] EdgeClass;
            public float Area;
            public Vector2 Centroid;
        }

        private static CityRaster _raster;
        private static readonly List<Block> Blocks = new List<Block>();
        private static readonly Dictionary<int, List<Block>> BlocksByLabel = new Dictionary<int, List<Block>>();

        // ---------- Malhas por pedaço do mapa ----------

        private const float ChunkSize = 100f;
        private static readonly Dictionary<int, MeshBatch> GroundBatches = new Dictionary<int, MeshBatch>();
        private static readonly Dictionary<int, MeshBatch> BuildBatches = new Dictionary<int, MeshBatch>();

        private static int ChunkKey(Vector3 p) => Mathf.FloorToInt((p.x + 700f) / ChunkSize) * 100 + Mathf.FloorToInt((p.z + 700f) / ChunkSize);

        private static MeshBatch Chunk(Dictionary<int, MeshBatch> batches, Vector3 p)
        {
            int key = ChunkKey(p);

            if (!batches.TryGetValue(key, out MeshBatch batch))
            {
                batch = new MeshBatch();
                batches[key] = batch;
            }

            return batch;
        }

        /// <summary>Malha do pedaço para coisas rente ao chão (calçada, faixas, pisos): aparece no minimapa.</summary>
        private static MeshBatch GroundBatch(Vector3 p) => Chunk(GroundBatches, p);

        /// <summary>Malha do pedaço para prédios e mobiliário alto (camada dos prédios).</summary>
        private static MeshBatch BuildBatch(Vector3 p) => Chunk(BuildBatches, p);

        private static void FinishBatches(Transform blocks)
        {
            int tris = 0;

            foreach (KeyValuePair<int, MeshBatch> pair in GroundBatches)
            {
                tris += pair.Value.TriangleCount;
                GameObject go = pair.Value.Build(blocks, $"Chao_{pair.Key / 100}_{pair.Key % 100}", false);

                if (go != null)
                {
                    GroundRenderers.Add(go.GetComponent<Renderer>());
                }
            }

            int buildingTris = 0;

            foreach (KeyValuePair<int, MeshBatch> pair in BuildBatches)
            {
                buildingTris += pair.Value.TriangleCount;
                GameObject go = pair.Value.Build(blocks, $"Predios_{pair.Key / 100}_{pair.Key % 100}");

                if (go != null)
                {
                    go.layer = BuildingLayer;
                }
            }

            Debug.Log($"[CityBuilder] Triângulos: chão {tris}, prédios e mobiliário {buildingTris}; pedaços {GroundBatches.Count}+{BuildBatches.Count}.");
        }

        // ---------- Quarteirões a partir da grade ----------

        private static void ComputeBlocks()
        {
            CityRaster r = _raster;
            int n = r.W * r.H;
            Blocks.Clear();
            BlocksByLabel.Clear();

            // Abertura: erode o que não é rua em 3,5 m e dilata de volta. As esquinas ficam arredondadas e as faixas finas viram rua.
            float[] toRoad = r.DistanceTo(i => r.Road[i] != 0);
            float[] toCore = r.DistanceTo(i => r.Road[i] == 0 && toRoad[i] >= OpenRadius);
            var mask = new bool[n];

            for (int i = 0; i < n; i++)
            {
                mask[i] = r.Road[i] == 0 && toCore[i] <= OpenRadius;
            }

            // Rotula as regiões de 4 vizinhos; as minúsculas (menos de 60 m²) viram rua.
            ushort label = 0;
            var stack = new Stack<int>();
            var cells = new List<int>();

            for (int start = 0; start < n; start++)
            {
                if (!mask[start] || r.Block[start] != 0)
                {
                    continue;
                }

                label++;
                cells.Clear();
                stack.Push(start);
                r.Block[start] = label;

                while (stack.Count > 0)
                {
                    int c = stack.Pop();
                    cells.Add(c);
                    int cx = c % r.W;
                    int cz = c / r.W;

                    for (int d = 0; d < 4; d++)
                    {
                        int nx = cx + (d == 0 ? 1 : d == 1 ? -1 : 0);
                        int nz = cz + (d == 2 ? 1 : d == 3 ? -1 : 0);

                        if (r.In(nx, nz) && mask[nz * r.W + nx] && r.Block[nz * r.W + nx] == 0)
                        {
                            r.Block[nz * r.W + nx] = label;
                            stack.Push(nz * r.W + nx);
                        }
                    }
                }

                if (cells.Count < 240)
                {
                    foreach (int c in cells)
                    {
                        r.Block[c] = 0;
                        mask[c] = false;
                    }
                }
            }

            r.Dist = r.DistanceTo(i => r.Block[i] == 0);

            int holes = 0;
            Dictionary<int, List<List<Vector2>>> loops = r.Trace(r.Block);

            foreach (KeyValuePair<int, List<List<Vector2>>> pair in loops)
            {
                foreach (List<Vector2> loop in pair.Value)
                {
                    float area = Geo.Area(loop);

                    if (area < 0f)
                    {
                        holes++;

                        continue;
                    }

                    if (area < 30f)
                    {
                        continue;
                    }

                    var block = new Block { Label = pair.Key, Poly = Geo.SimplifyClosed(loop, 0.85f), Area = area };
                    block.Centroid = Geo.Centroid(block.Poly);
                    Blocks.Add(block);

                    if (!BlocksByLabel.TryGetValue(block.Label, out List<Block> list))
                    {
                        list = new List<Block>();
                        BlocksByLabel[block.Label] = list;
                    }

                    list.Add(block);
                }
            }

            foreach (Block block in Blocks)
            {
                block.EdgeClass = new RoadClass[block.Poly.Count];

                for (int i = 0; i < block.Poly.Count; i++)
                {
                    block.EdgeClass[i] = FacingClass(block.Poly[i], block.Poly[(i + 1) % block.Poly.Count]);
                }
            }

            Debug.Log($"[CityBuilder] Quarteirões: {Blocks.Count} (rótulos {label}, buracos ignorados {holes}).");
        }

        /// <summary>Classe da rua que a aresta do quarteirão encara (a maior encontrada logo à frente).</summary>
        private static RoadClass FacingClass(Vector2 a, Vector2 b)
        {
            Vector2 dir = (b - a).normalized;
            Vector2 outward = Geo.Right(dir);
            Vector2 mid = (a + b) * 0.5f;
            RoadClass best = RoadClass.None;

            for (float d = 0.8f; d <= 14f; d += 1.6f)
            {
                int i = _raster.At(mid + outward * d);

                if (i >= 0 && _raster.Road[i] > (byte)best)
                {
                    best = (RoadClass)_raster.Road[i];
                }

                if (best != RoadClass.None && d > 3f)
                {
                    break;
                }
            }

            return best;
        }

        // ---------- Zonas ----------

        private static void PaintZones(OsmMap map)
        {
            CityRaster r = _raster;

            r.FillPolygon(Circle(LargoCenter, LargoRadius, 48), i =>
            {
                if (r.Block[i] != 0)
                {
                    r.Zone[i] = ZoneLargo;
                }
            });

            foreach (OsmWay way in map.Ways)
            {
                byte zone = 0;
                string leisure = way.Tag("leisure");
                string landuse = way.Tag("landuse");

                if (leisure == "park" || leisure == "garden" || leisure == "playground" || landuse == "grass" || landuse == "meadow" || landuse == "recreation_ground" || landuse == "village_green")
                {
                    zone = ZonePark;
                }
                else if (landuse == "cemetery")
                {
                    zone = ZoneCemetery;
                }
                else if (way.Tag("amenity") == "parking" && way.Tag("building") == null)
                {
                    zone = ZonePatio;
                }

                if (zone == 0 || !way.Closed)
                {
                    continue;
                }

                List<Vector2> poly = Geo.ClipPolygon(way.Points, map.HalfX, map.HalfZ);

                if (poly.Count < 3)
                {
                    continue;
                }

                if (Geo.Area(poly) < 0f)
                {
                    poly.Reverse();
                }

                if (Geo.Area(poly) < 150f)
                {
                    continue;
                }

                byte z = zone;
                r.FillPolygon(poly, i =>
                {
                    if (r.Block[i] != 0 && r.Zone[i] == 0)
                    {
                        r.Zone[i] = z;
                    }
                });
            }
        }

        private static List<Vector2> Circle(Vector2 center, float radius, int sides)
        {
            var points = new List<Vector2>(sides);

            for (int i = 0; i < sides; i++)
            {
                float a = i * Mathf.PI * 2f / sides;
                points.Add(center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius);
            }

            return points;
        }

        // ---------- Calçadas e pisos ----------

        private static void BuildSlabs()
        {
            Material sidewalk = CityStyle.Sidewalk();
            Material concrete = CityStyle.Curb();
            int failures = 0;

            foreach (Block block in Blocks)
            {
                MeshBatch b = GroundBatch(Geo.V3(block.Centroid));
                var tris = new List<int>();

                if (!Geo.Triangulate(block.Poly, tris))
                {
                    failures++;
                }

                b.Polygon(sidewalk, block.Poly, tris, CurbHeight);
                int n = block.Poly.Count;

                for (int i = 0; i < n; i++)
                {
                    Vector2 a = block.Poly[i];
                    Vector2 c = block.Poly[(i + 1) % n];
                    float length = Vector2.Distance(a, c);

                    if (length < 0.01f)
                    {
                        continue;
                    }

                    Vector2 dir = (c - a) / length;
                    Vector2 inward = Geo.Left(dir) * 0.15f;

                    // Topo da guia (uma faixa de 15 cm) e a face vertical voltada para a rua.
                    b.FlatQuad(concrete, Geo.V3(a, CurbHeight + 0.012f), Geo.V3(c, CurbHeight + 0.012f), Geo.V3(c + inward, CurbHeight + 0.012f), Geo.V3(a + inward, CurbHeight + 0.012f));
                    b.Rect(concrete, Geo.V3(a), Geo.V3(dir), Vector3.up, length, CurbHeight);
                }
            }

            Debug.Log($"[CityBuilder] Calçadas: {Blocks.Count} polígonos, {failures} triangulações imperfeitas.");
        }

        /// <summary>Pisos por cima da calçada: o largo (piso hexagonal), parques e cemitério (grama), estacionamentos (piso de concreto).</summary>
        private static void BuildZoneSurfaces()
        {
            CityRaster r = _raster;
            var labels = new ushort[r.W * r.H];

            for (byte zone = ZoneLargo; zone <= ZoneCemetery; zone++)
            {
                for (int i = 0; i < labels.Length; i++)
                {
                    labels[i] = (ushort)(r.Zone[i] == zone ? 1 : 0);
                }

                Material material = zone == ZoneLargo ? CityStyle.Paving() : zone == ZonePatio ? CityStyle.Floor() : CityStyle.Lawn();
                float y = CurbHeight + (zone == ZoneLargo ? 0.008f : 0.01f);

                if (!r.Trace(labels).TryGetValue(1, out List<List<Vector2>> loops))
                {
                    continue;
                }

                foreach (List<Vector2> loop in loops)
                {
                    if (Geo.Area(loop) < 8f)
                    {
                        continue;
                    }

                    List<Vector2> poly = Geo.SimplifyClosed(loop, 0.45f);
                    var tris = new List<int>();
                    Geo.Triangulate(poly, tris);
                    GroundBatch(Geo.V3(Geo.Centroid(poly))).Polygon(material, poly, tris, y);
                }
            }
        }

        // ---------- Colliders ----------

        private static void AddBoth(List<Vector3> verts, List<int> tris, Vector3 a, Vector3 b, Vector3 c)
        {
            int s = verts.Count;
            verts.Add(a);
            verts.Add(b);
            verts.Add(c);
            tris.AddRange(new[] { s, s + 1, s + 2, s, s + 2, s + 1 });
        }

        /// <summary>
        /// Colliders: por quarteirão, a laje da calçada (plana, com uma rampa de 0,8 m sobre a rua para a moto subir a guia) e, por região,
        /// as paredes dos prédios (extrusão do contorno da ocupação dos lotes, tal como foram colocados).
        /// </summary>
        private static void BuildColliders(Transform parent)
        {
            foreach (Block block in Blocks)
            {
                var verts = new List<Vector3>();
                var tris = new List<int>();
                var index = new List<int>();
                Geo.Triangulate(block.Poly, index);

                for (int i = 0; i + 2 < index.Count; i += 3)
                {
                    AddBoth(verts, tris, Geo.V3(block.Poly[index[i]], CurbHeight), Geo.V3(block.Poly[index[i + 1]], CurbHeight), Geo.V3(block.Poly[index[i + 2]], CurbHeight));
                }

                int n = block.Poly.Count;

                for (int i = 0; i < n; i++)
                {
                    Vector2 a = block.Poly[i];
                    Vector2 c = block.Poly[(i + 1) % n];
                    Vector2 outward = Geo.Right((c - a).normalized) * CurbRun;
                    Vector3 a0 = Geo.V3(a, CurbHeight);
                    Vector3 c0 = Geo.V3(c, CurbHeight);
                    Vector3 a1 = Geo.V3(a + outward, 0f);
                    Vector3 c1 = Geo.V3(c + outward, 0f);
                    AddBoth(verts, tris, a0, c0, c1);
                    AddBoth(verts, tris, a0, c1, a1);
                }

                AddMeshCollider(parent, $"Calcada_{block.Label}", verts, tris);
            }

            // Paredes dos prédios: contornos da ocupação (qualquer lote, fundo ou prédio do OSM) dentro dos quarteirões.
            CityRaster r = _raster;
            var solid = new ushort[r.W * r.H];

            for (int i = 0; i < solid.Length; i++)
            {
                if (r.Occ[i] != 0 && r.Block[i] != 0)
                {
                    solid[i] = r.Block[i];
                }
            }

            foreach (KeyValuePair<int, List<List<Vector2>>> pair in r.Trace(solid))
            {
                var verts = new List<Vector3>();
                var tris = new List<int>();

                foreach (List<Vector2> loop in pair.Value)
                {
                    List<Vector2> poly = Geo.SimplifyClosed(loop, 0.3f);

                    for (int i = 0; i < poly.Count; i++)
                    {
                        Vector2 a = poly[i];
                        Vector2 c = poly[(i + 1) % poly.Count];
                        Vector3 a0 = Geo.V3(a, 0f);
                        Vector3 c0 = Geo.V3(c, 0f);
                        Vector3 a1 = Geo.V3(a, 9f);
                        Vector3 c1 = Geo.V3(c, 9f);
                        AddBoth(verts, tris, a0, c0, c1);
                        AddBoth(verts, tris, a0, c1, a1);
                    }
                }

                AddMeshCollider(parent, $"Paredes_{pair.Key}", verts, tris);
            }
        }

        private static void AddMeshCollider(Transform parent, string name, List<Vector3> verts, List<int> tris)
        {
            if (tris.Count == 0)
            {
                return;
            }

            var mesh = new Mesh { name = MeshStore.UniqueName(name) };

            if (verts.Count > 65000)
            {
                mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            }

            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var collider = go.AddComponent<MeshCollider>();
            MeshStore.Add(mesh, m => collider.sharedMesh = m);
        }
    }
}

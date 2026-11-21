using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Entregador.Editor
{
    /// <summary>Um caminho (way) do OpenStreetMap já em metros locais (x = leste, z = norte, guardados em Vector2.x/.y).</summary>
    public sealed class OsmWay
    {
        public long Id;
        public Dictionary<string, string> Tags = new Dictionary<string, string>();
        public List<Vector2> Points = new List<Vector2>();
        public List<long> NodeIds = new List<long>();

        public string Tag(string key) => Tags.TryGetValue(key, out string value) ? value : null;

        public bool Is(string key, string value) => Tag(key) == value;

        public bool Closed => Points.Count > 3 && Vector2.Distance(Points[0], Points[Points.Count - 1]) < 0.01f;
    }

    /// <summary>Um ponto do OpenStreetMap com tags (semáforo, faixa de pedestre, ponto de ônibus, árvore).</summary>
    public sealed class OsmNode
    {
        public long Id;
        public Vector2 Pos;
        public Dictionary<string, string> Tags = new Dictionary<string, string>();

        public string Tag(string key) => Tags.TryGetValue(key, out string value) ? value : null;
    }

    /// <summary>
    /// Extrato do OpenStreetMap (JSON da Overpass API com "out geom") projetado em metros: projeção equiretangular em torno do
    /// centro da caixa (BBox*), x = leste, z = norte, 1 unidade = 1 m, sem altitude. Dados © OpenStreetMap contributors (ODbL).
    /// Para trocar de bairro: troque o JSON e a caixa abaixo (ver README).
    /// </summary>
    public sealed class OsmMap
    {
        public const string DataPath = "Assets/_Jogo/Data/OSM/santo_amaro.json";

        // Caixa da consulta (graus): sul, norte, oeste, leste.
        public const double BoxSouth = -23.6578;
        public const double BoxNorth = -23.6488;
        public const double BoxWest = -46.7112;
        public const double BoxEast = -46.7014;

        private const double EarthRadius = 6371000.0;

        public readonly List<OsmWay> Ways = new List<OsmWay>();
        public readonly List<OsmNode> Nodes = new List<OsmNode>();

        /// <summary>Meia-largura (leste-oeste) e meia-altura (norte-sul) da caixa, em metros.</summary>
        public float HalfX;
        public float HalfZ;

        private double _lat0;
        private double _lon0;
        private long _generated = -1;

        public static OsmMap Load(string path)
        {
            var map = new OsmMap();
            map._lat0 = (BoxSouth + BoxNorth) * 0.5;
            map._lon0 = (BoxWest + BoxEast) * 0.5;
            Vector2 corner = map.Project(BoxNorth, BoxEast);
            map.HalfX = corner.x;
            map.HalfZ = corner.y;

            var root = (Dictionary<string, object>)OsmJson.Parse(File.ReadAllText(path));

            foreach (object item in (List<object>)root["elements"])
            {
                var element = (Dictionary<string, object>)item;
                string type = (string)element["type"];
                var tags = new Dictionary<string, string>();

                if (element.TryGetValue("tags", out object tagObject))
                {
                    foreach (KeyValuePair<string, object> pair in (Dictionary<string, object>)tagObject)
                    {
                        tags[pair.Key] = pair.Value as string ?? pair.Value?.ToString();
                    }
                }

                long id = (long)(double)element["id"];

                if (type == "node")
                {
                    map.Nodes.Add(new OsmNode { Id = id, Pos = map.Project((double)element["lat"], (double)element["lon"]), Tags = tags });
                }
                else if (type == "way" && element.ContainsKey("geometry"))
                {
                    var way = new OsmWay { Id = id, Tags = tags };
                    var geometry = (List<object>)element["geometry"];
                    var ids = element.TryGetValue("nodes", out object nodeList) ? (List<object>)nodeList : null;

                    for (int i = 0; i < geometry.Count; i++)
                    {
                        var point = (Dictionary<string, object>)geometry[i];
                        way.Points.Add(map.Project((double)point["lat"], (double)point["lon"]));
                        way.NodeIds.Add(ids != null && i < ids.Count ? (long)(double)ids[i] : map.NewId());
                    }

                    map.Ways.Add(way);
                }
            }

            return map;
        }

        private long NewId() => _generated--;

        /// <summary>Latitude/longitude em graus para metros locais.</summary>
        public Vector2 Project(double lat, double lon)
        {
            double x = (lon - _lon0) * System.Math.PI / 180.0 * EarthRadius * System.Math.Cos(_lat0 * System.Math.PI / 180.0);
            double z = (lat - _lat0) * System.Math.PI / 180.0 * EarthRadius;

            return new Vector2((float)x, (float)z);
        }

        public bool InsideBox(Vector2 p) => Mathf.Abs(p.x) <= HalfX && Mathf.Abs(p.y) <= HalfZ;

        /// <summary>Recorta uma linha pela caixa: devolve os pedaços de dentro (os cortes ganham ids de nó novos).</summary>
        public List<OsmWay> ClipLine(OsmWay way)
        {
            var pieces = new List<OsmWay>();
            OsmWay current = null;

            for (int k = 0; k + 1 < way.Points.Count; k++)
            {
                Vector2 a = way.Points[k];
                Vector2 b = way.Points[k + 1];
                Vector2 ca = a;
                Vector2 cb = b;

                if (!Geo.ClipSegment(ref ca, ref cb, HalfX, HalfZ) || (cb - ca).sqrMagnitude < 0.01f)
                {
                    current = null;

                    continue;
                }

                bool startMoved = (ca - a).sqrMagnitude > 1e-6f;
                bool endMoved = (cb - b).sqrMagnitude > 1e-6f;

                if (current == null || startMoved)
                {
                    current = new OsmWay { Id = way.Id, Tags = way.Tags };
                    current.Points.Add(ca);
                    current.NodeIds.Add(startMoved ? NewId() : way.NodeIds[k]);
                    pieces.Add(current);
                }

                current.Points.Add(cb);
                current.NodeIds.Add(endMoved ? NewId() : way.NodeIds[k + 1]);

                if (endMoved)
                {
                    current = null;
                }
            }

            return pieces;
        }
    }
}

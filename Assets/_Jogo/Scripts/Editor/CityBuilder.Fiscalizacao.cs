using System.Collections.Generic;
using Entregador.Game;
using TMPro;
using UnityEngine;

namespace Entregador.Editor
{
    /// <summary>
    /// Fiscalização de trânsito da cidade: radares fixos nas avenidas (poste com a caixa da câmera, placa "FISCALIZAÇÃO ELETRÔNICA" 60 m
    /// antes e o gatilho de medição), pontos de blitz (cones afunilando a pista, viatura com barra de luz, policiais; desligados até o dia
    /// com blitz) e a viatura de ronda num circuito de ruas. Peças simples (primitivas e o sedã do kit de carros): modelos de verdade vêm depois.
    /// </summary>
    public static partial class CityBuilder
    {
        private const int RadarCount = 7;
        private const int BlitzSiteCount = 5;
        private const float RadarSignDistance = 60f;

        /// <summary>Um trecho reto de rua onde cabe um radar ou uma blitz.</summary>
        private sealed class RoadSpot
        {
            public Road Road;
            public Vector2 Point;
            public Vector2 Dir;
            public Vector2 SignPoint;
            public float ExtRight;
            public float ExtLeft;
            public int Limit;
            public bool Primary;
        }

        private static readonly List<ViewPoint> FiscalViews = new List<ViewPoint>();

        private static ViewPoint[] AppendFiscalViews(ViewPoint[] views)
        {
            var all = new List<ViewPoint>(views);
            all.AddRange(FiscalViews);

            return all.ToArray();
        }

        private static void BuildFiscalizacao(Transform world, Vector3 spawn, Place[] restaurants, Place[] customers)
        {
            FiscalViews.Clear();
            Transform root = Group(world, "Fiscalizacao");
            var avoid = new List<Vector3> { spawn };

            foreach (Place place in restaurants)
            {
                avoid.Add(place.Position);
            }

            foreach (Place place in customers)
            {
                avoid.Add(place.Position);
            }

            var spots = FindRoadSpots(65f);
            var rnd = new System.Random(2024);
            Shuffle(spots, rnd);
            spots.Sort((a, b) => b.Primary.CompareTo(a.Primary));

            var chosenRadars = new List<RoadSpot>();
            var taken = new List<Vector3>();

            foreach (RoadSpot spot in spots)
            {
                if (chosenRadars.Count >= RadarCount)
                {
                    break;
                }

                Vector3 p = Geo.V3(spot.Point);

                if (NearAny(p, taken, 170f) || NearAny(p, avoid, 25f) || NearAny(Geo.V3(spot.SignPoint), avoid, 8f))
                {
                    continue;
                }

                chosenRadars.Add(spot);
                taken.Add(p);
            }

            Transform radars = Group(root, "Radares");

            for (int i = 0; i < chosenRadars.Count; i++)
            {
                BuildRadar(radars, chosenRadars[i], i);
            }

            Transform blitz = Group(root, "Blitz");
            int sites = 0;

            foreach (RoadSpot spot in spots)
            {
                if (sites >= BlitzSiteCount)
                {
                    break;
                }

                Vector3 p = Geo.V3(spot.Point);

                if (NearAny(p, taken, 130f) || NearAny(p, avoid, 70f) || (spot.Road.Kind != RoadKind.Primary && spot.Road.Kind != RoadKind.Tertiary))
                {
                    continue;
                }

                BuildBlitzSite(blitz, spot, sites);
                taken.Add(p);
                sites++;
            }

            BuildPatrol(root);
            Debug.Log($"[CityBuilder] Fiscalização: {chosenRadars.Count} radares, {sites} pontos de blitz e 1 viatura de ronda.");
        }

        private static void Shuffle<T>(List<T> list, System.Random rnd)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = rnd.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }

        // ---------- Onde pôr ----------

        private static Vector2 PointAlong(Road r, float s, out Vector2 dir, out int segment)
        {
            float travelled = 0f;

            for (int k = 0; k + 1 < r.Pts.Count; k++)
            {
                float length = Vector2.Distance(r.Pts[k], r.Pts[k + 1]);

                if (s <= travelled + length || k + 2 == r.Pts.Count)
                {
                    dir = length > 0.01f ? (r.Pts[k + 1] - r.Pts[k]) / length : Vector2.up;
                    segment = k;

                    return Vector2.Lerp(r.Pts[k], r.Pts[k + 1], Mathf.Clamp01((s - travelled) / Mathf.Max(0.01f, length)));
                }

                travelled += length;
            }

            dir = Vector2.up;
            segment = 0;

            return r.Pts[0];
        }

        /// <summary>Pontos de ruas principais (avenidas e coletoras) onde os últimos straightMeters são retos, dentro da área jogável, longe de cruzamentos e dos corredores de ônibus.</summary>
        private static List<RoadSpot> FindRoadSpots(float straightMeters)
        {
            var spots = new List<RoadSpot>();
            float limit = PlayableHalf - 20f;

            foreach (Road r in Roads)
            {
                if (!r.Drivable || (r.Kind != RoadKind.Primary && r.Kind != RoadKind.Tertiary) || r.Pts.Count < 2)
                {
                    continue;
                }

                float total = 0f;

                for (int k = 0; k + 1 < r.Pts.Count; k++)
                {
                    total += Vector2.Distance(r.Pts[k], r.Pts[k + 1]);
                }

                // Sentido da mão: contrário à ordem dos pontos em ruas com oneway=-1.
                float sign = r.Oneway && r.Reverse ? -1f : 1f;

                for (float s = straightMeters + 5f; s < total - 5f; s += 10f)
                {
                    float back = s - sign * straightMeters;

                    if (back < 0f || back > total)
                    {
                        continue;
                    }

                    Vector2 p = PointAlong(r, s, out _, out int segment);
                    Vector2 behind = PointAlong(r, back, out _, out _);
                    Vector2 delta = p - behind;

                    if (delta.magnitude < straightMeters * 0.985f)
                    {
                        continue;
                    }

                    if (Mathf.Abs(p.x) > limit || Mathf.Abs(p.y) > limit || Mathf.Abs(behind.x) > limit || Mathf.Abs(behind.y) > limit
                        || NearJunction(p, 16f) || NearJunction(behind, 6f) || InBusLane(Geo.V3(p)) || InBusLane(Geo.V3(behind)))
                    {
                        continue;
                    }

                    bool flowLeft = sign < 0f;
                    spots.Add(new RoadSpot
                    {
                        Road = r,
                        Point = p,
                        Dir = delta.normalized,
                        SignPoint = behind,
                        ExtRight = flowLeft ? r.ExtL[segment] : r.ExtR[segment],
                        ExtLeft = flowLeft ? r.ExtR[segment] : r.ExtL[segment],
                        Limit = SpeedLimitOf(r),
                        Primary = r.Kind == RoadKind.Primary,
                    });
                }
            }

            return spots;
        }

        // ---------- Peças ----------

        private static GameObject Prim(PrimitiveType type, Transform parent, string name, Vector3 position, Quaternion rotation, Vector3 scale, Material material)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(position, rotation);
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = material;
            Object.DestroyImmediate(go.GetComponent<Collider>());

            return go;
        }

        private static TextMeshPro Label(Transform parent, string name, Vector3 position, Vector3 viewerLooks, string text, Color color, Vector2 size, float fontMax)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(position, Quaternion.LookRotation(viewerLooks, Vector3.up));
            var tmp = go.AddComponent<TextMeshPro>();
            tmp.rectTransform.sizeDelta = size;
            tmp.text = text;
            tmp.fontStyle = FontStyles.Bold;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.enableAutoSizing = true;
            tmp.fontSizeMin = 0.5f;
            tmp.fontSizeMax = fontMax;
            tmp.textWrappingMode = TextWrappingModes.Normal;
            tmp.color = color;
            tmp.fontSize = fontMax;

            return tmp;
        }

        // ---------- Radar ----------

        private static void BuildRadar(Transform parent, RoadSpot spot, int index)
        {
            Vector3 dir = new Vector3(spot.Dir.x, 0f, spot.Dir.y);
            Vector3 right = Geo.V3(Geo.Right(spot.Dir));
            Quaternion facing = Quaternion.LookRotation(dir, Vector3.up);
            Vector3 axis = Geo.V3(spot.Point);
            float side = spot.ExtRight + 0.9f;
            float baseY = 0.15f;

            Material pole = CityMaterials.Lit("Fisc_Poste", new Color(0.4f, 0.41f, 0.43f), 0.2f);
            Material dark = CityMaterials.Lit("Fisc_Caixa", new Color(0.07f, 0.07f, 0.08f), 0.35f);
            Material lens = CityMaterials.Lit("Fisc_Lente", new Color(0.02f, 0.03f, 0.06f), 0.9f);
            Material flash = CityMaterials.Unlit("Fisc_Flash", new Color(1f, 1f, 0.92f));
            Material loop = CityMaterials.Lit("Fisc_Laco", new Color(0.05f, 0.05f, 0.05f), 0.1f);

            var radar = new GameObject($"Radar_{index}_{spot.Limit}kmh");
            radar.transform.SetParent(parent, false);
            radar.transform.SetPositionAndRotation(axis, facing);

            // Poste na calçada, braço sobre a pista e a caixa da câmera olhando para quem chega.
            Vector3 poleBase = axis + right * side;
            Prim(PrimitiveType.Cylinder, radar.transform, "Poste", poleBase + Vector3.up * (baseY + 2.75f), Quaternion.identity, new Vector3(0.22f, 2.75f, 0.22f), pole);
            Prim(PrimitiveType.Cube, radar.transform, "Braco", poleBase - right * 0.9f + Vector3.up * (baseY + 5.3f), facing, new Vector3(1.9f, 0.12f, 0.12f), pole);
            Vector3 boxAt = poleBase - right * 1.7f + Vector3.up * (baseY + 5.05f);
            Prim(PrimitiveType.Cube, radar.transform, "CaixaCamera", boxAt, facing, new Vector3(0.55f, 0.42f, 0.9f), dark);
            Prim(PrimitiveType.Cylinder, radar.transform, "Lente", boxAt - dir * 0.46f, facing * Quaternion.Euler(90f, 0f, 0f), new Vector3(0.24f, 0.03f, 0.24f), lens);
            Prim(PrimitiveType.Cube, radar.transform, "Painel", poleBase + Vector3.up * (baseY + 2.2f) - dir * 0.12f, facing, new Vector3(0.5f, 0.7f, 0.1f), dark);

            // Flash: lente branca e uma luz, ligadas só quando o radar captura.
            GameObject flashLens = Prim(PrimitiveType.Sphere, radar.transform, "Flash", boxAt - dir * 0.52f, facing, Vector3.one * 0.3f, flash);
            var lightObject = new GameObject("LuzFlash");
            lightObject.transform.SetParent(radar.transform, false);
            lightObject.transform.position = boxAt - dir * 1.2f;
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Point;
            light.range = 30f;
            light.intensity = 40f;
            light.color = new Color(1f, 0.98f, 0.9f);
            light.shadows = LightShadows.None;

            // Laços no asfalto (onde o radar mede) e o gatilho que atravessa a pista.
            float width = spot.ExtLeft + spot.ExtRight;
            float centerShift = (spot.ExtRight - spot.ExtLeft) * 0.5f;
            Vector3 center = axis + right * centerShift;

            for (int i = 0; i < 2; i++)
            {
                Prim(PrimitiveType.Cube, radar.transform, "Laco" + i, center + dir * (i * 1.8f - 0.9f) + Vector3.up * 0.03f, facing, new Vector3(width - 0.4f, 0.01f, 0.3f), loop);
            }

            var trigger = new GameObject("Gatilho");
            trigger.transform.SetParent(radar.transform, false);
            trigger.transform.SetPositionAndRotation(center + Vector3.up * 1.5f, facing);
            var box = trigger.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(width + 1f, 3f, 3f);
            var camera = trigger.AddComponent<SpeedCamera>();
            camera.Configure(spot.Limit, light, flashLens.GetComponent<Renderer>());
            light.enabled = false;
            flashLens.GetComponent<Renderer>().enabled = false;

            BuildRadarSign(radar.transform, spot, dir, right, side, baseY);

            Vector2 view = spot.SignPoint - spot.Dir * 18f + Geo.Right(spot.Dir) * (spot.Road.Oneway ? 0f : 1.9f);
            FiscalViews.Add(new ViewPoint("radar" + index, Geo.V3(view, 0.1f), Mathf.Atan2(spot.Dir.x, spot.Dir.y) * Mathf.Rad2Deg));
        }

        /// <summary>Placa "FISCALIZAÇÃO ELETRÔNICA" com o limite, 60 m antes do radar, virada para quem chega.</summary>
        private static void BuildRadarSign(Transform parent, RoadSpot spot, Vector3 dir, Vector3 right, float side, float baseY)
        {
            Material pole = CityMaterials.Lit("Fisc_Poste", new Color(0.4f, 0.41f, 0.43f), 0.2f);
            Material board = CityMaterials.Lit("Fisc_Placa", new Color(0.95f, 0.95f, 0.92f), 0.15f);
            Material red = CityMaterials.Lit("Fisc_Vermelho", new Color(0.75f, 0.08f, 0.08f), 0.15f);
            Quaternion facing = Quaternion.LookRotation(dir, Vector3.up);

            Vector3 at = Geo.V3(spot.SignPoint) + right * (spot.ExtRight + 0.7f);
            Prim(PrimitiveType.Cylinder, parent, "PostePlaca", at + Vector3.up * (baseY + 1.5f), Quaternion.identity, new Vector3(0.12f, 1.5f, 0.12f), pole);
            Vector3 center = at + Vector3.up * (baseY + 3.0f);
            Prim(PrimitiveType.Cube, parent, "Placa", center, facing, new Vector3(1.7f, 1.3f, 0.06f), red);
            Prim(PrimitiveType.Cube, parent, "PlacaMiolo", center - dir * 0.035f, facing, new Vector3(1.58f, 1.18f, 0.03f), board);

            Vector3 front = center - dir * 0.06f;
            Label(parent, "TextoFiscalizacao", front + Vector3.up * 0.33f, dir, "FISCALIZAÇÃO\nELETRÔNICA", new Color(0.08f, 0.08f, 0.1f), new Vector2(1.5f, 0.55f), 3f);
            Label(parent, "TextoLimite", front - Vector3.up * 0.3f, dir, spot.Limit + " km/h", new Color(0.75f, 0.08f, 0.08f), new Vector2(1.5f, 0.5f), 3f);
        }

        // ---------- Blitz ----------

        private static void BuildBlitzSite(Transform parent, RoadSpot spot, int index)
        {
            Vector3 dir = new Vector3(spot.Dir.x, 0f, spot.Dir.y);
            Vector3 right = Geo.V3(Geo.Right(spot.Dir));
            Quaternion facing = Quaternion.LookRotation(dir, Vector3.up);
            Vector3 axis = Geo.V3(spot.Point);
            float halfWidth = Mathf.Min(spot.ExtRight, spot.ExtLeft);
            float narrow = Mathf.Min(4.2f, halfWidth * 0.6f);

            var root = new GameObject($"Blitz_{index}");
            root.transform.SetParent(parent, false);
            root.transform.SetPositionAndRotation(axis, facing);

            var zone = root.AddComponent<BoxCollider>();
            zone.isTrigger = true;
            float width = spot.ExtLeft + spot.ExtRight;
            zone.center = new Vector3((spot.ExtRight - spot.ExtLeft) * 0.5f, 1.5f, 0f);
            zone.size = new Vector3(width + 2f, 4f, 14f);
            var site = root.AddComponent<BlitzSite>();

            // Cones: dois funis (um para cada sentido) que estreitam a pista até a viatura, começando uns 34 m antes.
            for (int dirSign = -1; dirSign <= 1; dirSign += 2)
            {
                for (int i = 0; i < 9; i++)
                {
                    float t = i / 8f;
                    float along = dirSign * (36f - 32f * t);
                    float edge = dirSign > 0 ? spot.ExtRight - 0.7f - narrow * t : -(spot.ExtLeft - 0.7f - narrow * t);
                    Vector3 p = axis - dir * along + right * edge;
                    CityPieces.Model(root.transform, Kit.CityKitRoads, "construction-cone", new Vector3(p.x, 0.02f, p.z), 0f, 6.5f).name = "Cone";
                }
            }

            // Viatura na lateral, levemente enviesada, e os policiais.
            Vector3 carAt = axis + right * (spot.ExtRight - 2.3f) - dir * 1.5f;
            PoliceUnit unit = BuildPoliceCar(root.transform, "Viatura", carAt, facing.eulerAngles.y + 16f);
            site.Configure(unit);

            Material uniform = CityMaterials.Lit("Fisc_Uniforme", new Color(0.1f, 0.14f, 0.26f), 0.1f);
            BuildOfficer(root.transform, axis + right * (spot.ExtRight - 4.9f) + dir * 1.2f, facing.eulerAngles.y + 200f, uniform, 0);
            BuildOfficer(root.transform, axis + right * (spot.ExtRight - 6.8f) - dir * 3.5f, facing.eulerAngles.y + 180f, uniform, 1);
            BuildOfficer(root.transform, axis + right * (spot.ExtRight - 1.2f) + dir * 2.2f, facing.eulerAngles.y + 120f, uniform, 2);

            root.SetActive(false);

            Vector2 view = spot.Point - spot.Dir * 85f + Geo.Right(spot.Dir) * (spot.Road.Oneway ? 0f : 1.9f);
            FiscalViews.Add(new ViewPoint("blitz" + index, Geo.V3(view, 0.1f), Mathf.Atan2(spot.Dir.x, spot.Dir.y) * Mathf.Rad2Deg));
        }

        /// <summary>Sedã branco do kit de carros com faixa azul, barra de luz (vermelha e azul) e a lógica de PoliceUnit.</summary>
        private static PoliceUnit BuildPoliceCar(Transform parent, string name, Vector3 position, float yaw)
        {
            Bounds bounds = CityPieces.ModelBounds(Kit.CarKit, "sedan");
            float scale = CityPieces.CarScale;
            Vector3 size = bounds.size * scale;
            Vector3 center = bounds.center * scale;

            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            root.transform.SetPositionAndRotation(new Vector3(position.x, 0.02f, position.z), Quaternion.Euler(0f, yaw, 0f));
            CityPieces.Car(root.transform, "sedan", root.transform.position, yaw, false, 0).name = "Carro";

            var box = root.AddComponent<BoxCollider>();
            box.center = center;
            box.size = size;
            root.AddComponent<Rigidbody>().isKinematic = true;

            Material stripe = CityMaterials.Lit("Fisc_FaixaAzul", new Color(0.05f, 0.1f, 0.38f), 0.2f);
            Material redLamp = CityMaterials.Unlit("Fisc_LuzVermelha", new Color(1f, 0.04f, 0.04f));
            Material blueLamp = CityMaterials.Unlit("Fisc_LuzAzul", new Color(0.12f, 0.3f, 1f));
            Material barBase = CityMaterials.Lit("Fisc_BarraBase", new Color(0.04f, 0.04f, 0.05f), 0.3f);
            Transform t = root.transform;

            for (int side = -1; side <= 1; side += 2)
            {
                Vector3 local = new Vector3(side * (size.x * 0.5f + 0.005f), size.y * 0.36f, center.z);
                Prim(PrimitiveType.Cube, t, "Faixa", t.TransformPoint(local), t.rotation, new Vector3(0.02f, 0.3f, size.z * 0.62f), stripe);
            }

            float roof = size.y - 0.02f;
            Vector3 bar = new Vector3(0f, roof, center.z - 0.15f);
            Prim(PrimitiveType.Cube, t, "BarraBase", t.TransformPoint(bar + Vector3.up * 0.03f), t.rotation, new Vector3(0.95f, 0.06f, 0.3f), barBase);
            GameObject red = Prim(PrimitiveType.Cube, t, "LuzVermelha", t.TransformPoint(bar + new Vector3(-0.25f, 0.1f, 0f)), t.rotation, new Vector3(0.42f, 0.1f, 0.24f), redLamp);
            GameObject blue = Prim(PrimitiveType.Cube, t, "LuzAzul", t.TransformPoint(bar + new Vector3(0.25f, 0.1f, 0f)), t.rotation, new Vector3(0.42f, 0.1f, 0.24f), blueLamp);
            red.GetComponent<Renderer>().enabled = false;
            blue.GetComponent<Renderer>().enabled = false;

            Light lightRed = NewPoliceLight(t, "FocoVermelho", t.TransformPoint(bar + new Vector3(-0.6f, 0.5f, 0f)), new Color(1f, 0.1f, 0.05f));
            Light lightBlue = NewPoliceLight(t, "FocoAzul", t.TransformPoint(bar + new Vector3(0.6f, 0.5f, 0f)), new Color(0.15f, 0.3f, 1f));

            var unit = root.AddComponent<PoliceUnit>();
            unit.Configure(new[] { red.GetComponent<Renderer>() }, new[] { blue.GetComponent<Renderer>() }, lightRed, lightBlue, _graph);

            return unit;
        }

        private static Light NewPoliceLight(Transform parent, string name, Vector3 position, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = position;
            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.range = 14f;
            light.intensity = 12f;
            light.color = color;
            light.shadows = LightShadows.None;
            light.enabled = false;

            return light;
        }

        /// <summary>Policial simples: pernas, tronco de uniforme azul-escuro, cabeça e quepe; o tipo 1 usa colete refletivo.</summary>
        private static void BuildOfficer(Transform parent, Vector3 position, float yaw, Material uniform, int kind)
        {
            Material trousers = CityMaterials.Lit("Fisc_Calca", new Color(0.06f, 0.07f, 0.1f), 0.1f);
            Material skin = CityMaterials.Lit("Fisc_Pele" + (kind % 2), kind % 2 == 0 ? new Color(0.62f, 0.44f, 0.33f) : new Color(0.42f, 0.28f, 0.2f), 0.1f);
            Material vest = CityMaterials.Lit("Fisc_Colete", new Color(0.85f, 0.95f, 0.1f), 0.1f);
            Material cap = CityMaterials.Lit("Fisc_Quepe", new Color(0.05f, 0.06f, 0.1f), 0.2f);

            var root = new GameObject("Policial_" + kind);
            root.transform.SetParent(parent, false);
            root.transform.SetPositionAndRotation(new Vector3(position.x, 0.15f, position.z), Quaternion.Euler(0f, yaw, 0f));
            Transform t = root.transform;
            Vector3 p = t.position;

            Prim(PrimitiveType.Capsule, t, "Pernas", p + Vector3.up * 0.45f, t.rotation, new Vector3(0.36f, 0.45f, 0.3f), trousers);
            Prim(PrimitiveType.Capsule, t, "Tronco", p + Vector3.up * 1.18f, t.rotation, new Vector3(0.46f, 0.36f, 0.3f), kind == 1 ? vest : uniform);
            Prim(PrimitiveType.Sphere, t, "Cabeca", p + Vector3.up * 1.68f, t.rotation, Vector3.one * 0.24f, skin);
            Prim(PrimitiveType.Cylinder, t, "Quepe", p + Vector3.up * 1.82f, t.rotation, new Vector3(0.27f, 0.035f, 0.27f), cap);
        }

        // ---------- Ronda ----------

        /// <summary>Uma viatura rondando o maior circuito do trânsito (luzes apagadas até um flagrante).</summary>
        private static void BuildPatrol(Transform parent)
        {
            List<int> best = null;
            Vector3[] bestPoints = null;
            float bestLength = 0f;

            foreach (List<int> loop in _trafficLoops)
            {
                Vector3[] points = LoopWaypoints(loop);
                float length = 0f;

                for (int i = 0; i < points.Length; i++)
                {
                    length += Vector3.Distance(points[i], points[(i + 1) % points.Length]);
                }

                if (length > bestLength)
                {
                    bestLength = length;
                    best = loop;
                    bestPoints = points;
                }
            }

            if (best == null || bestPoints.Length < 3)
            {
                return;
            }

            int start = bestPoints.Length / 3;
            Vector3 a = bestPoints[start];
            Vector3 b = bestPoints[(start + 1) % bestPoints.Length];
            float yaw = Mathf.Atan2(b.x - a.x, b.z - a.z) * Mathf.Rad2Deg;
            PoliceUnit unit = BuildPoliceCar(parent, "Viatura_Ronda", a, yaw);
            unit.SetPatrol(bestPoints, 8.5f, start);
        }
    }
}

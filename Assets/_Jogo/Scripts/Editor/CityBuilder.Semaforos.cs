using System.Collections.Generic;
using Entregador.Core;
using Entregador.Game;
using UnityEngine;
using UnityEngine.Rendering;

namespace Entregador.Editor
{
    /// <summary>
    /// Semáforos que funcionam. Um controlador por cruzamento dos nós highway=traffic_signals (e crossing=traffic_signals) do OpenStreetMap;
    /// os braços de chegada são separados em dois eixos pelo rumo. Cada braço ganha linha de retenção pintada, poste cinza-escuro com a base
    /// listrada de amarelo e preto, foco de 3 lentes na coluna e outro no braço projetado sobre a pista, e foco de pedestre. As lentes são
    /// objetos à parte (materiais emissivos que o SemaforoControlador troca); o resto vai nos lotes de malha. Cerca de 25% dos cruzamentos
    /// têm fotossensor (câmera de avanço de sinal, com a placa 35 m antes).
    /// </summary>
    public static partial class CityBuilder
    {
        private const float StopLineBeyondStopDistance = 4.6f;
        private const float CameraFraction = 4f;

        private static readonly List<ViewPoint> SemaforoViews = new List<ViewPoint>();

        /// <summary>Resumo do último build, para o log.</summary>
        private static int _semaforoCount;
        private static int _semaforoCameraCount;
        private static int _semaforoHeadCount;
        private static int _semaforoOsm;
        private static int _semaforoFallback;
        private const float FallbackSpacing = 70f;

        private struct SemaforoSite
        {
            public OsmNode Node;
            public List<SignalArm> Arms;
            public List<int> Groups;
            public bool MidBlock;
            public bool Fallback;
        }

        private static Material[] SemaforoLenses(bool on)
        {
            Color[] colors = { new Color(0.95f, 0.05f, 0.03f), new Color(1f, 0.6f, 0.04f), new Color(0.05f, 0.9f, 0.35f) };
            string[] names = { "Vermelho", "Amarelo", "Verde" };
            var result = new Material[3];

            for (int i = 0; i < 3; i++)
            {
                Color baseColor = on ? colors[i] * 0.35f : colors[i] * 0.16f;
                Material material = CityMaterials.Lit($"SemaforoLente_{names[i]}_{(on ? "Acesa" : "Apagada")}", baseColor, on ? 0.6f : 0.9f);
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", colors[i] * (on ? 4.5f : 0.05f));
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                UnityEditor.EditorUtility.SetDirty(material);
                result[i] = material;
            }

            return result;
        }

        /// <summary>Monta os semáforos. Devolve os cantos dos postes (para as placas de PARE não colidirem).</summary>
        private static List<Vector2> BuildSemaforos(OsmMap map)
        {
            SemaforoViews.Clear();
            _semaforoCount = _semaforoCameraCount = _semaforoHeadCount = 0;
            var corners = new List<Vector2>();
            Transform root = Group(_signalRoot.parent, "Semaforos");
            root.gameObject.AddComponent<SemaforoFiscal>();
            Material[] on = SemaforoLenses(true);
            Material[] off = SemaforoLenses(false);

            // 1. Cruzamentos: um por grupo de nós a menos de 22 m (a travessia de pedestre ao lado de um semáforo é do mesmo).
            var sites = new List<SemaforoSite>();
            var centers = new List<Vector2>();

            foreach (OsmNode node in map.Nodes)
            {
                bool signal = node.Tag("highway") == "traffic_signals" || node.Tag("crossing") == "traffic_signals";

                if (!signal || !map.InsideBox(node.Pos) || centers.Exists(c => Vector2.Distance(c, node.Pos) < 22f))
                {
                    continue;
                }

                var arms = new List<SignalArm>();

                foreach (SignalArm arm in SignalArms(node.Pos))
                {
                    if (arm.Approach && ArmTotalLength(arm) >= arm.StopDistance + 2.5f)
                    {
                        arms.Add(arm);
                    }
                }

                if (arms.Count == 0)
                {
                    continue;
                }

                centers.Add(node.Pos);
                sites.Add(MakeSite(node, arms));
            }

            int osmCount = sites.Count;

            // 1b. Reserva: cruzamentos de duas vias principais/secundárias/terciárias (grau 3 ou mais, sem rotatória), a 120 m ou mais
            // de qualquer outro semáforo, mais ou menos como a cidade real (o OSM aqui é esparso).
            var ordered = new List<Junction>(Junctions);
            ordered.Sort((x, y) => JunctionRank(y).CompareTo(JunctionRank(x)));

            foreach (Junction j in ordered)
            {
                if (!map.InsideBox(j.Pos) || centers.Exists(c => Vector2.Distance(c, j.Pos) < FallbackSpacing))
                {
                    continue;
                }

                List<SignalArm> all = SignalArms(j.Pos);
                var majorWays = new HashSet<long>();
                bool roundabout = false;

                foreach (SignalArm arm in all)
                {
                    roundabout |= arm.Road.Roundabout;

                    if (arm.Road.Class >= RoadClass.Tertiary)
                    {
                        majorWays.Add(arm.Road.WayId);
                    }
                }

                int majorArms = all.FindAll(x => x.Road.Class >= RoadClass.Tertiary).Count;

                if (roundabout || all.Count < 3 || (majorWays.Count < 2 && majorArms < 2 && !(majorArms >= 1 && all.Count >= 4)))
                {
                    continue;
                }

                var arms = new List<SignalArm>();

                foreach (SignalArm arm in all)
                {
                    if (arm.Approach && ArmTotalLength(arm) >= arm.StopDistance + 2.5f)
                    {
                        arms.Add(arm);
                    }
                }

                var fake = new OsmNode { Id = 900000000L + (long)(j.Pos.x * 7f) * 1000 + (long)(j.Pos.y * 7f), Pos = j.Pos };
                SemaforoSite site = arms.Count == 0 ? default : MakeSite(fake, arms);

                if (arms.Count == 0 || (site.MidBlock && AvenueOf(site) == null))
                {
                    continue;
                }

                site.Fallback = true;
                centers.Add(j.Pos);
                sites.Add(site);
            }

            // 2. Fotossensor: um a cada quatro cruzamentos de verdade e pelo menos um em cada avenida (nome de via principal).
            var cameras = new bool[sites.Count];
            int candidate = 0;

            for (int i = 0; i < sites.Count; i++)
            {
                if (!sites[i].MidBlock)
                {
                    cameras[i] = candidate % (int)CameraFraction == 1;
                    candidate++;
                }
            }

            var avenues = new HashSet<string>();

            for (int i = 0; i < sites.Count; i++)
            {
                string avenue = AvenueOf(sites[i]);

                if (avenue == null || avenues.Contains(avenue))
                {
                    continue;
                }

                avenues.Add(avenue);
                bool has = false;

                for (int k = 0; k < sites.Count; k++)
                {
                    has |= cameras[k] && AvenueOf(sites[k]) == avenue;
                }

                if (!has && !sites[i].MidBlock)
                {
                    cameras[i] = true;
                }
            }

            if (sites.Count > 0 && !System.Array.Exists(cameras, c => c))
            {
                cameras[0] = true;
            }

            int built = 0;

            for (int i = 0; i < sites.Count; i++)
            {
                BuildSemaforo(root, sites[i], built, cameras[i], on, off, corners);
                built++;
            }

            _semaforoOsm = osmCount;
            _semaforoFallback = sites.Count - osmCount;

            Debug.Log($"[CityBuilder] Semáforos: {_semaforoCount} cruzamentos ({_semaforoOsm} dos nós OSM traffic_signals, {_semaforoFallback} por reserva de vias principais), {_semaforoHeadCount} focos, {_semaforoCameraCount} com fotossensor.");

            return corners;
        }

        /// <summary>Comprimento (m) do braço seguindo a polilinha da rua, do nó até a ponta da rua.</summary>
        private static float ArmTotalLength(SignalArm arm)
        {
            ArmPoint(arm, 100000f, out _, out _, out _, out float walked);

            return walked;
        }

        /// <summary>
        /// Anda "distance" metros pela polilinha da rua a partir do nó do braço (passando por vários trechos, se preciso) e devolve o
        /// ponto, a direção local (para fora do nó) e o trecho. Se a rua acaba antes, devolve o último ponto e walked menor que distance.
        /// </summary>
        private static void ArmPoint(SignalArm arm, float distance, out Vector2 point, out Vector2 direction, out int segment, out float walked)
        {
            List<Vector2> pts = arm.Road.Pts;
            bool forward = Vector2.Dot(arm.Dir, pts[arm.Index + 1] - pts[arm.Index]) >= 0f;
            int k = arm.Index;
            Vector2 current = arm.Node;
            float remaining = distance;
            point = current;
            direction = arm.Dir;
            segment = k;
            walked = 0f;

            while (true)
            {
                Vector2 target = forward ? pts[k + 1] : pts[k];
                Vector2 offset = target - current;
                float length = offset.magnitude;

                if (length > 0.01f)
                {
                    direction = offset / length;
                    segment = k;

                    if (remaining <= length)
                    {
                        point = current + direction * remaining;
                        walked += remaining;

                        return;
                    }

                    walked += length;
                    remaining -= length;
                }

                current = target;
                point = current;
                int next = forward ? k + 1 : k - 1;

                if (next < 0 || next + 1 >= pts.Count)
                {
                    return;
                }

                k = next;
            }
        }

        /// <summary>Direção do braço (para fora do nó) onde fica a linha de retenção: o rumo que decide o eixo do semáforo.</summary>
        private static Vector2 ArmBearing(SignalArm arm)
        {
            ArmPoint(arm, arm.StopDistance + StopLineBeyondStopDistance, out _, out Vector2 dir, out _, out _);

            return dir;
        }

        /// <summary>Importância de um nó: a maior classe de via que passa nele, depois o grau (avenidas primeiro).</summary>
        private static float JunctionRank(Junction j)
        {
            int best = 0;

            foreach (SignalArm arm in SignalArms(j.Pos))
            {
                best = Mathf.Max(best, (int)arm.Road.Class);
            }

            return best * 10f + j.Degree;
        }

        /// <summary>Nome da via principal do cruzamento (a do grupo 0), só para vias de classe primária.</summary>
        private static string AvenueOf(SemaforoSite site)
        {
            SignalArm best = site.Arms[0];

            foreach (SignalArm arm in site.Arms)
            {
                if (arm.Road.Class > best.Road.Class)
                {
                    best = arm;
                }
            }

            return best.Road.Class >= RoadClass.Primary && !string.IsNullOrEmpty(best.Road.Name) ? best.Road.Name : null;
        }

        private static SemaforoSite MakeSite(OsmNode node, List<SignalArm> arms)
        {
            // Eixo do grupo 0: o braço da via maior (e mais comprida).
            SignalArm best = arms[0];

            foreach (SignalArm arm in arms)
            {
                if (arm.Road.Class > best.Road.Class || (arm.Road.Class == best.Road.Class && ArmTotalLength(arm) > ArmTotalLength(best)))
                {
                    best = arm;
                }
            }

            var groups = new List<int>();
            bool second = false;

            foreach (SignalArm arm in arms)
            {
                int group = Mathf.Abs(Vector2.Dot(ArmBearing(arm), ArmBearing(best))) >= 0.7f ? 0 : 1;
                second |= group == 1;
                groups.Add(group);
            }

            return new SemaforoSite { Node = node, Arms = arms, Groups = groups, MidBlock = !second };
        }

        private static void BuildSemaforo(Transform root, SemaforoSite site, int index, bool camera, Material[] on, Material[] off, List<Vector2> corners)
        {
            var random = new System.Random((int)(site.Node.Id % 1000003) + 17);
            var config = new SemaforoConfig();

            if (site.MidBlock)
            {
                config.VerdeA = 30;
                config.VerdeB = 12;
                config.TodoVermelho = 1;
            }
            else
            {
                config.VerdeA = 24 + random.Next(0, 7);
                config.VerdeB = 18 + random.Next(0, 7);
                config.TodoVermelho = 1 + random.Next(0, 2);
            }

            config.Deslocamento = random.NextDouble() * config.Duracao;

            var go = new GameObject($"Semaforo_{index}");
            go.transform.SetParent(root, false);
            go.transform.position = Geo.V3(site.Node.Pos);
            var controller = go.AddComponent<SemaforoControlador>();
            var approaches = new List<SemaforoAproximacao>();

            for (int i = 0; i < site.Arms.Count; i++)
            {
                SemaforoAproximacao a = BuildApproach(go.transform, site.Arms[i], site.Groups[i], camera, index, i, on, off, corners);
                approaches.Add(a);
            }

            controller.Configure(config, camera, approaches.ToArray(), on, off);
            _semaforoCount++;
            _semaforoCameraCount += camera ? 1 : 0;

            // Pontos de vista para as capturas: o braço mais longo do cruzamento, 28 m antes da linha.
            int pick = 0;

            for (int i = 1; i < approaches.Count; i++)
            {
                if (ArmTotalLength(site.Arms[i]) > ArmTotalLength(site.Arms[pick]))
                {
                    pick = i;
                }
            }

            SemaforoAproximacao view = approaches[pick];
            Vector3 viewAt = view.Linha - view.Direcao * 28f + view.Direita * (site.Arms[pick].Road.Oneway ? 0f : view.LarguraDireita * 0.5f);
            float yaw = Mathf.Atan2(view.Direcao.x, view.Direcao.z) * Mathf.Rad2Deg;

            if (!site.MidBlock && !SemaforoViews.Exists(v => v.Name == "semaforo"))
            {
                SemaforoViews.Add(new ViewPoint("semaforo", new Vector3(viewAt.x, 0.1f, viewAt.z), yaw));
            }

            if (camera && !SemaforoViews.Exists(v => v.Name == "fotossensor"))
            {
                SemaforoViews.Add(new ViewPoint("fotossensor", new Vector3(viewAt.x, 0.1f, viewAt.z), yaw));
            }
        }

        private static SemaforoAproximacao BuildApproach(Transform parent, SignalArm arm, int group, bool camera, int siteIndex, int armIndex, Material[] on, Material[] off, List<Vector2> corners)
        {
            float lineDistance = Mathf.Min(arm.StopDistance + StopLineBeyondStopDistance, ArmTotalLength(arm) - 1f);
            ArmPoint(arm, lineDistance, out Vector2 line, out Vector2 armDir, out int lineSegment, out _);
            Vector2 travel = -armDir;
            Vector2 right = Geo.Right(travel);
            float extR = arm.Road.ExtToward(lineSegment, right);
            float extL = arm.Road.ExtToward(lineSegment, -right);
            bool oneway = arm.Road.Oneway;
            float leftReach = oneway ? extL : 0.3f;

            // Linha de retenção pintada na pista de chegada.
            Vector2 p0 = line - right * leftReach;
            Vector2 p1 = line + right * (extR - 0.15f);
            Quad2D(GroundBatch(Geo.V3(line)), CityStyle.PaintWhite(), p0, p1, 0.4f, PaintHeight + 0.003f);

            var a = new SemaforoAproximacao
            {
                Grupo = group,
                Linha = Geo.V3(line),
                Direcao = Geo.V3(travel),
                LarguraEsquerda = leftReach,
                LarguraDireita = extR,
            };

            // Poste à direita de quem chega, logo depois da faixa de pedestre.
            ArmPoint(arm, Mathf.Min(arm.StopDistance, lineDistance - 1f), out Vector2 poleOnAxis, out _, out _, out _);
            Vector2 corner = poleOnAxis + right * (extR + 0.7f);
            corner = PushToSidewalk(corner, right);
            corners.Add(corner);
            float y = SidewalkHeightAt(corner);
            Vector3 foot = Geo.V3(corner, y);
            Vector3 facing = Geo.V3(armDir);
            Vector3 toRoad = Geo.V3(-right);
            MeshBatch b = BuildBatch(foot);
            Material pole = PbrMaterials.Flat("SemaforoPoste", new Color(0.2f, 0.21f, 0.23f), 0.45f, 0.5f);

            // Base listrada de amarelo e preto (1,1 m) e o fuste.
            for (int i = 0; i < 5; i++)
            {
                b.Frustum(i % 2 == 0 ? CityStyle.PaintYellow() : CityStyle.PaintBlack(), foot + Vector3.up * (i * 0.22f), 0.112f, 0.112f, 0.22f, 10, false);
            }

            b.Frustum(pole, foot + Vector3.up * 1.1f, 0.085f, 0.07f, 4.1f, 10, true);
            SolidCapsule(_signalRoot, "Semaforo", foot, 0.2f, 3f);

            // Braço projetado sobre a pista, com o foco pendurado na ponta.
            float targetLateral = oneway ? (extR - extL) * 0.5f : extR * 0.5f;
            float armLength = Mathf.Clamp(extR + 0.7f - targetLateral, 2.2f, 6.5f);
            Vector3 armStart = foot + Vector3.up * 5.0f;
            Vector3 armEnd = armStart + toRoad * armLength;
            b.Wire(pole, armStart, armEnd, 0.065f);
            b.Wire(pole, armStart + Vector3.up * 0.1f, armEnd, 0.04f);
            b.Wire(pole, armEnd, armEnd + Vector3.down * 0.35f, 0.04f);

            var reds = new List<Renderer>();
            var ambers = new List<Renderer>();
            var greens = new List<Renderer>();
            var lists = new[] { reds, ambers, greens };
            string name = $"Foco_{siteIndex}_{armIndex}";
            SemaforoHead(b, parent, name + "_Braco", armEnd + Vector3.down * 1.43f, facing, off, lists, true);
            SemaforoHead(b, parent, name + "_Coluna", foot + Vector3.up * 2.55f + toRoad * 0.3f, facing, off, lists, true);
            _semaforoHeadCount += 2;

            // Foco de pedestre na coluna, virado para a calçada do outro lado.
            var pedRed = new List<Renderer>();
            var pedGreen = new List<Renderer>();
            SemaforoHead(b, parent, name + "_Pedestre", foot + Vector3.up * 1.4f + toRoad * 0.22f, toRoad, off, new[] { pedRed, pedGreen }, false);

            a.Vermelhos = reds.ToArray();
            a.Amarelos = ambers.ToArray();
            a.Verdes = greens.ToArray();
            a.PedestreVermelho = pedRed.ToArray();
            a.PedestreVerde = pedGreen.ToArray();

            if (camera)
            {
                BuildFotossensor(b, parent, a, arm, lineDistance, armStart, toRoad, facing, travel, right, extR, name);
            }

            return a;
        }

        /// <summary>
        /// Foco de semáforo. Veículos: 3 lentes (vermelha em cima) numa caixa preta com placa de contraste de borda amarela e quebra-luz
        /// em cada lente. Pedestre: 2 lentes, sem quebra-luz. Quem lê "facing" é o motorista (a frente do foco olha para ele).
        /// </summary>
        private static void SemaforoHead(MeshBatch b, Transform parent, string name, Vector3 bottomCenter, Vector3 facing, Material[] lensMaterials, List<Renderer>[] lists, bool vehicle)
        {
            Material black = CityStyle.IronBlack();
            Vector3 up = Vector3.up;
            Vector3 r = Vector3.Cross(facing, up);
            float width = vehicle ? 0.38f : 0.3f;
            float height = vehicle ? 1.08f : 0.62f;
            float depth = vehicle ? 0.3f : 0.2f;
            float lensRadius = vehicle ? 0.115f : 0.095f;
            float[] ys = vehicle ? new[] { 0.9f, 0.55f, 0.2f } : new[] { 0.45f, 0.16f };

            b.Box(black, bottomCenter - r * (width * 0.5f) + facing * (depth * 0.5f), facing, width, height, depth);

            if (vehicle)
            {
                // Placa de contraste: preta, com borda amarela refletiva.
                b.Box(CityStyle.PaintYellow(), bottomCenter - r * 0.33f - up * 0.14f + facing * 0.005f, facing, 0.66f, 1.36f, 0.015f);
                b.Box(black, bottomCenter - r * 0.3f - up * 0.11f + facing * 0.03f, facing, 0.6f, 1.3f, 0.025f);
            }

            // Os materiais de lente começam apagados; o controlador troca na primeira atualização.
            Material[] lensOff = lensMaterials;

            for (int i = 0; i < ys.Length; i++)
            {
                Vector3 center = bottomCenter + up * ys[i] + facing * (depth * 0.5f + 0.012f);
                Material material = vehicle ? lensOff[i] : lensOff[i == 0 ? 0 : 2];
                var lens = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                lens.name = $"{name}_Lente{i}";
                lens.transform.SetParent(parent, false);
                lens.transform.SetPositionAndRotation(center, Quaternion.LookRotation(facing, up));
                lens.transform.localScale = new Vector3(lensRadius * 2f, lensRadius * 2f, 0.07f);
                Object.DestroyImmediate(lens.GetComponent<Collider>());
                Renderer renderer = lens.GetComponent<Renderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                lists[i].Add(renderer);

                if (vehicle)
                {
                    // Quebra-luz em túnel: tampa em cima e as duas faces laterais.
                    float f0 = depth * 0.5f + 0.2f;
                    b.Box(black, bottomCenter + up * (ys[i] + 0.125f) - r * 0.16f + facing * f0, facing, 0.32f, 0.018f, 0.22f);
                    b.Box(black, bottomCenter + up * (ys[i] - 0.125f) - r * 0.16f + facing * f0, facing, 0.018f, 0.25f, 0.2f);
                    b.Box(black, bottomCenter + up * (ys[i] - 0.125f) + r * 0.142f + facing * f0, facing, 0.018f, 0.25f, 0.2f);
                }
            }
        }

        /// <summary>Fotossensor: caixa da câmera no braço sobre a pista, flash (desligado) e a placa 35 m antes da linha.</summary>
        private static void BuildFotossensor(MeshBatch b, Transform parent, SemaforoAproximacao a, SignalArm arm, float lineDistance, Vector3 armStart, Vector3 toRoad, Vector3 facing, Vector2 travel, Vector2 right, float extR, string name)
        {
            Material dark = CityMaterials.Lit("Fisc_Caixa", new Color(0.07f, 0.07f, 0.08f), 0.35f);
            Material lens = CityMaterials.Lit("Fisc_Lente", new Color(0.02f, 0.03f, 0.06f), 0.9f);
            Material flashMaterial = CityMaterials.Unlit("Fisc_Flash", new Color(1f, 1f, 0.92f));
            Vector3 boxAt = armStart + toRoad * 1.3f + Vector3.down * 0.42f;
            b.Wire(dark, armStart + toRoad * 1.3f, boxAt + Vector3.up * 0.15f, 0.04f);
            b.Box(dark, boxAt - Cross(facing) * 0.22f + facing * 0.4f - Vector3.up * 0.15f, facing, 0.44f, 0.3f, 0.7f);

            GameObject glass = Prim(PrimitiveType.Cylinder, parent, name + "_LenteCamera", boxAt + facing * 0.42f, Quaternion.LookRotation(facing, Vector3.up) * Quaternion.Euler(90f, 0f, 0f), new Vector3(0.2f, 0.02f, 0.2f), lens);
            glass.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            GameObject flash = Prim(PrimitiveType.Sphere, parent, name + "_Flash", boxAt + facing * 0.45f + Cross(facing) * 0.12f + Vector3.up * 0.1f, Quaternion.identity, Vector3.one * 0.14f, flashMaterial);
            flash.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            var lightObject = new GameObject(name + "_LuzFlash");
            lightObject.transform.SetParent(parent, false);
            lightObject.transform.position = boxAt + facing * 2.2f;
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Point;
            light.range = 30f;
            light.intensity = 40f;
            light.color = new Color(1f, 0.98f, 0.9f);
            light.shadows = LightShadows.None;
            light.enabled = false;
            flash.GetComponent<Renderer>().enabled = false;
            a.Flash = light;
            a.FlashLente = flash.GetComponent<Renderer>();

            // Placa "FOTOSSENSOR / AVANÇO DE SINAL" 35 m antes da linha, no acostamento da direita.
            float back = Mathf.Min(35f, ArmTotalLength(arm) - lineDistance - 1f);

            if (back < 12f)
            {
                return;
            }

            ArmPoint(arm, lineDistance + back, out Vector2 signOnAxis, out _, out _, out _);
            Vector2 signAt = signOnAxis + right * (extR + 0.7f);
            signAt = PushToSidewalk(signAt, right);
            Vector3 foot = Geo.V3(signAt, SidewalkHeightAt(signAt));
            Material pole = CityMaterials.Lit("Fisc_Poste", new Color(0.4f, 0.41f, 0.43f), 0.2f);
            Material board = CityMaterials.Lit("Fisc_Placa", new Color(0.95f, 0.95f, 0.92f), 0.15f);
            Material red = CityMaterials.Lit("Fisc_Vermelho", new Color(0.75f, 0.08f, 0.08f), 0.15f);
            Vector3 dir = Geo.V3(travel);
            Quaternion rotation = Quaternion.LookRotation(dir, Vector3.up);
            Prim(PrimitiveType.Cylinder, parent, name + "_PostePlaca", foot + Vector3.up * 1.5f, Quaternion.identity, new Vector3(0.12f, 1.5f, 0.12f), pole);
            Vector3 center = foot + Vector3.up * 3.0f;
            Prim(PrimitiveType.Cube, parent, name + "_Placa", center, rotation, new Vector3(1.7f, 1.3f, 0.06f), red);
            Prim(PrimitiveType.Cube, parent, name + "_PlacaMiolo", center - dir * 0.035f, rotation, new Vector3(1.58f, 1.18f, 0.03f), board);
            Vector3 front = center - dir * 0.06f;
            Label(parent, name + "_TextoTitulo", front + Vector3.up * 0.3f, dir, "FOTOSSENSOR", new Color(0.08f, 0.08f, 0.1f), new Vector2(1.5f, 0.5f), 3f);
            Label(parent, name + "_TextoSub", front - Vector3.up * 0.28f, dir, "AVANÇO DE SINAL", new Color(0.75f, 0.08f, 0.08f), new Vector2(1.5f, 0.5f), 2.4f);
            SolidCapsule(_signalRoot, "PostePlaca", foot, 0.1f, 3f);
        }

        /// <summary>Direita de quem olha para "facing".</summary>
        private static Vector3 Cross(Vector3 facing) => Vector3.Cross(facing, Vector3.up);
    }
}

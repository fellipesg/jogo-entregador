using System.Collections.Generic;
using UnityEngine;

namespace Entregador.Editor
{
    /// <summary>Chão, asfalto, faixas, faixas de pedestre, lombadas e calçadas (visual em malha fundida; colliders simples).</summary>
    public static partial class CityBuilder
    {
        /// <summary>Altura do meio-fio das calçadas, em metros. A moto sobe por uma rampa de colisão.</summary>
        private const float CurbHeight = 0.15f;

        /// <summary>Quanto a rampa avança na calçada (m). 0,15 m em 0,8 m dá cerca de 10,6 graus.</summary>
        private const float CurbRun = 0.8f;

        /// <summary>Distância do alinhamento da frente dos prédios até a esquina do quarteirão.</summary>
        private const float FrontInset = SidewalkInset;

        /// <summary>Fundo máximo de um prédio na fileira (m); o miolo do quarteirão fica vazio.</summary>
        private const float RowDepth = 15f;

        /// <summary>Renderers com materiais de chão que trocam para a versão molhada na chuva (ver WeatherLook).</summary>
        private static readonly List<Renderer> GroundRenderers = new List<Renderer>();

        /// <summary>Posições das lombadas (carros parados e mobiliário as evitam).</summary>
        private static readonly List<Vector3> BumpPositions = new List<Vector3>();

        private static void Floor(MeshBatch b, Material material, float x0, float z0, float x1, float z1, float y)
        {
            b.Rect(material, new Vector3(x0, y, z0), Vector3.right, Vector3.forward, x1 - x0, z1 - z0);
        }

        // ---------- Chão ----------

        private static void BuildGround(Transform world, Materials m)
        {
            float size = _span + 400f;

            // O collider do chão fica no y = 0; o visual (grama) um pouco abaixo, para as ruas aparecerem por cima sem brigar de profundidade.
            var ground = new GameObject("Chao");
            ground.transform.SetParent(world, false);
            ground.transform.position = new Vector3(0f, -0.5f, 0f);
            ground.AddComponent<BoxCollider>().size = new Vector3(size, 1f, size);

            var visual = new MeshBatch();
            Floor(visual, CityStyle.Grass(), -size * 0.5f, -size * 0.5f, size * 0.5f, size * 0.5f, -0.04f);
            visual.Build(world, "ChaoVisual", false);

            // Paredes invisíveis na borda do mapa.
            float half = _span * 0.5f + 20f;
            Wall(world, "LimiteNorte", new Vector3(0f, 2.5f, half), new Vector3(2f * half, 5f, 2f));
            Wall(world, "LimiteSul", new Vector3(0f, 2.5f, -half), new Vector3(2f * half, 5f, 2f));
            Wall(world, "LimiteLeste", new Vector3(half, 2.5f, 0f), new Vector3(2f, 5f, 2f * half));
            Wall(world, "LimiteOeste", new Vector3(-half, 2.5f, 0f), new Vector3(2f, 5f, 2f * half));
        }

        private static void Wall(Transform parent, string name, Vector3 center, Vector3 size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = center;
            go.AddComponent<BoxCollider>().size = size;
        }

        // ---------- Ruas ----------

        private static void Dashes(MeshBatch b, Material material, bool alongZ, float fixedCoord, float from, float to, float width, float dash, float gap, float y)
        {
            for (float s = from; s + 0.5f < to; s += dash + gap)
            {
                float e = Mathf.Min(s + dash, to);

                if (alongZ)
                {
                    Floor(b, material, fixedCoord - width * 0.5f, s, fixedCoord + width * 0.5f, e, y);
                }
                else
                {
                    Floor(b, material, s, fixedCoord - width * 0.5f, e, fixedCoord + width * 0.5f, y);
                }
            }
        }

        private static void BuildRoads(Transform parent, Materials m)
        {
            Material asphalt = CityStyle.Asphalt();
            var surface = new MeshBatch();
            float zMin = _cz[0] - _hwz[0];
            float zMax = _cz[StreetCount - 1] + _hwz[StreetCount - 1];

            // Ruas verticais inteiras; ruas horizontais só entre elas (os cruzamentos já estão cobertos).
            for (int i = 0; i < StreetCount; i++)
            {
                Floor(surface, asphalt, _cx[i] - _hwx[i], zMin, _cx[i] + _hwx[i], zMax, 0f);
            }

            for (int j = 0; j < StreetCount; j++)
            {
                for (int i = 0; i < BlockCount; i++)
                {
                    Floor(surface, asphalt, BlockX0(i), _cz[j] - _hwz[j], BlockX1(i), _cz[j] + _hwz[j], 0f);
                }
            }

            // Corredor de ônibus vermelho no meio da avenida (os cruzamentos ficam livres).
            Material red = CityStyle.AsphaltRed();

            for (int j = 0; j < BlockCount; j++)
            {
                Floor(surface, red, _cx[AvenueIndex] - 2.6f, BlockZ0(j), _cx[AvenueIndex] + 2.6f, BlockZ1(j), 0.012f);
            }

            GameObject road = surface.Build(parent, "Asfalto", false);
            GroundRenderers.Add(road.GetComponent<Renderer>());

            BuildMarkings(parent);
            BuildBumps(parent);
        }

        private static void BuildMarkings(Transform parent)
        {
            var paint = new MeshBatch();
            Material white = CityStyle.PaintWhite();
            Material yellow = CityStyle.PaintYellow();
            const float y = 0.02f;

            for (int j = 0; j < BlockCount; j++)
            {
                float z0 = BlockZ0(j);
                float z1 = BlockZ1(j);

                for (int i = 0; i < StreetCount; i++)
                {
                    if (i == AvenueIndex)
                    {
                        float cx = _cx[i];
                        Dashes(paint, white, true, cx - 2.75f, z0 + 7f, z1 - 7f, 0.15f, 400f, 0f, y);
                        Dashes(paint, white, true, cx + 2.75f, z0 + 7f, z1 - 7f, 0.15f, 400f, 0f, y);
                        Dashes(paint, white, true, cx - 6f, z0 + 7f, z1 - 7f, 0.15f, 3f, 5f, y);
                        Dashes(paint, white, true, cx + 6f, z0 + 7f, z1 - 7f, 0.15f, 3f, 5f, y);
                    }
                    else
                    {
                        Dashes(paint, yellow, true, _cx[i], z0 + 7f, z1 - 7f, 0.15f, 3.5f, 4f, y);
                    }
                }
            }

            for (int j = 0; j < StreetCount; j++)
            {
                for (int i = 0; i < BlockCount; i++)
                {
                    Dashes(paint, yellow, false, _cz[j], BlockX0(i) + 7f, BlockX1(i) - 7f, 0.15f, 3.5f, 4f, y);
                }
            }

            // Faixas de pedestre: em cada braço de cada cruzamento, logo depois da rua que cruza.
            for (int i = 0; i < StreetCount; i++)
            {
                for (int j = 0; j < StreetCount; j++)
                {
                    foreach (int s in new[] { -1, 1 })
                    {
                        if (j + s >= 0 && j + s < StreetCount)
                        {
                            float zc = _cz[j] + s * (_hwz[j] + 2.2f);

                            for (float x = _cx[i] - _hwx[i] + 1.2f; x < _cx[i] + _hwx[i] - 1f; x += 1.1f)
                            {
                                Floor(paint, white, x, zc - 1.5f, x + 0.55f, zc + 1.5f, y);
                            }
                        }

                        if (i + s >= 0 && i + s < StreetCount)
                        {
                            float xc = _cx[i] + s * (_hwx[i] + 2.2f);

                            for (float z = _cz[j] - _hwz[j] + 1.2f; z < _cz[j] + _hwz[j] - 1f; z += 1.1f)
                            {
                                Floor(paint, white, xc - 1.5f, z, xc + 1.5f, z + 0.55f, y);
                            }
                        }
                    }
                }
            }

            paint.Build(parent, "Faixas", false);
        }

        /// <summary>Lombadas amarelas e pretas em alguns trechos de rua residencial (só visuais: a moto não é afetada).</summary>
        private static void BuildBumps(Transform parent)
        {
            var b = new MeshBatch();
            var random = new System.Random(31);
            Material asphalt = CityStyle.Asphalt();
            Material yellow = CityStyle.PaintYellow();
            Material black = CityStyle.PaintBlack();
            int made = 0;
            int guard = 0;
            BumpPositions.Clear();

            while (made < 10 && guard++ < 200)
            {
                int i = random.Next(StreetCount);
                int j = random.Next(BlockCount);

                if (i == AvenueIndex)
                {
                    continue;
                }

                float z = Mathf.Lerp(_cz[j], _cz[j + 1], 0.3f + 0.4f * (random.Next(2)));
                float hw = _hwx[i];
                b.AxisBox(asphalt, new Vector3(_cx[i], 0.03f, z), new Vector3(2f * hw, 0.06f, 1.4f));
                int stripes = Mathf.RoundToInt(2f * hw / 0.5f);

                for (int k = 0; k < stripes; k++)
                {
                    float x0 = _cx[i] - hw + k * 0.5f;
                    Floor(b, k % 2 == 0 ? yellow : black, x0, z - 0.6f, x0 + 0.5f, z + 0.6f, 0.062f);
                }

                BumpPositions.Add(new Vector3(_cx[i], 0f, z));
                made++;
            }

            b.Build(parent, "Lombadas", false);
        }

        // ---------- Calçadas ----------

        /// <summary>
        /// Visual da calçada/praça: piso com textura, guia (meio-fio) de concreto em volta (topo e face) e a laje.
        /// Os colliders (laje plana e rampas) saem em <see cref="PavementColliders"/>.
        /// </summary>
        private static void PavementVisual(MeshBatch b, float x0, float x1, float z0, float z1, float height, Material top)
        {
            const float curb = 0.15f;
            Material concrete = CityStyle.Curb();
            Floor(b, top, x0 + curb, z0 + curb, x1 - curb, z1 - curb, height);
            Floor(b, concrete, x0, z0, x1, z0 + curb, height);
            Floor(b, concrete, x0, z1 - curb, x1, z1, height);
            Floor(b, concrete, x0, z0 + curb, x0 + curb, z1 - curb, height);
            Floor(b, concrete, x1 - curb, z0 + curb, x1, z1 - curb, height);
            Vector3 up = Vector3.up;
            b.Rect(concrete, new Vector3(x0, 0f, z0), Vector3.right, up, x1 - x0, height);
            b.Rect(concrete, new Vector3(x1, 0f, z1), Vector3.left, up, x1 - x0, height);
            b.Rect(concrete, new Vector3(x0, 0f, z1), Vector3.back, up, z1 - z0, height);
            b.Rect(concrete, new Vector3(x1, 0f, z0), Vector3.forward, up, z1 - z0, height);
        }

        /// <summary>Laje plana com collider e quatro rampas de meio-fio: a moto sobe a calçada em vez de bater numa parede.</summary>
        private static void PavementColliders(Transform parent, string name, float x0, float x1, float z0, float z1, float height)
        {
            float cx = (x0 + x1) * 0.5f;
            float cz = (z0 + z1) * 0.5f;
            Transform holder = Group(parent, name);

            var slab = new GameObject("Laje");
            slab.transform.SetParent(holder, false);
            slab.transform.position = new Vector3(cx, height * 0.5f, cz);
            slab.AddComponent<BoxCollider>().size = new Vector3(x1 - x0 - 2f * CurbRun, height, z1 - z0 - 2f * CurbRun);

            Ramp(holder, new Vector3(x0, 0f, cz), Vector3.right, z1 - z0, height);
            Ramp(holder, new Vector3(x1, 0f, cz), Vector3.left, z1 - z0, height);
            Ramp(holder, new Vector3(cx, 0f, z0), Vector3.forward, x1 - x0, height);
            Ramp(holder, new Vector3(cx, 0f, z1), Vector3.back, x1 - x0, height);
        }

        private static void Ramp(Transform parent, Vector3 edgePoint, Vector3 inward, float edgeLength, float height)
        {
            Vector3 climb = (inward * CurbRun + Vector3.up * height).normalized;
            Quaternion rotation = Quaternion.LookRotation(climb, Vector3.up);
            float slope = Mathf.Sqrt(CurbRun * CurbRun + height * height);
            const float thickness = 0.1f;

            var go = new GameObject("RampaMeioFio");
            go.transform.SetParent(parent, false);
            Vector3 surfaceMiddle = edgePoint + inward * (CurbRun * 0.5f) + Vector3.up * (height * 0.5f);
            go.transform.SetPositionAndRotation(surfaceMiddle - (rotation * Vector3.up) * (thickness * 0.5f), rotation);
            go.AddComponent<BoxCollider>().size = new Vector3(edgeLength, thickness, slope);
        }
    }
}

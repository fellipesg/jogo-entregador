using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Entregador.Editor
{
    /// <summary>
    /// Biblioteca dos carros reais (Fiat Uno 2001 e Chevrolet Onix, Sketchfab, CC-BY 4.0; ver README). Importa os FBX, normaliza
    /// escala (Uno 3,69 m, Onix 4,16 m), põe a origem no centro do chão com a frente em +Z, separa as quatro rodas (pivô no
    /// próprio eixo, para girar), mantém só a casca externa do Onix (a malha original tem interior, motor e triângulos virados),
    /// decima em dois LODs (perto até 15 mil triângulos com rodas, longe até 3 mil), tira emblemas e coloca placas fictícias no
    /// estilo Mercosul. A pintura vem de materiais por cor (Branco, Prata...), poucos e compartilhados.
    /// Gera tudo em Assets/_Jogo/Carros (prefabs e malhas) e Assets/_Jogo/Materials/Carros. Menu: Jogo/Gerar carros reais.
    /// </summary>
    public static class CarrosReais
    {
        public enum Modelo
        {
            Uno,
            Onix,
        }

        /// <summary>Categorias de material; cada uma vira uma submalha (ordem fixa).</summary>
        private enum Cat
        {
            Tinta,
            Vidro,
            Plastico,
            Cromo,
            Farol,
            Lanterna,
            Pisca,
            Pneu,
            Aro,
            Atlas,
            Roda,
        }

        public const string Pasta = "Assets/_Jogo/Carros";
        public const string MaterialPasta = "Assets/_Jogo/Materials/Carros";
        public const float ComprimentoUno = 3.69f;
        public const float ComprimentoOnix = 4.16f;
        public const int PlacaCount = 8;

        private static readonly string[] Placas = { "PLM7C45", "RDA1B63", "GHE9K27", "KSX3D58", "CBF8A40", "HTR5G91", "MAP6L34", "DEL2S76" };

        private static readonly Dictionary<Modelo, GameObject> PrefabCache = new Dictionary<Modelo, GameObject>();
        private static readonly Dictionary<Modelo, Bounds> BoundsCache = new Dictionary<Modelo, Bounds>();

        public static string NomeDoPrefab(Modelo modelo) => $"{Pasta}/{modelo}.prefab";

        [MenuItem("Jogo/Gerar carros reais")]
        public static void GerarMenu()
        {
            Gerar(Modelo.Uno);
            Gerar(Modelo.Onix);
        }

        /// <summary>Gera os prefabs que ainda não existem (a geração é lenta por causa da decimação).</summary>
        public static void Garantir()
        {
            foreach (Modelo modelo in new[] { Modelo.Uno, Modelo.Onix })
            {
                if (AssetDatabase.LoadAssetAtPath<GameObject>(NomeDoPrefab(modelo)) == null || System.Environment.GetEnvironmentVariable("JOGO_REGERAR_CARROS") == "1")
                {
                    Gerar(modelo);
                }
            }
        }

        public static void GerarDaLinhaDeComando()
        {
            GerarMenu();
        }

        public static GameObject Prefab(Modelo modelo)
        {
            if (!PrefabCache.TryGetValue(modelo, out GameObject prefab) || prefab == null)
            {
                prefab = AssetDatabase.LoadAssetAtPath<GameObject>(NomeDoPrefab(modelo));
                PrefabCache[modelo] = prefab;
            }

            return prefab;
        }

        /// <summary>Caixa do carro (LOD 0, espaço local da raiz: origem no centro do chão, frente em +Z).</summary>
        public static Bounds Limites(Modelo modelo)
        {
            if (BoundsCache.TryGetValue(modelo, out Bounds cached))
            {
                return cached;
            }

            GameObject temp = (GameObject)PrefabUtility.InstantiatePrefab(Prefab(modelo));
            temp.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            Renderer[] renderers = temp.transform.Find("LOD0").GetComponentsInChildren<Renderer>();
            Bounds b = renderers[0].bounds;

            foreach (Renderer r in renderers)
            {
                if (r.name != "Placas")
                {
                    b.Encapsulate(r.bounds);
                }
            }

            Object.DestroyImmediate(temp);
            BoundsCache[modelo] = b;

            return b;
        }

        /// <summary>Instancia um carro (prefab ligado), pintado, com a placa de índice plate e, se pedido, um BoxCollider do tamanho medido.</summary>
        public static GameObject Instanciar(Transform parent, Modelo modelo, Vector3 position, float yaw, int paint, int plate, bool collider)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(Prefab(modelo), parent);
            go.name = modelo.ToString();
            go.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
            Pintar(go, modelo, paint);

            Transform plates = go.transform.Find("LOD0/Placas");

            if (plates != null)
            {
                plates.GetComponent<MeshFilter>().sharedMesh = PlateMesh(modelo, plate);
            }

            if (collider)
            {
                Bounds b = Limites(modelo);
                var box = go.AddComponent<BoxCollider>();
                box.center = b.center;
                box.size = b.size;
            }

            return go;
        }

        /// <summary>Troca os materiais de pintura (os "Tinta"/"Atlas") do carro pela cor de índice paint.</summary>
        public static void Pintar(GameObject car, Modelo modelo, int paint)
        {
            Material tinta = MaterialDePintura(modelo, paint);

            foreach (Renderer renderer in car.GetComponentsInChildren<Renderer>(true))
            {
                Material[] materials = renderer.sharedMaterials;
                bool changed = false;

                for (int i = 0; i < materials.Length; i++)
                {
                    if (materials[i] != null && materials[i].name.Contains("_Pintura_"))
                    {
                        materials[i] = tinta;
                        changed = true;
                    }
                }

                if (changed)
                {
                    renderer.sharedMaterials = materials;
                }
            }
        }

        private static Mesh PlateMesh(Modelo modelo, int plate)
        {
            plate = ((plate % PlacaCount) + PlacaCount) % PlacaCount;

            string wanted = $"{modelo}_Placa{plate}";

            foreach (Object o in AssetDatabase.LoadAllAssetsAtPath($"{Pasta}/{modelo}_Malhas.asset"))
            {
                if (o is Mesh m && m.name == wanted)
                {
                    return m;
                }
            }

            return null;
        }

        // ---------- Geração ----------

        public static void Gerar(Modelo modelo)
        {
            Directory.CreateDirectory(Pasta);
            Directory.CreateDirectory(MaterialPasta);
            PrepararTexturas();

            string fbx = modelo == Modelo.Uno ? "Assets/ThirdParty/Sketchfab/FiatUno/FiatUno.fbx" : "Assets/ThirdParty/Sketchfab/ChevroletOnix/ChevroletOnix.fbx";
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
            bool onix = modelo == Modelo.Onix;

            // 1. Junta tudo em espaço do FBX, separando as rodas.
            var body = new MeshData();
            var wheelParts = new List<MeshData>();
            Matrix4x4 toRoot = source.transform.worldToLocalMatrix;

            foreach (MeshRenderer renderer in source.GetComponentsInChildren<MeshRenderer>())
            {
                Mesh mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
                MeshData data = Extract(mesh, toRoot * renderer.transform.localToWorldMatrix, renderer.sharedMaterials, onix);
                bool isWheel = onix ? renderer.name.StartsWith("wheel") : renderer.name.StartsWith("Wheel");

                if (isWheel)
                {
                    if (onix)
                    {
                        wheelParts.Add(data);
                    }
                    else
                    {
                        SplitPair(data, wheelParts);
                    }
                }
                else if (!IsEmblem(onix, renderer.name))
                {
                    body.Append(data);
                }
            }

            // 2. Normaliza: frente em +Z, escala pelo comprimento, origem no centro do chão.
            float length = onix ? ComprimentoOnix : ComprimentoUno;
            Bounds all = BoundsOf(body);

            foreach (MeshData w in wheelParts)
            {
                all.Encapsulate(BoundsOf(w));
            }

            float scale = length / all.size.z;
            Vector3 center = new Vector3(all.center.x, all.min.y, all.center.z);
            Vector3 squash = new Vector3(onix ? 0.92f : 0.85f, 1f, 1f);
            Matrix4x4 normalize = Matrix4x4.Scale(Vector3.Scale(Vector3.one * scale, squash)) * Matrix4x4.Rotate(Quaternion.Euler(0f, onix ? 180f : 0f, 0f)) * Matrix4x4.Translate(-center);
            Transform(body, normalize);

            foreach (MeshData w in wheelParts)
            {
                Transform(w, normalize);
            }

            Debug.Log($"[Carros] {modelo}: bruto {body.Triangles + SumTriangles(wheelParts)} triângulos, caixa {all.size * scale}");

            // 3. Onix: remalha pelo volume (a malha original é feita de camadas sobrepostas, com buracos e triângulos virados).
            if (onix)
            {
                body = Shell(body, 0.03f);
                Debug.Log($"[Carros] {modelo}: volume remalhado com {body.Triangles} triângulos");
            }

            // 4. LODs.
            const int wheelNear = 800;
            var wheelsNear = new List<MeshData>();
            var wheelCenters = new List<Vector3>();

            foreach (MeshData w in wheelParts)
            {
                Bounds wb = BoundsOf(w);
                MeshData near = MeshDecimator.Decimate(w, wheelNear, 10f);
                wheelCenters.Add(wb.center);
                Translate(near, -wb.center);
                wheelsNear.Add(near);
            }

            int bodyNearTarget = 15000 - wheelNear * wheelsNear.Count;
            MeshData bodyNear = MeshDecimator.Decimate(body, bodyNearTarget);

            if (onix)
            {
                bodyNear = CorrigirOnix(bodyNear, true);
            }

            Debug.Log($"[Carros] {modelo}: LOD0 corpo {bodyNear.Triangles} + rodas {SumTriangles(wheelsNear)}");

            // LOD 1: corpo LOD0 + rodas simples juntos.
            var far1 = new MeshData();
            far1.Append(bodyNear);

            for (int i = 0; i < wheelParts.Count; i++)
            {
                MeshData simple = MeshDecimator.Decimate(wheelParts[i], 110, 10f);
                far1.Append(simple);
            }

            MeshData bodyFar = MeshDecimator.Decimate(far1, 2900);
            Debug.Log($"[Carros] {modelo}: LOD1 {bodyFar.Triangles}");

            // 5. Placas: perto da ponta traseira e dianteira da carroceria.
            Bounds nb = BoundsOf(bodyNear);
            float plateY = onix ? 0.62f : 0.6f;
            float rearZ = ExtremeZ(bodyNear, false, plateY);
            float frontZ = ExtremeZ(bodyNear, true, plateY - 0.08f);

            // 6. Grava malhas, materiais e prefab.
            string meshPath = $"{Pasta}/{modelo}_Malhas.asset";
            AssetDatabase.DeleteAsset(meshPath);
            Mesh near0 = bodyNear.Weld().ToMesh(modelo + "_Corpo_LOD0");
            Mesh far0 = bodyFar.Weld().ToMesh(modelo + "_LOD1");
            AssetDatabase.CreateAsset(near0, meshPath);
            AssetDatabase.AddObjectToAsset(far0, meshPath);
            var wheelMeshes = new List<Mesh>();

            for (int i = 0; i < wheelsNear.Count; i++)
            {
                Mesh m = wheelsNear[i].Weld().ToMesh($"{modelo}_Roda{i}");
                AssetDatabase.AddObjectToAsset(m, meshPath);
                wheelMeshes.Add(m);
            }

            var plateMeshes = new Mesh[PlacaCount];

            for (int p = 0; p < PlacaCount; p++)
            {
                plateMeshes[p] = BuildPlateMesh(p, rearZ, frontZ, plateY, modelo);
                plateMeshes[p].name = $"{modelo}_Placa{p}";
                AssetDatabase.AddObjectToAsset(plateMeshes[p], meshPath);
            }

            Material[] nearMaterials = MaterialsFor(modelo, bodyNear, null);
            Material[] farMaterials = MaterialsFor(modelo, bodyFar, null);

            var root = new GameObject(modelo.ToString());
            var lod0 = new GameObject("LOD0");
            var lod1 = new GameObject("LOD1");
            lod0.transform.SetParent(root.transform, false);
            lod1.transform.SetParent(root.transform, false);

            var renderers0 = new List<Renderer>();
            renderers0.Add(AddPart(lod0.transform, "Corpo", near0, nearMaterials, Vector3.zero));
            string[] wheelNames = { "RodaA", "RodaB", "RodaC", "RodaD" };

            for (int i = 0; i < wheelMeshes.Count; i++)
            {
                renderers0.Add(AddPart(lod0.transform, wheelNames[i], wheelMeshes[i], MaterialsFor(modelo, wheelsNear[i], null), wheelCenters[i]));
            }

            Renderer plates = AddPart(lod0.transform, "Placas", plateMeshes[0], new[] { PlateMaterial() }, Vector3.zero);
            renderers0.Add(plates);
            Renderer far = AddPart(lod1.transform, "Corpo", far0, farMaterials, Vector3.zero);
            far.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            var group = root.AddComponent<LODGroup>();
            group.SetLODs(new[] { new LOD(0.1f, renderers0.ToArray()), new LOD(0.012f, new[] { far }) });
            group.RecalculateBounds();

            string prefabPath = NomeDoPrefab(modelo);
            AssetDatabase.DeleteAsset(prefabPath);
            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            Object.DestroyImmediate(root);
            AssetDatabase.SaveAssets();
            PrefabCache.Remove(modelo);
            BoundsCache.Remove(modelo);

            Bounds limits = Limites(modelo);
            File.AppendAllText("/private/tmp/claude-501/-Users-felipegoncalves/a275e7fa-b013-40ea-bca5-3880da79d55c/scratchpad/carros.txt", $"{modelo}: LOD0 {bodyNear.Triangles}+{SumTriangles(wheelsNear)} LOD1 {bodyFar.Triangles} caixa {limits.size}\n");
            Debug.Log($"[Carros] {modelo}: prefab gravado, caixa {limits.size}, corpo {nb.size}");
        }

        private static Renderer AddPart(Transform parent, string name, Mesh mesh, Material[] materials, Vector3 position)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = materials;

            return renderer;
        }

        // ---------- Extração e categorias ----------

        private static MeshData Extract(Mesh mesh, Matrix4x4 matrix, Material[] materials, bool onix)
        {
            var data = new MeshData();
            Vector3[] vertices = mesh.vertices;
            Vector3[] normals = mesh.normals;
            Vector2[] uvs = mesh.uv;
            bool flip = matrix.determinant < 0f;
            Matrix4x4 normalMatrix = matrix.inverse.transpose;

            for (int v = 0; v < vertices.Length; v++)
            {
                data.Pos.Add(matrix.MultiplyPoint3x4(vertices[v]));
                data.Nrm.Add(normals.Length > v ? normalMatrix.MultiplyVector(normals[v]).normalized : Vector3.up);
                data.Uv.Add(uvs.Length > v ? uvs[v] : Vector2.zero);
            }

            int cats = System.Enum.GetValues(typeof(Cat)).Length;

            for (int c = 0; c < cats; c++)
            {
                data.Subs.Add(new List<int>());
            }

            for (int s = 0; s < mesh.subMeshCount; s++)
            {
                string name = s < materials.Length && materials[s] != null ? materials[s].name : "";
                Color color = s < materials.Length && materials[s] != null && materials[s].HasProperty("_BaseColor") ? materials[s].GetColor("_BaseColor") : Color.gray;
                Cat cat = Categorize(onix, name, color);
                int[] tris = mesh.GetTriangles(s);

                for (int i = 0; i + 2 < tris.Length; i += 3)
                {
                    List<int> list = data.Subs[(int)cat];

                    if (flip)
                    {
                        list.Add(tris[i]);
                        list.Add(tris[i + 2]);
                        list.Add(tris[i + 1]);
                    }
                    else
                    {
                        list.Add(tris[i]);
                        list.Add(tris[i + 1]);
                        list.Add(tris[i + 2]);
                    }
                }
            }

            return data;
        }

        private static Cat Categorize(bool onix, string material, Color color)
        {
            if (!onix)
            {
                switch (material)
                {
                    case "Material.006": return Cat.Vidro;
                    case "Wheel": return Cat.Roda;
                    default: return Cat.Atlas;
                }
            }

            if (material.StartsWith("wheel"))
            {
                return color.r >= 0.55f || color.r < 0.1f ? Cat.Pneu : Cat.Aro;
            }

            switch (material)
            {
                case "instance_0.0":
                case "primary":
                    return Cat.Tinta;
                case "glass":
                case "instance_2.2":
                case "frontbump.2":
                    return Cat.Vidro;
                case "right front light":
                case "frontbump.4":
                    return Cat.Farol;
                case "right rear light":
                    return Cat.Lanterna;
                case "onix_90_1.3":
                case "group_6.0":
                case "group_5.0":
                    return Cat.Plastico;
                case "onix_90_1.2":
                case "frontbump.3":
                case "group_1.1":
                case "frontbump.1":
                case "onix_90_21.1":
                case "onix_90_1.0":
                    return Cat.Cromo;
                default:
                    return Cat.Plastico;
            }
        }

        /// <summary>Peças que carregam emblema ou placa no modelo original (somem).</summary>
        private static bool IsEmblem(bool onix, string meshName)
        {
            return onix && (meshName == "group_5" || meshName == "group_6");
        }

        /// <summary>O Uno tem as quatro rodas em duas malhas (um eixo cada): separa pela metade esquerda/direita.</summary>
        private static void SplitPair(MeshData pair, List<MeshData> output)
        {
            Bounds b = BoundsOf(pair);
            float cx = b.center.x;
            var left = new MeshData();
            var right = new MeshData();
            left.Pos = pair.Pos;
            right.Pos = pair.Pos;
            left.Nrm = pair.Nrm;
            right.Nrm = pair.Nrm;
            left.Uv = pair.Uv;
            right.Uv = pair.Uv;

            for (int s = 0; s < pair.Subs.Count; s++)
            {
                left.Subs.Add(new List<int>());
                right.Subs.Add(new List<int>());

                for (int i = 0; i + 2 < pair.Subs[s].Count; i += 3)
                {
                    float x = (pair.Pos[pair.Subs[s][i]].x + pair.Pos[pair.Subs[s][i + 1]].x + pair.Pos[pair.Subs[s][i + 2]].x) / 3f;
                    MeshData target = x < cx ? left : right;
                    target.Subs[s].Add(pair.Subs[s][i]);
                    target.Subs[s].Add(pair.Subs[s][i + 1]);
                    target.Subs[s].Add(pair.Subs[s][i + 2]);
                }
            }

            output.Add(Compact(left));
            output.Add(Compact(right));
        }

        /// <summary>Remove vértices não usados.</summary>
        private static MeshData Compact(MeshData m)
        {
            var result = new MeshData();
            var map = new Dictionary<int, int>();

            for (int s = 0; s < m.Subs.Count; s++)
            {
                result.Subs.Add(new List<int>());

                foreach (int i in m.Subs[s])
                {
                    if (!map.TryGetValue(i, out int n))
                    {
                        n = result.Pos.Count;
                        map[i] = n;
                        result.Pos.Add(m.Pos[i]);
                        result.Nrm.Add(m.Nrm[i]);
                        result.Uv.Add(m.Uv[i]);
                    }

                    result.Subs[s].Add(n);
                }
            }

            return result;
        }

        private static Bounds BoundsOf(MeshData m)
        {
            var used = new HashSet<int>();

            foreach (List<int> sub in m.Subs)
            {
                foreach (int i in sub)
                {
                    used.Add(i);
                }
            }

            bool first = true;
            Bounds b = new Bounds();

            foreach (int i in used)
            {
                if (first)
                {
                    b = new Bounds(m.Pos[i], Vector3.zero);
                    first = false;
                }
                else
                {
                    b.Encapsulate(m.Pos[i]);
                }
            }

            return b;
        }

        private static int SumTriangles(List<MeshData> list)
        {
            int total = 0;

            foreach (MeshData m in list)
            {
                total += m.Triangles;
            }

            return total;
        }

        private static void Transform(MeshData m, Matrix4x4 matrix)
        {
            Matrix4x4 normalMatrix = matrix.inverse.transpose;

            for (int i = 0; i < m.Pos.Count; i++)
            {
                m.Pos[i] = matrix.MultiplyPoint3x4(m.Pos[i]);
                m.Nrm[i] = normalMatrix.MultiplyVector(m.Nrm[i]).normalized;
            }
        }

        private static void Translate(MeshData m, Vector3 delta)
        {
            for (int i = 0; i < m.Pos.Count; i++)
            {
                m.Pos[i] += delta;
            }
        }

        /// <summary>Z mais extremo da carroceria perto do eixo central, na altura da placa (frente = maior z).</summary>
        private static float ExtremeZ(MeshData m, bool front, float y)
        {
            float best = front ? float.MinValue : float.MaxValue;

            foreach (List<int> sub in m.Subs)
            {
                foreach (int i in sub)
                {
                    Vector3 p = m.Pos[i];

                    if (Mathf.Abs(p.x) < 0.18f && Mathf.Abs(p.y - y) < 0.12f)
                    {
                        best = front ? Mathf.Max(best, p.z) : Mathf.Min(best, p.z);
                    }
                }
            }

            return best;
        }

        // ---------- Casca externa (voxels) ----------

        /// <summary>
        /// Descobre o que se vê de fora: rasteriza os triângulos numa grade de voxels, inunda o ar a partir do exterior e mantém só os
        /// triângulos encostados nesse ar. Orienta cada um para fora (a malha original tem triângulos virados) e recalcula as normais.
        /// </summary>
        private static MeshData Shell(MeshData body, float voxel)
        {
            Bounds b = BoundsOf(body);
            Vector3 min = b.min - Vector3.one * voxel * 3f;
            Vector3Int dim = new Vector3Int(Mathf.CeilToInt((b.size.x + voxel * 6f) / voxel), Mathf.CeilToInt((b.size.y + voxel * 6f) / voxel), Mathf.CeilToInt((b.size.z + voxel * 6f) / voxel));
            var occupied = new bool[dim.x * dim.y * dim.z];
            int Index(int x, int y, int z) => (z * dim.y + y) * dim.x + x;
            Vector3Int Cell(Vector3 p) => new Vector3Int(Mathf.FloorToInt((p.x - min.x) / voxel), Mathf.FloorToInt((p.y - min.y) / voxel), Mathf.FloorToInt((p.z - min.z) / voxel));
            bool Inside(Vector3Int c) => c.x >= 0 && c.y >= 0 && c.z >= 0 && c.x < dim.x && c.y < dim.y && c.z < dim.z;

            void Samples(Vector3 p0, Vector3 p1, Vector3 p2, System.Action<Vector3> visit)
            {
                float edge = Mathf.Max((p1 - p0).magnitude, Mathf.Max((p2 - p1).magnitude, (p0 - p2).magnitude));
                int n = Mathf.Clamp(Mathf.CeilToInt(edge / (voxel * 0.6f)), 1, 70);

                for (int i = 0; i <= n; i++)
                {
                    for (int j = 0; j <= n - i; j++)
                    {
                        visit(p0 + (p1 - p0) * (i / (float)n) + (p2 - p0) * (j / (float)n));
                    }
                }
            }

            foreach (List<int> sub in body.Subs)
            {
                for (int i = 0; i + 2 < sub.Count; i += 3)
                {
                    Samples(body.Pos[sub[i]], body.Pos[sub[i + 1]], body.Pos[sub[i + 2]], p =>
                    {
                        Vector3Int c = Cell(p);

                        if (Inside(c))
                        {
                            occupied[Index(c.x, c.y, c.z)] = true;
                        }
                    });
                }
            }

            // Chão virtual sob a carroceria (o modelo não tem assoalho: sem isso o ar entra na cabine por baixo).
            int floorY = Mathf.FloorToInt((0.17f - min.y) / voxel);

            var footprint = new List<(int x, int z)>();

            for (int z = 0; z < dim.z; z++)
            {
                for (int x = 0; x < dim.x; x++)
                {
                    for (int y = floorY + 2; y < dim.y; y++)
                    {
                        if (occupied[Index(x, y, z)])
                        {
                            footprint.Add((x, z));

                            break;
                        }
                    }
                }
            }

            foreach ((int x, int z) in footprint)
            {
                occupied[Index(x, floorY, z)] = true;
            }

            // Ar do exterior: inundação a partir do canto.
            var exterior = new bool[occupied.Length];
            var stack = new Stack<Vector3Int>();

            for (int z = 0; z < dim.z; z++)
            {
                for (int y = 0; y < dim.y; y++)
                {
                    for (int x = 0; x < dim.x; x++)
                    {
                        bool border = x == 0 || y == 0 || z == 0 || x == dim.x - 1 || y == dim.y - 1 || z == dim.z - 1;

                        if (border && !occupied[Index(x, y, z)])
                        {
                            exterior[Index(x, y, z)] = true;
                            stack.Push(new Vector3Int(x, y, z));
                        }
                    }
                }
            }

            Vector3Int[] dirs = { new Vector3Int(1, 0, 0), new Vector3Int(-1, 0, 0), new Vector3Int(0, 1, 0), new Vector3Int(0, -1, 0), new Vector3Int(0, 0, 1), new Vector3Int(0, 0, -1) };

            while (stack.Count > 0)
            {
                Vector3Int c = stack.Pop();

                foreach (Vector3Int d in dirs)
                {
                    Vector3Int n = c + d;

                    if (Inside(n) && !occupied[Index(n.x, n.y, n.z)] && !exterior[Index(n.x, n.y, n.z)])
                    {
                        exterior[Index(n.x, n.y, n.z)] = true;
                        stack.Push(n);
                    }
                }
            }

            // O exterior "engrossado" por 3 voxels (10 cm): mantém também a camada logo atrás de buracos da casca externa,
            // que o modelo original tem (cada painel é feito de camadas que se cobrem).
            var near = (bool[])exterior.Clone();

            for (int pass = 0; pass < 3; pass++)
            {
                var next = (bool[])near.Clone();

                for (int z = 1; z < dim.z - 1; z++)
                {
                    for (int y = 1; y < dim.y - 1; y++)
                    {
                        for (int x = 1; x < dim.x - 1; x++)
                        {
                            int i = Index(x, y, z);

                            if (next[i])
                            {
                                continue;
                            }

                            next[i] = near[i - 1] || near[i + 1] || near[i - dim.x] || near[i + dim.x] || near[i - dim.x * dim.y] || near[i + dim.x * dim.y];
                        }
                    }
                }

                near = next;
            }

            // Triângulos visíveis (de fora, ou da camada logo atrás de buracos), com a categoria de material.
            var tris = new List<(Vector3 a, Vector3 b, Vector3 c, int sub)>();

            for (int s = 0; s < body.Subs.Count; s++)
            {
                List<int> sub = body.Subs[s];

                for (int i = 0; i + 2 < sub.Count; i += 3)
                {
                    Vector3 p0 = body.Pos[sub[i]];
                    Vector3 p1 = body.Pos[sub[i + 1]];
                    Vector3 p2 = body.Pos[sub[i + 2]];
                    Vector3 n = Vector3.Cross(p1 - p0, p2 - p0);

                    if (n.sqrMagnitude < 1e-12f)
                    {
                        continue;
                    }

                    n.Normalize();
                    bool visible = false;
                    bool outer = false;

                    Samples(p0, p1, p2, p =>
                    {
                        if (visible)
                        {
                            return;
                        }

                        Vector3Int c = Cell(p);

                        if (!Inside(c) || near[Index(c.x, c.y, c.z)])
                        {
                            visible = true;
                        }

                        if (!outer)
                        {
                            foreach (Vector3Int d in dirs)
                            {
                                Vector3Int nb = c + d;

                                if (!Inside(nb) || exterior[Index(nb.x, nb.y, nb.z)])
                                {
                                    outer = true;

                                    break;
                                }
                            }
                        }
                    });

                    if (!visible)
                    {
                        continue;
                    }

                    // Camada de trás (só aparece por buracos da casca): vale só onde nenhuma camada de fora passa pelo voxel.
                    int category = outer ? s : s + 32;

                    tris.Add((p0, p1, p2, category));
                }
            }


            // Remalha pela superfície do volume: o modelo é feito de camadas sobrepostas, com buracos e triângulos virados, que
            // a decimação direta transforma em espinhos. O volume (ocupação fechada, preenchida por dentro) vira uma superfície
            // suave (surface nets) e cada pedaço dela ganha a categoria do material mais próximo.
            return SurfaceNets(occupied, near, dim, min, voxel, tris, Index);
        }

        /// <summary>Peso do voto de uma categoria nos filtros de maioria (detalhes finos, como lanternas e vidros, precisam de vantagem).</summary>
        private static float Weight(int cat)
        {
            switch ((Cat)cat)
            {
                case Cat.Farol:
                case Cat.Lanterna:
                case Cat.Pisca:
                    return 3.5f;
                case Cat.Vidro:
                    return 2.4f;
                default:
                    return 1f;
            }
        }

        /// <summary>
        /// Categorias do Onix por geometria (frente em +Z, medidas do perfil do carro). A remalha por voxels deixa o formato certo,
        /// mas as categorias de material saem manchadas; aqui tudo vira pintura e só vidros (para-brisa, laterais e traseiro),
        /// faróis, lanternas e as partes baixas dos para-choques e das soleiras ficam de outras cores, em regiões limpas.
        /// </summary>
        private static MeshData CorrigirOnix(MeshData m, bool completo)
        {
            var result = new MeshData { Pos = m.Pos, Nrm = m.Nrm, Uv = m.Uv, Weights = m.Weights };
            int cats = System.Enum.GetValues(typeof(Cat)).Length;

            for (int s = 0; s < cats; s++)
            {
                result.Subs.Add(new List<int>());
            }

            for (int sIndex = 0; sIndex < m.Subs.Count; sIndex++)
            {
                List<int> sub = m.Subs[sIndex];

                for (int i = 0; i + 2 < sub.Count; i += 3)
                {
                    Vector3 a = m.Pos[sub[i]];
                    Vector3 b = m.Pos[sub[i + 1]];
                    Vector3 c = m.Pos[sub[i + 2]];
                    Vector3 p = (a + b + c) / 3f;
                    Vector3 n = Vector3.Cross(b - a, c - a).normalized;
                    float ax = Mathf.Abs(p.x);
                    Cat cat = completo ? Cat.Tinta : (Cat)sIndex;

                    if (completo && p.y > 0.95f && ax < 0.64f && p.z > 0.05f && p.z < 1.18f && n.y < 0.975f && n.z > 0.1f)
                    {
                        cat = Cat.Vidro;
                    }
                    else if (completo && p.y > 0.98f && p.y < 1.33f && p.z > -1.02f && p.z < 0.62f && ax > 0.45f && Mathf.Abs(n.x) > 0.22f && n.y < 0.8f)
                    {
                        cat = Cat.Vidro;
                    }
                    else if (p.z > -1.64f && p.z < -1.0f && p.y > 1.06f && ax < 0.6f && n.y < 0.97f && n.z < 0f)
                    {
                        cat = Cat.Vidro;
                    }
                    else if (completo && p.z > 1.9f && p.y > 0.6f && p.y < 0.86f && ax > 0.42f && ax < 0.86f && n.z > 0.2f)
                    {
                        cat = Cat.Farol;
                    }
                    else if (p.z < -1.86f && p.y > 0.74f && p.y < 1.02f && ax > 0.3f && ax < 0.88f && n.z < -0.2f)
                    {
                        cat = Cat.Lanterna;
                    }
                    else if (completo && (p.y < 0.29f || (p.z > 1.95f && p.y > 0.3f && p.y < 0.56f && ax < 0.6f)))
                    {
                        cat = Cat.Plastico;
                    }

                    result.Subs[(int)cat].AddRange(new[] { sub[i], sub[i + 1], sub[i + 2] });
                }
            }

            return result;
        }

        private static int Priority(int cat)
        {
            switch ((Cat)cat)
            {
                case Cat.Farol: return 6;
                case Cat.Lanterna: return 6;
                case Cat.Pisca: return 6;
                case Cat.Vidro: return 5;
                case Cat.Cromo: return 4;
                case Cat.Plastico: return 3;
                case Cat.Tinta: return 1;
                default: return 2;
            }
        }

        private static MeshData SurfaceNets(bool[] occupied, bool[] near, Vector3Int dim, Vector3 min, float voxel, List<(Vector3 a, Vector3 b, Vector3 c, int sub)> tris, System.Func<int, int, int, int> index)
        {
            int total = occupied.Length;
            int sx = dim.x;
            int sy = dim.y;
            int sz = dim.z;

            // Categoria por voxel (o material de maior prioridade entre os triângulos que passam por ele).
            var cat = new sbyte[total];

            for (int i = 0; i < total; i++)
            {
                cat[i] = -1;
            }

            foreach (var t in tris)
            {
                float edge = Mathf.Max((t.b - t.a).magnitude, Mathf.Max((t.c - t.b).magnitude, (t.a - t.c).magnitude));
                int n = Mathf.Clamp(Mathf.CeilToInt(edge / (voxel * 0.6f)), 1, 70);

                for (int i = 0; i <= n; i++)
                {
                    for (int j = 0; j <= n - i; j++)
                    {
                        Vector3 p = t.a + (t.b - t.a) * (i / (float)n) + (t.c - t.a) * (j / (float)n);
                        int x = Mathf.FloorToInt((p.x - min.x) / voxel);
                        int y = Mathf.FloorToInt((p.y - min.y) / voxel);
                        int z = Mathf.FloorToInt((p.z - min.z) / voxel);

                        if (x < 0 || y < 0 || z < 0 || x >= sx || y >= sy || z >= sz)
                        {
                            continue;
                        }

                        int id = index(x, y, z);
                        bool inner = t.sub >= 32;
                        int tc = inner ? t.sub - 32 : t.sub;

                        if (inner)
                        {
                            if (cat[id] < 0)
                            {
                                cat[id] = (sbyte)(tc + 32);
                            }
                        }
                        else if (cat[id] < 0 || cat[id] >= 32 || Priority(tc) > Priority(cat[id]))
                        {
                            cat[id] = (sbyte)tc;
                        }
                    }
                }
            }

            // Ocupação fechada: engrossa 1 voxel (fecha frestas de até 2 voxels), inunda o exterior e preenche o resto.
            var closed = (bool[])occupied.Clone();

            for (int z = 1; z < sz - 1; z++)
            {
                for (int y = 1; y < sy - 1; y++)
                {
                    for (int x = 1; x < sx - 1; x++)
                    {
                        int i = index(x, y, z);

                        if (occupied[i])
                        {
                            closed[i - 1] = closed[i + 1] = closed[i - sx] = closed[i + sx] = closed[i - sx * sy] = closed[i + sx * sy] = true;
                        }
                    }
                }
            }

            // O chão virtual (y do piso) foi marcado em todo o plano; tira dele o que está fora da pegada do carro.
            var outside = new bool[total];
            var stack = new Stack<int>();

            for (int z = 0; z < sz; z++)
            {
                for (int y = 0; y < sy; y++)
                {
                    for (int x = 0; x < sx; x++)
                    {
                        if ((x == 0 || y == 0 || z == 0 || x == sx - 1 || y == sy - 1 || z == sz - 1) && !closed[index(x, y, z)])
                        {
                            outside[index(x, y, z)] = true;
                            stack.Push(index(x, y, z));
                        }
                    }
                }
            }

            int[] steps = { 1, -1, sx, -sx, sx * sy, -sx * sy };

            while (stack.Count > 0)
            {
                int i = stack.Pop();
                int x = i % sx;
                int y = (i / sx) % sy;
                int z = i / (sx * sy);

                for (int k = 0; k < 6; k++)
                {
                    if ((k == 0 && x == sx - 1) || (k == 1 && x == 0) || (k == 2 && y == sy - 1) || (k == 3 && y == 0) || (k == 4 && z == sz - 1) || (k == 5 && z == 0))
                    {
                        continue;
                    }

                    int j = i + steps[k];

                    if (!closed[j] && !outside[j])
                    {
                        outside[j] = true;
                        stack.Push(j);
                    }
                }
            }

            // Sólido = não é exterior; erode 1 voxel para voltar ao tamanho real.
            var solid = new bool[total];

            for (int i = 0; i < total; i++)
            {
                solid[i] = !outside[i];
            }

            var eroded = new float[total];

            for (int z = 1; z < sz - 1; z++)
            {
                for (int y = 1; y < sy - 1; y++)
                {
                    for (int x = 1; x < sx - 1; x++)
                    {
                        int i = index(x, y, z);
                        bool all = solid[i] && solid[i - 1] && solid[i + 1] && solid[i - sx] && solid[i + sx] && solid[i - sx * sy] && solid[i + sx * sy];
                        eroded[i] = all ? 1f : 0f;
                    }
                }
            }

            // Suaviza o campo (média 3x3x3) e extrai a superfície em 0,5.
            var field = new float[total];

            for (int blur = 0; blur < 1; blur++)
            {
                var src = blur == 0 ? eroded : (float[])field.Clone();

                for (int z = 1; z < sz - 1; z++)
                {
                    for (int y = 1; y < sy - 1; y++)
                    {
                        for (int x = 1; x < sx - 1; x++)
                        {
                            float sum = 0f;

                            for (int dz = -1; dz <= 1; dz++)
                            {
                                for (int dy = -1; dy <= 1; dy++)
                                {
                                    for (int dx = -1; dx <= 1; dx++)
                                    {
                                        sum += src[index(x + dx, y + dy, z + dz)];
                                    }
                                }
                            }

                            field[index(x, y, z)] = sum / 27f;
                        }
                    }
                }
            }

            var result = new MeshData();

            for (int c = 0; c < System.Enum.GetValues(typeof(Cat)).Length; c++)
            {
                result.Subs.Add(new List<int>());
            }

            var cellVertex = new int[total];
            var cellCat = new sbyte[total];

            for (int i = 0; i < total; i++)
            {
                cellVertex[i] = -1;
            }

            const float iso = 0.5f;
            int[,] corner = { { 0, 0, 0 }, { 1, 0, 0 }, { 0, 1, 0 }, { 1, 1, 0 }, { 0, 0, 1 }, { 1, 0, 1 }, { 0, 1, 1 }, { 1, 1, 1 } };
            int[,] edges = { { 0, 1 }, { 2, 3 }, { 4, 5 }, { 6, 7 }, { 0, 2 }, { 1, 3 }, { 4, 6 }, { 5, 7 }, { 0, 4 }, { 1, 5 }, { 2, 6 }, { 3, 7 } };
            var f = new float[8];

            for (int z = 0; z < sz - 1; z++)
            {
                for (int y = 0; y < sy - 1; y++)
                {
                    for (int x = 0; x < sx - 1; x++)
                    {
                        int inside = 0;

                        for (int k = 0; k < 8; k++)
                        {
                            f[k] = field[index(x + corner[k, 0], y + corner[k, 1], z + corner[k, 2])];
                            inside += f[k] >= iso ? 1 : 0;
                        }

                        if (inside == 0 || inside == 8)
                        {
                            continue;
                        }

                        Vector3 sum = Vector3.zero;
                        int crossings = 0;

                        for (int e = 0; e < 12; e++)
                        {
                            float a = f[edges[e, 0]];
                            float b = f[edges[e, 1]];

                            if ((a >= iso) == (b >= iso))
                            {
                                continue;
                            }

                            float tt = (iso - a) / (b - a);
                            Vector3 pa = new Vector3(corner[edges[e, 0], 0], corner[edges[e, 0], 1], corner[edges[e, 0], 2]);
                            Vector3 pb = new Vector3(corner[edges[e, 1], 0], corner[edges[e, 1], 1], corner[edges[e, 1], 2]);
                            sum += Vector3.Lerp(pa, pb, tt);
                            crossings++;
                        }

                        Vector3 local = sum / crossings;
                        int id = index(x, y, z);
                        cellVertex[id] = result.Pos.Count;
                        result.Pos.Add(min + (new Vector3(x, y, z) + local) * voxel);
                        result.Nrm.Add(Vector3.up);
                        result.Uv.Add(Vector2.zero);
                        cellCat[id] = NearestCategory(cat, sx, sy, sz, x, y, z, index);
                    }
                }
            }

            // Filtro de maioria nas categorias das células (limpa o "sal e pimenta" nas bordas das janelas).
            for (int pass = 0; pass < 2; pass++)
            {
                var smoothed = (sbyte[])cellCat.Clone();

                for (int z = 1; z < sz - 1; z++)
                {
                    for (int y = 1; y < sy - 1; y++)
                    {
                        for (int x = 1; x < sx - 1; x++)
                        {
                            if (cellVertex[index(x, y, z)] < 0)
                            {
                                continue;
                            }

                            var counts = new float[16];

                            for (int dz = -1; dz <= 1; dz++)
                            {
                                for (int dy = -1; dy <= 1; dy++)
                                {
                                    for (int dx = -1; dx <= 1; dx++)
                                    {
                                        int j = index(x + dx, y + dy, z + dz);

                                        if (cellVertex[j] >= 0)
                                        {
                                            counts[cellCat[j]] += Weight(cellCat[j]);
                                        }
                                    }
                                }
                            }

                            int best = cellCat[index(x, y, z)];

                            for (int c = 0; c < 16; c++)
                            {
                                if (counts[c] > counts[best])
                                {
                                    best = c;
                                }
                            }

                            smoothed[index(x, y, z)] = (sbyte)best;
                        }
                    }
                }

                cellCat = smoothed;
            }

            // Uma face (quad) por aresta da grade que cruza a superfície.
            void Quad(int c0, int c1, int c2, int c3, bool flip, int catIndex)
            {
                int v0 = cellVertex[c0];
                int v1 = cellVertex[c1];
                int v2 = cellVertex[c2];
                int v3 = cellVertex[c3];

                if (v0 < 0 || v1 < 0 || v2 < 0 || v3 < 0)
                {
                    return;
                }

                List<int> list = result.Subs[catIndex];

                if (flip)
                {
                    list.AddRange(new[] { v0, v3, v2, v0, v2, v1 });
                }
                else
                {
                    list.AddRange(new[] { v0, v1, v2, v0, v2, v3 });
                }
            }

            int PickCat(int c0, int c1, int c2, int c3)
            {
                int best = -1;
                var votes = new int[16];

                foreach (int c in new[] { cellCat[c0], cellCat[c1], cellCat[c2], cellCat[c3] })
                {
                    if (c >= 0)
                    {
                        votes[c]++;
                    }
                }

                for (int c = 0; c < 16; c++)
                {
                    if (votes[c] > 0 && (best < 0 || votes[c] > votes[best] || (votes[c] == votes[best] && Priority(c) > Priority(best))))
                    {
                        best = c;
                    }
                }

                return best < 0 ? (int)Cat.Tinta : best;
            }

            for (int z = 1; z < sz - 1; z++)
            {
                for (int y = 1; y < sy - 1; y++)
                {
                    for (int x = 1; x < sx - 1; x++)
                    {
                        float here = field[index(x, y, z)];
                        bool hereIn = here >= iso;

                        // Aresta em +X: células (x, y-1..y, z-1..z).
                        if (hereIn != (field[index(x + 1, y, z)] >= iso))
                        {
                            int c0 = index(x, y - 1, z - 1), c1 = index(x, y, z - 1), c2 = index(x, y, z), c3 = index(x, y - 1, z);
                            Quad(c0, c1, c2, c3, !hereIn, PickCat(c0, c1, c2, c3));
                        }

                        if (hereIn != (field[index(x, y + 1, z)] >= iso))
                        {
                            int c0 = index(x - 1, y, z - 1), c1 = index(x - 1, y, z), c2 = index(x, y, z), c3 = index(x, y, z - 1);
                            Quad(c0, c1, c2, c3, !hereIn, PickCat(c0, c1, c2, c3));
                        }

                        if (hereIn != (field[index(x, y, z + 1)] >= iso))
                        {
                            int c0 = index(x - 1, y - 1, z), c1 = index(x, y - 1, z), c2 = index(x, y, z), c3 = index(x - 1, y, z);
                            Quad(c0, c1, c2, c3, !hereIn, PickCat(c0, c1, c2, c3));
                        }
                    }
                }
            }

            ComputeNormals(result);

            return result;
        }

        private static sbyte NearestCategory(sbyte[] cat, int sx, int sy, int sz, int x, int y, int z, System.Func<int, int, int, int> index)
        {
            for (int r = 0; r <= 4; r++)
            {
                int best = -1;
                var counts = new float[16];

                for (int dz = -r; dz <= r; dz++)
                {
                    for (int dy = -r; dy <= r; dy++)
                    {
                        for (int dx = -r; dx <= r; dx++)
                        {
                            if (Mathf.Max(Mathf.Abs(dx), Mathf.Max(Mathf.Abs(dy), Mathf.Abs(dz))) != r)
                            {
                                continue;
                            }

                            int xx = x + dx, yy = y + dy, zz = z + dz;

                            if (xx < 0 || yy < 0 || zz < 0 || xx >= sx || yy >= sy || zz >= sz)
                            {
                                continue;
                            }

                            int c = cat[index(xx, yy, zz)];

                            if (c >= 32)
                            {
                                c -= 32;
                            }

                            if (c >= 0)
                            {
                                counts[c] += Weight(c);
                            }
                        }
                    }
                }

                for (int c = 0; c < 16; c++)
                {
                    if (counts[c] > 0 && (best < 0 || counts[c] > counts[best]))
                    {
                        best = c;
                    }
                }

                if (best >= 0)
                {
                    return (sbyte)best;
                }
            }

            return (sbyte)Cat.Tinta;
        }

        /// <summary>Normais suaves por vértice (média das normais das faces, ponderadas pela área).</summary>
        private static void ComputeNormals(MeshData m)
        {
            var sum = new Vector3[m.Pos.Count];

            foreach (List<int> sub in m.Subs)
            {
                for (int i = 0; i + 2 < sub.Count; i += 3)
                {
                    Vector3 n = Vector3.Cross(m.Pos[sub[i + 1]] - m.Pos[sub[i]], m.Pos[sub[i + 2]] - m.Pos[sub[i]]);
                    sum[sub[i]] += n;
                    sum[sub[i + 1]] += n;
                    sum[sub[i + 2]] += n;
                }
            }

            for (int i = 0; i < sum.Length; i++)
            {
                m.Nrm[i] = sum[i].sqrMagnitude < 1e-14f ? Vector3.up : sum[i].normalized;
            }
        }

        // ---------- Materiais ----------

        private static Material[] MaterialsFor(Modelo modelo, MeshData data, string unused)
        {
            var list = new List<Material>();

            for (int s = 0; s < data.Subs.Count; s++)
            {
                if (data.Subs[s].Count > 0)
                {
                    list.Add(MaterialOf(modelo, (Cat)s));
                }
            }

            return list.ToArray();
        }

        private static Material MaterialOf(Modelo modelo, Cat cat)
        {
            switch (cat)
            {
                case Cat.Tinta:
                case Cat.Atlas:
                    return MaterialDePintura(modelo, 0);
                case Cat.Vidro: return Flat("Carro_Vidro", new Color(0.035f, 0.045f, 0.055f), 0.92f, 0.1f);
                case Cat.Plastico: return Flat("Carro_Plastico", new Color(0.055f, 0.055f, 0.06f), 0.3f, 0f);
                case Cat.Cromo: return Flat("Carro_Cromo", new Color(0.55f, 0.56f, 0.58f), 0.75f, 0.8f);
                case Cat.Farol: return Flat("Carro_Farol", new Color(0.88f, 0.9f, 0.9f), 0.9f, 0.1f);
                case Cat.Lanterna: return Flat("Carro_Lanterna", new Color(0.55f, 0.03f, 0.03f), 0.8f, 0.05f);
                case Cat.Pisca: return Flat("Carro_Pisca", new Color(0.85f, 0.4f, 0.04f), 0.7f, 0.05f);
                case Cat.Pneu: return Flat("Carro_Pneu", new Color(0.03f, 0.03f, 0.032f), 0.15f, 0f);
                case Cat.Aro: return Flat("Carro_Aro", new Color(0.42f, 0.43f, 0.45f), 0.55f, 0.5f);
                default: return WheelMaterial();
            }
        }

        /// <summary>Material de pintura do carro na cor de índice paint (CarPaint.Colors). O nome leva "_Pintura_" para o Pintar achar.</summary>
        public static Material MaterialDePintura(Modelo modelo, int paint)
        {
            paint = ((paint % CarPaint.Colors.Length) + CarPaint.Colors.Length) % CarPaint.Colors.Length;
            string color = CarPaint.Colors[paint].name;
            string path = $"{MaterialPasta}/{modelo}_Pintura_{color}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);

            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(material, path);
            }

            if (modelo == Modelo.Uno)
            {
                material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(UnoAtlasPath(paint)));
                material.SetColor("_BaseColor", Color.white);
                material.SetFloat("_Smoothness", 0.7f);
                material.SetFloat("_Metallic", 0.15f);
            }
            else
            {
                material.SetColor("_BaseColor", CarPaint.Colors[paint].color);
                material.SetFloat("_Smoothness", 0.72f);
                material.SetFloat("_Metallic", 0.3f);
            }

            EditorUtility.SetDirty(material);

            return material;
        }

        private static Material Flat(string name, Color color, float smoothness, float metallic)
        {
            string path = $"{MaterialPasta}/{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);

            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(material, path);
            }

            material.SetColor("_BaseColor", color);
            material.SetFloat("_Smoothness", smoothness);
            material.SetFloat("_Metallic", metallic);
            EditorUtility.SetDirty(material);

            return material;
        }

        private static Material WheelMaterial()
        {
            string path = $"{MaterialPasta}/Carro_RodaUno.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);

            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(material, path);
            }

            material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/ThirdParty/Sketchfab/FiatUno/FiatUno_Roda.png"));
            material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_Smoothness", 0.3f);
            material.SetFloat("_Metallic", 0.2f);
            EditorUtility.SetDirty(material);

            return material;
        }

        // ---------- Texturas: atlas do Uno recolorido e placas ----------

        private static string UnoAtlasPath(int paint) => $"{MaterialPasta}/UnoAtlas_{CarPaint.Colors[paint].name}.png";

        private const string PlateTexturePath = MaterialPasta + "/Carro_Placas.png";

        private static void PrepararTexturas()
        {
            string basePath = "Assets/ThirdParty/Sketchfab/FiatUno/FiatUno_BaseColor.png";
            bool missing = !File.Exists(PlateTexturePath);

            for (int i = 0; i < CarPaint.Colors.Length; i++)
            {
                missing |= !File.Exists(UnoAtlasPath(i));
            }

            if (!missing)
            {
                return;
            }

            var source = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            source.LoadImage(File.ReadAllBytes(basePath));
            const int size = 1024;
            int ratio = source.width / size;
            Color32[] big = source.GetPixels32();
            var pixels = new Color32[size * size];

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    int r = 0, g = 0, b = 0, a = 0;

                    for (int dy = 0; dy < ratio; dy++)
                    {
                        for (int dx = 0; dx < ratio; dx++)
                        {
                            Color32 c = big[(y * ratio + dy) * source.width + x * ratio + dx];
                            r += c.r;
                            g += c.g;
                            b += c.b;
                            a += c.a;
                        }
                    }

                    int n = ratio * ratio;
                    pixels[y * size + x] = new Color32((byte)(r / n), (byte)(g / n), (byte)(b / n), (byte)(a / n));
                }
            }

            Object.DestroyImmediate(source);

            // Cor dominante da carroceria: o maior grupo de pixels (quantizado) com brilho alto e pouca saturação.
            var histogram = new Dictionary<int, int>();

            foreach (Color32 c in pixels)
            {
                int key = (c.r >> 3) << 10 | (c.g >> 3) << 5 | (c.b >> 3);
                histogram[key] = histogram.TryGetValue(key, out int count) ? count + 1 : 1;
            }

            int bestKey = 0;
            int bestCount = -1;

            foreach (var pair in histogram)
            {
                if (pair.Value > bestCount)
                {
                    bestCount = pair.Value;
                    bestKey = pair.Key;
                }
            }

            Color dominant = new Color(((bestKey >> 10) & 31) / 31f, ((bestKey >> 5) & 31) / 31f, (bestKey & 31) / 31f);
            float dominantLum = dominant.grayscale;

            for (int i = 0; i < CarPaint.Colors.Length; i++)
            {
                var variant = new Color32[pixels.Length];
                Color target = CarPaint.Colors[i].color;

                for (int p = 0; p < pixels.Length; p++)
                {
                    Color c = pixels[p];
                    float distance = Mathf.Abs(c.r - dominant.r) + Mathf.Abs(c.g - dominant.g) + Mathf.Abs(c.b - dominant.b);
                    float weight = Mathf.Clamp01(1f - (distance - 0.06f) / 0.1f);

                    if (weight > 0f)
                    {
                        float shade = Mathf.Clamp(c.grayscale / dominantLum, 0.6f, 1.15f);
                        Color painted = target * shade;
                        painted.a = 1f;
                        c = Color.Lerp(c, painted, weight);
                    }

                    variant[p] = c;
                }

                var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
                texture.SetPixels32(variant);
                File.WriteAllBytes(UnoAtlasPath(i), texture.EncodeToPNG());
                Object.DestroyImmediate(texture);
            }

            WritePlateTexture();
            AssetDatabase.Refresh();

            for (int i = 0; i < CarPaint.Colors.Length; i++)
            {
                ConfigureTexture(UnoAtlasPath(i), 1024);
            }

            ConfigureTexture(PlateTexturePath, 512);
            ConfigureTexture("Assets/ThirdParty/Sketchfab/FiatUno/FiatUno_Roda.png", 512);
            ConfigureTexture(basePath, 256);
            Debug.Log($"[Carros] atlas do Uno recolorido; cor dominante {dominant}");
        }

        private static void ConfigureTexture(string path, int maxSize)
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.mipmapEnabled = path != PlateTexturePath;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.maxTextureSize = maxSize;

            foreach (string platform in new[] { "Android", "iPhone" })
            {
                TextureImporterPlatformSettings settings = importer.GetPlatformTextureSettings(platform);
                settings.overridden = true;
                settings.maxTextureSize = maxSize;
                settings.format = TextureImporterFormat.ASTC_6x6;
                importer.SetPlatformTextureSettings(settings);
            }

            importer.SaveAndReimport();
        }

        // ---------- Placas fictícias (estilo Mercosul) ----------

        private static readonly Dictionary<char, string> Font = new Dictionary<char, string>
        {
            ['A'] = ".###.#...##...#######...##...##...#",
            ['B'] = "####.#...##...#####.#...##...#####.",
            ['C'] = ".#####....#....#....#....#.....####",
            ['D'] = "####.#...##...##...##...##...#####.",
            ['E'] = "######....#....####.#....#....#####",
            ['F'] = "######....#....####.#....#....#....",
            ['G'] = ".#####....#....#.####...##...#.###.",
            ['H'] = "#...##...##...#######...##...##...#",
            ['K'] = "#...##..#.#.#..##...#.#..#..#.#...#",
            ['L'] = "#....#....#....#....#....#....#####",
            ['M'] = "#...###.###.#.##.#.##...##...##...#",
            ['P'] = "####.#...##...#####.#....#....#....",
            ['R'] = "####.#...##...#####.#.#..#..#.#...#",
            ['S'] = ".#####....#.....###.....#....#####.",
            ['T'] = "#####..#....#....#....#....#....#..",
            ['X'] = "#...##...#.#.#...#...#.#.#...##...#",
            ['0'] = ".###.#...##..###.#.###..##...#.###.",
            ['1'] = "..#...##....#....#....#....#...###.",
            ['2'] = ".###.#...#....#...#...#...#...#####",
            ['3'] = "####.....#....#.###.....#....#####.",
            ['4'] = "...#...##..#.#.#..#.#####...#....#.",
            ['5'] = "######....####.....#....##...#.###.",
            ['6'] = "..##..#...#....####.#...##...#.###.",
            ['7'] = "#####....#...#...#...#....#....#...",
            ['8'] = ".###.#...##...#.###.#...##...#.###.",
            ['9'] = ".###.#...##...#.####....#...#..##..",
        };

        private static void WritePlateTexture()
        {
            const int w = 256;
            const int h = 64;
            var texture = new Texture2D(w, h * PlacaCount, TextureFormat.RGBA32, false);
            var pixels = new Color32[w * h * PlacaCount];

            for (int plate = 0; plate < PlacaCount; plate++)
            {
                for (int y = 0; y < h; y++)
                {
                    for (int x = 0; x < w; x++)
                    {
                        Color c = new Color(0.94f, 0.94f, 0.92f);

                        if (y >= h - 13)
                        {
                            c = new Color(0.1f, 0.2f, 0.62f);
                        }

                        if (x < 3 || x >= w - 3 || y < 3 || y >= h - 3)
                        {
                            c = new Color(0.05f, 0.05f, 0.05f);
                        }

                        pixels[(plate * h + y) * w + x] = c;
                    }
                }

                string text = Placas[plate];
                const int scale = 5;
                const int glyphW = 5 * scale;
                int total = 7 * glyphW + 6 * 7;
                int startX = (w - total) / 2;
                int startY = 9;

                for (int ch = 0; ch < 7; ch++)
                {
                    string glyph = Font[text[ch]];

                    for (int gy = 0; gy < 7; gy++)
                    {
                        for (int gx = 0; gx < 5; gx++)
                        {
                            if (glyph[gy * 5 + gx] != '#')
                            {
                                continue;
                            }

                            for (int sy = 0; sy < scale; sy++)
                            {
                                for (int sx = 0; sx < scale; sx++)
                                {
                                    int px = startX + ch * (glyphW + 7) + gx * scale + sx;
                                    int py = startY + (6 - gy) * scale + sy;
                                    pixels[(plate * h + py) * w + px] = new Color32(15, 15, 18, 255);
                                }
                            }
                        }
                    }
                }
            }

            texture.SetPixels32(pixels);
            File.WriteAllBytes(PlateTexturePath, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
        }

        private static Material PlateMaterial()
        {
            string path = $"{MaterialPasta}/Carro_Placa.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);

            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(material, path);
            }

            material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(PlateTexturePath));
            material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_Smoothness", 0.3f);
            EditorUtility.SetDirty(material);

            return material;
        }

        /// <summary>Duas placas (traseira em rearZ e dianteira em frontZ), 40 x 13 cm, com o texto de índice plate.</summary>
        private static Mesh BuildPlateMesh(int plate, float rearZ, float frontZ, float y, Modelo modelo)
        {
            const float halfW = 0.2f;
            const float halfH = 0.065f;
            var vertices = new List<Vector3>();
            var uvs = new List<Vector2>();
            var normals = new List<Vector3>();
            var triangles = new List<int>();
            float v0 = plate / (float)PlacaCount;
            float v1 = (plate + 1) / (float)PlacaCount;

            void Quad(float z, float dir, float yc)
            {
                int o = vertices.Count;
                float zz = z + dir * 0.012f;
                vertices.Add(new Vector3(-halfW, yc - halfH, zz));
                vertices.Add(new Vector3(halfW, yc - halfH, zz));
                vertices.Add(new Vector3(halfW, yc + halfH, zz));
                vertices.Add(new Vector3(-halfW, yc + halfH, zz));

                // Quem olha a traseira (de trás para a frente) vê +X à direita; quem olha a dianteira vê -X à direita.
                if (dir < 0f)
                {
                    uvs.Add(new Vector2(0f, v0));
                    uvs.Add(new Vector2(1f, v0));
                    uvs.Add(new Vector2(1f, v1));
                    uvs.Add(new Vector2(0f, v1));
                    triangles.AddRange(new[] { o, o + 2, o + 1, o, o + 3, o + 2 });
                }
                else
                {
                    uvs.Add(new Vector2(1f, v0));
                    uvs.Add(new Vector2(0f, v0));
                    uvs.Add(new Vector2(0f, v1));
                    uvs.Add(new Vector2(1f, v1));
                    triangles.AddRange(new[] { o, o + 1, o + 2, o, o + 2, o + 3 });
                }

                for (int i = 0; i < 4; i++)
                {
                    normals.Add(new Vector3(0f, 0f, dir));
                }
            }

            Quad(rearZ, -1f, y);
            Quad(frontZ, 1f, y - 0.08f);
            var mesh = new Mesh();
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();

            return mesh;
        }
    }
}

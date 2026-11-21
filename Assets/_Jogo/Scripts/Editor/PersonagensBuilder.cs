using System.Collections.Generic;
using System.IO;
using Entregador.Game;
using UnityEditor;
using UnityEngine;

namespace Entregador.Editor
{
    /// <summary>
    /// Prepara os cinco personagens do Mixamo (Assets/ThirdParty/Mixamo) para o jogo: tira o que o capacete esconde (cabelo, cílios,
    /// chapéu, a cabeça do corpo) e o que a roupa cobre (tronco, coxas e pés do corpo nu), decima a malha com os pesos de osso
    /// (MeshDecimator), troca os materiais por um URP/Lit com a textura de cor em 1024 e grava um prefab por personagem mais o
    /// catálogo (PersonagensCatalogo) que o MotoVisual usa. O Robsão é engordado (tronco, barriga, braços e coxas) deformando a malha.
    /// Menu: Jogo/Gerar personagens.
    /// </summary>
    public static class PersonagensBuilder
    {
        public const string Pasta = "Assets/_Jogo/Personagens";
        public const string CatalogoPath = Pasta + "/PersonagensCatalogo.asset";

        /// <summary>Meta de triângulos do corpo do personagem (sem o capacete).</summary>
        public const int MetaTriangulos = 22000;

        private enum Grupo
        {
            Outro,
            Cabeca,
            Tronco,
            Coxa,
            Canela,
            Pe,
        }

        private struct Entrada
        {
            public Personagem Personagem;
            public string Nome;
            public Grupo[] PodarNoCorpo;
        }

        private static readonly Entrada[] Entradas =
        {
            new Entrada { Personagem = Personagem.Jefinho, Nome = "Jefinho", PodarNoCorpo = new[] { Grupo.Cabeca, Grupo.Tronco, Grupo.Coxa, Grupo.Pe } },
            new Entrada { Personagem = Personagem.Robsao, Nome = "Robsao", PodarNoCorpo = new[] { Grupo.Cabeca } },
            new Entrada { Personagem = Personagem.Marcao, Nome = "Marcao", PodarNoCorpo = new[] { Grupo.Cabeca, Grupo.Tronco, Grupo.Coxa, Grupo.Canela, Grupo.Pe } },
            new Entrada { Personagem = Personagem.SeuValdir, Nome = "SeuValdir", PodarNoCorpo = new[] { Grupo.Cabeca, Grupo.Tronco, Grupo.Coxa, Grupo.Canela, Grupo.Pe } },
            new Entrada { Personagem = Personagem.Camila, Nome = "Camila", PodarNoCorpo = new[] { Grupo.Cabeca, Grupo.Tronco, Grupo.Coxa, Grupo.Pe } },
        };

        [MenuItem("Jogo/Gerar personagens")]
        public static void GerarTodosMenu()
        {
            GerarTodos();
        }

        public static void GerarDaLinhaDeComando()
        {
            GerarTodos();
        }

        /// <summary>Catálogo pronto (gera os prefabs se faltarem; JOGO_REGERAR_PERSONAGENS=1 força).</summary>
        public static PersonagensCatalogo Garantir()
        {
            var catalogo = AssetDatabase.LoadAssetAtPath<PersonagensCatalogo>(CatalogoPath);
            bool force = System.Environment.GetEnvironmentVariable("JOGO_REGERAR_PERSONAGENS") == "1";

            if (catalogo == null || force)
            {
                GerarTodos();
                catalogo = AssetDatabase.LoadAssetAtPath<PersonagensCatalogo>(CatalogoPath);
            }

            return catalogo;
        }

        public static void GerarTodos()
        {
            Directory.CreateDirectory(Pasta);
            AssetDatabase.DeleteAsset(CatalogoPath);
            var catalogo = ScriptableObject.CreateInstance<PersonagensCatalogo>();
            AssetDatabase.CreateAsset(catalogo, CatalogoPath);

            foreach (Entrada e in Entradas)
            {
                GameObject prefab = Gerar(e);
                catalogo.Definir(e.Personagem, prefab);
            }

            EditorUtility.SetDirty(catalogo);
            AssetDatabase.SaveAssets();
        }

        private static Grupo Classificar(string bone)
        {
            int colon = bone.LastIndexOf(':');
            string n = colon >= 0 ? bone.Substring(colon + 1) : bone;

            if (n.StartsWith("Head"))
            {
                return Grupo.Cabeca;
            }

            if (n.StartsWith("Hips") || n.StartsWith("Spine"))
            {
                return Grupo.Tronco;
            }

            if (n.Contains("UpLeg"))
            {
                return Grupo.Coxa;
            }

            if (n.Contains("Foot") || n.Contains("Toe"))
            {
                return Grupo.Pe;
            }

            if (n.Contains("Leg"))
            {
                return Grupo.Canela;
            }

            return Grupo.Outro;
        }

        private static GameObject Gerar(Entrada entrada)
        {
            string folder = $"Assets/ThirdParty/Mixamo/{entrada.Nome}";
            var fbx = AssetDatabase.LoadAssetAtPath<GameObject>($"{folder}/{entrada.Nome}.fbx");
            GameObject go = Object.Instantiate(fbx);
            go.name = entrada.Nome;
            Material material = CriarMaterial(entrada.Nome, $"{folder}/{entrada.Nome}_Corpo.png");
            string meshPath = $"{Pasta}/{entrada.Nome}_Malhas.asset";
            AssetDatabase.DeleteAsset(meshPath);

            // 1. O que sai: cabelo, cílios, chapéu de obra.
            var work = new List<(SkinnedMeshRenderer renderer, MeshData data, string[] materialNames)>();
            int total = 0;

            foreach (SkinnedMeshRenderer renderer in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                string n = renderer.name;

                if (n.Contains("Hair") || n.Contains("Eyelash") || n.Contains("Helmet"))
                {
                    Object.DestroyImmediate(renderer.gameObject);

                    continue;
                }

                Mesh mesh = renderer.sharedMesh;
                MeshData data = MeshData.FromMesh(mesh);
                var names = new string[mesh.subMeshCount];

                for (int s = 0; s < names.Length; s++)
                {
                    names[s] = s < renderer.sharedMaterials.Length && renderer.sharedMaterials[s] != null ? renderer.sharedMaterials[s].name : "";
                }

                // Submalhas de cabelo (o Robsão vem numa malha só, com o cabelo como segundo material).
                for (int s = 0; s < data.Subs.Count; s++)
                {
                    if (names[s].ToLowerInvariant().Contains("hair"))
                    {
                        data.Subs[s].Clear();
                    }
                }

                // 2. Poda do corpo: triângulos dominados pela cabeça (o capacete cobre) e por tronco/coxas/pés (a roupa cobre).
                if (n.Contains("Body") || n == "Ch12")
                {
                    Podar(data, renderer, entrada.PodarNoCorpo);
                }

                work.Add((renderer, data, names));
                total += data.Triangles;
            }

            // 3. Decimação proporcional e materiais.
            float ratio = Mathf.Min(1f, MetaTriangulos / (float)Mathf.Max(1, total));
            int finalTotal = 0;
            bool first = true;

            foreach (var item in work)
            {
                int target = Mathf.Max(200, Mathf.RoundToInt(item.data.Triangles * ratio));
                MeshData reduced = MeshDecimator.Decimate(item.data, target, 20f);

                if (entrada.Personagem == Personagem.Robsao)
                {
                    Engordar(reduced, item.renderer);
                }

                MeshData welded = reduced.Weld();
                Mesh output = welded.ToMesh($"{entrada.Nome}_{item.renderer.name}");
                output.bindposes = item.renderer.sharedMesh.bindposes;
                output.RecalculateBounds();

                if (first)
                {
                    AssetDatabase.CreateAsset(output, meshPath);
                    first = false;
                }
                else
                {
                    AssetDatabase.AddObjectToAsset(output, meshPath);
                }

                item.renderer.sharedMesh = output;
                var materials = new Material[output.subMeshCount];

                for (int i = 0; i < materials.Length; i++)
                {
                    materials[i] = material;
                }

                item.renderer.sharedMaterials = materials;
                item.renderer.updateWhenOffscreen = true;
                finalTotal += welded.Triangles;
            }

            string prefabPath = $"{Pasta}/{entrada.Nome}.prefab";
            AssetDatabase.DeleteAsset(prefabPath);
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(go, prefabPath);
            Object.DestroyImmediate(go);
            Debug.Log($"[Personagens] {entrada.Nome}: {total} triângulos após a poda, {finalTotal} depois da decimação");
            File.AppendAllText("/private/tmp/claude-501/-Users-felipegoncalves/a275e7fa-b013-40ea-bca5-3880da79d55c/scratchpad/personagens.txt", $"{entrada.Nome}: poda {total}, final {finalTotal}\n");

            return prefab;
        }

        private static void Podar(MeshData data, SkinnedMeshRenderer renderer, Grupo[] groups)
        {
            Transform[] bones = renderer.bones;
            var prune = new HashSet<Grupo>(groups);

            Grupo Dominant(int vertex)
            {
                BoneWeight w = data.Weights[vertex];
                int best = w.boneIndex0;
                float weight = w.weight0;

                if (w.weight1 > weight)
                {
                    best = w.boneIndex1;
                    weight = w.weight1;
                }

                if (w.weight2 > weight)
                {
                    best = w.boneIndex2;
                    weight = w.weight2;
                }

                if (w.weight3 > weight)
                {
                    best = w.boneIndex3;
                }

                return best >= 0 && best < bones.Length ? Classificar(bones[best].name) : Grupo.Outro;
            }

            foreach (List<int> sub in data.Subs)
            {
                var kept = new List<int>(sub.Count);

                for (int i = 0; i + 2 < sub.Count; i += 3)
                {
                    bool a = prune.Contains(Dominant(sub[i]));
                    bool b = prune.Contains(Dominant(sub[i + 1]));
                    bool c = prune.Contains(Dominant(sub[i + 2]));

                    // Triângulos com algum vértice fora do grupo ficam (a emenda com o pescoço e as mãos).
                    if (a && b && c)
                    {
                        continue;
                    }

                    kept.Add(sub[i]);
                    kept.Add(sub[i + 1]);
                    kept.Add(sub[i + 2]);
                }

                sub.Clear();
                sub.AddRange(kept);
            }
        }

        /// <summary>Robsão: alarga tronco e quadril, empurra a barriga para frente e engrossa braços e coxas, pelos pesos dos ossos.</summary>
        private static void Engordar(MeshData data, SkinnedMeshRenderer renderer)
        {
            Transform[] bones = renderer.bones;
            Transform mesh = renderer.transform;
            Vector3 Joint(string suffix)
            {
                foreach (Transform b in bones)
                {
                    if (b.name.EndsWith(suffix))
                    {
                        return mesh.InverseTransformPoint(b.position);
                    }
                }

                return Vector3.zero;
            }

            Vector3 hips = Joint(":Hips");
            Vector3 armL = Joint(":LeftArm");
            Vector3 foreArmL = Joint(":LeftForeArm");
            Vector3 armR = Joint(":RightArm");
            Vector3 foreArmR = Joint(":RightForeArm");
            Vector3 legL = Joint(":LeftUpLeg");
            Vector3 kneeL = Joint(":LeftLeg");
            Vector3 legR = Joint(":RightUpLeg");
            Vector3 kneeR = Joint(":RightLeg");
            var kind = new int[bones.Length];
            var limb = new int[bones.Length];

            for (int i = 0; i < bones.Length; i++)
            {
                string n = bones[i].name;
                int colon = n.LastIndexOf(':');
                n = colon >= 0 ? n.Substring(colon + 1) : n;
                kind[i] = n == "Hips" || n.StartsWith("Spine") ? 1 : 0;
                limb[i] = n == "LeftArm" ? 1 : n == "RightArm" ? 2 : n == "LeftUpLeg" ? 3 : n == "RightUpLeg" ? 4 : 0;
            }

            for (int v = 0; v < data.Pos.Count; v++)
            {
                BoneWeight w = data.Weights[v];
                float torso = 0f;
                var limbWeight = new float[5];
                Accumulate(w.boneIndex0, w.weight0);
                Accumulate(w.boneIndex1, w.weight1);
                Accumulate(w.boneIndex2, w.weight2);
                Accumulate(w.boneIndex3, w.weight3);

                void Accumulate(int bone, float weight)
                {
                    if (weight <= 0f || bone < 0 || bone >= bones.Length)
                    {
                        return;
                    }

                    torso += kind[bone] == 1 ? weight : 0f;
                    limbWeight[limb[bone]] += weight;
                }

                Vector3 p = data.Pos[v];

                if (torso > 0f)
                {
                    float k = 1f + 0.3f * torso;
                    p.x = p.x * k;
                    float z = hips.z + (p.z - hips.z) * k;

                    // Barriga: para frente na altura da cintura.
                    float belly = Mathf.Clamp01(1f - Mathf.Abs(p.y - (hips.y + 0.12f)) / 0.2f);
                    z += p.z > hips.z ? 0.055f * torso * belly : 0f;
                    p.z = z;
                }

                RadialGrow(ref p, armL, foreArmL, limbWeight[1], 1.2f);
                RadialGrow(ref p, armR, foreArmR, limbWeight[2], 1.2f);
                RadialGrow(ref p, legL, kneeL, limbWeight[3], 1.18f);
                RadialGrow(ref p, legR, kneeR, limbWeight[4], 1.18f);
                data.Pos[v] = p;
            }

            // Recalcula as normais pelas faces (a deformação mudou as formas).
            var sum = new Vector3[data.Pos.Count];

            foreach (List<int> sub in data.Subs)
            {
                for (int i = 0; i + 2 < sub.Count; i += 3)
                {
                    Vector3 nrm = Vector3.Cross(data.Pos[sub[i + 1]] - data.Pos[sub[i]], data.Pos[sub[i + 2]] - data.Pos[sub[i]]);
                    sum[sub[i]] += nrm;
                    sum[sub[i + 1]] += nrm;
                    sum[sub[i + 2]] += nrm;
                }
            }

            for (int i = 0; i < sum.Length; i++)
            {
                if (sum[i].sqrMagnitude > 1e-14f)
                {
                    data.Nrm[i] = Vector3.Lerp(data.Nrm[i], sum[i].normalized, 0.5f).normalized;
                }
            }
        }

        private static void RadialGrow(ref Vector3 p, Vector3 a, Vector3 b, float weight, float factor)
        {
            if (weight <= 0f)
            {
                return;
            }

            Vector3 axis = b - a;
            float len = axis.magnitude;

            if (len < 1e-5f)
            {
                return;
            }

            axis /= len;
            float t = Mathf.Clamp(Vector3.Dot(p - a, axis), 0f, len);
            Vector3 closest = a + axis * t;
            p = closest + (p - closest) * (1f + (factor - 1f) * weight);
        }

        private static Material CriarMaterial(string nome, string texturePath)
        {
            ConfigureTexture(texturePath);
            Directory.CreateDirectory(CityMaterials.Folder + "/Personagens");
            string path = $"{CityMaterials.Folder}/Personagens/Personagem_{nome}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);

            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(material, path);
            }

            material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath));
            material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_Smoothness", 0.18f);
            material.SetFloat("_Metallic", 0f);
            EditorUtility.SetDirty(material);

            return material;
        }

        private static void ConfigureTexture(string path)
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);

            if (importer == null)
            {
                return;
            }

            importer.maxTextureSize = 1024;
            importer.mipmapEnabled = true;
            importer.alphaSource = TextureImporterAlphaSource.None;

            foreach (string platform in new[] { "Android", "iPhone" })
            {
                TextureImporterPlatformSettings settings = importer.GetPlatformTextureSettings(platform);
                settings.overridden = true;
                settings.maxTextureSize = 1024;
                settings.format = TextureImporterFormat.ASTC_6x6;
                importer.SetPlatformTextureSettings(settings);
            }

            importer.SaveAndReimport();
        }
    }
}

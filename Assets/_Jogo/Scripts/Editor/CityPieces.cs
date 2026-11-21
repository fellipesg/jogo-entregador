using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Entregador.Editor
{
    /// <summary>Os kits do Kenney (CC0) em Assets/ThirdParty/Kenney. O nome do enum é o nome da pasta.</summary>
    public enum Kit
    {
        CarKit,
        CityKitRoads,
        CityKitCommercial,
        CityKitSuburban,
    }

    /// <summary>
    /// Fábrica das peças do cenário. Toda criação de prédio, rua, árvore, carro e marcador passa por aqui.
    ///
    /// Convenção de escala: os FBX do Kenney são importados com escala 1 (KenneyImportSettings). Os kits de cidade
    /// são desenhados em "tiles" de 1 unidade (uma rua = 1) e o kit de carros em metros reais; o tamanho final é
    /// escolhido AQUI, na hora de posicionar, com as constantes abaixo (tile de rua 1 -> 12 m, casas 9,5x, carros 1,5x).
    /// Os tamanhos de cada modelo são medidos (Renderer.bounds do FBX), nunca chutados.
    /// </summary>
    public static class CityPieces
    {
        /// <summary>Escala dos kits Roads e Commercial: o tile de rua (1 unidade) vira a rua de 12 m.</summary>
        public const float CityScale = 12f;

        /// <summary>Escala das casas do kit Suburban.</summary>
        public const float SuburbScale = 9.5f;

        /// <summary>Escala dos carros (o sedã de 2,55 m vira cerca de 3,8 m; mais gordinho que o real, combina com a moto).</summary>
        public const float CarScale = 1.5f;

        private static readonly Dictionary<string, GameObject> AssetCache = new Dictionary<string, GameObject>();
        private static readonly Dictionary<string, Bounds> BoundsCache = new Dictionary<string, Bounds>();

        private static GameObject LoadAsset(Kit kit, string model)
        {
            string path = $"{KenneyImportSettings.Folder}{kit}/Models/{model}.fbx";

            if (!AssetCache.TryGetValue(path, out GameObject asset) || asset == null)
            {
                asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);

                if (asset == null)
                {
                    throw new System.InvalidOperationException("Modelo do Kenney não encontrado: " + path);
                }

                AssetCache[path] = asset;
            }

            return asset;
        }

        /// <summary>Caixa do modelo sem escala (espaço local do FBX), medida a partir dos renderers.</summary>
        public static Bounds ModelBounds(Kit kit, string model)
        {
            string key = kit + "/" + model;

            if (BoundsCache.TryGetValue(key, out Bounds cached))
            {
                return cached;
            }

            GameObject asset = LoadAsset(kit, model);
            Renderer[] renderers = asset.GetComponentsInChildren<Renderer>();
            Bounds bounds = renderers[0].bounds;

            for (int i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }

            BoundsCache[key] = bounds;

            return bounds;
        }

        /// <summary>
        /// Instancia um modelo do Kenney (cópia solta, sem vínculo com o FBX), aplica o material único do kit
        /// e, se pedido, um BoxCollider do tamanho medido. A posição é a da base do modelo.
        /// </summary>
        public static GameObject Model(Transform parent, Kit kit, string model, Vector3 position, float yaw, Vector3 scale, bool collider = false)
        {
            GameObject go = Object.Instantiate(LoadAsset(kit, model));
            go.name = model;
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
            go.transform.localScale = scale;

            Material material = CityMaterials.Kenney(kit.ToString());

            foreach (Renderer renderer in go.GetComponentsInChildren<Renderer>())
            {
                renderer.sharedMaterial = material;
            }

            if (collider)
            {
                Bounds b = ModelBounds(kit, model);
                var box = go.AddComponent<BoxCollider>();
                box.center = b.center;
                box.size = b.size;
            }

            return go;
        }

        public static GameObject Model(Transform parent, Kit kit, string model, Vector3 position, float yaw, float scale, bool collider = false) =>
            Model(parent, kit, model, position, yaw, Vector3.one * scale, collider);

        /// <summary>Um carro do kit de carros, em pé no chão, com collider sólido.</summary>
        public static GameObject Car(Transform parent, string model, Vector3 position, float yaw, bool collider = true) =>
            Model(parent, Kit.CarKit, model, position, yaw, CarScale, collider);

        /// <summary>Um tile de rua (quadrado de 1 unidade) esticado para width x length metros (length ao longo do eixo X do tile, onde correm as faixas); a espessura fica em 4 cm.</summary>
        public static GameObject RoadTile(Transform parent, string model, Vector3 center, float yaw, float width, float length) =>
            Model(parent, Kit.CityKitRoads, model, center, yaw, new Vector3(length, 2f, width));

        public static GameObject Box(Transform parent, string name, Vector3 center, Vector3 size, Material material, bool collider = false)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Setup(go, parent, name, center, size, material, collider);

            return go;
        }

        public static GameObject Cylinder(Transform parent, string name, Vector3 center, float radius, float height, Material material, bool collider = false)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Setup(go, parent, name, center, new Vector3(radius * 2f, height * 0.5f, radius * 2f), material, collider);

            return go;
        }

        public static GameObject Sphere(Transform parent, string name, Vector3 center, float diameter, Material material)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Setup(go, parent, name, center, Vector3.one * diameter, material, false);

            return go;
        }

        public static GameObject Capsule(Transform parent, string name, Vector3 center, Vector3 size, Material material)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            Setup(go, parent, name, center, size, material, false);

            return go;
        }

        public static GameObject Empty(Transform parent, string name, Vector3 position)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;

            return go;
        }

        /// <summary>Uma árvore (kit Suburban, tree-large ou tree-small) com altura aproximada em metros; tronco sólido opcional.</summary>
        public static GameObject Tree(Transform parent, string name, Vector3 basePosition, float height, bool solidTrunk, bool large = true)
        {
            string model = large ? "tree-large" : "tree-small";
            float scale = height / ModelBounds(Kit.CityKitSuburban, model).size.y;
            GameObject tree = Model(parent, Kit.CityKitSuburban, model, basePosition, 0f, scale);
            tree.name = name;

            if (solidTrunk)
            {
                var capsule = tree.AddComponent<CapsuleCollider>();
                capsule.center = new Vector3(0f, 0.15f, 0f);
                capsule.radius = 0.04f;
                capsule.height = 0.3f;
            }

            return tree;
        }

        private static void Setup(GameObject go, Transform parent, string name, Vector3 center, Vector3 size, Material material, bool collider)
        {
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.position = center;
            go.transform.localScale = size;
            go.GetComponent<Renderer>().sharedMaterial = material;

            if (!collider)
            {
                Object.DestroyImmediate(go.GetComponent<Collider>());
            }
        }
    }
}

using UnityEngine;

namespace Entregador.Editor
{
    /// <summary>
    /// Fábrica das peças do cenário. Toda criação de prédio, rua, árvore e marcador passa por aqui:
    /// para trocar o greybox por modelos (Kenney City Kit), basta mudar estes métodos para instanciar prefabs.
    /// </summary>
    public static class CityPieces
    {
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

        /// <summary>Um prédio genérico. Aqui entraria o prefab do kit de cidade.</summary>
        public static GameObject Building(Transform parent, string name, Vector3 baseCenter, Vector3 footprintAndHeight, Material material)
        {
            Vector3 center = baseCenter + Vector3.up * footprintAndHeight.y * 0.5f;

            return Box(parent, name, center, footprintAndHeight, material);
        }

        /// <summary>Uma árvore de praça: tronco e copa.</summary>
        public static GameObject Tree(Transform parent, string name, Vector3 basePosition, Material trunk, Material leaves)
        {
            GameObject root = Empty(parent, name, basePosition);
            Cylinder(root.transform, "Tronco", Vector3.zero + Vector3.up * 1.5f, 0.3f, 3f, trunk, true).transform.localPosition = Vector3.up * 1.5f;
            Sphere(root.transform, "Copa", Vector3.up * 4.5f, 4.5f, leaves).transform.localPosition = Vector3.up * 4.5f;

            return root;
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

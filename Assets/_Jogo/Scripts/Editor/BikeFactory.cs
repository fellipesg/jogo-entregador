using System.IO;
using Entregador.Game;
using UnityEditor;
using UnityEngine;

namespace Entregador.Editor
{
    /// <summary>Monta a moto de placeholder (primitivas) com Rigidbody, collider e os componentes de jogo.</summary>
    public static class BikeFactory
    {
        public static ArcadeBikeController Build(Vector3 position, float yaw)
        {
            Material dark = CityMaterials.Lit("Moto_Corpo", new Color(0.1f, 0.1f, 0.12f), 0.3f);
            Material metal = CityMaterials.Lit("Moto_Metal", new Color(0.6f, 0.62f, 0.66f), 0.5f);
            Material tank = CityMaterials.Lit("Moto_Tanque", new Color(0.85f, 0.85f, 0.88f), 0.4f);
            Material rider = CityMaterials.Lit("Moto_Piloto", new Color(0.15f, 0.35f, 0.75f));
            Material helmet = CityMaterials.Lit("Moto_Capacete", new Color(0.95f, 0.95f, 0.95f), 0.5f);
            Material visor = CityMaterials.Lit("Moto_Viseira", new Color(0.05f, 0.05f, 0.08f), 0.8f);
            Material box = CityMaterials.Lit("Moto_Bau", new Color(1f, 0.08f, 0.1f), 0.35f);
            Material lid = CityMaterials.Lit("Moto_BauTampa", new Color(0.75f, 0.03f, 0.06f), 0.35f);
            Material stripe = CityMaterials.Lit("Moto_BauFaixa", new Color(1f, 0.85f, 0.2f));
            Material headlight = CityMaterials.Unlit("Moto_Farol", new Color(1f, 0.97f, 0.75f));
            Material taillight = CityMaterials.Unlit("Moto_Lanterna", new Color(1f, 0.1f, 0.05f));

            var root = new GameObject("Moto");
            root.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));

            var body = root.AddComponent<Rigidbody>();
            body.mass = 180f;
            body.linearDamping = 0f;
            body.angularDamping = 0.05f;
            body.constraints = RigidbodyConstraints.FreezeRotation;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.Continuous;

            var collider = root.AddComponent<BoxCollider>();
            collider.center = new Vector3(0f, 0.65f, 0f);
            collider.size = new Vector3(0.75f, 1.2f, 1.9f);
            collider.sharedMaterial = SlipperyMaterial();

            Transform visual = new GameObject("Visual").transform;
            visual.SetParent(root.transform, false);

            // Chassi fino, tanque e banco.
            Part(CityPieces.Box(visual, "Corpo", Vector3.zero, new Vector3(0.22f, 0.3f, 1.2f), dark), new Vector3(0f, 0.55f, 0f));
            Part(CityPieces.Box(visual, "Tanque", Vector3.zero, new Vector3(0.3f, 0.22f, 0.5f), tank), new Vector3(0f, 0.82f, 0.28f));
            Part(CityPieces.Box(visual, "Banco", Vector3.zero, new Vector3(0.3f, 0.1f, 0.55f), dark), new Vector3(0f, 0.8f, -0.25f));
            Part(CityPieces.Box(visual, "Escapamento", Vector3.zero, new Vector3(0.1f, 0.1f, 0.6f), metal), new Vector3(0.2f, 0.32f, -0.4f));

            // Rodas do kit de carros do Kenney (eixo no X), mais finas que as de carro.
            Wheel(visual, "RodaFrente", new Vector3(0f, 0.35f, 0.72f));
            Wheel(visual, "RodaTras", new Vector3(0f, 0.35f, -0.72f));

            // Garfo, guidão e farol.
            GameObject fork = CityPieces.Cylinder(visual, "Garfo", Vector3.zero, 0.03f, 0.8f, metal);
            fork.transform.localPosition = new Vector3(0f, 0.7f, 0.62f);
            fork.transform.localRotation = Quaternion.Euler(-18f, 0f, 0f);
            GameObject bars = CityPieces.Cylinder(visual, "Guidao", Vector3.zero, 0.025f, 0.75f, metal);
            bars.transform.localPosition = new Vector3(0f, 1.08f, 0.55f);
            bars.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            Part(CityPieces.Sphere(visual, "Farol", Vector3.zero, 0.2f, headlight), new Vector3(0f, 0.98f, 0.72f));
            Part(CityPieces.Box(visual, "Lanterna", Vector3.zero, new Vector3(0.18f, 0.08f, 0.04f), taillight), new Vector3(0f, 0.82f, -1.1f));

            // Piloto: tronco (cápsula), capacete e viseira.
            Part(CityPieces.Capsule(visual, "Piloto", Vector3.zero, new Vector3(0.42f, 0.42f, 0.42f), rider), new Vector3(0f, 1.3f, 0.02f));
            Part(CityPieces.Sphere(visual, "Capacete", Vector3.zero, 0.36f, helmet), new Vector3(0f, 1.92f, 0.08f));
            Part(CityPieces.Sphere(visual, "Viseira", Vector3.zero, 0.22f, visor), new Vector3(0f, 1.92f, 0.22f));

            // Baú de entrega vermelho com tampa e faixa.
            Part(CityPieces.Box(visual, "Bau", Vector3.zero, new Vector3(0.74f, 0.55f, 0.74f), box), new Vector3(0f, 1.2f, -0.82f));
            Part(CityPieces.Box(visual, "BauTampa", Vector3.zero, new Vector3(0.78f, 0.1f, 0.78f), lid), new Vector3(0f, 1.52f, -0.82f));
            Part(CityPieces.Box(visual, "BauFaixa", Vector3.zero, new Vector3(0.76f, 0.1f, 0.02f), stripe), new Vector3(0f, 1.25f, -1.2f));

            var input = root.AddComponent<BikeInput>();
            var controller = root.AddComponent<ArcadeBikeController>();
            controller.Configure(input, visual);

            return controller;
        }

        private static void Wheel(Transform parent, string name, Vector3 localPosition)
        {
            GameObject wheel = CityPieces.Model(parent, Kit.CarKit, "wheel-default", Vector3.zero, 0f, new Vector3(0.5f, 1.15f, 1.15f));
            wheel.name = name;
            wheel.transform.localPosition = localPosition;
        }

        private static void Part(GameObject go, Vector3 localPosition)
        {
            go.transform.localPosition = localPosition;
        }

        private static PhysicsMaterial SlipperyMaterial()
        {
            Directory.CreateDirectory(RenderSetup.SettingsFolder);
            string path = RenderSetup.SettingsFolder + "/MotoFisica.asset";
            var material = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(path);

            if (material == null)
            {
                material = new PhysicsMaterial("MotoFisica");
                AssetDatabase.CreateAsset(material, path);
            }

            material.dynamicFriction = 0f;
            material.staticFriction = 0f;
            material.bounciness = 0f;
            material.frictionCombine = PhysicsMaterialCombine.Minimum;
            material.bounceCombine = PhysicsMaterialCombine.Minimum;
            EditorUtility.SetDirty(material);

            return material;
        }
    }
}

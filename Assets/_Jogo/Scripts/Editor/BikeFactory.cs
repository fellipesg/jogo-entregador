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
            Material dark = CityMaterials.Lit("Moto_Corpo", new Color(0.12f, 0.12f, 0.14f));
            Material tire = CityMaterials.Lit("Moto_Roda", new Color(0.05f, 0.05f, 0.05f));
            Material rider = CityMaterials.Lit("Moto_Piloto", new Color(0.15f, 0.35f, 0.75f));
            Material helmet = CityMaterials.Lit("Moto_Capacete", new Color(0.95f, 0.95f, 0.95f));
            Material box = CityMaterials.Lit("Moto_Bau", new Color(1f, 0.2f, 0.35f), 0.2f);

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

            Part(CityPieces.Box(visual, "Corpo", Vector3.zero, new Vector3(0.35f, 0.45f, 1.4f), dark), new Vector3(0f, 0.6f, 0f));
            Wheel(visual, "RodaFrente", new Vector3(0f, 0.35f, 0.75f), tire);
            Wheel(visual, "RodaTras", new Vector3(0f, 0.35f, -0.75f), tire);
            Part(CityPieces.Capsule(visual, "Piloto", Vector3.zero, new Vector3(0.5f, 0.5f, 0.5f), rider), new Vector3(0f, 1.3f, 0.05f));
            Part(CityPieces.Sphere(visual, "Capacete", Vector3.zero, 0.38f, helmet), new Vector3(0f, 1.95f, 0.1f));
            Part(CityPieces.Box(visual, "Bau", Vector3.zero, new Vector3(0.75f, 0.65f, 0.75f), box), new Vector3(0f, 1.4f, -0.8f));

            var input = root.AddComponent<BikeInput>();
            var controller = root.AddComponent<ArcadeBikeController>();
            controller.Configure(input, visual);

            return controller;
        }

        private static void Wheel(Transform parent, string name, Vector3 localPosition, Material material)
        {
            GameObject wheel = CityPieces.Cylinder(parent, name, Vector3.zero, 0.35f, 0.2f, material);
            wheel.transform.localPosition = localPosition;
            wheel.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
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

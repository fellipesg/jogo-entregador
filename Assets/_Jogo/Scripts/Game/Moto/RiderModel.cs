using System.Collections.Generic;
using UnityEngine;

namespace Entregador.Game
{
    /// <summary>
    /// O capacete integral do piloto, em malha procedural (loft de superelipses). O corpo, as roupas e as mãos agora vêm dos
    /// personagens do Mixamo (ver PersonagensCatalogo e RiderRig); o capacete continua procedural porque cobre o cabelo,
    /// o chapéu de obra do Seu Valdir e o rosto, e combina com as cores de cada personagem.
    /// </summary>
    public static class RiderModel
    {
        private static readonly Vector3 X = Vector3.right;
        private static readonly Vector3 Y = Vector3.up;
        private static readonly Vector3 Z = Vector3.forward;

        private static Vector3 V(float x, float y, float z) => new Vector3(x, y, z);

        /// <summary>
        /// Capacete com a origem no centro da cabeça (x direita, y cima, z frente), ocupando cerca de 0,26 x 0,31 x 0,31 m na escala 1.
        /// Faixa no alto, viseira escura e queixeira; sem triângulos virados para dentro.
        /// </summary>
        public static Mesh BuildHelmet(PersonagemConfig c, MotoPalette pal, float scale, out int triangles)
        {
            var kit = new MotoMeshKit(pal);
            int cHelmet = kit.Color(c.Capacete, 0.85f);
            int cHelmetAcc = kit.Color(c.CapaceteDetalhe, 0.8f);
            int cVisor = kit.Color(new Color(0.03f, 0.04f, 0.06f), 0.95f);
            float s = scale;
            var helmet = new List<Sec>();
            float[][] hs =
            {
                new[] { -0.145f, 0.045f, 0.05f, 0.05f }, new[] { -0.118f, 0.092f, 0.11f, 0.035f }, new[] { -0.06f, 0.12f, 0.14f, 0.015f },
                new[] { 0f, 0.13f, 0.155f, 0f }, new[] { 0.06f, 0.125f, 0.15f, -0.005f }, new[] { 0.115f, 0.1f, 0.12f, -0.01f },
                new[] { 0.15f, 0.05f, 0.065f, -0.01f }, new[] { 0.168f, 0.012f, 0.014f, -0.01f },
            };

            foreach (float[] r in hs)
            {
                helmet.Add(new Sec(V(0f, r[0] * s, r[3] * s), X, Z, r[1] * s, r[2] * s, 2.3f));
            }

            kit.Loft(helmet, 16, cHelmet);

            // Faixa no alto do capacete.
            var stripe = new List<Sec>();

            foreach (float[] r in new[] { new[] { -0.125f, 0.085f }, new[] { -0.06f, 0.138f }, new[] { 0f, 0.159f }, new[] { 0.06f, 0.146f }, new[] { 0.11f, 0.105f } })
            {
                stripe.Add(new Sec(V(0f, r[1] * s, r[0] * s), X, Y, 0.016f * s, 0.004f * s, 2f));
            }

            kit.Loft(stripe, 6, cHelmetAcc);

            // Viseira escura.
            kit.Loft(new List<Sec>
            {
                new Sec(V(0f, 0.015f * s, 0.05f * s), X, Y, 0.108f * s, 0.04f * s, 3f),
                new Sec(V(0f, 0.015f * s, 0.115f * s), X, Y, 0.098f * s, 0.04f * s, 3f),
                new Sec(V(0f, 0.015f * s, 0.145f * s), X, Y, 0.085f * s, 0.036f * s, 3f),
                new Sec(V(0f, 0.015f * s, 0.163f * s), X, Y, 0.06f * s, 0.028f * s, 3f),
            }, 14, cVisor);

            // Queixeira escura embaixo.
            kit.Loft(new List<Sec>
            {
                new Sec(V(0f, -0.105f * s, 0.07f * s), X, Y, 0.05f * s, 0.012f * s, 3f),
                new Sec(V(0f, -0.108f * s, 0.135f * s), X, Y, 0.03f * s, 0.01f * s, 3f),
            }, 8, cVisor);

            triangles = kit.Triangles;

            return kit.ToMesh("Capacete");
        }
    }
}

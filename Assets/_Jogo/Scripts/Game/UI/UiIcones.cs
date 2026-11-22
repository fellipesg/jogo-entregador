using System.Collections.Generic;
using UnityEngine;

namespace Entregador.Game
{
    /// <summary>Ícones vetoriais (listas de polígonos em 0..1, y para cima) para o UiShape.</summary>
    public static class UiIcones
    {
        /// <summary>Manobras na ordem: 0 siga em frente, 1 vire à direita, 2 vire à esquerda, 3 mantenha à direita, 4 mantenha à esquerda, 5 retorno, 6 rotatória, 7 chegada.</summary>
        public static List<Vector2[]> Manobra(int manobra)
        {
            var list = new List<Vector2[]>();

            switch (manobra)
            {
                case 1:
                    list.Add(UiShape.Rect(0.30f, 0.08f, 0.46f, 0.58f));
                    list.Add(UiShape.Rect(0.30f, 0.42f, 0.68f, 0.58f));
                    list.Add(new[] { new Vector2(0.62f, 0.26f), new Vector2(0.94f, 0.5f), new Vector2(0.62f, 0.74f) });
                    break;
                case 2:
                    list.Add(UiShape.Rect(0.54f, 0.08f, 0.70f, 0.58f));
                    list.Add(UiShape.Rect(0.32f, 0.42f, 0.70f, 0.58f));
                    list.Add(new[] { new Vector2(0.38f, 0.26f), new Vector2(0.06f, 0.5f), new Vector2(0.38f, 0.74f) });
                    break;
                case 3:
                    return Rotated(Straight(), -38f);
                case 4:
                    return Rotated(Straight(), 38f);
                case 5:
                    list.Add(UiShape.Rect(0.20f, 0.10f, 0.36f, 0.72f));
                    list.Add(UiShape.Rect(0.20f, 0.62f, 0.72f, 0.78f));
                    list.Add(UiShape.Rect(0.56f, 0.42f, 0.72f, 0.78f));
                    list.Add(new[] { new Vector2(0.44f, 0.44f), new Vector2(0.84f, 0.44f), new Vector2(0.64f, 0.12f) });
                    break;
                case 6:
                    for (int a = 300; a < 300 + 300; a += 20)
                    {
                        float a0 = a * Mathf.Deg2Rad;
                        float a1 = (a + 21) * Mathf.Deg2Rad;
                        var c = new Vector2(0.5f, 0.38f);
                        list.Add(new[]
                        {
                            c + new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)) * 0.19f, c + new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)) * 0.30f,
                            c + new Vector2(Mathf.Cos(a1), Mathf.Sin(a1)) * 0.30f, c + new Vector2(Mathf.Cos(a1), Mathf.Sin(a1)) * 0.19f,
                        });
                    }

                    list.Add(UiShape.Rect(0.44f, 0.02f, 0.56f, 0.20f));
                    list.Add(UiShape.Rect(0.44f, 0.64f, 0.56f, 0.80f));
                    list.Add(new[] { new Vector2(0.32f, 0.78f), new Vector2(0.5f, 0.98f), new Vector2(0.68f, 0.78f) });
                    break;
                case 7:
                    list.Add(UiShape.Rect(0.24f, 0.08f, 0.33f, 0.92f));
                    list.Add(new[] { new Vector2(0.33f, 0.90f), new Vector2(0.84f, 0.74f), new Vector2(0.33f, 0.56f) });
                    break;
                default:
                    return Straight();
            }

            return list;
        }

        private static List<Vector2[]> Straight()
        {
            return new List<Vector2[]>
            {
                UiShape.Rect(0.42f, 0.08f, 0.58f, 0.64f),
                new[] { new Vector2(0.22f, 0.58f), new Vector2(0.5f, 0.94f), new Vector2(0.78f, 0.58f) },
            };
        }

        private static List<Vector2[]> Rotated(List<Vector2[]> polys, float deg)
        {
            var rot = Quaternion.Euler(0f, 0f, deg);

            foreach (Vector2[] p in polys)
            {
                for (int i = 0; i < p.Length; i++)
                {
                    Vector3 v = rot * (new Vector3(p[i].x - 0.5f, p[i].y - 0.5f, 0f) * 0.92f);
                    p[i] = new Vector2(v.x + 0.5f, v.y + 0.5f);
                }
            }

            return polys;
        }

        public static List<Vector2[]> Celular()
        {
            return new List<Vector2[]> { UiShape.Rect(0.30f, 0.06f, 0.70f, 0.94f) };
        }

        public static List<Vector2[]> CelularTela()
        {
            return new List<Vector2[]> { UiShape.Rect(0.36f, 0.22f, 0.64f, 0.84f) };
        }

        public static List<Vector2[]> Pausa()
        {
            return new List<Vector2[]> { UiShape.Rect(0.26f, 0.18f, 0.43f, 0.82f), UiShape.Rect(0.57f, 0.18f, 0.74f, 0.82f) };
        }
    }
}

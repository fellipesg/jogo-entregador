using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Entregador.Editor
{
    /// <summary>
    /// Acumula geometria (quadrados, caixas, cilindros, blobs) por material e vira UM MeshRenderer com um submesh por material.
    /// Os UVs são em METROS do mundo (u = p·direção da face): o tiling do material (1 / metros por repetição) faz a textura
    /// ter tamanho real em qualquer parede, sem esticar. A malha é montada em coordenadas de mundo (objeto na origem).
    ///
    /// Convenção de uma face: Rect(bl, right, up, w, h) com normal = Cross(up, right) (a frente vista por quem olha para ela).
    /// Num prédio, F é a direção para fora da fachada e R = Cross(F, up) é a "direita" de quem olha a fachada.
    /// </summary>
    public sealed class MeshBatch
    {
        private sealed class Part
        {
            public readonly List<Vector3> Vertices = new List<Vector3>();
            public readonly List<Vector3> Normals = new List<Vector3>();
            public readonly List<Vector2> Uvs = new List<Vector2>();
            public readonly List<int> Triangles = new List<int>();
        }

        private readonly Dictionary<Material, Part> _parts = new Dictionary<Material, Part>();
        private readonly List<Material> _order = new List<Material>();

        public int TriangleCount { get; private set; }

        public bool IsEmpty => _order.Count == 0;

        private Part For(Material material)
        {
            if (!_parts.TryGetValue(material, out Part part))
            {
                part = new Part();
                _parts[material] = part;
                _order.Add(material);
            }

            return part;
        }

        /// <summary>Quadrilátero. bl = canto inferior esquerdo; right e up são unitários; uvOffset desloca a textura (variação).</summary>
        public void Rect(Material material, Vector3 bl, Vector3 right, Vector3 up, float width, float height, Vector2 uvOffset = default)
        {
            if (width <= 0.0005f || height <= 0.0005f)
            {
                return;
            }

            Part p = For(material);
            Vector3 normal = Vector3.Cross(up, right).normalized;
            int start = p.Vertices.Count;
            Vector3 br = bl + right * width;
            Vector3 tl = bl + up * height;
            Vector3 tr = br + up * height;

            foreach (Vector3 v in new[] { bl, br, tr, tl })
            {
                p.Vertices.Add(v);
                p.Normals.Add(normal);
                p.Uvs.Add(new Vector2(Vector3.Dot(v, right), Vector3.Dot(v, up)) + uvOffset);
            }

            p.Triangles.AddRange(new[] { start, start + 3, start + 1, start + 3, start + 2, start + 1 });
            TriangleCount += 2;
        }

        /// <summary>Caixa orientada. o = canto inferior esquerdo da frente; f para fora da frente; w ao longo de R, h para cima, d para trás.</summary>
        public void Box(Material material, Vector3 o, Vector3 f, float w, float h, float d, bool front = true, bool back = true, bool sides = true, bool top = true, Vector2 uvOffset = default)
        {
            Vector3 up = Vector3.up;
            Vector3 r = Vector3.Cross(f, up);

            if (front)
            {
                Rect(material, o, r, up, w, h, uvOffset);
            }

            if (back)
            {
                Rect(material, o + r * w - f * d, -r, up, w, h, uvOffset);
            }

            if (sides)
            {
                Rect(material, o + r * w, -f, up, d, h, uvOffset);
                Rect(material, o - f * d, f, up, d, h, uvOffset);
            }

            if (top)
            {
                Rect(material, o + up * h, r, -f, w, d, uvOffset);
            }
        }

        /// <summary>Caixa alinhada aos eixos, pelo centro e tamanho (todas as faces, menos a de baixo).</summary>
        public void AxisBox(Material material, Vector3 center, Vector3 size)
        {
            // Frente para -Z (f = back): r = Cross(back, up) = +X; o é o canto inferior esquerdo da frente.
            var o = new Vector3(center.x - size.x * 0.5f, center.y - size.y * 0.5f, center.z - size.z * 0.5f);
            Box(material, o, Vector3.back, size.x, size.y, size.z);
        }

        /// <summary>
        /// Quadrilátero qualquer (p0..p3 em ordem de contorno) virado para o lado de outward (o enrolamento se ajusta sozinho).
        /// UV em metros: u ao longo de p0->p1, v perpendicular no plano.
        /// </summary>
        public void Quad(Material material, Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, Vector3 outward, Vector2 uvOffset = default)
        {
            Part p = For(material);
            Vector3 normal = Vector3.Cross(p3 - p0, p1 - p0).normalized;
            bool flip = Vector3.Dot(normal, outward) < 0f;

            if (flip)
            {
                normal = -normal;
            }

            Vector3 uAxis = (p1 - p0).normalized;
            Vector3 vAxis = Vector3.Cross(normal, uAxis).normalized;
            int start = p.Vertices.Count;

            foreach (Vector3 v in new[] { p0, p1, p2, p3 })
            {
                p.Vertices.Add(v);
                p.Normals.Add(normal);
                p.Uvs.Add(new Vector2(Vector3.Dot(v, uAxis), Vector3.Dot(v, vAxis)) + uvOffset);
            }

            if (flip)
            {
                p.Triangles.AddRange(new[] { start, start + 1, start + 3, start + 1, start + 2, start + 3 });
            }
            else
            {
                p.Triangles.AddRange(new[] { start, start + 3, start + 1, start + 3, start + 2, start + 1 });
            }

            TriangleCount += 2;
        }

        /// <summary>Triângulo virado para o lado de outward.</summary>
        public void Tri(Material material, Vector3 a, Vector3 b, Vector3 c, Vector3 outward, Vector2 uvOffset = default)
        {
            Part p = For(material);
            Vector3 normal = Vector3.Cross(b - a, c - a).normalized;
            bool flip = Vector3.Dot(normal, outward) < 0f;

            if (flip)
            {
                normal = -normal;
                (b, c) = (c, b);
            }

            // Cross(b-a, c-a) é a normal do enrolamento (a, b, c) na Unity; se não virou, usa a ordem invertida.
            Vector3 uAxis = (b - a).normalized;
            Vector3 vAxis = Vector3.Cross(normal, uAxis).normalized;
            int start = p.Vertices.Count;

            foreach (Vector3 v in new[] { a, b, c })
            {
                p.Vertices.Add(v);
                p.Normals.Add(normal);
                p.Uvs.Add(new Vector2(Vector3.Dot(v, uAxis), Vector3.Dot(v, vAxis)) + uvOffset);
            }

            p.Triangles.AddRange(flip ? new[] { start, start + 1, start + 2 } : new[] { start, start + 1, start + 2 });
            TriangleCount += 1;
        }

        /// <summary>Disco (polígono regular) num plano: right e up unitários; normal = Cross(up, right).</summary>
        public void Disc(Material material, Vector3 center, Vector3 right, Vector3 up, float radius, int sides)
        {
            Vector3 normal = Vector3.Cross(up, right).normalized;

            for (int i = 0; i < sides; i++)
            {
                float a0 = i * Mathf.PI * 2f / sides;
                float a1 = (i + 1) * Mathf.PI * 2f / sides;
                Vector3 v0 = center + (right * Mathf.Cos(a0) + up * Mathf.Sin(a0)) * radius;
                Vector3 v1 = center + (right * Mathf.Cos(a1) + up * Mathf.Sin(a1)) * radius;
                Tri(material, center, v0, v1, normal);
            }
        }

        /// <summary>Polígono horizontal (leque) com raios por vértice, normal para cima. UV = xz do mundo.</summary>
        public void Fan(Material material, Vector3 center, float[] radii, float rotation = 0f)
        {
            Part p = For(material);
            int n = radii.Length;
            int start = p.Vertices.Count;
            p.Vertices.Add(center);
            p.Normals.Add(Vector3.up);
            p.Uvs.Add(new Vector2(center.x, center.z));

            for (int i = 0; i < n; i++)
            {
                float a = rotation + i * Mathf.PI * 2f / n;
                Vector3 v = center + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radii[i];
                p.Vertices.Add(v);
                p.Normals.Add(Vector3.up);
                p.Uvs.Add(new Vector2(v.x, v.z));
            }

            for (int i = 0; i < n; i++)
            {
                // Vértices em ângulo crescente (anti-horário em X,Z visto de cima, que na Unity é horário no sentido da frente).
                p.Triangles.AddRange(new[] { start, start + 1 + (i + 1) % n, start + 1 + i });
            }

            TriangleCount += n;
        }

        /// <summary>Tronco de cone vertical (cilindro se r0 == r1). Base em baseCenter; normais suaves; tampa de cima opcional.</summary>
        public void Frustum(Material material, Vector3 baseCenter, float r0, float r1, float height, int sides, bool capTop = true)
        {
            Part p = For(material);
            int start = p.Vertices.Count;
            float slope = (r0 - r1) / Mathf.Max(0.001f, height);

            for (int i = 0; i <= sides; i++)
            {
                float a = i * Mathf.PI * 2f / sides;
                var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                Vector3 normal = (dir + Vector3.up * slope).normalized;
                float u = a * (r0 + r1) * 0.5f;
                p.Vertices.Add(baseCenter + dir * r0);
                p.Normals.Add(normal);
                p.Uvs.Add(new Vector2(u, baseCenter.y));
                p.Vertices.Add(baseCenter + Vector3.up * height + dir * r1);
                p.Normals.Add(normal);
                p.Uvs.Add(new Vector2(u, baseCenter.y + height));
            }

            for (int i = 0; i < sides; i++)
            {
                int a = start + i * 2;
                p.Triangles.AddRange(new[] { a, a + 1, a + 2, a + 1, a + 3, a + 2 });
            }

            TriangleCount += sides * 2;

            if (capTop && r1 > 0.001f)
            {
                var radii = new float[sides];

                for (int i = 0; i < sides; i++)
                {
                    radii[i] = r1;
                }

                Fan(material, baseCenter + Vector3.up * height, radii);
            }
        }

        /// <summary>Esfera irregular (icosaedro subdividido uma vez) para copas de árvore; UV planar em metros.</summary>
        public void Blob(Material material, Vector3 center, Vector3 radius, int seed, bool subdivide = true)
        {
            Part p = For(material);
            var random = new System.Random(seed);
            float t = (1f + Mathf.Sqrt(5f)) * 0.5f;
            var verts = new List<Vector3>
            {
                new Vector3(-1, t, 0), new Vector3(1, t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0),
                new Vector3(0, -1, t), new Vector3(0, 1, t), new Vector3(0, -1, -t), new Vector3(0, 1, -t),
                new Vector3(t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1),
            };
            int[] faces =
            {
                0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
                3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1,
            };

            var cache = new Dictionary<long, int>();

            int Mid(int a, int b)
            {
                long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;

                if (!cache.TryGetValue(key, out int index))
                {
                    verts.Add((verts[a] + verts[b]) * 0.5f);
                    index = verts.Count - 1;
                    cache[key] = index;
                }

                return index;
            }

            var next = new List<int>();

            for (int i = 0; i < faces.Length && !subdivide; i += 3)
            {
                next.AddRange(new[] { faces[i], faces[i + 1], faces[i + 2] });
            }

            for (int i = 0; i < faces.Length && subdivide; i += 3)
            {
                int a = faces[i];
                int b = faces[i + 1];
                int c = faces[i + 2];
                int ab = Mid(a, b);
                int bc = Mid(b, c);
                int ca = Mid(c, a);
                next.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
            }

            int start = p.Vertices.Count;

            for (int i = 0; i < verts.Count; i++)
            {
                Vector3 dir = verts[i].normalized;
                float jitter = 0.82f + 0.3f * (float)random.NextDouble();
                Vector3 position = center + Vector3.Scale(dir, radius) * jitter;
                p.Vertices.Add(position);
                p.Normals.Add(dir);
                p.Uvs.Add(new Vector2(position.x + position.z * 0.7f, position.y + position.z * 0.3f));
            }

            for (int i = 0; i < next.Count; i += 3)
            {
                p.Triangles.AddRange(new[] { start + next[i], start + next[i + 1], start + next[i + 2] });
            }

            TriangleCount += next.Count / 3;
        }

        /// <summary>Fio fino entre dois pontos: dois quadrados em cruz, visíveis dos dois lados.</summary>
        public void Wire(Material material, Vector3 a, Vector3 b, float thickness)
        {
            Part p = For(material);
            Vector3 dir = (b - a).normalized;
            Vector3 side = Vector3.Cross(dir, Vector3.up);
            side = side.sqrMagnitude < 0.0001f ? Vector3.right : side.normalized;
            Vector3 vert = Vector3.Cross(dir, side).normalized;

            foreach (Vector3 axis in new[] { side, vert })
            {
                int start = p.Vertices.Count;
                Vector3 off = axis * (thickness * 0.5f);
                p.Vertices.Add(a - off);
                p.Vertices.Add(a + off);
                p.Vertices.Add(b + off);
                p.Vertices.Add(b - off);
                Vector3 n = Vector3.Cross(dir, axis).normalized;

                for (int i = 0; i < 4; i++)
                {
                    p.Normals.Add(n);
                    p.Uvs.Add(Vector2.zero);
                }

                p.Triangles.AddRange(new[] { start, start + 1, start + 2, start, start + 2, start + 3 });
                TriangleCount += 2;
            }
        }

        /// <summary>Cria o objeto com a malha (salva no arquivo de malhas da cidade, ver MeshStore) e marca como estático.</summary>
        public GameObject Build(Transform parent, string name, bool castShadows = true)
        {
            if (IsEmpty)
            {
                return null;
            }

            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();

            foreach (Material material in _order)
            {
                Part p = _parts[material];
                vertices.AddRange(p.Vertices);
                normals.AddRange(p.Normals);
                uvs.AddRange(p.Uvs);
            }

            var mesh = new Mesh { name = name };

            if (vertices.Count > 65000)
            {
                mesh.indexFormat = IndexFormat.UInt32;
            }

            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.subMeshCount = _order.Count;
            int offset = 0;

            for (int i = 0; i < _order.Count; i++)
            {
                Part p = _parts[_order[i]];
                var indices = new int[p.Triangles.Count];

                for (int k = 0; k < indices.Length; k++)
                {
                    indices[k] = p.Triangles[k] + offset;
                }

                mesh.SetTriangles(indices, i);
                offset += p.Vertices.Count;
            }

            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            mesh.name = MeshStore.UniqueName(name);

            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var filter = go.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;
            MeshStore.Add(mesh, filter);
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = _order.ToArray();
            renderer.shadowCastingMode = castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;

            return go;
        }
    }

    /// <summary>
    /// Guarda as malhas geradas num único asset binário (Assets/_Jogo/Scenes/SantoAmaro_MalhasNN.asset), para a cena
    /// ficar pequena (o projeto serializa assets em texto, e malhas em texto são enormes). É recriado a cada geração.
    /// </summary>
    public static class MeshStore
    {
        public const string Path = "Assets/_Jogo/Scenes/SantoAmaro_Malhas00.asset";

        private static readonly List<Mesh> Meshes = new List<Mesh>();
        private static readonly List<MeshFilter> Filters = new List<MeshFilter>();
        private static int _counter;

        public static void Begin()
        {
            Meshes.Clear();
            Filters.Clear();
            _counter = 0;
        }

        public static string UniqueName(string name) => $"{name}_{_counter++}";

        public static void Add(Mesh mesh, MeshFilter filter)
        {
            Meshes.Add(mesh);
            Filters.Add(filter);
        }

        /// <summary>Grava as malhas em binário, em arquivos de ~25 malhas (limite de tamanho do git) e aponta os MeshFilter da cena para elas.</summary>
        public static void Save()
        {
            const int perFile = 25;
            string folder = System.IO.Path.GetDirectoryName(Path).Replace('\\', '/');

            foreach (string old in System.IO.Directory.GetFiles(folder, "SantoAmaro_Malhas*.asset"))
            {
                AssetDatabase.DeleteAsset(old.Replace('\\', '/'));
            }

            var byName = new Dictionary<string, Mesh>();
            var names = new List<string>();

            foreach (Mesh mesh in Meshes)
            {
                names.Add(mesh.name);
            }

            for (int start = 0, file = 0; start < Meshes.Count; start += perFile, file++)
            {
                int count = Mathf.Min(perFile, Meshes.Count - start);
                string path = $"{folder}/SantoAmaro_Malhas{file:00}.asset";
                UnityEditorInternal.InternalEditorUtility.SaveToSerializedFileAndForget(Meshes.GetRange(start, count).ToArray(), path, false);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);

                foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
                {
                    if (asset is Mesh loaded)
                    {
                        byName[loaded.name] = loaded;
                    }
                }
            }

            if (byName.Count != names.Count)
            {
                throw new System.InvalidOperationException($"Malhas gravadas: {byName.Count} de {names.Count}.");
            }

            for (int i = 0; i < Filters.Count; i++)
            {
                Filters[i].sharedMesh = byName[names[i]];
            }

            Meshes.Clear();
            Filters.Clear();
        }
    }
}

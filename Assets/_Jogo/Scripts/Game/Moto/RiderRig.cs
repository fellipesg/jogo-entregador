using System.Collections.Generic;
using UnityEngine;

namespace Entregador.Game
{
    /// <summary>Ossos do esqueleto humanoide do Mixamo que o piloto usa (nomes sem o prefixo "mixamorig:").</summary>
    public enum Osso
    {
        Hips,
        Spine,
        Spine1,
        Spine2,
        Neck,
        Head,
        HeadTop,
        BracoE,
        BracoD,
        AntebracoE,
        AntebracoD,
        MaoE,
        MaoD,
        DedoE,
        DedoD,
        CoxaE,
        CoxaD,
        CanelaE,
        CanelaD,
        PeE,
        PeD,
        BolaE,
        BolaD,
    }

    /// <summary>
    /// Esqueleto do personagem sentado na moto. Pose feita por código (sem Animator e sem clipes): a cada quadro volta os ossos
    /// à pose de referência (T) e aplica a inclinação do tronco, IK de dois ossos nos braços (até as manoplas) e nas pernas
    /// (até as pedaleiras ou o chão). Os dedos ficam fechados na pose de referência, em volta da manopola.
    /// </summary>
    [System.Serializable]
    public sealed class RiderRig
    {
        private static readonly string[] Nomes =
        {
            "Hips", "Spine", "Spine1", "Spine2", "Neck", "Head", "HeadTop_End",
            "LeftArm", "RightArm", "LeftForeArm", "RightForeArm", "LeftHand", "RightHand", "LeftHandMiddle1", "RightHandMiddle1",
            "LeftUpLeg", "RightUpLeg", "LeftLeg", "RightLeg", "LeftFoot", "RightFoot", "LeftToeBase", "RightToeBase",
        };

        [SerializeField] private Transform _raiz;
        [SerializeField] private Transform[] _ossos = new Transform[23];
        [SerializeField] private Quaternion[] _referencia = new Quaternion[23];
        [SerializeField] private Quaternion[] _referenciaNaRaiz = new Quaternion[23];
        [SerializeField] private float _alturaTornozelo;
        [SerializeField] private float _bolaDoPe;
        [SerializeField] private float _comprimentoMao;
        [SerializeField] private Vector3[] _direcaoDedo = new Vector3[2];

        public Transform Raiz => _raiz;

        public Transform Cabeca => _ossos[(int)Osso.Head];

        public Transform Pegar(Osso osso) => _ossos[(int)osso];

        public bool Valido => _raiz != null && _ossos != null && _ossos[0] != null;

        /// <summary>Altura do tornozelo acima da sola, na escala atual.</summary>
        public float AlturaTornozelo => _alturaTornozelo;

        public float BolaDoPe => _bolaDoPe;

        /// <summary>Acha os ossos pelo nome, fecha os dedos e guarda a pose de referência. A raiz deve estar na pose T, sem escala nem giro.</summary>
        public static RiderRig Montar(Transform raiz)
        {
            var rig = new RiderRig { _raiz = raiz };
            var byName = new Dictionary<string, Transform>();

            foreach (Transform t in raiz.GetComponentsInChildren<Transform>(true))
            {
                string name = t.name;
                int colon = name.LastIndexOf(':');
                byName[colon >= 0 ? name.Substring(colon + 1) : name] = t;
            }

            for (int i = 0; i < Nomes.Length; i++)
            {
                byName.TryGetValue(Nomes[i], out rig._ossos[i]);
            }

            rig.FecharDedos(byName, "Left");
            rig.FecharDedos(byName, "Right");

            for (int i = 0; i < rig._ossos.Length; i++)
            {
                if (rig._ossos[i] == null)
                {
                    continue;
                }

                rig._referencia[i] = rig._ossos[i].localRotation;
                rig._referenciaNaRaiz[i] = Quaternion.Inverse(raiz.rotation) * rig._ossos[i].rotation;
            }

            Transform foot = rig._ossos[(int)Osso.PeE];
            Transform toes = rig._ossos[(int)Osso.BolaE];
            Transform hand = rig._ossos[(int)Osso.MaoE];
            Transform finger = rig._ossos[(int)Osso.DedoE];

            if (foot != null && toes != null)
            {
                rig._alturaTornozelo = raiz.InverseTransformPoint(foot.position).y;
                rig._bolaDoPe = raiz.InverseTransformPoint(toes.position).z - raiz.InverseTransformPoint(foot.position).z;
            }

            if (hand != null && finger != null)
            {
                rig._comprimentoMao = Vector3.Distance(hand.position, finger.position);
            }

            for (int side = 0; side < 2; side++)
            {
                Transform h = rig._ossos[(int)(side == 0 ? Osso.MaoE : Osso.MaoD)];
                Transform f = rig._ossos[(int)(side == 0 ? Osso.DedoE : Osso.DedoD)];

                if (h != null && f != null)
                {
                    rig._direcaoDedo[side] = raiz.InverseTransformDirection((f.position - h.position).normalized);
                }
            }

            return rig;
        }

        private void FecharDedos(Dictionary<string, Transform> byName, string side)
        {
            foreach (string finger in new[] { "Index", "Middle", "Ring", "Pinky" })
            {
                float[] angles = { 62f, 78f, 55f };

                for (int k = 1; k <= 3; k++)
                {
                    if (!byName.TryGetValue($"{side}Hand{finger}{k}", out Transform bone) || !byName.TryGetValue($"{side}Hand{finger}{k + 1}", out Transform next))
                    {
                        continue;
                    }

                    Vector3 dir = (next.position - bone.position).normalized;
                    Vector3 axis = Vector3.Cross(dir, Vector3.down).normalized;
                    bone.rotation = Quaternion.AngleAxis(angles[k - 1], axis) * bone.rotation;
                }
            }

            if (byName.TryGetValue($"{side}HandThumb1", out Transform thumb) && byName.TryGetValue($"{side}HandThumb2", out Transform thumbNext))
            {
                Vector3 dir = (thumbNext.position - thumb.position).normalized;
                thumb.rotation = Quaternion.AngleAxis(28f, Vector3.Cross(dir, Vector3.down).normalized) * thumb.rotation;
            }
        }

        /// <summary>
        /// Monta a pose: inclinação do tronco (graus, para frente), manoplas e pedaleiras em coordenadas de mundo, e o pé esquerdo no
        /// chão (stop = 1 baixa o pé esquerdo até groundLeft, o ponto no chão onde fica a bola do pé).
        /// </summary>
        public void Posicionar(Quaternion motoRot, float pitch, float headPitch, Vector3 gripL, Vector3 gripR, Vector3 pegL, Vector3 pegR, Vector3 groundL, float stop, float cabecaRoll = 0f)
        {
            if (!Valido)
            {
                return;
            }

            for (int i = 0; i < _ossos.Length; i++)
            {
                if (_ossos[i] != null && i != (int)Osso.Hips)
                {
                    _ossos[i].localRotation = _referencia[i];
                }
            }

            // Tronco: a inclinação se divide em três ossos; o pescoço e a cabeça devolvem parte, para o olhar seguir à frente.
            Vector3 right = motoRot * Vector3.right;
            Girar(Osso.Spine, pitch * 0.34f, right);
            Girar(Osso.Spine1, pitch * 0.33f, right);
            Girar(Osso.Spine2, pitch * 0.33f, right);
            Girar(Osso.Neck, -pitch * 0.35f + headPitch * 0.5f, right);
            Girar(Osso.Head, -pitch * 0.45f + headPitch * 0.5f, right);

            // Em curva a cabeça (e um pouco do pescoço) desfaz parte da inclinação em volta do eixo da moto: o olhar fica nivelado.
            Vector3 frente = motoRot * Vector3.forward;
            Girar(Osso.Neck, cabecaRoll * 0.4f, frente);
            Girar(Osso.Head, cabecaRoll * 0.6f, frente);

            // Braços.
            for (int side = 0; side < 2; side++)
            {
                float sx = side == 0 ? -1f : 1f;
                Transform upper = _ossos[(int)(side == 0 ? Osso.BracoE : Osso.BracoD)];
                Transform lower = _ossos[(int)(side == 0 ? Osso.AntebracoE : Osso.AntebracoD)];
                Transform hand = _ossos[(int)(side == 0 ? Osso.MaoE : Osso.MaoD)];

                if (upper == null || lower == null || hand == null)
                {
                    continue;
                }

                // A mão agarra por cima: o punho fica atrás e um pouco acima da manopola.
                Vector3 fingers = motoRot * new Vector3(-sx * 0.12f, -0.05f, 1f).normalized;
                Vector3 grip = side == 0 ? gripL : gripR;
                Vector3 wrist = grip - fingers * (_comprimentoMao * 0.5f) + motoRot * new Vector3(0f, 0.032f, 0f);
                Vector3 pole = motoRot * new Vector3(sx * 0.85f, -0.55f, -0.35f);
                TwoBone(upper, lower, hand, wrist, pole);

                // Orientação da mão: a de referência (palma para baixo, dedos para fora) girada até os dedos apontarem para frente.
                Transform finger = _ossos[(int)(side == 0 ? Osso.DedoE : Osso.DedoD)];

                if (finger != null)
                {
                    Quaternion bind = _raiz.rotation * _referenciaNaRaiz[(int)(side == 0 ? Osso.MaoE : Osso.MaoD)];
                    Vector3 bindDirection = _raiz.TransformDirection(_direcaoDedo[side]);
                    hand.rotation = Quaternion.FromToRotation(bindDirection, fingers) * bind;
                }
            }

            // Pernas.
            Quaternion footFrame = motoRot * Quaternion.Euler(8f, 0f, 0f);

            for (int side = 0; side < 2; side++)
            {
                float sx = side == 0 ? -1f : 1f;
                Transform upper = _ossos[(int)(side == 0 ? Osso.CoxaE : Osso.CoxaD)];
                Transform lower = _ossos[(int)(side == 0 ? Osso.CanelaE : Osso.CanelaD)];
                Transform foot = _ossos[(int)(side == 0 ? Osso.PeE : Osso.PeD)];

                if (upper == null || lower == null || foot == null)
                {
                    continue;
                }

                float scale = _raiz.lossyScale.x;
                Vector3 peg = side == 0 ? pegL : pegR;
                Vector3 ankle = peg + footFrame * new Vector3(0f, _alturaTornozelo * scale, -_bolaDoPe * scale);
                Quaternion rotation = footFrame;

                if (side == 0 && stop > 0f)
                {
                    Vector3 groundAnkle = new Vector3(groundL.x, groundL.y + _alturaTornozelo * scale + 0.004f, groundL.z - _bolaDoPe * scale);
                    ankle = Vector3.Lerp(ankle, groundAnkle, stop);
                    rotation = Quaternion.Slerp(footFrame, Quaternion.Euler(0f, motoRot.eulerAngles.y, 0f), stop);
                }

                Vector3 pole = motoRot * new Vector3(sx * 0.35f, 0.3f, 1f);
                TwoBone(upper, lower, foot, ankle, pole);
                foot.rotation = rotation * _referenciaNaRaiz[(int)(side == 0 ? Osso.PeE : Osso.PeD)];
            }
        }

        private void Girar(Osso osso, float graus, Vector3 eixo)
        {
            Transform t = _ossos[(int)osso];

            if (t != null)
            {
                t.rotation = Quaternion.AngleAxis(graus, eixo) * t.rotation;
            }
        }

        /// <summary>IK analítico de dois ossos sem esticar o osso: gira o osso de cima e o de baixo até a ponta chegar em target (com o cotovelo/joelho para o lado de pole).</summary>
        private static void TwoBone(Transform upper, Transform lower, Transform end, Vector3 target, Vector3 pole)
        {
            Vector3 p0 = upper.position;
            Vector3 p1 = lower.position;
            Vector3 p2 = end.position;
            float a = Vector3.Distance(p0, p1);
            float b = Vector3.Distance(p1, p2);
            Vector3 d = target - p0;
            float dist = Mathf.Clamp(d.magnitude, Mathf.Abs(a - b) + 0.01f, (a + b) * 0.999f);
            Vector3 dir = d.sqrMagnitude < 1e-8f ? Vector3.down : d.normalized;
            float x = (a * a - b * b + dist * dist) / (2f * dist);
            float h = Mathf.Sqrt(Mathf.Max(0f, a * a - x * x));
            Vector3 perp = pole - dir * Vector3.Dot(pole, dir);
            perp = perp.sqrMagnitude < 1e-6f ? Vector3.up : perp.normalized;
            Vector3 elbow = p0 + dir * x + perp * h;
            Vector3 reach = p0 + dir * dist;

            upper.rotation = Quaternion.FromToRotation(p1 - p0, elbow - p0) * upper.rotation;
            p1 = lower.position;
            p2 = end.position;
            lower.rotation = Quaternion.FromToRotation(p2 - p1, reach - p1) * lower.rotation;
        }
    }
}

using System;
using System.Collections.Generic;
using UnityEngine;

namespace Entregador.Game
{
    /// <summary>Os AudioClips gerados em código (criados na primeira vez que são pedidos e guardados).</summary>
    public static class SoundBank
    {
        private static readonly Dictionary<string, AudioClip> Cache = new Dictionary<string, AudioClip>();

        public static AudioClip Click => Get("click", SoundDesign.Click);

        public static AudioClip Ping => Get("ping", SoundDesign.Ping);

        public static AudioClip Plim => Get("plim", SoundDesign.Plim);

        public static AudioClip RadarClick => Get("radar", SoundDesign.RadarClick);

        public static AudioClip Whoop => Get("whoop", SoundDesign.Whoop);

        public static AudioClip Static => Get("static", SoundDesign.Static);

        public static AudioClip CarHum => Get("carhum", SoundDesign.CarHum, true);

        public static AudioClip Squeal => Get("squeal", SoundDesign.Squeal, true);

        public static AudioClip CityBed => Get("city", SoundDesign.CityBed, true);

        public static AudioClip Rain => Get("rain", SoundDesign.Rain, true);

        public static AudioClip Wind => Get("wind", SoundDesign.Wind, true);

        public static AudioClip Horn(int variant) => Get("horn" + (variant % 3), () => SoundDesign.Horn(400f + (variant % 3) * 28f));

        public static AudioClip Bird(int variant) => Get("bird" + (variant % 3), () => SoundDesign.Bird(variant % 3));

        public static AudioClip Passagem => Get("passagem", RenderPassagem);

        public static AudioClip Oferta(string app) => Get("oferta_" + app, () => SoundDesign.RenderJingle(JingleCatalog.Oferta(app)));

        public static AudioClip Aceito(string app) => Get("aceito_" + app, () => SoundDesign.RenderJingle(JingleCatalog.Aceito(app)));

        public static AudioClip Perdido(string app) => Get("perdido_" + app, () => SoundDesign.RenderJingle(JingleCatalog.Perdido(app)));

        public static AudioClip Get(string name, Func<float[]> make, bool loopable = false)
        {
            if (Cache.TryGetValue(name, out AudioClip clip) && clip != null)
            {
                return clip;
            }

            float[] data = make();
            clip = AudioClip.Create("Jogo_" + name, data.Length, 1, SoundDesign.Rate, false);
            clip.SetData(data, 0);
            Cache[name] = clip;

            return clip;
        }

        /// <summary>Uma moto passando (para o som de fundo da cidade): o motor sintetizado a giro médio, com entrada e saída suaves.</summary>
        private static float[] RenderPassagem()
        {
            var synth = new MotorSynth(SoundDesign.Rate, MotorVoice.Titan, 777u) { Rpm = 5200f, Throttle = 0.55f, Gain = 1f };
            var buf = new float[(int)(5f * SoundDesign.Rate)];
            synth.Render(buf, 0, buf.Length);

            for (int i = 0; i < buf.Length; i++)
            {
                float u = i / (float)buf.Length;
                float s = Mathf.Sin(Mathf.PI * u);
                buf[i] *= s * s;
            }

            SoundDesign.Normalize(buf, 0.7f);

            return buf;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Cache.Clear();
        }
    }
}

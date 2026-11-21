using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Entregador.Game
{
    /// <summary>Qual "visual" de céu e luz está ativo: dia de sol (céu parcialmente nublado), chuva (céu fechado) ou fim de tarde.</summary>
    public enum LookKind
    {
        Sunny,
        Rain,
        Sunset,
    }

    /// <summary>Um conjunto de ajustes de luz e atmosfera (céu HDRI, sol, ambiente, neblina e pós-processamento).</summary>
    [Serializable]
    public sealed class LookSettings
    {
        public Material Skybox;
        public Color SunColor = Color.white;
        public float SunIntensity = 1.5f;
        public Vector3 SunEuler = new Vector3(50f, -30f, 0f);
        public LightShadows Shadows = LightShadows.Soft;
        public float ShadowStrength = 0.9f;
        public float AmbientIntensity = 1f;
        public Color FogColor = new Color(0.7f, 0.8f, 0.9f);
        public float FogStart = 180f;
        public float FogEnd = 520f;
        public VolumeProfile Profile;
    }

    /// <summary>
    /// Aplica o visual do clima: skybox HDRI, sol (direção, cor, sombra), ambiente vindo do céu, neblina, perfil de
    /// pós-processamento, reflexo (ReflectionProbe) e o asfalto molhado na chuva (troca os materiais de chão pelos
    /// molhados). O CityBuilder cria e liga este componente; o GameFlow chama <see cref="Apply"/> a cada dia.
    /// SSAO é um renderer feature desligado por padrão no celular: ligue com <see cref="UseSsao"/>.
    /// </summary>
    public sealed class WeatherLook : MonoBehaviour
    {
        [SerializeField] private LookSettings _sunny;
        [SerializeField] private LookSettings _rain;
        [SerializeField] private LookSettings _sunset;
        [SerializeField] private Light _sun;
        [SerializeField] private Volume _volume;
        [SerializeField] private ReflectionProbe _probe;
        [SerializeField] private Renderer[] _groundRenderers;
        [SerializeField] private Material[] _dry;
        [SerializeField] private Material[] _wet;
        [SerializeField] private ScriptableRendererFeature _ssao;

        /// <summary>Qualidade: SSAO ligado? Padrão desligado (custa caro em celulares médios).</summary>
        public bool UseSsao;

        public LookKind Current { get; private set; } = LookKind.Sunny;

        public void Configure(LookSettings sunny, LookSettings rain, LookSettings sunset, Light sun, Volume volume, ReflectionProbe probe, Renderer[] ground, Material[] dry, Material[] wet, ScriptableRendererFeature ssao)
        {
            _sunny = sunny;
            _rain = rain;
            _sunset = sunset;
            _sun = sun;
            _volume = volume;
            _probe = probe;
            _groundRenderers = ground;
            _dry = dry;
            _wet = wet;
            _ssao = ssao;
        }

        private void Start()
        {
            Apply(Current);
        }

        public void Apply(LookKind kind, bool renderProbe = true)
        {
            Current = kind;
            LookSettings look = kind == LookKind.Rain ? _rain : (kind == LookKind.Sunset ? _sunset : _sunny);

            if (look == null)
            {
                return;
            }

            if (look.Skybox != null)
            {
                RenderSettings.skybox = look.Skybox;
            }

            RenderSettings.ambientMode = AmbientMode.Skybox;
            RenderSettings.ambientIntensity = look.AmbientIntensity;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = look.FogColor;
            RenderSettings.fogStartDistance = look.FogStart;
            RenderSettings.fogEndDistance = look.FogEnd;

            if (_sun != null)
            {
                _sun.color = look.SunColor;
                _sun.intensity = look.SunIntensity;
                _sun.transform.rotation = Quaternion.Euler(look.SunEuler);
                _sun.shadows = look.Shadows;
                _sun.shadowStrength = look.ShadowStrength;
            }

            if (_volume != null && look.Profile != null)
            {
                _volume.profile = look.Profile;
            }

            if (_ssao != null)
            {
                _ssao.SetActive(UseSsao);
            }

            SwapGround(kind == LookKind.Rain);
            DynamicGI.UpdateEnvironment();

            if (_probe != null && renderProbe)
            {
                _probe.RenderProbe();
            }
        }

        private void SwapGround(bool wet)
        {
            if (_groundRenderers == null || _dry == null || _wet == null)
            {
                return;
            }

            Material[] from = wet ? _dry : _wet;
            Material[] to = wet ? _wet : _dry;

            foreach (Renderer renderer in _groundRenderers)
            {
                if (renderer == null)
                {
                    continue;
                }

                Material[] materials = renderer.sharedMaterials;
                bool changed = false;

                for (int i = 0; i < materials.Length; i++)
                {
                    int index = Array.IndexOf(from, materials[i]);

                    if (index >= 0 && index < to.Length)
                    {
                        materials[i] = to[index];
                        changed = true;
                    }
                }

                if (changed)
                {
                    renderer.sharedMaterials = materials;
                }
            }
        }
    }
}

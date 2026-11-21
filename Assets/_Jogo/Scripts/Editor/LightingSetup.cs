using System.IO;
using Entregador.Game;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Entregador.Editor
{
    /// <summary>
    /// Luz e atmosfera por clima: skybox HDRI (Poly Haven), sol alinhado ao sol do HDRI, perfis de pós-processamento (ACES,
    /// bloom leve, vinheta, tom quente), neblina na cor do horizonte do céu, ReflectionProbe e o componente WeatherLook.
    /// </summary>
    public static class LightingSetup
    {
        private const string HdriFolder = "Assets/ThirdParty/PolyHaven/HDRI/";
        private const string SettingsFolder = "Assets/_Jogo/Settings/";

        private struct HdriInfo
        {
            public Vector3 SunDirection;
            public Color Horizon;
            public Color Zenith;
        }

        /// <summary>Lê o .hdr (Radiance RGBE) e acha a direção do pixel mais brilhante (o sol) e a cor média do horizonte.</summary>
        private static HdriInfo Analyze(string path)
        {
            byte[] data = File.ReadAllBytes(path);
            int pos = 0;
            int width = 0;
            int height = 0;

            while (pos < data.Length)
            {
                int start = pos;

                while (data[pos] != '\n')
                {
                    pos++;
                }

                string line = System.Text.Encoding.ASCII.GetString(data, start, pos - start);
                pos++;

                if (line.StartsWith("-Y") || line.StartsWith("+Y"))
                {
                    string[] parts = line.Split(' ');
                    height = int.Parse(parts[1]);
                    width = int.Parse(parts[3]);

                    break;
                }
            }

            var rgbe = new byte[width * 4];
            var luminance = new float[width * height];
            var color = new Color[width * height];

            for (int y = 0; y < height; y++)
            {
                // Scanline com RLE por canal (formato novo): 2 2 hi lo.
                pos += 4;

                for (int channel = 0; channel < 4; channel++)
                {
                    int x = 0;

                    while (x < width)
                    {
                        int count = data[pos++];

                        if (count > 128)
                        {
                            count -= 128;
                            byte value = data[pos++];

                            for (int k = 0; k < count; k++)
                            {
                                rgbe[(x++) * 4 + channel] = value;
                            }
                        }
                        else
                        {
                            for (int k = 0; k < count; k++)
                            {
                                rgbe[(x++) * 4 + channel] = data[pos++];
                            }
                        }
                    }
                }

                for (int x = 0; x < width; x++)
                {
                    float scale = rgbe[x * 4 + 3] == 0 ? 0f : Mathf.Pow(2f, rgbe[x * 4 + 3] - 136f);
                    var c = new Color(rgbe[x * 4] * scale, rgbe[x * 4 + 1] * scale, rgbe[x * 4 + 2] * scale);
                    color[y * width + x] = c;
                    luminance[y * width + x] = 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;
                }
            }

            // Sol: centroide (ponderado pela luminância) dos pixels acima de 60% do máximo.
            float max = 0f;

            foreach (float l in luminance)
            {
                max = Mathf.Max(max, l);
            }

            Vector3 sum = Vector3.zero;

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    float l = luminance[y * width + x];

                    if (l > max * 0.6f)
                    {
                        sum += Direction(x, y, width, height) * l;
                    }
                }
            }

            // Horizonte: média das linhas perto de 90 graus (levemente acima).
            Color horizon = Color.black;
            int samples = 0;

            for (int y = Mathf.RoundToInt(height * 0.47f); y < Mathf.RoundToInt(height * 0.5f); y++)
            {
                for (int x = 0; x < width; x += 4)
                {
                    horizon += color[y * width + x];
                    samples++;
                }
            }

            horizon /= Mathf.Max(1, samples);
            Color zenith = Color.black;
            samples = 0;

            for (int y = 0; y < height / 8; y++)
            {
                for (int x = 0; x < width; x += 8)
                {
                    zenith += color[y * width + x];
                    samples++;
                }
            }

            zenith /= Mathf.Max(1, samples);

            return new HdriInfo { SunDirection = sum.sqrMagnitude > 0f ? sum.normalized : Vector3.up, Horizon = horizon, Zenith = zenith };
        }

        /// <summary>Direção do pixel (x, y) no mapeamento latitude/longitude do Skybox/Panoramic: u = 0,5 - atan2(z, x) / 2pi.</summary>
        private static Vector3 Direction(int x, int y, int width, int height)
        {
            float u = (x + 0.5f) / width;
            float theta = (y + 0.5f) / height * Mathf.PI;
            float lon = (0.5f - u) * 2f * Mathf.PI;

            return new Vector3(Mathf.Sin(theta) * Mathf.Cos(lon), Mathf.Cos(theta), Mathf.Sin(theta) * Mathf.Sin(lon));
        }

        private static Material Skybox(string file, string name)
        {
            string texturePath = HdriFolder + file;
            var importer = (TextureImporter)AssetImporter.GetAtPath(texturePath);

            if (importer != null && importer.maxTextureSize != 2048)
            {
                importer.textureShape = TextureImporterShape.Texture2D;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.mipmapEnabled = true;
                importer.maxTextureSize = 2048;
                importer.textureCompression = TextureImporterCompression.Compressed;
                var android = importer.GetPlatformTextureSettings("Android");
                android.overridden = true;
                android.maxTextureSize = 1024;
                android.format = TextureImporterFormat.ASTC_HDR_6x6;
                importer.SetPlatformTextureSettings(android);
                importer.SaveAndReimport();
            }

            string path = $"{CityMaterials.Folder}/Ceu_{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);

            if (material == null)
            {
                material = new Material(Shader.Find("Skybox/Panoramic"));
                AssetDatabase.CreateAsset(material, path);
            }

            material.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture>(texturePath));
            material.SetFloat("_Mapping", 1f);
            material.EnableKeyword("_MAPPING_LATITUDE_LONGITUDE_LAYOUT");
            material.SetFloat("_ImageType", 0f);
            material.SetFloat("_Exposure", 1f);
            EditorUtility.SetDirty(material);

            return material;
        }

        private static VolumeProfile Profile(string name, float exposure, float saturation, float contrast, Color filter, float bloom, float vignette)
        {
            string path = $"{SettingsFolder}Pos_{name}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);

            if (existing != null)
            {
                AssetDatabase.DeleteAsset(path);
            }

            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, path);

            var tone = profile.Add<Tonemapping>(false);
            tone.mode.Override(TonemappingMode.ACES);

            var colors = profile.Add<ColorAdjustments>(false);
            colors.postExposure.Override(exposure);
            colors.saturation.Override(saturation);
            colors.contrast.Override(contrast);
            colors.colorFilter.Override(filter);

            var bloomOverride = profile.Add<Bloom>(false);
            bloomOverride.threshold.Override(1.0f);
            bloomOverride.intensity.Override(bloom);
            bloomOverride.scatter.Override(0.55f);

            var vignetteOverride = profile.Add<Vignette>(false);
            vignetteOverride.intensity.Override(vignette);
            vignetteOverride.smoothness.Override(0.45f);

            foreach (VolumeComponent component in profile.components)
            {
                AssetDatabase.AddObjectToAsset(component, profile);
            }

            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();

            return profile;
        }

        /// <summary>Cria sol, volume global, ReflectionProbe e o WeatherLook; aplica o visual de sol. Devolve o sol e o WeatherLook.</summary>
        public static WeatherLook Build(Renderer[] ground, Material[] dry, Material[] wet, out Light sun)
        {
            HdriInfo sunnyInfo = Analyze(HdriFolder + "kloofendal_48d_partly_cloudy_puresky_2k.hdr");
            HdriInfo overcastInfo = Analyze(HdriFolder + "kloofendal_overcast_puresky_2k.hdr");
            HdriInfo sunsetInfo = Analyze(HdriFolder + "belfast_sunset_puresky_2k.hdr");
            Debug.Log($"[LightingSetup] Sol (sunny) {sunnyInfo.SunDirection}, horizonte {sunnyInfo.Horizon}; sunset {sunsetInfo.SunDirection}");

            Material skySunny = Skybox("kloofendal_48d_partly_cloudy_puresky_2k.hdr", "Sol");
            Material skyRain = Skybox("kloofendal_overcast_puresky_2k.hdr", "Chuva");
            Material skySunset = Skybox("belfast_sunset_puresky_2k.hdr", "Tarde");

            var sunny = new LookSettings
            {
                Skybox = skySunny,
                SunColor = new Color(1f, 0.95f, 0.85f),
                SunIntensity = 1.25f,
                SunEuler = Quaternion.LookRotation(-sunnyInfo.SunDirection).eulerAngles,
                Shadows = LightShadows.Soft,
                ShadowStrength = 0.92f,
                AmbientIntensity = 0.7f,
                FogColor = Tone(sunnyInfo.Horizon),
                FogStart = 160f,
                FogEnd = 520f,
                Profile = Profile("Sol", -0.35f, 8f, 10f, new Color(1f, 0.97f, 0.92f), 0.22f, 0.2f),
            };

            var rain = new LookSettings
            {
                Skybox = skyRain,
                SunColor = new Color(0.8f, 0.86f, 0.95f),
                SunIntensity = 0.4f,
                SunEuler = new Vector3(60f, -30f, 0f),
                Shadows = LightShadows.None,
                ShadowStrength = 0f,
                AmbientIntensity = 0.75f,
                FogColor = Tone(overcastInfo.Horizon),
                FogStart = 70f,
                FogEnd = 330f,
                Profile = Profile("Chuva", -0.2f, -8f, 5f, new Color(0.95f, 0.98f, 1f), 0.1f, 0.28f),
            };

            var sunset = new LookSettings
            {
                Skybox = skySunset,
                SunColor = new Color(1f, 0.68f, 0.42f),
                SunIntensity = 1.3f,
                SunEuler = Quaternion.LookRotation(-sunsetInfo.SunDirection).eulerAngles,
                Shadows = LightShadows.Soft,
                ShadowStrength = 0.95f,
                AmbientIntensity = 0.7f,
                FogColor = Tone(sunsetInfo.Horizon),
                FogStart = 140f,
                FogEnd = 480f,
                Profile = Profile("Tarde", -0.3f, 12f, 8f, new Color(1f, 0.93f, 0.85f), 0.3f, 0.25f),
            };

            var sunObject = new GameObject("Sol");
            sun = sunObject.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.shadowBias = 0.05f;
            sun.shadowNormalBias = 0.4f;
            RenderSettings.sun = sun;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
            RenderSettings.subtractiveShadowColor = new Color(0.4f, 0.45f, 0.5f);

            var volumeObject = new GameObject("PosProcessamento");
            var volume = volumeObject.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 1f;
            volume.sharedProfile = sunny.Profile;

            var probeObject = new GameObject("SondaDeReflexo");
            probeObject.transform.position = new Vector3(0f, 12f, 0f);
            var probe = probeObject.AddComponent<ReflectionProbe>();
            probe.mode = ReflectionProbeMode.Realtime;
            probe.refreshMode = ReflectionProbeRefreshMode.ViaScripting;
            probe.timeSlicingMode = ReflectionProbeTimeSlicingMode.NoTimeSlicing;
            probe.resolution = 128;
            probe.size = new Vector3(1400f, 200f, 1400f);
            probe.farClipPlane = 250f;
            probe.intensity = 1f;
            probe.boxProjection = false;
            probe.cullingMask = 0;

            var holder = new GameObject("ClimaVisual");
            var look = holder.AddComponent<WeatherLook>();
            look.Configure(sunny, rain, sunset, sun, volume, probe, ground, dry, wet, RenderSetup.FindSsao());
            look.Apply(LookKind.Sunny, false);

            return look;
        }

        /// <summary>Cor do horizonte do HDR (linear, pode passar de 1) levada para a faixa do monitor, para a neblina.</summary>
        private static Color Tone(Color hdr)
        {
            Color c = new Color(hdr.r / (1f + hdr.r), hdr.g / (1f + hdr.g), hdr.b / (1f + hdr.b)) * 1.6f;
            c.a = 1f;

            return new Color(Mathf.Clamp01(c.r), Mathf.Clamp01(c.g), Mathf.Clamp01(c.b), 1f);
        }
    }
}

using UnityEngine;

namespace Entregador.Editor
{
    /// <summary>
    /// Paleta e materiais da cidade (um lugar só). Reboco = texturas Poly Haven tingidas com cores muted brasileiras;
    /// poucas variantes de material no total (a cena é toda em malhas fundidas por quarteirão).
    /// </summary>
    public static class CityStyle
    {
        private static readonly (string name, Color color)[] PlasterColors =
        {
            ("Ocre", new Color(0.93f, 0.78f, 0.5f)),
            ("Salmao", new Color(0.93f, 0.68f, 0.58f)),
            ("VerdeAgua", new Color(0.68f, 0.84f, 0.76f)),
            ("AzulClaro", new Color(0.66f, 0.78f, 0.9f)),
            ("Amarelo", new Color(0.96f, 0.86f, 0.5f)),
            ("Branco", new Color(0.98f, 0.97f, 0.94f)),
            ("Cinza", new Color(0.85f, 0.85f, 0.83f)),
            ("Rosa", new Color(0.92f, 0.72f, 0.76f)),
            ("Terracota", new Color(0.84f, 0.54f, 0.4f)),
            ("Verde", new Color(0.6f, 0.76f, 0.54f)),
            ("Bege", new Color(0.92f, 0.85f, 0.7f)),
            ("Lilas", new Color(0.78f, 0.72f, 0.88f)),
        };

        private static Material[] _plasters;
        private static Material[] _oldPlasters;

        /// <summary>Reboco pintado novo (painted_plaster_wall tingido).</summary>
        public static Material Plaster(System.Random random)
        {
            if (_plasters == null)
            {
                _plasters = new Material[PlasterColors.Length];

                for (int i = 0; i < _plasters.Length; i++)
                {
                    _plasters[i] = PbrMaterials.Surface("plaster", PlasterColors[i].name, PlasterColors[i].color * 1.12f);
                }
            }

            return _plasters[random.Next(_plasters.Length)];
        }

        /// <summary>Reboco sujo/manchado (white_plaster_02 tingido), para prédios mais velhos.</summary>
        public static Material OldPlaster(System.Random random)
        {
            if (_oldPlasters == null)
            {
                _oldPlasters = new Material[PlasterColors.Length];

                for (int i = 0; i < _oldPlasters.Length; i++)
                {
                    Color c = PlasterColors[i].color * 1.25f;
                    c.a = 1f;
                    _oldPlasters[i] = PbrMaterials.Surface("plaster2", PlasterColors[i].name, c);
                }
            }

            return _oldPlasters[random.Next(_oldPlasters.Length)];
        }

        public static Material Brick() => PbrMaterials.Surface("brick", null, new Color(0.95f, 0.85f, 0.8f));

        public static Material Concrete() => PbrMaterials.Surface("crack", "Laje", new Color(0.8f, 0.8f, 0.78f));

        public static Material ConcreteWall() => PbrMaterials.Surface("concrete", null, new Color(0.9f, 0.9f, 0.88f));

        public static Material GreyPlaster() => PbrMaterials.Surface("grey", "Torre", new Color(1.1f, 1.1f, 1.08f));

        public static Material PoleConcrete() => PbrMaterials.Surface("grey", "Poste", new Color(0.78f, 0.78f, 0.76f));

        public static Material ClayRoof() => PbrMaterials.Surface("roof");

        public static Material Fibrocement() => PbrMaterials.Surface("iron", "Fibro", new Color(0.78f, 0.78f, 0.76f));

        public static Material RollUp(int variant)
        {
            Color[] colors =
            {
                new Color(0.62f, 0.66f, 0.7f), new Color(0.55f, 0.62f, 0.56f), new Color(0.7f, 0.55f, 0.5f), new Color(0.6f, 0.6f, 0.62f),
            };

            return PbrMaterials.Surface("rust", "Enrolar" + (variant % colors.Length), colors[variant % colors.Length]);
        }

        public static Material Gate(int variant)
        {
            if (variant % 3 == 0)
            {
                return PbrMaterials.Surface("greenmetal", "Portao");
            }

            Color[] colors =
            {
                new Color(0.25f, 0.4f, 0.65f), new Color(0.2f, 0.2f, 0.22f), new Color(0.85f, 0.85f, 0.82f), new Color(0.5f, 0.3f, 0.2f),
            };

            return PbrMaterials.Surface("rust", "Portao" + variant % colors.Length, colors[variant % colors.Length]);
        }

        public static Material Asphalt(bool wet = false) => PbrMaterials.Surface("asphalt", null, new Color(0.85f, 0.85f, 0.85f), wet);

        public static Material AsphaltRed(bool wet = false) => PbrMaterials.Surface("asphalt", "Corredor", new Color(0.82f, 0.28f, 0.24f), wet);

        public static Material AsphaltPatch() => PbrMaterials.Surface("asphalt", "Remendo", new Color(0.42f, 0.42f, 0.44f), false, 0.5f);

        public static Material Sidewalk(bool wet = false) => PbrMaterials.Surface("sidewalk", null, new Color(0.88f, 0.88f, 0.88f), wet);

        public static Material Paving(bool wet = false) => PbrMaterials.Surface("paving", null, new Color(0.78f, 0.78f, 0.78f), wet);

        public static Material Curb() => PbrMaterials.Surface("concrete", "Guia", new Color(0.75f, 0.75f, 0.73f));

        public static Material Floor() => PbrMaterials.Surface("floor", null, new Color(0.9f, 0.9f, 0.9f));

        public static Material Grass() => PbrMaterials.Surface("grass", null, new Color(1.25f, 1.35f, 0.95f));

        // Cores lisas.
        public static Material Glass() => PbrMaterials.Flat("Vidro", new Color(0.12f, 0.17f, 0.22f), 0.85f);

        public static Material Interior() => PbrMaterials.Flat("Interior", new Color(0.04f, 0.035f, 0.035f), 0.05f);

        public static Material ShopLight() => PbrMaterials.Flat("VitrineClara", new Color(0.35f, 0.4f, 0.45f), 0.85f);

        public static Material Aluminum() => PbrMaterials.Flat("Aluminio", new Color(0.88f, 0.88f, 0.86f), 0.5f, 0.6f);

        public static Material IronBlack() => PbrMaterials.Flat("FerroPreto", new Color(0.07f, 0.07f, 0.08f), 0.4f, 0.5f);

        public static Material Door() => PbrMaterials.Flat("PortaMadeira", new Color(0.38f, 0.24f, 0.14f), 0.3f);

        public static Material Water() => PbrMaterials.Flat("CaixaDagua", new Color(0.12f, 0.35f, 0.75f), 0.6f);

        public static Material AcUnit() => PbrMaterials.Flat("ArCondicionado", new Color(0.9f, 0.9f, 0.9f), 0.4f);

        public static Material Dark() => PbrMaterials.Flat("Escuro", new Color(0.08f, 0.08f, 0.09f), 0.2f);

        public static Material Wire()
        {
            Material material = PbrMaterials.Flat("Fio", new Color(0.03f, 0.03f, 0.03f), 0.3f);
            material.SetFloat("_Cull", 0f);

            return material;
        }

        public static Material PaintWhite() => PbrMaterials.Flat("TintaBranca", new Color(0.88f, 0.88f, 0.85f), 0.25f);

        public static Material PaintYellow() => PbrMaterials.Flat("TintaAmarela", new Color(0.95f, 0.75f, 0.1f), 0.25f);

        public static Material PaintBlack() => PbrMaterials.Flat("TintaPreta", new Color(0.05f, 0.05f, 0.05f), 0.2f);

        public static Material Orange() => PbrMaterials.Flat("LixeiraLaranja", new Color(0.95f, 0.42f, 0.05f), 0.35f);

        public static Material Tire() => PbrMaterials.Flat("Pneu", new Color(0.04f, 0.04f, 0.04f), 0.15f);

        public static Material Bark() => PbrMaterials.Flat("Casca", new Color(0.25f, 0.18f, 0.12f), 0.05f);

        public static Material Leaves(int variant) => PbrMaterials.Foliage("Folhagem" + variant % 3, new[] { new Color(0.85f, 1f, 0.8f), new Color(0.75f, 0.95f, 0.65f), new Color(0.95f, 1f, 0.75f) }[variant % 3]);

        public static Material Awning(int variant)
        {
            Color[] colors =
            {
                new Color(0.75f, 0.12f, 0.1f), new Color(0.12f, 0.4f, 0.2f), new Color(0.15f, 0.25f, 0.6f),
                new Color(0.9f, 0.6f, 0.1f), new Color(0.85f, 0.85f, 0.8f), new Color(0.5f, 0.15f, 0.4f),
            };

            return PbrMaterials.Flat("Toldo" + variant % colors.Length, colors[variant % colors.Length], 0.15f);
        }

        public static Material Board(int variant)
        {
            Color[] colors =
            {
                new Color(0.75f, 0.1f, 0.1f), new Color(0.08f, 0.35f, 0.2f), new Color(0.1f, 0.2f, 0.5f), new Color(0.95f, 0.8f, 0.15f),
                new Color(0.95f, 0.95f, 0.92f), new Color(0.12f, 0.12f, 0.14f), new Color(0.85f, 0.45f, 0.08f),
            };

            return PbrMaterials.Flat("Placa" + variant % colors.Length, colors[variant % colors.Length], 0.3f);
        }

        /// <summary>Cor do texto que contrasta com a placa de índice variant.</summary>
        public static Color BoardText(int variant)
        {
            int v = variant % 7;

            return v == 3 || v == 4 ? new Color(0.1f, 0.1f, 0.12f) : Color.white;
        }

        public static Material Goods(int variant)
        {
            Color[] colors =
            {
                new Color(0.85f, 0.2f, 0.15f), new Color(0.9f, 0.75f, 0.2f), new Color(0.2f, 0.5f, 0.8f), new Color(0.3f, 0.65f, 0.3f), new Color(0.85f, 0.85f, 0.8f),
            };

            return PbrMaterials.Flat("Mercadoria" + variant % colors.Length, colors[variant % colors.Length], 0.2f);
        }

        public static Material Plastic(string name, Color color) => PbrMaterials.Flat("Plastico" + name, color, 0.35f);
    }
}

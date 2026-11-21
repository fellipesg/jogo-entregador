using UnityEngine;

namespace Entregador.Game
{
    /// <summary>Modelos de moto jogáveis (nomes fictícios; inspirados nas motos mais comuns da entrega no Brasil).</summary>
    public enum MotoModelo
    {
        /// <summary>"Titã 160": a street clássica de entrega (farol redondo, tanque reto, banco longo, guidão alto).</summary>
        Titan,

        /// <summary>"Tornado 250": naked esportiva (carenagem pequena angular, tanque esculpido com abas, banco escalonado).</summary>
        Twister,
    }

    /// <summary>Cor da pintura (tanque, laterais e rabeta).</summary>
    public enum MotoCor
    {
        Vermelha,
        Preta,
        Azul,
        Branca,
    }

    /// <summary>Os cinco personagens jogáveis. A tela de seleção (rodada futura) só precisa chamar MotoVisual.Configurar.</summary>
    public enum Personagem
    {
        Jefinho,
        Robsao,
        Marcao,
        SeuValdir,
        Camila,
    }

    /// <summary>Parâmetros de corpo e cores de um personagem.</summary>
    public sealed class PersonagemConfig
    {
        public string Nome;

        /// <summary>Escala geral dos membros (1 = adulto de 1,75 m).</summary>
        public float Escala = 1f;

        /// <summary>Multiplicador da largura de ombros e quadril.</summary>
        public float Ombros = 1f;

        /// <summary>Multiplicador da profundidade do tronco.</summary>
        public float Peito = 1f;

        /// <summary>Profundidade extra na cintura, em metros.</summary>
        public float Barriga;

        /// <summary>Inclinação do tronco para frente, em graus.</summary>
        public float Inclinacao = 14f;

        /// <summary>Quanto a cabeça avança em relação ao pescoço (postura curvada), em metros.</summary>
        public float CabecaFrente;

        /// <summary>Deslocamento do quadril no banco, em metros (positivo = para frente).</summary>
        public float QuadrilZ;

        public Color Jaqueta;
        public Color Moletom;
        public bool TemCapuz;
        public Color Jeans;
        public Color Tenis;
        public Color Capacete;
        public Color CapaceteDetalhe;
        public Color Luva;
        public Color Pele;
        public Color Cabelo;
        public bool RaboDeCavalo;
    }

    /// <summary>Catálogo: nomes de exibição e parâmetros de cada moto, cor e personagem.</summary>
    public static class MotoCatalogo
    {
        public static string Nome(MotoModelo modelo) => modelo == MotoModelo.Titan ? "Titã 160" : "Tornado 250";

        public static Color Pintura(MotoCor cor)
        {
            switch (cor)
            {
                case MotoCor.Preta: return new Color(0.04f, 0.04f, 0.05f);
                case MotoCor.Azul: return new Color(0.07f, 0.18f, 0.55f);
                case MotoCor.Branca: return new Color(0.9f, 0.9f, 0.88f);
                default: return new Color(0.72f, 0.05f, 0.06f);
            }
        }

        /// <summary>Cor secundária (abas do tanque, detalhes), que contrasta com a pintura.</summary>
        public static Color Detalhe(MotoCor cor)
        {
            switch (cor)
            {
                case MotoCor.Preta: return new Color(0.7f, 0.05f, 0.06f);
                case MotoCor.Branca: return new Color(0.07f, 0.07f, 0.08f);
                default: return new Color(0.88f, 0.88f, 0.86f);
            }
        }

        public static PersonagemConfig Config(Personagem personagem)
        {
            switch (personagem)
            {
                case Personagem.Robsao:
                    return new PersonagemConfig
                    {
                        Nome = "Robsão", Escala = 1.02f, Ombros = 1.3f, Peito = 1.28f, Barriga = 0.075f, Inclinacao = 9f, QuadrilZ = -0.04f,
                        Jaqueta = new Color(0.1f, 0.3f, 0.2f), Jeans = new Color(0.1f, 0.1f, 0.13f), Tenis = new Color(0.08f, 0.08f, 0.09f),
                        Capacete = new Color(0.05f, 0.05f, 0.06f), CapaceteDetalhe = new Color(0.8f, 0.8f, 0.2f), Luva = new Color(0.07f, 0.07f, 0.08f),
                        Pele = new Color(0.55f, 0.36f, 0.26f), Cabelo = new Color(0.05f, 0.04f, 0.04f),
                    };
                case Personagem.Marcao:
                    return new PersonagemConfig
                    {
                        Nome = "Marcão", Escala = 1f, Ombros = 1.18f, Peito = 1.12f, Barriga = 0.025f, Inclinacao = 12f, QuadrilZ = -0.02f,
                        Jaqueta = new Color(0.27f, 0.3f, 0.2f), Jeans = new Color(0.12f, 0.18f, 0.32f), Tenis = new Color(0.45f, 0.3f, 0.15f),
                        Capacete = new Color(0.85f, 0.3f, 0.06f), CapaceteDetalhe = new Color(0.05f, 0.05f, 0.06f), Luva = new Color(0.1f, 0.1f, 0.1f),
                        Pele = new Color(0.45f, 0.3f, 0.22f), Cabelo = new Color(0.45f, 0.3f, 0.22f),
                    };
                case Personagem.SeuValdir:
                    return new PersonagemConfig
                    {
                        Nome = "Seu Valdir", Escala = 0.97f, Ombros = 0.92f, Peito = 1f, Barriga = 0.03f, Inclinacao = 27f, CabecaFrente = 0.05f,
                        Jaqueta = new Color(0.52f, 0.5f, 0.44f), Jeans = new Color(0.3f, 0.3f, 0.33f), Tenis = new Color(0.3f, 0.2f, 0.12f),
                        Capacete = new Color(0.82f, 0.78f, 0.66f), CapaceteDetalhe = new Color(0.3f, 0.3f, 0.3f), Luva = new Color(0.35f, 0.3f, 0.25f),
                        Pele = new Color(0.7f, 0.55f, 0.45f), Cabelo = new Color(0.8f, 0.8f, 0.8f),
                    };
                case Personagem.Camila:
                    return new PersonagemConfig
                    {
                        Nome = "Camila", Escala = 0.95f, Ombros = 0.8f, Peito = 0.82f, Inclinacao = 14f, RaboDeCavalo = true,
                        Jaqueta = new Color(0.4f, 0.16f, 0.5f), Jeans = new Color(0.15f, 0.2f, 0.36f), Tenis = new Color(0.9f, 0.9f, 0.92f),
                        Capacete = new Color(0.92f, 0.55f, 0.7f), CapaceteDetalhe = new Color(0.95f, 0.95f, 0.95f), Luva = new Color(0.12f, 0.1f, 0.14f),
                        Pele = new Color(0.65f, 0.45f, 0.33f), Cabelo = new Color(0.12f, 0.07f, 0.05f),
                    };
                default:
                    return new PersonagemConfig
                    {
                        Nome = "Jefinho", Escala = 1f, Ombros = 0.86f, Peito = 0.84f, Inclinacao = 15f, TemCapuz = true,
                        Jaqueta = new Color(0.07f, 0.08f, 0.1f), Moletom = new Color(0.45f, 0.46f, 0.5f), Jeans = new Color(0.14f, 0.22f, 0.42f),
                        Tenis = new Color(0.92f, 0.92f, 0.92f), Capacete = new Color(0.9f, 0.9f, 0.92f), CapaceteDetalhe = new Color(0.95f, 0.45f, 0.05f),
                        Luva = new Color(0.1f, 0.1f, 0.1f), Pele = new Color(0.5f, 0.33f, 0.24f), Cabelo = new Color(0.05f, 0.04f, 0.04f),
                    };
            }
        }
    }
}

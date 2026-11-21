using System;
using System.Collections.Generic;

namespace Entregador.Core
{
    public enum AbordagemMotivo
    {
        Blitz,
        Flagrante,
    }

    public enum AbordagemChoice
    {
        Parar,
        Fugir,
    }

    public enum AbordagemDesfecho
    {
        /// <summary>Documentos em ordem: só o atraso.</summary>
        Liberado,
        LiberadoComMulta,
        MotoNoPatio,
        MotoApreendida,
        Escapou,
    }

    /// <summary>Uma abordagem em andamento: a cena mostra as escolhas e a sessão resolve.</summary>
    public sealed class Abordagem
    {
        public Abordagem(AbordagemMotivo motivo, Infracao? flagrante, MotoOrigem origem, bool cnhValida)
        {
            Motivo = motivo;
            Flagrante = flagrante;
            Origem = origem;
            CnhValida = cnhValida;
            Choices = new[] { AbordagemChoice.Parar, AbordagemChoice.Fugir };
        }

        public AbordagemMotivo Motivo { get; }

        /// <summary>A infração que a polícia viu, no caso de flagrante.</summary>
        public Infracao? Flagrante { get; }

        public MotoOrigem Origem { get; }

        public bool CnhValida { get; }

        public IReadOnlyList<AbordagemChoice> Choices { get; }
    }

    /// <summary>O efeito da escolha. A sessão aplica nas multas, na CNH, na moto, no dia e na entrega.</summary>
    public sealed class AbordagemOutcome
    {
        /// <summary>Infrações autuadas agora (inclui a do flagrante, a falta de CNH, a fuga etc.).</summary>
        public readonly List<Infracao> Infracoes = new List<Infracao>();

        public int DelayMinutes;

        /// <summary>Moto levada ao pátio (alugada): dá para recuperar pagando.</summary>
        public bool MotoNoPatio;

        /// <summary>Moto apreendida de vez (bob, sem documentos).</summary>
        public bool MotoApreendida;

        public bool Escapou;

        /// <summary>Com escapada, a placa foi anotada: se a moto é alugada, chega uma notificação depois.</summary>
        public bool PlacaAnotada;

        public AbordagemDesfecho Desfecho;

        public bool MotoRetida => MotoNoPatio || MotoApreendida;

        /// <summary>Moto retida encerra o dia e cancela a entrega em andamento.</summary>
        public bool EncerraDia => MotoRetida;

        public bool CancelsDelivery => MotoRetida;
    }

    /// <summary>Sorteia blitz e resolve as abordagens da polícia.</summary>
    public sealed class AbordagemSystem
    {
        private readonly TransitoConfig _config;

        public AbordagemSystem(TransitoConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
        }

        /// <summary>Sorteia se há blitz no dia. O dia 1 é segunda: dias 6 e 7 (fim de semana) têm chance maior.</summary>
        public bool RollBlitz(IGameRandom random, int day)
        {
            bool fimDeSemana = ((day - 1) % 7) >= 5;

            return random.Chance(fimDeSemana ? _config.BlitzChanceFimDeSemana : _config.BlitzChance);
        }

        public Abordagem Iniciar(MotoOrigem origem, bool cnhValida, Infracao? flagrante = null) =>
            new Abordagem(flagrante.HasValue ? AbordagemMotivo.Flagrante : AbordagemMotivo.Blitz, flagrante, origem, cnhValida);

        public AbordagemOutcome Resolve(Abordagem abordagem, AbordagemChoice choice, IGameRandom random)
        {
            if (abordagem == null)
            {
                throw new ArgumentNullException(nameof(abordagem));
            }

            if (choice == AbordagemChoice.Parar)
            {
                return Parar(abordagem, random, true);
            }

            // Fugir: primeiro o sorteio de escapar; só então, se a moto é alugada, o de a placa ter sido anotada.
            if (random.Chance(_config.FugaEscapaChance))
            {
                var escapou = new AbordagemOutcome { Escapou = true, Desfecho = AbordagemDesfecho.Escapou };

                if (abordagem.Origem == MotoOrigem.Alugada && random.Chance(_config.PlacaAnotadaChance))
                {
                    escapou.PlacaAnotada = true;
                }

                return escapou;
            }

            // Pego: tudo que uma parada daria, mais a desobediência, moto removida de qualquer jeito e muito atraso.
            AbordagemOutcome pego = Parar(abordagem, random, false);
            pego.Infracoes.Add(Infracao.FugaDeAbordagem);
            pego.DelayMinutes = _config.FugaPegaDelayMinutos;
            Reter(pego, abordagem.Origem);

            return pego;
        }

        private AbordagemOutcome Parar(Abordagem abordagem, IGameRandom random, bool sorteiaAcaso)
        {
            var outcome = new AbordagemOutcome { Desfecho = AbordagemDesfecho.Liberado };

            if (abordagem.Flagrante.HasValue)
            {
                outcome.Infracoes.Add(abordagem.Flagrante.Value);
            }

            if (!abordagem.CnhValida)
            {
                outcome.Infracoes.Add(Infracao.DirigirSemCnh);
                Reter(outcome, abordagem.Origem);
            }

            if (abordagem.Origem == MotoOrigem.Bob)
            {
                outcome.Infracoes.Add(Infracao.MotoSemLicenciamento);
                Reter(outcome, abordagem.Origem);
            }

            if (outcome.MotoRetida)
            {
                return outcome;
            }

            if (sorteiaAcaso)
            {
                outcome.DelayMinutes = random.Range(_config.ParadaDelayMinMinutos, _config.ParadaDelayMaxMinutos + 1);

                if (!abordagem.Flagrante.HasValue && random.Chance(_config.ProblemaMenorChance))
                {
                    outcome.Infracoes.Add(Infracao.EquipamentoIrregular);
                }
            }

            outcome.Desfecho = outcome.Infracoes.Count > 0 ? AbordagemDesfecho.LiberadoComMulta : AbordagemDesfecho.Liberado;

            return outcome;
        }

        private static void Reter(AbordagemOutcome outcome, MotoOrigem origem)
        {
            if (origem == MotoOrigem.Bob)
            {
                outcome.MotoApreendida = true;
                outcome.MotoNoPatio = false;
                outcome.Desfecho = AbordagemDesfecho.MotoApreendida;
            }
            else
            {
                outcome.MotoNoPatio = true;
                outcome.Desfecho = AbordagemDesfecho.MotoNoPatio;
            }
        }
    }
}

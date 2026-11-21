using System.Collections.Generic;

namespace Entregador.Core
{
    public enum IncidentKind
    {
        PneuFurado,
        ClienteDizQueNaoRecebeu,
    }

    public enum IncidentChoice
    {
        EmpurrarAteBorracharia,
        VerAnuncio,
        CancelarEntrega,
        MostrarFoto,
        Contestar,
        Aceitar,
    }

    public sealed class Incident
    {
        public Incident(IncidentKind kind, IReadOnlyList<IncidentChoice> choices)
        {
            Kind = kind;
            Choices = choices;
        }

        public IncidentKind Kind { get; }

        public IReadOnlyList<IncidentChoice> Choices { get; }
    }

    /// <summary>O efeito da escolha. A sessão aplica no dinheiro, na nota e no app.</summary>
    public sealed class IncidentOutcome
    {
        public Money Cost = Money.Zero;
        public int DelayMinutes;
        public bool CancelsDelivery;
        public bool LosesPay;
        public int? ForcedStars;
        public bool RequiresRewardedAd;
    }

    /// <summary>
    /// Sorteia e resolve os imprevistos da fatia. A chuva não está aqui: ela é do dia, não da corrida (ver Weather).
    /// </summary>
    public sealed class IncidentSystem
    {
        public double FlatTireChance = 0.08;
        public double BumpyRoadMultiplier = 2;
        public double NotReceivedChance = 0.05;
        public double ContestWinChance = 0.5;

        public int PushToShopDelayMinutes = 15;
        public int AdRepairDelayMinutes = 5;

        public Incident RollDuringRun(IGameRandom random, bool bumpyRoad)
        {
            double chance = FlatTireChance * (bumpyRoad ? BumpyRoadMultiplier : 1);

            if (!random.Chance(chance))
            {
                return null;
            }

            return new Incident(IncidentKind.PneuFurado, new[]
            {
                IncidentChoice.EmpurrarAteBorracharia,
                IncidentChoice.VerAnuncio,
                IncidentChoice.CancelarEntrega,
            });
        }

        /// <summary>Sem foto da entrega, a opção de mostrar a foto não aparece.</summary>
        public Incident RollAfterDelivery(IGameRandom random, bool photoTaken)
        {
            if (!random.Chance(NotReceivedChance))
            {
                return null;
            }

            var choices = new List<IncidentChoice>();

            if (photoTaken)
            {
                choices.Add(IncidentChoice.MostrarFoto);
            }

            choices.Add(IncidentChoice.Contestar);
            choices.Add(IncidentChoice.Aceitar);

            return new Incident(IncidentKind.ClienteDizQueNaoRecebeu, choices);
        }

        public IncidentOutcome Resolve(Incident incident, IncidentChoice choice, Money flatTireRepair, IGameRandom random)
        {
            if (!ContainsChoice(incident, choice))
            {
                throw new System.ArgumentException($"Escolha {choice} não existe para {incident.Kind}.", nameof(choice));
            }

            switch (choice)
            {
                case IncidentChoice.EmpurrarAteBorracharia:
                    return new IncidentOutcome { Cost = flatTireRepair, DelayMinutes = PushToShopDelayMinutes };

                case IncidentChoice.VerAnuncio:
                    return new IncidentOutcome { DelayMinutes = AdRepairDelayMinutes, RequiresRewardedAd = true };

                case IncidentChoice.CancelarEntrega:
                    return new IncidentOutcome { CancelsDelivery = true };

                case IncidentChoice.MostrarFoto:
                    return new IncidentOutcome();

                case IncidentChoice.Contestar:
                    return random.Chance(ContestWinChance)
                        ? new IncidentOutcome()
                        : new IncidentOutcome { LosesPay = true };

                default:
                    return new IncidentOutcome { LosesPay = true, ForcedStars = 1 };
            }
        }

        private static bool ContainsChoice(Incident incident, IncidentChoice choice)
        {
            foreach (IncidentChoice available in incident.Choices)
            {
                if (available == choice)
                {
                    return true;
                }
            }

            return false;
        }
    }
}

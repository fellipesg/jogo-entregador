using System.Collections.Generic;
using System.Text;
using Entregador.Core;
using UnityEngine;

namespace Entregador.Game
{
    /// <summary>
    /// Parte do GameFlow sobre o perfil do jogador: tela de novo jogo (personagem, moto, CNH, modelo), a economia que dela resulta,
    /// as opções de quem ficou sem moto (pátio, apreensão) e os textos de multas, CNH e moto nos relatórios.
    /// </summary>
    public sealed partial class GameFlow
    {
        private static readonly Personagem[] Personagens =
        {
            Personagem.Jefinho, Personagem.Robsao, Personagem.Marcao, Personagem.SeuValdir, Personagem.Camila,
        };

        private PerfilJogador _perfil = new PerfilJogador();
        private int _multasVistas;

        /// <summary>
        /// Abre a tela de novo jogo ao iniciar o app. Os testes de PlayMode desligam (RuntimeInitializeOnLoadMethod) para cair direto no dia 1.
        /// </summary>
        public static bool ShowSetupOnStart = true;

        public PerfilJogador Perfil => _perfil;

        // ---------- Tela de novo jogo ----------

        /// <summary>Mostra a tela de novo jogo. Cada botão de opção troca o valor e redesenha o painel.</summary>
        public void ShowSetup()
        {
            State = FlowState.Setup;
            EnterIdle();
            CloseBlitz();
            ApplyProfile();

            if (_followCamera != null)
            {
                _followCamera.Snap();
            }

            TransitoConfig t = new EconomyConfig().Transito;
            var body = new StringBuilder();
            body.AppendLine("Escolha como você começa a semana de entregas.");
            body.AppendLine();

            if (_perfil.Origem == MotoOrigem.Alugada)
            {
                Money rent = EconomyForProfile().WeeklyRent;
                Money deposit = EconomyForProfile().RentalDeposit;
                body.AppendLine($"Moto alugada, com documentos: caução {deposit} e aluguel {rent} por semana. Segura contra blitz.");
            }
            else
            {
                body.AppendLine($"Moto bob: entrada {t.BobEntrada} e parcela {t.BobParcelaSemanal} por semana. Barata, mas sem documentos: se a polícia parar, você perde a moto.");
            }

            body.AppendLine(_perfil.TemCnh
                ? "Você tem CNH categoria A."
                : "Sem CNH: qualquer parada da polícia termina com a moto retida e multa pesada.");

            var buttons = new List<PanelButton>
            {
                new PanelButton("Personagem: " + MotoCatalogo.Config(_perfil.Personagem).Nome + "  >", CycleCharacter),
                new PanelButton("Moto: " + (_perfil.Origem == MotoOrigem.Alugada ? "Alugar moto documentada" : "Comprar moto bob") + "  >", ToggleOrigin),
            };

            if (_perfil.Origem == MotoOrigem.Alugada)
            {
                buttons.Add(new PanelButton("Modelo: " + MotoCatalogo.Nome(_perfil.Modelo) + (_perfil.Modelo == MotoModelo.Twister ? " (aluguel mais caro)" : string.Empty) + "  >", ToggleModel));
            }

            buttons.Add(new PanelButton("Tenho CNH: " + (_perfil.TemCnh ? "sim" : "não") + "  >", ToggleCnh));
            buttons.Add(new PanelButton("Começar", StartFromSetup));

            _ui.ShowSetup(BuildSetupData(), buttons.ToArray());
        }

        /// <summary>Define o perfil por código (testes e futuras telas) e atualiza a moto na cena.</summary>
        public void SetProfile(PerfilJogador perfil)
        {
            _perfil = perfil.Clonar();
            ApplyProfile();
        }

        public void CycleCharacter()
        {
            int index = System.Array.IndexOf(Personagens, _perfil.Personagem);
            _perfil.Personagem = Personagens[(index + 1) % Personagens.Length];
            ShowSetup();
        }

        public void ToggleOrigin()
        {
            _perfil.Origem = _perfil.Origem == MotoOrigem.Alugada ? MotoOrigem.Bob : MotoOrigem.Alugada;
            ShowSetup();
        }

        public void ToggleModel()
        {
            _perfil.Modelo = _perfil.Modelo == MotoModelo.Titan ? MotoModelo.Twister : MotoModelo.Titan;
            ShowSetup();
        }

        public void ToggleCnh()
        {
            _perfil.TemCnh = !_perfil.TemCnh;
            ShowSetup();
        }

        public void StartFromSetup()
        {
            if (State != FlowState.Setup)
            {
                return;
            }

            NewWeek(_seed);
            EnterFreeRoamFromSetup();
        }

        /// <summary>Aplica o perfil à moto da cena: física do modelo, visual do modelo, da cor e do personagem.</summary>
        private void ApplyProfile()
        {
            if (_bike == null)
            {
                return;
            }

            MotoModelo modelo = _perfil.ModeloEfetivo;
            _bike.SetModelo(modelo);
            MotoVisual visual = _bike.GetComponentInChildren<MotoVisual>();

            if (visual != null && (visual.Modelo != modelo || visual.Personagem != _perfil.Personagem || visual.Cor != _perfil.Cor))
            {
                visual.Configurar(modelo, _perfil.Cor, _perfil.Personagem);
            }

            if (_input != null && _tuning != null)
            {
                _input.TiltSteering = _tuning.TiltSteering;
            }
        }

        /// <summary>Economia da semana: a de sempre, com aluguel e caução maiores se o jogador alugou o Tornado 250.</summary>
        private EconomyConfig EconomyForProfile()
        {
            var economy = new EconomyConfig();

            if (_perfil.Origem == MotoOrigem.Alugada && _perfil.Modelo == MotoModelo.Twister && _tuning != null)
            {
                economy.WeeklyRent += Money.Reais((decimal)_tuning.TornadoRentExtra);
                economy.RentalDeposit += Money.Reais((decimal)_tuning.TornadoDepositExtra);
            }

            return economy;
        }

        // ---------- Dia sem moto ----------

        private string MotoCostLine()
        {
            if (Session.Rental != null)
            {
                return $"Aluguel da moto: {Session.Rental.WeeklyRent}, cobrado no fim da semana";
            }

            if (Session.ParcelasRestantes > 0)
            {
                return $"Parcela da moto: {Session.Economy.Transito.BobParcelaSemanal} ({Session.ParcelasRestantes} restantes), cobrada no fim da semana";
            }

            return "Sem moto no momento.";
        }

        /// <summary>Moto no pátio: retirar (paga e perde o dia) ou folgar. Moto apreendida: nova moto ou fim da semana.</summary>
        private void AddNoMotoOptions(StringBuilder body, List<PanelButton> buttons)
        {
            if (Session.MotoStatus == MotoStatus.NoPatio)
            {
                Money cost = Session.CustoPatio;
                body.AppendLine();
                body.AppendLine($"Sua moto está no pátio. Retirar custa {cost} (guincho e diárias) e você perde o dia de hoje com a burocracia.");

                if (Session.Wallet.Balance >= cost)
                {
                    buttons.Add(new PanelButton($"Retirar do pátio ({cost})", RetrieveFromYard));
                }
                else
                {
                    body.AppendLine("Você não tem dinheiro para retirar a moto.");
                }

                buttons.Add(new PanelButton("Folgar", TakeDayOff));

                if (Session.Wallet.Balance < cost)
                {
                    buttons.Add(new PanelButton("Encerrar a semana", EndWeekEarly));
                }

                return;
            }

            body.AppendLine();
            body.AppendLine("Sua moto foi apreendida. Sem moto não há entrega: dá para alugar outra (com documentos) ou comprar outra bob.");

            bool any = false;

            if (Session.PodeEscolherNovaMoto(MotoOrigem.Alugada))
            {
                any = true;
                buttons.Add(new PanelButton($"Alugar moto documentada (caução {Session.CustoNovaMoto(MotoOrigem.Alugada)})", () => ChooseNewMoto(MotoOrigem.Alugada)));
            }

            if (Session.PodeEscolherNovaMoto(MotoOrigem.Bob))
            {
                any = true;
                buttons.Add(new PanelButton($"Comprar outra bob (entrada {Session.CustoNovaMoto(MotoOrigem.Bob)})", () => ChooseNewMoto(MotoOrigem.Bob)));
            }

            if (!any)
            {
                body.AppendLine("Você não tem dinheiro nem para a entrada de outra moto: sua semana acaba aqui.");
                buttons.Add(new PanelButton("Encerrar a semana", EndWeekEarly));
            }
            else
            {
                buttons.Add(new PanelButton("Folgar", TakeDayOff));
            }
        }

        public void RetrieveFromYard()
        {
            if (State != FlowState.DayStart || Session.MotoStatus != MotoStatus.NoPatio)
            {
                return;
            }

            if (!Session.RetirarDoPatio())
            {
                _ui.Toast("Dinheiro insuficiente para retirar a moto.");

                return;
            }

            _bike.Teleport(_map.BikeSpawn, _map.BikeSpawnYaw);
            Session.EndDay();
            ShowDayStart();
            _ui.Toast("Moto retirada do pátio. O dia de ontem se perdeu na fila.");
        }

        public void ChooseNewMoto(MotoOrigem origem)
        {
            if (State != FlowState.DayStart || Session.MotoStatus != MotoStatus.Apreendida)
            {
                return;
            }

            if (!Session.EscolherNovaMoto(origem))
            {
                _ui.Toast("Dinheiro insuficiente.");

                return;
            }

            _perfil.Origem = origem;
            ApplyProfile();
            _bike.Teleport(_map.BikeSpawn, _map.BikeSpawnYaw);
            ShowDayStart();
        }

        /// <summary>Sem moto e sem dinheiro: os dias que faltam passam como folga e a semana fecha como SemMoto.</summary>
        public void EndWeekEarly()
        {
            if (State != FlowState.DayStart)
            {
                return;
            }

            while (!Session.IsWeekOver)
            {
                Session.TakeDayOff();
                Session.EndDay();
            }

            ShowDayStart();
        }

        // ---------- Textos de multas, CNH e moto ----------

        /// <summary>Notificações que chegaram desde a última vez que o jogador viu (multas de radar entregues pelo Core, com a taxa da locadora).</summary>
        private void AppendNotifications(StringBuilder body)
        {
            IReadOnlyList<MultaRegistro> paid = Session.MultasPagas;

            for (int i = _multasVistas; i < paid.Count; i++)
            {
                MultaRegistro fine = paid[i];
                string fee = fine.Taxa > Money.Zero ? $" + taxa de indicação {fine.Taxa}" : string.Empty;
                body.AppendLine($"Notificação: {CodigoTransito.Regra(fine.Infracao).Descricao}" + (fine.NoRadar ? " (radar)" : string.Empty) +
                                $", {fine.Valor}{fee}. Infração do dia {fine.DiaInfracao}.");
            }

            _multasVistas = paid.Count;

            if (Session.Cnh.Status == CnhStatus.Suspensa)
            {
                body.AppendLine("Sua CNH está suspensa: qualquer parada da polícia é problema grave.");
            }
        }

        /// <summary>Linhas de trânsito do relatório. Fim do dia: só o que o jogador já sabe. Fim da semana: inclui as multas ainda a chegar.</summary>
        private string TransitoSummary(bool weekEnd)
        {
            var text = new StringBuilder();
            Money paid = Money.Zero;

            foreach (MultaRegistro fine in Session.MultasPagas)
            {
                paid += fine.Total;
            }

            text.AppendLine($"Multas pagas na semana: {paid} ({Session.MultasPagas.Count})");

            if (weekEnd && Session.MultasPendentes.Count > 0)
            {
                text.AppendLine("Multas ainda a chegar:");

                foreach (MultaRegistro fine in Session.MultasPendentes)
                {
                    text.AppendLine($"  {CodigoTransito.Regra(fine.Infracao).Descricao}: {fine.Total}, chega no dia {fine.DiaChegada}");
                }
            }

            text.AppendLine($"CNH: {CnhText(Session.Cnh)}");
            text.AppendLine("Moto: " + MotoStatusText());

            foreach (AbordagemRegistro record in Session.Abordagens)
            {
                if (weekEnd || record.Dia == Session.Day)
                {
                    text.AppendLine($"{(record.Motivo == AbordagemMotivo.Blitz ? "Blitz" : "Flagrante")} no dia {record.Dia}: {record.Escolha}, {DesfechoText(record.Desfecho)}");
                }
            }

            return text.ToString();
        }

        private string MotoStatusText()
        {
            string origin = Session.Origem == MotoOrigem.Bob ? "bob" : "alugada";

            switch (Session.MotoStatus)
            {
                case MotoStatus.NoPatio: return $"no pátio ({origin})";
                case MotoStatus.Apreendida: return "apreendida";
                default: return $"em ordem ({origin})";
            }
        }

        private static string CnhText(Carteira cnh)
        {
            switch (cnh.Status)
            {
                case CnhStatus.SemCnh: return "não tem";
                case CnhStatus.Suspensa: return $"suspensa ({cnh.Pontos} pontos)";
                default: return $"válida, {cnh.Pontos} pontos";
            }
        }

        private static string DesfechoText(AbordagemDesfecho desfecho)
        {
            switch (desfecho)
            {
                case AbordagemDesfecho.Liberado: return "liberado";
                case AbordagemDesfecho.LiberadoComMulta: return "liberado com multa";
                case AbordagemDesfecho.MotoNoPatio: return "moto levada ao pátio";
                case AbordagemDesfecho.MotoApreendida: return "moto apreendida";
                default: return "escapou";
            }
        }
    }
}

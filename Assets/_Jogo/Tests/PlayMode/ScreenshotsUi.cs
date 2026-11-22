using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Entregador.Game.Tests
{
    public sealed partial class ScreenshotTests
    {
        private static IEnumerator Wait(float seconds)
        {
            yield return new WaitForSecondsRealtime(seconds);
        }

        private IEnumerator PlaceOnAvenue()
        {
            ViewPoint v = View("avenida");
            yield return PlaceFacing(v.Position, v.Position + Heading(v.Yaw) * 50f);
        }

        /// <summary>Menu principal, opções, créditos, novo jogo (piloto, moto, CNH).</summary>
        [UnityTest]
        public IEnumerator CaptureUiMenus()
        {
            string dir = Dir;
            Camera main = Camera.main;
            StartDryWeek();

            Flow.ShowMainMenu();
            yield return Wait(1.2f);
            Save(dir, "u01_menu_principal.png", main);

            Flow.Ui.ClickModalButton("Opções");
            yield return Wait(0.4f);
            Save(dir, "u02_opcoes.png", main);
            Flow.Ui.ClickModalButton("Voltar");
            Flow.Ui.ClickModalButton("Créditos");
            yield return Wait(0.8f);
            Save(dir, "u03_creditos.png", main);
            Flow.Ui.ClickModalButton("Voltar");

            Flow.ShowSetup();
            yield return Wait(0.5f);
            Save(dir, "u04_novo_jogo.png", main);
            Flow.SelectCharacter(Personagem.Camila);
            Flow.SetModel(MotoModelo.Twister);
            Flow.SetCnh(false);
            yield return Wait(0.4f);
            Save(dir, "u05_novo_jogo_twister_sem_cnh.png", main);
            Flow.SetOrigin(Entregador.Core.MotoOrigem.Bob);
            yield return Wait(0.4f);
            Save(dir, "u06_novo_jogo_bob.png", main);
            Flow.SetProfile(new PerfilJogador());
        }

        /// <summary>HUD de corrida: marcha 3 no meio do giro, passando do limitador, luzes de aviso, navegação, cortina e exploração livre.</summary>
        [UnityTest]
        public IEnumerator CaptureUiHud()
        {
            string dir = Dir;
            Camera main = Camera.main;
            StartDryWeek();
            Flow.EnterFreeRoam();
            yield return PlaceOnAvenue();
            Flow.enabled = false;
            Flow.Ui.SetHudVisible(true);
            Flow.Ui.HideModal();
            Flow.Ui.ClearToast();

            // Modo livre, parado.
            Flow.Ui.SetRacingData(1300f, 0, 9000f, 0f, 0f, 50, false, false, 0f);
            yield return Wait(0.8f);
            Save(dir, "h01_livre_parado.png", main);

            // Andando, marcha 3, meio do giro, dentro do limite.
            Flow.Ui.SetRacingData(5200f, 3, 9000f, 0.6f, 0f, 60, false, false, 58f);
            yield return Wait(0.7f);
            Save(dir, "h02_marcha3_meio_giro.png", main);

            // Navegação de rua a rua.
            Flow.Ui.SetNavegacao(1, 200f, "Av. Adolfo Pinheiro", "esquerda na R. Verbo Divino");
            yield return Wait(0.3f);
            Save(dir, "h03_navegacao_200m.png", main);

            // Passando do limitador e da velocidade da via, com contramão e corredor de ônibus.
            Flow.Ui.SetNavegacao(1, 40f, "Av. Adolfo Pinheiro");
            Flow.Ui.SetRacingData(9300f, 4, 9000f, 1f, 0f, 50, true, true, 86f);
            yield return Wait(0.65f);
            Save(dir, "h04_limitador_excesso.png", main);

            // Freando, tanque na reserva, farol alto e seta.
            Flow.Ui.Painel.Limpar();
            Flow.Ui.Painel.Demo = true;
            Flow.Ui.Painel.Reserva = true;
            Flow.Ui.Painel.Combustivel = 0.1f;
            Flow.Ui.Painel.FarolAlto = true;
            Flow.Ui.Painel.SetaEsquerda = true;
            Flow.Ui.SetNavegacao(7, 12f, "Hamburgueria Zona Sul");
            Flow.Ui.SetRacingData(3800f, 2, 9000f, 0f, 0.8f, 40, false, false, 31f);
            yield return Wait(0.62f);
            Save(dir, "h05_freando_reserva.png", main);

            // Todas as luzes de aviso acesas.
            Flow.Ui.Painel.AcenderTudo();
            Flow.Ui.SetRacingData(6400f, 3, 9000f, 0.4f, 0f, 60, false, false, 52f);
            Flow.Ui.HideNavegacao();
            yield return Wait(0.5f);
            Save(dir, "h06_todas_as_luzes.png", main);
            Flow.Ui.Painel.Limpar();

            // Cortina de informações: meio aberta e aberta.
            Flow.Ui.SetPainelInfo(new InfoPainel
            {
                Etapa = "Entregar", Restaurante = "Hamburgueria Zona Sul", Cliente = "R. Padre José de Anchieta, 120", Endereco = "Santo Amaro",
                DistanciaM = 840f, DecorridoS = 95f, EsperadoS = 150f, Pagamento = "R$ 18,50", Gorjeta = "Inclui adicional de chuva",
                Saldo = "R$ 1.284,00", Dia = "Dia 2: Terça", EntregasFeitas = 5, EntregasMeta = 12, Nota = 4.6f,
                Multas = "R$ 130,16", PontosCnh = "4 de 20 pontos", MotoStatus = "Titã 160 · alugada, documentada", CnhStatus = "Válida, categoria A",
            });
            Flow.Ui.SetRacingData(4200f, 3, 9000f, 0.3f, 0f, 50, false, false, 46f);
            Flow.Ui.ShadeBegin();
            Flow.Ui.ShadeDrag(240f);
            yield return Wait(0.3f);
            Save(dir, "h07_cortina_meio.png", main);
            Flow.Ui.ShadeEnd(1000f);
            yield return Wait(1.2f);
            Save(dir, "h08_cortina_aberta.png", main);
            Flow.Ui.CloseInfo();
            yield return Wait(1f);

            // Aviso (banner de notificação).
            Flow.Ui.Toast("Pedido retirado em Hamburgueria Zona Sul. Agora leve até R. Padre José de Anchieta.", 6f);
            yield return Wait(0.6f);
            Save(dir, "h09_aviso.png", main);
            Flow.enabled = true;
        }

        /// <summary>Menu do celular, oferta, pausa, abordagem, blitz, fim do dia, relatório da semana e título do dia.</summary>
        [UnityTest]
        public IEnumerator CaptureUiApp()
        {
            string dir = Dir;
            Camera main = Camera.main;
            StartDryWeek();
            yield return PlaceOnAvenue();
            Flow.EnterFreeRoam();
            yield return null;

            Flow.OpenPhoneMenu();
            yield return Wait(0.9f);
            Save(dir, "a01_menu_celular.png", main);
            Flow.StartShiftFromMenu();
            yield return Wait(0.9f);
            Save(dir, "a02_inicio_do_dia.png", main);

            Flow.BeginShift();
            yield return Wait(0.9f);
            Save(dir, "a03_oferta_pedido.png", main);

            Flow.AcceptOffer();
            Flow.Ui.ClearToast();
            yield return Wait(0.5f);
            Flow.PauseGame();
            yield return Wait(0.9f);
            Save(dir, "a04_pausa.png", main);
            Flow.ResumeGame();

            Flow.Ui.ShowModal("Abordagem encerrada",
                "Os policiais conferiram a moto e os documentos.\nMulta: Contramão R$ 130,16\nTaxa de pagamento: R$ 7,50\nVocê perdeu 4 minutos na abordagem.\nPontos na carteira: 7 pontos\n\nSeguir viagem com a entrega em andamento.",
                new PanelButton("Seguir viagem", () => { }));
            yield return Wait(0.9f);
            Save(dir, "a05_abordagem.png", main);

            Flow.Ui.ShowModal("Blitz!",
                "Blitz da polícia. Um policial manda você encostar e pede os documentos.\n\nParar: conferem os documentos. Perde alguns minutos e pode vir multa.\nFugir: pode dar certo, mas se te pegarem a multa é pesada, a moto é levada e você perde mais de uma hora.\nSua moto é bob, sem documentos: se ela for parada, será apreendida.",
                new PanelButton("Parar", () => { }), new PanelButton("Fugir", () => { }));
            yield return Wait(0.9f);
            Save(dir, "a06_blitz.png", main);

            Flow.Ui.ShowModal("Fim do turno: Segunda",
                "Entregas hoje: 6\nResultado do dia: R$ 142,50\nSaldo: R$ 1.284,00\nNota: 4,7\nMultas: R$ 137,66\nBlitz hoje: 1 abordagem, documentos em ordem",
                new PanelButton("Próximo dia", () => { }));
            yield return Wait(0.9f);
            Save(dir, "a07_fim_do_dia.png", main);

            Flow.Ui.ShowModal("Deu pra pagar. Semana que vem tem mais.",
                "Entregas: 38\nBônus da meta: R$ 120,00\nMultas: R$ 267,32\nAluguel da moto: R$ 330,00\nCaução devolvida: R$ 300,00\nNota final: 4,6\nSaldo final: R$ 1.284,00",
                new PanelButton("Jogar outra semana", () => { }), new PanelButton("Novo perfil", () => { }));
            yield return Wait(0.9f);
            Save(dir, "a08_relatorio_semana.png", main);

            Flow.Ui.HideModal();
            Flow.Ui.ShowDayTitle("Dia 2 · Terça", "Previsão: tempo firme");
            yield return Wait(0.8f);
            Save(dir, "a09_titulo_do_dia.png", main);
            yield return Wait(1.6f);
        }
    }
}

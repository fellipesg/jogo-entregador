namespace Entregador.Core
{
    /// <summary>
    /// Falas da roda de entregadores (texto puro, gíria paulistana, nada ofensivo). Marcadores: {rua}, {ref}, {lim}, {rest}, {min},
    /// {zona}, {valor}. As falas de informação (blitz, radar, chuva, demanda, app) só saem quando o jogador pergunta.
    /// </summary>
    public static class FofocaCatalogo
    {
        public static readonly string[] Saudacoes =
        {
            "Salve, truta! Chega aí, a sombra tá boa.",
            "E aí, mano! Parou pra respirar um pouco?",
            "Fala, parceiro! Senta aí, o pedido não vai fugir.",
            "Opa, mais um na resenha! Chega mais.",
            "Salve, salve! Tá corrido hoje ou tá osso também?",
            "Eita, olha quem apareceu! Bora trocar uma ideia.",
            "Fala, campeão! Água, café, o que precisar a gente tem.",
            "Beleza, mano? Tamo aqui esperando o app tocar.",
        };

        public static readonly string[] Despedidas =
        {
            "Vai com Deus, mano! Corre que o app tá chamando.",
            "Valeu a resenha! Se cuida no trânsito, truta.",
            "Fechou! Qualquer coisa a gente tá por aqui.",
            "Tmj, parceiro! Boas entregas aí.",
            "Vai lá, campeão! Sem pressa, mas com pressa.",
            "Falou! Capacete no lugar e olho na rua.",
            "Tá liberado, corre lá! Depois conta como foi.",
            "Salve! Passa aqui de novo, a roda não acaba.",
        };

        public static readonly string[] Reacoes =
        {
            "Pois é, né, mano.",
            "Tá osso mesmo!",
            "Verdade, truta, sem mentira.",
            "Hahaha, essa foi boa!",
            "Nem me fala...",
            "Isso aí, falou tudo.",
            "Tô sabendo, aconteceu comigo semana passada.",
            "É isso, a vida do corre.",
            "Boa, anota aí!",
            "Concordo, mano. Concordo demais.",
            "Pior que é verdade.",
            "Eita, é o que tem pra hoje.",
            "Ah, isso eu já passei também!",
            "Fala sério, mano.",
            "Tá doido, hein?",
            "Prova que a gente aguenta de tudo.",
            "Hahaha, pior que é isso mesmo.",
            "E ainda tem quem reclame, né?",
            "Segura que o dia é longo.",
            "Boa, mano, é nóis.",
        };

        public static readonly string[] Papo =
        {
            "Viu o jogo ontem? O juiz tava com a cabeça na Marginal, só pode.",
            "O time lá do bairro tá igual moto sem gasolina: não anda.",
            "Meu primo diz que o time dele vai ser campeão. Ele fala isso desde 2009.",
            "Jogo de domingo eu assisto no balcão do restaurante, entre um pedido e outro.",
            "Ontem teve gol aos 48 do segundo tempo. Quase caí da moto de tanto grito.",
            "Gasolina subiu de novo, mano. Daqui a pouco vou a pé com a mochila nas costas.",
            "Completei o tanque ontem e o frentista me olhou com pena.",
            "Meu freio tá fazendo um barulho de gato miando. Alguém conhece mecânico bom?",
            "Troquei o óleo e a moto até ficou educada, parou de reclamar.",
            "Pneu careca é igual promessa de político: parece que segura, mas não segura.",
            "Tem gente que anda de moto bob e acha que é invisível pra fiscalização. Tá osso.",
            "Moto documentada tem IPVA, seguro, multa... mas dorme tranquilo, truta.",
            "Debate eterno: bob dá liberdade, documentada dá paz. Eu fico com a paz.",
            "O Zé comprou uma moto bob baratinha. Tá pagando parcela de uma moto que pode ser apreendida.",
            "Um conhecido teve a moto apreendida semana passada. Foi andar sem papel no dia errado.",
            "Soube de uma moto que foi pro pátio por causa de documento atrasado. Tá feio.",
            "O capacete novo que eu comprei é tão bom que parece que eu tô numa nave espacial.",
            "Eu devia ter virado padeiro. Acordar cedo e cheirar pão. Mas aqui é o corre, né.",
            "Hoje de manhã um cachorro me seguiu quatro quarteirões. Achei que era cliente.",
            "Me pediram uma entrega de pastel de feira. Chegou frio por culpa do semáforo, não minha.",
            "Cliente falou que o pedido estava errado. Eu só entrego, mano, não cozinho.",
            "Tem cliente que manda o endereço sem número e ainda reclama que demorei.",
            "Entreguei num prédio sem porteiro, sem interfone e com cachorro. Ganhei experiência.",
            "O melhor lanche do dia é o que a gente come em pé, esperando o pedido ficar pronto.",
            "Quem inventou o semáforo vermelho de dois minutos não conhece a pressa de quem entrega.",
            "A Marginal tá sempre cheia, mas quem anda de moto sabe um atalho. Só não conto!",
            "Chuva e pedido de sorvete no mesmo dia. Quem vai entender o cliente?",
            "Ontem eu deixei o capacete na moto e voltei com saudade dele. Brincadeira, voltei com ele.",
            "Meu celular tem mais rachadura que asfalto de rua residencial.",
            "Suporte de celular bom é igual cinto de segurança: ninguém dá valor até precisar.",
            "Lembra quando a gente achava que o corre era só no começo do mês?",
            "Bateria do celular acabou no meio da entrega. Nunca mais saio sem carregador portátil.",
            "O pessoal da padaria sempre dá um cafezinho. Esses sim merecem cinco estrelas.",
            "Minha mãe pergunta quando eu paro de andar de moto. Eu respondo: quando a moto parar de andar.",
            "Já viu aquele motoboy que entrega de uniforme impecável? Eu só consigo no primeiro dia da semana.",
            "Vizinho me pediu pra consertar a moto dele. Eu só sei bater no tanque e rezar.",
            "Final de semana é aquele corre: pizza, hambúrguer, pizza, hambúrguer, pizza...",
            "Dia sem pedido é igual domingo sem futebol: estranho demais.",
            "A pamonha de beira de estrada é o único vício que eu assumo.",
            "Se eu ganhasse cinco reais por cada 'tá chegando?' do cliente, já tinha comprado moto nova.",
            "Quem dá bom dia pra entregador já ganha meu dia. Gentileza é rara, truta.",
            "Fui entregar num condomínio com seis portarias. Cheguei no fim da noite, mas cheguei.",
            "Aquela buraqueira da rua de trás é pegadinha, só pode. Passei de olho fechado.",
            "Se eu tivesse um real pra cada vez que o GPS me mandou pro lado errado, tava rico.",
            "Piada do dia: o app disse 'entrega rápida'. Eu ri sozinho no capacete.",
            "O cara pediu um milk-shake num dia de chuva e ainda reclamou que tava gelado.",
            "Tem dia que a moto parece uma amiga, tem dia que parece sogra. Hoje tá sogra.",
        };

        // ---------- Informação, só quando o jogador pergunta ----------

        public static readonly string[] BlitzEspecifica =
        {
            "Tá tendo blitz lá na {rua}, {ref}. Vai por outro lado.",
            "Passei agora e tinha blitz na {rua}. Fica esperto, {ref}.",
            "Cuidado com a {rua}: blitz armada, {ref}. Documento em dia, né?",
            "Um parceiro avisou blitz na {rua}, {ref}. Se tá sem papel, evita.",
            "Tem blitz na {rua}. Pelo que falaram, {ref}.",
        };

        public static readonly string[] BlitzVaga =
        {
            "Ouvi dizer que tem blitz {ref}, mas não sei a rua certinho.",
            "Falaram de blitz {ref}. Vai de olho e não confia em palpite.",
            "Tem uns cones armados {ref}. Mais que isso eu não sei.",
            "Blitz {ref}, tá no grupo do pessoal. Só não sei o ponto exato.",
        };

        public static readonly string[] BlitzLimpa =
        {
            "Hoje tá limpo, não vi blitz nenhuma. Mas sempre olho dobrado.",
            "Pelo que o pessoal falou, não tem blitz hoje. Dia tranquilo!",
            "Até agora ninguém avisou de blitz. Tá calmo, truta.",
        };

        public static readonly string[] BlitzFalsa =
        {
            "Dizem que tem blitz lá na {rua}. Foi o que me contaram, vai saber.",
            "Ouvi um papo de blitz na {rua}. Não vi com meus olhos, hein.",
            "Falaram que a polícia armou na {rua}. Rumor de grupo, né.",
        };

        public static readonly string[] BlitzFalsaLimpa =
        {
            "Hoje não tem blitz nenhuma, pode ir tranquilo. Foi o que me disseram.",
            "Tá liberado, ninguém viu blitz hoje. Pelo menos foi o que chegou aqui.",
        };

        public static readonly string[] Radar =
        {
            "O radar da {rua} tá pegando a {lim}. Não esquece de frear.",
            "Cuidado na {rua}: tem radar de {lim} km/h. A multa chega depois, e dói.",
            "Radar na {rua}, limite {lim}. Já levei uma ali, nunca mais.",
            "Tem fotossensor na {rua} que pega a {lim}. Vai manso que compensa.",
            "Na {rua} o radar não perdoa: {lim} e pronto.",
        };

        public static readonly string[] RadarNenhum =
        {
            "Radar aqui perto eu não lembro de nenhum. Mas vai com cuidado do mesmo jeito.",
        };

        public static readonly string[] ChuvaAgora =
        {
            "Já tá chovendo, mano! Pista escorregadia, vai com calma.",
            "Chuva agora! Capa na mochila e freio com carinho.",
        };

        public static readonly string[] ChuvaMaisTarde =
        {
            "Pelo céu, vai cair um pé d'água mais tarde. Guarda a capa à mão.",
            "A previsão diz chuva depois do almoço. Prepara a capa, truta.",
            "Mais tarde vem chuva, sim. Pedido de sorvete vai cair e o de sopa vai subir.",
        };

        public static readonly string[] ChuvaAmanha =
        {
            "Hoje tá seco, mas amanhã vem chuva. Pode comprar a capa.",
            "A previsão diz que amanhã chove. Aproveita o dia seco!",
        };

        public static readonly string[] ChuvaNenhuma =
        {
            "Previsão de chuva? Nada por enquanto. Céu limpo, mano.",
            "Hoje e amanhã sem chuva, pelo que vi. Aproveita!",
        };

        public static readonly string[] Demanda =
        {
            "Tá bombando {zona}. Pedido cai que não para.",
            "Se eu fosse você, ficava {zona}. É onde o app mais tá chamando.",
            "Tá movimentado {zona}. Quem fica lá pega corrida a rodo.",
            "O movimento hoje tá {zona}. O resto tá devagar.",
        };

        public static readonly string[] RestauranteLento =
        {
            "O balcão de {rest} tá demorando uns {min} min hoje. Prepara a paciência.",
            "Evita {rest} se tiver pressa: tá demorando {min} minutos no balcão.",
            "Fiquei {min} min esperando em {rest}. Chega com calma.",
        };

        public static readonly string[] App =
        {
            "O app tá pagando pouco hoje, mano. Corrida de 5 km por menos de dez reais.",
            "Taxa do app comeu metade da corrida de ontem. Tá osso demais.",
            "Um amigo foi bloqueado pelo app por baixa aceitação. Recusou muita corrida seguida.",
            "Se recusar corrida demais, o app te bloqueia. Já aconteceu com dois aqui.",
            "O app reduziu o valor por quilômetro. Quem tem moto alugada sente mais.",
            "Nota baixa derruba as ofertas. Entrega rápida e sorriso no rosto salvam.",
            "Hoje as corridas estão curtas e mal pagas. Melhor pegar só as boas.",
            "Taxa de serviço do app tá estranha esta semana. Confere o extrato.",
        };

        public static readonly string[] Gorjeta =
        {
            "Um cliente deixou R$ {valor} de gorjeta hoje. Só um, mas foi bonito.",
            "Dei sorte: gorjeta de R$ {valor} no prédio da esquina. Dia salvo!",
            "Gorjeta de R$ {valor} é raridade, mas apareceu. Pagou o café.",
        };

        // ---------- Rótulos das respostas do jogador ----------

        public static readonly string[] PerguntaBlitz = { "Tem blitz por aí?", "Viu blitz hoje?", "Tá sabendo de blitz?" };
        public static readonly string[] PerguntaRadar = { "Tem radar pegando por aqui?", "Cuidado com algum radar?", "Algum fotossensor perto?" };
        public static readonly string[] PerguntaChuva = { "Vai chover hoje?", "E a chuva, vem aí?", "Previsão do tempo, alguém sabe?" };
        public static readonly string[] PerguntaDemanda = { "Onde tá bombando?", "Qual região tá com mais pedido?", "Onde vale a pena ficar?" };
        public static readonly string[] PerguntaRestaurante = { "Qual restaurante tá demorando?", "Algum balcão lento hoje?", "Evito qual restaurante?" };
        public static readonly string[] PerguntaApp = { "E o app, como tá?", "Tá pagando bem o app?", "Alguma novidade do app?" };
        public static readonly string[] PerguntaPapo = { "Conta uma aí, mano.", "Qual a boa de hoje?", "Fala besteira, vai." };
        public static readonly string[] PerguntaSair = { "Valeu, tô indo!", "Preciso correr, truta.", "Fechou, até a próxima!" };

        public static readonly string[] NomesEntregadores =
        {
            "Dudu", "Betão", "Juninho", "Rafa", "Careca", "Tiago", "Lúcio", "Bira", "Cleiton", "Naldo", "Vini", "Sandrão", "Dani",
            "Jessi", "Carol", "Wesley", "Ricardo", "Zeca",
        };

        /// <summary>Cumprimentos curtos que aparecem em balão acima da cabeça quando o jogador passa devagar.</summary>
        public static readonly string[] Cumprimentos = { "Salve!", "Fala, mano!", "E aí!", "Opa!", "Tmj!", "Firmeza?" };
    }
}

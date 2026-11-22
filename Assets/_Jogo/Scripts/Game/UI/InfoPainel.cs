namespace Entregador.Game
{
    /// <summary>Dados do painel de informações (a "cortina" que desce do topo): pedido, turno, moto e documentos.</summary>
    public sealed class InfoPainel
    {
        /// <summary>"Retirar" ou "Entregar"; vazio = sem pedido.</summary>
        public string Etapa = string.Empty;

        public string Restaurante = string.Empty;
        public string Cliente = string.Empty;
        public string Endereco = string.Empty;
        public float DistanciaM;
        public float DecorridoS;
        public float EsperadoS;
        public string Pagamento = string.Empty;
        public string Gorjeta = string.Empty;

        public string Saldo = "R$ 0,00";
        public string Dia = string.Empty;
        public int EntregasFeitas;
        public int EntregasMeta;
        public float Nota;

        public string Multas = string.Empty;
        public string PontosCnh = string.Empty;
        public string MotoStatus = string.Empty;
        public string CnhStatus = string.Empty;
        public float Comida = 1f;
    }
}

using Entregador.Core;
using NUnit.Framework;

namespace Entregador.Core.Tests
{
    /// <summary>Quantos entregadores ficam num ponto: depende da hora e da chuva, sempre entre o mínimo e o máximo.</summary>
    public sealed class PontoPopulacaoTests
    {
        [Test]
        public void AlwaysStaysBetweenMinimumAndMaximum()
        {
            for (float hora = -1f; hora <= 24f; hora += 0.5f)
            {
                foreach (bool chuva in new[] { false, true })
                {
                    for (double sorte = 0.0; sorte <= 1.0; sorte += 0.25)
                    {
                        Assert.That(PontoPopulacao.Quantidade(hora, chuva, sorte), Is.InRange(PontoPopulacao.Minimo, PontoPopulacao.Maximo));
                    }
                }
            }
        }

        [Test]
        public void MealPeaksHaveMoreCouriersThanTheSmallHours()
        {
            Assert.Greater(PontoPopulacao.Quantidade(12.5f, false, 0.5), PontoPopulacao.Quantidade(3f, false, 0.5));
            Assert.Greater(PontoPopulacao.Quantidade(19.5f, false, 0.5), PontoPopulacao.Quantidade(9f, false, 0.5));
        }

        [Test]
        public void RainReducesTheCount()
        {
            Assert.Less(PontoPopulacao.Quantidade(12.5f, true, 0.5), PontoPopulacao.Quantidade(12.5f, false, 0.5));
        }
    }
}

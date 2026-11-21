using System;
using System.Globalization;

namespace Entregador.Core
{
    /// <summary>
    /// Valor em reais, guardado em centavos para não acumular erro de arredondamento.
    /// </summary>
    public readonly struct Money : IEquatable<Money>, IComparable<Money>
    {
        public readonly long Cents;

        public Money(long cents)
        {
            Cents = cents;
        }

        public static Money Zero => new Money(0);

        public static Money Reais(decimal reais) =>
            new Money((long)Math.Round(reais * 100m, MidpointRounding.AwayFromZero));

        public bool IsNegative => Cents < 0;

        public static Money operator +(Money a, Money b) => new Money(a.Cents + b.Cents);
        public static Money operator -(Money a, Money b) => new Money(a.Cents - b.Cents);
        public static Money operator -(Money a) => new Money(-a.Cents);

        public static Money operator *(Money a, decimal factor) =>
            new Money((long)Math.Round(a.Cents * factor, MidpointRounding.AwayFromZero));

        public static bool operator ==(Money a, Money b) => a.Cents == b.Cents;
        public static bool operator !=(Money a, Money b) => a.Cents != b.Cents;
        public static bool operator <(Money a, Money b) => a.Cents < b.Cents;
        public static bool operator >(Money a, Money b) => a.Cents > b.Cents;
        public static bool operator <=(Money a, Money b) => a.Cents <= b.Cents;
        public static bool operator >=(Money a, Money b) => a.Cents >= b.Cents;

        public bool Equals(Money other) => Cents == other.Cents;
        public override bool Equals(object obj) => obj is Money other && Equals(other);
        public override int GetHashCode() => Cents.GetHashCode();
        public int CompareTo(Money other) => Cents.CompareTo(other.Cents);

        /// <summary>Formato brasileiro: "R$ 1.234,56" e "-R$ 12,00".</summary>
        public override string ToString()
        {
            long abs = Math.Abs(Cents);
            string reais = (abs / 100).ToString("#,0", CultureInfo.InvariantCulture).Replace(',', '.');
            string cents = (abs % 100).ToString("00", CultureInfo.InvariantCulture);

            return (Cents < 0 ? "-" : "") + "R$ " + reais + "," + cents;
        }
    }
}

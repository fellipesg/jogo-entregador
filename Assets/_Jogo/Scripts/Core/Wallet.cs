using System.Collections.Generic;

namespace Entregador.Core
{
    public enum LedgerCategory
    {
        Entrega,
        Gorjeta,
        Bonus,
        Estorno,
        Combustivel,
        Aluguel,
        Caucao,
        Conserto,
        Multa,
        Compra,
    }

    public readonly struct LedgerEntry
    {
        public readonly int Day;
        public readonly LedgerCategory Category;
        public readonly Money Amount;
        public readonly string Description;

        public LedgerEntry(int day, LedgerCategory category, Money amount, string description)
        {
            Day = day;
            Category = category;
            Amount = amount;
            Description = description;
        }
    }

    /// <summary>
    /// Dinheiro do jogador e o extrato de tudo que entrou e saiu. O saldo pode ficar negativo: é a dívida.
    /// </summary>
    public sealed class Wallet
    {
        private readonly List<LedgerEntry> _entries = new List<LedgerEntry>();

        public Wallet(Money startingBalance)
        {
            Balance = startingBalance;
        }

        public Money Balance { get; private set; }

        public bool IsInDebt => Balance.IsNegative;

        public IReadOnlyList<LedgerEntry> Entries => _entries;

        public void Credit(int day, LedgerCategory category, Money amount, string description)
        {
            Balance += amount;
            _entries.Add(new LedgerEntry(day, category, amount, description));
        }

        public void Debit(int day, LedgerCategory category, Money amount, string description)
        {
            Balance -= amount;
            _entries.Add(new LedgerEntry(day, category, -amount, description));
        }

        /// <summary>Soma das entradas de um dia, positivas e negativas.</summary>
        public Money NetForDay(int day)
        {
            Money total = Money.Zero;

            foreach (LedgerEntry entry in _entries)
            {
                if (entry.Day == day)
                {
                    total += entry.Amount;
                }
            }

            return total;
        }
    }
}

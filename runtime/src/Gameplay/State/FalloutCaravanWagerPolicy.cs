using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

// GECK ShowCaravanMenu: Difficulty is 0..3; MaxBetPercent is a fraction of
// the opponent's currency, including faction currency at its base value.
// https://geckwiki.com/index.php?title=ShowCaravanMenu
// Exact native rounding and denomination selection remain unverified. We floor
// the ceiling and transfer actual denominations of exactly the agreed value.
internal sealed class FalloutCaravanWagerPolicy(FalloutPlayerInventory player, FalloutPlayerInventory opponent, FalloutFormKey caps)
{
    private sealed record Currency(FalloutFormKey Form, int Value, int Count);
    private IReadOnlyList<Currency> Wallet(FalloutPlayerInventory inventory) => inventory.Items
        .Where(item => item.FormKey == caps || item.RecordType == "CMNY")
        .Select(item => new Currency(item.FormKey, item.FormKey == caps ? 1 :
            item.Value is > 0 ? item.Value.Value : throw new NotSupportedException($"Caravan currency {item.FormKey} has no positive source value."), item.Count))
        .OrderByDescending(item => item.Value).ThenBy(item => item.Form.ToString(), StringComparer.Ordinal).ToArray();

    internal long PlayerValue => Wallet(player).Sum(item => (long)item.Value * item.Count);
    internal long OpponentValue => Wallet(opponent).Sum(item => (long)item.Value * item.Count);
    internal int Maximum(FalloutCaravanCommandRequest request)
    {
        if (request.AiType is < 0 or > 3) throw new NotSupportedException($"Caravan difficulty {request.AiType} is outside documented 0..3.");
        if (!float.IsFinite(request.BetPercentage) || request.BetPercentage is < 0 or > 1)
            throw new NotSupportedException($"Caravan max-bet fraction {request.BetPercentage} is outside 0..1.");
        return checked((int)Math.Min(int.MaxValue, Math.Min(PlayerValue, Math.Floor(OpponentValue * (double)request.BetPercentage))));
    }

    internal void RequirePayable(int amount)
    {
        _ = Payment(player, amount); _ = Payment(opponent, amount);
    }

    internal void Settle(int amount, bool playerWon)
    {
        if (amount == 0) return;
        var payment = Payment(playerWon ? opponent : player, amount);
        player.Exchange(opponent, payment.Select(item => new FalloutTradeTransfer(item.Form, item.Count, !playerWon)).ToArray());
    }

    private IReadOnlyList<(FalloutFormKey Form, int Count)> Payment(FalloutPlayerInventory inventory, int amount)
    {
        if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
        var wallet = Wallet(inventory);
        var selected = new int[wallet.Count];
        var remainingValue = new long[wallet.Count + 1];
        var remainingDivisor = new int[wallet.Count + 1];
        static int Gcd(int first, int second)
        {
            while (second != 0) (first, second) = (second, first % second);
            return first;
        }
        for (var index = wallet.Count - 1; index >= 0; --index)
        {
            remainingValue[index] = checked(remainingValue[index + 1] + (long)wallet[index].Value * wallet[index].Count);
            remainingDivisor[index] = Gcd(wallet[index].Value, remainingDivisor[index + 1]);
        }
        var failed = new HashSet<(int Index, int Left)>();
        bool Find(int index, int left)
        {
            if (left == 0) return true;
            if (index == wallet.Count || !failed.Add((index, left))) return false;
            if (left > remainingValue[index] || left % remainingDivisor[index] != 0) return false;
            var currency = wallet[index];
            // The last denomination has exactly one possible count. Do not
            // iterate a player's entire wallet when no combination can pay.
            if (index + 1 == wallet.Count)
            {
                if (left % currency.Value != 0 || left / currency.Value > currency.Count) return false;
                selected[index] = left / currency.Value; return true;
            }
            var maximum = Math.Min(currency.Count, left / currency.Value);
            for (var count = maximum; count >= 0; --count)
            {
                selected[index] = count;
                if (Find(index + 1, left - count * currency.Value)) return true;
            }
            selected[index] = 0;
            return false;
        }
        if (!Find(0, amount)) throw new InvalidOperationException("Both players must hold denominations that can pay this exact wager. Choose another amount.");
        return wallet.Select((currency, index) => (currency.Form, Count: selected[index])).Where(item => item.Count > 0).ToArray();
    }
}

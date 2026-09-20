namespace Zunavio.KdpFactory.Domain.ValueObjects;

/// <summary>
/// Immutable USD cost estimate. Stored as decimal because PostgreSQL
/// maps it to numeric which avoids floating point surprises for invoices.
/// </summary>
public readonly record struct EstimatedCost
{
    public decimal Usd { get; }

    public EstimatedCost(decimal usd)
    {
        if (usd < 0) throw new ArgumentOutOfRangeException(nameof(usd), "Cost cannot be negative.");
        Usd = usd;
    }

    public static EstimatedCost Zero => new(0m);

    public static EstimatedCost FromTokens(int? input, int? output, decimal? pricePerMillionInput, decimal? pricePerMillionOutput)
    {
        if (input is null || output is null) return Zero;

        var cost = 0m;
        if (pricePerMillionInput is > 0) cost += input.Value / 1_000_000m * pricePerMillionInput.Value;
        if (pricePerMillionOutput is > 0) cost += output.Value / 1_000_000m * pricePerMillionOutput.Value;
        return new EstimatedCost(Math.Round(cost, 6, MidpointRounding.AwayFromZero));
    }

    public override string ToString() => Usd.ToString("0.######");
}
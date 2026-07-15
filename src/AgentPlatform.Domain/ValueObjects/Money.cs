namespace AgentPlatform.Domain.ValueObjects;

/// <summary>
/// Represents a monetary amount with an associated currency, supporting arithmetic
/// and comparison operators that enforce currency consistency.
/// </summary>
/// <param name="Amount">The numeric value of the monetary amount.</param>
/// <param name="Currency">The ISO 4217 currency code. Defaults to "USD".</param>
public record Money(decimal Amount, string Currency = "USD")
{
    /// <summary>
    /// Gets a <see cref="Money"/> value representing zero in the default currency.
    /// </summary>
    public static Money Zero => new(0);

    private static void EnsureSameCurrency(Money a, Money b)
    {
        if (!string.Equals(a.Currency, b.Currency, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"Cannot operate on Money with different currencies: {a.Currency} vs {b.Currency}");
    }

    /// <summary>
    /// Adds two monetary values of the same currency.
    /// </summary>
    /// <param name="a">The first monetary value.</param>
    /// <param name="b">The second monetary value.</param>
    /// <returns>A new <see cref="Money"/> representing the sum.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="a"/> or <paramref name="b"/> is <c>null</c>.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the currencies differ.</exception>
    public static Money operator +(Money a, Money b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        EnsureSameCurrency(a, b);
        return new(a.Amount + b.Amount, a.Currency);
    }

    /// <summary>
    /// Subtracts one monetary value from another of the same currency.
    /// </summary>
    /// <param name="a">The monetary value to subtract from.</param>
    /// <param name="b">The monetary value to subtract.</param>
    /// <returns>A new <see cref="Money"/> representing the difference.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="a"/> or <paramref name="b"/> is <c>null</c>.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the currencies differ.</exception>
    public static Money operator -(Money a, Money b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        EnsureSameCurrency(a, b);
        return new(a.Amount - b.Amount, a.Currency);
    }

    /// <summary>
    /// Determines whether the first monetary value is less than the second.
    /// </summary>
    /// <param name="a">The first monetary value.</param>
    /// <param name="b">The second monetary value.</param>
    /// <returns><c>true</c> if <paramref name="a"/> is less than <paramref name="b"/>; otherwise <c>false</c>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="a"/> or <paramref name="b"/> is <c>null</c>.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the currencies differ.</exception>
    public static bool operator <(Money a, Money b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        EnsureSameCurrency(a, b);
        return a.Amount < b.Amount;
    }

    /// <summary>
    /// Determines whether the first monetary value is greater than the second.
    /// </summary>
    /// <param name="a">The first monetary value.</param>
    /// <param name="b">The second monetary value.</param>
    /// <returns><c>true</c> if <paramref name="a"/> is greater than <paramref name="b"/>; otherwise <c>false</c>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="a"/> or <paramref name="b"/> is <c>null</c>.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the currencies differ.</exception>
    public static bool operator >(Money a, Money b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        EnsureSameCurrency(a, b);
        return a.Amount > b.Amount;
    }

    /// <summary>
    /// Determines whether the first monetary value is less than or equal to the second.
    /// </summary>
    /// <param name="a">The first monetary value.</param>
    /// <param name="b">The second monetary value.</param>
    /// <returns><c>true</c> if <paramref name="a"/> is less than or equal to <paramref name="b"/>; otherwise <c>false</c>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="a"/> or <paramref name="b"/> is <c>null</c>.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the currencies differ.</exception>
    public static bool operator <=(Money a, Money b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        EnsureSameCurrency(a, b);
        return a.Amount <= b.Amount;
    }

    /// <summary>
    /// Determines whether the first monetary value is greater than or equal to the second.
    /// </summary>
    /// <param name="a">The first monetary value.</param>
    /// <param name="b">The second monetary value.</param>
    /// <returns><c>true</c> if <paramref name="a"/> is greater than or equal to <paramref name="b"/>; otherwise <c>false</c>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="a"/> or <paramref name="b"/> is <c>null</c>.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the currencies differ.</exception>
    public static bool operator >=(Money a, Money b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        EnsureSameCurrency(a, b);
        return a.Amount >= b.Amount;
    }
}

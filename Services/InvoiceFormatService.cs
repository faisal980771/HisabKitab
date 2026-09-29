using System.Globalization;

namespace HisabKitab.Services;

public static class InvoiceFormatService
{
    public static readonly CultureInfo IndiaCulture = new("en-IN");

    public static string Money(decimal value, bool withSymbol = true)
    {
        var formatted = value.ToString("#,##0.00", IndiaCulture);
        return withSymbol ? $"₹ {formatted}" : formatted;
    }

    public static string Quantity(decimal value) => value.ToString("#,##0.##", IndiaCulture);

    public static string AmountInWords(decimal amount)
    {
        var negative = amount < 0;
        amount = Math.Abs(Math.Round(amount, 2));
        var rupees = (long)Math.Truncate(amount);
        var paise = (int)Math.Round((amount - rupees) * 100m, MidpointRounding.AwayFromZero);

        if (paise == 100)
        {
            rupees++;
            paise = 0;
        }

        var result = NumberToWords(rupees) + " Rupees";
        if (paise > 0)
            result += " and " + NumberToWords(paise) + " Paise";
        result += " Only";

        return negative ? "Minus " + result : result;
    }

    public static string Safe(string? value) => value?.Trim() ?? string.Empty;

    private static string NumberToWords(long number)
    {
        if (number == 0) return "Zero";
        if (number < 0) return "Minus " + NumberToWords(Math.Abs(number));

        var parts = new List<string>();
        if (number >= 10000000)
        {
            parts.Add(NumberToWords(number / 10000000));
            parts.Add("Crore");
            number %= 10000000;
        }

        if (number >= 100000)
        {
            parts.Add(NumberToWords(number / 100000));
            parts.Add("Lakh");
            number %= 100000;
        }

        if (number >= 1000)
        {
            parts.Add(NumberToWords(number / 1000));
            parts.Add("Thousand");
            number %= 1000;
        }

        if (number >= 100)
        {
            parts.Add(NumberToWords(number / 100));
            parts.Add("Hundred");
            number %= 100;
        }

        if (number > 0)
            parts.AddRange(BelowHundred(number));

        return string.Join(" ", parts);
    }

    private static IEnumerable<string> BelowHundred(long number)
    {
        string[] ones = { "Zero", "One", "Two", "Three", "Four", "Five", "Six", "Seven", "Eight", "Nine", "Ten", "Eleven", "Twelve", "Thirteen", "Fourteen", "Fifteen", "Sixteen", "Seventeen", "Eighteen", "Nineteen" };
        string[] tens = { "", "", "Twenty", "Thirty", "Forty", "Fifty", "Sixty", "Seventy", "Eighty", "Ninety" };

        if (number < 20)
        {
            yield return ones[number];
            yield break;
        }

        yield return tens[number / 10];
        if (number % 10 != 0)
            yield return ones[number % 10];
    }
}

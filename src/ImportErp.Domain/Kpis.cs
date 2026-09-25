namespace ImportErp.Domain;

public static class KpiCalculator
{
    public static RuptureRisk CalculateRuptureRisk(
        DateOnly? necessityDate,
        DateOnly? estimatedDeliveryDate,
        string? status)
    {
        if (IsFinal(status))
        {
            return RuptureRisk.NotApplicable;
        }

        if (necessityDate is null || estimatedDeliveryDate is null)
        {
            return RuptureRisk.Unknown;
        }

        return estimatedDeliveryDate > necessityDate.Value.AddDays(-7)
            ? RuptureRisk.Rupture
            : RuptureRisk.NoRisk;
    }

    public static TimingKpis CalculateTiming(
        DateOnly? scApprovalDate,
        DateOnly? poApprovalDate,
        DateOnly? poSentDate,
        DateOnly? blDate,
        DateOnly? arrivalDate,
        DateOnly? deliveryDate,
        DateOnly asOfDate)
    {
        bool? scOnTime = poSentDate is null
            ? null
            : poSentDate.Value.Day <= 8;

        return new TimingKpis(
            scOnTime,
            Difference(poApprovalDate, scApprovalDate),
            Difference(poSentDate, scApprovalDate),
            poSentDate is null ? null : poSentDate.Value.Day - 10,
            poSentDate is null ? null : (blDate ?? asOfDate).DayNumber - poSentDate.Value.DayNumber,
            Difference(arrivalDate, blDate),
            Difference(deliveryDate, arrivalDate));
    }

    private static bool IsFinal(string? status) => string.Equals(status, "CANCELLED", StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, "DELIVERED", StringComparison.OrdinalIgnoreCase);

    private static int? Difference(DateOnly? end, DateOnly? start) => end is not null && start is not null
        ? end.Value.DayNumber - start.Value.DayNumber
        : null;
}

public enum RuptureRisk
{
    Unknown,
    NoRisk,
    Rupture,
    NotApplicable
}

public sealed record TimingKpis(
    bool? ScOnTime,
    int? PoApprovalMinusScApprovalDays,
    int? PoSentMinusScApprovalDays,
    int? PoSentVsTenthDayOfMonth,
    int? LeadTimeHqDays,
    int? TransitDays,
    int? ClearanceToDeliveryDays);

namespace Domain.Settings
{
    public static class CaregiverEarningsPolicy
    {
        public const decimal DefaultConfiguredShareRate = 0.60m;
        public const decimal LegacyShareRate = 0.80m;

        public static decimal NormalizeConfiguredShareRate(decimal configuredShareRate)
        {
            if (configuredShareRate <= 0m || configuredShareRate >= 1m)
            {
                return DefaultConfiguredShareRate;
            }

            return configuredShareRate;
        }

        public static decimal ResolveOrderShareRate(decimal? orderSnapshotShareRate)
        {
            if (!orderSnapshotShareRate.HasValue || orderSnapshotShareRate.Value <= 0m || orderSnapshotShareRate.Value >= 1m)
            {
                return LegacyShareRate;
            }

            return orderSnapshotShareRate.Value;
        }
    }
}
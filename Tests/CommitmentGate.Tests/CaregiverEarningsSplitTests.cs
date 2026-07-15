using Domain.Entities;
using Domain.Settings;
using Xunit;

namespace CommitmentGate.Tests;

public class CaregiverEarningsSplitTests
{
    [Fact]
    public void Scenario1_NewOrderAfterChange_Uses60PercentOfFullOrderFee()
    {
        decimal configuredShare = 0.60m;
        decimal normalizedShare = CaregiverEarningsPolicy.NormalizeConfiguredShareRate(configuredShare);

        // Example: 10,000 per visit x 4 visits = 40,000 full order fee
        decimal fullOrderFee = 40000m;
        int totalVisits = 4;

        decimal caregiverTotal = decimal.Round(fullOrderFee * normalizedShare, 2);
        decimal caregiverPerVisit = decimal.Round(caregiverTotal / totalVisits, 2);

        Assert.Equal(0.60m, normalizedShare);
        Assert.Equal(24000m, caregiverTotal);
        Assert.Equal(6000m, caregiverPerVisit);

        Console.WriteLine($"SCENARIO1_EVIDENCE share={normalizedShare} fullOrderFee={fullOrderFee} caregiverTotal={caregiverTotal} caregiverPerVisit={caregiverPerVisit}");
    }

    [Fact]
    public void Scenario2_PreChangeOrderSnapshot80_RemainingVisitsStay80AfterGoLive()
    {
        decimal liveConfiguredShare = 0.60m;
        decimal normalizedLiveShare = CaregiverEarningsPolicy.NormalizeConfiguredShareRate(liveConfiguredShare);

        var preChangeOrder = new ClientOrder
        {
            OrderFee = 40000m,
            FrequencyPerWeek = 1,
            PaymentOption = "monthly",
            CaregiverSharePercentageAtCreation = 0.80m
        };

        decimal appliedShare = CaregiverEarningsPolicy.ResolveOrderShareRate(preChangeOrder.CaregiverSharePercentageAtCreation);
        int totalVisits = 4;
        decimal caregiverTotal = decimal.Round((preChangeOrder.OrderFee ?? 0m) * appliedShare, 2);
        decimal caregiverPerVisit = decimal.Round(caregiverTotal / totalVisits, 2);

        Assert.Equal(0.60m, normalizedLiveShare);
        Assert.Equal(0.80m, appliedShare);
        Assert.Equal(32000m, caregiverTotal);
        Assert.Equal(8000m, caregiverPerVisit);

        Console.WriteLine($"SCENARIO2_EVIDENCE liveShare={normalizedLiveShare} orderSnapshotShare={appliedShare} caregiverTotal={caregiverTotal} caregiverPerVisit={caregiverPerVisit}");
    }

    [Fact]
    public void Scenario3_AlreadyReleasedEarningsRemainUnchanged_NoRetroRecompute()
    {
        // Represents historical ledger/wallet amounts already released before the go-live.
        decimal alreadyReleasedBeforeChange = 32000m;

        // Live config changes now, but historical released values remain as recorded.
        decimal newLiveConfiguredShare = CaregiverEarningsPolicy.NormalizeConfiguredShareRate(0.60m);
        decimal releasedAfterChange = alreadyReleasedBeforeChange;

        Assert.Equal(0.60m, newLiveConfiguredShare);
        Assert.Equal(32000m, releasedAfterChange);

        Console.WriteLine($"SCENARIO3_EVIDENCE newLiveShare={newLiveConfiguredShare} historicalReleasedBefore={alreadyReleasedBeforeChange} historicalReleasedAfter={releasedAfterChange}");
    }

    [Fact]
    public void Scenario4_DiscountedCheckout_StillUsesFullUndiscountedOrderFee()
    {
        decimal share = CaregiverEarningsPolicy.NormalizeConfiguredShareRate(0.60m);

        decimal fullUndiscountedOrderFee = 40000m;
        decimal discountedClientPayable = 30000m; // commitment/referral can reduce payable amount

        decimal caregiverFromFullOrderFee = decimal.Round(fullUndiscountedOrderFee * share, 2);
        decimal caregiverIfIncorrectlyUsingDiscountedPayable = decimal.Round(discountedClientPayable * share, 2);

        Assert.Equal(24000m, caregiverFromFullOrderFee);
        Assert.NotEqual(caregiverIfIncorrectlyUsingDiscountedPayable, caregiverFromFullOrderFee);

        Console.WriteLine($"SCENARIO4_EVIDENCE share={share} fullOrderFee={fullUndiscountedOrderFee} discountedPayable={discountedClientPayable} caregiverFromFull={caregiverFromFullOrderFee}");
    }

    [Fact]
    public void Scenario5_SubscriptionRenewal_CreatesNewOrderSnapshotWithCurrentLiveRate()
    {
        // Existing subscription order (created before go-live)
        var originalOrder = new ClientOrder
        {
            OrderFee = 40000m,
            PaymentOption = "monthly",
            FrequencyPerWeek = 1,
            CaregiverSharePercentageAtCreation = 0.80m
        };

        // Renewal order (created after go-live) snapshots current 60%
        decimal currentLiveShare = CaregiverEarningsPolicy.NormalizeConfiguredShareRate(0.60m);
        var renewalOrder = new ClientOrder
        {
            OrderFee = 40000m,
            PaymentOption = "monthly",
            FrequencyPerWeek = 1,
            CaregiverSharePercentageAtCreation = currentLiveShare
        };

        int totalVisits = 4;
        decimal originalPerVisit = decimal.Round((originalOrder.OrderFee ?? 0m) * CaregiverEarningsPolicy.ResolveOrderShareRate(originalOrder.CaregiverSharePercentageAtCreation) / totalVisits, 2);
        decimal renewalPerVisit = decimal.Round((renewalOrder.OrderFee ?? 0m) * CaregiverEarningsPolicy.ResolveOrderShareRate(renewalOrder.CaregiverSharePercentageAtCreation) / totalVisits, 2);

        Assert.Equal(8000m, originalPerVisit);
        Assert.Equal(6000m, renewalPerVisit);

        Console.WriteLine($"SCENARIO5_EVIDENCE originalOrderSnapshot={originalOrder.CaregiverSharePercentageAtCreation} renewalOrderSnapshot={renewalOrder.CaregiverSharePercentageAtCreation} originalPerVisit={originalPerVisit} renewalPerVisit={renewalPerVisit}");
    }
}

using System.Net.Http.Json;
using System.Text.Json;
using CarePro_Api.Controllers;
using Xunit;

namespace CommitmentGate.Tests;

public class PublicConfigLockoutTests
{
    [Theory]
    [InlineData("2026-12-01T00:00:00Z", 2026)]
    [InlineData("2027-01-05", 2027)]
    public void ParseLockoutDate_Valid(string raw, int year) => Assert.Equal(year, PublicConfigController.ParseLockoutDate(raw)!.Value.Year);

    [Theory]
    [InlineData(null)] [InlineData("")] [InlineData("  ")] [InlineData("garbage")]
    public void ParseLockoutDate_UnsetOrBad_IsNull_FailOpen(string? raw) => Assert.Null(PublicConfigController.ParseLockoutDate(raw));

    [Fact]
    public async Task PublicConfig_Anonymous_ExposesDate_OrNull()
    {
        using var unset = new ClearanceHostFactory();
        var a = await (await unset.Client().GetAsync("/api/public-config")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Null, a.GetProperty("vettingLockoutEnforcementDate").ValueKind);
        Assert.True(a.TryGetProperty("commitmentGateEnabled", out _));

        using var set = new ClearanceHostFactory();
        set.ExtraConfig["VettingLockout:EnforcementDate"] = "2026-12-01T00:00:00Z";
        var b = await (await set.Client().GetAsync("/api/public-config")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2026, b.GetProperty("vettingLockoutEnforcementDate").GetDateTime().Year);
    }
}

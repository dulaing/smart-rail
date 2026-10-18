using SmartRail.Api.Infrastructure;

namespace SmartRail.Api.Tests.Catering;

public class StationIdTests
{
    // services/railbites/src/seed.ts copies these two IDs, because the services share identifiers
    // and no code. If the Rail station codes or the ID rule change, RailBites must change with them.
    [Fact]
    public void RailBites_collection_points_use_the_station_ids_Rail_seeds()
    {
        Assert.Equal(Guid.Parse("ea24d5bf-95a2-db6c-e85c-ff4705645a2f"), StableIds.For("station", "PLG"));
        Assert.Equal(Guid.Parse("04111fa9-fc9c-3657-e13b-d606dbe37c55"), StableIds.For("station", "PDN"));
    }
}

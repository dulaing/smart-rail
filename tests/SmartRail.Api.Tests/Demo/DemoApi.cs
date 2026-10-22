using Microsoft.AspNetCore.Hosting;
using SmartRail.Api.Modules.Rail.Running;
using SmartRail.Api.Tests.Catering;

namespace SmartRail.Api.Tests.Demo;

// The API with the Development-only demo routes mapped (the test host defaults to Development),
// the simulator switched on, a disposable PostgreSQL, a real Grilled for the outage switch and
// the test token signer standing in for Keycloak.
public sealed class DemoApi : CateringApi
{
    protected override void ConfigureHost(IWebHostBuilder builder)
    {
        base.ConfigureHost(builder);
        builder.UseSetting(SimulatorSetup.EnabledSetting, "true");
    }
}

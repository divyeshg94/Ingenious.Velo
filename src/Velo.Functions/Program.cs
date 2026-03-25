using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using Velo.Functions.Services;
using Velo.SQL;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureServices((ctx, services) =>
    {
        services.AddDbContext<VeloDbContext>(options =>
            options.UseSqlServer(ctx.Configuration["ConnectionStrings:VeloDb"]));

        services.AddScoped<IEventNormalizer, EventNormalizer>();
        services.AddScoped<IMetricsEngine, MetricsEngine>();
    })
    .ConfigureLogging(logging =>
    {
        logging.AddConsole();
    })
    .Build();

await host.RunAsync();

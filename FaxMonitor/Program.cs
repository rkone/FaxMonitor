using FaxMonitor;
using FaxMonitor.Data;
using Microsoft.EntityFrameworkCore;

IHost host = Host.CreateDefaultBuilder(args)
    .ConfigureServices((hostContext, services) =>
    {
        var config = hostContext.Configuration;
        services.AddWindowsService(opts => opts.ServiceName = "Fax Monitor");
        services.AddDbContextFactory<FaxDbContext>(options => options.UseSqlite(config.GetConnectionString("SqlLite")));
        services.AddHostedService<FaxMonitorService>();
        var logPath = config.GetValue<string>("LogPath") ?? string.Empty;
        services.AddLogging(loggingBuilder => {
            loggingBuilder.AddFile(Path.Combine(logPath, "FaxMonitor-{0:yyyy}-{0:MM}-{0:dd}.log"), opts =>
            {
                opts.FormatLogFileName = fName => string.Format(fName, DateTime.Now);
            });
        });
    })
    .Build();

using (var scope = host.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    var context = services.GetRequiredService<FaxDbContext>();
    context.Database.EnsureCreated();   
}
host.Run();

using SupplyMesh.Inventory.Worker;
using SupplyMesh.Shared;
var b = Host.CreateApplicationBuilder(args);
b.Services.AddSingleton<IDb>(new SqlDb(b.Configuration.GetConnectionString("Sql")!));
b.Services.AddSingleton<IMetrics>(new StatsdMetrics(b.Configuration["Statsd:Host"] ?? "telegraf", 8125));
b.Services.AddHostedService<PoEventConsumer>();
b.Build().Run();

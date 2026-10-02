using SupplyMesh.Shared;
var b = WebApplication.CreateBuilder(args);
b.UseSupplyMeshLogging("orders-api");
b.Services.AddControllers();
b.Services.AddEndpointsApiExplorer().AddSwaggerGen();
b.Services.AddSupplyMeshSecurity(b.Configuration);
b.Services.AddSingleton<IDb>(new SqlDb(b.Configuration.GetConnectionString("Sql")!));
b.Services.AddSingleton<IMetrics>(new StatsdMetrics(b.Configuration["Statsd:Host"] ?? "telegraf", 8125));
b.Services.AddHostedService<OutboxRelay>();      // transactional outbox -> Kafka
b.Services.AddScoped<SupplyMesh.Orders.Api.PoService>();
b.Services.AddHealthChecks();
b.Services.AddCors(o => o.AddDefaultPolicy(p => p.WithOrigins(b.Configuration["Cors:Origin"] ?? "http://localhost:5173").AllowAnyHeader().AllowAnyMethod()));
var app = b.Build();
app.UseObservability(); app.UseCors(); app.UseAuthentication(); app.UseAuthorization();
app.UseSwagger(); app.UseSwaggerUI();
app.MapControllers(); app.MapHealthChecks("/health/live"); app.MapHealthChecks("/health/ready");
app.Run();
public partial class Program { }

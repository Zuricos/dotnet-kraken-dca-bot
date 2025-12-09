using Kbot.MailService.Database;
using Kbot.MailService.Utility;
using Serilog;

var builder = Host.CreateApplicationBuilder(args);

builder.Configuration.Setup(builder.Environment.EnvironmentName);

Log.Logger = new LoggerConfiguration().ReadFrom.Configuration(builder.Configuration).CreateLogger();
builder.Logging.ClearProviders();
builder.Logging.AddSerilog();

builder.Services.Setup(builder.Configuration);

var host = builder.Build();

// Start the application
var migrationS = host.Services.GetRequiredService<MigrationService>();
await migrationS.StartAsync(CancellationToken.None);
host.Run();

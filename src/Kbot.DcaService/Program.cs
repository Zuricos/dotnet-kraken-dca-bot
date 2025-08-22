using Kbot.DcaService.Utility;
using Serilog;

var builder = Host.CreateApplicationBuilder(args);

builder.Configuration.Setup(builder.Environment.EnvironmentName);

Log.Logger = new LoggerConfiguration().ReadFrom.Configuration(builder.Configuration).CreateLogger();
builder.Logging.ClearProviders();
builder.Logging.AddSerilog();

builder.Services.Setup(builder.Configuration);

var host = builder.Build();

host.Run();

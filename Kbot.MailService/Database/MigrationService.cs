using Microsoft.EntityFrameworkCore;

namespace Kbot.MailService.Database;

public class MigrationService(IDbContextFactory<KrakenDbContext> dbContextFactory) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        int tries = 0;
        while (tries < 5)
        {
            try
            {
                await using var dbContext = dbContextFactory.CreateDbContext();
                await dbContext.Database.MigrateAsync(cancellationToken);
                return;
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                tries++;
                Task.Delay(1000).Wait(cancellationToken);
            }
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}

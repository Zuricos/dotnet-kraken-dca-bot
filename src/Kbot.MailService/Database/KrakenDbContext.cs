using Kbot.Common.Models;
using Microsoft.EntityFrameworkCore;

namespace Kbot.MailService.Database;

public class KrakenDbContext(DbContextOptions<KrakenDbContext> options) : DbContext(options)
{
  public DbSet<Order> Orders { get; set; } = null!;

  protected override void OnModelCreating(ModelBuilder modelBuilder)
  {
    modelBuilder.Entity<Order>(entity =>
    {
      entity.HasKey(e => e.OrderId);
      entity.Property(e => e.OrderId).IsRequired();
      entity.Property(e => e.Pair).IsRequired();
      entity.Property(e => e.Type).IsRequired();
      entity.Property(e => e.OrderType).IsRequired();
      entity.Property(e => e.Price).IsRequired();
      entity.Property(e => e.Volume).IsRequired();
      entity.Property(e => e.Cost).IsRequired();
      entity.Property(e => e.Fee).IsRequired();
    });
  }
}

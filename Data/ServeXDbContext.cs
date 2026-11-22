using System;
using System.IO;
using Microsoft.EntityFrameworkCore;
using ServeX.Models;

namespace ServeX.Data
{
    public class ServeXDbContext : DbContext
    {
        public ServeXDbContext()
        {
        }

        public ServeXDbContext(DbContextOptions<ServeXDbContext> options)
            : base(options)
        {
        }

        public DbSet<MenuItem> MenuItems { get; set; }

        public DbSet<AppSetting> Settings { get; set; }

        public DbSet<Table> Tables { get; set; }

        public DbSet<Room> Rooms { get; set; }

        public DbSet<Order> Orders { get; set; }

        public DbSet<OrderItem> OrderItems { get; set; }

        public DbSet<Bill> Bills { get; set; }

        public DbSet<User> Users { get; set; }

        private static readonly Lazy<ServeX.Infrastructure.Persistence.DatabaseLocation> DefaultLocation =
            new Lazy<ServeX.Infrastructure.Persistence.DatabaseLocation>(() =>
                ServeX.Infrastructure.Persistence.DatabaseLocation.Resolve(AppContext.BaseDirectory,
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    Environment.GetEnvironmentVariable("SERVEX_DATABASE_PATH")));

        public static string GetDatabaseFilePath()
        {
            return DefaultLocation.Value.FilePath;
        }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            if (!optionsBuilder.IsConfigured)
            {
                optionsBuilder.UseSqlite(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder { DataSource = GetDatabaseFilePath() }.ToString());
            }
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<MenuItem>(entity =>
            {
                entity.Property(item => item.Name).IsRequired().HasMaxLength(100);
                entity.Property(item => item.Category).IsRequired().HasMaxLength(50);
                entity.Property(item => item.Price).HasPrecision(18, 2);
            });

            modelBuilder.Entity<AppSetting>(entity =>
            {
                entity.ToTable("Settings");
                entity.Property(setting => setting.RestaurantName).IsRequired().HasMaxLength(120);
                entity.Property(setting => setting.Address).IsRequired().HasMaxLength(250);
                entity.Property(setting => setting.Phone).IsRequired().HasMaxLength(30);
                entity.Property(setting => setting.Locale).IsRequired();
                entity.Property(setting => setting.TaxName).IsRequired();
                entity.Property(setting => setting.TaxPercentage).HasPrecision(5, 2);
                entity.Property(setting => setting.LicenseKey).IsRequired().HasMaxLength(80);
            });

            modelBuilder.Entity<Table>(entity =>
            {
                entity.Property(table => table.TableNumber).IsRequired().HasMaxLength(20);
                entity.Property(table => table.Area).HasConversion<string>().HasMaxLength(20);
                entity.Property(table => table.Status).HasConversion<string>().HasMaxLength(30);
                entity.HasIndex(table => table.TableNumber).IsUnique();
            });

            modelBuilder.Entity<Room>(entity =>
            {
                entity.Property(room => room.RoomNumber).IsRequired().HasMaxLength(20);
                entity.Property(room => room.Status).HasConversion<string>().HasMaxLength(30);
                entity.HasIndex(room => room.RoomNumber).IsUnique();
            });

            modelBuilder.Entity<Order>(entity =>
            {
                entity.Property(order => order.Type).HasConversion<string>().HasMaxLength(20);
                entity.Property(order => order.CustomerName).HasMaxLength(80);
                entity.Property(order => order.Status).HasConversion<string>().HasMaxLength(30);
                entity.Property(order => order.CreatedAt).IsRequired();
            });

            modelBuilder.Entity<OrderItem>(entity =>
            {
                entity.Property(orderItem => orderItem.Price).HasPrecision(18, 2);

                entity.HasOne(orderItem => orderItem.Order)
                    .WithMany(order => order.Items)
                    .HasForeignKey(orderItem => orderItem.OrderId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(orderItem => orderItem.MenuItem)
                    .WithMany(menuItem => menuItem.OrderItems)
                    .HasForeignKey(orderItem => orderItem.MenuItemId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<Bill>(entity =>
            {
                entity.Property(bill => bill.Subtotal).HasPrecision(18, 2);
                entity.Property(bill => bill.Tax).HasPrecision(18, 2);
                entity.Property(bill => bill.Discount).HasPrecision(18, 2);
                entity.Property(bill => bill.Total).HasPrecision(18, 2);
                entity.Property(bill => bill.PaymentMethod).HasConversion<string>().HasMaxLength(20);

                entity.HasOne(bill => bill.Order)
                    .WithOne(order => order.Bill)
                    .HasForeignKey<Bill>(bill => bill.OrderId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<User>(entity =>
            {
                entity.Property(user => user.Username).IsRequired().HasMaxLength(50);
                entity.Property(user => user.Password).IsRequired().HasMaxLength(200);
                entity.HasIndex(user => user.Username).IsUnique();
            });
        }
    }
}

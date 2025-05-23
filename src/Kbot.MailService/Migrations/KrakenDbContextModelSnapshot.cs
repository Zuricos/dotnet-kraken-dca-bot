﻿// <auto-generated />
using System;
using Kbot.MailService.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Kbot.MailService.Migrations
{
    [DbContext(typeof(KrakenDbContext))]
    partial class KrakenDbContextModelSnapshot : ModelSnapshot
    {
        protected override void BuildModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder
                .HasAnnotation("ProductVersion", "9.0.0")
                .HasAnnotation("Relational:MaxIdentifierLength", 63);

            NpgsqlModelBuilderExtensions.UseIdentityByDefaultColumns(modelBuilder);

            modelBuilder.Entity("Kbot.Common.Models.Order", b =>
                {
                    b.Property<string>("OrderId")
                        .HasColumnType("text");

                    b.Property<string>("ClientOrderId")
                        .HasColumnType("text");

                    b.Property<DateTimeOffset?>("CloseTimeStamp")
                        .HasColumnType("timestamp with time zone");

                    b.Property<double>("Cost")
                        .HasColumnType("double precision");

                    b.Property<double>("Fee")
                        .HasColumnType("double precision");

                    b.Property<int>("OrderType")
                        .HasColumnType("integer");

                    b.Property<string>("Pair")
                        .IsRequired()
                        .HasColumnType("text");

                    b.Property<double>("Price")
                        .HasColumnType("double precision");

                    b.Property<int>("Status")
                        .HasColumnType("integer");

                    b.Property<int>("Type")
                        .HasColumnType("integer");

                    b.Property<double>("Volume")
                        .HasColumnType("double precision");

                    b.HasKey("OrderId");

                    b.ToTable("Orders");
                });
#pragma warning restore 612, 618
        }
    }
}

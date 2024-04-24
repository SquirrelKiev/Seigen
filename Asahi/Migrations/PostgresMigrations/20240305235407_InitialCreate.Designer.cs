﻿// <auto-generated />

using Asahi.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Asahi.Migrations.PostgresMigrations
{
    [DbContext(typeof(PostgresContext))]
    [Migration("20240305235407_InitialCreate")]
    partial class InitialCreate
    {
        /// <inheritdoc />
        protected override void BuildTargetModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder
                .HasAnnotation("ProductVersion", "8.0.2")
                .HasAnnotation("Relational:MaxIdentifierLength", 63);

            NpgsqlModelBuilderExtensions.UseIdentityByDefaultColumns(modelBuilder);

            modelBuilder.Entity("BotBase.Database.GuildPrefixPreference", b =>
                {
                    b.Property<long>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("bigint");

                    NpgsqlPropertyBuilderExtensions.UseIdentityByDefaultColumn(b.Property<long>("Id"));

                    b.Property<decimal>("GuildId")
                        .HasColumnType("numeric(20,0)");

                    b.Property<string>("Prefix")
                        .HasColumnType("text");

                    b.HasKey("Id");

                    b.ToTable("GuildPrefixPreferences");
                });

            modelBuilder.Entity("Seigen.Database.Models.CachedUserRole", b =>
                {
                    b.Property<decimal>("RoleId")
                        .HasColumnType("numeric(20,0)");

                    b.Property<decimal>("UserId")
                        .HasColumnType("numeric(20,0)");

                    b.Property<decimal>("GuildId")
                        .HasColumnType("numeric(20,0)");

                    b.HasKey("RoleId", "UserId");

                    b.HasIndex("RoleId");

                    b.ToTable("CachedUsersRoles");
                });

            modelBuilder.Entity("Seigen.Database.Models.Trackable", b =>
                {
                    b.Property<long>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("bigint");

                    NpgsqlPropertyBuilderExtensions.UseIdentityByDefaultColumn(b.Property<long>("Id"));

                    b.Property<decimal>("AssignableGuild")
                        .HasColumnType("numeric(20,0)");

                    b.Property<decimal>("AssignableRole")
                        .HasColumnType("numeric(20,0)");

                    b.Property<long>("Limit")
                        .HasColumnType("bigint");

                    b.Property<decimal>("LoggingChannel")
                        .HasColumnType("numeric(20,0)");

                    b.Property<decimal>("MonitoredGuild")
                        .HasColumnType("numeric(20,0)");

                    b.Property<decimal>("MonitoredRole")
                        .HasColumnType("numeric(20,0)");

                    b.HasKey("Id");

                    b.HasIndex("AssignableRole", "MonitoredRole")
                        .IsUnique();

                    b.ToTable("Trackables");
                });

            modelBuilder.Entity("Seigen.Database.Models.TrackedUser", b =>
                {
                    b.Property<long>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("bigint");

                    NpgsqlPropertyBuilderExtensions.UseIdentityByDefaultColumn(b.Property<long>("Id"));

                    b.Property<long>("TrackableId")
                        .HasColumnType("bigint");

                    b.Property<decimal>("UserId")
                        .HasColumnType("numeric(20,0)");

                    b.HasKey("Id");

                    b.HasIndex("TrackableId", "UserId")
                        .IsUnique();

                    b.ToTable("TrackedUsers");
                });

            modelBuilder.Entity("Seigen.Database.Models.TrackedUser", b =>
                {
                    b.HasOne("Seigen.Database.Models.Trackable", "Trackable")
                        .WithMany()
                        .HasForeignKey("TrackableId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.Navigation("Trackable");
                });
#pragma warning restore 612, 618
        }
    }
}

﻿// <auto-generated />
using System;
using LeaderBoard.ReadThrough.Infrastructure.Data.EFContext;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace LeaderBoard.ReadThrough.Infrastructure.Data.EFContext.Migrations
{
    [DbContext(typeof(LeaderBoardDBContext))]
    partial class LeaderBoardDBContextModelSnapshot : ModelSnapshot
    {
        protected override void BuildModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder
                .HasAnnotation("ProductVersion", "7.0.9")
                .HasAnnotation("Relational:MaxIdentifierLength", 63);

            NpgsqlModelBuilderExtensions.UseIdentityByDefaultColumns(modelBuilder);

            modelBuilder.Entity("LeaderBoard.ReadThrough.Models.PlayerScore", b =>
                {
                    b.Property<string>("PlayerId")
                        .HasMaxLength(25)
                        .HasColumnType("character varying(25)")
                        .HasColumnName("player_id");

                    b.Property<string>("Country")
                        .HasMaxLength(50)
                        .HasColumnType("character varying(50)")
                        .HasColumnName("country");

                    b.Property<DateTime>("CreatedAt")
                        .HasColumnType("timestamp with time zone")
                        .HasColumnName("created_at");

                    b.Property<string>("FirstName")
                        .HasMaxLength(50)
                        .HasColumnType("character varying(50)")
                        .HasColumnName("first_name");

                    b.Property<string>("LastName")
                        .HasColumnType("text")
                        .HasColumnName("last_name");

                    b.Property<string>("LeaderBoardName")
                        .IsRequired()
                        .HasMaxLength(50)
                        .HasColumnType("character varying(50)")
                        .HasColumnName("leader_board_name");

                    b.Property<long?>("Rank")
                        .HasColumnType("bigint")
                        .HasColumnName("rank");

                    b.Property<double>("Score")
                        .HasColumnType("double precision")
                        .HasColumnName("score");

                    b.Property<DateTime>("UpdatedAt")
                        .HasColumnType("timestamp with time zone")
                        .HasColumnName("updated_at");

                    b.HasKey("PlayerId")
                        .HasName("pk_player_score");

                    b.ToTable("player_score", (string)null);
                });
#pragma warning restore 612, 618
        }
    }
}

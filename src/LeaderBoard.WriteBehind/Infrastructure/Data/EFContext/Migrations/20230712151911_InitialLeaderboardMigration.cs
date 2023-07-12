﻿using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LeaderBoard.WriteBehind.Infrastructure.Data.EFContext.Migrations
{
    /// <inheritdoc />
    public partial class InitialLeaderboardMigration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "player_score",
                columns: table => new
                {
                    player_id = table.Column<string>(type: "character varying(25)", maxLength: 25, nullable: false),
                    score = table.Column<double>(type: "double precision", nullable: false),
                    leader_board_name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    rank = table.Column<long>(type: "bigint", nullable: true),
                    country = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    first_name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    last_name = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_player_score", x => x.player_id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "player_score");
        }
    }
}

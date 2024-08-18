﻿using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lyvads.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class addVerifiedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "VerifiedAt",
                table: "VerificationRecords",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "VerifiedAt",
                table: "VerificationRecords");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Torque.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Scheduling : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:btree_gist", ",,")
                .Annotation("Npgsql:PostgresExtension:pg_trgm", ",,");

            migrationBuilder.CreateTable(
                name: "bays",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bays", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "customers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    full_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    phone = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    is_archived = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_customers", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "services",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    category = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    duration_minutes = table.Column<int>(type: "integer", nullable: false),
                    price = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_services", x => x.id);
                    table.CheckConstraint("ck_services_duration", "duration_minutes > 0 AND duration_minutes % 15 = 0");
                    table.CheckConstraint("ck_services_price", "price >= 0");
                });

            migrationBuilder.CreateTable(
                name: "working_hours",
                columns: table => new
                {
                    day = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    opens = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    closes = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    is_closed = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_working_hours", x => x.day);
                    table.CheckConstraint("ck_working_hours_range", "is_closed OR closes > opens");
                });

            migrationBuilder.CreateTable(
                name: "vehicles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    make = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    model = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    year = table.Column<int>(type: "integer", nullable: true),
                    plate = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    vin = table.Column<string>(type: "character(17)", fixedLength: true, maxLength: 17, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_vehicles", x => x.id);
                    table.CheckConstraint("ck_vehicles_year", "year IS NULL OR year BETWEEN 1950 AND 2100");
                    table.ForeignKey(
                        name: "fk_vehicles_customers_customer_id",
                        column: x => x.customer_id,
                        principalTable: "customers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "appointments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    vehicle_id = table.Column<Guid>(type: "uuid", nullable: false),
                    bay_id = table.Column<Guid>(type: "uuid", nullable: false),
                    starts_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ends_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    comment = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    cancellation_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_appointments", x => x.id);
                    table.CheckConstraint("ck_appointments_status", "status IN ('Scheduled', 'CheckedIn', 'Completed', 'Cancelled', 'NoShow')");
                    table.CheckConstraint("ck_appointments_time", "ends_at > starts_at");
                    table.ForeignKey(
                        name: "fk_appointments_bays_bay_id",
                        column: x => x.bay_id,
                        principalTable: "bays",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_appointments_customers_customer_id",
                        column: x => x.customer_id,
                        principalTable: "customers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_appointments_vehicles_vehicle_id",
                        column: x => x.vehicle_id,
                        principalTable: "vehicles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "appointment_services",
                columns: table => new
                {
                    appointment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    service_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_appointment_services", x => new { x.appointment_id, x.service_id });
                    table.ForeignKey(
                        name: "fk_appointment_services_appointments_appointment_id",
                        column: x => x.appointment_id,
                        principalTable: "appointments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_appointment_services_services_service_id",
                        column: x => x.service_id,
                        principalTable: "services",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "working_hours",
                columns: new[] { "day", "closes", "is_closed", "opens" },
                values: new object[,]
                {
                    { "Friday", new TimeOnly(20, 0, 0), false, new TimeOnly(9, 0, 0) },
                    { "Monday", new TimeOnly(20, 0, 0), false, new TimeOnly(9, 0, 0) },
                    { "Saturday", new TimeOnly(20, 0, 0), false, new TimeOnly(9, 0, 0) },
                    { "Sunday", new TimeOnly(0, 0, 0), true, new TimeOnly(0, 0, 0) },
                    { "Thursday", new TimeOnly(20, 0, 0), false, new TimeOnly(9, 0, 0) },
                    { "Tuesday", new TimeOnly(20, 0, 0), false, new TimeOnly(9, 0, 0) },
                    { "Wednesday", new TimeOnly(20, 0, 0), false, new TimeOnly(9, 0, 0) }
                });

            migrationBuilder.CreateIndex(
                name: "ix_appointment_services_service_id",
                table: "appointment_services",
                column: "service_id");

            migrationBuilder.CreateIndex(
                name: "ix_appointments_bay_id",
                table: "appointments",
                column: "bay_id");

            migrationBuilder.CreateIndex(
                name: "ix_appointments_customer_id",
                table: "appointments",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "ix_appointments_starts_at",
                table: "appointments",
                column: "starts_at");

            migrationBuilder.CreateIndex(
                name: "ix_appointments_vehicle_id",
                table: "appointments",
                column: "vehicle_id");

            migrationBuilder.CreateIndex(
                name: "ix_bays_name",
                table: "bays",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_customers_phone",
                table: "customers",
                column: "phone",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_services_name",
                table: "services",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_vehicles_customer_id",
                table: "vehicles",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "ix_vehicles_plate",
                table: "vehicles",
                column: "plate",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_vehicles_vin",
                table: "vehicles",
                column: "vin",
                unique: true,
                filter: "vin IS NOT NULL");

            // ---- Hand-written SQL: things EF Core cannot express ------------------------------------

            // THE core business rule: in one bay, two active appointments can never overlap in time.
            // '[)' = half-open range, so 10:00–11:00 and 11:00–12:00 do NOT conflict.
            // Cancelled / completed / no-show appointments are excluded by the WHERE and free the slot.
            // Enforced inside PostgreSQL, so it holds even for two requests at the same millisecond
            // (an "if slot is free then insert" check in C# would have a race window).
            migrationBuilder.Sql("""
                ALTER TABLE appointments ADD CONSTRAINT ex_appointments_no_overlap_per_bay
                EXCLUDE USING gist (
                    bay_id WITH =,
                    tstzrange(starts_at, ends_at, '[)') WITH &&
                ) WHERE (status IN ('Scheduled', 'CheckedIn'));
                """);

            // "Name contains …" search: a normal B-tree index cannot help LIKE '%text%'; a trigram index can.
            migrationBuilder.Sql("""
                CREATE INDEX ix_customers_full_name_trgm ON customers USING gin (lower(full_name) gin_trgm_ops);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_customers_full_name_trgm;");
            migrationBuilder.Sql("ALTER TABLE appointments DROP CONSTRAINT IF EXISTS ex_appointments_no_overlap_per_bay;");

            migrationBuilder.DropTable(
                name: "appointment_services");

            migrationBuilder.DropTable(
                name: "working_hours");

            migrationBuilder.DropTable(
                name: "appointments");

            migrationBuilder.DropTable(
                name: "services");

            migrationBuilder.DropTable(
                name: "bays");

            migrationBuilder.DropTable(
                name: "vehicles");

            migrationBuilder.DropTable(
                name: "customers");

            migrationBuilder.AlterDatabase()
                .OldAnnotation("Npgsql:PostgresExtension:btree_gist", ",,")
                .OldAnnotation("Npgsql:PostgresExtension:pg_trgm", ",,");
        }
    }
}

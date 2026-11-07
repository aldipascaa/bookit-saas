using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BookIt.Api.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Database-level double-booking protection. SQLite has no exclusion constraints, so two triggers reject any
    /// insert/update that would make a non-cancelled booking overlap another non-cancelled booking of the same
    /// service. Triggers run inside the writing statement under SQLite's single-writer lock, so concurrent
    /// requests (and multiple app instances sharing the file) cannot both pass. The message text must match
    /// BookItDb.OverlapTriggerMessage. Status 2 = BookingStatus.Cancelled.
    /// </summary>
    public partial class AddBookingOverlapGuard : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
CREATE TRIGGER TR_Bookings_NoOverlap_Insert
BEFORE INSERT ON Bookings
WHEN NEW.Status <> 2
BEGIN
    SELECT RAISE(ABORT, 'booking_overlap')
    WHERE EXISTS (
        SELECT 1 FROM Bookings b
        WHERE b.ServiceId = NEW.ServiceId AND b.Status <> 2
          AND b.StartTime < NEW.EndTime AND NEW.StartTime < b.EndTime);
END;");

            migrationBuilder.Sql(@"
CREATE TRIGGER TR_Bookings_NoOverlap_Update
BEFORE UPDATE OF ServiceId, StartTime, EndTime, Status ON Bookings
WHEN NEW.Status <> 2
BEGIN
    SELECT RAISE(ABORT, 'booking_overlap')
    WHERE EXISTS (
        SELECT 1 FROM Bookings b
        WHERE b.Id <> NEW.Id AND b.ServiceId = NEW.ServiceId AND b.Status <> 2
          AND b.StartTime < NEW.EndTime AND NEW.StartTime < b.EndTime);
END;");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS TR_Bookings_NoOverlap_Update;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS TR_Bookings_NoOverlap_Insert;");
        }
    }
}

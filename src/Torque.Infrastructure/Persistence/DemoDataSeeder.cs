using Microsoft.EntityFrameworkCore;
using Torque.Domain.Catalog;
using Torque.Domain.Customers;
using Torque.Domain.Scheduling;

namespace Torque.Infrastructure.Persistence;

/// <summary>
/// Realistic sample data for demos and manual testing (Seed:DemoData = true). Never enabled in tests
/// or real deployments. Idempotent: does nothing if bays already exist.
/// </summary>
internal static class DemoDataSeeder
{
    public static async Task<bool> SeedAsync(AppDbContext db, int currentYear, CancellationToken ct)
    {
        if (await db.Bays.AnyAsync(ct))
        {
            return false;
        }

        db.Bays.AddRange(Bay.Create("Bay 1 — Tyres"), Bay.Create("Bay 2 — Tyres"), Bay.Create("Bay 3 — Lift"));

        db.Services.AddRange(
            ServiceOffering.Create("Tyre change R13–R15", "Tyres", 30, 1600m),
            ServiceOffering.Create("Tyre change R16–R18", "Tyres", 45, 2200m),
            ServiceOffering.Create("Tyre change R19+", "Tyres", 60, 3000m),
            ServiceOffering.Create("Wheel balancing (4 wheels)", "Tyres", 30, 1200m),
            ServiceOffering.Create("Puncture repair", "Tyres", 15, 500m),
            ServiceOffering.Create("Seasonal tyre storage", "Storage", 15, 4000m),
            ServiceOffering.Create("Oil and filter change", "Maintenance", 45, 1500m),
            ServiceOffering.Create("Wheel alignment", "Maintenance", 60, 2500m));

        var customers = new (string Name, string Phone, string Make, string Model, int Year, string Plate)[]
        {
            ("Ivan Petrov", "+7 912 345-67-01", "Lada", "Vesta", 2021, "А123ВС96"),
            ("Maria Sokolova", "8 (912) 345-67-02", "Kia", "Rio", 2019, "В456ОР196"),
            ("Alexey Smirnov", "9123456703", "Toyota", "Camry", 2022, "Е789КХ96"),
            ("Olga Kuznetsova", "+79123456704", "Hyundai", "Creta", 2020, "М321НТ196"),
            ("Dmitry Volkov", "+7 912 345 67 05", "Volkswagen", "Polo", 2018, "С654УХ96"),
        };

        foreach (var c in customers)
        {
            var customer = Customer.Create(c.Name, c.Phone, null, null);
            db.Customers.Add(customer);
            db.Vehicles.Add(Vehicle.Create(customer.Id, c.Make, c.Model, c.Year, c.Plate, null, currentYear));
        }

        await db.SaveChangesAsync(ct);
        return true;
    }
}

using Torque.Domain.Catalog;
using Torque.Domain.Common;
using Torque.Domain.Customers;

namespace Torque.UnitTests.Domain;

public class PhoneNumberTests
{
    [Theory]
    [InlineData("+7 912 345-67-89")]
    [InlineData("8 (912) 345-67-89")]
    [InlineData("89123456789")]
    [InlineData("9123456789")]
    [InlineData("+7(912)3456789")]
    public void Russian_formats_normalize_to_E164(string raw) =>
        PhoneNumber.Normalize(raw).ShouldBe("+79123456789");

    [Fact]
    public void Foreign_numbers_keep_their_country_code() =>
        PhoneNumber.Normalize("+49 30 1234 5678").ShouldBe("+493012345678");

    [Theory]
    [InlineData("")]
    [InlineData("12345")]
    [InlineData("abc")]
    [InlineData("+1234567890123456")]
    public void Invalid_numbers_are_rejected(string raw) =>
        Should.Throw<DomainException>(() => PhoneNumber.Normalize(raw));
}

public class LicensePlateTests
{
    [Theory]
    [InlineData("А123ВС96")]   // Cyrillic
    [InlineData("A123BC96")]   // Latin
    [InlineData("a 123 bc 96")]
    [InlineData("А123-ВС-96")]
    public void Cyrillic_and_latin_spellings_are_the_same_plate(string raw) =>
        LicensePlate.Normalize(raw).ShouldBe("A123BC96");

    [Theory]
    [InlineData("")]
    [InlineData("A1")]
    [InlineData("Ж123ЖЖ96")] // Ж is not a plate letter
    public void Invalid_plates_are_rejected(string raw) =>
        Should.Throw<DomainException>(() => LicensePlate.Normalize(raw));
}

public class VehicleTests
{
    [Fact]
    public void Valid_vehicle_is_normalized()
    {
        var v = Vehicle.Create(Guid.NewGuid(), " Lada ", "Vesta", 2021, "а123вс96", "xta12345678901234", 2026);

        v.Make.ShouldBe("Lada");
        v.Plate.ShouldBe("A123BC96");
        v.Vin.ShouldBe("XTA12345678901234");
    }

    [Theory]
    [InlineData("SHORT")]
    [InlineData("XTA1234567890123O")] // letter O is never used in VINs
    public void Invalid_vin_is_rejected(string vin) =>
        Should.Throw<DomainException>(() => Vehicle.Create(Guid.NewGuid(), "Lada", "Vesta", 2021, "A123BC96", vin, 2026));

    [Fact]
    public void Year_from_the_future_is_rejected() =>
        Should.Throw<DomainException>(() => Vehicle.Create(Guid.NewGuid(), "Lada", "Vesta", 2030, "A123BC96", null, 2026));
}

public class ServiceOfferingTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(20)]   // not a multiple of 15
    [InlineData(495)]  // more than 8 hours
    public void Duration_must_fit_the_15_minute_grid(int minutes) =>
        Should.Throw<DomainException>(() => ServiceOffering.Create("Tyre change", "Tyres", minutes, 1000m));

    [Theory]
    [InlineData(-1)]
    [InlineData(10.555)]
    public void Price_must_be_non_negative_money(decimal price) =>
        Should.Throw<DomainException>(() => ServiceOffering.Create("Tyre change", "Tyres", 30, price));
}

using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;
using Torque.Application.Abstractions;

namespace Torque.Infrastructure;

public sealed class BusinessOptions
{
    public const string SectionName = "Business";

    /// <summary>IANA time zone id, e.g. "Asia/Yekaterinburg".</summary>
    [Required]
    public string TimeZone { get; init; } = "Asia/Yekaterinburg";
}

public sealed class BusinessTimeZone(IOptions<BusinessOptions> options) : IBusinessTimeZone
{
    public TimeZoneInfo TimeZone { get; } = TimeZoneInfo.FindSystemTimeZoneById(options.Value.TimeZone);
}

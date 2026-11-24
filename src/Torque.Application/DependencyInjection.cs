using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Torque.Application.Auditing;
using Torque.Application.Auth;
using Torque.Application.Catalog;
using Torque.Application.Customers;
using Torque.Application.Messaging;
using Torque.Application.Orders;
using Torque.Application.Reports;
using Torque.Application.Scheduling;
using Torque.Application.Users;

namespace Torque.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        // Scoped = one instance per HTTP request, same lifetime as the DbContext they use.
        services.AddSingleton<Common.TorqueMetrics>(); // IMeterFactory is provided by the host

        services.AddScoped<AuthService>();
        services.AddScoped<UserService>();
        services.AddScoped<CustomerService>();
        services.AddScoped<CatalogService>();
        services.AddScoped<BusinessCalendarProvider>();
        services.AddScoped<AppointmentService>();
        services.AddScoped<WorkOrderService>();
        services.AddScoped<PaymentService>();
        services.AddScoped<AuditService>();
        services.AddScoped<ReportService>();
        services.AddScoped<IOutboxMessageHandler, AppointmentReminderHandler>();

        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly, ServiceLifetime.Singleton);
        return services;
    }
}

using Torque.Domain.Common;
using Torque.Domain.Orders;

namespace Torque.UnitTests.Domain;

public class WorkOrderTests
{
    private static readonly DateTimeOffset Now = new(2026, 12, 1, 10, 0, 0, TimeSpan.Zero);

    private static WorkOrder OrderWith(params decimal[] prices)
    {
        var order = WorkOrder.Open(Guid.NewGuid(), Guid.NewGuid(), null);
        foreach (var price in prices)
        {
            order.AddService(Guid.NewGuid(), $"Service {price}", price, 1);
        }

        return order;
    }

    private static WorkOrder CompletedOrder(params decimal[] prices)
    {
        var order = OrderWith(prices);
        order.Start(Guid.NewGuid());
        order.Complete(Now);
        return order;
    }

    [Fact]
    public void Totals_follow_the_lines()
    {
        var order = OrderWith(1600m);
        order.AddPart("Valve", 150.50m, 4);

        order.Subtotal.ShouldBe(2202m);
        order.Total.ShouldBe(2202m);
        order.AmountDue.ShouldBe(2202m);
    }

    [Fact]
    public void Percent_discount_is_rounded_to_kopecks()
    {
        var order = OrderWith(999.99m);

        order.ApplyDiscount(DiscountType.Percent, 10, approvedByAdmin: false);

        order.DiscountAmount.ShouldBe(100.00m); // 99.999 → 100.00
        order.Total.ShouldBe(899.99m);
    }

    [Fact]
    public void Amount_discount_cannot_exceed_subtotal()
    {
        var order = OrderWith(500m);

        order.ApplyDiscount(DiscountType.Amount, 800m, approvedByAdmin: true);

        order.Total.ShouldBe(0m);
    }

    [Fact]
    public void Large_discount_needs_admin_and_a_refusal_changes_nothing()
    {
        var order = OrderWith(1000m);

        Should.Throw<DiscountApprovalRequiredException>(() => order.ApplyDiscount(DiscountType.Percent, 20, approvedByAdmin: false));
        order.Total.ShouldBe(1000m);
        order.DiscountType.ShouldBeNull();

        order.ApplyDiscount(DiscountType.Percent, 20, approvedByAdmin: true);
        order.Total.ShouldBe(800m);
    }

    [Fact]
    public void Discount_rule_also_applies_to_fixed_amounts() =>
        Should.Throw<DiscountApprovalRequiredException>(() =>
            OrderWith(1000m).ApplyDiscount(DiscountType.Amount, 151m, approvedByAdmin: false));

    [Fact]
    public void Cannot_complete_empty_order()
    {
        var order = WorkOrder.Open(Guid.NewGuid(), Guid.NewGuid(), null);
        order.Start(Guid.NewGuid());

        Should.Throw<DomainException>(() => order.Complete(Now));
    }

    [Fact]
    public void Completed_order_cannot_be_edited() =>
        Should.Throw<InvalidStateException>(() => CompletedOrder(1000m).AddPart("Extra", 100m, 1));

    [Fact]
    public void Full_payment_closes_a_completed_order()
    {
        var order = CompletedOrder(1000m);

        order.RegisterPayment(400m, Now);
        order.Status.ShouldBe(WorkOrderStatus.Completed);

        order.RegisterPayment(600m, Now);
        order.Status.ShouldBe(WorkOrderStatus.Closed);
        order.ClosedAt.ShouldBe(Now);
    }

    [Fact]
    public void Prepayment_then_completion_closes_immediately()
    {
        var order = OrderWith(1000m);
        order.Start(Guid.NewGuid());
        order.RegisterPayment(1000m, Now);

        order.Complete(Now);

        order.Status.ShouldBe(WorkOrderStatus.Closed);
    }

    [Theory]
    [InlineData(1000.01)]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(10.001)]
    public void Invalid_or_excessive_payments_are_rejected(decimal amount) =>
        Should.Throw<DomainException>(() => CompletedOrder(1000m).RegisterPayment(amount, Now));

    [Fact]
    public void Refund_reopens_a_closed_order_and_cannot_exceed_paid()
    {
        var order = CompletedOrder(1000m);
        order.RegisterPayment(1000m, Now);

        Should.Throw<DomainException>(() => order.RegisterRefund(1000.01m));
        order.RegisterRefund(300m);

        order.Status.ShouldBe(WorkOrderStatus.Completed);
        order.AmountDue.ShouldBe(300m);
    }

    [Fact]
    public void Paid_order_cannot_be_cancelled()
    {
        var order = OrderWith(1000m);
        order.Start(Guid.NewGuid());
        order.RegisterPayment(100m, Now);

        Should.Throw<DomainException>(order.Cancel);
    }

    [Fact]
    public void Removing_a_line_below_the_paid_amount_is_rejected_and_leaves_the_order_unchanged()
    {
        var order = OrderWith(1000m, 500m);
        order.Start(Guid.NewGuid());
        order.RegisterPayment(1200m, Now);
        var expensive = order.Lines.Single(l => l.UnitPrice == 1000m);

        Should.Throw<DomainException>(() => order.RemoveLine(expensive.Id));

        order.Lines.Count.ShouldBe(2);
        order.Total.ShouldBe(1500m);
    }
}

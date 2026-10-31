namespace SpazaSnap;
public record Product(int Id, string Name, string Category, string Unit, decimal Price, int Quantity, int ReorderLevel, decimal DailySales);
public record ProductInput(string Name, string Category, string Unit, decimal Price, int Quantity, int ReorderLevel, decimal DailySales);
public record Adjustment(int Quantity, string Reason, int? PhotoId = null);
public record Restock(int ProductId, string Name, int Quantity, decimal DaysLeft, int SuggestedQuantity, string Urgency);
public record OrderLine(int ProductId, int Quantity);
public record OrderInput(string Supplier, string Channel, List<OrderLine> Items);
public record OrderSummary(int Id, string Supplier, string Channel, string Status, DateTime CreatedAt, List<OrderDetail> Items);
public record OrderDetail(int ProductId, string Name, int Quantity, decimal UnitCost);
public static class Forecast
{
    public static Restock For(Product p)
    {
        var days = p.DailySales > 0 ? Math.Round(p.Quantity / p.DailySales, 1) : -1;
        var target = Math.Max(p.ReorderLevel * 2, (int)Math.Ceiling(p.DailySales * 7));
        return new(p.Id, p.Name, p.Quantity, days, Math.Max(0, target - p.Quantity),
            p.Quantity == 0 ? "Out of stock" : p.Quantity <= p.ReorderLevel || days is >= 0 and <= 3 ? "Order soon" : "Healthy");
    }
}

using Xunit;
namespace SpazaSnap.Tests;

public class ForecastTests
{
    private static Product Sample => new(1,"Maize meal","Staples","2.5 kg",38.99m,8,10,3);
    [Fact] public void UsesSevenDaySalesWhenHigherThanBuffer()
    { var r=Forecast.For(Sample); Assert.Equal(13,r.SuggestedQuantity); Assert.Equal(2.7m,r.DaysLeft); Assert.Equal("Order soon",r.Urgency); }
    [Fact] public void UsesReorderBufferWhenHigherThanSales()
    { Assert.Equal(12,Forecast.For(Sample with {DailySales=1}).SuggestedQuantity); }
    [Fact] public void RoundsFractionalSalesTargetUp()
    { Assert.Equal(2,Forecast.For(Sample with {Quantity=0,ReorderLevel=0,DailySales=0.2m}).SuggestedQuantity); }
    [Fact] public void NeverSuggestsNegativeOrder()
    { Assert.Equal(0,Forecast.For(Sample with {Quantity=100}).SuggestedQuantity); }
    [Fact] public void UnknownCoverageWhenSalesAreZero()
    { Assert.Equal(-1,Forecast.For(Sample with {DailySales=0}).DaysLeft); }
    [Fact] public void ZeroSalesStillUsesReorderBuffer()
    { Assert.Equal(12,Forecast.For(Sample with {DailySales=0}).SuggestedQuantity); }
    [Theory]
    [InlineData(0,10,3,"Out of stock")]
    [InlineData(0,0,0,"Out of stock")]
    [InlineData(10,10,1,"Order soon")]
    [InlineData(12,2,4,"Order soon")]
    [InlineData(13,2,4,"Healthy")]
    [InlineData(100,10,0,"Healthy")]
    public void ClassifiesStockAtImportantBoundaries(int quantity,int reorder,int daily,string expected)
    { Assert.Equal(expected,Forecast.For(Sample with {Quantity=quantity,ReorderLevel=reorder,DailySales=daily}).Urgency); }
    [Fact] public void KeepsProductIdentityInSuggestion()
    { var r=Forecast.For(Sample); Assert.Equal(Sample.Id,r.ProductId); Assert.Equal(Sample.Name,r.Name); Assert.Equal(Sample.Quantity,r.Quantity); }
}

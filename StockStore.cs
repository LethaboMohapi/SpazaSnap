using MySqlConnector;
namespace SpazaSnap;

public sealed class StockStore(IConfiguration config, TenantContext tenant)
{
    private async Task<MySqlConnection> OpenAsync()
    { var c = new MySqlConnection(config.GetConnectionString("MySql")); try { await c.OpenAsync(); return c; } catch { await c.DisposeAsync(); throw; } }
    private MySqlCommand Cmd(MySqlConnection c, string sql, MySqlTransaction? tx = null, params (string, object?)[] values)
    { var cmd = new MySqlCommand(sql, c, tx); if(sql.Contains("@shop"))cmd.Parameters.AddWithValue("@shop",tenant.ShopId); foreach (var (key, value) in values) cmd.Parameters.AddWithValue(key, value ?? DBNull.Value); return cmd; }
    private static string Text(string? value, string label, int max = 120)
    { if (string.IsNullOrWhiteSpace(value) || value.Trim().Length > max) throw new ArgumentException($"{label} is required and must be at most {max} characters."); return value.Trim(); }
    public async Task PingAsync() { await using var c = await OpenAsync(); await using var cmd = Cmd(c, "SELECT COUNT(*) FROM products"); await cmd.ExecuteScalarAsync(); }
    public async Task InitializeAsync()
    {
        await using var c = await OpenAsync();
        var sql = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "database", "schema.sql"));
        await using var cmd = Cmd(c, sql); await cmd.ExecuteNonQueryAsync();

    }
    public async Task<List<Product>> ProductsAsync()
    {
        await using var c = await OpenAsync(); await using var cmd = Cmd(c, "SELECT id,name,category,unit,price,quantity,reorder_level,daily_sales FROM products WHERE shop_id=@shop ORDER BY name");
        await using var r = await cmd.ExecuteReaderAsync(); var list = new List<Product>();
        while (await r.ReadAsync()) list.Add(new(r.GetInt32(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetDecimal(4), r.GetInt32(5), r.GetInt32(6), r.GetDecimal(7)));
        return list;
    }
    public async Task<Product> AddProductAsync(ProductInput p)
    {
        var name = Text(p.Name, "Name"); var category = Text(p.Category, "Category", 60); var unit = Text(p.Unit, "Unit", 40);
        if (p.Price is < 0 or > 999999 || p.Quantity is < 0 or > 1000000 || p.ReorderLevel is < 0 or > 1000000 || p.DailySales is < 0 or > 100000)
            throw new ArgumentException("Price, stock, reorder level, and daily sales must be within the allowed non-negative range.");
        await using var c = await OpenAsync(); await using var tx = await c.BeginTransactionAsync();
        await using var shop = Cmd(c,"SELECT id FROM shops WHERE id=@shop FOR UPDATE",tx); await shop.ExecuteScalarAsync();
        await using var count = Cmd(c,"SELECT COUNT(*) FROM products WHERE shop_id=@shop",tx);
        if(Convert.ToInt32(await count.ExecuteScalarAsync())>=100)throw new ArgumentException("The Free plan supports 100 products per shop.");
        await using var cmd = Cmd(c, "INSERT INTO products(shop_id,name,category,unit,price,quantity,reorder_level,daily_sales) VALUES(@shop,@n,@c,@u,@p,@q,@r,@d)", tx,
            ("@n",name),("@c",category),("@u",unit),("@p",p.Price),("@q",p.Quantity),("@r",p.ReorderLevel),("@d",p.DailySales));
        await cmd.ExecuteNonQueryAsync(); await tx.CommitAsync(); return new((int)cmd.LastInsertedId, name, category, unit, p.Price, p.Quantity, p.ReorderLevel, p.DailySales);
    }
    public async Task AdjustAsync(int id, Adjustment a)
    {
        if (a.Quantity is < 0 or > 1000000) throw new ArgumentException("Count must be between 0 and 1,000,000.");
        var reason = Text(a.Reason, "Reason", 240);
        await using var c = await OpenAsync(); await using var tx = await c.BeginTransactionAsync();
        await using var read = Cmd(c, "SELECT quantity FROM products WHERE id=@id AND shop_id=@shop FOR UPDATE", tx, ("@id",id));
        var old = await read.ExecuteScalarAsync() ?? throw new KeyNotFoundException("Product not found.");
        if (a.PhotoId.HasValue)
        {
            await using var photo = Cmd(c, "SELECT COUNT(*) FROM photos WHERE id=@id AND shop_id=@shop",tx,("@id",a.PhotoId));
            if (Convert.ToInt32(await photo.ExecuteScalarAsync()) == 0) throw new ArgumentException("Photo not found.");
        }
        await using var update = Cmd(c, "UPDATE products SET quantity=@q WHERE id=@id AND shop_id=@shop", tx,("@q",a.Quantity),("@id",id)); await update.ExecuteNonQueryAsync();
        await using var log = Cmd(c, "INSERT INTO stock_movements(shop_id,product_id,old_quantity,new_quantity,reason,photo_id) VALUES(@shop,@id,@old,@q,@reason,@photo)",tx,
            ("@id",id),("@old",old),("@q",a.Quantity),("@reason",reason),("@photo",a.PhotoId)); await log.ExecuteNonQueryAsync(); await tx.CommitAsync();
    }
    public async Task<List<Restock>> RestocksAsync() => (await ProductsAsync()).Select(Forecast.For).OrderBy(x => x.DaysLeft < 0 ? decimal.MaxValue : x.DaysLeft).ToList();
    public async Task<int> SavePhotoAsync(string name)
    {
        await using var c=await OpenAsync();await using var tx=await c.BeginTransactionAsync();
        await using var shop=Cmd(c,"SELECT id FROM shops WHERE id=@shop FOR UPDATE",tx);await shop.ExecuteScalarAsync();
        await using var count=Cmd(c,"SELECT COUNT(*) FROM photos WHERE shop_id=@shop",tx);
        if(Convert.ToInt32(await count.ExecuteScalarAsync())>=1000)throw new ArgumentException("The Free plan supports 1,000 photos per shop.");
        await using var cmd=Cmd(c,"INSERT INTO photos(shop_id,file_name) VALUES(@shop,@n)",tx,("@n",name));await cmd.ExecuteNonQueryAsync();await tx.CommitAsync();return (int)cmd.LastInsertedId;
    }
    public async Task<string> PhotoAsync(int id)
    { await using var c = await OpenAsync(); await using var cmd = Cmd(c,"SELECT file_name FROM photos WHERE id=@id AND shop_id=@shop",null,("@id",id)); return (string?)await cmd.ExecuteScalarAsync() ?? throw new KeyNotFoundException("Photo not found."); }
    public async Task<int> CreateOrderAsync(OrderInput o)
    {
        var supplier = Text(o.Supplier,"Supplier");
        if (o.Channel is not ("Online draft" or "USSD draft")) throw new ArgumentException("Choose Online draft or USSD draft.");
        if (o.Items is null || o.Items.Count is < 1 or > 100 || o.Items.Any(i => i.Quantity is < 1 or > 100000) || o.Items.Select(i => i.ProductId).Distinct().Count() != o.Items.Count)
            throw new ArgumentException("Choose 1–100 distinct products with quantities from 1 to 100,000.");
        await using var c = await OpenAsync(); await using var tx = await c.BeginTransactionAsync();
        await using var cmd = Cmd(c,"INSERT INTO orders(shop_id,supplier,channel) VALUES(@shop,@s,@c)",tx,("@s",supplier),("@c",o.Channel)); await cmd.ExecuteNonQueryAsync(); var id = (int)cmd.LastInsertedId;
        foreach (var item in o.Items)
        {
            await using var read = Cmd(c,"SELECT price FROM products WHERE id=@id AND shop_id=@shop",tx,("@id",item.ProductId));
            var price = await read.ExecuteScalarAsync() ?? throw new ArgumentException("An order product no longer exists.");
            // Catalogue price is a retail estimate, not a supplier quote.
            await using var line = Cmd(c,"INSERT INTO order_items(shop_id,order_id,product_id,quantity,unit_cost) VALUES(@shop,@o,@p,@q,@c)",tx,("@o",id),("@p",item.ProductId),("@q",item.Quantity),("@c",price)); await line.ExecuteNonQueryAsync();
        }
        await tx.CommitAsync(); return id;
    }
    public async Task<List<OrderSummary>> OrdersAsync()
    {
        await using var c = await OpenAsync();
        await using var cmd = Cmd(c,"SELECT o.id,o.supplier,o.channel,o.status,o.created_at,i.product_id,p.name,i.quantity,i.unit_cost FROM orders o JOIN order_items i ON i.order_id=o.id JOIN products p ON p.id=i.product_id AND p.shop_id=o.shop_id WHERE o.shop_id=@shop ORDER BY o.id DESC,i.product_id");
        await using var r = await cmd.ExecuteReaderAsync(); var orders = new List<OrderSummary>();
        while (await r.ReadAsync())
        {
            var order = orders.LastOrDefault();
            if (order?.Id != r.GetInt32(0)) { order = new(r.GetInt32(0),r.GetString(1),r.GetString(2),r.GetString(3),r.GetDateTime(4),[]); orders.Add(order); }
            order.Items.Add(new(r.GetInt32(5),r.GetString(6),r.GetInt32(7),r.GetDecimal(8)));
        }
        return orders;
    }
    public async Task ReceiveAsync(int id)
    {
        await using var c = await OpenAsync(); await using var tx = await c.BeginTransactionAsync();
        await using var read = Cmd(c,"SELECT status FROM orders WHERE id=@id AND shop_id=@shop FOR UPDATE",tx,("@id",id));
        var status = (string?)await read.ExecuteScalarAsync() ?? throw new KeyNotFoundException("Order not found.");
        if (status == "Received") throw new ArgumentException("This order was already received.");
        var items = new List<(int Id,int Qty)>();
        await using (var cmd = Cmd(c,"SELECT product_id,quantity FROM order_items WHERE order_id=@id AND shop_id=@shop ORDER BY product_id",tx,("@id",id)))
        await using (var r = await cmd.ExecuteReaderAsync()) { while(await r.ReadAsync()) items.Add((r.GetInt32(0),r.GetInt32(1))); }
        foreach (var item in items)
        {
            await using var q = Cmd(c,"SELECT quantity FROM products WHERE id=@id AND shop_id=@shop FOR UPDATE",tx,("@id",item.Id)); var old = Convert.ToInt32(await q.ExecuteScalarAsync());
            if (old + item.Qty > 1000000) throw new ArgumentException("Receiving this order exceeds the stock limit.");
            await using var update = Cmd(c,"UPDATE products SET quantity=quantity+@q WHERE id=@id AND shop_id=@shop",tx,("@q",item.Qty),("@id",item.Id)); await update.ExecuteNonQueryAsync();
            await using var log = Cmd(c,"INSERT INTO stock_movements(shop_id,product_id,old_quantity,new_quantity,reason) VALUES(@shop,@id,@old,@new,@r)",tx,("@id",item.Id),("@old",old),("@new",old+item.Qty),("@r",$"Received order #{id}")); await log.ExecuteNonQueryAsync();
        }
        await using var done = Cmd(c,"UPDATE orders SET status='Received' WHERE id=@id AND shop_id=@shop",tx,("@id",id)); await done.ExecuteNonQueryAsync(); await tx.CommitAsync();
    }
}

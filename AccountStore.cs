using Microsoft.AspNetCore.Identity;
using MySqlConnector;
namespace SpazaSnap;

public sealed class AccountStore(IConfiguration config)
{
    private readonly PasswordHasher<string> hasher = new();
    private async Task<MySqlConnection> OpenAsync()
    { var c = new MySqlConnection(config.GetConnectionString("MySql")); try { await c.OpenAsync(); return c; } catch { await c.DisposeAsync(); throw; } }
    public async Task<Account> RegisterAsync(RegisterInput input)
    {
        var email = AccountRules.Email(input.Email); AccountRules.Password(input.Password);
        var shopName = input.ShopName?.Trim();
        if (string.IsNullOrWhiteSpace(shopName) || shopName.Length > 120) throw new ArgumentException("Shop name is required (up to 120 characters).");
        await using var c = await OpenAsync(); await using var tx = await c.BeginTransactionAsync();
        await using var shop = new MySqlCommand("INSERT INTO shops(name) VALUES(@n)",c,tx); shop.Parameters.AddWithValue("@n",shopName); await shop.ExecuteNonQueryAsync(); var shopId=(int)shop.LastInsertedId;
        await using var user = new MySqlCommand("INSERT INTO users(shop_id,email,password_hash) VALUES(@s,@e,@p)",c,tx);
        user.Parameters.AddWithValue("@s",shopId);user.Parameters.AddWithValue("@e",email);user.Parameters.AddWithValue("@p",hasher.HashPassword(email,input.Password));
        try { await user.ExecuteNonQueryAsync(); } catch(MySqlException e) when(e.Number==1062) { throw new ArgumentException("An account with this email already exists. Sign in instead."); }
        var userId=(int)user.LastInsertedId;
        if (input.IncludeSamples)
        {
            var sql=await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory,"database","seed.sql"));
            await using var seed=new MySqlCommand(sql,c,tx);seed.Parameters.AddWithValue("@shop",shopId);await seed.ExecuteNonQueryAsync();
        }
        await tx.CommitAsync(); return new(userId,shopId,shopName,email,"Free");
    }
    public async Task<Account?> LoginAsync(LoginInput input)
    {
        var email=AccountRules.Email(input.Email);
        if(input.Password is null || input.Password.Length>128)return null;
        await using var c=await OpenAsync();await using var tx=await c.BeginTransactionAsync();
        await using var cmd=new MySqlCommand("SELECT u.id,u.shop_id,s.name,u.password_hash,u.failed_attempts,u.locked_until FROM users u JOIN shops s ON s.id=u.shop_id WHERE u.email=@e FOR UPDATE",c,tx);cmd.Parameters.AddWithValue("@e",email);
        int id,shop,attempts;string name,hash;DateTime? locked;
        await using(var r=await cmd.ExecuteReaderAsync())
        {
            if(!await r.ReadAsync()) { hasher.HashPassword(email,"Timing-work-for-unknown-account-1");return null; }
            id=r.GetInt32(0);shop=r.GetInt32(1);name=r.GetString(2);hash=r.GetString(3);attempts=r.GetInt32(4);locked=r.IsDBNull(5)?null:r.GetDateTime(5);
        }
        if(locked>DateTime.UtcNow)return null;
        var verified=hasher.VerifyHashedPassword(email,hash,input.Password);
        if(verified==PasswordVerificationResult.Failed)
        {
            attempts=locked.HasValue?1:attempts+1;
            await using var fail=new MySqlCommand("UPDATE users SET failed_attempts=@a,locked_until=@l WHERE id=@id",c,tx);
            fail.Parameters.AddWithValue("@a",attempts);fail.Parameters.AddWithValue("@l",attempts>=5?DateTime.UtcNow.AddMinutes(15):DBNull.Value);fail.Parameters.AddWithValue("@id",id);await fail.ExecuteNonQueryAsync();await tx.CommitAsync();return null;
        }
        await using var ok=new MySqlCommand("UPDATE users SET failed_attempts=0,locked_until=NULL,password_hash=@p WHERE id=@id",c,tx);
        ok.Parameters.AddWithValue("@p",verified==PasswordVerificationResult.SuccessRehashNeeded?hasher.HashPassword(email,input.Password):hash);ok.Parameters.AddWithValue("@id",id);await ok.ExecuteNonQueryAsync();await tx.CommitAsync();return new(id,shop,name,email,"Free");
    }
    public async Task<Account?> FindAsync(int userId)
    {
        await using var c=await OpenAsync();await using var cmd=new MySqlCommand("SELECT u.shop_id,s.name,u.email FROM users u JOIN shops s ON s.id=u.shop_id WHERE u.id=@id",c);cmd.Parameters.AddWithValue("@id",userId);
        await using var r=await cmd.ExecuteReaderAsync();return await r.ReadAsync()?new(userId,r.GetInt32(0),r.GetString(1),r.GetString(2),"Free"):null;
    }
}

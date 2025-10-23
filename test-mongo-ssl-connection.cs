using MongoDB.Driver;
using System;
using System.Threading.Tasks;

class Program
{
    static async Task Main(string[] args)
    {
        var connectionString = "mongodb+srv://codesquareltd_db_user:Codesquare%402025@cnet-database.vh5p3m4.mongodb.net/Care-pro_db?retryWrites=true&w=majority&appName=cnet-database&tls=true&tlsInsecure=false";
        
        try
        {
            Console.WriteLine("Testing MongoDB connection...");
            var client = new MongoClient(connectionString);
            var database = client.GetDatabase("Care-pro_db");
            
            // Test the connection
            await database.RunCommandAsync<object>("{ ping: 1 }");
            Console.WriteLine("✅ Connection successful!");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Connection failed: {ex.Message}");
            Console.WriteLine($"Inner exception: {ex.InnerException?.Message}");
        }
    }
}
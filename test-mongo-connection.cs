using MongoDB.Driver;
using System;

class Program
{
    static async Task Main(string[] args)
    {
        // Replace with your actual connection string
        string connectionString = "mongodb+srv://codesquareltd_db_user:YOUR_PASSWORD_HERE@carepro-prod-cluster.d179ao.mongodb.net/?retryWrites=true&w=majority&appName=carepro-prod-cluster";
        
        try
        {
            var client = new MongoClient(connectionString);
            var database = client.GetDatabase("test");
            
            // Test the connection by listing collections
            var collections = await database.ListCollectionNamesAsync();
            var collectionList = await collections.ToListAsync();
            
            Console.WriteLine("✅ MongoDB connection successful!");
            Console.WriteLine($"Found {collectionList.Count} collections");
            
            // Test ping
            await database.RunCommandAsync<MongoDB.Bson.BsonDocument>(new MongoDB.Bson.BsonDocument("ping", 1));
            Console.WriteLine("✅ Ping successful!");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ MongoDB connection failed: {ex.Message}");
            if (ex.InnerException != null)
            {
                Console.WriteLine($"Inner exception: {ex.InnerException.Message}");
            }
        }
    }
}
# Care Pro SignalR Chat Implementation Guide

## Overview

This document provides a comprehensive guide for implementing the SignalR chat backend required by the Care Pro frontend messaging system. It includes all necessary code, database models, and configuration to ensure proper communication with the JavaScript SignalR client.

## Table of Contents

1. [Requirements](#requirements)
2. [SignalR Hub Implementation](#signalr-hub-implementation)
3. [Database Models](#database-models)
4. [Connection Management](#connection-management)
5. [Message Repository](#message-repository)
6. [User Status Management](#user-status-management)
7. [Authentication & Authorization](#authentication--authorization)
8. [Configuration](#configuration)
9. [Integration Steps](#integration-steps)
10. [Testing](#testing)

## Requirements

The frontend expects a SignalR Hub available at the endpoint `/chathub` with the following functionality:

- Real-time messaging between users
- User online status tracking
- Message history storage and retrieval
- Message read/received status updates
- Automatic reconnection support
- JWT authentication

## SignalR Hub Implementation

### ChatHub Class

```csharp
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.Authorization;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace CarePro.Api.Hubs
{
    [Authorize]
    public class ChatHub : Hub
    {
        private readonly IConnectionManager _connectionManager;
        private readonly IMessageRepository _messageRepository;
        private readonly ILogger<ChatHub> _logger;

        public ChatHub(
            IConnectionManager connectionManager,
            IMessageRepository messageRepository,
            ILogger<ChatHub> logger)
        {
            _connectionManager = connectionManager;
            _messageRepository = messageRepository;
            _logger = logger;
        }

        /// <summary>
        /// Register a user's connection when they connect
        /// </summary>
        public async Task RegisterConnection(string userId)
        {
            // Validate the user
            if (string.IsNullOrEmpty(userId))
            {
                throw new HubException("UserId cannot be null or empty");
            }

            // Get the connection ID
            string connectionId = Context.ConnectionId;

            // Associate this connection with the user
            _connectionManager.AddConnection(userId, connectionId);
            _logger.LogInformation($"User {userId} registered connection {connectionId}");
            
            // Notify all clients that this user is now online
            await Clients.All.SendAsync("UserStatusChanged", userId, "Online");
        }

        /// <summary>
        /// Send a message from one user to another
        /// </summary>
        public async Task<string> SendMessage(string senderId, string receiverId, string message)
        {
            // Validate input
            if (string.IsNullOrEmpty(senderId) || string.IsNullOrEmpty(receiverId) || string.IsNullOrEmpty(message))
            {
                throw new HubException("SenderId, ReceiverId, and Message are required");
            }

            try
            {
                // Store the message in the database
                var messageId = await _messageRepository.SaveMessageAsync(senderId, receiverId, message);
                
                // If receiver is online, send them the message directly
                if (_connectionManager.IsUserOnline(receiverId))
                {
                    var receiverConnections = _connectionManager.GetConnections(receiverId);
                    foreach (var connection in receiverConnections)
                    {
                        await Clients.Client(connection).SendAsync("ReceiveMessage", 
                            senderId, message, messageId, "sent");
                    }
                }

                _logger.LogInformation($"Message sent from {senderId} to {receiverId}: {messageId}");
                return messageId;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error sending message from {senderId} to {receiverId}");
                throw new HubException("Failed to send message: " + ex.Message);
            }
        }

        /// <summary>
        /// Get message history between two users
        /// </summary>
        public async Task<List<MessageDTO>> GetMessageHistory(string user1Id, string user2Id, int skip = 0, int take = 50)
        {
            try
            {
                // Retrieve messages from the database
                var messages = await _messageRepository.GetMessageHistoryAsync(user1Id, user2Id, skip, take);
                
                // Convert to DTOs
                var messageDTOs = messages.Select(m => new MessageDTO
                {
                    Id = m.Id,
                    SenderId = m.SenderId,
                    ReceiverId = m.ReceiverId,
                    Content = m.Content,
                    Timestamp = m.Timestamp,
                    Status = m.Status.ToString()
                }).ToList();

                return messageDTOs;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error retrieving message history between {user1Id} and {user2Id}");
                throw new HubException("Failed to retrieve message history: " + ex.Message);
            }
        }

        /// <summary>
        /// Mark a message as received
        /// </summary>
        public async Task MessageReceived(string messageId)
        {
            try
            {
                await _messageRepository.UpdateMessageStatusAsync(messageId, MessageStatus.Delivered);
                _logger.LogInformation($"Message {messageId} marked as received");
                
                // Get the message to notify the sender
                var message = await _messageRepository.GetMessageByIdAsync(messageId);
                if (message != null && _connectionManager.IsUserOnline(message.SenderId))
                {
                    var senderConnections = _connectionManager.GetConnections(message.SenderId);
                    foreach (var connection in senderConnections)
                    {
                        await Clients.Client(connection).SendAsync("MessageStatusUpdated", 
                            messageId, "delivered");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error marking message {messageId} as received");
            }
        }

        /// <summary>
        /// Mark a message as read
        /// </summary>
        public async Task MessageRead(string messageId)
        {
            try
            {
                await _messageRepository.UpdateMessageStatusAsync(messageId, MessageStatus.Read);
                _logger.LogInformation($"Message {messageId} marked as read");
                
                // Get the message to notify the sender
                var message = await _messageRepository.GetMessageByIdAsync(messageId);
                if (message != null && _connectionManager.IsUserOnline(message.SenderId))
                {
                    var senderConnections = _connectionManager.GetConnections(message.SenderId);
                    foreach (var connection in senderConnections)
                    {
                        await Clients.Client(connection).SendAsync("MessageStatusUpdated", 
                            messageId, "read");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error marking message {messageId} as read");
            }
        }

        /// <summary>
        /// Get a list of online users
        /// </summary>
        public Task<List<string>> GetOnlineUsers()
        {
            try
            {
                var onlineUsers = _connectionManager.GetOnlineUsers().ToList();
                return Task.FromResult(onlineUsers);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting online users");
                throw new HubException("Failed to retrieve online users: " + ex.Message);
            }
        }

        /// <summary>
        /// Check if a specific user is online
        /// </summary>
        public Task<bool> GetOnlineStatus(string userId)
        {
            try
            {
                bool isOnline = _connectionManager.IsUserOnline(userId);
                return Task.FromResult(isOnline);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error checking online status for user {userId}");
                throw new HubException("Failed to check online status: " + ex.Message);
            }
        }

        /// <summary>
        /// Handle client disconnection
        /// </summary>
        public override async Task OnDisconnectedAsync(Exception exception)
        {
            var connectionId = Context.ConnectionId;
            var userId = _connectionManager.GetUserIdByConnectionId(connectionId);
            
            if (!string.IsNullOrEmpty(userId))
            {
                // Remove the connection
                _connectionManager.RemoveConnection(userId, connectionId);
                
                // If the user has no more connections, notify others they're offline
                if (!_connectionManager.IsUserOnline(userId))
                {
                    await Clients.All.SendAsync("UserStatusChanged", userId, "Offline");
                }
                
                _logger.LogInformation($"User {userId} disconnected");
            }
            
            await base.OnDisconnectedAsync(exception);
        }
    }
}
```

## Database Models

### Message Model

```csharp
using System;

namespace CarePro.Domain.Entities
{
    public class Message
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string SenderId { get; set; }
        public string ReceiverId { get; set; }
        public string Content { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public MessageStatus Status { get; set; } = MessageStatus.Sent;
        
        // Navigation properties if using Entity Framework
        public virtual AppUser Sender { get; set; }
        public virtual AppUser Receiver { get; set; }
    }
    
    public enum MessageStatus
    {
        Sent,
        Delivered,
        Read
    }
}
```

### Message DTO

```csharp
using System;

namespace CarePro.Application.DTOs
{
    public class MessageDTO
    {
        public string Id { get; set; }
        public string SenderId { get; set; }
        public string ReceiverId { get; set; }
        public string Content { get; set; }
        public DateTime Timestamp { get; set; }
        public string Status { get; set; }
    }
}
```

### DbContext Configuration

```csharp
using Microsoft.EntityFrameworkCore;
using CarePro.Domain.Entities;

namespace CarePro.Infrastructure.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
        {
        }
        
        public DbSet<Message> Messages { get; set; }
        
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            
            // Configure Message entity
            modelBuilder.Entity<Message>()
                .HasKey(m => m.Id);
                
            modelBuilder.Entity<Message>()
                .HasOne(m => m.Sender)
                .WithMany()
                .HasForeignKey(m => m.SenderId)
                .OnDelete(DeleteBehavior.Restrict);
                
            modelBuilder.Entity<Message>()
                .HasOne(m => m.Receiver)
                .WithMany()
                .HasForeignKey(m => m.ReceiverId)
                .OnDelete(DeleteBehavior.Restrict);
                
            modelBuilder.Entity<Message>()
                .Property(m => m.Timestamp)
                .HasDefaultValueSql("GETUTCDATE()");
                
            modelBuilder.Entity<Message>()
                .Property(m => m.Status)
                .HasConversion<string>();
        }
    }
}
```

## Connection Management

### IConnectionManager Interface

```csharp
using System.Collections.Generic;

namespace CarePro.Application.Interfaces
{
    public interface IConnectionManager
    {
        void AddConnection(string userId, string connectionId);
        void RemoveConnection(string userId, string connectionId);
        IEnumerable<string> GetConnections(string userId);
        bool IsUserOnline(string userId);
        IEnumerable<string> GetOnlineUsers();
        string GetUserIdByConnectionId(string connectionId);
    }
}
```

### ConnectionManager Implementation

```csharp
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using CarePro.Application.Interfaces;

namespace CarePro.Infrastructure.Services
{
    public class ConnectionManager : IConnectionManager
    {
        // Maps user IDs to their connection IDs (one user can have multiple connections)
        private readonly ConcurrentDictionary<string, HashSet<string>> _userConnections = new();
        
        // Maps connection IDs back to user IDs for quick lookup
        private readonly ConcurrentDictionary<string, string> _connectionToUser = new();
        
        public void AddConnection(string userId, string connectionId)
        {
            // Add to user->connections mapping
            _userConnections.AddOrUpdate(
                userId,
                _ => new HashSet<string> { connectionId },
                (_, connections) => 
                {
                    connections.Add(connectionId);
                    return connections;
                });
                
            // Add to connection->user mapping
            _connectionToUser[connectionId] = userId;
        }
        
        public void RemoveConnection(string userId, string connectionId)
        {
            // Remove from user->connections mapping
            if (_userConnections.TryGetValue(userId, out var connections))
            {
                connections.Remove(connectionId);
                
                // If no more connections, remove the user entry
                if (connections.Count == 0)
                {
                    _userConnections.TryRemove(userId, out _);
                }
            }
            
            // Remove from connection->user mapping
            _connectionToUser.TryRemove(connectionId, out _);
        }
        
        public IEnumerable<string> GetConnections(string userId)
        {
            return _userConnections.TryGetValue(userId, out var connections)
                ? connections
                : Enumerable.Empty<string>();
        }
        
        public bool IsUserOnline(string userId)
        {
            return _userConnections.TryGetValue(userId, out var connections) && connections.Count > 0;
        }
        
        public IEnumerable<string> GetOnlineUsers()
        {
            return _userConnections.Keys;
        }
        
        public string GetUserIdByConnectionId(string connectionId)
        {
            return _connectionToUser.TryGetValue(connectionId, out var userId) ? userId : null;
        }
    }
}
```

## Message Repository

### IMessageRepository Interface

```csharp
using System.Collections.Generic;
using System.Threading.Tasks;
using CarePro.Domain.Entities;

namespace CarePro.Application.Interfaces
{
    public interface IMessageRepository
    {
        Task<string> SaveMessageAsync(string senderId, string receiverId, string content);
        Task<List<Message>> GetMessageHistoryAsync(string user1Id, string user2Id, int skip = 0, int take = 50);
        Task UpdateMessageStatusAsync(string messageId, MessageStatus status);
        Task<Message> GetMessageByIdAsync(string messageId);
        Task<List<Message>> GetUnreadMessagesAsync(string userId);
        Task<int> GetUnreadMessageCountAsync(string userId);
        Task<List<MessageConversation>> GetConversationsAsync(string userId);
    }
    
    public class MessageConversation
    {
        public string Id { get; set; } // Other user's ID
        public string Name { get; set; }
        public string Avatar { get; set; }
        public string LastMessage { get; set; }
        public DateTime LastMessageTime { get; set; }
    }
}
```

### MessageRepository Implementation

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using CarePro.Application.Interfaces;
using CarePro.Domain.Entities;
using CarePro.Infrastructure.Data;

namespace CarePro.Infrastructure.Services
{
    public class MessageRepository : IMessageRepository
    {
        private readonly AppDbContext _context;
        
        public MessageRepository(AppDbContext context)
        {
            _context = context;
        }
        
        public async Task<string> SaveMessageAsync(string senderId, string receiverId, string content)
        {
            var message = new Message
            {
                SenderId = senderId,
                ReceiverId = receiverId,
                Content = content,
                Timestamp = DateTime.UtcNow,
                Status = MessageStatus.Sent
            };
            
            _context.Messages.Add(message);
            await _context.SaveChangesAsync();
            
            return message.Id;
        }
        
        public async Task<List<Message>> GetMessageHistoryAsync(string user1Id, string user2Id, int skip = 0, int take = 50)
        {
            return await _context.Messages
                .Where(m => 
                    (m.SenderId == user1Id && m.ReceiverId == user2Id) || 
                    (m.SenderId == user2Id && m.ReceiverId == user1Id))
                .OrderBy(m => m.Timestamp)
                .Skip(skip)
                .Take(take)
                .ToListAsync();
        }
        
        public async Task UpdateMessageStatusAsync(string messageId, MessageStatus status)
        {
            var message = await _context.Messages.FindAsync(messageId);
            
            if (message != null)
            {
                message.Status = status;
                await _context.SaveChangesAsync();
            }
        }
        
        public async Task<Message> GetMessageByIdAsync(string messageId)
        {
            return await _context.Messages.FindAsync(messageId);
        }
        
        public async Task<List<Message>> GetUnreadMessagesAsync(string userId)
        {
            return await _context.Messages
                .Where(m => m.ReceiverId == userId && m.Status != MessageStatus.Read)
                .OrderBy(m => m.Timestamp)
                .ToListAsync();
        }
        
        public async Task<int> GetUnreadMessageCountAsync(string userId)
        {
            return await _context.Messages
                .CountAsync(m => m.ReceiverId == userId && m.Status != MessageStatus.Read);
        }
        
        public async Task<List<MessageConversation>> GetConversationsAsync(string userId)
        {
            // This is a more complex query that gets all users that the current user
            // has exchanged messages with, along with the most recent message
            
            // Get all users this user has sent messages to
            var sentToUsers = await _context.Messages
                .Where(m => m.SenderId == userId)
                .Select(m => m.ReceiverId)
                .Distinct()
                .ToListAsync();
                
            // Get all users that have sent messages to this user
            var receivedFromUsers = await _context.Messages
                .Where(m => m.ReceiverId == userId)
                .Select(m => m.SenderId)
                .Distinct()
                .ToListAsync();
                
            // Combine the lists (distinct)
            var allUsers = sentToUsers.Union(receivedFromUsers).Distinct();
            
            var conversations = new List<MessageConversation>();
            
            foreach (var otherUserId in allUsers)
            {
                // Get the most recent message between these users
                var lastMessage = await _context.Messages
                    .Where(m => 
                        (m.SenderId == userId && m.ReceiverId == otherUserId) ||
                        (m.SenderId == otherUserId && m.ReceiverId == userId))
                    .OrderByDescending(m => m.Timestamp)
                    .FirstOrDefaultAsync();
                    
                // Get user info
                var otherUser = await _context.Users.FindAsync(otherUserId);
                
                if (lastMessage != null && otherUser != null)
                {
                    conversations.Add(new MessageConversation
                    {
                        Id = otherUserId,
                        Name = otherUser.FullName ?? otherUser.UserName ?? "Unknown User",
                        Avatar = otherUser.ProfileImage ?? "/default-avatar.png",
                        LastMessage = lastMessage.Content,
                        LastMessageTime = lastMessage.Timestamp
                    });
                }
            }
            
            // Sort by the most recent message
            return conversations.OrderByDescending(c => c.LastMessageTime).ToList();
        }
    }
}
```

## Authentication & Authorization

### Configure JWT Authentication for SignalR

```csharp
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using System.Threading.Tasks;
using System.Text;

namespace CarePro.Api.Extensions
{
    public static class SignalRAuthenticationExtensions
    {
        public static void AddSignalRWithJwtAuth(this IServiceCollection services, string key)
        {
            var keyBytes = Encoding.ASCII.GetBytes(key);
            
            // Add SignalR
            services.AddSignalR(options =>
            {
                options.EnableDetailedErrors = true;
                options.MaximumReceiveMessageSize = 102400; // 100 KB
            });
            
            // Configure authentication
            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            })
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(keyBytes),
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidIssuer = "carepro-api",
                    ValidAudience = "carepro-client"
                };
                
                // Allow JWT tokens in SignalR WebSocket requests
                options.Events = new JwtBearerEvents
                {
                    OnMessageReceived = context =>
                    {
                        var accessToken = context.Request.Query["access_token"];
                        
                        // If the request is for our hub...
                        var path = context.HttpContext.Request.Path;
                        if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/chathub"))
                        {
                            // Read the token out of the query string
                            context.Token = accessToken;
                        }
                        return Task.CompletedTask;
                    }
                };
            });
        }
    }
}
```

## Configuration

### Program.cs Configuration

```csharp
using CarePro.Api.Extensions;
using CarePro.Api.Hubs;
using CarePro.Application.Interfaces;
using CarePro.Infrastructure.Data;
using CarePro.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Add database
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// Add SignalR with JWT authentication
builder.Services.AddSignalRWithJwtAuth(builder.Configuration["JwtSettings:Key"]);

// Add services
builder.Services.AddSingleton<IConnectionManager, ConnectionManager>();
builder.Services.AddScoped<IMessageRepository, MessageRepository>();

// Configure CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("CorsPolicy", builder => builder
        .WithOrigins("https://carepro-client.com") // Replace with your actual client URL
        .AllowAnyMethod()
        .AllowAnyHeader()
        .AllowCredentials());
});

var app = builder.Build();

// Configure middleware
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseCors("CorsPolicy");
app.UseAuthentication();
app.UseAuthorization();

// Map hubs
app.MapHub<ChatHub>("/chathub");

app.MapControllers();

app.Run();
```

### appsettings.json Configuration

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=YOUR_SERVER;Database=CarePro;User Id=YOUR_USER;Password=YOUR_PASSWORD;TrustServerCertificate=True;"
  },
  "JwtSettings": {
    "Key": "your-very-secure-secret-key-that-is-at-least-32-characters-long",
    "Issuer": "carepro-api",
    "Audience": "carepro-client",
    "DurationInMinutes": 60
  },
  "SignalR": {
    "HubUrl": "/chathub",
    "EnableDetailedErrors": true
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning",
      "Microsoft.AspNetCore.SignalR": "Debug"
    }
  },
  "AllowedHosts": "*"
}
```

## Integration Steps

1. **Create Database Models**
   - Add the Message model to your Domain project
   - Add the MessageDTO to your Application project
   - Update your DbContext to include the Messages DbSet

2. **Add Message Repository**
   - Add the IMessageRepository interface to your Application project
   - Implement MessageRepository in your Infrastructure project
   - Register the repository in your DI container

3. **Create Connection Manager**
   - Add the IConnectionManager interface to your Application project
   - Implement ConnectionManager in your Infrastructure project
   - Register it as a singleton in your DI container

4. **Configure Authentication**
   - Ensure JWT authentication is set up correctly
   - Add the SignalR JWT authentication extension

5. **Add SignalR Hub**
   - Create the ChatHub class in your API project
   - Map the hub in Program.cs
   - Configure CORS to allow SignalR connections

6. **Configure Services**
   - Update appsettings.json with required configuration
   - Register all services in the DI container

7. **Create Database Migration**
   - Run the following commands:

   ```bash
   dotnet ef migrations add AddMessageEntity -p Infrastructure -s CarePro-Api
   dotnet ef database update -p Infrastructure -s CarePro-Api
   ```

## Testing

Test your implementation thoroughly to ensure it meets the frontend requirements:

1. **Connection Tests**
   - Verify users can connect with valid JWT tokens
   - Verify connection rejection with invalid tokens
   - Test reconnection behavior after network disruption

2. **Message Tests**
   - Send messages between online users
   - Verify message persistence when receiver is offline
   - Check message history retrieval with pagination

3. **Status Tests**
   - Verify read/received status updates
   - Test online/offline status updates
   - Validate unread message counts

4. **Performance Tests**
   - Test with multiple simultaneous connections
   - Verify message delivery under load
   - Check database query performance

5. **Error Handling**
   - Test with invalid inputs
   - Verify proper error responses
   - Check logging of important events

## Conclusion

This implementation provides a complete SignalR Chat backend that works with the Care Pro frontend messaging system. The connection management and message repository handle all the required functionality, and the JWT authentication integration ensures secure communication.

For any questions or issues, please contact the development team.

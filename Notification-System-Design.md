# Care Pro Notification System Design

## Overview
This document outlines the design and implementation steps for creating a comprehensive notification system in the Care Pro application. The notification system will support:

1. Message notifications between clients and caregivers ✅
2. Payment notifications for gig orders ✅
3. Real-time notification delivery ✅
4. Persistent storage of notifications ✅
5. UI components for notification display ✅

## 1. Database Design

### Notification Entity
```csharp
public class Notification
{
    public Guid Id { get; set; }
    public string RecipientId { get; set; } // User receiving the notification
    public string SenderId { get; set; } // User who triggered the notification (optional)
    public NotificationType Type { get; set; } // Message, Payment, etc.
    public string Content { get; set; } // Notification text
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool IsRead { get; set; } = false;
    public string RelatedEntityId { get; set; } // ID of message/payment/gig
    
    // Navigation properties
    public AppUser Recipient { get; set; }
    public AppUser Sender { get; set; }
}

public enum NotificationType
{
    Message,
    Payment,
    SystemNotice,
    NewGig
    // Add more types as needed
}
```

### Database Context Updates
Add Notifications DbSet to CareProDbContext:

```csharp
public DbSet<Notification> Notifications { get; set; }
```

## 2. Backend Implementation

### A. Notification Service Interface
```csharp
public interface INotificationService
{
    Task<Notification> CreateNotificationAsync(string recipientId, string senderId, NotificationType type, string content, string relatedEntityId);
    Task<List<Notification>> GetUserNotificationsAsync(string userId, int page = 1, int pageSize = 10);
    Task<int> GetUnreadNotificationCountAsync(string userId);
    Task MarkAsReadAsync(Guid notificationId);
    Task MarkAllAsReadAsync(string userId);
    Task DeleteNotificationAsync(Guid notificationId);
    Task<bool> SendRealTimeNotificationAsync(string userId, Notification notification);
}
```

### B. Notification Service Implementation
Implement the service with:
- Database operations for CRUD
- SignalR integration for real-time delivery
- Message formatting

### C. Notification Controller
Endpoints:
- GET /api/notifications - Get user's notifications
- GET /api/notifications/unread/count - Get unread count
- PUT /api/notifications/{id}/read - Mark as read
- PUT /api/notifications/read-all - Mark all as read
- DELETE /api/notifications/{id} - Delete notification

### D. Integration Points
- Message Service: Create notification when messages are sent
- Payment Service: Create notification when payments are processed
- Gig Service: Create notification when gigs are ordered

## 3. Real-Time Notification Delivery with SignalR

### A. NotificationHub
```csharp
public class NotificationHub : Hub
{
    public async Task SendNotification(string userId, string message)
    {
        await Clients.User(userId).SendAsync("ReceiveNotification", message);
    }
}
```

### B. Connection Management
- Track user connections in memory or distributed cache
- Map user IDs to SignalR connection IDs
- Handle connection and disconnection events

### C. Program.cs Configuration
```csharp
// Add SignalR
builder.Services.AddSignalR();

// Map hub
app.MapHub<NotificationHub>("/notificationHub");
```

## 4. Frontend Implementation

### A. Notification Component
- Create notification icon with counter in navbar
- Implement dropdown for recent notifications
- Style read vs unread notifications
- Add "Mark as read" functionality

### B. SignalR Client
- Connect to NotificationHub on login
- Listen for notification events
- Update UI on notification receipt
- Handle connection management

## 5. Implementation Steps

### Phase 1: Database & Service Layer ✅
1. Create Notification entity ✅
2. Add migrations and update database ✅
3. Implement INotificationService ✅
4. Implement NotificationsController ✅
5. Add dependency injection in Program.cs ✅

### Phase 2: SignalR Integration ✅
1. Create NotificationHub ✅
2. Configure SignalR in Program.cs ✅
3. Implement connection management ✅
4. Create client-side SignalR connection ✅

### Phase 3: Message Notification Integration ✅
1. Update message service to create notifications ✅
2. Send real-time notifications via SignalR on message send ✅
3. Test message notifications end-to-end ✅

### Phase 4: Payment Notification Integration ✅
1. Update payment/order service to create notifications ✅
2. Send real-time notifications on gig purchase ✅
3. Test payment notifications end-to-end ✅

### Phase 5: UI Implementation ✅
1. Create notification component in navbar ✅
2. Implement notification list view ✅
3. Add read/unread styling and functionality ✅
4. Test UI functionality ✅

## 6. Testing Strategy
1. Unit test notification service methods
2. Integration test notification controller endpoints
3. Test real-time notification delivery
4. Test message notification integration
5. Test payment notification integration
6. End-to-end UI testing

## 7. Security Considerations
1. Ensure users can only access their own notifications
2. Validate inputs in controller methods
3. Implement authorization on NotificationHub methods
4. Protect against notification flooding

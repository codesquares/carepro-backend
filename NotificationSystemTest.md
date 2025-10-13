# Notification System Testing Guide

## Prerequisites
1. Ensure the backend API is running
2. Ensure the frontend application is running
3. Have at least two user accounts: one caregiver and one client

## Test Plan

### 1. Message Notifications
1. Login as a client user
2. Send a message to a caregiver
3. Verify that the caregiver receives a notification when they log in

### 2. Payment Notifications
1. Login as a client user
2. Place an order for a caregiver's service 
3. Complete payment
4. Verify that the caregiver receives a notification about the new order

### 3. Real-Time Notifications
1. Open two browser windows/tabs
2. Login as a client in one window
3. Login as a caregiver in the other window
4. Send a message from the client to the caregiver
5. Verify that the caregiver receives the notification in real-time without refreshing

### 4. Notification Management
1. Login as a user with notifications
2. Click on a notification to mark it as read
3. Verify that the unread count decreases
4. Click "Mark all as read"
5. Verify that all notifications are marked as read

### 5. Notification Persistence
1. Login as a user with notifications
2. Close browser and re-login
3. Verify that notifications are still available

## Expected Results
- The notification bell should display the correct unread count
- Notifications should appear in the dropdown when clicking the bell icon
- Notifications should be formatted with appropriate icons based on type
- Clicking on a notification should mark it as read
- The "Mark all as read" button should clear all unread notifications
- The "View all" button should navigate to the notifications page
- The notifications page should show all notifications in chronological order

## Troubleshooting
If notifications are not working as expected:
1. Check browser console for errors
2. Verify SignalR connection status in the browser console
3. Check backend logs for any errors in the NotificationService
4. Ensure that the notification hub is correctly mapped in Program.cs
5. Verify that the user's ConnectionId is correctly saved in the database

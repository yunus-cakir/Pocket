using System;

namespace Pocket.Client.Services
{
    public partial class NotificationService : INotificationService
    {
        public void ShowMessageNotification(
            string senderId,
            string senderDisplayName,
            string messageText,
            string? senderAvatarPath = null)
        {
            if (string.IsNullOrWhiteSpace(messageText))
                return;

            string header = string.IsNullOrWhiteSpace(senderDisplayName) ? "Pocket" : senderDisplayName.Trim();
            ShowPlatformNotification(
                senderId: senderId,
                header: header,
                body: messageText,
                senderAvatarPath: senderAvatarPath,
                isReaction: false,
                mediaPostId: null);
        }

        public void ShowReactionNotification(
            string senderId,
            string senderDisplayName,
            string? senderAvatarPath = null,
            Guid? mediaPostId = null)
        {
            string header = string.IsNullOrWhiteSpace(senderDisplayName) ? "Pocket" : senderDisplayName.Trim();
            ShowPlatformNotification(
                senderId: senderId,
                header: header,
                body: "Sent a reaction!",
                senderAvatarPath: senderAvatarPath,
                isReaction: true,
                mediaPostId: mediaPostId);
        }

        partial void ShowPlatformNotification(
            string senderId,
            string header,
            string body,
            string? senderAvatarPath,
            bool isReaction,
            Guid? mediaPostId);
    }
}


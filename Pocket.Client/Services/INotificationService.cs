using System;

namespace Pocket.Client.Services
{
    public interface INotificationService
    {
        void ShowMessageNotification(
            string senderId,
            string senderDisplayName,
            string messageText,
            string? senderAvatarPath = null);

        void ShowReactionNotification(
            string senderId,
            string senderDisplayName,
            string? senderAvatarPath = null,
            Guid? mediaPostId = null);
    }
}


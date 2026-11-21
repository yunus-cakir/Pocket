using SQLite;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Pocket.Client.Models;

namespace Pocket.Client.Data
{
    public class LocalDatabase
    {
        private SQLiteAsyncConnection? _connection;
        private readonly System.Threading.SemaphoreSlim _initLock = new(1, 1);
        private bool _isInitialized;

        public LocalDatabase()
        {
        }

        private async Task InitAsync()
        {
            if (_isInitialized)
                return;

            await _initLock.WaitAsync();
            try
            {
                if (_isInitialized)
                    return;

#if DEBUG && ANDROID
                await DebugSeedData.SeedIfMissingAsync();
#endif
                _connection = new SQLiteAsyncConnection(DatabaseConstants.DatabasePath, DatabaseConstants.Flags);

                await _connection.CreateTableAsync<User>();
                await _connection.CreateTableAsync<Friend>();
                await _connection.CreateTableAsync<MediaPost>();
                await _connection.CreateTableAsync<DirectMessage>();
                await _connection.CreateTableAsync<ConnectionEvent>();
                await _connection.CreateTableAsync<PostDelivery>();

                _isInitialized = true;
            }
            finally
            {
                _initLock.Release();
            }
        }

        public async Task<SQLiteAsyncConnection> GetConnectionAsync()
        {
            await InitAsync();
            return _connection!;
        }

        public async Task CloseConnectionAsync()
        {
            await _initLock.WaitAsync();
            try
            {
                if (_connection != null)
                {
                    try
                    {
                        await _connection.ExecuteAsync("PRAGMA wal_checkpoint(TRUNCATE);");
                    }
                    catch
                    {
                        // Ignore if not in WAL mode
                    }

                    await _connection.CloseAsync();
                    _connection = null;
                }
                _isInitialized = false;
            }
            finally
            {
                _initLock.Release();
            }
        }

        public async Task ReinitializeAsync()
        {
            await CloseConnectionAsync();
            await InitAsync();
        }

        #region User Operations
        public async Task<User?> GetUserAsync()
        {
            await InitAsync();
            return await _connection!.Table<User>().FirstOrDefaultAsync();
        }

        public async Task<int> SaveUserAsync(User user)
        {
            await InitAsync();
            var existingUser = await GetUserAsync();
            if (existingUser != null)
            {
                user.Id = existingUser.Id;
                return await _connection!.UpdateAsync(user);
            }
            return await _connection!.InsertAsync(user);
        }
        #endregion

        #region Friend Operations
        public async Task<List<Friend>> GetFriendsAsync()
        {
            await InitAsync();
            return await _connection!.Table<Friend>().ToListAsync();
        }

        public async Task<Friend?> GetFriendAsync(string id)
        {
            await InitAsync();
            return await _connection!.Table<Friend>().Where(f => f.Id == id).FirstOrDefaultAsync();
        }

        public async Task<int> SaveFriendAsync(Friend friend)
        {
            await InitAsync();
            var existing = await GetFriendAsync(friend.Id);
            if (existing != null)
            {
                return await _connection!.UpdateAsync(friend);
            }
            return await _connection!.InsertAsync(friend);
        }
        #endregion

        #region MediaPost Operations
        public async Task<List<MediaPost>> GetMediaPostsForFriendAsync(string friendId)
        {
            await InitAsync();
            return await _connection!.Table<MediaPost>()
                .Where(m => m.FriendId == friendId)
                .OrderBy(m => m.SentAt)
                .ToListAsync();
        }

        public async Task<MediaPost?> GetMediaPostAsync(Guid id)
        {
            await InitAsync();
            return await _connection!.Table<MediaPost>()
                .Where(m => m.Id == id)
                .FirstOrDefaultAsync();
        }

        public async Task<List<MediaPost>> GetRollcallMediaPostsAsync()
        {
            await InitAsync();
            var cutoff = DateTime.UtcNow.AddDays(-7);
            return await _connection!.Table<MediaPost>()
                .Where(m => m.IsIncludedInRollcall && m.SentAt >= cutoff)
                .OrderByDescending(m => m.SentAt)
                .ToListAsync();
        }

        public async Task<List<MediaPost>> GetAllMediaPostsAsync()
        {
            await InitAsync();
            return await _connection!.Table<MediaPost>()
                .OrderByDescending(m => m.SentAt)
                .ToListAsync();
        }

        public async Task<List<MediaPost>> GetPendingOutgoingMediaPostsAsync()
        {
            await InitAsync();
            return await _connection!.Table<MediaPost>()
                .Where(m => !m.IsIncoming && m.SyncStatus == Pocket.Shared.Enums.SyncStatus.Pending)
                .OrderBy(m => m.SentAt)
                .ToListAsync();
        }

        public async Task<MediaPost?> GetLatestIncomingPhotoPostAsync()
        {
            await InitAsync();
            return await _connection!.Table<MediaPost>()
                .Where(m => m.IsIncoming && m.MediaType == Pocket.Shared.Enums.MediaType.Photo)
                .OrderByDescending(m => m.SentAt)
                .FirstOrDefaultAsync();
        }

        public async Task<int> GetUnseenIncomingPhotoCountAsync()
        {
            await InitAsync();
            return await _connection!.Table<MediaPost>()
                .Where(m => m.IsIncoming && m.MediaType == Pocket.Shared.Enums.MediaType.Photo && m.IsUnseen)
                .CountAsync();
        }

        public async Task MarkAllIncomingPhotosSeenAsync()
        {
            await InitAsync();
            var unseenPosts = await _connection!.Table<MediaPost>()
                .Where(m => m.IsIncoming && m.IsUnseen)
                .ToListAsync();
            foreach (var post in unseenPosts)
            {
                post.IsUnseen = false;
                await _connection.UpdateAsync(post);
            }
        }

        public async Task<int> SaveMediaPostAsync(MediaPost mediaPost)
        {
            await InitAsync();
            var existing = await _connection!.Table<MediaPost>().Where(m => m.Id == mediaPost.Id).FirstOrDefaultAsync();
            if (existing != null)
            {
                return await _connection!.UpdateAsync(mediaPost);
            }
            return await _connection!.InsertAsync(mediaPost);
        }

        public async Task<int> DeleteMediaPostAsync(Guid id)
        {
            await InitAsync();
            // Delete anyreaction-only DMs associated with this post
            var postDms = await GetDirectMessagesForPostAsync(id);
            foreach (var dm in postDms)
            {
                if (dm.IsPostReactionOnly)
                {
                    await _connection!.DeleteAsync<DirectMessage>(dm.Id);
                }
                else
                {
                    dm.MediaPostId = null;
                    await _connection!.UpdateAsync(dm);
                }
            }
            return await _connection!.DeleteAsync<MediaPost>(id);
        }
        #endregion

        #region DirectMessage Operations
        public async Task<DirectMessage?> GetDirectMessageAsync(Guid id)
        {
            await InitAsync();
            return await _connection!.Table<DirectMessage>()
                .Where(dm => dm.Id == id)
                .FirstOrDefaultAsync();
        }

        public async Task<List<DirectMessage>> GetDirectMessagesForFriendAsync(string friendId)
        {
            await InitAsync();
            return await _connection!.Table<DirectMessage>()
                .Where(dm => dm.FriendId == friendId)
                .OrderBy(dm => dm.SentAt)
                .ToListAsync();
        }

        /// <summary>All DMs that reference a media post, in one query (avoids an N+1 lookup per post when loading the feed).</summary>
        public async Task<List<DirectMessage>> GetAllPostLinkedDirectMessagesAsync()
        {
            await InitAsync();
            return await _connection!.Table<DirectMessage>()
                .Where(dm => dm.MediaPostId != null)
                .OrderBy(dm => dm.SentAt)
                .ToListAsync();
        }

        public async Task<List<DirectMessage>> GetDirectMessagesForPostAsync(Guid mediaPostId)
        {
            await InitAsync();
            return await _connection!.Table<DirectMessage>()
                .Where(dm => dm.MediaPostId == mediaPostId)
                .OrderBy(dm => dm.SentAt)
                .ToListAsync();
        }

        public async Task<int> SaveDirectMessageAsync(DirectMessage dm)
        {
            await InitAsync();
            var existing = await _connection!.Table<DirectMessage>().Where(m => m.Id == dm.Id).FirstOrDefaultAsync();
            if (existing != null)
            {
                return await _connection!.UpdateAsync(dm);
            }
            return await _connection!.InsertAsync(dm);
        }

        public async Task<int> DeleteDirectMessageAsync(Guid id)
        {
            await InitAsync();
            return await _connection!.DeleteAsync<DirectMessage>(id);
        }

        public async Task MarkPostReactionsAnimationShownAsync(IEnumerable<Guid> dmIds)
        {
            await InitAsync();
            foreach (var id in dmIds)
            {
                var dm = await _connection!.Table<DirectMessage>().Where(d => d.Id == id).FirstOrDefaultAsync();
                if (dm != null && !dm.IsReactionAnimationShown)
                {
                    dm.IsReactionAnimationShown = true;
                    await _connection.UpdateAsync(dm);
                }
            }
        }
        #endregion

        #region Connection History
        /// <summary>Outgoing direct messages since <paramref name="sinceUtc"/> the recipient has not confirmed (Pending or Sent).</summary>
        public async Task<List<DirectMessage>> GetUndeliveredOutgoingDirectMessagesAsync(DateTime sinceUtc)
        {
            await InitAsync();
            return await _connection!.Table<DirectMessage>()
                .Where(dm => !dm.IsIncoming && dm.SentAt >= sinceUtc &&
                             (dm.SyncStatus == Pocket.Shared.Enums.SyncStatus.Pending ||
                              dm.SyncStatus == Pocket.Shared.Enums.SyncStatus.Sent))
                .OrderBy(dm => dm.SentAt)
                .ToListAsync();
        }

        public async Task<List<PostDelivery>> GetPostDeliveriesAsync(Guid postId)
        {
            await InitAsync();
            return await _connection!.Table<PostDelivery>().Where(d => d.PostId == postId).ToListAsync();
        }

        /// <summary>Deliveries not yet confirmed by the recipient (resend candidates).</summary>
        public async Task<List<PostDelivery>> GetUndeliveredPostDeliveriesAsync()
        {
            await InitAsync();
            return await _connection!.Table<PostDelivery>()
                .Where(d => d.State != DeliveryState.Delivered)
                .ToListAsync();
        }

        public async Task SavePostDeliveryAsync(PostDelivery delivery)
        {
            await InitAsync();
            delivery.UpdatedAtUtc = DateTime.UtcNow;
            await _connection!.InsertOrReplaceAsync(delivery);
        }

        public async Task DeletePostDeliveriesAsync(Guid postId)
        {
            await InitAsync();
            await _connection!.Table<PostDelivery>().DeleteAsync(d => d.PostId == postId);
        }

        public async Task<int> SaveConnectionEventAsync(ConnectionEvent evt)
        {
            await InitAsync();
            return await _connection!.InsertAsync(evt);
        }

        /// <summary>Most recent events first.</summary>
        public async Task<List<ConnectionEvent>> GetRecentConnectionEventsAsync(int limit)
        {
            await InitAsync();
            return await _connection!.Table<ConnectionEvent>()
                .OrderByDescending(e => e.TimestampUtc)
                .Take(limit)
                .ToListAsync();
        }

        /// <summary>Events in [fromUtc, toUtc], oldest first (for outage / data-loss analysis).</summary>
        public async Task<List<ConnectionEvent>> GetConnectionEventsAsync(DateTime fromUtc, DateTime toUtc)
        {
            await InitAsync();
            return await _connection!.Table<ConnectionEvent>()
                .Where(e => e.TimestampUtc >= fromUtc && e.TimestampUtc <= toUtc)
                .OrderBy(e => e.TimestampUtc)
                .ToListAsync();
        }

        public async Task<int> DeleteConnectionEventsBeforeAsync(DateTime cutoffUtc)
        {
            await InitAsync();
            return await _connection!.Table<ConnectionEvent>().DeleteAsync(e => e.TimestampUtc < cutoffUtc);
        }
        #endregion
    }
}

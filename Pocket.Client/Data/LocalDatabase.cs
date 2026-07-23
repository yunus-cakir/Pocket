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

        public LocalDatabase()
        {
        }

        private async Task InitAsync()
        {
            if (_connection is not null)
                return;

            _connection = new SQLiteAsyncConnection(DatabaseConstants.DatabasePath, DatabaseConstants.Flags);

            await _connection.CreateTableAsync<User>();
            await _connection.CreateTableAsync<Friend>();
            await _connection.CreateTableAsync<MediaPost>();
            await _connection.CreateTableAsync<DirectMessage>();
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

        public async Task<List<MediaPost>> GetRollcallMediaPostsAsync()
        {
            await InitAsync();
            return await _connection!.Table<MediaPost>()
                .Where(m => m.IsIncludedInRollcall)
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
        #endregion

        #region DirectMessage Operations
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
        #endregion
    }
}

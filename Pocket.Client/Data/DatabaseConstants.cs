using System.IO;
using Microsoft.Maui.Storage;

namespace Pocket.Client.Data
{
    public static class DatabaseConstants
    {
        public const string DatabaseFilename = "PocketLocal.db3";

        public const SQLite.SQLiteOpenFlags Flags =
            SQLite.SQLiteOpenFlags.ReadWrite |
            SQLite.SQLiteOpenFlags.Create |
            SQLite.SQLiteOpenFlags.SharedCache;

        public static string DatabasePath =>
            Path.Combine(FileSystem.AppDataDirectory, DatabaseFilename);
    }
}

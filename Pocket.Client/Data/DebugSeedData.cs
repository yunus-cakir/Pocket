#if DEBUG && ANDROID
using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Maui.Storage;

namespace Pocket.Client.Data
{
    /// <summary>
    /// Debug Android builds only: on a fresh install (no database yet) the "Mert Kaya" sample data
    /// bundled from SeedData/ is copied in, so the app starts with friends, posts and messages
    /// instead of an empty new identity. Existing data is never touched.
    /// </summary>
    internal static class DebugSeedData
    {
        private const string SeedRoot = "Seed";

        public static async Task SeedIfMissingAsync()
        {
            string dbPath = DatabaseConstants.DatabasePath;
            if (File.Exists(dbPath))
                return;

            try
            {
                string appData = FileSystem.AppDataDirectory;
                await CopyFolderAsync("Media", Path.Combine(appData, "Media"));
                await CopyFolderAsync("Profile", Path.Combine(appData, "Profile"));

                string tempPath = dbPath + ".seed";
                await CopyAssetAsync($"{SeedRoot}/{DatabaseConstants.DatabaseFilename}", tempPath);
                ShiftTimestampsToNow(tempPath);
                File.Move(tempPath, dbPath);

                Debug.WriteLine("[DebugSeedData] Installed the Mert Kaya sample database.");
            }
            catch (Exception ex)
            {
                // Leave no half-seeded database behind: the app then creates a fresh identity.
                Debug.WriteLine($"[DebugSeedData] Seeding failed: {ex.Message}");
                try { File.Delete(dbPath + ".seed"); } catch { }
            }
        }

        private static async Task CopyFolderAsync(string folder, string destDir)
        {
            Directory.CreateDirectory(destDir);
            string[] names = Android.App.Application.Context.Assets?.List($"{SeedRoot}/{folder}") ?? Array.Empty<string>();
            foreach (string name in names)
            {
                string dest = Path.Combine(destDir, name);
                if (!File.Exists(dest))
                    await CopyAssetAsync($"{SeedRoot}/{folder}/{name}", dest);
            }
        }

        private static async Task CopyAssetAsync(string assetPath, string dest)
        {
            await using Stream source = await FileSystem.OpenAppPackageFileAsync(assetPath);
            await using FileStream target = File.Create(dest);
            await source.CopyToAsync(target);
        }

        /// <summary>Moves every stored date forward so the newest message is a few minutes old (posts stay inside the 7-day rollcall).</summary>
        private static void ShiftTimestampsToNow(string path)
        {
            using var db = new SQLite.SQLiteConnection(path);
            long newest = Math.Max(
                db.ExecuteScalar<long>("SELECT IFNULL(MAX(SentAt), 0) FROM MediaPost"),
                db.ExecuteScalar<long>("SELECT IFNULL(MAX(SentAt), 0) FROM DirectMessage"));
            if (newest <= 0)
                return;

            long delta = DateTime.UtcNow.AddMinutes(-2).Ticks - newest;
            if (delta <= 0)
                return;

            db.Execute("UPDATE MediaPost SET SentAt = SentAt + ?", delta);
            db.Execute("UPDATE DirectMessage SET SentAt = SentAt + ?", delta);
            db.Execute("UPDATE Friend SET AddedAt = AddedAt + ?, LastInteractionAt = LastInteractionAt + ?", delta, delta);
            db.Execute("UPDATE User SET CreatedAt = CreatedAt + ?", delta);
        }
    }
}
#endif

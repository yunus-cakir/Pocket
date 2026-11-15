import os
import sqlite3
import uuid
import urllib.request
from datetime import datetime, timedelta, timezone

SEED_DIR = os.path.dirname(os.path.abspath(__file__))
MEDIA_LOCAL_DIR = os.path.join(SEED_DIR, "Media")
PROFILE_LOCAL_DIR = os.path.join(SEED_DIR, "Profile")
os.makedirs(MEDIA_LOCAL_DIR, exist_ok=True)
os.makedirs(PROFILE_LOCAL_DIR, exist_ok=True)

APP_FILES_BASE = "/data/user/0/com.yunuscakir.pocket.client.debug/files"

# Sabit eşleşmiş ECDH P-256 anahtar çiftleri (emulator-5554 <-> emulator-5556 canlı E2EE iletişimi için)
DEFAULT_5554 = {
    "Id": "552f4979-da7d-453e-b0a1-e4ee026decb0",
    "Username": "mertkaya",
    "DisplayName": "Mert Kaya",
    "PublicKey": "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEi/Zwfeo7N3tPViG9WolkiOUze0gYY7FauZI3Rk5o+/LONPRnzrME1rwSOcEpZ2l1CcXvcKWeWWIEB2CwA7NHpQ==",
    "EncryptedPrivateKey": "MHcCAQEEIPVMMcbeCzblXEFG2cZaVN/ws6OLI8dU6HzXqj5ELHqOoAoGCCqGSM49AwEHoUQDQgAEi/Zwfeo7N3tPViG9WolkiOUze0gYY7FauZI3Rk5o+/LONPRnzrME1rwSOcEpZ2l1CcXvcKWeWWIEB2CwA7NHpQ==",
}

DEFAULT_5556 = {
    "Id": "08d4e824-aa93-42c2-818a-783e65faf1d1",
    "Username": "selindemir",
    "DisplayName": "Selin Demir",
    "PublicKey": "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEEgzQAifXeb+XV92etDlC0Cwy3oE9bsWAoNzt/uxer3RCZzHc7y8rFjvp1Re0t6PJlxCnykiJYV0udH8IxLKDWQ==",
    "EncryptedPrivateKey": "MHcCAQEEIH/gQANN+ygRA7bt9/rz7mykCpabl9HI3MyC6YUSPwCPoAoGCCqGSM49AwEHoUQDQgAEEgzQAifXeb+XV92etDlC0Cwy3oE9bsWAoNzt/uxer3RCZzHc7y8rFjvp1Re0t6PJlxCnykiJYV0udH8IxLKDWQ==",
}


def dt_to_ticks(dt: datetime) -> int:
    return 621355968000000000 + int(dt.timestamp() * 10_000_000)


def ensure_image(picsum_id: int, size: int, dest_path: str):
    if os.path.exists(dest_path) and os.path.getsize(dest_path) > 5000:
        return
    urls = [
        f"https://picsum.photos/id/{picsum_id}/{size}/{size}",
        f"https://picsum.photos/seed/pocket_{picsum_id}/{size}/{size}",
    ]
    for url in urls:
        try:
            req = urllib.request.Request(url, headers={"User-Agent": "Mozilla/5.0"})
            with urllib.request.urlopen(req, timeout=10) as resp:
                data = resp.read()
                if len(data) > 5000:
                    with open(dest_path, "wb") as f:
                        f.write(data)
                    print(f"Downloaded {os.path.basename(dest_path)} ({len(data)} bytes)")
                    return
        except Exception as e:
            print(f"Warning downloading {url}: {e}")


def ensure_schema(cur: sqlite3.Cursor):
    cur.execute("""
        CREATE TABLE IF NOT EXISTS "User" (
            "Id" varchar primary key not null,
            "Username" varchar(50),
            "DisplayName" varchar(100),
            "ProfilePictureUrl" varchar,
            "PublicKey" varchar,
            "EncryptedPrivateKey" varchar,
            "CreatedAt" bigint
        )
    """)
    cur.execute("""
        CREATE TABLE IF NOT EXISTS "Friend" (
            "Id" varchar primary key not null,
            "Username" varchar(50),
            "DisplayName" varchar(100),
            "ProfilePictureUrl" varchar,
            "PublicKey" varchar,
            "IsMuted" integer,
            "AddedAt" bigint,
            "LastInteractionAt" bigint
        )
    """)
    cur.execute('CREATE INDEX IF NOT EXISTS "Friend_Username" on "Friend"("Username")')
    cur.execute("""
        CREATE TABLE IF NOT EXISTS "MediaPost" (
            "Id" varchar primary key not null,
            "FriendId" varchar,
            "IsIncoming" integer,
            "MediaLocalPath" varchar,
            "MediaType" integer,
            "Caption" varchar,
            "IsIncludedInRollcall" integer,
            "SentAt" bigint,
            "SyncStatus" integer
        )
    """)
    cur.execute('CREATE INDEX IF NOT EXISTS "MediaPost_FriendId" on "MediaPost"("FriendId")')
    cur.execute('CREATE INDEX IF NOT EXISTS "MediaPost_SentAt" on "MediaPost"("SentAt")')
    cur.execute("""
        CREATE TABLE IF NOT EXISTS "DirectMessage" (
            "Id" varchar primary key not null,
            "MediaPostId" varchar,
            "FriendId" varchar,
            "IsIncoming" integer,
            "TextContent" varchar,
            "ReactionEmoji" varchar,
            "SentAt" bigint,
            "SyncStatus" integer
        )
    """)
    cur.execute('CREATE INDEX IF NOT EXISTS "DirectMessage_MediaPostId" on "DirectMessage"("MediaPostId")')
    cur.execute('CREATE INDEX IF NOT EXISTS "DirectMessage_FriendId" on "DirectMessage"("FriendId")')
    cur.execute('CREATE INDEX IF NOT EXISTS "DirectMessage_SentAt" on "DirectMessage"("SentAt")')


def main():
    avatars = {
        "avatar_mert.jpg": 1005,
        "avatar_selin.jpg": 1027,
        "friend_mert.jpg": 1005,
        "friend_selin.jpg": 1027,
        "friend_zeynep.jpg": 64,
        "friend_can.jpg": 91,
        "friend_elif.jpg": 177,
    }
    for filename, pid in avatars.items():
        ensure_image(pid, 400, os.path.join(PROFILE_LOCAL_DIR, filename))

    sample_photos = [
        ("sample_01_coffee.jpg", 1060, "Sabah kahvesi olmadan güne başlayamayanlar kulübü ☕✨"),
        ("sample_02_mountain.jpg", 1018, "Hafta sonu kaçamağından kalanlar 🏔️🌲"),
        ("sample_03_dog.jpg", 237, "Parkta yeni bir dost edindim 🐶🖤"),
        ("sample_04_sunset.jpg", 1015, "Kanyon manzarası ve huzur 🌅"),
        ("sample_05_workspace.jpg", 180, "Gece mesaisi başladı 💻🌙"),
        ("sample_06_beach.jpg", 1011, "Göl kenarında sakin bir öğleden sonra 🛶🌊"),
        ("sample_07_forest.jpg", 1043, "Sisli orman yürüyüşü 🌲🌫️"),
        ("sample_08_city.jpg", 43, "Şehrin ışıkları 🌃✨"),
        ("sample_09_vintage.jpg", 250, "Eski kamerayla denemeler 📸🎞️"),
        ("sample_10_berries.jpg", 1080, "Taze çilek zamanı 🍓😋"),
        ("sample_11_camp.jpg", 1039, "Doğanın kalbinde şelale sesi 🌿💧"),
        ("sample_12_architecture.jpg", 1040, "Masal şatosu gibi 🏰✨"),
        ("sample_13_coastal.jpg", 1050, "Kayalıklar ve okyanus rüzgarı 🌊"),
        ("sample_14_roadtrip.jpg", 1071, "Yolculuk başlasın! 🚗💨"),
        ("sample_15_tea.jpg", 225, "Yağmurlu havada sıcak çay keyfi 🍵🌧️"),
    ]
    for filename, pid, _ in sample_photos:
        ensure_image(pid, 800, os.path.join(MEDIA_LOCAL_DIR, filename))

    db_5554_path = os.path.join(SEED_DIR, "PocketLocal_5554.db3")
    db_5556_path = os.path.join(SEED_DIR, "PocketLocal_5556.db3")

    con_5554 = sqlite3.connect(db_5554_path)
    cur_5554 = con_5554.cursor()
    ensure_schema(cur_5554)

    con_5556 = sqlite3.connect(db_5556_path)
    cur_5556 = con_5556.cursor()
    ensure_schema(cur_5556)

    now = datetime.now(timezone.utc)
    created_ticks = dt_to_ticks(now - timedelta(days=30))

    id_5554 = DEFAULT_5554["Id"]
    pub_5554 = DEFAULT_5554["PublicKey"]
    priv_5554 = DEFAULT_5554["EncryptedPrivateKey"]

    id_5556 = DEFAULT_5556["Id"]
    pub_5556 = DEFAULT_5556["PublicKey"]
    priv_5556 = DEFAULT_5556["EncryptedPrivateKey"]

    for cur in (cur_5554, cur_5556):
        cur.execute("DELETE FROM User")
        cur.execute("DELETE FROM Friend")
        cur.execute("DELETE FROM MediaPost")
        cur.execute("DELETE FROM DirectMessage")

    cur_5554.execute("""
        INSERT INTO User (Id, Username, DisplayName, ProfilePictureUrl, PublicKey, EncryptedPrivateKey, CreatedAt)
        VALUES (?, ?, ?, ?, ?, ?, ?)
    """, (id_5554, DEFAULT_5554["Username"], DEFAULT_5554["DisplayName"],
          f"{APP_FILES_BASE}/Profile/avatar_mert.jpg", pub_5554, priv_5554, created_ticks))

    cur_5556.execute("""
        INSERT INTO User (Id, Username, DisplayName, ProfilePictureUrl, PublicKey, EncryptedPrivateKey, CreatedAt)
        VALUES (?, ?, ?, ?, ?, ?, ?)
    """, (id_5556, DEFAULT_5556["Username"], DEFAULT_5556["DisplayName"],
          f"{APP_FILES_BASE}/Profile/avatar_selin.jpg", pub_5556, priv_5556, created_ticks))

    friend_zeynep_id = "f1111111-2222-4333-8444-555555555501"
    friend_can_id    = "f1111111-2222-4333-8444-555555555502"
    friend_elif_id   = "f1111111-2222-4333-8444-555555555503"

    friends_5554 = [
        (id_5556, "selindemir", "Selin Demir", f"{APP_FILES_BASE}/Profile/friend_selin.jpg", pub_5556, 0,
         dt_to_ticks(now - timedelta(days=14)), dt_to_ticks(now - timedelta(minutes=5))),
        (friend_zeynep_id, "zeynepyilmaz", "Zeynep Yılmaz", f"{APP_FILES_BASE}/Profile/friend_zeynep.jpg", pub_5556, 0,
         dt_to_ticks(now - timedelta(days=10)), dt_to_ticks(now - timedelta(minutes=22))),
        (friend_can_id, "canozkan", "Can Özkan", f"{APP_FILES_BASE}/Profile/friend_can.jpg", pub_5556, 0,
         dt_to_ticks(now - timedelta(days=8)), dt_to_ticks(now - timedelta(hours=2))),
        (friend_elif_id, "elifarslan", "Elif Arslan", f"{APP_FILES_BASE}/Profile/friend_elif.jpg", pub_5556, 0,
         dt_to_ticks(now - timedelta(days=5)), dt_to_ticks(now - timedelta(hours=5))),
    ]

    friends_5556 = [
        (id_5554, "mertkaya", "Mert Kaya", f"{APP_FILES_BASE}/Profile/friend_mert.jpg", pub_5554, 0,
         dt_to_ticks(now - timedelta(days=14)), dt_to_ticks(now - timedelta(minutes=5))),
        (friend_zeynep_id, "zeynepyilmaz", "Zeynep Yılmaz", f"{APP_FILES_BASE}/Profile/friend_zeynep.jpg", pub_5554, 0,
         dt_to_ticks(now - timedelta(days=10)), dt_to_ticks(now - timedelta(minutes=22))),
        (friend_can_id, "canozkan", "Can Özkan", f"{APP_FILES_BASE}/Profile/friend_can.jpg", pub_5554, 0,
         dt_to_ticks(now - timedelta(days=8)), dt_to_ticks(now - timedelta(hours=2))),
        (friend_elif_id, "elifarslan", "Elif Arslan", f"{APP_FILES_BASE}/Profile/friend_elif.jpg", pub_5554, 0,
         dt_to_ticks(now - timedelta(days=5)), dt_to_ticks(now - timedelta(hours=5))),
    ]

    for f in friends_5554:
        cur_5554.execute("""
            INSERT INTO Friend (Id, Username, DisplayName, ProfilePictureUrl, PublicKey, IsMuted, AddedAt, LastInteractionAt)
            VALUES (?, ?, ?, ?, ?, ?, ?, ?)
        """, f)

    for f in friends_5556:
        cur_5556.execute("""
            INSERT INTO Friend (Id, Username, DisplayName, ProfilePictureUrl, PublicKey, IsMuted, AddedAt, LastInteractionAt)
            VALUES (?, ?, ?, ?, ?, ?, ?, ?)
        """, f)

    post_definitions = [
        ("a0000001-0000-4000-8000-000000000001", 0,  "5554",   8,    [("peer", "💗"), ("zeynep", "🔥"), ("can", "😋")]),
        ("a0000001-0000-4000-8000-000000000002", 1,  "5556",   18,   [("peer", "🔥"), ("elif", "💗"), ("zeynep", "🙃")]),
        ("a0000001-0000-4000-8000-000000000003", 2,  "zeynep", 35,   []),
        ("a0000001-0000-4000-8000-000000000004", 3,  "5554",   65,   [("peer", "💗"), ("elif", "❤️")]),
        ("a0000001-0000-4000-8000-000000000005", 4,  "can",    110,  []),
        ("a0000001-0000-4000-8000-000000000006", 5,  "5556",   160,  [("peer", "😋"), ("can", "🔥"), ("zeynep", "💗")]),
        ("a0000001-0000-4000-8000-000000000007", 6,  "elif",   240,  []),
        ("a0000001-0000-4000-8000-000000000008", 7,  "5554",   320,  [("zeynep", "🙃"), ("can", "🔥"), ("elif", "💗")]),
        ("a0000001-0000-4000-8000-000000000009", 8,  "zeynep", 480,  []),
        ("a0000001-0000-4000-8000-000000000010", 9,  "5556",   720,  [("peer", "💗"), ("elif", "😋")]),
        ("a0000001-0000-4000-8000-000000000011", 10, "can",    1100, []),
        ("a0000001-0000-4000-8000-000000000012", 11, "5554",   1500, [("peer", "🔥")]),
        ("a0000001-0000-4000-8000-000000000013", 12, "elif",   2100, []),
        ("a0000001-0000-4000-8000-000000000014", 13, "5556",   2800, [("zeynep", "💗"), ("can", "🙃")]),
        ("a0000001-0000-4000-8000-000000000015", 14, "zeynep", 3600, []),
    ]

    def resolve_friend_id(key: str, current_device: str) -> str:
        if key == "peer":
            return id_5556 if current_device == "5554" else id_5554
        if key == "zeynep":
            return friend_zeynep_id
        if key == "can":
            return friend_can_id
        if key == "elif":
            return friend_elif_id
        return ""

    for post_id, photo_idx, sender_key, mins_ago, reactions in post_definitions:
        filename, _, caption = sample_photos[photo_idx]
        media_path = f"{APP_FILES_BASE}/Media/{filename}"
        sent_dt = now - timedelta(minutes=mins_ago)
        sent_ticks = dt_to_ticks(sent_dt)

        if sender_key == "5554":
            is_inc_5554, friend_id_5554, rollcall_5554 = 0, "", 1
        elif sender_key == "5556":
            is_inc_5554, friend_id_5554, rollcall_5554 = 1, id_5556, 0
        else:
            is_inc_5554, friend_id_5554, rollcall_5554 = 1, resolve_friend_id(sender_key, "5554"), 0

        cur_5554.execute("""
            INSERT INTO MediaPost (Id, FriendId, IsIncoming, MediaLocalPath, MediaType, Caption, IsIncludedInRollcall, SentAt, SyncStatus)
            VALUES (?, ?, ?, ?, 1, ?, ?, ?, 2)
        """, (post_id, friend_id_5554, is_inc_5554, media_path, caption, rollcall_5554, sent_ticks))

        if sender_key == "5556":
            is_inc_5556, friend_id_5556, rollcall_5556 = 0, "", 1
        elif sender_key == "5554":
            is_inc_5556, friend_id_5556, rollcall_5556 = 1, id_5554, 0
        else:
            is_inc_5556, friend_id_5556, rollcall_5556 = 1, resolve_friend_id(sender_key, "5556"), 0

        cur_5556.execute("""
            INSERT INTO MediaPost (Id, FriendId, IsIncoming, MediaLocalPath, MediaType, Caption, IsIncludedInRollcall, SentAt, SyncStatus)
            VALUES (?, ?, ?, ?, 1, ?, ?, ?, 2)
        """, (post_id, friend_id_5556, is_inc_5556, media_path, caption, rollcall_5556, sent_ticks))

        for idx_r, (reactor_key, emoji) in enumerate(reactions):
            react_ticks = dt_to_ticks(sent_dt + timedelta(minutes=1 + idx_r))
            if sender_key == "5554":
                r_friend = resolve_friend_id(reactor_key, "5554")
                cur_5554.execute("""
                    INSERT INTO DirectMessage (Id, MediaPostId, FriendId, IsIncoming, TextContent, ReactionEmoji, SentAt, SyncStatus)
                    VALUES (?, ?, ?, 1, '', ?, ?, 2)
                """, (str(uuid.uuid4()), post_id, r_friend, emoji, react_ticks))
            elif sender_key == "5556":
                r_friend = resolve_friend_id(reactor_key, "5556")
                cur_5556.execute("""
                    INSERT INTO DirectMessage (Id, MediaPostId, FriendId, IsIncoming, TextContent, ReactionEmoji, SentAt, SyncStatus)
                    VALUES (?, ?, ?, 1, '', ?, ?, 2)
                """, (str(uuid.uuid4()), post_id, r_friend, emoji, react_ticks))

    mutual_dms = [
        ("5554", "a0000001-0000-4000-8000-000000000006", "Göl manzarası harika çıkmış, neresi burası? 😍", None, 150),
        ("5556", None, "Bolu tarafında küçük bir göl! Haftaya beraber gidelim mi? 🌲", None, 145),
        ("5554", None, "Süper fikir, kesinlikle gidelim! 🚗✨", None, 140),
        ("5556", "a0000001-0000-4000-8000-000000000001", "Afiyet olsun! Bana da bir latte borcun olsun ☕😄", None, 6),
        ("5554", None, "Her zaman! Akşam uğrarsan ısmarlarım 🙌", None, 4),
    ]

    for sender, post_ref, text, emoji, mins_ago in mutual_dms:
        dm_id = str(uuid.uuid4())
        t_ticks = dt_to_ticks(now - timedelta(minutes=mins_ago))
        cur_5554.execute("""
            INSERT INTO DirectMessage (Id, MediaPostId, FriendId, IsIncoming, TextContent, ReactionEmoji, SentAt, SyncStatus)
            VALUES (?, ?, ?, ?, ?, ?, ?, 2)
        """, (dm_id, post_ref, id_5556, 0 if sender == "5554" else 1, text, emoji, t_ticks))
        cur_5556.execute("""
            INSERT INTO DirectMessage (Id, MediaPostId, FriendId, IsIncoming, TextContent, ReactionEmoji, SentAt, SyncStatus)
            VALUES (?, ?, ?, ?, ?, ?, ?, 2)
        """, (dm_id, post_ref, id_5554, 0 if sender == "5556" else 1, text, emoji, t_ticks))

    other_dms = [
        (friend_zeynep_id, "a0000001-0000-4000-8000-000000000003", 0, "Çok tatlı bakıyor ya! Adı ne? 🐶", None, 30),
        (friend_zeynep_id, None, 1, "Adı Tarçın! Bütün gün peşimden ayrılmadı 🥰", None, 22),
        (friend_can_id, "a0000001-0000-4000-8000-000000000005", 0, "Kolay gelsin, proje yetişiyor mu? 💻", None, 95),
        (friend_can_id, None, 1, "Az kaldı, bu gece bitiriyoruz inşallah 🚀", None, 80),
        (friend_elif_id, "a0000001-0000-4000-8000-000000000007", 1, "Sabah yürüyüşünden selamlar! 🌲✨", None, 230),
        (friend_elif_id, None, 0, "Harika bir atmosfer, iyi yürüyüşler! 🙌", None, 210),
    ]

    for friend_id, post_ref, is_inc, text, emoji, mins_ago in other_dms:
        t_ticks = dt_to_ticks(now - timedelta(minutes=mins_ago))
        for cur in (cur_5554, cur_5556):
            cur.execute("""
                INSERT INTO DirectMessage (Id, MediaPostId, FriendId, IsIncoming, TextContent, ReactionEmoji, SentAt, SyncStatus)
                VALUES (?, ?, ?, ?, ?, ?, ?, 2)
            """, (str(uuid.uuid4()), post_ref, friend_id, is_inc, text, emoji, t_ticks))

    con_5554.commit()
    con_5554.close()
    con_5556.commit()
    con_5556.close()
    print("Seed databases (PocketLocal_5554.db3 & PocketLocal_5556.db3) refreshed with current timestamps!")


if __name__ == "__main__":
    main()

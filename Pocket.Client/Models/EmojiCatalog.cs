using System;
using System.Collections.Generic;
using System.Linq;

namespace Pocket.Client.Models
{
    public sealed class EmojiEntry
    {
        public string Emoji { get; }
        public int CategoryIndex { get; }
        public string Keywords { get; }

        public EmojiEntry(string emoji, int categoryIndex, string keywords)
        {
            Emoji = emoji;
            CategoryIndex = categoryIndex;
            Keywords = keywords;
        }
    }

    public sealed class EmojiCategoryDefinition
    {
        public int Index { get; }
        public string Title { get; }
        public IReadOnlyList<EmojiEntry> Entries { get; }

        public EmojiCategoryDefinition(int index, string title, IReadOnlyList<EmojiEntry> entries)
        {
            Index = index;
            Title = title;
            Entries = entries;
        }
    }

    public sealed class EmojiListRow
    {
        public bool IsHeader { get; init; }
        public bool IsEmojiRow => !IsHeader;
        public int CategoryIndex { get; init; }
        public string HeaderTitle { get; init; } = string.Empty;

        public string Emoji0 { get; init; } = string.Empty;
        public string Emoji1 { get; init; } = string.Empty;
        public string Emoji2 { get; init; } = string.Empty;
        public string Emoji3 { get; init; } = string.Empty;
        public string Emoji4 { get; init; } = string.Empty;
        public string Emoji5 { get; init; } = string.Empty;

        public bool HasEmoji0 => !string.IsNullOrEmpty(Emoji0);
        public bool HasEmoji1 => !string.IsNullOrEmpty(Emoji1);
        public bool HasEmoji2 => !string.IsNullOrEmpty(Emoji2);
        public bool HasEmoji3 => !string.IsNullOrEmpty(Emoji3);
        public bool HasEmoji4 => !string.IsNullOrEmpty(Emoji4);
        public bool HasEmoji5 => !string.IsNullOrEmpty(Emoji5);
    }

    public static class EmojiCatalog
    {
        private static IReadOnlyList<EmojiCategoryDefinition>? _cachedCategories;
        private static readonly object _syncLock = new();

        public static readonly string[] CategoryTitles =
        {
            "Smileys & Emotion",
            "People & Body",
            "Animals & Nature",
            "Food & Drink",
            "Travel & Places",
            "Activities",
            "Objects",
            "Symbols",
            "Flags"
        };

        public static IReadOnlyList<EmojiCategoryDefinition> GetSupportedCategories()
        {
            if (_cachedCategories != null)
                return _cachedCategories;

            lock (_syncLock)
            {
                if (_cachedCategories != null)
                    return _cachedCategories;

                var rawDefinitions = BuildRawDefinitions();
                var result = new List<EmojiCategoryDefinition>(rawDefinitions.Count);

#if ANDROID
                using var paint = new global::Android.Graphics.Paint();
#endif

                foreach (var cat in rawDefinitions)
                {
                    var supportedEntries = new List<EmojiEntry>(cat.Entries.Count);
                    foreach (var entry in cat.Entries)
                    {
#if ANDROID
                        if (!IsEmojiSupportedOnDevice(paint, entry.Emoji))
                            continue;
#endif
                        supportedEntries.Add(entry);
                    }

                    result.Add(new EmojiCategoryDefinition(cat.Index, cat.Title, supportedEntries));
                }

                _cachedCategories = result;
                return _cachedCategories;
            }
        }

#if ANDROID
        private static bool IsEmojiSupportedOnDevice(global::Android.Graphics.Paint paint, string emoji)
        {
            if (string.IsNullOrWhiteSpace(emoji))
                return false;

            try
            {
                if (paint.HasGlyph(emoji))
                    return true;

                // Some OEM fonts require stripping Variation Selector-16 (\uFE0F) when querying HasGlyph
                string stripped = emoji.Replace("\uFE0F", string.Empty);
                if (stripped.Length > 0 && stripped != emoji && paint.HasGlyph(stripped))
                    return true;

                return false;
            }
            catch
            {
                return true;
            }
        }
#endif

        public static List<EmojiListRow> BuildRows(string? searchQuery, out Dictionary<int, int> categoryHeaderIndices)
        {
            categoryHeaderIndices = new Dictionary<int, int>();
            var categories = GetSupportedCategories();
            var rows = new List<EmojiListRow>(100);
            string query = searchQuery?.Trim() ?? string.Empty;

            if (!string.IsNullOrEmpty(query))
            {
                var matched = new List<EmojiEntry>();
                foreach (var cat in categories)
                {
                    foreach (var entry in cat.Entries)
                    {
                        if (entry.Emoji.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                            entry.Keywords.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                            cat.Title.Contains(query, StringComparison.OrdinalIgnoreCase))
                        {
                            matched.Add(entry);
                        }
                    }
                }

                rows.Add(new EmojiListRow
                {
                    IsHeader = true,
                    CategoryIndex = 0,
                    HeaderTitle = matched.Count > 0 ? "Search Results" : "No results found"
                });

                AppendEmojiRows(rows, 0, matched);
                return rows;
            }

            foreach (var cat in categories)
            {
                if (cat.Entries.Count == 0)
                    continue;

                categoryHeaderIndices[cat.Index] = rows.Count;
                rows.Add(new EmojiListRow
                {
                    IsHeader = true,
                    CategoryIndex = cat.Index,
                    HeaderTitle = cat.Title
                });

                AppendEmojiRows(rows, cat.Index, cat.Entries);
            }

            return rows;
        }

        private static void AppendEmojiRows(List<EmojiListRow> rows, int categoryIndex, IReadOnlyList<EmojiEntry> entries)
        {
            for (int i = 0; i < entries.Count; i += 6)
            {
                rows.Add(new EmojiListRow
                {
                    IsHeader = false,
                    CategoryIndex = categoryIndex,
                    Emoji0 = i < entries.Count ? entries[i].Emoji : string.Empty,
                    Emoji1 = i + 1 < entries.Count ? entries[i + 1].Emoji : string.Empty,
                    Emoji2 = i + 2 < entries.Count ? entries[i + 2].Emoji : string.Empty,
                    Emoji3 = i + 3 < entries.Count ? entries[i + 3].Emoji : string.Empty,
                    Emoji4 = i + 4 < entries.Count ? entries[i + 4].Emoji : string.Empty,
                    Emoji5 = i + 5 < entries.Count ? entries[i + 5].Emoji : string.Empty
                });
            }
        }

        private static List<EmojiCategoryDefinition> BuildRawDefinitions()
        {
            return new List<EmojiCategoryDefinition>
            {
                // 0: Smileys & Emotion (Matches reference screenshot order first, followed by full set + latest Unicode)
                new(0, CategoryTitles[0], new List<EmojiEntry>
                {
                    new("😀", 0, "grinning smile happy face gülümseme mutlu yüz"),
                    new("😃", 0, "smiley happy open mouth gülen mutlu"),
                    new("😄", 0, "smile happy laugh глаза gülen neşeli"),
                    new("😁", 0, "beaming grin teeth sırıtma diş"),
                    new("😆", 0, "laughing squinting haha kahkaha"),
                    new("😅", 0, "sweat smile relief soğuk ter gülümseme"),
                    new("🤣", 0, "rofl rolling laughing gülmekten yıkılan"),
                    new("😂", 0, "joy tears laugh ağlayarak gülen kahkaha"),
                    new("🙂", 0, "slightly smiling hafif gülümseme"),
                    new("🙃", 0, "upside down face ters yüz"),
                    new("😉", 0, "wink göz kırpma"),
                    new("😊", 0, "blush smiling eyes utangaç mutlu"),
                    new("😇", 0, "innocent angel halo melek masum"),
                    new("🥰", 0, "smiling hearts love in love kalpli yüz aşık"),
                    new("😍", 0, "heart eyes love kalp gözlü aşık"),
                    new("🤩", 0, "star struck excited yıldız gözlü hayran"),
                    new("😘", 0, "kissing heart blow kiss öpücük kalp"),
                    new("😗", 0, "kissing face öpücük"),
                    new("☺️", 0, "smiling relaxed sıcak gülümseme"),
                    new("😚", 0, "kissing closed eyes gözü kapalı öpücük"),
                    new("😙", 0, "kissing smiling eyes gülerek öpücük"),
                    new("🥲", 0, "smiling tear proud grateful gözü yaşlı tebessüm"),
                    new("😋", 0, "yum delicious tongue lezzetli dil"),
                    new("😛", 0, "stuck out tongue dil çıkaran"),
                    new("😜", 0, "winking tongue crazy göz kırpıp dil çıkaran"),
                    new("🤪", 0, "zany crazy goofy çılgın deli"),
                    new("😝", 0, "squinting tongue şaka dil"),
                    new("🤑", 0, "money mouth rich dolar para zengin"),
                    new("🤗", 0, "hugging face warm kucaklama sarılma"),
                    new("🤭", 0, "hand over mouth giggle oops ağzını kapatan"),
                    new("🤫", 0, "shushing quiet secret şşş sessiz sır"),
                    new("🤔", 0, "thinking hmm düşünen"),
                    new("🤐", 0, "zipper mouth secret ağzı fermuarlı"),
                    new("🤨", 0, "raised eyebrow skeptical tek kaş şüpheci"),
                    new("😐", 0, "neutral straight face düz yüz tepkisiz"),
                    new("😑", 0, "expressionless ifadesiz"),
                    new("😶", 0, "no mouth speechless ağzı olmayan sessiz"),
                    new("🫥", 0, "dotted line invisible gizli görünmez"),
                    new("😏", 0, "smirking flirt sinsi gülüş"),
                    new("😒", 0, "unamused bored memnuniyetsiz bıkkın"),
                    new("🙄", 0, "rolling eyes göz deviren"),
                    new("😬", 0, "grimacing awkward diş sıkan"),
                    new("😮‍💨", 0, "exhaling sigh relief nefes veren of"),
                    new("🤥", 0, "lying pinocchio yalancı pinokyo"),
                    new("🫨", 0, "shaking face shock titreyen yüz şok"),
                    new("😌", 0, "relieved peaceful huzurlu rahatlamış"),
                    new("😔", 0, "pensive sad düşünceli üzgün"),
                    new("😪", 0, "sleepy uykulu"),
                    new("🤤", 0, "drooling ağzı sulanan"),
                    new("😴", 0, "sleeping zzz uyuyan"),
                    new("🫠", 0, "melting face hot embarrassed eriyen yüz"),
                    new("🫡", 0, "saluting face respect asker selamı saygı"),
                    new("🫢", 0, "open eyes hand over mouth gasp şaşkın"),
                    new("🫣", 0, "peeking eye shy gizlice bakan utangaç"),
                    new("🥹", 0, "holding back tears emotional duygulanan dolan göz"),
                    new("😷", 0, "mask sick doctor maskeli hasta"),
                    new("🤒", 0, "thermometer sick fever ateşli hasta"),
                    new("🤕", 0, "head bandage hurt yaralı sargılı"),
                    new("🤢", 0, "nauseated sick disgusting mide bulantısı"),
                    new("🤮", 0, "vomiting puke kusan"),
                    new("🤧", 0, "sneezing hapşıran"),
                    new("🥵", 0, "hot face sweating sıcaklamış terleyen"),
                    new("🥶", 0, "cold freezing donan üşüyen"),
                    new("🥴", 0, "woozy dizzy sarhoş sersem"),
                    new("😵", 0, "dizzy knocked out başı dönen"),
                    new("🤯", 0, "exploding head mind blown beyni patlayan şok"),
                    new("🤠", 0, "cowboy hat kovboy"),
                    new("🥳", 0, "partying celebrate birthday parti kutlama"),
                    new("🥸", 0, "disguised glasses mustache kılık değiştirmiş"),
                    new("😎", 0, "sunglasses cool havalı güneş gözlüklü"),
                    new("🤓", 0, "nerd glasses inek öğrenci"),
                    new("🧐", 0, "monocle curious inceleyen"),
                    new("😕", 0, "confused kafası karışık"),
                    new("🫤", 0, "diagonal mouth unsure kararsız"),
                    new("😟", 0, "worried endişeli"),
                    new("🙁", 0, "slightly frowning somurtkan"),
                    new("😮", 0, "open mouth wow şaşıran"),
                    new("😯", 0, "hushed surprised şaşkın"),
                    new("😲", 0, "astonished shocked çok şaşkın"),
                    new("😳", 0, "flushed embarrassed kızarmış utanan"),
                    new("🥺", 0, "pleading puppy eyes yalvaran masum göz"),
                    new("😦", 0, "frowning open mouth"),
                    new("😧", 0, "anguished"),
                    new("😨", 0, "fearful scared korkmuş"),
                    new("😰", 0, "anxious cold sweat endişeli terleyen"),
                    new("😥", 0, "sad relieved üzgün"),
                    new("😢", 0, "crying tear ağlayan gözyaşı"),
                    new("😭", 0, "loudly crying sobbing hüngür hüngür ağlayan"),
                    new("😱", 0, "screaming fear çığlık korku"),
                    new("😖", 0, "confounded"),
                    new("😣", 0, "persevering"),
                    new("😞", 0, "disappointed hayal kırıklığı"),
                    new("😓", 0, "downcast sweat"),
                    new("😩", 0, "weary tired yorgun bıkkın"),
                    new("😫", 0, "tired exhausted çok yorgun"),
                    new("🥱", 0, "yawning sleepy esneyen"),
                    new("😤", 0, "triumph steam nose kızgın burnundan soluyan"),
                    new("😡", 0, "pouting angry rage çok kızgın öfkeli"),
                    new("😠", 0, "angry mad kızgın sinirli"),
                    new("🤬", 0, "cursing symbols mouth küfreden çok sinirli"),
                    new("😈", 0, "smiling horns devil şeytan"),
                    new("👿", 0, "angry horns devil kızgın şeytan"),
                    new("💀", 0, "skull dead laughing kuru kafa"),
                    new("☠️", 0, "skull crossbones tehlike"),
                    new("💩", 0, "poop pile kaka"),
                    new("🤡", 0, "clown palyaço"),
                    new("👻", 0, "ghost spooky hayalet"),
                    new("👽", 0, "alien uzaylı"),
                    new("🤖", 0, "robot"),
                    new("😺", 0, "smiley cat gülen kedi"),
                    new("😻", 0, "heart eyes cat aşık kedi"),
                    new("😹", 0, "joy cat gülen kedi"),
                    new("🙈", 0, "see no evil monkey görmedim maymun"),
                    new("🙉", 0, "hear no evil monkey duymadım maymun"),
                    new("🙊", 0, "speak no evil monkey bilmiyorum maymun"),
                    new("🩷", 0, "pink heart love pembe kalp aşk"),
                    new("❤️", 0, "red heart love kırmızı kalp aşk"),
                    new("🧡", 0, "orange heart turuncu kalp"),
                    new("💛", 0, "yellow heart sarı kalp"),
                    new("💚", 0, "green heart yeşil kalp"),
                    new("💙", 0, "blue heart mavi kalp"),
                    new("🩵", 0, "light blue heart açık mavi kalp"),
                    new("💜", 0, "purple heart mor kalp"),
                    new("🖤", 0, "black heart siyah kalp"),
                    new("🩶", 0, "grey heart gri kalp"),
                    new("🤍", 0, "white heart beyaz kalp"),
                    new("🤎", 0, "brown heart kahverengi kalp"),
                    new("💔", 0, "broken heart kırık kalp"),
                    new("❤️‍🔥", 0, "heart on fire passionate yanan kalp ateşli"),
                    new("❤️‍🩹", 0, "mending heart iyileşen kalp"),
                    new("❣️", 0, "heart exclamation kalp ünlem"),
                    new("💕", 0, "two hearts iki kalp"),
                    new("💞", 0, "revolving hearts dönen kalpler"),
                    new("💓", 0, "beating heart atan kalp"),
                    new("💗", 0, "growing heart büyüyen pembe kalp"),
                    new("💖", 0, "sparkling heart ışıltılı kalp"),
                    new("💘", 0, "heart arrow cupid oklu kalp"),
                    new("💝", 0, "heart ribbon hediye kalp"),
                    new("💟", 0, "heart decoration"),
                    new("💋", 0, "kiss mark lips dudak öpücük izi"),
                    new("💯", 0, "hundred points 100 yüz tam puan"),
                    new("💢", 0, "anger symbol kızgınlık"),
                    new("💥", 0, "collision boom patlama"),
                    new("💫", 0, "dizzy star yıldız"),
                    new("💦", 0, "sweat droplets su damlası ter"),
                    new("💨", 0, "dashing away wind rüzgar hız"),
                    new("💤", 0, "zzz sleep uyku")
                }),

                // 1: People & Body
                new(1, CategoryTitles[1], new List<EmojiEntry>
                {
                    new("👋", 1, "waving hand hello bye el sallama merhaba"),
                    new("🤚", 1, "raised back of hand el"),
                    new("🖐️", 1, "hand fingers splayed beş parmak"),
                    new("✋", 1, "raised hand stop dur el"),
                    new("🖖", 1, "vulcan salute"),
                    new("🫱", 1, "rightwards hand"),
                    new("🫲", 1, "leftwards hand"),
                    new("🫳", 1, "palm down hand"),
                    new("🫴", 1, "palm up hand"),
                    new("🫷", 1, "leftwards pushing hand stop"),
                    new("🫸", 1, "rightwards pushing hand stop"),
                    new("👌", 1, "ok hand perfect tamam süper"),
                    new("🤌", 1, "pinched fingers italian ne istiyorsun"),
                    new("🤏", 1, "pinching hand small biraz azıcık"),
                    new("✌️", 1, "victory peace barış zafer"),
                    new("🤞", 1, "crossed fingers luck şans dilek"),
                    new("🫰", 1, "hand with index finger and thumb crossed money heart kore kalbi"),
                    new("🤟", 1, "love you gesture seni seviyorum"),
                    new("🤘", 1, "sign of the horns rock metal"),
                    new("🤙", 1, "call me hand shaka beni ara"),
                    new("👈", 1, "backhand index pointing left sol"),
                    new("👉", 1, "backhand index pointing right sağ"),
                    new("👆", 1, "backhand index pointing up yukarı"),
                    new("🖕", 1, "middle finger orta parmak"),
                    new("👇", 1, "backhand index pointing down aşağı"),
                    new("☝️", 1, "index pointing up bir dakika"),
                    new("🫵", 1, "index pointing at the viewer you sen"),
                    new("👍", 1, "thumbs up like good beğen tamam süper"),
                    new("👎", 1, "thumbs down dislike kötü beğenmedim"),
                    new("✊", 1, "raised fist yumruk güç"),
                    new("👊", 1, "oncoming fist brofist yumruk"),
                    new("🤛", 1, "left facing fist"),
                    new("🤜", 1, "right facing fist"),
                    new("👏", 1, "clapping hands applause alkış bravo"),
                    new("🙌", 1, "raising hands celebration yaşasın eller havaya"),
                    new("🫶", 1, "heart hands love kalp eller aşk"),
                    new("👐", 1, "open hands açık eller"),
                    new("🤲", 1, "palms up together prayer dua avuç"),
                    new("🤝", 1, "handshake deal tokalaşma anlaşma"),
                    new("🙏", 1, "folded hands please thank you pray lütfen teşekkür dua"),
                    new("✍️", 1, "writing hand yazan el"),
                    new("💅", 1, "nail polish sassy oje tırnak bakım"),
                    new("🤳", 1, "selfie özçekim"),
                    new("💪", 1, "flexed biceps strong muscle güç kas"),
                    new("🦵", 1, "leg bacak"),
                    new("🦶", 1, "foot ayak"),
                    new("👂", 1, "ear kulak"),
                    new("👃", 1, "nose burun"),
                    new("🧠", 1, "brain smart beyin zeka"),
                    new("🫀", 1, "anatomical heart kalp"),
                    new("🫁", 1, "lungs akciğer"),
                    new("🦷", 1, "tooth diş"),
                    new("🦴", 1, "bone kemik"),
                    new("👀", 1, "eyes looking gözler bakış"),
                    new("👁️", 1, "eye göz"),
                    new("👅", 1, "tongue dil"),
                    new("👄", 1, "mouth lips dudak ağız"),
                    new("🫦", 1, "biting lip flirt dudak ısırma"),
                    new("👶", 1, "baby bebek"),
                    new("🧒", 1, "child çocuk"),
                    new("👦", 1, "boy erkek çocuk"),
                    new("👧", 1, "girl kız çocuk"),
                    new("🧑", 1, "person kişi"),
                    new("👱", 1, "blond person sarışın"),
                    new("👨", 1, "man erkek"),
                    new("🧔", 1, "man beard sakallı"),
                    new("👩", 1, "woman kadın"),
                    new("🧓", 1, "older person yaşlı"),
                    new("👴", 1, "old man dede"),
                    new("👵", 1, "old woman nine"),
                    new("🙍", 1, "person frowning"),
                    new("🙎", 1, "person pouting"),
                    new("🙅", 1, "person gesturing no hayır"),
                    new("🙆", 1, "person gesturing ok tamam"),
                    new("💁", 1, "person tipping hand"),
                    new("🙋", 1, "person raising hand el kaldıran"),
                    new("🧏", 1, "deaf person"),
                    new("🙇", 1, "person bowing özür saygı"),
                    new("🤦", 1, "person facepalming elini yüzüne vuran"),
                    new("🤷", 1, "person shrugging idk bilmem omuz silken"),
                    new("👮", 1, "police officer polis"),
                    new("🕵️", 1, "detective dedektif"),
                    new("💂", 1, "guard muhafız"),
                    new("🥷", 1, "ninja"),
                    new("👷", 1, "construction worker işçi"),
                    new("🫅", 1, "person with crown kraliyet"),
                    new("🤴", 1, "prince prens"),
                    new("👸", 1, "princess prenses"),
                    new("👼", 1, "baby angel melek"),
                    new("🎅", 1, "santa claus noel baba"),
                    new("🦸", 1, "superhero süper kahraman"),
                    new("🦹", 1, "supervillain"),
                    new("🧙", 1, "mage wizard büyücü"),
                    new("🧚", 1, "fairy peri"),
                    new("🧛", 1, "vampire vampir"),
                    new("🧜", 1, "merperson denizkızı"),
                    new("💃", 1, "woman dancing dans eden kadın"),
                    new("🕺", 1, "man dancing dans eden erkek"),
                    new("👫", 1, "woman and man holding hands çift"),
                    new("💏", 1, "kiss couple öpüşen çift"),
                    new("💑", 1, "couple with heart aşık çift"),
                    new("👪", 1, "family aile"),
                    new("🗣️", 1, "speaking head konuşan"),
                    new("👤", 1, "bust in silhouette profil"),
                    new("👥", 1, "busts in silhouette friends arkadaşlar"),
                    new("🫂", 1, "people hugging sarılma kucaklaşma")
                }),

                // 2: Animals & Nature
                new(2, CategoryTitles[2], new List<EmojiEntry>
                {
                    new("🐶", 2, "dog face puppy köpek yavru"),
                    new("🐱", 2, "cat face kitty kedi"),
                    new("🐭", 2, "mouse face fare"),
                    new("🐹", 2, "hamster"),
                    new("🐰", 2, "rabbit face bunny tavşan"),
                    new("🦊", 2, "fox tilki"),
                    new("🐻", 2, "bear ayı"),
                    new("🐼", 2, "panda"),
                    new("🐻‍❄️", 2, "polar bear kutup ayısı"),
                    new("🐨", 2, "koala"),
                    new("🐯", 2, "tiger face kaplan"),
                    new("🦁", 2, "lion aslan"),
                    new("🐮", 2, "cow face inek"),
                    new("🐷", 2, "pig face domuz"),
                    new("🐸", 2, "frog kurbağa"),
                    new("🐵", 2, "monkey face maymun"),
                    new("🐔", 2, "chicken tavuk"),
                    new("🐧", 2, "penguin penguen"),
                    new("🐦", 2, "bird kuş"),
                    new("🐤", 2, "baby chick civciv"),
                    new("🦆", 2, "duck ördek"),
                    new("🦅", 2, "eagle kartal"),
                    new("🦉", 2, "owl baykuş"),
                    new("🦇", 2, "bat yarasa"),
                    new("🐺", 2, "wolf kurt"),
                    new("🐴", 2, "horse face at"),
                    new("🦄", 2, "unicorn tek boynuzlu at"),
                    new("🐝", 2, "honeybee bee arı"),
                    new("🦋", 2, "butterfly kelebek"),
                    new("🐌", 2, "snail salyangoz"),
                    new("🐞", 2, "lady beetle uğur böceği"),
                    new("🐢", 2, "turtle kaplumbağa"),
                    new("🐍", 2, "snake yılan"),
                    new("🦖", 2, "t-rex dinosaur dinozor"),
                    new("🐙", 2, "octopus ahtapot"),
                    new("🐬", 2, "dolphin yunus"),
                    new("🐳", 2, "spouting whale balina"),
                    new("🦈", 2, "shark köpekbalığı"),
                    new("🐊", 2, "crocodile timsah"),
                    new("🦍", 2, "gorilla goril"),
                    new("🐘", 2, "elephant fil"),
                    new("🦒", 2, "giraffe zürafa"),
                    new("🦘", 2, "kangaroo kanguru"),
                    new("🐎", 2, "horse racing at"),
                    new("🐕", 2, "dog köpek"),
                    new("🐈", 2, "cat kedi"),
                    new("🐈‍⬛", 2, "black cat kara kedi"),
                    new("🦢", 2, "swan kuğu"),
                    new("🦩", 2, "flamingo"),
                    new("🕊️", 2, "dove peace güvercin barış"),
                    new("🐾", 2, "paw prints pati izi"),
                    new("💐", 2, "bouquet flowers çiçek buketi"),
                    new("🌸", 2, "cherry blossom sakura bahar çiçeği"),
                    new("🌹", 2, "rose red flower gül"),
                    new("🌺", 2, "hibiscus flower çiçek"),
                    new("🌻", 2, "sunflower ayçiçeği"),
                    new("🌼", 2, "blossom papatya çiçek"),
                    new("🌷", 2, "tulip lale"),
                    new("🪻", 2, "hyacinth sümbül"),
                    new("🌱", 2, "seedling sprout filiz"),
                    new("🪴", 2, "potted plant saksı bitkisi"),
                    new("🌲", 2, "evergreen tree çam ağacı"),
                    new("🌳", 2, "deciduous tree ağaç"),
                    new("🌴", 2, "palm tree palmiye"),
                    new("🌵", 2, "cactus kaktüs"),
                    new("🍀", 2, "four leaf clover luck dört yapraklı yonca şans"),
                    new("🍁", 2, "maple leaf autumn sonbahar yaprak"),
                    new("🍄", 2, "mushroom mantar"),
                    new("🌍", 2, "earth globe dünya"),
                    new("🌕", 2, "full moon dolunay"),
                    new("🌙", 2, "crescent moon hilal ay"),
                    new("⭐", 2, "star yıldız"),
                    new("🌟", 2, "glowing star parlayan yıldız"),
                    new("✨", 2, "sparkles magic ışıltı parıltı"),
                    new("⚡", 2, "high voltage lightning şimşek yıldırım"),
                    new("🔥", 2, "fire flame hot lit ateş alev"),
                    new("🌈", 2, "rainbow gökkuşağı"),
                    new("☀️", 2, "sun sunny güneş"),
                    new("🌧️", 2, "cloud with rain yağmur"),
                    new("❄️", 2, "snowflake cold kar tanesi"),
                    new("⛄", 2, "snowman kardan adam"),
                    new("🌊", 2, "water wave sea deniz dalga")
                }),

                // 3: Food & Drink
                new(3, CategoryTitles[3], new List<EmojiEntry>
                {
                    new("🍏", 3, "green apple yeşil elma"),
                    new("🍎", 3, "red apple kırmızı elma"),
                    new("🍐", 3, "pear armut"),
                    new("🍊", 3, "tangerine orange mandalina portakal"),
                    new("🍋", 3, "lemon limon"),
                    new("🍌", 3, "banana muz"),
                    new("🍉", 3, "watermelon karpuz"),
                    new("🍇", 3, "grapes üzüm"),
                    new("🍓", 3, "strawberry çilek"),
                    new("🫐", 3, "blueberries yaban mersini"),
                    new("🍒", 3, "cherries kiraz"),
                    new("🍑", 3, "peach şeftali"),
                    new("🥭", 3, "mango"),
                    new("🍍", 3, "pineapple ananas"),
                    new("🥥", 3, "coconut hindistan cevizi"),
                    new("🥝", 3, "kiwi kivi"),
                    new("🍅", 3, "tomato domates"),
                    new("🥑", 3, "avocado avokado"),
                    new("🍆", 3, "eggplant patlıcan"),
                    new("🥔", 3, "potato patates"),
                    new("🥕", 3, "carrot havuç"),
                    new("🌽", 3, "ear of corn mısır"),
                    new("🌶️", 3, "hot pepper acı biber"),
                    new("🥒", 3, "cucumber salatalık"),
                    new("🥦", 3, "broccoli brokoli"),
                    new("🍄", 3, "mushroom mantar"),
                    new("🥐", 3, "croissant kruvasan"),
                    new("🍞", 3, "bread ekmek"),
                    new("🥖", 3, "baguette bread baget"),
                    new("🥨", 3, "pretzel simit"),
                    new("🧀", 3, "cheese wedge peynir"),
                    new("🥚", 3, "egg yumurta"),
                    new("🍳", 3, "cooking fried egg sahanda yumurta"),
                    new("🥞", 3, "pancakes pankek"),
                    new("🧇", 3, "waffle"),
                    new("🥓", 3, "bacon pastırma"),
                    new("🥩", 3, "cut of meat steak et biftek"),
                    new("🍗", 3, "poultry leg chicken tavuk budu"),
                    new("🍖", 3, "meat on bone et"),
                    new("🌭", 3, "hot dog sosisli"),
                    new("🍔", 3, "hamburger burger"),
                    new("🍟", 3, "french fries patates kızartması"),
                    new("🍕", 3, "pizza slice dilim"),
                    new("🥪", 3, "sandwich sandviç"),
                    new("🥙", 3, "stuffed flatbread döner dürüm kebap"),
                    new("🌮", 3, "taco"),
                    new("🌯", 3, "burrito dürüm"),
                    new("🥗", 3, "green salad salata"),
                    new("🍝", 3, "spaghetti pasta makarna"),
                    new("🍜", 3, "steaming bowl ramen noodle erişte çorba"),
                    new("🍲", 3, "pot of food yemek tencere"),
                    new("🍣", 3, "sushi suşi"),
                    new("🍱", 3, "bento box"),
                    new("🥟", 3, "dumpling mantı"),
                    new("🍚", 3, "cooked rice pilav"),
                    new("🍦", 3, "soft ice cream dondurma"),
                    new("🍨", 3, "ice cream dondurma"),
                    new("🍩", 3, "doughnut donut"),
                    new("🍪", 3, "cookie kurabiye"),
                    new("🎂", 3, "birthday cake doğum günü pastası"),
                    new("🍰", 3, "shortcake cake pasta"),
                    new("🧁", 3, "cupcake"),
                    new("🍫", 3, "chocolate bar çikolata"),
                    new("🍬", 3, "candy şeker"),
                    new("🍭", 3, "lollipop lolipop"),
                    new("🍯", 3, "honey pot bal"),
                    new("🍼", 3, "baby bottle biberon"),
                    new("🥛", 3, "glass of milk süt"),
                    new("☕", 3, "hot beverage coffee tea kahve çay"),
                    new("🫖", 3, "teapot çaydanlık"),
                    new("🍵", 3, "teacup yeşil çay"),
                    new("🧃", 3, "beverage box meyve suyu"),
                    new("🥤", 3, "cup with straw soğuk içecek kola"),
                    new("🧋", 3, "bubble tea boba"),
                    new("🍺", 3, "beer mug bira"),
                    new("🍻", 3, "clinking beer mugs şerefe bira"),
                    new("🥂", 3, "clinking glasses cheers şampanya kutlama"),
                    new("🍷", 3, "wine glass şarap"),
                    new("🍸", 3, "cocktail glass kokteyl"),
                    new("🍹", 3, "tropical drink kokteyl"),
                    new("🧊", 3, "ice cube buz"),
                    new("🍴", 3, "fork and knife çatal bıçak")
                }),

                // 4: Travel & Places
                new(4, CategoryTitles[4], new List<EmojiEntry>
                {
                    new("🚗", 4, "automobile car araba otomobil"),
                    new("🚕", 4, "taxi taksi"),
                    new("🚙", 4, "sport utility vehicle suv araba"),
                    new("🚌", 4, "bus otobüs"),
                    new("🏎️", 4, "racing car yarış arabası"),
                    new("🚓", 4, "police car polis arabası"),
                    new("🚑", 4, "ambulance ambulans"),
                    new("🚒", 4, "fire engine itfaiye"),
                    new("🚐", 4, "minibus minibüs"),
                    new("🛻", 4, "pickup truck kamyonet"),
                    new("🚚", 4, "delivery truck kamyon"),
                    new("🏍️", 4, "motorcycle motosiklet motor"),
                    new("🛵", 4, "motor scooter mobilet"),
                    new("🚲", 4, "bicycle bike bisiklet"),
                    new("🛴", 4, "kick scooter"),
                    new("🚨", 4, "police car light siren alarm"),
                    new("🚔", 4, "oncoming police car"),
                    new("🚆", 4, "train tren"),
                    new("🚇", 4, "metro"),
                    new("✈️", 4, "airplane flight uçak seyahat"),
                    new("🛫", 4, "airplane departure kalkış"),
                    new("🛬", 4, "airplane arrival iniş"),
                    new("🚁", 4, "helicopter helikopter"),
                    new("🚀", 4, "rocket space roket uzay"),
                    new("🛸", 4, "flying saucer ufo"),
                    new("⛵", 4, "sailboat yelkenli"),
                    new("🚤", 4, "speedboat sürat teknesi"),
                    new("🛳️", 4, "passenger ship cruise gemi"),
                    new("⚓", 4, "anchor çapa"),
                    new("🗺️", 4, "world map harita"),
                    new("🗽", 4, "statue of liberty"),
                    new("🗼", 4, "tokyo tower kule"),
                    new("🏰", 4, "castle şato kale"),
                    new("🏟️", 4, "stadium stadyum"),
                    new("🎡", 4, "ferris wheel dönme dolap"),
                    new("🎢", 4, "roller coaster lunapark"),
                    new("🏖️", 4, "beach with umbrella plaj sahil tatil"),
                    new("🏝️", 4, "desert island ada"),
                    new("🏜️", 4, "desert çöl"),
                    new("🌋", 4, "volcano yanardağ"),
                    new("⛰️", 4, "mountain dağ"),
                    new("🏔️", 4, "snow capped mountain karlı dağ"),
                    new("🏕️", 4, "camping kamp çadır"),
                    new("🏠", 4, "house home ev"),
                    new("🏡", 4, "house with garden bahçeli ev"),
                    new("🏢", 4, "office building ofis bina"),
                    new("🏥", 4, "hospital hastane"),
                    new("🏦", 4, "bank banka"),
                    new("🏨", 4, "hotel otel"),
                    new("🕌", 4, "mosque cami"),
                    new("⛪", 4, "church kilise"),
                    new("🌆", 4, "cityscape at dusk şehir"),
                    new("🌃", 4, "night with stars gece yıldızlar"),
                    new("🌉", 4, "bridge at night köprü")
                }),

                // 5: Activities
                new(5, CategoryTitles[5], new List<EmojiEntry>
                {
                    new("⚽", 5, "soccer ball football futbol topu"),
                    new("🏀", 5, "basketball basketbol"),
                    new("🏈", 5, "american football amerikan futbolu"),
                    new("⚾", 5, "baseball beyzbol"),
                    new("🎾", 5, "tennis tenis"),
                    new("🏐", 5, "volleyball voleybol"),
                    new("🏉", 5, "rugby football"),
                    new("🎱", 5, "pool 8 ball bilardo"),
                    new("🏓", 5, "ping pong masa tenisi"),
                    new("🏸", 5, "badminton"),
                    new("🥊", 5, "boxing glove boks eldiveni"),
                    new("🥋", 5, "martial arts uniform karate judo"),
                    new("⛳", 5, "flag in hole golf"),
                    new("⛸️", 5, "ice skate buz pateni"),
                    new("🎣", 5, "fishing pole balık tutma"),
                    new("🤿", 5, "diving mask dalış"),
                    new("🎿", 5, "skis kayak"),
                    new("🛹", 5, "skateboard kaykay"),
                    new("🏆", 5, "trophy cup winner şampiyon kupa ödül"),
                    new("🥇", 5, "1st place medal gold altın madalya birinci"),
                    new("🥈", 5, "2nd place medal silver gümüş madalya"),
                    new("🥉", 5, "3rd place medal bronze bronz madalya"),
                    new("🏅", 5, "sports medal madalya"),
                    new("🎖️", 5, "military medal"),
                    new("🎫", 5, "ticket bilet"),
                    new("🎭", 5, "performing arts theater tiyatro"),
                    new("🎨", 5, "artist palette art resim sanat"),
                    new("🎬", 5, "clapper board movie film sinema"),
                    new("🎤", 5, "microphone sing mikrofon şarkı"),
                    new("🎧", 5, "headphone music kulaklık müzik"),
                    new("🎼", 5, "musical score müzik notası"),
                    new("🎹", 5, "musical keyboard piano piyano"),
                    new("🥁", 5, "drum davul bateri"),
                    new("🎷", 5, "saxophone saksafon"),
                    new("🎺", 5, "trumpet trompet"),
                    new("🎸", 5, "guitar gitar"),
                    new("🎻", 5, "violin keman"),
                    new("🎲", 5, "game die dice zar oyun"),
                    new("♟️", 5, "chess pawn satranç"),
                    new("🎯", 5, "bullseye direct hit hedef tam isabet"),
                    new("🎳", 5, "bowling"),
                    new("🎮", 5, "video game controller oyun kolu konsol"),
                    new("🕹️", 5, "joystick oyun"),
                    new("🎰", 5, "slot machine"),
                    new("🧩", 5, "puzzle piece yapboz"),
                    new("🎃", 5, "jack o lantern halloween cadılar bayramı"),
                    new("🎄", 5, "christmas tree yılbaşı ağacı"),
                    new("🎆", 5, "fireworks havai fişek"),
                    new("🎇", 5, "sparkler maytap"),
                    new("🧨", 5, "firecracker"),
                    new("🎈", 5, "balloon balon"),
                    new("🎉", 5, "party popper celebrate kutlama konfeti"),
                    new("🎊", 5, "confetti ball konfeti"),
                    new("🎁", 5, "wrapped gift present hediye paketi"),
                    new("🎀", 5, "ribbon kurdele"),
                    new("🪄", 5, "magic wand sihirli değnek"),
                    new("🪩", 5, "mirror ball disco party disko topu")
                }),

                // 6: Objects
                new(6, CategoryTitles[6], new List<EmojiEntry>
                {
                    new("👓", 6, "glasses gözlük"),
                    new("🕶️", 6, "sunglasses güneş gözlüğü"),
                    new("🥽", 6, "goggles"),
                    new("👔", 6, "necktie kravat gömlek"),
                    new("👕", 6, "t-shirt tişört"),
                    new("👖", 6, "jeans pantolon"),
                    new("👗", 6, "dress elbise"),
                    new("👟", 6, "running shoe sneaker spor ayakkabı"),
                    new("👠", 6, "high heeled shoe topuklu ayakkabı"),
                    new("👑", 6, "crown king queen kral kraliçe taç"),
                    new("🎩", 6, "top hat şapka"),
                    new("🎓", 6, "graduation cap mezuniyet kep"),
                    new("🧢", 6, "billed cap şapka"),
                    new("💄", 6, "lipstick ruj makyaj"),
                    new("💍", 6, "ring diamond yüzük tektaş evlilik"),
                    new("💎", 6, "gem stone diamond elmas pırlanta"),
                    new("📱", 6, "mobile phone smartphone telefon cep telefonu"),
                    new("💻", 6, "laptop computer bilgisayar"),
                    new("⌚", 6, "watch kol saati"),
                    new("📷", 6, "camera photo fotoğraf makinesi kamera"),
                    new("📸", 6, "camera with flash flaşlı kamera"),
                    new("📹", 6, "video camera kamera"),
                    new("🎥", 6, "movie camera sinema kamerası"),
                    new("📞", 6, "telephone receiver telefon"),
                    new("📺", 6, "television tv televizyon"),
                    new("🔋", 6, "battery pil şarj"),
                    new("💡", 6, "light bulb idea ampul fikir"),
                    new("🔦", 6, "flashlight el feneri"),
                    new("🕯️", 6, "candle mum"),
                    new("💸", 6, "money with wings uçan para"),
                    new("💵", 6, "dollar banknote dolar para"),
                    new("💰", 6, "money bag para çuvalı zengin"),
                    new("💳", 6, "credit card kredi kartı"),
                    new("✉️", 6, "envelope mail mektup zarf"),
                    new("📦", 6, "package box paket kargo kutu"),
                    new("✏️", 6, "pencil kalem"),
                    new("✒️", 6, "black nib dolma kalem"),
                    new("📝", 6, "memo note not kağıt"),
                    new("📚", 6, "books kitaplar ders"),
                    new("📖", 6, "open book açık kitap okuma"),
                    new("📌", 6, "pushpin raptiye"),
                    new("📎", 6, "paperclip ataş"),
                    new("✂️", 6, "scissors makas"),
                    new("🔒", 6, "locked kilit"),
                    new("🔓", 6, "unlocked açık kilit"),
                    new("🔑", 6, "key anahtar"),
                    new("🔨", 6, "hammer çekiç"),
                    new("⚙️", 6, "gear settings ayarlar çark"),
                    new("🧲", 6, "magnet mıknatıs"),
                    new("💣", 6, "bomb bomba"),
                    new("🛡️", 6, "shield kalkan"),
                    new("💊", 6, "pill medicine ilaç hap"),
                    new("🩹", 6, "adhesive bandage yara bandı"),
                    new("🧸", 6, "teddy bear oyuncak ayı"),
                    new("🖼️", 6, "framed picture tablo çerçeve"),
                    new("🛍️", 6, "shopping bags alışveriş poşeti"),
                    new("🫧", 6, "bubbles köpük baloncuk")
                }),

                // 7: Symbols
                new(7, CategoryTitles[7], new List<EmojiEntry>
                {
                    new("❤️", 7, "red heart kırmızı kalp"),
                    new("💛", 7, "yellow heart sarı kalp"),
                    new("💚", 7, "green heart yeşil kalp"),
                    new("💙", 7, "blue heart mavi kalp"),
                    new("💜", 7, "purple heart mor kalp"),
                    new("🖤", 7, "black heart siyah kalp"),
                    new("🤍", 7, "white heart beyaz kalp"),
                    new("💔", 7, "broken heart kırık kalp"),
                    new("☮️", 7, "peace symbol barış"),
                    new("♾️", 7, "infinity sonsuzluk"),
                    new("❓", 7, "question mark soru işareti"),
                    new("❔", 7, "white question mark soru"),
                    new("❕", 7, "white exclamation mark ünlem"),
                    new("❗", 7, "exclamation mark ünlem"),
                    new("‼️", 7, "double exclamation mark çift ünlem"),
                    new("⁉️", 7, "exclamation question mark"),
                    new("⚠️", 7, "warning dikkat uyarı"),
                    new("🚫", 7, "prohibited forbidden yasak iptal"),
                    new("❌", 7, "cross mark wrong çarpı yanlış hayır"),
                    new("⭕", 7, "hollow red circle daire"),
                    new("✅", 7, "check mark button onay doğru tamam"),
                    new("☑️", 7, "check box with check onay"),
                    new("✔️", 7, "check mark tik onay"),
                    new("➕", 7, "plus artı"),
                    new("➖", 7, "minus eksi"),
                    new("➗", 7, "divide bölme"),
                    new("✖️", 7, "multiply çarpma"),
                    new("♻️", 7, "recycling symbol geri dönüşüm"),
                    new("⚜️", 7, "fleur-de-lis"),
                    new("🔱", 7, "trident emblem"),
                    new("📛", 7, "name badge"),
                    new("🔔", 7, "bell notification zil bildirim"),
                    new("🔕", 7, "bell with slash sessiz"),
                    new("🎵", 7, "musical note müzik notası"),
                    new("🎶", 7, "musical notes müzik notaları"),
                    new("💬", 7, "speech balloon mesaj balon"),
                    new("💭", 7, "thought balloon düşünce balonu"),
                    new("🕐", 7, "clock saat"),
                    new("⬆️", 7, "up arrow yukarı ok"),
                    new("⬇️", 7, "down arrow aşağı ok"),
                    new("⬅️", 7, "left arrow sol ok"),
                    new("➡️", 7, "right arrow sağ ok"),
                    new("🔄", 7, "counterclockwise arrows yenile"),
                    new("🔝", 7, "top arrow zirve"),
                    new("🆕", 7, "new button yeni"),
                    new("🆓", 7, "free button ücretsiz"),
                    new("🆗", 7, "ok button tamam"),
                    new("🆒", 7, "cool button havalı")
                }),

                // 8: Flags
                new(8, CategoryTitles[8], new List<EmojiEntry>
                {
                    new("🏳️", 8, "white flag beyaz bayrak"),
                    new("🏴", 8, "black flag siyah bayrak"),
                    new("🏁", 8, "chequered flag racing yarış bayrağı"),
                    new("🚩", 8, "triangular flag red flag kırmızı bayrak"),
                    new("🏳️‍🌈", 8, "rainbow flag gökkuşağı bayrağı"),
                    new("🏴‍☠️", 8, "pirate flag korsan bayrağı"),
                    new("🇹🇷", 8, "turkey flag türkiye bayrağı türk"),
                    new("🇦🇿", 8, "azerbaijan flag azerbaycan bayrağı"),
                    new("🇺🇸", 8, "united states usa flag amerika abd bayrağı"),
                    new("🇬🇧", 8, "united kingdom uk flag ingiltere birleşik krallık"),
                    new("🇩🇪", 8, "germany flag almanya bayrağı"),
                    new("🇫🇷", 8, "france flag fransa bayrağı"),
                    new("🇮🇹", 8, "italy flag italya bayrağı"),
                    new("🇪🇸", 8, "spain flag ispanya bayrağı"),
                    new("🇳🇱", 8, "netherlands flag hollanda bayrağı"),
                    new("🇧🇪", 8, "belgium flag belçika"),
                    new("🇨🇭", 8, "switzerland flag isviçre"),
                    new("🇦🇹", 8, "austria flag avusturya"),
                    new("🇸🇪", 8, "sweden flag isveç"),
                    new("🇳🇴", 8, "norway flag norveç"),
                    new("🇩🇰", 8, "denmark flag danimarka"),
                    new("🇫🇮", 8, "finland flag finlandiya"),
                    new("🇵🇱", 8, "poland flag polonya"),
                    new("🇬🇷", 8, "greece flag yunanistan"),
                    new("🇵🇹", 8, "portugal flag portekiz"),
                    new("🇮🇪", 8, "ireland flag irlanda"),
                    new("🇷🇺", 8, "russia flag rusya"),
                    new("🇺🇦", 8, "ukraine flag ukrayna"),
                    new("🇨🇦", 8, "canada flag kanada"),
                    new("🇲🇽", 8, "mexico flag meksika"),
                    new("🇧🇷", 8, "brazil flag brezilya"),
                    new("🇦🇷", 8, "argentina flag arjantin"),
                    new("🇯🇵", 8, "japan flag japonya"),
                    new("🇰🇷", 8, "south korea flag güney kore"),
                    new("🇨🇳", 8, "china flag çin"),
                    new("🇮🇳", 8, "india flag hindistan"),
                    new("🇦🇺", 8, "australia flag avustralya"),
                    new("🇳🇿", 8, "new zealand flag yeni zelanda"),
                    new("🇸🇦", 8, "saudi arabia flag suudi arabistan"),
                    new("🇦🇪", 8, "united arab emirates uae dubai bae"),
                    new("🇪🇬", 8, "egypt flag mısır"),
                    new("🇿🇦", 8, "south africa flag güney afrika"),
                    new("🇰🇿", 8, "kazakhstan flag kazakistan"),
                    new("🇺🇿", 8, "uzbekistan flag özbekistan"),
                    new("🇹🇲", 8, "turkmenistan flag türkmenistan"),
                    new("🇰🇬", 8, "kyrgyzstan flag kırgızistan")
                })
            };
        }
    }
}


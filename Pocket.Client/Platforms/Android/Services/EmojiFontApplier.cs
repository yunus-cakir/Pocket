using Android.Graphics;
using Android.Text;
using Android.Text.Style;
using Android.Widget;
using Microsoft.Maui.Handlers;
using Pocket.Client.Services;

namespace Pocket.Client.Platforms.Android.Services
{
    /// <summary>
    /// Draws emoji with the font picked in Settings → Appearance → Emoji layout.
    /// Only the emoji runs get a typeface span, so the surrounding text keeps its own font.
    /// Labels are re-processed after every mapper update that can rewrite their text;
    /// Entries/Editors are processed as the user types.
    /// </summary>
    public static class EmojiFontApplier
    {
        private static readonly Dictionary<EmojiLayout, Typeface?> TypefaceCache = new();
        private static readonly List<WeakReference<TextView>> TrackedViews = new();
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<TextView, object> Tracked = new();

        // Mapper keys after which MAUI may have replaced the Label's native text.
        private static readonly string[] LabelTextKeys =
        {
            nameof(ILabel.Text), "FormattedText", "TextType", nameof(ILabel.Font), nameof(ILabel.TextColor),
            nameof(ILabel.CharacterSpacing), nameof(ILabel.TextDecorations), nameof(ILabel.LineHeight), "TextTransform"
        };

        public static void Register()
        {
            LabelHandler.Mapper.AppendToMapping("PocketEmojiLayout", (handler, view) => ProcessLabel(handler.PlatformView, view));
            foreach (string key in LabelTextKeys)
            {
                LabelHandler.Mapper.AppendToMapping(key, (handler, view) => ProcessLabel(handler.PlatformView, view));
            }

            EntryHandler.Mapper.AppendToMapping("PocketEmojiLayout", (handler, _) => AttachEditable(handler.PlatformView));
            EditorHandler.Mapper.AppendToMapping("PocketEmojiLayout", (handler, _) => AttachEditable(handler.PlatformView));

            EmojiLayoutSettings.Changed += _ => RefreshAll();
        }

        private static EmojiLayout LayoutFor(IView? view)
        {
            if (view is BindableObject bindable && EmojiLayoutSettings.GetOverride(bindable) is { } forced)
                return forced;
            return EmojiLayoutSettings.Current;
        }

        private static Typeface? GetTypeface(EmojiLayout layout)
        {
            string? file = EmojiLayoutSettings.GetFontFile(layout);
            if (file == null)
                return null;
            if (TypefaceCache.TryGetValue(layout, out var cached))
                return cached;

            Typeface? typeface = null;
            try
            {
                if (EmojiLayoutSettings.IsAvailable(layout))
                    typeface = Typeface.CreateFromAsset(global::Android.App.Application.Context.Assets, file);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[EmojiLayout] Could not load {file}: {ex.Message}");
            }

            TypefaceCache[layout] = typeface;
            return typeface;
        }

        private static void Track(TextView textView, IView? view)
        {
            if (Tracked.TryGetValue(textView, out _))
                return;
            Tracked.Add(textView, view is BindableObject b ? new WeakReference<BindableObject>(b) : new object());
            lock (TrackedViews)
            {
                TrackedViews.RemoveAll(w => !w.TryGetTarget(out var t) || t.Handle == IntPtr.Zero);
                TrackedViews.Add(new WeakReference<TextView>(textView));
            }
        }

        private static IView? TrackedVirtualView(TextView textView) =>
            Tracked.TryGetValue(textView, out var entry) && entry is WeakReference<BindableObject> weak &&
            weak.TryGetTarget(out var bindable)
                ? bindable as IView
                : null;

        // ---------------------------------------------------------------- Labels

        private static void ProcessLabel(TextView textView, IView? view)
        {
            Track(textView, view);
            ApplyToLabel(textView, LayoutFor(view));
        }

        private static void ApplyToLabel(TextView textView, EmojiLayout layout)
        {
            var text = textView.TextFormatted;
            if (text == null || text.Length() == 0)
                return;

            string plain = text.ToString() ?? string.Empty;
            var ranges = EmojiScanner.Find(plain);
            bool hasOurSpans = text is ISpanned spanned && GetOurSpans(spanned).Length > 0;
            if (ranges.Count == 0 && !hasOurSpans)
                return; // leave plain strings alone (keeps StaticLayout ellipsizing intact)

            var typeface = GetTypeface(layout);
            if (typeface == null && !hasOurSpans)
                return; // system emoji, nothing to undo

            var spannable = new SpannableString(text);
            RemoveOurSpans(spannable);
            if (typeface != null)
                AddSpans(spannable, ranges, typeface);

            textView.SetText(spannable, TextView.BufferType.Spannable);
        }

        // ---------------------------------------------------------------- Entries / Editors

        private static void AttachEditable(EditText editText)
        {
            if (Tracked.TryGetValue(editText, out _))
                return;
            Track(editText, null);
            editText.AddTextChangedListener(new EmojiTextWatcher());
            if (editText.EditableText is { } editable)
                ApplyToEditable(editable, EmojiLayoutSettings.Current);
        }

        private static void ApplyToEditable(IEditable editable, EmojiLayout layout)
        {
            RemoveOurSpans(editable);
            var typeface = GetTypeface(layout);
            if (typeface == null || editable.Length() == 0)
                return;
            AddSpans(editable, EmojiScanner.Find(editable.ToString() ?? string.Empty), typeface);
        }

        private sealed class EmojiTextWatcher : Java.Lang.Object, ITextWatcher
        {
            public void AfterTextChanged(IEditable? s)
            {
                if (s != null)
                    ApplyToEditable(s, EmojiLayoutSettings.Current);
            }

            public void BeforeTextChanged(Java.Lang.ICharSequence? s, int start, int count, int after) { }
            public void OnTextChanged(Java.Lang.ICharSequence? s, int start, int before, int count) { }
        }

        // ---------------------------------------------------------------- Refresh / spans

        private static void RefreshAll()
        {
            List<TextView> alive = new();
            lock (TrackedViews)
            {
                TrackedViews.RemoveAll(w => !w.TryGetTarget(out var t) || t.Handle == IntPtr.Zero);
                foreach (var weak in TrackedViews)
                {
                    if (weak.TryGetTarget(out var tv))
                        alive.Add(tv);
                }
            }

            foreach (var tv in alive)
            {
                try
                {
                    if (tv is EditText et)
                    {
                        if (et.EditableText is { } editable)
                            ApplyToEditable(editable, EmojiLayoutSettings.Current);
                    }
                    else
                    {
                        ApplyToLabel(tv, LayoutFor(TrackedVirtualView(tv)));
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[EmojiLayout] Refresh failed: {ex.Message}");
                }
            }
        }

        private static Java.Lang.Object[] GetOurSpans(ISpanned spanned) =>
            spanned.GetSpans(0, spanned.Length(), Java.Lang.Class.FromType(typeof(EmojiTypefaceSpan))) ?? Array.Empty<Java.Lang.Object>();

        private static void RemoveOurSpans(ISpannable spannable)
        {
            foreach (var span in GetOurSpans(spannable))
                spannable.RemoveSpan(span);
        }

        private static void AddSpans(ISpannable spannable, List<(int Start, int Length)> ranges, Typeface typeface)
        {
            foreach (var (start, length) in ranges)
                spannable.SetSpan(new EmojiTypefaceSpan(typeface), start, start + length, SpanTypes.ExclusiveExclusive);
        }

        /// <summary>Switches the paint to the emoji typeface for one emoji run (works on every API level).</summary>
        private sealed class EmojiTypefaceSpan : MetricAffectingSpan
        {
            private readonly Typeface _typeface;

            public EmojiTypefaceSpan(Typeface typeface) => _typeface = typeface;

            public override void UpdateDrawState(TextPaint? tp) => tp?.SetTypeface(_typeface);
            public override void UpdateMeasureState(TextPaint textPaint) => textPaint.SetTypeface(_typeface);
        }
    }

    /// <summary>Finds emoji runs (UTF-16 offsets) in a string: pictographs, ZWJ sequences, flags, keycaps.</summary>
    internal static class EmojiScanner
    {
        public static List<(int Start, int Length)> Find(string text)
        {
            var result = new List<(int, int)>();
            int i = 0;
            while (i < text.Length)
            {
                int start = i;
                int cp = CodePointAt(text, i, out int len);

                // Keycap: [0-9#*] FE0F? 20E3
                if ((cp is >= '0' and <= '9') || cp == '#' || cp == '*')
                {
                    int j = i + 1;
                    if (j < text.Length && text[j] == '️') j++;
                    if (j < text.Length && text[j] == '⃣')
                    {
                        result.Add((start, j + 1 - start));
                        i = j + 1;
                        continue;
                    }
                    i += len;
                    continue;
                }

                // Flags: two regional indicators.
                if (IsRegionalIndicator(cp))
                {
                    i += len;
                    if (i < text.Length)
                    {
                        int next = CodePointAt(text, i, out int nlen);
                        if (IsRegionalIndicator(next)) i += nlen;
                    }
                    result.Add((start, i - start));
                    continue;
                }

                bool followedByVs16 = i + len < text.Length && text[i + len] == '️';
                bool followedByVs15 = i + len < text.Length && text[i + len] == '︎';
                if (followedByVs15 || !(IsEmojiPresentation(cp) || (followedByVs16 && cp >= 0x00A9)))
                {
                    i += len;
                    continue;
                }

                i += len;
                // Modifiers, variation selectors, tags and ZWJ-joined components.
                while (i < text.Length)
                {
                    int c = CodePointAt(text, i, out int clen);
                    if (c == 0xFE0F || c == 0x20E3 || c is >= 0x1F3FB and <= 0x1F3FF || c is >= 0xE0020 and <= 0xE007F)
                    {
                        i += clen;
                        continue;
                    }
                    if (c == 0x200D && i + 1 < text.Length)
                    {
                        int joined = CodePointAt(text, i + 1, out int jlen);
                        if (joined >= 0x2000 && !char.IsWhiteSpace((char)Math.Min(joined, 0xFFFF)))
                        {
                            i += 1 + jlen;
                            continue;
                        }
                    }
                    break;
                }
                result.Add((start, i - start));
            }
            return result;
        }

        private static bool IsRegionalIndicator(int cp) => cp is >= 0x1F1E6 and <= 0x1F1FF;

        // Emoji drawn as colour emoji without a VS16 (Emoji_Presentation=Yes), approximated.
        private static bool IsEmojiPresentation(int cp) =>
            cp is >= 0x1F000 and <= 0x1FAFF ||
            cp is 0x231A or 0x231B or 0x23F0 or 0x23F3 or 0x25FD or 0x25FE or 0x2614 or 0x2615 or 0x267F or 0x2693 or
                0x26A1 or 0x26AA or 0x26AB or 0x26BD or 0x26BE or 0x26C4 or 0x26C5 or 0x26CE or 0x26D4 or 0x26EA or
                0x26F2 or 0x26F3 or 0x26F5 or 0x26FA or 0x26FD or 0x2705 or 0x270A or 0x270B or 0x2728 or 0x274C or
                0x274E or 0x2757 or 0x27B0 or 0x27BF or 0x2B1B or 0x2B1C or 0x2B50 or 0x2B55 ||
            cp is >= 0x23E9 and <= 0x23EC ||
            cp is >= 0x2648 and <= 0x2653 ||
            cp is >= 0x2753 and <= 0x2755 ||
            cp is >= 0x2795 and <= 0x2797;

        private static int CodePointAt(string text, int index, out int length)
        {
            if (char.IsHighSurrogate(text[index]) && index + 1 < text.Length && char.IsLowSurrogate(text[index + 1]))
            {
                length = 2;
                return char.ConvertToUtf32(text[index], text[index + 1]);
            }
            length = 1;
            return text[index];
        }
    }
}

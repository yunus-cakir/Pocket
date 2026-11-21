using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Graphics;

namespace Pocket.Client.Controls
{
    /// <summary>
    /// Round emoji badge on a chat bubble's corner. When <see cref="Emoji"/> changes on the same message
    /// (a live reaction, not a recycled list row) it pops in: grows past full size, then settles.
    /// </summary>
    public class ReactionBadge : Border
    {
        public static readonly BindableProperty EmojiProperty = BindableProperty.Create(
            nameof(Emoji), typeof(string), typeof(ReactionBadge), null, propertyChanged: OnEmojiChanged);

        private readonly Label _label;
        private object? _settledContext;

        public ReactionBadge()
        {
            WidthRequest = 36;
            HeightRequest = 36;
            Padding = 0;
            StrokeShape = new RoundRectangle { CornerRadius = 18 };
            Stroke = Colors.Black;
            StrokeThickness = 2;
            IsVisible = false;
            InputTransparent = true;

            _label = new Label
            {
                FontSize = 15,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center,
                HorizontalTextAlignment = TextAlignment.Center,
                VerticalTextAlignment = TextAlignment.Center
            };
            Content = _label;
        }

        public string? Emoji
        {
            get => (string?)GetValue(EmojiProperty);
            set => SetValue(EmojiProperty, value);
        }

        protected override void OnBindingContextChanged()
        {
            base.OnBindingContextChanged();
            // Bindings of the new row are applied by now: later changes are live reactions.
            _settledContext = BindingContext;
        }

        private static void OnEmojiChanged(BindableObject bindable, object oldValue, object newValue)
        {
            var badge = (ReactionBadge)bindable;
            var emoji = newValue as string;
            bool hasEmoji = !string.IsNullOrWhiteSpace(emoji);

            badge._label.Text = emoji;
            badge.AbortAnimation("ReactionPop");

            bool isLiveChange = badge.BindingContext != null && ReferenceEquals(badge.BindingContext, badge._settledContext);
            if (!hasEmoji)
            {
                badge.IsVisible = false;
                badge.Scale = 1;
                return;
            }

            badge.IsVisible = true;
            if (isLiveChange)
            {
                badge.Pop();
            }
            else
            {
                badge.Scale = 1;
            }
        }

        private void Pop()
        {
            // Damped spring: shoots past full size, then keeps swelling and shrinking a little less each
            // time until it settles (1.27 → 0.94 → 1.03 → 0.99 → 1).
            Scale = 0;
            var pop = new Animation(
                t => Scale = 1 - System.Math.Exp(-5.2 * t) * System.Math.Cos(3.6 * System.Math.PI * t),
                0, 1, Easing.Linear);
            pop.Commit(this, "ReactionPop", rate: 16, length: 760, finished: (_, _) => Scale = 1);
        }
    }
}

using System;
using Microsoft.Maui.Controls;

namespace Screen_Painter.Behaviors;

public class ShimmerBehavior : Behavior<VisualElement>
{
    private const string AnimationName = "ScreenPainterShimmerSweep";
    private const uint SweepDurationMs = 1200;

    private VisualElement? _element;
    private VisualElement? _attachedParent;

    protected override void OnAttachedTo(VisualElement bindable)
    {
        base.OnAttachedTo(bindable);
        _element = bindable;
        bindable.SizeChanged += OnSizeChanged;
        _attachedParent = bindable.Parent as VisualElement;
        if (_attachedParent != null)
            _attachedParent.SizeChanged += OnSizeChanged;
        StartSweep();
    }

    protected override void OnDetachingFrom(VisualElement bindable)
    {
        bindable.SizeChanged -= OnSizeChanged;
        // Unsubscribe from the exact parent instance we subscribed to at attach time.
        // Subscribing to whatever parent is present at detach time leaks the original
        // parent's subscription if the element was reparented in between.
        if (_attachedParent != null)
            _attachedParent.SizeChanged -= OnSizeChanged;
        _attachedParent = null;
        bindable.AbortAnimation(AnimationName);
        _element = null;
        base.OnDetachingFrom(bindable);
    }

    private void OnSizeChanged(object? sender, EventArgs e) => StartSweep();

    private void StartSweep()
    {
        var element = _element;
        if (element is null || !element.IsVisible)
            return;

        double stripeWidth = element.Width;
        double containerWidth = (_attachedParent ?? element.Parent as VisualElement)?.Width ?? 0;

        if (double.IsNaN(stripeWidth) || stripeWidth <= 0 ||
            double.IsNaN(containerWidth) || containerWidth <= 0)
            return;

        element.AbortAnimation(AnimationName);

        double start = -stripeWidth;
        double end = containerWidth + stripeWidth;
        element.TranslationX = start;

        var animation = new Animation(
            v => element.TranslationX = v,
            start,
            end);

        animation.Commit(
            element,
            AnimationName,
            length: SweepDurationMs,
            easing: Easing.SinInOut,
            repeat: () => element.IsVisible);
    }
}

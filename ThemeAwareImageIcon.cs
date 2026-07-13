using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Wpf.Ui.Controls;

namespace ChatGPTUpdater;

/// <summary>
/// Uses a monochrome bitmap as an alpha mask and paints it with the current theme brush.
/// </summary>
public sealed class ThemeAwareImageIcon : IconElement
{
    public static readonly DependencyProperty SourceProperty = DependencyProperty.Register(
        nameof(Source),
        typeof(ImageSource),
        typeof(ThemeAwareImageIcon),
        new FrameworkPropertyMetadata(
            null,
            FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender,
            OnSourceChanged));

    private Border? _icon;
    private ImageBrush? _mask;

    public ImageSource? Source
    {
        get => (ImageSource?)GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    protected override UIElement InitializeChildren()
    {
        _mask = new ImageBrush(Source)
        {
            Stretch = Stretch.Uniform,
            AlignmentX = AlignmentX.Center,
            AlignmentY = AlignmentY.Center
        };

        _icon = new Border
        {
            Background = Foreground,
            OpacityMask = _mask,
            SnapsToDevicePixels = true
        };

        return _icon;
    }

    protected override void OnForegroundChanged(DependencyPropertyChangedEventArgs args)
    {
        base.OnForegroundChanged(args);

        if (_icon is not null)
            _icon.SetCurrentValue(Border.BackgroundProperty, (Brush)args.NewValue);
    }

    private static void OnSourceChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        var icon = (ThemeAwareImageIcon)dependencyObject;
        if (icon._mask is not null)
            icon._mask.ImageSource = (ImageSource?)args.NewValue;
    }
}

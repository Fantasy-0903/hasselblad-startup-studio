using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
namespace StartupStudio;

public sealed class CameraPreview : Control
{
    private GrayImage? image; private WriteableBitmap? bitmap;
    public GrayImage? Image { get => image; set { bitmap?.Dispose(); image = value; bitmap = value == null ? null : Images.Bitmap(value); InvalidateVisual(); } }
    public static Rect CenterRect(GrayImage image) => new((1024 - image.Width) / 2, (768 - image.Height) / 2, image.Width, image.Height);
    public override void Render(DrawingContext context)
    {
        var k = Math.Min(Bounds.Width / 1024, Bounds.Height / 768); var canvas = new Rect((Bounds.Width - 1024 * k) / 2, (Bounds.Height - 768 * k) / 2, 1024 * k, 768 * k);
        context.FillRectangle(Brushes.Black, canvas);
        if (image == null || bitmap == null) return;
        var r = CenterRect(image); var target = new Rect(canvas.X + r.X * k, canvas.Y + r.Y * k, r.Width * k, r.Height * k);
        context.DrawImage(bitmap, new Rect(0, 0, image.Width, image.Height), target);
    }
}
public sealed class CropEditor : Control
{
    private GrayImage? image; private WriteableBitmap? bitmap; private Rect crop;
    private Point dragStart; private Rect startRect; private int kind; private bool dragging;
    public bool Active { get; set; }
    public Action<Rect>? Changed { get; set; }
    public Rect Crop { get => crop; set { crop = value; InvalidateVisual(); } }
    public GrayImage? Image { get => image; set { bitmap?.Dispose(); image = value; bitmap = value == null ? null : Images.Bitmap(value); crop = value == null ? default : new(0, 0, value.Width, value.Height); InvalidateVisual(); } }
    private Rect Frame
    {
        get { if (image == null) return default; var k = Math.Min(Math.Max(0, Bounds.Width - 24) / image.Width, Math.Max(0, Bounds.Height - 24) / image.Height); return new((Bounds.Width - image.Width * k) / 2, (Bounds.Height - image.Height * k) / 2, image.Width * k, image.Height * k); }
    }
    private Rect View(Rect r) { var f = Frame; var k = f.Width / image!.Width; return new(f.X + r.X * k, f.Y + r.Y * k, r.Width * k, r.Height * k); }
    private Point Pixel(Point point) { var f = Frame; var k = f.Width / image!.Width; return new(Math.Clamp((point.X - f.X) / k, 0, image.Width), Math.Clamp((point.Y - f.Y) / k, 0, image.Height)); }
    public override void Render(DrawingContext context)
    {
        context.FillRectangle(new SolidColorBrush(Color.Parse("#111315")), new Rect(Bounds.Size));
        if (image == null || bitmap == null) return; var f = Frame; context.DrawImage(bitmap, new Rect(0, 0, image.Width, image.Height), f);
        if (!Active) return;
        var r = View(crop); var shade = new SolidColorBrush(Color.FromArgb(150, 0, 0, 0));
        context.FillRectangle(shade, new Rect(f.X, f.Y, f.Width, Math.Max(0, r.Top - f.Top)));
        context.FillRectangle(shade, new Rect(f.X, r.Bottom, f.Width, Math.Max(0, f.Bottom - r.Bottom)));
        context.FillRectangle(shade, new Rect(f.X, r.Top, Math.Max(0, r.Left - f.Left), r.Height));
        context.FillRectangle(shade, new Rect(r.Right, r.Top, Math.Max(0, f.Right - r.Right), r.Height));
        context.DrawRectangle(null, new Pen(new SolidColorBrush(Color.Parse("#DF702B")), 1.5), r);
        foreach (var p in new[] { r.TopLeft, r.TopRight, r.BottomLeft, r.BottomRight }) context.FillRectangle(Brushes.White, new Rect(p.X - 4, p.Y - 4, 8, 8));
    }
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e); if (!Active || image == null || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || !Frame.Contains(e.GetPosition(this))) return;
        var point = e.GetPosition(this); dragStart = Pixel(point); startRect = crop; var r = View(crop); kind = 0;
        var corners = new[] { r.TopLeft, r.TopRight, r.BottomLeft, r.BottomRight };
        for (var i = 0; i < 4; i++) { var dx = point.X - corners[i].X; var dy = point.Y - corners[i].Y; if (dx * dx + dy * dy < 144) { kind = i + 2; break; } }
        if (kind == 0 && crop.Contains(dragStart)) kind = 1;
        if (kind == 0) crop = new(dragStart, new Size(1, 1));
        dragging = true; e.Pointer.Capture(this); InvalidateVisual(); e.Handled = true;
    }
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e); if (!dragging || !Active || image == null) return; var p = Pixel(e.GetPosition(this));
        if (kind == 1) crop = new(Math.Clamp(startRect.X + p.X - dragStart.X, 0, image.Width - startRect.Width), Math.Clamp(startRect.Y + p.Y - dragStart.Y, 0, image.Height - startRect.Height), startRect.Width, startRect.Height);
        else
        {
            var anchor = kind switch { 2 => startRect.BottomRight, 3 => startRect.BottomLeft, 4 => startRect.TopRight, 5 => startRect.TopLeft, _ => dragStart };
            var x = Math.Clamp(Math.Min(anchor.X, p.X), 0, image.Width - 1); var y = Math.Clamp(Math.Min(anchor.Y, p.Y), 0, image.Height - 1);
            crop = new(x, y, Math.Clamp(Math.Abs(p.X - anchor.X), 1, image.Width - x), Math.Clamp(Math.Abs(p.Y - anchor.Y), 1, image.Height - y));
        }
        Changed?.Invoke(crop); InvalidateVisual();
    }
    protected override void OnPointerReleased(PointerReleasedEventArgs e) { base.OnPointerReleased(e); if (!dragging) return; dragging = false; e.Pointer.Capture(null); Changed?.Invoke(crop); }
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e) { base.OnPointerCaptureLost(e); dragging = false; }
}

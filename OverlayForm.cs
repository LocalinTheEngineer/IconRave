using System.Drawing.Drawing2D;

namespace DesktopIconDropper;

// Bu, tüm ekranı kaplayan ama TAMAMEN ŞEFFAF ve tıklamalara KAPALI (tıklamalar
// altındaki masaüstüne geçer) özel bir pencere. Gerçek Windows simgeleri
// döndürülemediği için, bir simge "takla atarken" gerçek simgeyi geçici olarak
// ekran dışına gizleyip, onun yerine burada döndürerek çizdiğimiz bir kopya
// resim gösteriyoruz. Simge yere inince gerçek simge geri görünür oluyor.
internal class OverlayForm : Form
{
    private readonly Dictionary<int, (Bitmap Bitmap, float X, float Y, float RotationDeg)> _items = new();

    public OverlayForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.Magenta;
        TransparencyKey = Color.Magenta; // bu renk görünmez olacak
        DoubleBuffered = true;

        Bounds = SystemInformation.VirtualScreen; // tüm monitörleri kapsayacak şekilde
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= NativeMethods.WS_EX_TRANSPARENT | NativeMethods.WS_EX_NOACTIVATE;
            return cp;
        }
    }

    protected override bool ShowWithoutActivation => true;

    // Bir simgeyi (index) belirtilen ekran konumunda, verilen açıyla döndürerek çizmeye ekler/günceller
    public void SetIcon(int index, Bitmap bitmap, float screenX, float screenY, float rotationDeg)
    {
        _items[index] = (bitmap, screenX, screenY, rotationDeg);
    }

    // Bir simgeyi artık burada çizmeyi bırak (gerçek simge tekrar görünür olacak)
    public void RemoveIcon(int index)
    {
        _items.Remove(index);
    }

    public void RedrawAll() => Invalidate();

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;

        foreach (var (bitmap, x, y, rotationDeg) in _items.Values)
        {
            int w = bitmap.Width, h = bitmap.Height;
            var state = e.Graphics.Save();
            e.Graphics.TranslateTransform(x - Left + w / 2f, y - Top + h / 2f);
            e.Graphics.RotateTransform(rotationDeg);
            e.Graphics.DrawImage(bitmap, -w / 2f, -h / 2f, w, h);
            e.Graphics.Restore(state);
        }
    }
}

namespace DesktopIconDropper;

// Hem "otomatik düşme" (FallingIcon) hem de "elle sürükleyip fırlatma" (ThrownIcon)
// animasyonlarının ortak arayüzü. MainForm bu sayede ikisini de aynı listede,
// aynı kodla işleyebiliyor.
internal interface IIconAnimation
{
    int IconIndex { get; }
    float X { get; }
    float Y { get; }
    float RotationDegrees { get; }
    bool IsSettled { get; }
    void Update(float deltaSeconds);
}

namespace DesktopIconDropper;

// Kullanıcı bir simgeyi sürükleyip bıraktığında ya da ses vuruşuyla zıpladığında,
// simgenin fiziğini yönetir. Yerçekimi vardır, ekranın sol/sağ/üst kenarlarına
// çarpınca top gibi seker (enerji kaybederek), zemine (görev çubuğu) değince
// zıplamaya devam eder ve yeterince yavaşlayınca orada durur.
//
// Fizik değerleri (yerçekimi, sekme sertliği, dönme miktarı) kullanıcının
// ayarlar penceresinden değiştirdiği AppSettings'ten gelir.
internal class ThrownIcon : IIconAnimation
{
    public int IconIndex { get; }
    public float X { get; private set; }
    public float Y { get; private set; }
    public float RotationDegrees { get; private set; }
    public bool IsSettled { get; private set; }

    private float _velocityX;
    private float _velocityY;
    private float _spinVelocity;

    private readonly float _minX, _maxX, _minY, _floorY;
    private readonly AppSettings _settings;

    private const float FloorFriction = 0.75f;
    private const float SettleSpeed = 55f;

    public ThrownIcon(int iconIndex, float startX, float startY, float velocityX, float velocityY,
        float minX, float maxX, float minY, float floorY, Random rng, AppSettings settings)
    {
        IconIndex = iconIndex;
        X = startX;
        Y = startY;
        _velocityX = velocityX;
        _velocityY = velocityY;
        _minX = minX;
        _maxX = maxX;
        _minY = minY;
        _floorY = floorY;
        _settings = settings;

        _spinVelocity = (velocityX * 0.4f + (float)(rng.NextDouble() * 160 - 80)) * settings.SpinAmount;
    }

    // Simge hâlâ havadayken (örneğin yeni bir ses vuruşuyla) ekstra bir itme uygular.
    public void ApplyImpulse(float extraVelocityX, float extraVelocityY)
    {
        _velocityX += extraVelocityX;
        _velocityY += extraVelocityY;
    }

    public void Update(float deltaSeconds)
    {
        if (IsSettled) return;

        // Yükselirken normal yerçekimi, düşerken daha güçlü - böylece zıplama
        // çok yükseğe çıkmıyor ve iniş daha hızlı/akıcı oluyor (sese daha uyumlu).
        float gravity = _velocityY < 0
            ? _settings.Gravity
            : _settings.Gravity * _settings.FallGravityMultiplier;

        _velocityY += gravity * deltaSeconds;
        X += _velocityX * deltaSeconds;
        Y += _velocityY * deltaSeconds;
        RotationDegrees += _spinVelocity * deltaSeconds;

        float wallBounce = _settings.WallBounciness;

        // Sol / sağ duvar
        if (X < _minX)
        {
            X = _minX;
            _velocityX = -_velocityX * wallBounce;
            _spinVelocity = -_spinVelocity * 0.8f;
        }
        else if (X > _maxX)
        {
            X = _maxX;
            _velocityX = -_velocityX * wallBounce;
            _spinVelocity = -_spinVelocity * 0.8f;
        }

        // Üst duvar (ekranın tepesi) - sekmesin, sadece yukarı gitmeyi durdursun
        if (Y < _minY)
        {
            Y = _minY;
            _velocityY = Math.Max(_velocityY, 0f);
        }

        // Zemin (görev çubuğunun üstü)
        if (Y >= _floorY)
        {
            Y = _floorY;

            if (Math.Abs(_velocityY) > SettleSpeed || Math.Abs(_velocityX) > SettleSpeed)
            {
                _velocityY = -_velocityY * _settings.FloorBounciness;
                _velocityX *= FloorFriction;
                _spinVelocity *= 0.5f;
            }
            else
            {
                _velocityX = 0;
                _velocityY = 0;
                _spinVelocity = 0;
                IsSettled = true;
            }
        }
    }
}

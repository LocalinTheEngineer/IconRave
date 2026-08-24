namespace DesktopIconDropper;

// Kullanıcı bir simgeyi sürükleyip bıraktığında, fare hızına göre fırlatılan
// simgenin fiziğini yönetir. Yerçekimi vardır, ekranın sol/sağ/üst kenarlarına
// çarpınca top gibi seker (enerji kaybederek), zemine (görev çubuğu) değince
// zıplamaya devam eder ve yeterince yavaşlayınca orada durur.
internal class ThrownIcon : IIconAnimation
{
    public int IconIndex { get; }
    public float X { get; private set; }
    public float Y { get; private set; }
    public float RotationDegrees { get; private set; }
    public bool IsSettled { get; private set; }

    private float _velocityX;
    private float _velocityY;
    private float _spinVelocity; // saniyede kaç derece dönüyor

    private readonly float _minX, _maxX, _minY, _floorY;

    private const float Gravity = 1300f;
    private const float FallGravityMultiplier = 1.9f; // düşerken yerçekimi daha güçlü - hızlı iniş için
    private const float WallRestitution = 0.5f;   // duvara çarpınca hızın ne kadarı kalır (daha yumuşak)
    private const float FloorRestitution = 0.3f;   // zemine çarpınca hızın ne kadarı kalır (daha yumuşak)
    private const float FloorFriction = 0.75f;      // zemine her değişte yatay hız çarpanı
    private const float SettleSpeed = 55f;          // bu hızın altındaysa artık "durdu" sayılır

    public ThrownIcon(int iconIndex, float startX, float startY, float velocityX, float velocityY,
        float minX, float maxX, float minY, float floorY, Random rng)
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

        // Fırlatma hızına bağlı + biraz rastgele dönme (takla) - daha yumuşak/az agresif
        _spinVelocity = velocityX * 0.4f + (float)(rng.NextDouble() * 160 - 80);
    }

    // Simge hâlâ havadayken (örneğin yeni bir ses vuruşuyla) ekstra bir itme uygular.
    // Yeni bir animasyon oluşturmadan, mevcut hızın üzerine ekler - akıcı görünür.
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
        float gravity = _velocityY < 0 ? Gravity : Gravity * FallGravityMultiplier;
        _velocityY += gravity * deltaSeconds;

        // Hafif hava sürtünmesi: yere hiç değmeden köşelerde sonsuza kadar
        // sekip durmasını önler, er ya da geç yavaşlayıp yere inmesini sağlar.
        _velocityX *= (1f - 0.6f * deltaSeconds);

        X += _velocityX * deltaSeconds;
        Y += _velocityY * deltaSeconds;
        RotationDegrees += _spinVelocity * deltaSeconds;

        // Sol / sağ duvar
        if (X < _minX)
        {
            X = _minX;
            _velocityX = -_velocityX * WallRestitution;
            _spinVelocity = -_spinVelocity * 0.8f;
        }
        else if (X > _maxX)
        {
            X = _maxX;
            _velocityX = -_velocityX * WallRestitution;
            _spinVelocity = -_spinVelocity * 0.8f;
        }

        // Üst duvar (ekranın tepesi) - sekmesin, sadece yukarı gitmeyi durdursun
        // (aksi halde bazen tavan+kenar arasında hiç yere inmeden sonsuza kadar sekebiliyordu)
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
                _velocityY = -_velocityY * FloorRestitution;
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

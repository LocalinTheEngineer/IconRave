namespace DesktopIconDropper;

// Tek bir simgenin animasyonunu temsil eder. Süreç 4 aşamalı:
//   1) WaitingToStart : küçük bir gecikme (hepsi aynı anda değil, art arda düşsün diye)
//   2) Falling        : yerçekimiyle aşağı düşer, hafif yaprak gibi sallanır
//   3) Hopping        : yere değince BİR KERE, EKRANDA TAMAMEN RASTGELE bir noktaya
//                        doğru takla atarak fırlar (yay çizerek)
//   4) Settled        : fırlama bitince o yeni rastgele noktada sabitlenir
internal enum FallPhase { WaitingToStart, Falling, Hopping, Settled }

internal class FallingIcon : IIconAnimation
{
    public int IconIndex { get; }
    public float X { get; private set; }
    public float Y { get; private set; }

    public bool IsSettled => Phase == FallPhase.Settled;
    public FallPhase Phase { get; private set; } = FallPhase.WaitingToStart;
    public float RotationDegrees { get; private set; }

    private float VelocityY;
    private readonly float _targetY;   // ilk düşüşün ineceği zemin (görev çubuğunun üstü)
    private readonly float _baseX;

    private float _elapsedSeconds;
    private readonly float _startDelay;

    private readonly float _swayAmplitude;
    private readonly float _swayFrequency;
    private readonly float _swayPhase;

    private float _hopStartX, _hopStartY;
    private readonly float _hopTargetX;   // tamamen rastgele, ekranın herhangi bir yerinde
    private readonly float _hopLandingY;  // rastgele bir yükseklikte inecek (hepsi aynı çizgide değil)
    private float _hopElapsed;
    private readonly float _hopDuration;
    private float _hopVelocityX;
    private float _hopInitialVelocityY;
    private readonly float _totalSpin;

    private readonly AppSettings _settings;

    public FallingIcon(int iconIndex, float startX, float startY, float targetY,
        float minX, float maxX, float scatterBandHeight, Random rng, AppSettings settings)
    {
        IconIndex = iconIndex;
        _settings = settings;
        X = startX;
        _baseX = startX;
        Y = startY;
        _targetY = targetY;

        _startDelay = (float)rng.NextDouble() * 0.6f;

        _swayAmplitude = 6f + (float)rng.NextDouble() * 10f;
        _swayFrequency = 2.5f + (float)rng.NextDouble() * 2f;
        _swayPhase = (float)(rng.NextDouble() * Math.PI * 2);

        // Hedef X: orijinal konumla ilgisi yok, ekranın/masaüstünün herhangi bir yerinde
        _hopTargetX = minX + (float)rng.NextDouble() * Math.Max(1f, maxX - minX);

        // Hedef Y: hepsi görev çubuğunun hemen üstüne, gerçekten "yere" insin.
        // Çok hafif bir rastgelelik bırakıyoruz (aynı çizgide robotik durmasınlar diye)
        // ama havada asılı kalmayacak kadar küçük.
        _hopLandingY = targetY - (float)rng.NextDouble() * scatterBandHeight;

        _hopDuration = 0.35f + (float)rng.NextDouble() * 0.25f;

        int spins = rng.Next(1, 3);
        float direction = rng.NextDouble() < 0.5 ? -1f : 1f;
        _totalSpin = 360f * spins * direction * settings.SpinAmount;
    }

    public void Update(float deltaSeconds)
    {
        if (Phase == FallPhase.Settled) return;

        _elapsedSeconds += deltaSeconds;

        if (Phase == FallPhase.WaitingToStart)
        {
            if (_elapsedSeconds < _startDelay) return;
            Phase = FallPhase.Falling;
        }

        if (Phase == FallPhase.Falling)
        {
            VelocityY += _settings.Gravity * deltaSeconds;
            Y += VelocityY * deltaSeconds;

            float sway = _swayAmplitude * (float)Math.Sin(_elapsedSeconds * _swayFrequency + _swayPhase);
            X = _baseX + sway;

            if (Y >= _targetY)
            {
                Y = _targetY;

                _hopStartX = X;
                _hopStartY = Y;
                _hopVelocityX = (_hopTargetX - _hopStartX) / _hopDuration;

                // İniş yüksekliği kalkış yüksekliğinden farklı olabileceği için
                // (rastgele scatter), asimetrik bir yay hesaplıyoruz:
                float deltaY = _hopLandingY - _hopStartY;
                _hopInitialVelocityY = (deltaY - 0.5f * _settings.Gravity * _hopDuration * _hopDuration) / _hopDuration;

                _hopElapsed = 0f;
                Phase = FallPhase.Hopping;
            }
            return;
        }

        if (Phase == FallPhase.Hopping)
        {
            _hopElapsed += deltaSeconds;
            float t = Math.Min(_hopElapsed, _hopDuration);

            X = _hopStartX + _hopVelocityX * t;
            Y = _hopStartY + _hopInitialVelocityY * t + 0.5f * _settings.Gravity * t * t;
            RotationDegrees = _totalSpin * (t / _hopDuration);

            if (_hopElapsed >= _hopDuration)
            {
                X = _hopTargetX;
                Y = _hopLandingY;
                Phase = FallPhase.Settled;
            }
        }
    }
}

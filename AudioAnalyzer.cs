using NAudio.CoreAudioApi;
using NAudio.Dsp;
using NAudio.Wave;

namespace DesktopIconDropper;

// Bilgisayardan o an çalan SİSTEM SESİNİ (herhangi bir uygulamadan - Spotify,
// YouTube, oyun, ne olursa) "loopback capture" denen teknikle dinler.
// Sesi FFT (Fourier dönüşümü) ile analiz edip, düşük frekanstan (bas) yüksek
// frekansa (tiz) doğru sıralanmış bantlara ayırır - tıpkı bir müzik programındaki
// "equalizer" çubukları gibi. Her bandın o anki şiddetini 0-1 arası bir sayı
// olarak dışarıya açar.
internal class AudioAnalyzer : IDisposable
{
    public const int BandCount = 12; // soldan sağa: bas -> tiz

    public float[] BandLevels { get; } = new float[BandCount];
    public float OverallVolume { get; private set; }

    // İnsan sesinin (vokal) en çok yoğunlaştığı frekans aralığı - yaklaşık
    // 300Hz-3000Hz. Bant dizisi logaritmik olduğu için bu aralık kabaca
    // 4-8 numaralı bantlara denk geliyor. Enstrümanlardan tamamen izole
    // edemesek de (gerçek vokal ayrıştırma yapay zeka gerektirir), genel
    // ses seviyesinden çok daha "vokale duyarlı" bir sonuç veriyor.
    private const int VocalBandStart = 4;
    private const int VocalBandEnd = 8; // dahil

    public float VocalLevel
    {
        get
        {
            float sum = 0;
            int count = 0;
            for (int i = VocalBandStart; i <= VocalBandEnd && i < BandCount; i++)
            {
                sum += BandLevels[i];
                count++;
            }
            return count > 0 ? sum / count : 0f;
        }
    }

    private WasapiLoopbackCapture? _capture;

    private const int FftLength = 1024;
    private const int FftLengthLog2 = 10; // 2^10 = 1024
    private readonly float[] _fftBuffer = new float[FftLength];
    private readonly Complex[] _fftComplex = new Complex[FftLength];
    private int _fftPos;

    // Her bandın yakın zamandaki en yüksek değeri - buna göre otomatik kazanç
    // uygulayarak, tiz sesler de (doğal olarak bastan daha düşük enerjili olsa bile)
    // kendi içinde rahatça eşiği geçebiliyor.
    private readonly float[] _bandRunningMax = CreateInitialMax();

    // Genel ses seviyesi için de aynı otomatik kazanç mantığı - aksi halde ses
    // hızlıca "tavana" çıkıp orada tıkanıyor ve bir daha yükseliş algılanamıyordu.
    private float _overallRunningMax = 0.02f;

    private static float[] CreateInitialMax()
    {
        var arr = new float[BandCount];
        Array.Fill(arr, 0.02f);
        return arr;
    }

    public bool Start()
    {
        try
        {
            _capture = new WasapiLoopbackCapture();
            _capture.DataAvailable += OnDataAvailable;
            _capture.StartRecording();
            return true;
        }
        catch
        {
            // Bazı sistemlerde (sürücü, izin vb.) ses yakalama başarısız olabilir.
            // Bu durumda uygulamanın geri kalanı normal çalışmaya devam etsin.
            return false;
        }
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (_capture == null) return;

        int bytesPerSample = _capture.WaveFormat.BitsPerSample / 8;
        int channels = Math.Max(1, _capture.WaveFormat.Channels);
        int frameSize = bytesPerSample * channels;
        if (frameSize <= 0) return;

        int frameCount = e.BytesRecorded / frameSize;
        float sumSquares = 0f;

        for (int i = 0; i < frameCount; i++)
        {
            int byteOffset = i * frameSize;
            if (byteOffset + 4 > e.Buffer.Length) break;

            float sample = BitConverter.ToSingle(e.Buffer, byteOffset); // sol kanal
            sumSquares += sample * sample;

            _fftBuffer[_fftPos] = sample;
            _fftPos++;

            if (_fftPos >= FftLength)
            {
                ProcessFft();
                _fftPos = 0;
            }
        }

        if (frameCount > 0)
        {
            float rms = MathF.Sqrt(sumSquares / frameCount);

            // Otomatik kazanç: sesin kendi yakın geçmişteki en yüksek değerine göre
            // normalize ediyoruz - böylece şarkı boyunca sürekli duyarlı kalıyor,
            // sürekli yüksek sesli bir bölümde bile "tavana yapışıp" kalmıyor.
            if (rms > _overallRunningMax)
                _overallRunningMax = rms;
            else
                _overallRunningMax = Math.Max(0.004f, _overallRunningMax * 0.992f);

            float target = Math.Min(1f, rms / _overallRunningMax);

            // Çok az yumuşatma - vuruşun anında görünmesi için ham sese olabildiğince yakın takip
            OverallVolume = OverallVolume * 0.25f + target * 0.75f;
        }
    }

    private void ProcessFft()
    {
        for (int i = 0; i < FftLength; i++)
        {
            // Hann penceresi - FFT sonucunun daha temiz çıkması için
            float window = 0.5f - 0.5f * MathF.Cos(2 * MathF.PI * i / (FftLength - 1));
            _fftComplex[i].X = _fftBuffer[i] * window;
            _fftComplex[i].Y = 0;
        }

        FastFourierTransform.FFT(true, FftLengthLog2, _fftComplex);

        int usableBins = FftLength / 2;

        for (int band = 0; band < BandCount; band++)
        {
            // Bantları LOGARİTMİK olarak dağıtıyoruz - insan kulağı da frekansı
            // logaritmik algıladığı için (bas tarafı dar, tiz tarafı geniş bin aralığı kaplar)
            int startBin = Math.Max(1, (int)MathF.Pow(usableBins, (float)band / BandCount));
            int endBin = Math.Max(startBin + 1, (int)MathF.Pow(usableBins, (float)(band + 1) / BandCount));
            endBin = Math.Min(usableBins, endBin);

            float sum = 0;
            for (int bin = startBin; bin < endBin; bin++)
            {
                float re = _fftComplex[bin].X;
                float im = _fftComplex[bin].Y;
                sum += MathF.Sqrt(re * re + im * im);
            }

            float magnitude = sum / (endBin - startBin);

            // Otomatik kazanç: bu bandı, kendi yakın geçmişteki en yüksek değerine
            // göre normalize ediyoruz. Böylece tiz frekanslar bas kadar "sessiz"
            // kalmıyor - her bant kendi içinde eşit derecede duyarlı oluyor.
            if (magnitude > _bandRunningMax[band])
                _bandRunningMax[band] = magnitude;
            else
                _bandRunningMax[band] = Math.Max(0.005f, _bandRunningMax[band] * 0.985f);

            float normalized = Math.Min(1f, magnitude / _bandRunningMax[band]);
            // Hafif bir eğri - çok agresif değil, aksi halde her ses tetikleme yapıyor
            normalized = MathF.Pow(normalized, 0.8f);

            // Yumuşak geçiş (titrek değil, akıcı görünsün)
            BandLevels[band] = BandLevels[band] * 0.45f + normalized * 0.55f;
        }
    }

    public void Dispose()
    {
        try
        {
            _capture?.StopRecording();
            _capture?.Dispose();
        }
        catch { }
    }
}

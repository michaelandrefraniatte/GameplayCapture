using System;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using SharpDX.Direct2D1;
using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using SharpDX.Mathematics.Interop;
using SharpDX.MediaFoundation;
using SharpDX.Multimedia;
using System.Windows.Forms;
using System.Threading.Tasks;
namespace GameplayCapture
{
    public class GameplayCapture : INotifyPropertyChanged, IDisposable
    {
        // common
        private int _resized = 0;
        private System.Threading.Timer _frameRateTimer = null;

        // recording common
        private Lazy<SinkWriter> _sinkWriter = null;
        private long _startTime = 0;

        // video recording
        private ConcurrentQueue<VideoFrame> _videoFramesQueue = new ConcurrentQueue<VideoFrame>();
        private Lazy<DXGIDeviceManager> _devManager = new Lazy<DXGIDeviceManager>();
        private int _videoOutputIndex = 0;

        // sound recording
        private int _soundOutputIndex = 0;
        private int _microphoneOutputIndex = 0;
        private long _soundElapsedNs = 0;
        private long _microphoneElapsedNs = 0;
        private AudioCapture _soundCapture = null;
        private AudioCapture _microphoneCapture = null;
        private int _soundSamplesCount = 0;
        private int _soundGapsCount = 0;
        private int _microphoneSamplesCount = 0;
        private int _microphoneGapsCount = 0;

        // duplicating
        private Lazy<SharpDX.Direct2D1.Device> _2D1Device = new Lazy<SharpDX.Direct2D1.Device>();
        private Lazy<SharpDX.Direct2D1.DeviceContext> _backBufferDc = new Lazy<SharpDX.Direct2D1.DeviceContext>();
        private Lazy<SharpDX.Direct3D11.Device> _device = new Lazy<SharpDX.Direct3D11.Device>();
        private Lazy<OutputDuplication> _outputDuplication = new Lazy<OutputDuplication>();
        private Lazy<Output1> _output = new Lazy<Output1>();
        private Lazy<SwapChain1> _swapChain = new Lazy<SwapChain1>();

        // properties
        public event PropertyChangedEventHandler PropertyChanged = null;
        public event EventHandler<GameplayCaptureInformationEventArgs> InformationAvailable = null;
        private int frameRateNumerator = 0;
        private int frameRateDenominator = 0;
        private int bitrate;
        private int height;
        private int width;
        private double newheight;
        private double newwidth;
        private int heighthd;
        private int widthhd;
        public bool CapturingState, DuplicatingState, RecordingState, DrainRecordingState;
        private int sleeptime;

        public GameplayCapture(GameplayCaptureOptions options)
        {
            Options = options;
            Options.PropertyChanged += OnOptionsChanged;

            _devManager = new Lazy<DXGIDeviceManager>(CreateDeviceManager, true);
            _sinkWriter = new Lazy<SinkWriter>(CreateSinkWriter, true);
            _device = new Lazy<SharpDX.Direct3D11.Device>(CreateDevice, true);
            _output = new Lazy<Output1>(CreateOutput, true);
            _outputDuplication = new Lazy<OutputDuplication>(CreateOutputDuplication, true);

            _soundCapture = new AudioCapture();
            _soundCapture.RaiseNativeDataEvents = true;
            _soundCapture.RaiseDataEvents = false;
            _soundCapture.NativeDataReady += SoundCaptureDataReady;

            _microphoneCapture = new AudioCapture();
            _microphoneCapture.RaiseNativeDataEvents = true;
            _microphoneCapture.RaiseDataEvents = false;
            _microphoneCapture.NativeDataReady += MicrophoneCaptureDataReady;

            _swapChain = new Lazy<SwapChain1>(CreateSwapChain, true);
            _2D1Device = new Lazy<SharpDX.Direct2D1.Device>(Create2D1Device, true);
            _backBufferDc = new Lazy<SharpDX.Direct2D1.DeviceContext>(CreateBackBufferDc, true);
        }

        public GameplayCaptureOptions Options { get; }
        public bool IsUsingDirect3D11AwareEncoder { get; private set; }
        public bool IsUsingHardwareBasedEncoder { get; private set; }
        public bool IsUsingBuiltinEncoder { get; private set; }
        public string EncoderFriendlyName { get; private set; }
        public int SoundSamplesPerSecond { get; private set; }
        public int MicrophoneSamplesPerSecond { get; private set; }
        public IntPtr Hwnd { get; set; }

        public void StartDuplicating()
        {
            MediaManager.Startup();

            CapturingState = true;
            RecordingState = true;
            Initializing();
            width = Screen.PrimaryScreen.Bounds.Width;
            height = Screen.PrimaryScreen.Bounds.Height;
            newheight = Screen.PrimaryScreen.Bounds.Height;
            newwidth = (double)width / (double)height * newheight;
            heighthd = (int)newheight;
            widthhd = (int)newwidth;
            sleeptime = (int)(1000f / (double)Options.RecordingFrameRate);
            bitrate = (int)(Options.RecordingBitRate * 1000000f);
            Task.Run(() => DuplicationTask());
        }

        public void StopDuplicating()
        {
            CapturingState = false;

            while (RecordingState)
            {
                Thread.Sleep(1);
            }

            try
            {
                DrainRecordingVideoQueue();
            }
            catch { }

            try
            {
                _microphoneCapture.Stop();
            }
            catch { }

            try
            {
                _soundCapture.Stop();
            }
            catch { }

            try
            {
                _sinkWriter.Value.Finalize();
            }
            catch { }

            Initializing();

            MediaManager.Shutdown();
        }
        private void Initializing()
        {
            _soundSamplesCount = 0;
            _microphoneSamplesCount = 0;
            _sinkWriter = Reset(_sinkWriter, CreateSinkWriter);
            _devManager = Reset(_devManager, CreateDeviceManager);
            _startTime = 0;
            _soundElapsedNs = 0;
            _microphoneElapsedNs = 0;
            _videoOutputIndex = 0;
            _soundOutputIndex = 0;
            _microphoneOutputIndex = 0;

            ClearDuplicatedFrame();

            _output = Reset(_output, CreateOutput);
            _outputDuplication = Reset(_outputDuplication, CreateOutputDuplication);

            _2D1Device = Reset(_2D1Device, Create2D1Device);
            _backBufferDc = Reset(_backBufferDc, CreateBackBufferDc);
            _swapChain = Reset(_swapChain, CreateSwapChain);

            _device = Reset(_device, CreateDevice);

            _resized = 0;
            _startTime = 0;
            _videoOutputIndex = 0;
            _soundOutputIndex = 0;
            _microphoneOutputIndex = 0;
            _soundElapsedNs = 0;
            _microphoneElapsedNs = 0;
            _soundSamplesCount = 0;
            _soundGapsCount = 0;
            _microphoneSamplesCount = 0;
            _microphoneGapsCount = 0;
        }

        private void DuplicationTask()
        {
            while (CapturingState)
            {
                // handle host window resize
                if (Interlocked.CompareExchange(ref _resized, 0, 1) == 1)
                {
                    if (_swapChain.IsValueCreated)
                    {
                        if (_backBufferDc.IsValueCreated)
                        {
                            // release outstanding references to swapchain's back buffers
                            _backBufferDc = Reset(_backBufferDc, CreateBackBufferDc);
                        }

                        _swapChain.Value.ResizeBuffers(_swapChain.Value.Description1.BufferCount, widthhd, heighthd, _swapChain.Value.Description1.Format, _swapChain.Value.Description1.Flags);
                    }
                }

                SafeAcquireFrame();
                Thread.Sleep(sleeptime);
            }
            RecordingState = false;
        }

        private class VideoFrame
        {
            public Texture2D Texture;
            public long Time;
            public long Duration;
        }

        private void DrainRecordingVideoQueue()
        {
            while (_videoFramesQueue.TryDequeue(out VideoFrame frame))
            {
                WriteVideoSample(frame);
                Thread.Sleep(1);
            }
        }

        private void SafeAcquireFrame()
        {
            try
            {
                var od = _outputDuplication.Value;

                SharpDX.DXGI.Resource frame;
                OutputDuplicateFrameInformation frameInfo = new OutputDuplicateFrameInformation();

                od.AcquireNextFrame(100, out frameInfo, out frame);

                using (frame)
                {
                    if (_startTime == 0)
                    {
                        _startTime = Stopwatch.GetTimestamp();
                        _soundElapsedNs = 0;
                        _microphoneElapsedNs = 0;
                    }

                    var vf = new VideoFrame();
                    var ticks = Stopwatch.GetTimestamp() - _startTime;
                    var elapsedNs = 10000000 * ticks / Stopwatch.Frequency;
                    vf.Time = elapsedNs;
                    vf.Duration = 0;
                    vf.Texture = CreateTexture2D();
                    using (var res = frame.QueryInterface<SharpDX.Direct3D11.Resource>())
                    {
                        _device.Value.ImmediateContext.CopyResource(res, vf.Texture);
                    }

                    WriteVideoSample(vf);

                    od.ReleaseFrame();
                }
            }
            catch { }
        }

        private void ClearDuplicatedFrame()
        {
            try
            {
                if (!_backBufferDc.IsValueCreated)
                    return;

                _backBufferDc.Value.BeginDraw();
                _backBufferDc.Value.Clear(new RawColor4(0, 0, 0, 1));
                _backBufferDc.Value.EndDraw();
                _swapChain.Value.Present(1, 0);
            }
            catch { }
        }

        private void WriteVideoSample(VideoFrame frame)
        {
            using (frame.Texture)
            {
                using (var sample = MediaFactory.CreateSample())
                {
                    MediaFactory.CreateDXGISurfaceBuffer(typeof(Texture2D).GUID, frame.Texture, 0, new RawBool(true), out MediaBuffer buffer);
                    using (buffer)
                    using (var buffer2 = buffer.QueryInterface<Buffer2D>())
                    {
                        sample.SampleTime = frame.Time;
                        sample.SampleDuration = frame.Duration;

                        buffer.CurrentLength = buffer2.ContiguousLength;
                        sample.AddBuffer(buffer);
                        try
                        {
                            _sinkWriter.Value.WriteSample(_videoOutputIndex, sample);
                        }
                        catch { }
                    }
                }
            }
        }

        private void MicrophoneCaptureDataReady(object sender, AudioCaptureNativeDataEventArgs e)
        {
            WriteAudioSample(e.Data, e.Size, _microphoneOutputIndex, ref _microphoneGapsCount, ref _microphoneSamplesCount, ref _microphoneElapsedNs);
            e.Handled = true;
        }

        private void SoundCaptureDataReady(object sender, AudioCaptureNativeDataEventArgs e)
        {
            WriteAudioSample(e.Data, e.Size, _soundOutputIndex, ref _soundGapsCount, ref _soundSamplesCount, ref _soundElapsedNs);
            e.Handled = true;
        }

        private void WriteAudioSample(IntPtr data, int size, int streamIndex, ref int gaps, ref int samplesCount, ref long elapsed)
        {
            if (size == 0)
            {
                var ticks = Stopwatch.GetTimestamp() - _startTime;
                var elapsedNs = 10000000 * ticks / Stopwatch.Frequency;
                
                try
                {
                    _sinkWriter.Value.SendStreamTick(streamIndex, elapsedNs);
                }
                catch { }

                gaps++;
                return;
            }

            using (var buffer = MediaFactory.CreateMemoryBuffer(size))
            {
                var ptr = buffer.Lock(out int max, out int current);
                CopyMemory(ptr, data, size);
                buffer.CurrentLength = size;
                buffer.Unlock();

                using (var sample = MediaFactory.CreateSample())
                {
                    var ticks = Stopwatch.GetTimestamp() - _startTime;
                    var elapsedNs = (10000000 * ticks) / Stopwatch.Frequency;
                    sample.SampleTime = elapsed;
                    sample.SampleDuration = elapsedNs - elapsed;
                    elapsed = elapsedNs;
                    sample.AddBuffer(buffer);
                    try
                    {
                        _sinkWriter.Value.WriteSample(streamIndex, sample);
                        samplesCount++;
                    }
                    catch { }
                }
            }
        }

        public void Dispose()
        {
            _frameRateTimer = Dispose(_frameRateTimer);
            if (CapturingState)
                StopDuplicating();
        }

        private void OnInformationAvailable(string text)
        {
            if (text == null)
                return;

            InformationAvailable?.Invoke(this, new GameplayCaptureInformationEventArgs(text));
        }

        private void OnPropertyChanged(string name, string traceValue)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        // we need to handle some important properties change
        private void OnOptionsChanged(object sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(GameplayCaptureOptions.Adapter):
                case nameof(GameplayCaptureOptions.Output):
                    if (CapturingState)
                        StopDuplicating();
                    _output = new Lazy<Output1>(CreateOutput, true);
                    break;
            }
        }

        private SharpDX.Direct2D1.Device Create2D1Device()
        {
            using (var fac = new SharpDX.Direct2D1.Factory1())
            {
                using (var dxDev = _device.Value.QueryInterface<SharpDX.DXGI.Device>())
                {
                    return new SharpDX.Direct2D1.Device(fac, dxDev);
                }
            }
        }

        private SharpDX.Direct2D1.DeviceContext CreateBackBufferDc()
        {
            try
            {
                var dc = new SharpDX.Direct2D1.DeviceContext(_2D1Device.Value, DeviceContextOptions.EnableMultithreadedOptimizations);
                using (var backBufferSurface = _swapChain.Value.GetBackBuffer<Surface>(0))
                {
                    using (var bmp = new Bitmap1(dc, backBufferSurface))
                    {
                        dc.Target = bmp;
                        return dc;
                    }
                }
            }
            catch { return null; }
        }

        private SharpDX.Direct3D11.Device CreateDevice()
        {
            using (var fac = new SharpDX.DXGI.Factory1())
            {
                using (var adapter = Options.GetAdapter())
                {
                    if (adapter == null)
                        return null;

                    var flags = DeviceCreationFlags.BgraSupport; // for D2D cooperation
                    flags |= DeviceCreationFlags.VideoSupport;
                    var device = new SharpDX.Direct3D11.Device(adapter, flags);
                    using (var mt = device.QueryInterface<DeviceMultithread>())
                    {
                        mt.SetMultithreadProtected(new RawBool(true));
                    }
                    return device;
                }
            }
        }

        private SwapChain1 CreateSwapChain()
        {
            // https://msdn.microsoft.com/en-us/library/windows/desktop/hh780339.aspx
            using (var fac = new SharpDX.Direct2D1.Factory1())
            {
                using (var dxFac = new SharpDX.DXGI.Factory2(false))
                {
                    using (var dxDev = _device.Value.QueryInterface<SharpDX.DXGI.Device1>())
                    {
                        try
                        {
                            var desc = new SwapChainDescription1();
                            desc.SampleDescription = new SampleDescription(Options.RecordingFrameCount, Options.RecordingFrameQuality);
                            desc.SwapEffect = SwapEffect.FlipSequential;
                            desc.Scaling = Scaling.None;
                            desc.Usage = Usage.RenderTargetOutput;
                            desc.Format = Format.B8G8R8A8_UNorm;
                            desc.BufferCount = 2;
                            var sw = new SwapChain1(dxFac, dxDev, Hwnd, ref desc);
                            dxDev.MaximumFrameLatency = Options.RecordingFrameBuffer;
                            return sw;
                        }
                        catch { return null; }
                    }
                }
            }
        }

        private Output1 CreateOutput() => Options.GetOutput();

        private Texture2D CreateTexture2D()
        {
            // this is meant for GPU to GPU operation for maximum performance, so CPU has no access to this
            var desc = new Texture2DDescription()
            {
                CpuAccessFlags = CpuAccessFlags.None,
                BindFlags = BindFlags.RenderTarget,
                Format = Format.B8G8R8A8_UNorm,
                Width = widthhd,
                Height = heighthd,
                OptionFlags = ResourceOptionFlags.None,
                MipLevels = 1,
                ArraySize = 1,
                SampleDescription = { Count = 1, Quality = 0 },
                Usage = ResourceUsage.Default
            };

            return new Texture2D(_device.Value, desc);
        }

        private OutputDuplication CreateOutputDuplication()
        {
            try
            {
                return _output.Value?.DuplicateOutput(_device.Value);
            }
            catch
            {
                return null;
            }
        }

        private DXGIDeviceManager CreateDeviceManager()
        {
            var devManager = new DXGIDeviceManager();
            MediaFactory.CreateDXGIDeviceManager(out int token, devManager);
            devManager.ResetDevice(_device.Value);
            return devManager;
        }

        private SinkWriter CreateSinkWriter()
        {
            string RecordFileName = "Capture_" + DateTime.Now.ToString().Replace("/", "_").Replace(":", "_").Replace(" ", "_") + ".mp4";

            SinkWriter writer;
            using (var ma = new MediaAttributes())
            {
                // note this doesn't mean you *will* have a hardware transform. Intel Media SDK sometimes is not happy with configuration for HDCP issues.
                ma.Set(SinkWriterAttributeKeys.ReadwriteEnableHardwareTransforms, 0);
                ma.Set(SinkWriterAttributeKeys.D3DManager, _devManager.Value);

                // by default, the sink writer's WriteSample method limits the data rate by blocking the calling thread.
                // this prevents the application from delivering samples too quickly.
                // to disable this behavior, set the attribute to 1
                ma.Set(SinkWriterAttributeKeys.DisableThrottling, 0);
                ma.Set(SinkWriterAttributeKeys.LowLatency, false);

                writer = MediaFactory.CreateSinkWriterFromURL(RecordFileName, null, ma);
            }

            frameRateNumerator = 0;
            frameRateDenominator = 0;
            MediaFactory.AverageTimePerFrameToFrameRate((long)(10000000 / Options.RecordingFrameRate), out frameRateNumerator, out frameRateDenominator);

            using (var videoStream = new MediaType())
            {
                videoStream.Set(MediaTypeAttributeKeys.AvgBitrate, bitrate);
                videoStream.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Video);
                videoStream.Set(MediaTypeAttributeKeys.Subtype, VideoFormatGuids.FromFourCC(new FourCC("H264")));
                videoStream.Set(MediaTypeAttributeKeys.InterlaceMode, (int)VideoInterlaceMode.Progressive);
                videoStream.Set(MediaTypeAttributeKeys.FrameRate, ((long)frameRateNumerator << 32) | (uint)frameRateDenominator);
                videoStream.Set(MediaTypeAttributeKeys.FrameSize, ((long)widthhd << 32) | (uint)heighthd);
                writer.AddStream(videoStream, out _videoOutputIndex);
            }

            using (var video = new MediaType())
            {
                video.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Video);
                video.Set(MediaTypeAttributeKeys.Subtype, VideoFormatGuids.Rgb32);
                video.Set(MediaTypeAttributeKeys.FrameSize, ((long)widthhd << 32) | (uint)heighthd);
                video.Set(MediaTypeAttributeKeys.FrameRate, ((long)frameRateNumerator << 32) | (uint)frameRateDenominator);
                writer.SetInputMediaType(_videoOutputIndex, video, null);
            }

            if (Options.CaptureSound)
            {
                var format = _soundCapture.GetFormat();
                SoundSamplesPerSecond = format.SamplesPerSecond;
                OnInformationAvailable("Sound Samples Per Second : " + SoundSamplesPerSecond);
                OnPropertyChanged(nameof(SoundSamplesPerSecond), SoundSamplesPerSecond.ToString());

                using (var audioStream = new MediaType())
                {
                    audioStream.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Audio);
                    audioStream.Set(MediaTypeAttributeKeys.Subtype, AudioFormatGuids.Aac);
                    audioStream.Set(MediaTypeAttributeKeys.AudioNumChannels, format.ChannelsCount);
                    audioStream.Set(MediaTypeAttributeKeys.AudioSamplesPerSecond, format.SamplesPerSecond);
                    audioStream.Set(MediaTypeAttributeKeys.AudioBitsPerSample, 16); // loopback forces 16
                    writer.AddStream(audioStream, out _soundOutputIndex);
                }

                using (var audio = new MediaType())
                {
                    audio.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Audio);
                    audio.Set(MediaTypeAttributeKeys.Subtype, AudioFormatGuids.Pcm);
                    audio.Set(MediaTypeAttributeKeys.AudioNumChannels, format.ChannelsCount);
                    audio.Set(MediaTypeAttributeKeys.AudioSamplesPerSecond, format.SamplesPerSecond);
                    audio.Set(MediaTypeAttributeKeys.AudioBitsPerSample, 16); // loopback forces 16
                    writer.SetInputMediaType(_soundOutputIndex, audio, null);
                }
            }

            if (Options.CaptureMicrophone)
            {
                var format = _microphoneCapture.GetFormat();
                MicrophoneSamplesPerSecond = format.SamplesPerSecond;
                OnInformationAvailable("Microphone Samples Per Second : " + MicrophoneSamplesPerSecond);
                OnPropertyChanged(nameof(MicrophoneSamplesPerSecond), MicrophoneSamplesPerSecond.ToString());

                using (var audioStream = new MediaType())
                {
                    audioStream.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Audio);
                    audioStream.Set(MediaTypeAttributeKeys.Subtype, AudioFormatGuids.Aac);
                    audioStream.Set(MediaTypeAttributeKeys.AudioNumChannels, format.ChannelsCount);
                    audioStream.Set(MediaTypeAttributeKeys.AudioSamplesPerSecond, format.SamplesPerSecond);
                    audioStream.Set(MediaTypeAttributeKeys.AudioBitsPerSample, 16); // loopback forces 16
                    writer.AddStream(audioStream, out _microphoneOutputIndex);
                }

                using (var audio = new MediaType())
                {
                    audio.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Audio);
                    audio.Set(MediaTypeAttributeKeys.Subtype, AudioFormatGuids.Pcm);
                    audio.Set(MediaTypeAttributeKeys.AudioNumChannels, format.ChannelsCount);
                    audio.Set(MediaTypeAttributeKeys.AudioSamplesPerSecond, format.SamplesPerSecond);
                    audio.Set(MediaTypeAttributeKeys.AudioBitsPerSample, 16); // loopback forces 16
                    writer.SetInputMediaType(_microphoneOutputIndex, audio, null);
                }
            }

            // gather some information
            IsUsingBuiltinEncoder = H264Encoder.IsBuiltinEncoder(writer, _videoOutputIndex);
            IsUsingDirect3D11AwareEncoder = H264Encoder.IsDirect3D11AwareEncoder(writer, _videoOutputIndex);
            IsUsingHardwareBasedEncoder = H264Encoder.IsHardwareBasedEncoder(writer, _videoOutputIndex);
            EncoderFriendlyName = H264Encoder.GetEncoderFriendlyName(writer, _videoOutputIndex);
            OnInformationAvailable("Encoder Type : " + (IsUsingHardwareBasedEncoder ? "Hardware" : "Software"));
            OnInformationAvailable("Direct3D 11 Aware Encoder Type : " + IsUsingDirect3D11AwareEncoder);
            OnInformationAvailable("Encoder Name : " + EncoderFriendlyName);
            OnPropertyChanged(nameof(IsUsingBuiltinEncoder), IsUsingBuiltinEncoder.ToString());
            OnPropertyChanged(nameof(IsUsingDirect3D11AwareEncoder), IsUsingDirect3D11AwareEncoder.ToString());
            OnPropertyChanged(nameof(IsUsingHardwareBasedEncoder), IsUsingHardwareBasedEncoder.ToString());

            writer.BeginWriting();

            if (Options.CaptureMicrophone)
            {
                var ad = Options.GetMicrophoneDevice() ?? AudioCapture.GetMicrophoneDevice();
                if (ad != null)
                {
                    _microphoneCapture.Start(ad);
                }
            }

            if (Options.CaptureSound)
            {
                var ad = Options.GetSoundDevice() ?? AudioCapture.GetSpeakersDevice();
                if (ad != null)
                {
                    _soundCapture.Start(ad);
                }
            }
            return writer;
        }

        // a multi thread version is:
        // disposable = Interlocked.Exchange(ref disposable, null)?.Dispose();
        private static T Dispose<T>(T disposable) where T : IDisposable
        {
            disposable?.Dispose();
            return default(T);
        }

        private static Lazy<T> Reset<T>(Lazy<T> disposable, Func<T> valueFactory) where T : IDisposable
        {
            if (disposable != null && disposable.IsValueCreated)
            {
                disposable.Value?.Dispose();
            }
            return new Lazy<T>(valueFactory, true);
        }

        [DllImport("kernel32")]
        private static extern void CopyMemory(IntPtr Destination, IntPtr Source, int Length);
    }
}
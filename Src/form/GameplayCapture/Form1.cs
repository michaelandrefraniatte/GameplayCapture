using System;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.IO;
using System.Threading;
using System.Runtime.InteropServices;
namespace GameplayCapture
{
    public partial class Form1 : Form
    {
        [DllImport("user32.dll")]
        public static extern bool GetAsyncKeyState(Keys vKey);
        private WinGameplayCaptureOptions _options;
        private GameplayCapture _duplicator;
        private static bool Getstate;
        public static int[] wd = { 2, 2 };
        public static int[] wu = { 2, 2 };
        static void valchanged(int n, bool val)
        {
            if (val)
            {
                if (wd[n] <= 1)
                {
                    wd[n] = wd[n] + 1;
                }
                wu[n] = 0;
            }
            else
            {
                if (wu[n] <= 1)
                {
                    wu[n] = wu[n] + 1;
                }
                wd[n] = 0;
            }
        }
        public Form1()
        {
            InitializeComponent();
        }
        private void Form1_Shown(object sender, EventArgs e)
        {
            MinimumSize = Size;
            _options = new WinGameplayCaptureOptions();
            propertyGridMain.SelectedObject = _options;
            _duplicator = new GameplayCapture(_options);
            Task.Run(() => Start());
        }
        public void Start()
        {
            for (; ; )
            {
                valchanged(0, GetAsyncKeyState(Keys.NumPad0));
                valchanged(1, GetAsyncKeyState(Keys.Decimal));
                if (wd[1] == 1 & !Getstate)
                {
                    try 
                    { 
                        _duplicator.StartDuplicating();
                    }
                    catch (Exception exception)
                    {
                        MessageBox.Show(exception.ToString());
                    }
                    Getstate = true;
                }
                else
                {
                    if (wd[0] == 1 & Getstate)
                    {
                        if (_duplicator.CapturingState)
                            _duplicator.StopDuplicating();
                        Getstate = false;
                    }
                }
                Thread.Sleep(70);
            }
        }
        private void Form1_FormClosed(object sender, FormClosedEventArgs e)
        {
            if (_duplicator.CapturingState)
                _duplicator.StopDuplicating();
            using (StreamWriter createdfile = new StreamWriter("tempsave"))
            {
                createdfile.WriteLine(_duplicator.Options.RecordingFrameCount.ToString());
                createdfile.WriteLine(_duplicator.Options.RecordingFrameBuffer.ToString());
                createdfile.WriteLine(_duplicator.Options.RecordingFrameQuality.ToString());
                createdfile.WriteLine(_duplicator.Options.RecordingBitRate.ToString());
                createdfile.WriteLine(_duplicator.Options.RecordingFrameRate.ToString());
                createdfile.WriteLine(_duplicator.Options.CaptureSound);
                createdfile.WriteLine(_duplicator.Options.CaptureMicrophone);
            }
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _duplicator?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
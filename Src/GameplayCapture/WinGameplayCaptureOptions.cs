using System;
using System.ComponentModel;
using System.Drawing.Design;
using System.Windows.Forms;
using System.Windows.Forms.Design;
namespace GameplayCapture
{
    public class WinGameplayCaptureOptions : GameplayCaptureOptions
    {
        [Editor(typeof(OutputEditor), typeof(UITypeEditor))]
        public override string Output { get => base.Output; set => base.Output = value; }

        [Editor(typeof(AdapterEditor), typeof(UITypeEditor))]
        public override string Adapter { get => base.Adapter; set => base.Adapter = value; }

        [Editor(typeof(RenderAudioDeviceEditor), typeof(UITypeEditor))]
        public override string SoundDevice { get => base.SoundDevice; set => base.SoundDevice = value; }

        [Editor(typeof(CaptureAudioDeviceEditor), typeof(UITypeEditor))]
        public override string MicrophoneDevice { get => base.MicrophoneDevice; set => base.MicrophoneDevice = value; }

        private class OutputEditor : UITypeEditor
        {
            public override UITypeEditorEditStyle GetEditStyle(ITypeDescriptorContext context) => UITypeEditorEditStyle.Modal;

            public override object EditValue(ITypeDescriptorContext context, IServiceProvider provider, object value)
            {
                var editorService = (IWindowsFormsEditorService)provider.GetService(typeof(IWindowsFormsEditorService));
                if (editorService == null)
                    return base.EditValue(context, provider, value);

                var options = context.Instance as GameplayCaptureOptions;
                if (options == null)
                    return base.EditValue(context, provider, value);

                var adapter = options.GetAdapter();
                if (adapter == null)
                    return base.EditValue(context, provider, value);

                var form = new ChooseOutput(adapter, value as string);
                if (editorService.ShowDialog(form) == DialogResult.OK)
                    return form.DeviceName;

                return base.EditValue(context, provider, value);
            }
        }

        private class AdapterEditor : UITypeEditor
        {
            public override UITypeEditorEditStyle GetEditStyle(ITypeDescriptorContext context) => UITypeEditorEditStyle.Modal;

            public override object EditValue(ITypeDescriptorContext context, IServiceProvider provider, object value)
            {
                var editorService = (IWindowsFormsEditorService)provider.GetService(typeof(IWindowsFormsEditorService));
                if (editorService == null)
                    return base.EditValue(context, provider, value);

                var form = new ChooseAdapter(value as string);
                if (editorService.ShowDialog(form) == DialogResult.OK)
                    return form.Adapter.Description1.Description;

                return base.EditValue(context, provider, value);
            }
        }

        private class RenderAudioDeviceEditor : UITypeEditor
        {
            public override UITypeEditorEditStyle GetEditStyle(ITypeDescriptorContext context) => UITypeEditorEditStyle.Modal;

            public override object EditValue(ITypeDescriptorContext context, IServiceProvider provider, object value)
            {
                var editorService = (IWindowsFormsEditorService)provider.GetService(typeof(IWindowsFormsEditorService));
                if (editorService == null)
                    return base.EditValue(context, provider, value);

                var form = new ChooseAudioDevice(value as string, AudioCapture.DataFlow.Render);
                if (editorService.ShowDialog(form) == DialogResult.OK)
                    return form.Device.FriendlyName;

                return base.EditValue(context, provider, value);
            }
        }

        private class CaptureAudioDeviceEditor : UITypeEditor
        {
            public override UITypeEditorEditStyle GetEditStyle(ITypeDescriptorContext context) => UITypeEditorEditStyle.Modal;

            public override object EditValue(ITypeDescriptorContext context, IServiceProvider provider, object value)
            {
                var editorService = (IWindowsFormsEditorService)provider.GetService(typeof(IWindowsFormsEditorService));
                if (editorService == null)
                    return base.EditValue(context, provider, value);

                var form = new ChooseAudioDevice(value as string, AudioCapture.DataFlow.Capture);
                if (editorService.ShowDialog(form) == DialogResult.OK)
                    return form.Device.FriendlyName;

                return base.EditValue(context, provider, value);
            }
        }
    }
}
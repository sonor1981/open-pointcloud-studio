using System;
using System.Collections.Generic;
using System.Reflection;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using ScanQa.Revit.UI;

namespace ScanQa.Revit
{
    /// <summary>Runs queued work in a valid Revit API context (the QA window is modeless).</summary>
    internal sealed class RevitActionQueue : IExternalEventHandler
    {
        private readonly Queue<Action<UIApplication>> _queue = new Queue<Action<UIApplication>>();
        private readonly ExternalEvent _event;

        public RevitActionQueue()
        {
            _event = ExternalEvent.Create(this);
        }

        public void Post(Action<UIApplication> action)
        {
            lock (_queue) _queue.Enqueue(action);
            _event.Raise();
        }

        public void Execute(UIApplication app)
        {
            while (true)
            {
                Action<UIApplication> next;
                lock (_queue)
                {
                    if (_queue.Count == 0) return;
                    next = _queue.Dequeue();
                }
                try
                {
                    next(app);
                }
                catch (Exception ex)
                {
                    TaskDialog.Show("Scan-to-BIM QA", ex.Message);
                }
            }
        }

        public string GetName() => "Scan-to-BIM QA";
    }

    public sealed class App : IExternalApplication
    {
        internal static QaDisplayServer Display { get; private set; }
        internal static RevitActionQueue Actions { get; private set; }
        internal static QaWindow Window { get; set; }

        public Result OnStartup(UIControlledApplication application)
        {
            const string tab = "Scan QA";
            try { application.CreateRibbonTab(tab); }
            catch (Exception) { /* tab exists */ }
            var panel = application.CreateRibbonPanel(tab, "Scan-to-BIM");
            var button = new PushButtonData("ScanQaOpen", "Scan-to-BIM\nQA", Assembly.GetExecutingAssembly().Location,
                typeof(OpenQaWindowCommand).FullName)
            {
                ToolTip = "Check pipes, ducts and fittings against the linked point cloud inside the section box of the active 3D view.",
            };
            panel.AddItem(button);

            Display = new QaDisplayServer();
            Actions = new RevitActionQueue();
            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            try
            {
                Window?.Close();
                Display?.Unregister();
            }
            catch (Exception)
            {
                // Shutting down; nothing to report.
            }
            return Result.Succeeded;
        }
    }

    [Transaction(TransactionMode.Manual)]
    public sealed class OpenQaWindowCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                App.Display.Register();
                if (App.Window == null || App.Window.IsDisposed)
                {
                    App.Window = new QaWindow(App.Actions, App.Display);
                    App.Window.Show(new RevitWindowOwner(commandData.Application.MainWindowHandle));
                }
                else
                {
                    // A minimized owned form does not come back with Activate() alone.
                    if (App.Window.WindowState == System.Windows.Forms.FormWindowState.Minimized)
                        App.Window.WindowState = System.Windows.Forms.FormWindowState.Normal;
                    App.Window.Show();
                    App.Window.Activate();
                }
                App.Window.RefreshInputs(commandData.Application);
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    internal sealed class RevitWindowOwner : System.Windows.Forms.IWin32Window
    {
        public RevitWindowOwner(IntPtr handle) { Handle = handle; }
        public IntPtr Handle { get; }
    }
}

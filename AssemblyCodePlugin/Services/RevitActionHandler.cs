using System;
using Autodesk.Revit.UI;

namespace AssemblyCodePlugin.Services
{
    public class RevitActionHandler : IExternalEventHandler
    {
        public Action<UIApplication> ActionToExecute { get; set; }

        public void Execute(UIApplication app)
        {
            ActionToExecute?.Invoke(app);
        }

        public string GetName() => "AssemblyCodeRevitActionHandler";
    }
}

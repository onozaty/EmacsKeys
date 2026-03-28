using System;
using System.ComponentModel.Composition;
using System.Windows.Input;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;

namespace Microsoft.VisualStudio.Editor.EmacsEmulation
{
    /// <summary>
    /// Provides a KeyProcessor for non-PrimaryDocument interactive views to handle
    /// the Enter key, which the Emacs keybinding scheme maps to BreakLine.
    /// Views without IVsTextView adapters (e.g., Copilot Chat embedded views) cannot use
    /// the IOleCommandTarget-based InteractiveRoleWorkAroundFilter, so this KeyProcessor
    /// intercepts Enter at the WPF level and posts the standard RETURN command through
    /// VS's command system instead.
    /// </summary>
    [Export(typeof(IKeyProcessorProvider))]
    [ContentType("any")]
    [TextViewRole(PredefinedTextViewRoles.Interactive)]
    [Name("Emacs Interactive Role Key Processor")]
    [Order(Before = "default")]
    internal class InteractiveRoleKeyProcessorProvider : IKeyProcessorProvider
    {
        [Import]
        EmacsCommandsManager Manager { get; set; }

        [Import(typeof(SVsServiceProvider))]
        System.IServiceProvider ServiceProvider { get; set; }

        public KeyProcessor GetAssociatedProcessor(IWpfTextView wpfTextView)
        {
            return wpfTextView.Properties.GetOrCreateSingletonProperty(
                typeof(InteractiveRoleKeyProcessor),
                () => new InteractiveRoleKeyProcessor(wpfTextView, Manager, ServiceProvider));
        }
    }

    /// <summary>
    /// KeyProcessor that handles the Enter key for non-PrimaryDocument interactive views.
    /// 
    /// When the Emacs keybinding scheme is active, Enter is mapped to EmacsCommandID.BreakLine.
    /// In PrimaryDocument views, the full Emacs command filter chain handles BreakLine.
    /// In other views (e.g., Copilot Chat), there is no handler for BreakLine, so VS consumes
    /// the keystroke with no effect. This processor intercepts Enter before VS command routing
    /// and posts the standard VSStd2KCmdID.RETURN command through IVsUIShell.PostExecCommand,
    /// which routes it through VS's normal command dispatch to the active window.
    /// </summary>
    internal class InteractiveRoleKeyProcessor : KeyProcessor
    {
        private readonly IWpfTextView _view;
        private readonly EmacsCommandsManager _manager;
        private readonly System.IServiceProvider _serviceProvider;

        public InteractiveRoleKeyProcessor(
            IWpfTextView view,
            EmacsCommandsManager manager,
            System.IServiceProvider serviceProvider)
        {
            _view = view;
            _manager = manager;
            _serviceProvider = serviceProvider;
        }

        public override void PreviewKeyDown(KeyEventArgs args)
        {
            if (!_manager.IsEnabled)
                return;

            // PrimaryDocument views use the full Emacs command filter chain (CommandRouter + EmacsCommandsFilter)
            if (_view.Roles.Contains(PredefinedTextViewRoles.PrimaryDocument))
                return;

            if (args.Key == Key.Return)
            {
                // Mark as handled to prevent VS from converting Enter to the
                // Emacs BreakLine command (which has no handler in non-PrimaryDocument views)
                args.Handled = true;

                // Post the standard RETURN command through VS's command system.
                // This routes through the active window's command chain, allowing
                // Copilot Chat, Interactive Window, etc. to handle it normally.
                PostReturnCommand();
            }
        }


        private void PostReturnCommand()
        {
            try
            {
                var shell = _serviceProvider.GetService(typeof(SVsUIShell)) as IVsUIShell;
                if (shell != null)
                {
                    var cmdGroup = typeof(VSConstants.VSStd2KCmdID).GUID;
                    shell.PostExecCommand(cmdGroup, (uint)VSConstants.VSStd2KCmdID.RETURN, 0, null);
                }
            }
            catch
            {
            }
        }
    }
}

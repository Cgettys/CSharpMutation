//------------------------------------------------------------------------------
// <copyright file="MutationTesting.cs" company="Company">
//     Copyright (c) Company.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.ComponentModel.Design;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using CSharpMutation;
using EnvDTE;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.VisualStudio.LanguageServices;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.ComponentModelHost;
using Project = Microsoft.CodeAnalysis.Project;

namespace CSharpMutation_VSIX
{
    /// <summary>
    /// Command handler
    /// </summary>
    internal sealed class MutationTesting
    {
        /// <summary>
        /// Command ID.
        /// </summary>
        public const int CommandId = 0x0100;

        /// <summary>
        /// Command menu group (command set GUID).
        /// </summary>
        public static readonly Guid CommandSet = new Guid("df28c03d-edfe-4b60-aff9-8aa615772428");

        /// <summary>
        /// VS Package that provides this command, not null.
        /// </summary>
        private readonly Package package;
        private ErrorListProvider errorProvider;
        private MenuCommand menuItem;

        /// <summary>
        /// Initializes a new instance of the <see cref="MutationTesting"/> class.
        /// Adds our command handlers for menu (commands must exist in the command table file)
        /// </summary>
        /// <param name="package">Owner package, not null.</param>
        private MutationTesting(Package package)
        {
            if (package == null)
            {
                throw new ArgumentNullException("package");
            }

            this.package = package;

            OleMenuCommandService commandService = this.ServiceProvider.GetService(typeof(IMenuCommandService)) as OleMenuCommandService;
            if (commandService != null)
            {
                var menuCommandID = new CommandID(CommandSet, CommandId);
                menuItem = new MenuCommand(this.MenuItemCallback, menuCommandID);
                commandService.AddCommand(menuItem);
                errorProvider = new ErrorListProvider(ServiceProvider);

            }

            var componentModel = (IComponentModel)this.ServiceProvider.GetService(typeof(SComponentModel));
            myWorkspace = componentModel.GetService<VisualStudioWorkspace>();

        }

        /// <summary>
        /// Gets the instance of the command.
        /// </summary>
        public static MutationTesting Instance
        {
            get;
            private set;
        }

        public MutationResult Result
        {
            get;
            private set;
        }

        [Import(typeof(Microsoft.VisualStudio.LanguageServices.VisualStudioWorkspace))]
        public VisualStudioWorkspace myWorkspace { get; set; }

        /// <summary>
        /// Gets the service provider from the owner package.
        /// </summary>
        private IServiceProvider ServiceProvider
        {
            get
            {
                return this.package;
            }
        }

        /// <summary>
        /// Initializes the singleton instance of the command.
        /// </summary>
        /// <param name="package">Owner package, not null.</param>
        public static void Initialize(Package package)
        {
            Instance = new MutationTesting(package);
        }

        /// <summary>
        /// This function is the callback used to execute the command when the menu item is clicked.
        /// See the constructor to see how the menu item is associated with this function using
        /// OleMenuCommandService service and MenuCommand class.
        /// </summary>
        /// <param name="sender">Event sender.</param>
        /// <param name="e">Event args.</param>
        private void MenuItemCallback(object sender, EventArgs e)
        {
            SelectProject dialog = new SelectProject();
            DialogResult result = dialog.ShowDialog();
            if (result == DialogResult.OK)
            {
                errorProvider.Tasks.Clear();
                Task infoTask = new Task
                {
                    Category = TaskCategory.User,
                    Text = "Mutation testing has started. Mutants that your tests could not detect will appear here.",
                    CanDelete = true,
                    Priority = TaskPriority.Low
                };
                errorProvider.Tasks.Add(infoTask);

                errorProvider.Show();
                Project project = dialog.SelectedProject;
                Project testProject = dialog.SelectedTestProject;
                new System.Threading.Thread(() =>
                {
                    int killCount = 0;
                    menuItem.Enabled = false;
                    IVsStatusbar statusBar = (IVsStatusbar) ServiceProvider.GetService(typeof(SVsStatusbar));
                    
                    object icon = (short) Microsoft.VisualStudio.Shell.Interop.Constants.SBAI_Build;
                    statusBar.SetText("Instrumenting code for mutation...");
                    statusBar.Animation(1, ref icon);
                    try
                    {
                        CoveredProject coveredProject = new CoveredProject(project, testProject,
                            (m) =>
                            {
                                UpdateProgress(m, statusBar, ++killCount);
                                //ListMutantInErrorWindow(m, true);
                            },
                            (m) =>
                            {
                                UpdateProgress(m, statusBar, killCount);
                                ListMutantInErrorWindow(m, false);
                            });
                        Result = coveredProject.MutateAndTestProject();
                        //Result.LiveMutants.ForEach(ListMutantInErrorWindow);
                        Result.KilledMutants.ForEach((m) => ListMutantInErrorWindow(m, true));

                        statusBar.Animation(0, ref icon);
                        statusBar.SetText("Mutation complete (" + killCount + " mutants killed)");
                        menuItem.Enabled = true;
                        errorProvider.Tasks.Remove(infoTask);

                        VsShellUtilities.ShowMessageBox(this.ServiceProvider,
                            "Mutation complete. " + Result.LiveMutants.Count + " mutants lived. " +
                            Result.KilledMutants.Count +
                            " mutants successfully killed. See Error List pane for descriptions of live mutants.",
                            "Mutation testing", OLEMSGICON.OLEMSGICON_INFO, OLEMSGBUTTON.OLEMSGBUTTON_OK,
                            OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
                    }
                    catch (Exception e2)
                    {
                        Task newError = new Task
                        {
                            Category = TaskCategory.User,
                            Text = "Fatal error during mutation: "+e2.ToString(),
                            CanDelete = true,
                            Priority = TaskPriority.High
                        };
                        errorProvider.Tasks.Add(newError);
                    }
                }).Start();
            }
            dialog.Dispose();
        }

        private void UpdateProgress(ExportedMutantInfo info, IVsStatusbar statusBar, int killCount)
        {
            // https://msdn.microsoft.com/en-us/library/bb166795.aspx
            // Make sure the status bar is not frozen  
            int frozen;

            statusBar.IsFrozen(out frozen);

            if (frozen != 0)
            {
                statusBar.FreezeOutput(0);
            }
            statusBar.SetText("Mutating "+Path.GetFileName(info.fileName) + " (" + killCount + " mutants killed) ...");
        }

        private void ListMutantInErrorWindow(ExportedMutantInfo mutantInfo, bool killed)
        {
            
            Task newError = new Task
            {
                Category = TaskCategory.User,
                Text =
                    (killed ? "KILLED: " : "") + "Replaced " + mutantInfo.original.ToString() + " with " +
                    mutantInfo.mutant.ToString(),
                CanDelete = true,
                Document = mutantInfo.fileName,
                Line = mutantInfo.lineNumber,
                Column = mutantInfo.column,
                Priority = killed ? TaskPriority.Low : TaskPriority.Normal
            };
            // FIXME: use DocumentTask, which has built-in navigation.
            newError.Navigate += (object sender2, EventArgs e2) =>
            {
                // https://www.mztools.com/articles/2015/MZ2015002.aspx
                //Guid guid_source_code_text_editor_with_encoding = new Guid("{C7747503-0E24-4FBE-BE4B-94180C3947D7}");
                //var ds = myWorkspace.GetOpenDocumentIds();
                //IVsUIHierarchy hierarchy = null;
                //uint itemID = 0;
                //IVsWindowFrame frame = null;
                //if (VsShellUtilities.IsDocumentOpen(this.ServiceProvider, mutantInfo.fileName,
                //    guid_source_code_text_editor_with_encoding, out hierarchy, out itemID, out frame))
                //{
                //}
                //else
                //{
                //    frame = VsShellUtilities.OpenDocumentWithSpecificEditor(this.ServiceProvider, mutantInfo.fileName,
                //        guid_source_code_text_editor_with_encoding,
                //        Microsoft.VisualStudio.VSConstants.LOGVIEWID.Code_guid);
                //}

                //frame.Show();

                // https://social.msdn.microsoft.com/Forums/vstudio/en-US/6dc1a84c-3821-4e7f-aca8-d3b20929a34d/programmatically-open-a-file-item-and-place-the-cursor-at-a-row-and-column?forum=vsx
                DTE dte = (DTE) ServiceProvider.GetService(typeof(DTE));
                ProjectItem projItem = null;
                try
                {
                    projItem = dte.Solution.FindProjectItem(mutantInfo.fileName);
                }
                catch
                {
                }
                if (projItem != null)
                {
                    bool wasOpen = projItem.get_IsOpen(EnvDTE.Constants.vsViewKindCode);
                    Window win = null;
                    if (!wasOpen)
                    {
                        win = projItem.Open(EnvDTE.Constants.vsViewKindCode);
                        win.Visible = true;
                        win.SetFocus();
                    }
                    else
                    {
                        projItem.Document.Activate();
                    }
                    // FIXME: most lines are off-by-one, but not all.
                    // This is due to the code rewriting that adds curly braces.
                    ((TextSelection) projItem.Document.Selection).GotoLine(mutantInfo.lineNumber, true);
                    // Hacky solution: look forward from its approximate location for it.
                    ((TextSelection) projItem.Document.Selection).FindText(mutantInfo.original.ToString());

                }
            };
            lock (errorProvider)
            {
                errorProvider.Tasks.Add(newError);
            }
        }

        public List<Project> Projects => myWorkspace.CurrentSolution.Projects.ToList();
    }
}

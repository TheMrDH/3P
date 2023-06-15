#region header
// ========================================================================
// Copyright (c) 2018 - Julien Caillon (julien.caillon@gmail.com)
// This file (FileExplorerForm.cs) is part of 3P.
// 
// 3P is a free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
// 
// 3P is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
// GNU General Public License for more details.
// 
// You should have received a copy of the GNU General Public License
// along with 3P. If not, see <http://www.gnu.org/licenses/>.
// ========================================================================
#endregion
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using YamuiFramework.Animations.Transitions;
using YamuiFramework.Controls;
using YamuiFramework.Controls.YamuiList;
using YamuiFramework.Helper;
using _3PA.Lib;
using _3PA.MainFeatures.Appli;
using _3PA.MainFeatures.Pro;
using _3PA.MainFeatures.SyntaxHighlighting;
using _3PA.NppCore;
using _3PA.NppCore.NppInterfaceForm;
using _3PA._Resource;
using _3PA.MainFeatures.Pro.Deploy;
using System.Threading;
using _3PA.MainFeatures.AutoCompletionFeature;

namespace _3PA.MainFeatures.FileExplorer {
    internal partial class FileExplorerForm : NppDockableDialogForm {
        #region Private

        private string[] _explorerDirStr;
        // remember the list that was passed to the autocomplete form when we set the items, we need this
        // because we reorder the list each time the user filters stuff, but we need the original order
        private List<FileListItem> _initialObjectsList;
        private bool _isExpanded = true;
        public System.Windows.Forms.ContextMenu ContextMenu { get; set; }
        /// <summary>
        /// Use this to change the image of the refresh button to let the user know the tree is being refreshed
        /// </summary>
        private bool Refreshing {
            get { return _refreshing; }
            set {
                _refreshing = value;
                this.SafeInvoke(form => {
                    var refreshButton = filterbox.ExtraButtonsList != null && filterbox.ExtraButtonsList.Count > 0 ? filterbox.ExtraButtonsList[0] : null;
                    if (refreshButton == null)
                        return;
                    if (_refreshing) {
                        refreshButton.BackGrndImage = ImageResources.Refreshing;
                        btDirectory.Enabled = false;
                        toolTipHtml.SetToolTip(refreshButton, "The list is being refreshed, please wait");
                    } else {
                        refreshButton.BackGrndImage = ImageResources.Refresh;
                        toolTipHtml.SetToolTip(refreshButton, "Click this button to <b>refresh</b> the list of files for the current directory<br>No automatic //refreshing is done so you have to use this button when you add/delete a file in said directory");
                        btDirectory.Enabled = true;
                    }
                });
            }
        }

        private volatile bool _refreshing;

        private volatile bool _refreshRequiredWhileRefreshing;

        #endregion

        #region Fields public

        public YamuiFilteredTypeTreeList YamuiList {
            get { return yamuiList; }
        }

        public YamuiFilterBox FilterBox {
            get { return filterbox; }
        }

        #endregion

        #region constructor

        public FileExplorerForm(NppEmptyForm formToCover) : base(formToCover) {
            InitializeComponent();

            #region Current env

            // register to env change event
            ProEnvironment.OnEnvironmentChange += UpdateCurrentEnvName;

            UpdateCurrentEnvName();

            btEnvList.BackGrndImage = ImageResources.Env;
            btEnvList.ButtonPressed += BtEnvListOnButtonPressed;
            toolTipHtml.SetToolTip(btEnvList, "Click to <b>open a menu</b> that allows you to quickly select another environment");

            btPingServer.BackGrndImage = ImageResources.Database;
            btPingServer.ButtonPressed += BtPingServerOnButtonPressed;
            toolTipHtml.SetToolTip(btPingServer, "Click to <b>ping environment server</b> to check server connection");

            btEnvModify.BackGrndImage = ImageResources.ZoomIn;
            btEnvModify.ButtonPressed += BtEnvModifyOnButtonPressed;
            toolTipHtml.SetToolTip(btEnvModify, "Click to go to see the details of the current environment");

            toolTipHtml.SetToolTip(lblEnv, "Name of the currently selected environment");

            #endregion

            #region Current file

            // register to Updated Operation events
            OpenedFilesInfo.OnUpdatedOperation += FilesInfoOnUpdatedOperation;
            OpenedFilesInfo.OnUpdatedErrors += FilesInfoOnUpdatedErrors;

            btPrevError.ButtonPressed += BtPrevErrorOnButtonPressed;
            btNextError.ButtonPressed += BtNextErrorOnButtonPressed;
            btClearAllErrors.ButtonPressed += BtClearAllErrorsOnButtonPressed;
            btGetHelp.ButtonPressed += BtGetHelpOnButtonPressed;
            btStopExecution.ButtonPressed += BtStopExecutionOnButtonPressed;
            btBringProcessToFront.ButtonPressed += BtBringProcessToFrontOnButtonPressed;

            btPrevError.BackGrndImage = ImageResources.Previous;
            btNextError.BackGrndImage = ImageResources.Next;
            btClearAllErrors.BackGrndImage = ImageResources.ClearAll;
            btGetHelp.BackGrndImage = ImageResources.GetHelp;
            btGetHelp.UseGreyScale = !Config.Instance.GlobalShowDetailedHelpForErrors;
            btStopExecution.BackGrndImage = ImageResources.Stop;
            btBringProcessToFront.BackGrndImage = ImageResources.BringToFront;
            btStopExecution.Hide();
            btBringProcessToFront.Hide();

            UpdateErrorButtons(false);

            toolTipHtml.SetToolTip(btGetHelp, "Toggle on/off the <b>detailed help</b> for compilation errors and warnings");
            toolTipHtml.SetToolTip(btPrevError, "<b>Move the caret</b> to the previous error");
            toolTipHtml.SetToolTip(btNextError, "<b>Move the caret</b> to the next error");
            toolTipHtml.SetToolTip(btClearAllErrors, "<b>Clear</b> all the displayed errors");
            toolTipHtml.SetToolTip(lbStatus, "Provides information on the current status of the file");
            toolTipHtml.SetToolTip(btStopExecution, "Click to <b>kill</b> the current processus");
            toolTipHtml.SetToolTip(btBringProcessToFront, "Click to <b>bring</b> the current process to foreground");

            lbStatus.BackColor = ThemeManager.Current.FormBack;

            #endregion

            #region File list

            // add the refresh button to the filter box
            filterbox.ExtraButtons = new List<YamuiFilterBox.YamuiFilterBoxButton> {
                new YamuiFilterBox.YamuiFilterBoxButton {
                    Image = ImageResources.Refresh,
                    OnClic = OnRefreshClic
                },
                new YamuiFilterBox.YamuiFilterBoxButton {
                    Image = ImageResources.Collapse,
                    OnClic = buttonExpandRetract_Click,
                    ToolTip = "Toggle <b>Expand/Collapse</b>"
                }
            };

            filterbox.Initialize(yamuiList);
            yamuiList.EmptyListString = @"No files!";
            yamuiList.ShowTreeBranches = Config.Instance.ShowTreeBranches;

            toolTipHtml.SetToolTip(btGotoDir, "<b>Open</b> the current path in the windows explorer");
            toolTipHtml.SetToolTip(btDirectory, "Click to <b>change</b> the directory to explore");
            toolTipHtml.SetToolTip(lbDirectory, "Current directory being explored");

            btGotoDir.BackGrndImage = ImageResources.OpenInExplorer;
            btGotoDir.ButtonPressed += BtGotoDirOnButtonPressed;
            _explorerDirStr = new[] {"Local path ", "Compilation path", "Propath", "Everywhere"};
            btDirectory.ButtonPressed += BtDirectoryOnButtonPressed;

            RefreshGotoDirButton();

            Refreshing = false;

            yamuiList.RowClicked += YamuiListOnRowClicked;
            yamuiList.EnterPressed += YamuiListOnEnterPressed;

            #endregion
        }

        #endregion

        #region Refresh file list and selector mechanic

        /// <summary>
        /// Call this method to completly refresh the object view list (recompute the items of the list)
        /// </summary>
        public void RefreshFileList() {
            if (Refreshing) {
                _refreshRequiredWhileRefreshing = true;
                return;
            }
            _refreshRequiredWhileRefreshing = false;
            Refreshing = true;
            Task.Factory.StartNew(() => {
                try {
                    this.SafeInvoke(form => RefreshFileListAction());
                } catch (Exception e) {
                    ErrorHandler.ShowErrors(e, "Error while listing files");
                } finally {
                    Refreshing = false;
                    if (_refreshRequiredWhileRefreshing)
                        RefreshFileList();
                }
            });
        }

        private void RefreshFileListAction() {
            // get the list of FileObjects
            _initialObjectsList = new List<FileListItem>();
            switch (Config.Instance.FileExplorerDirectoriesToExplore) {
                case 0:
                    _initialObjectsList = FileExplorer.Instance.ListFileOjectsInDirectory(ProEnvironment.Current.BaseLocalPath);
                    break;
                case 1:
                    _initialObjectsList = FileExplorer.Instance.ListFileOjectsInDirectory(ProEnvironment.Current.BaseCompilationPath);
                    break;
                case 2:
                    foreach (var dir in ProEnvironment.Current.GetProPathDirList) {
                        _initialObjectsList.AddRange(FileExplorer.Instance.ListFileOjectsInDirectory(dir, false, false));
                    }
                    break;
                default:
                    // get the list of FileObjects
                    Regex regex = new Regex(@"\\\.");
                    var fullList = new HashSet<string>(StringComparer.CurrentCultureIgnoreCase);
                    fullList.Add(ProEnvironment.Current.BaseLocalPath);
                    if (!fullList.Contains(ProEnvironment.Current.BaseCompilationPath))
                        fullList.Add(ProEnvironment.Current.BaseCompilationPath);
                    // base local path
                    if (Directory.Exists(ProEnvironment.Current.BaseLocalPath)) {
                        foreach (var directory in Directory.GetDirectories(ProEnvironment.Current.BaseLocalPath, "*", SearchOption.AllDirectories)) {
                            if (!fullList.Contains(directory) && (!Config.Instance.FileExplorerIgnoreUnixHiddenFolders || !regex.IsMatch(directory)))
                                fullList.Add(directory);
                        }
                    }
                    // base compilation path
                    if (Directory.Exists(ProEnvironment.Current.BaseCompilationPath)) {
                        foreach (var directory in Directory.GetDirectories(ProEnvironment.Current.BaseCompilationPath, "*", SearchOption.AllDirectories)) {
                            if (!fullList.Contains(directory) && (!Config.Instance.FileExplorerIgnoreUnixHiddenFolders || !regex.IsMatch(directory)))
                                fullList.Add(directory);
                        }
                    }
                    // for each dir in propath
                    foreach (var directory in ProEnvironment.Current.GetProPathDirList) {
                        if (!fullList.Contains(directory) && (!Config.Instance.FileExplorerIgnoreUnixHiddenFolders || !regex.IsMatch(directory)))
                            fullList.Add(directory);
                    }
                    foreach (var path in fullList) {
                        _initialObjectsList.AddRange(FileExplorer.Instance.ListFileOjectsInDirectory(path, false));
                    }
                    break;
            }
            // apply custom sorting
            _initialObjectsList.Sort(FileSortingClass<FileListItem>.Instance);
            
            try {
                yamuiList.SetItems(_initialObjectsList.Cast<ListItem>().ToList());
            } catch (Exception e) {
                ErrorHandler.ShowErrors(e, "Error while showing the list of files");
            }
        }

        #endregion

        #region File list events

        private void YamuiListOnEnterPressed(YamuiScrollList yamuiScrollList, KeyEventArgs keyEventArgs) {
            var curItem = (FileListItem) yamuiList.SelectedItem;
            if (curItem == null)
                return;
            Utils.OpenAnyLink(curItem.FullPath);
        }

        private void OnExecCompilationOk(MultiCompilation proc, List<FileToCompile> fileToCompiles, List<FileToDeploy> filesToDeploy)
        {

            FileDeployedCompiled fileDeployedCompiled = new FileDeployedCompiled();
            string fileName = fileToCompiles.First().SourcePath.Substring(0, fileToCompiles.First().SourcePath.Length - (fileToCompiles.First().BaseFileName.Length + 2));
            UserCommunication.NotifyUnique("", "<div style='padding-bottom: 5px;'>File Explorer Compile:</div>" + ProExecutionCompile.FormatCompilationResultForFolder(fileName, fileToCompiles, filesToDeploy), MessageImg.MsgOk, "Compiled Folder", fileName, null);


            this._currentOperation = -1;

        }

        public void FileExplorerCompile(object sender, EventArgs e) 
        {

            System.Windows.Forms.MenuItem contextMenu = (System.Windows.Forms.MenuItem)sender;
            int action = 0;
            var curItem = (FileListItem)yamuiList.SelectedItem;
            string fileName = curItem.FullPath.Split('\\')[curItem.FullPath.Split('\\').Length - 1];
            if ( contextMenu.Name.Equals("OpenExplorer"))
            {
                if(File.GetAttributes(curItem.FullPath) == FileAttributes.Directory)
                {
                    Utils.OpenFolder(curItem.FullPath);
                }
                Utils.OpenFileInFolder(curItem.FullPath);
            }
            else
            {
                if (File.GetAttributes(curItem.FullPath) == FileAttributes.Directory)
                {
                    action = 2;
                }
                else
                {
                    action = 1;

                }
            }
            if(action == 1)
            {
                Pro.Deploy.FileToCompile g = new Pro.Deploy.FileToCompile(curItem.FullPath, curItem.Size);
                List<Pro.Deploy.FileToCompile> fileToCompiles = new List<Pro.Deploy.FileToCompile>();
                fileToCompiles.Add(g);
                ProMisc.StartProgressManualComp(ExecutionType.Compile, fileToCompiles);
                UserCommunication.NotifyUnique("", "<div style='padding-bottom: 5px;'>File Explorer Compile:</div>" + ProExecutionCompile.FormatCompilationResultForSingleFile(g.SourcePath, g, null), MessageImg.MsgOk, "Compiling File", fileName, null, 10);
            }else if(action == 2)
            {
                MainFeatures.Pro.Deploy.MultiCompilation proExecutionCompile = new Pro.Deploy.MultiCompilation(ProEnvironment.Current);
                List<Pro.Deploy.FileToCompile> fileToCompiles = new List<Pro.Deploy.FileToCompile>();

                string[] files = System.IO.Directory.GetFiles(curItem.FullPath, "*.*").Where(f => f.Contains(".p") || f.Contains(".w")).ToArray();
                Npp.NppFileInfo fileInfo = new Npp.NppFileInfo();

                foreach (string s in files)
                {
                    fileInfo.Path = s;
                    if (fileInfo.IsCompilable && fileInfo.IsProgress)
                    {
                        Pro.Deploy.FileToCompile g = new Pro.Deploy.FileToCompile(s, curItem.Size);
                        fileToCompiles.Add(g);
                    }

                }
                proExecutionCompile.CompileFiles(fileToCompiles);
                proExecutionCompile.OnCompilationOk += OnExecCompilationOk;
                fileName = fileToCompiles.First().SourcePath.Substring(0, fileToCompiles.First().SourcePath.Length - (fileToCompiles.First().BaseFileName.Length + 2));
                UserCommunication.NotifyUnique("", "<div style='padding-bottom: 5px;'>File Explorer Compile:</div>" + ProExecutionCompile.FormatCompilationResultForFolder(fileName, fileToCompiles, null), MessageImg.MsgOk, "Compiling Folder", fileName, null);
            }
        }

        public void FormExplorerPopup(object sender, EventArgs e)
        {
            System.Windows.Forms.ContextMenu contextMenu = (System.Windows.Forms.ContextMenu)sender;
            var curItem = (FileListItem)yamuiList.SelectedItem;
            if(curItem != null)
            {
                string fileName = curItem.FullPath.Split('\\')[curItem.FullPath.Split('\\').Length - 1];
                contextMenu.MenuItems[1].Text = "Compile " + fileName;

            }
        }

        private void YamuiListOnMouseHover(object yamuiScrollList, EventArgs e)
        {
            var curItem = (FileListItem)yamuiList.SelectedItem;


            if (curItem == null)
            {
                return;
            }
            curItem.DisplayText = curItem.FullPath;
        }

        private void YamuiListOnRowClicked(YamuiScrollList yamuiScrollList, MouseEventArgs e) {
            var curItem = (FileListItem)yamuiList.SelectedItem;


            if (curItem == null)
            {
                return;
            }
                
            if (e.Button == MouseButtons.Right) {
                ContextMenu.Show(yamuiScrollList, yamuiScrollList.PointToClient(MousePosition)); //Figure out why context menu is not appearing at mouse.
            } else if (e.Clicks >= 2)
                Utils.OpenAnyLink(curItem.FullPath);
        }

        /// <summary>
        /// Redirect mouse wheel to yamuilist?
        /// </summary>
        protected override void OnMouseWheel(MouseEventArgs e) {
            if (ActiveControl is YamuiFilterBox)
                yamuiList.DoScroll(e.Delta);
            base.OnMouseWheel(e);
        }

        private void RefreshGotoDirButton() {
            // refresh a button depending on the mode...
            if (IsHandleCreated) {
                BeginInvoke((Action) delegate {
                    btGotoDir.Visible = Config.Instance.FileExplorerDirectoriesToExplore <= 1;
                    Image tryImg = (Image) ImageResources.ResourceManager.GetObject("ExplorerDir" + Config.Instance.FileExplorerDirectoriesToExplore);
                    btDirectory.BackGrndImage = tryImg ?? ImageResources.Error;
                    btDirectory.Invalidate();
                    lbDirectory.Text = _explorerDirStr[Config.Instance.FileExplorerDirectoriesToExplore];
                });
            }
        }

        private void BtGotoDirOnButtonPressed(object sender, EventArgs buttonPressedEventArgs) {
            if (Config.Instance.FileExplorerDirectoriesToExplore == 0)
                Utils.OpenFolder(ProEnvironment.Current.BaseLocalPath);
            else if (Config.Instance.FileExplorerDirectoriesToExplore == 1)
                Utils.OpenFolder(ProEnvironment.Current.BaseCompilationPath);
        }

        private void BtDirectoryOnButtonPressed(object sender, EventArgs buttonPressedEventArgs) {
            Config.Instance.FileExplorerDirectoriesToExplore++;
            if (Config.Instance.FileExplorerDirectoriesToExplore > 3) Config.Instance.FileExplorerDirectoriesToExplore = 0;
            RefreshGotoDirButton();

            RefreshFileList();
            filterbox.FocusFilter();
        }

        private void OnRefreshClic(YamuiButtonImage yamuiButtonImage, EventArgs e) {
            RefreshFileList();
            filterbox.FocusFilter();
        }

        private void buttonExpandRetract_Click(YamuiButtonImage sender, EventArgs e) {
            if (_isExpanded)
                yamuiList.ForceAllToCollapse();
            else
                yamuiList.ForceAllToExpand();
            _isExpanded = !_isExpanded;
            filterbox.ExtraButtonsList[1].BackGrndImage = _isExpanded ? ImageResources.Collapse : ImageResources.Expand;
            Sci.GrabFocus();
        }

        #endregion

        #region Current file

        private int _currentOperation = -1;

        public void FilesInfoOnUpdatedOperation(UpdatedOperationEventArgs updatedOperationEventArgs) {
            this.SafeInvoke(form => {
                if (_currentOperation == (int) updatedOperationEventArgs.CurrentOperation)
                    return;

                // status text, take the last flag found
                typeof(CurrentOperation).ForEach<CurrentOperation>((name, flag) => {
                    if (updatedOperationEventArgs.CurrentOperation.HasFlag(flag)) {
                        lbStatus.Text = flag.GetAttribute<CurrentOperationAttr>().Name;
                    }
                });

                // blink back color
                lbStatus.UseCustomBackColor = true;
                if (updatedOperationEventArgs.CurrentOperation > 0) {
                    Transition.run(lbStatus, "BackColor", ThemeManager.Current.FormBack, ThemeManager.Current.AccentColor, new TransitionType_Flash(3, 400), (o, args) => {
                        lbStatus.BackColor = ThemeManager.Current.AccentColor;
                        lbStatus.Invalidate();
                    });
                } else {
                    Transition.run(lbStatus, "BackColor", ThemeManager.Current.AccentColor, ThemeManager.Current.FormBack, new TransitionType_Flash(3, 400), (o, args) => {
                        lbStatus.UseCustomBackColor = false;
                        lbStatus.Invalidate();
                    });
                }

                _currentOperation = (int) updatedOperationEventArgs.CurrentOperation;

                if (btStopExecution.Visible != (_currentOperation >= (int) CurrentOperation.Prolint)) {
                    btStopExecution.Visible = (_currentOperation >= (int) CurrentOperation.Prolint);
                    lbStatus.Width = lbStatus.Width + (btStopExecution.Visible ? -1 : 1) * btStopExecution.Width;
                }

                if (btBringProcessToFront.Visible != updatedOperationEventArgs.CurrentOperation.HasFlag(CurrentOperation.Run)) {
                    btBringProcessToFront.Visible = updatedOperationEventArgs.CurrentOperation.HasFlag(CurrentOperation.Run);
                    lbStatus.Width = lbStatus.Width + (btBringProcessToFront.Visible ? -1 : 1) * btBringProcessToFront.Width;
                }
            });
        }

        public void FilesInfoOnUpdatedErrors(UpdatedErrorsEventArgs updatedErrorsEventArgs) {
            this.SafeInvoke(form => {
                lbNbErrors.UseCustomBackColor = true;
                lbNbErrors.UseCustomForeColor = true;
                var t = new Transition(new TransitionType_Linear(500));

                // disable/enable buttons
                UpdateErrorButtons(updatedErrorsEventArgs.NbErrors > 0);

                // colors
                t.add(lbNbErrors, "BackColor", ScintillaTheme.CurrentTheme.GetErrorBg((int) updatedErrorsEventArgs.ErrorLevel));
                t.add(lbNbErrors, "ForeColor", ScintillaTheme.CurrentTheme.GetErrorFg((int) updatedErrorsEventArgs.ErrorLevel));

                // text
                t.add(lbNbErrors, "Text", updatedErrorsEventArgs.NbErrors.ToString());
                t.add(lbErrorText, "Text", updatedErrorsEventArgs.ErrorLevel.GetDescription());

                t.run();
            });
        }

        private void UpdateErrorButtons(bool activate) {
            btPrevError.Enabled = activate;
            btNextError.Enabled = activate;
            btClearAllErrors.Enabled = activate;
        }

        private void BtClearAllErrorsOnButtonPressed(object sender, EventArgs buttonPressedEventArgs) {
            OpenedFilesInfo.ClearAllErrors(Npp.CurrentFileInfo.Path);
            Sci.GrabFocus();
        }

        private void BtNextErrorOnButtonPressed(object sender, EventArgs buttonPressedEventArgs) {
            OpenedFilesInfo.GoToNextError(Sci.Line.CurrentLine + 1);
        }

        private void BtPrevErrorOnButtonPressed(object sender, EventArgs buttonPressedEventArgs) {
            OpenedFilesInfo.GoToPrevError(Sci.Line.CurrentLine - 1);
        }

        private void BtGetHelpOnButtonPressed(object sender, EventArgs buttonPressedEventArgs) {
            Config.Instance.GlobalShowDetailedHelpForErrors = !Config.Instance.GlobalShowDetailedHelpForErrors;
            btGetHelp.UseGreyScale = !Config.Instance.GlobalShowDetailedHelpForErrors;
            OpenedFilesInfo.ClearAnnotationsAndMarkers();
            OpenedFilesInfo.UpdateErrorsInScintilla();
            Sci.GrabFocus();
        }

        private void BtStopExecutionOnButtonPressed(object sender, EventArgs eventArgs) {
            ProMisc.KillCurrentProcess();
        }

        private void BtBringProcessToFrontOnButtonPressed(object sender, EventArgs eventArgs) {
            if (OpenedFilesInfo.CurrentOpenedFileInfo.ProgressExecution != null)
                OpenedFilesInfo.CurrentOpenedFileInfo.ProgressExecution.BringProcessToFront();
        }

        #endregion

        #region Current env

        private void UpdateCurrentEnvName() {
            lblEnv.Text = ProEnvironment.Current.Name + (!string.IsNullOrEmpty(ProEnvironment.Current.Suffix) ? " - " + ProEnvironment.Current.Suffix : "");
        }

        private void BtEnvModifyOnButtonPressed(object sender, EventArgs eventArgs) {
            Appli.Appli.GoToPage(PageNames.SetEnvironment);
        }

        private void BtEnvListOnButtonPressed(object sender, EventArgs eventArgs) {
            AppliMenu.ShowEnvMenu(true);
        }

        private void BtPingServerOnButtonPressed(object sender, EventArgs eventArgs)
        {


            string output = "";
            List<string> dbList = new List<string>();
            
            Pro.ProEnvironment.Current.ConnectionString.Split('-').ToList().ForEach(o => {
                
                if (o.Contains("db")) 
                {
                    output += "<div>";
                    output += o.Replace("db ", "");
                    dbList.Add(o.Replace("db ", "").Trim());
                }
                if (o.Contains("H"))
                {
                    output += o.Replace("H ", " -").Trim();
                    output += "</div>";
                }

                else if (o.Contains(".pf"))
                {
                    StreamReader streamReader = new StreamReader(o);
                    var m = streamReader.ReadToEnd().Split('-').ToList();
                    foreach(var n in m)
                    {
                        
                        if (n.Contains("db"))
                        {
                            output += "<div>";
                            output += n.Replace("db ", "");
                            dbList.Add(n.Replace("db ", "").Trim());
                        }
                        if (n.Contains("H"))
                        {
                            output += n.Replace("H ", " -").Trim();
                            output += "</div>";
                        }
                        
                    }
                }

            });
            DataBase.Instance.PingCurrentDb(null, "", String.Join(",",dbList));
            UserCommunication.Notify("<div>" + DataBase.Instance.GetCurrentDumpPath + " Test: " + output + "</div>");
            
        }

        #endregion
    }
}
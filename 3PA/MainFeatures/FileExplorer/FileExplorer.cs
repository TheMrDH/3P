﻿#region header
// ========================================================================
// Copyright (c) 2018 - Julien Caillon (julien.caillon@gmail.com)
// This file (FileExplorer.cs) is part of 3P.
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
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using YamuiFramework.Controls.YamuiList;
using _3PA.MainFeatures.Appli;
using _3PA.NppCore;
using _3PA.NppCore.NppInterfaceForm;
using _3PA._Resource;
using System.Windows.Forms;

namespace _3PA.MainFeatures.FileExplorer {
    internal class FileExplorer : NppDockableDialog<FileExplorerForm> {
        #region Core

        #region Singleton

        private static FileExplorer _instance;

        public static FileExplorer Instance {
            get { return _instance ?? (_instance = new FileExplorer()); }
            set { _instance = value; }
        }

        private FileExplorer() {
            _dialogDescription = "File explorer";
            _formDefaultPos = NppTbMsg.DWS_DF_CONT_LEFT;
            _iconImage = ImageResources.FileExplorerLogo;
        }

        #endregion

        #region Override methods

        protected override void InitForm() {
            Form = new FileExplorerForm(_fakeForm);
            System.Windows.Forms.MenuItem[] menuItem = { new System.Windows.Forms.MenuItem(), new System.Windows.Forms.MenuItem() };
            ContextMenu contextMenu = new ContextMenu();
            menuItem[0].Text = "Open In Explorer";
            menuItem[0].Name = "OpenExplorer";
            menuItem[0].Click += Form.FileExplorerCompile;
            menuItem[1].Name = "CompileFile";
            menuItem[1].Click += Form.FileExplorerCompile;
            contextMenu.MenuItems.AddRange(menuItem);
            contextMenu.Popup += Form.FormExplorerPopup;
            Form.ContextMenu = contextMenu;
        }

        protected override void OnVisibilityChange(bool visible) {
            Config.Instance.FileExplorerVisible = visible;
            if (visible) {
                Form.RefreshFileList();
            }
        }

        #endregion

        #endregion

        #region handling form

        /// <summary>
        /// Use this to redraw the docked form
        /// </summary>
        public void ApplyColorSettings() {
            if (Form == null)
                return;
            Form.YamuiList.ShowTreeBranches = Config.Instance.ShowTreeBranches;
            Form.Refresh();
        }

        #endregion

        #region public methods

        /// <summary>
        /// Refresh the files list
        /// </summary>
        public void RebuildFileList() {
            if (!IsVisible)
                return;
            Form.RefreshFileList();
        }

        /// <summary>
        /// Start a new search for files
        /// </summary>
        public void StartSearch() {
            if (!IsVisible)
                return;
            Form.Focus();
            Form.FilterBox.ClearAndFocusFilter();
        }

        private DateTime _startTime;

        /// <summary>
        /// Add each files/folders of a given path to the output List of FileObject,
        /// can be set to be recursive,
        /// can be set to not add the subfolders in the results
        /// TODO: Parallelize for max speed
        /// </summary>
        public List<FileListItem> ListFileOjectsInDirectory(string dirPath, bool recursive = true, bool includeFolders = true, bool firstCall = true) {
            if (firstCall)
                _startTime = DateTime.Now;

            var output = new List<FileListItem>();
            if (!Directory.Exists(dirPath))
                return output;

            // get dir info
            var dirInfo = new DirectoryInfo(dirPath);

            // for each file in the dir
            try {
                foreach (var fileInfo in dirInfo.GetFiles()) {
                    FileExt fileExt;
                    if (!Enum.TryParse(fileInfo.Extension.Replace(".", ""), true, out fileExt))
                        fileExt = FileExt.Unknow;
                    output.Add(new FileListItem {
                        DisplayText = fileInfo.Name,
                        BasePath = fileInfo.DirectoryName,
                        FullPath = fileInfo.FullName,
                        Flags = FileFlag.ReadOnly,
                        Size = fileInfo.Length,
                        CreateDateTime = fileInfo.CreationTime,
                        ModifieDateTime = fileInfo.LastWriteTime,
                        Type = fileExt,
                    });
                }
            } catch (Exception e) {
                ErrorHandler.LogError(e);
            }

            // for each folder in dir
            if (includeFolders) {
                Regex regex = new Regex(@"\\\.");
                try {
                    foreach (var directoryInfo in dirInfo.GetDirectories()) {
                        if (!Config.Instance.FileExplorerIgnoreUnixHiddenFolders || !regex.IsMatch(directoryInfo.FullName)) {
                            var folderItem = new FileListItem {
                                DisplayText = directoryInfo.Name,
                                BasePath = Path.GetDirectoryName(directoryInfo.FullName),
                                FullPath = directoryInfo.FullName,
                                CreateDateTime = directoryInfo.CreationTime,
                                ModifieDateTime = directoryInfo.LastWriteTime,
                                Type = FileExt.Folder
                            };
                            output.Add(folderItem);
                            // recursive
                            if (recursive && DateTime.Now.Subtract(_startTime).TotalMilliseconds <= Config.Instance.FileExplorerListFilesTimeOutInMs) {
                                folderItem.Children = ListFileOjectsInDirectory(directoryInfo.FullName, true, true, false).Cast<FilteredTypeTreeListItem>().ToList();
                                if (folderItem.Children.Count == 0)
                                    folderItem.Children = null;
                            }
                        }
                    }
                } catch (Exception e) {
                    ErrorHandler.LogError(e);
                }
            }

            if (firstCall && DateTime.Now.Subtract(_startTime).TotalMilliseconds > Config.Instance.FileExplorerListFilesTimeOutInMs) {
                UserCommunication.NotifyUnique("FileExplorerTimeOut", "The file explorer was listing all the files of the requested folder but has been interrupted because it was taking too long.<br><br>You can set a value for this time out in the option page.", MessageImg.MsgInfo, "Listing files", "Time out reached", args => {
                    Appli.Appli.GoToPage(PageNames.OptionsMisc);
                    UserCommunication.CloseUniqueNotif("FileExplorerTimeOut");
                    args.Handled = true;
                });
            }

            return output;
        }

        #endregion
    }
}
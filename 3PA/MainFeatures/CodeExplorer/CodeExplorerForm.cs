﻿#region header
// ========================================================================
// Copyright (c) 2018 - Julien Caillon (julien.caillon@gmail.com)
// This file (CodeExplorerForm.cs) is part of 3P.
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
using System.Linq;
using System.Windows.Forms;
using YamuiFramework.Controls;
using YamuiFramework.Controls.YamuiList;
using _3PA.MainFeatures.Parser;
using _3PA.MainFeatures.Parser.Pro;
using _3PA.NppCore;
using _3PA.NppCore.NppInterfaceForm;
using _3PA._Resource;
using System.Threading;

namespace _3PA.MainFeatures.CodeExplorer {
    internal partial class CodeExplorerForm : NppDockableDialogForm {
        #region private

        private volatile bool _refreshing;

        private bool _isExpanded = true;

        #endregion

        #region constructor

        public CodeExplorerForm(NppEmptyForm formToCover) : base(formToCover) {
            InitializeComponent();

            // add the refresh button to the filter box
            filterbox.ExtraButtons = new List<YamuiFilterBox.YamuiFilterBoxButton> {
                new YamuiFilterBox.YamuiFilterBoxButton {
                    Image = ImageResources.Refresh,
                    OnClic = buttonRefresh_Click
                },
                new YamuiFilterBox.YamuiFilterBoxButton {
                    Image = ImageResources.Collapse,
                    OnClic = buttonExpandRetract_Click,
                    ToolTip = "Toggle <b>Expand/Collapse</b>"
                },
                new YamuiFilterBox.YamuiFilterBoxButton {
                    Image = ImageResources.Numerical_sorting,
                    OnClic = buttonSort_Click,
                    ToolTip = "Choose the way the items are sorted :<br>- Natural order (code order)<br>-Alphabetical order"
                },
                new YamuiFilterBox.YamuiFilterBoxButton {
                    Image = ImageResources.Persistent,
                    OnClic = ButtonPersistentOnButtonPressed,
                    ToolTip = "Toggle on/off <b>to display</b>, in the explorer, the functions and procedures loaded in persistent in this file"
                },
                new YamuiFilterBox.YamuiFilterBoxButton {
                    Image = ImageResources.FromInclude,
                    OnClic = ButtonFromIncludeOnButtonPressed,
                    ToolTip = "Toggle on/off <b>to display</b>, in the explorer, all the items loaded from include files"
                }
            };
            filterbox.Initialize(yamuiList);
            filterbox.ExtraButtonsList[0].AcceptsAnyClick = true;
            filterbox.ExtraButtonsList[1].BackGrndImage = _isExpanded ? ImageResources.Collapse : ImageResources.Expand;
            filterbox.ExtraButtonsList[2].BackGrndImage = Config.Instance.CodeExplorerSortingType == SortingType.Alphabetical ? ImageResources.Alphabetical_sorting : ImageResources.Numerical_sorting;
            filterbox.ExtraButtonsList[3].UseGreyScale = !Config.Instance.CodeExplorerDisplayPersistentItems;
            filterbox.ExtraButtonsList[4].UseGreyScale = !Config.Instance.CodeExplorerDisplayItemsFromInclude;

            Refreshing = false;

            yamuiList.ShowTreeBranches = Config.Instance.ShowTreeBranches;
            yamuiList.EmptyListString = @"Nothing to display";

            // list events
            yamuiList.RowClicked += YamuiListOnRowClicked;
            yamuiList.EnterPressed += YamuiListOnEnterPressed;
        }

        #endregion

        #region Public

        /// <summary>
        /// Use this to change the image of the refresh button to let the user know the tree is being refreshed
        /// </summary>
        public bool Refreshing {
            get { return _refreshing; }
            set {
                _refreshing = value;
                var refreshButton = filterbox.ExtraButtonsList != null && filterbox.ExtraButtonsList.Count > 0 ? filterbox.ExtraButtonsList[0] : null;
                if (refreshButton == null)
                    return;
                if (_refreshing) {
                    refreshButton.BackGrndImage = ImageResources.Refreshing;
                    toolTipHtml.SetToolTip(refreshButton, "The tree is being refreshed, please wait");
                } else {
                    refreshButton.BackGrndImage = ImageResources.Refresh;
                    toolTipHtml.SetToolTip(refreshButton, "Click to <b>Refresh</b> the tree");
                }
            }
        }

        public void ShowTreeBranches(bool show) {
            yamuiList.ShowTreeBranches = show;
        }

        /// <summary>
        /// This method uses the items found by the parser to update the code explorer tree (async)
        /// </summary>
        /// <param name="codeExplorerItems"></param>
        public void UpdateTreeData(List<CodeItem> codeExplorerItems) {
            yamuiList.SetItems(codeExplorerItems.Cast<ListItem>().ToList());
        }

        /// <summary>
        /// Updates the current scope to inform the user in which scope the caret is currently in
        /// </summary>
        public void UpdateCurrentScope(string text, Image image) {
            pbCurrentScope.BackGrndImage = image;
            lbCurrentScope.Text = text;
        }

        #endregion

        #region Private

        /// <summary>
        /// Redirect mouse wheel to yamuilist?
        /// </summary>
        protected override void OnMouseWheel(MouseEventArgs e) {
            if (ActiveControl is YamuiFilterBox)
                yamuiList.DoScroll(e.Delta);
            base.OnMouseWheel(e);
        }

        /// <summary>
        /// Executed when the user double click an item or press enter
        /// </summary>
        private bool OnActivateItem() {
            var curItem = yamuiList.SelectedItem as CodeItem;
            if (curItem == null)
                return false;

            if (!curItem.CanExpand && !string.IsNullOrEmpty(curItem.DocumentOwner)) {
                // Item clicked : go to line
                Npp.Goto(curItem.DocumentOwner, curItem.GoToLine, curItem.GoToColumn);
                return true;
            }

            return false;
        }

        #endregion

        #region Button events

        private void YamuiListOnEnterPressed(YamuiScrollList yamuiScrollList, KeyEventArgs keyEventArgs) {
            OnActivateItem();
        }

        private void YamuiListOnRowClicked(YamuiScrollList yamuiScrollList, MouseEventArgs mouseEventArgs) {
            if (OnActivateItem())
                Sci.GrabFocus();
        }

        private void buttonRefresh_Click(YamuiButtonImage sender, EventArgs e) {
            MouseEventArgs mouseEventArgs = (MouseEventArgs) e;
            
            if (mouseEventArgs.Button == MouseButtons.Left)
            {
                if (Refreshing)
                    return;
                ParserHandler.ClearStaticData();
                Npp.CurrentSci.Lines.Reset();
                ParserHandler.ParseDocumentNow();
                Sci.GrabFocus();
            }
            else if (((MouseEventArgs)e).Button == MouseButtons.Right)
            {
                List<string> list = new List<string> { "List Threads", "Close" };
                object temp = null ;
                object temp2 = null ;
                int i = UserCommunication.Input(ref temp, Npp.threads.Count + " active threads. One may be the parser. ", MessageImg.MsgDebug, "Number of threads:", "", list);
                if (Npp.threads.Count > 0 && i == 0 )
                {
                    string threadList = "";
                    Npp.threads.ForEach(o => { threadList += "<br><a href=\"" + o.Name + "\" style =\"text-decoration: underline\">" + o.Name + "</a></br>"; });
                    
                    int m = UserCommunication.Input(ref temp2, "Click link to kill it." + threadList, MessageImg.MsgDebug, "Active threads:", "", new List<string>() { "Ok", "Cancel", "Kill All" },  args =>
                    {

                        int tempThread = Npp.threads.FindIndex(o => o.Name == args.Link);
                        if (tempThread == -1)
                        {
                            UserCommunication.Message("Thread not found: " + args.Link, MessageImg.MsgError, "Thread not found", "");
                        }
                        else
                        {
                            Npp.threads.Remove(Npp.threads[tempThread]);
                            args.Handled = true;
                            UserCommunication.CloseUniqueMessage("Thread List");
                            UserCommunication.Message("Killed thread: " + args.Link, MessageImg.MsgPoison, "Thread Killed", "");
                        }
                    }
                    );

                    if(m == 2)
                    {
                        string temp3 = "<div>";
                        foreach(Thread n in Npp.threads)
                        {
                            n.Abort();
                            temp3 += "<br><span>" + n.Name + ": Killed. </span></br>";
                        }
                        temp3 += "</div>";
                        Npp.threads.RemoveAll(o => o.Name != "");
                        UserCommunication.Message(temp3 , MessageImg.MsgPoison, "Threads Killed", "");
                    }
                }
                else if ( i == 0)
                {
                    UserCommunication.Message( "No active threads.", MessageImg.MsgDebug, "", "" );
                }
                return;
            }
            else
            {
                return;
            }
        }

        private void buttonSort_Click(YamuiButtonImage sender, EventArgs e) {
            Config.Instance.CodeExplorerSortingType++;
            if (Config.Instance.CodeExplorerSortingType > SortingType.Alphabetical)
                Config.Instance.CodeExplorerSortingType = SortingType.NaturalOrder;
            filterbox.ExtraButtonsList[2].BackGrndImage = Config.Instance.CodeExplorerSortingType == SortingType.Alphabetical ? ImageResources.Alphabetical_sorting : ImageResources.Numerical_sorting;
            ParserHandler.ParseDocumentNow();
            Sci.GrabFocus();
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

        private void ButtonPersistentOnButtonPressed(YamuiButtonImage sender, EventArgs e) {
            // change option and image
            Config.Instance.CodeExplorerDisplayPersistentItems = !Config.Instance.CodeExplorerDisplayPersistentItems;
            filterbox.ExtraButtonsList[3].UseGreyScale = !Config.Instance.CodeExplorerDisplayPersistentItems;
            // Parse the document
            ParserHandler.ParseDocumentNow();
            Sci.GrabFocus();
        }

        private void ButtonFromIncludeOnButtonPressed(YamuiButtonImage sender, EventArgs e) {
            // change option and image
            Config.Instance.CodeExplorerDisplayItemsFromInclude = !Config.Instance.CodeExplorerDisplayItemsFromInclude;
            filterbox.ExtraButtonsList[4].UseGreyScale = !Config.Instance.CodeExplorerDisplayItemsFromInclude;
            // Parse the document
            ParserHandler.ParseDocumentNow();
            Sci.GrabFocus();
        }

        #endregion
    }
}
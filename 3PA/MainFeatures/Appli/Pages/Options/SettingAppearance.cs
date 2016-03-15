﻿#region header
// ========================================================================
// Copyright (c) 2016 - Julien Caillon (julien.caillon@gmail.com)
// This file (SettingAppearance.cs) is part of 3P.
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
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using YamuiFramework.Controls;
using _3PA.MainFeatures.AutoCompletion;
using _3PA.MainFeatures.FilesInfoNs;

namespace _3PA.MainFeatures.Appli.Pages.Options {
    internal partial class SettingAppearance : YamuiPage {

        private static YamuiColorRadioButton _checkButton;

        public SettingAppearance() {
            InitializeComponent();

            // AccentColors picker
            int x = 0;
            int y = 0;
            foreach (var accentColor in ThemeManager.GetAccentColors) {
                var newColorPicker = new YamuiColorRadioButton();
                PanelAccentColor.Controls.Add(newColorPicker);
                newColorPicker.CheckedChanged += NewColorPickerOnCheckedChanged;
                newColorPicker.BackColor = accentColor;
                newColorPicker.Bounds = new Rectangle(x, y, 50, 50);
                if (y + 2*newColorPicker.Height > PanelAccentColor.Height) {
                    x += newColorPicker.Width;
                    y = 0;
                } else
                    y += newColorPicker.Height;
                if (ThemeManager.Current.AccentColor == accentColor) {
                    _checkButton = newColorPicker;
                    newColorPicker.Checked = true;
                }
                toolTip.SetToolTip(newColorPicker, "Click me to set a new accent color for the current theme");
            }

            // themes combo box
            cbApplication.DataSource = ThemeManager.GetThemesList().Select(theme => theme.ThemeName).ToList();
            cbApplication.SelectedIndex = ThemeManager.GetThemesList().FindIndex(theme => theme.UniqueId == Config.Instance.ThemeId);
            cbApplication.SelectedIndexChanged += CbApplicationOnSelectedIndexChanged;

            // syntax combo
            cbSyntax.DataSource = Style.GetThemesList().Select(theme => theme.Name).ToList();
            cbSyntax.SelectedIndex = Config.Instance.SyntaxHighlightThemeId;
            cbSyntax.SelectedIndexChanged += CbSyntaxSelectedIndexChanged;

            // toggle
            tg_colorOn.CheckedChanged += TgColorOnOnCheckedChanged;
            tg_colorOn.Checked = Config.Instance.GlobalDontUseSyntaxHighlightTheme;
            UpdateToggle();

            // tooltips
            toolTip.SetToolTip(cbApplication, "Choose the theme you wish to use for the software");
            toolTip.SetToolTip(cbSyntax, "Choose the theme you wish to use for the syntax highlighting");
            toolTip.SetToolTip(tg_colorOn, "Toggle this option on if you are using your own User Defined Language<br><br>By default, 3P created a new UDL called 'OpenEdgeABL' and applies the selected theme below<br>each time the user switches the current document<br>By toggling this on, you will prevent this behavior and you can define your own UDL<br><br><i>If you toggle this, select the UDL to use from the Language menu before you can see any changes</i>");
        }

        /// <summary>
        /// Toggle on/off the use of a custom UDL
        /// </summary>
        private void TgColorOnOnCheckedChanged(object sender, EventArgs eventArgs) {
            Config.Instance.GlobalDontUseSyntaxHighlightTheme = tg_colorOn.Checked;
            UpdateToggle();
        }

        private void UpdateToggle() {
            if (tg_colorOn.Checked) {
                tg_colorOn.Text = "Use a custom User Defined Language";
                cbSyntax.Hide();
            } else {
                tg_colorOn.Text = "Use the themes provided by 3P, select one below : ";
                cbSyntax.Show();
            }
        }

        /// <summary>
        /// Changing theme
        /// </summary>
        private void CbApplicationOnSelectedIndexChanged(object sender, EventArgs eventArgs) {
            try {
                ThemeManager.Current = ThemeManager.GetThemesList()[cbApplication.SelectedIndex];
                ThemeManager.Current.AccentColor = ThemeManager.Current.ThemeAccentColor;
                Config.Instance.AccentColor = ThemeManager.Current.AccentColor;
                _checkButton.Checked = false;
            } catch (Exception x) {
                if (!(x is NullReferenceException))
                    ErrorHandler.Log(x.Message);
            } finally {
                Config.Instance.ThemeId = ThemeManager.Current.UniqueId;
                PlsRefresh();
            }
            
        }

        /// <summary>
        /// Changing syntax theme
        /// </summary>
        private void CbSyntaxSelectedIndexChanged(object sender, EventArgs eventArgs) {
            try {
                Style.CurrentTheme = Style.GetThemesList()[cbSyntax.SelectedIndex];
            } catch (Exception x) {
                ErrorHandler.Log(x.Message);
            } finally {
                Config.Instance.SyntaxHighlightThemeId = cbSyntax.SelectedIndex;
                if (Plug.IsCurrentFileProgress)
                    Style.SetSyntaxStyles();
            }
        }

        /// <summary>
        /// Changing accent Color
        /// </summary>
        private void NewColorPickerOnCheckedChanged(object sender, EventArgs eventArgs) {
            YamuiColorRadioButton rb = sender as YamuiColorRadioButton;
            if (rb != null && rb.Checked) {
                ThemeManager.Current.AccentColor = rb.BackColor;
                Config.Instance.AccentColor = ThemeManager.Current.AccentColor;
                _checkButton = rb;
                PlsRefresh();
            }
        }

        /// <summary>
        /// force all the html panel/label to refresh and try to refresh the main window
        /// </summary>
        private void PlsRefresh() {

            // Allows to refresh stuff corrrectly (mainly, it sets the baseCssData to null so it can be recomputed)
            ThemeManager.Current = ThemeManager.Current;

            var thisForm = FindForm();
            if (thisForm == null || Appli.Form == null)
                return;

            Style.SetGeneralStyles();

            // force the autocomplete to redraw
            AutoComplete.ForceClose();

            // force the dockable to redraw
            CodeExplorer.CodeExplorer.ApplyColorSettings();
            FileExplorer.FileExplorer.ApplyColorSettings();

            Application.DoEvents();
            thisForm.Refresh();
        }
    }
}

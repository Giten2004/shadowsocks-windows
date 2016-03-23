using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

using ZXing;
using ZXing.Common;
using ZXing.QrCode;

using Shadowsocks.Controller;
using Shadowsocks.Model;
using Shadowsocks.Properties;
using Shadowsocks.Util;

namespace Shadowsocks.View
{
    // yes this is just a menu view controller
    // when config form is closed, it moves away from RAM
    // and it should just do anything related to the config form
    public class MenuViewController
    {
        private ShadowsocksController _controller;
        private UpdateChecker _updateChecker;

        private NotifyIcon _notifyIcon;
        private ContextMenu _contextMenu1;

        private bool _isFirstRun;
        private bool _isStartupChecking;
        private MenuItem _enableSystemProxyItem;
        private MenuItem _systemProxyModeItem;
        private MenuItem _AutoStartupItem;
        private MenuItem _ShareOverLANItem;
        private MenuItem _SeperatorItem;
        private MenuItem _ConfigItem;
        private MenuItem _ServersItem;
        private MenuItem _globalModeItem;
        private MenuItem _PACModeItem;
        private MenuItem _localPACItem;
        private MenuItem _onlinePACItem;
        private MenuItem _editLocalPACItem;
        private MenuItem _updateFromGFWListItem;
        private MenuItem _editGFWUserRuleItem;
        private MenuItem _editOnlinePACItem;
        private MenuItem _autoCheckUpdatesToggleItem;
        private ConfigForm _configForm;
        private List<LogForm> _logForms = new List<LogForm>();
        private bool _logFormsVisible = false;
        private string _urlToOpen;

        public MenuViewController(ShadowsocksController controller)
        {
            _controller = controller;
            _controller.SystemProxyStatusChanged += controller_SystemProxyStatusChanged;
            _controller.ConfigChanged += controller_ConfigChanged;
            _controller.PACFileReadyToOpen += controller_FileReadyToOpen;
            _controller.UserRuleFileReadyToOpen += controller_FileReadyToOpen;
            _controller.ShareOverLANStatusChanged += controller_ShareOverLANStatusChanged;
            _controller.EnableGlobalChanged += controller_EnableGlobalChanged;
            _controller.Errored += controller_Errored;
            _controller.UpdatePACFromGFWListCompleted += controller_UpdatePACFromGFWListCompleted;
            _controller.UpdatePACFromGFWListError += controller_UpdatePACFromGFWListError;

            LoadMenu();

            _notifyIcon = new NotifyIcon();
            _notifyIcon.Visible = true;
            _notifyIcon.ContextMenu = _contextMenu1;
            _notifyIcon.BalloonTipClicked += _notifyIcon1_BalloonTipClicked;
            _notifyIcon.MouseClick += _notifyIcon1_Click;
            _notifyIcon.MouseDoubleClick += _notifyIcon1_DoubleClick;
            _notifyIcon.BalloonTipClosed += _notifyIcon_BalloonTipClosed;

            UpdateTrayIcon();

            _updateChecker = new UpdateChecker();
            _updateChecker.CheckUpdateCompleted += _updateChecker_CheckUpdateCompleted;

            LoadCurrentConfiguration();

            if (controller.Configuration.isDefault)
            {
                _isFirstRun = true;
                ShowConfigForm();
            }
            else if (controller.Configuration.autoCheckUpdate)
            {
                _isStartupChecking = true;
                _updateChecker.CheckUpdate(controller.Configuration, 3000);
            }
        }

        private void UpdateTrayIcon()
        {
            int dpi;
            Graphics graphics = Graphics.FromHwnd(IntPtr.Zero);
            dpi = (int)graphics.DpiX;
            graphics.Dispose();
            Bitmap icon = null;
            if (dpi < 97)
            {
                // dpi = 96;
                icon = Resources.ss16;
            }
            else if (dpi < 121)
            {
                // dpi = 120;
                icon = Resources.ss20;
            }
            else
            {
                icon = Resources.ss24;
            }
            Configuration config = _controller.Configuration;
            bool enabled = config.enabled;
            bool global = config.global;
            if (!enabled)
            {
                Bitmap iconCopy = new Bitmap(icon);
                for (int x = 0; x < iconCopy.Width; x++)
                {
                    for (int y = 0; y < iconCopy.Height; y++)
                    {
                        Color color = icon.GetPixel(x, y);
                        iconCopy.SetPixel(x, y, Color.FromArgb((byte)(color.A / 1.25), color.R, color.G, color.B));
                    }
                }
                icon = iconCopy;
            }
            _notifyIcon.Icon = Icon.FromHandle(icon.GetHicon());

            string serverInfo = null;
            if (_controller.GetCurrentStrategy() != null)
            {
                serverInfo = _controller.GetCurrentStrategy().Name;
            }
            else
            {
                serverInfo = config.GetCurrentServer().FriendlyName();
            }
            // we want to show more details but notify icon title is limited to 63 characters
            string text = I18N.GetString("Shadowsocks") + " " + UpdateChecker.Version + "\n" +
                (enabled ?
                    I18N.GetString("System Proxy On: ") + (global ? I18N.GetString("Global") : I18N.GetString("PAC")) :
                    String.Format(I18N.GetString("Running: Port {0}"), config.localPort))  // this feedback is very important because they need to know Shadowsocks is running
                + "\n" + serverInfo;
            _notifyIcon.Text = text.Substring(0, Math.Min(63, text.Length));
        }

        private MenuItem CreateMenuItem(string text, EventHandler click)
        {
            return new MenuItem(I18N.GetString(text), click);
        }

        private MenuItem CreateMenuGroup(string text, MenuItem[] items)
        {
            return new MenuItem(I18N.GetString(text), items);
        }

        private void LoadMenu()
        {
            this._contextMenu1 = new ContextMenu(new MenuItem[] {
                this._enableSystemProxyItem = CreateMenuItem("Enable System Proxy", new EventHandler(this.EnableSystemProxyItem_Click)),
                this._systemProxyModeItem = CreateMenuGroup("Mode", new MenuItem[] {
                    this._PACModeItem = CreateMenuItem("PAC", new EventHandler(this.PACModeItem_Click)),
                    this._globalModeItem = CreateMenuItem("Global", new EventHandler(this.GlobalModeItem_Click))
                }),
                this._ServersItem = CreateMenuGroup("Servers", new MenuItem[] {
                    this._SeperatorItem = new MenuItem("-"),
                    this._ConfigItem = CreateMenuItem("Edit Servers...", new EventHandler(this.Config_Click)),
                    CreateMenuItem("Statistics Config...", StatisticsConfigItem_Click),
                    CreateMenuItem("Show QRCode...", new EventHandler(this.QRCodeItem_Click)),
                    CreateMenuItem("Scan QRCode from Screen...", new EventHandler(this.ScanQRCodeItem_Click))
                }),
                CreateMenuGroup("PAC ", new MenuItem[] {
                    this._localPACItem = CreateMenuItem("Local PAC", new EventHandler(this.LocalPACItem_Click)),
                    this._onlinePACItem = CreateMenuItem("Online PAC", new EventHandler(this.OnlinePACItem_Click)),
                    new MenuItem("-"),
                    this._editLocalPACItem = CreateMenuItem("Edit Local PAC File...", new EventHandler(this.EditPACFileItem_Click)),
                    this._updateFromGFWListItem = CreateMenuItem("Update Local PAC from GFWList", new EventHandler(this.UpdatePACFromGFWListItem_Click)),
                    this._editGFWUserRuleItem = CreateMenuItem("Edit User Rule for GFWList...", new EventHandler(this.EditUserRuleFileForGFWListItem_Click)),
                    this._editOnlinePACItem = CreateMenuItem("Edit Online PAC URL...", new EventHandler(this.UpdateOnlinePACURLItem_Click)),
                }),
                new MenuItem("-"),
                this._AutoStartupItem = CreateMenuItem("Start on Boot", new EventHandler(this.AutoStartupItem_Click)),
                this._ShareOverLANItem = CreateMenuItem("Allow Clients from LAN", new EventHandler(this.ShareOverLANItem_Click)),
                new MenuItem("-"),
                CreateMenuItem("Show Logs...", new EventHandler(this.ShowLogItem_Click)),
                CreateMenuGroup("Updates...", new MenuItem[] {
                    CreateMenuItem("Check for Updates...", new EventHandler(this.checkUpdatesItem_Click)),
                    new MenuItem("-"),
                    this._autoCheckUpdatesToggleItem = CreateMenuItem("Check for Updates at Startup", new EventHandler(this.autoCheckUpdatesToggleItem_Click)),
                }),
                CreateMenuItem("About...", new EventHandler(this.AboutItem_Click)),
                new MenuItem("-"),
                CreateMenuItem("Quit", new EventHandler(this.Quit_Click))
            });
        }

        #region Event process of menu

        private void EnableSystemProxyItem_Click(object sender, EventArgs e)
        {
            _controller.ToggleSystemProxyEnable(!_enableSystemProxyItem.Checked);
        }

        private void GlobalModeItem_Click(object sender, EventArgs e)
        {
            _controller.ToggleGlobal(true);
        }

        private void PACModeItem_Click(object sender, EventArgs e)
        {
            _controller.ToggleGlobal(false);
        }

        private void ShareOverLANItem_Click(object sender, EventArgs e)
        {
            _ShareOverLANItem.Checked = !_ShareOverLANItem.Checked;
            _controller.ToggleShareOverLAN(_ShareOverLANItem.Checked);
        }

        private void EditPACFileItem_Click(object sender, EventArgs e)
        {
            _controller.TouchPACFile();
        }

        private void UpdatePACFromGFWListItem_Click(object sender, EventArgs e)
        {
            _controller.UpdatePACFromGFWList();
        }

        private void EditUserRuleFileForGFWListItem_Click(object sender, EventArgs e)
        {
            _controller.TouchUserRuleFile();
        }

        private void AServerItem_Click(object sender, EventArgs e)
        {
            MenuItem item = (MenuItem)sender;
            _controller.SelectServerIndex((int)item.Tag);
        }

        private void AStrategyItem_Click(object sender, EventArgs e)
        {
            MenuItem item = (MenuItem)sender;
            _controller.SelectStrategy((string)item.Tag);
        }

        private void ShowLogItem_Click(object sender, EventArgs e)
        {
            LogForm f = new LogForm(_controller, Logging.LogFilePath);
            f.Show();
            f.FormClosed += logForm_FormClosed;

            _logForms.Add(f);
        }

        private void StatisticsConfigItem_Click(object sender, EventArgs e)
        {
            StatisticsStrategyConfigurationForm form = new StatisticsStrategyConfigurationForm(_controller);
            form.Show();
        }

        private void QRCodeItem_Click(object sender, EventArgs e)
        {
            QRCodeForm qrCodeForm = new QRCodeForm(_controller.GetQRCodeForCurrentServer());
            //qrCodeForm.Icon = this.Icon;
            // TODO
            qrCodeForm.Show();
        }

        private void ScanQRCodeItem_Click(object sender, EventArgs e)
        {
            foreach (Screen screen in Screen.AllScreens)
            {
                using (Bitmap fullImage = new Bitmap(screen.Bounds.Width,
                                                screen.Bounds.Height))
                {
                    using (Graphics g = Graphics.FromImage(fullImage))
                    {
                        g.CopyFromScreen(screen.Bounds.X,
                                         screen.Bounds.Y,
                                         0, 0,
                                         fullImage.Size,
                                         CopyPixelOperation.SourceCopy);
                    }
                    int maxTry = 10;
                    for (int i = 0; i < maxTry; i++)
                    {
                        int marginLeft = (int)((double)fullImage.Width * i / 2.5 / maxTry);
                        int marginTop = (int)((double)fullImage.Height * i / 2.5 / maxTry);
                        Rectangle cropRect = new Rectangle(marginLeft, marginTop, fullImage.Width - marginLeft * 2, fullImage.Height - marginTop * 2);
                        Bitmap target = new Bitmap(screen.Bounds.Width, screen.Bounds.Height);

                        double imageScale = (double)screen.Bounds.Width / (double)cropRect.Width;
                        using (Graphics g = Graphics.FromImage(target))
                        {
                            g.DrawImage(fullImage, new Rectangle(0, 0, target.Width, target.Height),
                                            cropRect,
                                            GraphicsUnit.Pixel);
                        }
                        var source = new BitmapLuminanceSource(target);
                        var bitmap = new BinaryBitmap(new HybridBinarizer(source));
                        QRCodeReader reader = new QRCodeReader();
                        var result = reader.decode(bitmap);
                        if (result != null)
                        {
                            var success = _controller.AddServerBySSURL(result.Text);
                            QRCodeSplashForm splash = new QRCodeSplashForm();
                            if (success)
                            {
                                splash.FormClosed += splash_FormClosed;
                            }
                            else if (result.Text.StartsWith("http://") || result.Text.StartsWith("https://"))
                            {
                                _urlToOpen = result.Text;
                                splash.FormClosed += openURLFromQRCode;
                            }
                            else
                            {
                                MessageBox.Show(I18N.GetString("Failed to decode QRCode"));
                                return;
                            }
                            double minX = Int32.MaxValue, minY = Int32.MaxValue, maxX = 0, maxY = 0;
                            foreach (ResultPoint point in result.ResultPoints)
                            {
                                minX = Math.Min(minX, point.X);
                                minY = Math.Min(minY, point.Y);
                                maxX = Math.Max(maxX, point.X);
                                maxY = Math.Max(maxY, point.Y);
                            }
                            minX /= imageScale;
                            minY /= imageScale;
                            maxX /= imageScale;
                            maxY /= imageScale;
                            // make it 20% larger
                            double margin = (maxX - minX) * 0.20f;
                            minX += -margin + marginLeft;
                            maxX += margin + marginLeft;
                            minY += -margin + marginTop;
                            maxY += margin + marginTop;
                            splash.Location = new Point(screen.Bounds.X, screen.Bounds.Y);
                            // we need a panel because a window has a minimal size
                            // TODO: test on high DPI
                            splash.TargetRect = new Rectangle((int)minX + screen.Bounds.X, (int)minY + screen.Bounds.Y, (int)maxX - (int)minX, (int)maxY - (int)minY);
                            splash.Size = new Size(fullImage.Width, fullImage.Height);
                            splash.Show();
                            return;
                        }
                    }
                }
            }
            MessageBox.Show(I18N.GetString("No QRCode found. Try to zoom in or move it to the center of the screen."));
        }

        private void Config_Click(object sender, EventArgs e)
        {
            ShowConfigForm();
        }

        private void Quit_Click(object sender, EventArgs e)
        {
            _controller.Stop();
            _notifyIcon.Visible = false;

            Application.Exit();
        }

        private void AboutItem_Click(object sender, EventArgs e)
        {
            Process.Start("https://github.com/shadowsocks/shadowsocks-windows");
        }

        private void LocalPACItem_Click(object sender, EventArgs e)
        {
            if (!_localPACItem.Checked)
            {
                _localPACItem.Checked = true;
                _onlinePACItem.Checked = false;
                _controller.UseOnlinePAC(false);
                UpdatePACItemsEnabledStatus();
            }
        }

        private void OnlinePACItem_Click(object sender, EventArgs e)
        {
            if (!_onlinePACItem.Checked)
            {
                if (_controller.Configuration.pacUrl.IsNullOrEmpty())
                {
                    UpdateOnlinePACURLItem_Click(sender, e);
                }
                if (!_controller.Configuration.pacUrl.IsNullOrEmpty())
                {
                    _localPACItem.Checked = false;
                    _onlinePACItem.Checked = true;
                    _controller.UseOnlinePAC(true);
                }
                UpdatePACItemsEnabledStatus();
            }
        }

        private void UpdateOnlinePACURLItem_Click(object sender, EventArgs e)
        {
            string origPacUrl = _controller.Configuration.pacUrl;
            string pacUrl = Microsoft.VisualBasic.Interaction.InputBox(
                I18N.GetString("Please input PAC Url"),
                I18N.GetString("Edit Online PAC URL"),
                origPacUrl, -1, -1);
            if (!pacUrl.IsNullOrEmpty() && pacUrl != origPacUrl)
            {
                _controller.SavePACUrl(pacUrl);
            }
        }

        private void AutoStartupItem_Click(object sender, EventArgs e)
        {
            _AutoStartupItem.Checked = !_AutoStartupItem.Checked;
            if (!AutoStartup.Set(_AutoStartupItem.Checked))
            {
                MessageBox.Show(I18N.GetString("Failed to update registry"));
            }
        }

        private void checkUpdatesItem_Click(object sender, EventArgs e)
        {
            _updateChecker.CheckUpdate(_controller.Configuration, 1);
        }

        private void autoCheckUpdatesToggleItem_Click(object sender, EventArgs e)
        {
            _controller.ToggleCheckingUpdate(!_controller.Configuration.autoCheckUpdate);
            UpdateUpdateMenu();
        }

        #endregion

        #region ShadowsocksController Event process

        private void controller_ConfigChanged(object sender, EventArgs e)
        {
            LoadCurrentConfiguration();
            UpdateTrayIcon();
        }

        private void controller_SystemProxyStatusChanged(object sender, EventArgs e)
        {
            _enableSystemProxyItem.Checked = _controller.Configuration.enabled;
            _systemProxyModeItem.Enabled = _enableSystemProxyItem.Checked;
        }

        private void controller_ShareOverLANStatusChanged(object sender, EventArgs e)
        {
            _ShareOverLANItem.Checked = _controller.Configuration.shareOverLan;
        }

        private void controller_EnableGlobalChanged(object sender, EventArgs e)
        {
            _globalModeItem.Checked = _controller.Configuration.global;
            _PACModeItem.Checked = !_globalModeItem.Checked;
        }

        private void controller_FileReadyToOpen(object sender, PathEventArgs e)
        {
            string argument = @"/select, " + e.Path;

            System.Diagnostics.Process.Start("explorer.exe", argument);
        }

        private void controller_UpdatePACFromGFWListError(object sender, System.IO.ErrorEventArgs e)
        {
            ShowBalloonTip(I18N.GetString("Failed to update PAC file"), e.GetException().Message, ToolTipIcon.Error, 5000);
            Logging.LogUsefulException(e.GetException());
        }

        private void controller_UpdatePACFromGFWListCompleted(object sender, ResultEventArgs e)
        {
            string result = e.Success ? I18N.GetString("PAC updated") : I18N.GetString("No updates found. Please report to GFWList if you have problems with it.");
            ShowBalloonTip(I18N.GetString("Shadowsocks"), result, ToolTipIcon.Info, 1000);
        }

        private void controller_Errored(object sender, System.IO.ErrorEventArgs e)
        {
            MessageBox.Show(e.GetException().ToString(), String.Format(I18N.GetString("Shadowsocks Error: {0}"), e.GetException().Message));
        }

        #endregion

        #region _notifyIcon event process

        private void _notifyIcon1_BalloonTipClicked(object sender, EventArgs e)
        {
            if (_updateChecker.NewVersionFound)
            {
                _updateChecker.NewVersionFound = false; /* Reset the flag */
                if (System.IO.File.Exists(_updateChecker.LatestVersionLocalName))
                {
                    string argument = "/select, \"" + _updateChecker.LatestVersionLocalName + "\"";
                    System.Diagnostics.Process.Start("explorer.exe", argument);
                }
            }
        }

        private void _notifyIcon_BalloonTipClosed(object sender, EventArgs e)
        {
            if (_updateChecker.NewVersionFound)
            {
                _updateChecker.NewVersionFound = false; /* Reset the flag */
            }
        }

        private void _notifyIcon1_Click(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                // TODO: show something interesting
            }
            else if (e.Button == MouseButtons.Middle)
            {
                ShowLogForms();
            }
        }

        private void _notifyIcon1_DoubleClick(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                ShowConfigForm();
            }
        }

        #endregion

        private void _updateChecker_CheckUpdateCompleted(object sender, EventArgs e)
        {
            if (_updateChecker.NewVersionFound)
            {
                ShowBalloonTip(String.Format(I18N.GetString("Shadowsocks {0} Update Found"), _updateChecker.LatestVersionNumber), I18N.GetString("Click here to update"), ToolTipIcon.Info, 5000);
            }
            else if (!_isStartupChecking)
            {
                ShowBalloonTip(I18N.GetString("Shadowsocks"), I18N.GetString("No update is available"), ToolTipIcon.Info, 5000);
            }
            _isStartupChecking = false;
        }

        private void ShowBalloonTip(string title, string content, ToolTipIcon icon, int timeout)
        {
            _notifyIcon.BalloonTipTitle = title;
            _notifyIcon.BalloonTipText = content;
            _notifyIcon.BalloonTipIcon = icon;
            _notifyIcon.ShowBalloonTip(timeout);
        }

        private void LoadCurrentConfiguration()
        {
            Configuration config = _controller.Configuration;

            UpdateServersMenu();

            _enableSystemProxyItem.Checked = config.enabled;
            _systemProxyModeItem.Enabled = config.enabled;
            _globalModeItem.Checked = config.global;
            _PACModeItem.Checked = !config.global;
            _ShareOverLANItem.Checked = config.shareOverLan;
            _AutoStartupItem.Checked = AutoStartup.Check();
            _onlinePACItem.Checked = _onlinePACItem.Enabled && config.useOnlinePac;
            _localPACItem.Checked = !_onlinePACItem.Checked;

            UpdatePACItemsEnabledStatus();
            UpdateUpdateMenu();
        }

        private void UpdateServersMenu()
        {
            var items = _ServersItem.MenuItems;
            while (items[0] != _SeperatorItem)
            {
                items.RemoveAt(0);
            }

            int i = 0;
            foreach (var strategy in _controller.GetStrategies())
            {
                MenuItem item = new MenuItem(strategy.Name);
                item.Tag = strategy.ID;
                item.Click += AStrategyItem_Click;
                items.Add(i, item);
                i++;
            }

            int strategyCount = i;
            Configuration configuration = _controller.Configuration;
            foreach (var server in configuration.configs)
            {
                MenuItem item = new MenuItem(server.FriendlyName());
                item.Tag = i - strategyCount;
                item.Click += AServerItem_Click;
                items.Add(i, item);
                i++;
            }

            foreach (MenuItem item in items)
            {
                if (item.Tag != null && (item.Tag.ToString() == configuration.index.ToString() || item.Tag.ToString() == configuration.strategy))
                {
                    item.Checked = true;
                }
            }
        }

        private void ShowConfigForm()
        {
            if (_configForm != null)
            {
                _configForm.Activate();
            }
            else
            {
                _configForm = new ConfigForm(_controller);
                _configForm.Show();
                _configForm.FormClosed += configForm_FormClosed;
            }
        }

        private void ShowLogForms()
        {
            if (_logForms.Count == 0)
            {
                LogForm f = new LogForm(_controller, Logging.LogFilePath);
                f.Show();
                f.FormClosed += logForm_FormClosed;

                _logForms.Add(f);
                _logFormsVisible = true;
            }
            else
            {
                _logFormsVisible = !_logFormsVisible;
                foreach (LogForm f in _logForms)
                {
                    f.Visible = _logFormsVisible;
                }
            }
        }

        private void logForm_FormClosed(object sender, FormClosedEventArgs e)
        {
            _logForms.Remove((LogForm)sender);
        }

        private void configForm_FormClosed(object sender, FormClosedEventArgs e)
        {
            _configForm = null;
            Utils.ReleaseMemory(true);

            if (_isFirstRun)
            {
                CheckUpdateForFirstRun();
                ShowFirstTimeBalloon();
                _isFirstRun = false;
            }
        }

        private void CheckUpdateForFirstRun()
        {
            if (!_controller.Configuration.isDefault)
            {
                _isStartupChecking = true;
                _updateChecker.CheckUpdate(_controller.Configuration, 3000);
            }
        }

        private void ShowFirstTimeBalloon()
        {
            _notifyIcon.BalloonTipTitle = I18N.GetString("Shadowsocks is here");
            _notifyIcon.BalloonTipText = I18N.GetString("You can turn on/off Shadowsocks in the context menu");
            _notifyIcon.BalloonTipIcon = ToolTipIcon.Info;
            _notifyIcon.ShowBalloonTip(1000);
        }

        private void splash_FormClosed(object sender, FormClosedEventArgs e)
        {
            ShowConfigForm();
        }

        private void openURLFromQRCode(object sender, FormClosedEventArgs e)
        {
            Process.Start(_urlToOpen);
        }

        private void UpdatePACItemsEnabledStatus()
        {
            if (this._localPACItem.Checked)
            {
                this._editLocalPACItem.Enabled = true;
                this._updateFromGFWListItem.Enabled = true;
                this._editGFWUserRuleItem.Enabled = true;
                this._editOnlinePACItem.Enabled = false;
            }
            else
            {
                this._editLocalPACItem.Enabled = false;
                this._updateFromGFWListItem.Enabled = false;
                this._editGFWUserRuleItem.Enabled = false;
                this._editOnlinePACItem.Enabled = true;
            }
        }

        private void UpdateUpdateMenu()
        {
            _autoCheckUpdatesToggleItem.Checked = _controller.Configuration.autoCheckUpdate;
        }

    }
}

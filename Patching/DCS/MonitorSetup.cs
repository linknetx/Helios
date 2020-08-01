// Copyright 2020 Ammo Goettsch
// 
// Helios is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
// 
// Helios is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
// 
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <http://www.gnu.org/licenses/>.
// 

using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using System.Xml;
using GadrocsWorkshop.Helios.ComponentModel;
using GadrocsWorkshop.Helios.Interfaces.Capabilities;

// REVISIT missing feature: support explicit view ports for MAIN and UI
// REVISIT factor out ShadowModel (IShadowVisualParent) into field?
namespace GadrocsWorkshop.Helios.Patching.DCS
{
    /// <summary>
    /// This interface represents the capability of generating a MonitorSetup lua file for DCS.
    /// </summary>
    [HeliosInterface("Patching.DCS.MonitorSetup", "DCS Monitor Setup", typeof(MonitorSetupEditor),
        Factory = typeof(UniqueHeliosInterfaceFactory))]
    public class MonitorSetup : HeliosInterface, IReadyCheck, IStatusReportNotify,
        IShadowVisualParent, IInstallation, IExtendedDescription
    {
        #region Delegates

        /// <summary>
        /// fired when the bounds of some view model items may have changed, delayed to batch many changes
        /// </summary>
        public event EventHandler GeometryChangeDelayed;

        /// <summary>
        /// fired when the offset of the top left corner from Windows screen coordinates to
        /// DCS (0,0)-based coordinates has changed
        /// </summary>
        public event EventHandler<GlobalOffsetEventArgs> GlobalOffsetChanged;

        public event EventHandler<ShadowMonitorEventArgs> MonitorAdded;
        public event EventHandler<ShadowMonitorEventArgs> MonitorRemoved;
        public event EventHandler<ShadowViewportEventArgs> ViewportAdded;
        public event EventHandler<ShadowViewportEventArgs> ViewportRemoved;

        /// <summary>
        /// fired when new viewport configuration is calculated
        /// </summary>
        public event EventHandler<UpdatedViewportsEventArgs> UpdatedViewports;

        #endregion

        #region Constant

        internal const string DISPLAYS_SETTINGS_GROUP = "DCSMonitorSetup";
        internal const string PREFERENCES_SETTINGS_GROUP = "DCSMonitorSetupPreferences";

        #endregion

        #region Nested

        public class GlobalOffsetEventArgs : EventArgs
        {
            public GlobalOffsetEventArgs(Vector globalOffset)
            {
                GlobalOffset = globalOffset;
            }

            public Vector GlobalOffset { get; }
        }

        public class UpdatedViewportsEventArgs : EventArgs
        {
            public UpdatedViewportsEventArgs(ViewportSetupFile localViewports)
            {
                LocalViewports = localViewports;
            }

            public ViewportSetupFile LocalViewports { get; }
        }

        #endregion

        #region Private

        /// <summary>
        /// live inventory of our profile, indexed by monitor key
        /// </summary>
        private readonly Dictionary<string, ShadowMonitor> _monitors = new Dictionary<string, ShadowMonitor>();

        private readonly HashSet<IStatusReportObserver> _observers = new HashSet<IStatusReportObserver>();

        /// <summary>
        /// live inventory of our profile, indexed by source visual
        /// </summary>
        private readonly Dictionary<HeliosVisual, ShadowVisual> _viewports =
            new Dictionary<HeliosVisual, ShadowVisual>();

        private MonitorSetupGenerator _renderer;

        /// <summary>
        /// timer to delay execution of change in geometry because we sometimes receive thousands of events,
        /// such as on reset monitors
        /// </summary>
        private DispatcherTimer _geometryChangeTimer;


        /// <summary>
        /// scale used for the viewmodels
        /// </summary>
        private double _scale = 0.1;

        /// <summary>
        /// backing field for property CombinedMonitorSetupName, contains
        /// the name of the combined monitor setup that needs to be selected in DCS
        /// </summary>
        private string _combinedMonitorSetupName = "Helios";

        /// <summary>
        /// backing field for property CurrentProfileName, contains
        /// short name of profile currently being edited
        /// </summary>
        private string _currentProfileName;

        /// <summary>
        /// backing field for property GenerateCombined, contains
        /// true if this profile uses the combined monitor setup file
        /// </summary>
        private bool _generateCombined = true;

        /// <summary>
        /// backing field for property MonitorLayoutMode, contains
        /// the currently selected method for mapping the DCS resolution to the desktop
        /// </summary>
        private MonitorLayoutMode _monitorLayoutMode;

        /// <summary>
        /// backing field for property UsingViewportProvider, contains
        /// true if Helios IViewportProvider is the source of
        /// additional viewports, and false if external solution is used
        /// </summary>
        private bool _usingViewportProvider = true;

        /// <summary>
        /// backing field for property Rendered, contains
        /// The desktop rectangle (in Windows coordinates) that DCS will select for rendering, based on specifying its size as the "Resolution" parameter
        /// </summary>
        private Rect _rendered;

        /// <summary>
        /// if true, then we are currently processing a geometry change and making changes that could schedule
        /// further geometry change timer calls, so we should suppress those
        /// </summary>
        private bool _geometryChanging;

        #endregion

        public MonitorSetup()
            : base("DCS Monitor Setup")
        {
            // don't do any work here since we don't have a profile yet and we may just be
            // test instantiated for add interface dialog
        }

        public static Rect VisualToRect(HeliosVisual visual) =>
            new Rect(visual.Left, visual.Top, visual.Width, visual.Height);

        protected override void AttachToProfileOnMainThread()
        {
            base.AttachToProfileOnMainThread();

            // read persistent config
            _monitorLayoutMode = ConfigManager.SettingsManager.LoadSetting(PREFERENCES_SETTINGS_GROUP,
                "MonitorLayoutMode", MonitorLayoutMode.FromTopLeftCorner);

            // real initialization, not just a test instantiation
            Combined.Initialize();
            CurrentProfileName = string.IsNullOrWhiteSpace(Profile.Path) ? null : Profile.Name;
            _geometryChangeTimer = new DispatcherTimer(
                TimeSpan.FromMilliseconds(100),
                DispatcherPriority.Normal,
                OnDelayedGeometryChange,
                Application.Current.Dispatcher)
            {
                IsEnabled = false
            };
            _renderer = new MonitorSetupGenerator(this);

            // recursively walk profile and track every visual
            foreach (Monitor monitor in Profile.Monitors)
            {
                AddMonitor(monitor);
            }

            UpdateAllGeometry();

            // register for changes
            Profile.Monitors.CollectionChanged += Monitors_CollectionChanged;
            Profile.PropertyChanged += Profile_PropertyChanged;
        }

        protected override void DetachFromProfileOnMainThread(HeliosProfile oldProfile)
        {
            // stop updating shadow collections
            oldProfile.Monitors.CollectionChanged -= Monitors_CollectionChanged;
            oldProfile.PropertyChanged -= Profile_PropertyChanged;

            base.DetachFromProfileOnMainThread(oldProfile);

            // deallocate timer we allocate on Attach
            _geometryChangeTimer?.Stop();
            _geometryChangeTimer = null;

            // deallocate renderer we allocate on Attach
            _renderer?.Dispose();
            _renderer = null;

            // clean up shadow collections
            foreach (ShadowMonitor shadow in _monitors.Values)
            {
                shadow.Dispose();
            }
            foreach (ShadowVisual shadow in _viewports.Values)
            {
                shadow.Dispose();
            }
            _monitors.Clear();
            _viewports.Clear();
        }

        private void AddMonitor(Monitor monitor)
        {
            ShadowMonitor shadow = new ShadowMonitor(this, monitor);
            _monitors[shadow.Key] = shadow;

            // now that we can find the monitor in our index, we can safely add viewports
            // and other children
            shadow.Instrument();
            shadow.KeyChanged += Shadow_KeyChanged;
            shadow.PropertyChanged += Shadow_PropertyChanged;
            shadow.MonitorChanged += Raw_MonitorChanged;

            MonitorAdded?.Invoke(this, new ShadowMonitorEventArgs(shadow));
        }

        private void RemoveMonitor(Monitor monitor)
        {
            string key = ShadowMonitor.CreateKey(monitor);
            ShadowMonitor shadow = _monitors[key];
            shadow.MonitorChanged -= Raw_MonitorChanged;
            MonitorRemoved?.Invoke(this, new ShadowMonitorEventArgs(shadow));
            _monitors.Remove(key);
            shadow.Dispose();
        }

        public void AddViewport(ShadowVisual viewport)
        {
            _viewports[viewport.Visual] = viewport;
            ViewportAdded?.Invoke(this, new ShadowViewportEventArgs(viewport));

            // update viewport count on hosting monitor
            string monitorKey = ShadowMonitor.CreateKey(viewport.Monitor);
            ShadowMonitor monitor = _monitors[monitorKey];
            monitor.AddViewport();

            viewport.ViewportChanged += Raw_ViewportChanged;

            // recalculate, delayed
            ScheduleGeometryChange();
        }

        public void RemoveViewport(ShadowVisual viewport)
        {
            viewport.ViewportChanged -= Raw_ViewportChanged;
            _viewports.Remove(viewport.Visual);
            ViewportRemoved?.Invoke(this, new ShadowViewportEventArgs(viewport));

            // update viewport count on hosting monitor
            string monitorKey = ShadowMonitor.CreateKey(viewport.Monitor);
            ShadowMonitor monitor = _monitors[monitorKey];
            monitor.RemoveViewport();

            // recalculate, delayed
            ScheduleGeometryChange();
        }

        private void Profile_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(Profile.Name):
                    // WARNING: Path is changed before Name is set, so don't do this update on the change of "Path"
                    CurrentProfileName = string.IsNullOrWhiteSpace(Profile.Path) ? null : Profile.Name;
                    break;
                case nameof(Profile.Path):
                    InvalidateStatusReport();
                    break;
            }
        }

        /// <summary>
        /// called when a configuration property on our model has changed
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Shadow_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            // configuration of our shadow object changed by UI, need to re-evaluate
            // configuration state
            ScheduleGeometryChange();
        }

        /// <summary>
        /// called when a monitor has changed enough to require a new
        /// key (currently this happens every time the dimensions are changed because
        /// the dimensions are the key)
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Shadow_KeyChanged(object sender, ShadowMonitor.KeyChangeEventArgs e)
        {
            ShadowMonitor renamed = _monitors[e.OldKey];
            _monitors.Remove(e.OldKey);
            if (_monitors.ContainsKey(e.NewKey))
            {
                throw new Exception("logic error: collapsed one monitor's records into another during reset; please file a bug");
            }
            _monitors[e.NewKey] = renamed;
        }

        /// <summary>
        /// called when the viewport dimensions are modified
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Raw_ViewportChanged(object sender, RawViewportEventArgs e)
        {
            _ = sender;
            _ = e;
            ScheduleGeometryChange();
        }

        /// <summary>
        /// called when the monitor dimensions are modified
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Raw_MonitorChanged(object sender, RawMonitorEventArgs e)
        {
            _ = sender;
            _ = e;
            UpdateGlobalOffset();
            ScheduleGeometryChange();
        }

        /// <summary>
        /// called when the collection of monitors is changed after we initially instrumented everything,
        /// such as during reset monitors
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Monitors_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.NewItems != null)
            {
                foreach (Monitor monitor in e.NewItems)
                {
                    AddMonitor(monitor);
                }
            }

            if (e.OldItems != null)
            {
                foreach (Monitor monitor in e.OldItems)
                {
                    RemoveMonitor(monitor);
                }
            }

            ScheduleGeometryChange();
        }

        /// <summary>
        /// find the offset between windows coordinates and the 0,0 coordinate system used by DCS
        /// </summary>
        private void UpdateGlobalOffset()
        {
            double globalX = -_monitors.Values.Select(m => m.Visual.Left).Min<double>();
            double globalY = -_monitors.Values.Select(m => m.Visual.Top).Min<double>();
            GlobalOffset = new Vector(globalX, globalY);
            ConfigManager.LogManager.LogDebug(
                $"new top left corner offset to translate from windows coordinates to DCS coordinates is {GlobalOffset}");

            // push this value to all viewports and monitors
            GlobalOffsetChanged?.Invoke(this, new GlobalOffsetEventArgs(GlobalOffset));
        }

        /// <summary>
        /// find the minimum required DCS resolution to contain all the configured content
        /// </summary>
        private void UpdateRenderedRectangle()
        {
            // NOTE: we need this for status reporting, so this can't be in view model only
            IList<ShadowMonitor> included = _monitors.Values.Where(m => m.Included).ToList();
            Point topLeft;
            Point bottomRight;
            switch (_monitorLayoutMode)
            {
                case MonitorLayoutMode.FromTopLeftCorner:
                    bottomRight = new Point(
                        included.Select(m => m.Visual.Left + m.Visual.Width).Max<double>(),
                        included.Select(m => m.Visual.Top + m.Visual.Height).Max<double>());
                    topLeft = new Point(0, 0);
                    topLeft -= GlobalOffset;
                    break;
                case MonitorLayoutMode.Column:
                {
                    Monitor primary = GetPrimaryMonitor();
                    if (primary == null)
                    {
                        // transient state during reset monitors can end up here
                        return;
                    }
                    bottomRight = new Point(
                        primary.Right,
                        included.Select(m => m.Visual.Top + m.Visual.Height).Max<double>());
                    topLeft = new Point(
                        primary.Left,
                        // NOTE: DCS will position at the minimum y coordinate, even if it doesn't end up using that monitor
                        _monitors.Values.Select(m => m.Visual.Top).Min<double>());
                    break;
                }
                case MonitorLayoutMode.Row:
                {
                    Monitor primary = GetPrimaryMonitor();
                    if (primary == null)
                    {
                        // transient state during reset monitors can end up here
                        return;
                    }
                    bottomRight = new Point(
                        included.Select(m => m.Visual.Left + m.Visual.Width).Max<double>(),
                        primary.Bottom);
                    topLeft = new Point(
                        // NOTE: DCS will position at the minimum x coordinate, even if it doesn't end up using that monitor
                        _monitors.Values.Select(m => m.Visual.Left).Min<double>(),
                        primary.Top);
                    break;
                }
                case MonitorLayoutMode.PrimaryOnly:
                {
                    Monitor primary = GetPrimaryMonitor();
                    if (primary == null)
                    {
                        // transient state during reset monitors can end up here
                        return;
                    }
                    bottomRight = new Point(primary.Right, primary.Bottom);
                    topLeft = new Point(primary.Left, primary.Top);
                    break;
                }
                default:
                    throw new ArgumentOutOfRangeException();
            }

            Rendered = new Rect(topLeft, bottomRight);
        }

        private Monitor GetPrimaryMonitor()
        {
            return _monitors.Values.Select(shadow => shadow.Monitor).FirstOrDefault(m => m.IsPrimaryDisplay);
        }

        /// <summary>
        /// if we somehow end up with no main view, select one
        /// this also happens when we start fresh
        /// </summary>
        private void AutoSelectMainView()
        {
            // REVISIT add support for explicit main and UI view viewports, which will require updates to the editor also
            if (_monitors.Values.Any(m => m.Main))
            {
                // got at least one
                return;
            }

            // select any monitors with minimal viewports (might not be entirely empty)
            int min = _monitors.Values
                .Select(m => m.ViewportCount)
                .Min<int>();
            foreach (ShadowMonitor monitor in _monitors.Values.Where(m => m.ViewportCount == min))
            {
                monitor.Main = true;
            }
        }

        /// <summary>
        /// if we somehow end up with no UI view, select one
        /// this also happens when we start fresh
        /// </summary>
        private void AutoSelectUserInterfaceView()
        {
            // REVISIT add support for explicit main and UI view viewports, which will require updates to the editor also
            if (_monitors.Values.Any(m => m.UserInterface))
            {
                // got at least one
                return;
            }

            // select any monitors with minimal viewports (might not be entirely empty)
            int minViewports = _monitors.Values
                .Select(m => m.ViewportCount)
                .Min<int>();
            IList<ShadowMonitor> sortedMinPopulated = _monitors.Values
                .Where(m => m.ViewportCount == minViewports)
                .OrderBy(m => m.Monitor.Left)
                .ToList();
            double maxSize = sortedMinPopulated.Select(m => m.Monitor.Width).Max<double>();
            ShadowMonitor ui = sortedMinPopulated.First(m => Math.Abs(m.Monitor.Width - maxSize) < 0.001);
            ui.UserInterface = true;
        }

        private void ScheduleGeometryChange()
        {
            if (_geometryChanging)
            {
                // we are the source of whatever change caused us to call this, don't recurse
                return;
            }

            // eat all events for a short duration and process only once in case there are a lot of updates
            _geometryChangeTimer?.Start();
        }

        public override void ReadXml(XmlReader reader)
        {
            TypeConverter bc = TypeDescriptor.GetConverter(typeof(bool));
            while (reader.NodeType == XmlNodeType.Element)
            {
                switch (reader.Name)
                {
                    case "GenerateCombined":
                    {
                        string text = reader.ReadElementString("GenerateCombined");
                        _generateCombined = bc.ConvertFromInvariantString(text) as bool? ?? true;
                        break;
                    }
                    case "UsingViewportProvider":
                    {
                        string text = reader.ReadElementString("UsingViewportProvider");
                        _usingViewportProvider = bc.ConvertFromInvariantString(text) as bool? ?? true;
                        break;
                    }
                    default:
                    {
                        // ignore unsupported settings
                        string elementName = reader.Name;
                        string discard = reader.ReadElementString(elementName);
                            ConfigManager.LogManager.LogWarning(
                            $"Ignored unsupported {GetType().Name} setting '{elementName}' with value '{discard}'");
                        break;
                    }
                }
            }
        }

        public override void WriteXml(XmlWriter writer)
        {
            TypeConverter bc = TypeDescriptor.GetConverter(typeof(bool));
            if (!_generateCombined)
            {
                writer.WriteElementString("GenerateCombined", bc.ConvertToInvariantString(false));
            }

            if (!_usingViewportProvider)
            {
                writer.WriteElementString("UsingViewportProvider", bc.ConvertToInvariantString(false));
            }
        }

        /// <summary>
        /// react to a batch of changes to the viewport and monitor geometries
        /// </summary>
        private void OnDelayedGeometryChange(object sender, EventArgs e)
        {
            // recursion prevention flag
            _geometryChanging = true;
            try
            {
                _geometryChangeTimer?.Stop();
                if (Profile == null)
                {
                    // although we turn off the timer when we are removed from the profile, this call is still
                    // delivered late
                    return;
                }

                UpdateAllGeometry();
            }
            finally
            {
                _geometryChanging = false;
            }

            // notify anyone configuring or monitoring us
            GeometryChangeDelayed?.Invoke(this, EventArgs.Empty);

            // also send new status report
            InvalidateStatusReport();
        }

        /// <summary>
        /// check monitor settings and clean up any abandoned ones
        /// this is necessary because when we run with a profile that has not been reset,
        /// we end up with settings for monitors that don't actually exist.  Also, actual
        /// changes to our windows monitors will abandon settings for monitors that have
        /// changed locations or have been removed.
        /// </summary>
        private void CheckMonitorSettings()
        {
            if (!Profile.IsValidMonitorLayout)
            {
                // hasn't been reset yet, these monitors aren't the ones will will use
                return;
            }

            // get the list of real monitors and all their serialized settings names
            HashSet<string> displayKeys = new HashSet<string>();
            foreach (Monitor monitor in ConfigManager.DisplayManager.Displays)
            {
                displayKeys.UnionWith(ShadowMonitor.GetAllKeys(monitor));
            }

            if (!(ConfigManager.SettingsManager is ISettingsManager2 settings))
            {
                return;
            }

            // get the list of configured monitors in the settings file
            List<string> stableKeys = settings.EnumerateSettingNames(DISPLAYS_SETTINGS_GROUP).ToList();

            if (!ConfigManager.Application.SettingsAreWritable)
            {
                // this is a readonly application, let another application (i.e. Profile Editor) clean this up
                return;
            }

            // clean up settings for monitors that are not existing any more or are phantoms from unreset profiles
            foreach (string key in stableKeys.Where(key => !displayKeys.Contains(key)))
            {
                ConfigManager.LogManager.LogDebug($"removed setting '{key}' that does not refer to a real monitor");
                settings.DeleteSetting(DISPLAYS_SETTINGS_GROUP, key);
            }
        }

        /// <summary>
        /// Calculate a key/hash that identifies a set of monitors and their assignment to
        /// Main and UI views.  Any change to the monitor setup that is not an extra viewport
        /// will change this value.
        /// </summary>
        private string CalculateMonitorLayoutKey()
        {
            if (!Profile.IsValidMonitorLayout)
            {
                return null;
            }

            IEnumerable<string> keys = Monitors
                .Where(m => m.Included)
                .OrderBy(m => m.Monitor.Left)
                .ThenBy(m => m.Monitor.Top)
                .Select(CalculateMonitorKey)
                .Append(_monitorLayoutMode.ToString());

            return string.Join(", ", keys);
        }

        private static string CalculateMonitorKey(ShadowMonitor shadow)
        {
            string main = shadow.Main ? " MAIN" : "";
            string ui = shadow.UserInterface ? " UI" : "";
            return
                $"{shadow.Monitor.Left} {shadow.Monitor.Top} {shadow.Monitor.Width} {shadow.Monitor.Height}{main}{ui}";
        }

        private void UpdateAllGeometry()
        {
            CheckMonitorSettings();
            AutoSelectMainView();
            AutoSelectUserInterfaceView();
            UpdateGlobalOffset();
            EnsureValidMonitorSelections();
            UpdateRenderedRectangle();
            MonitorLayoutKey = CalculateMonitorLayoutKey();
        }

        private void EnsureValidMonitorSelections()
        {
            // in case of changes to the monitor layout mode, scan all monitors and make sure our selections are valid
            switch (_monitorLayoutMode)
            {
                case MonitorLayoutMode.FromTopLeftCorner:
                    EnsureTopLeftLayout();
                    break;
                case MonitorLayoutMode.Column:
                    EnsureColumnLayout();
                    break;
                case MonitorLayoutMode.Row:
                    EnsureRowLayout();
                    break;
                case MonitorLayoutMode.PrimaryOnly:
                    EnsurePrimaryLayout();
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        private void EnsureTopLeftLayout()
        {
            // NOTE: always legal, just reset defaults
            foreach (ShadowMonitor shadow in _monitors.Values)
            {
                shadow.Included = shadow.Included || shadow.HasContent;
                shadow.Permissions = ShadowMonitor.PermissionsFlags.CanInclude | ShadowMonitor.PermissionsFlags.CanExclude;
            }
        }

        private void EnsureRowLayout()
        {
            Monitor primary = GetPrimaryMonitor();
            if (primary == null)
            {
                // transient state during reset monitors can end up here
                return;
            }
            // WARNING: Rect does not understand rectangles that extend to negative infinity
            Rect allowed = new Rect(
                new Point(double.MinValue, primary.DisplayRectangle.Top),
                new Point(double.MaxValue, primary.DisplayRectangle.Bottom));
            foreach (ShadowMonitor shadow in _monitors.Values)
            {
                Rect intersection = shadow.Monitor.DisplayRectangle;
                intersection.Intersect(allowed);
                // WARNING: IntersectsWith is true even if the intersection is a touch line of 0 height, so we can't use it
                if (intersection.Height >= 1d)
                {
                    if (shadow.Monitor.Left <= primary.Left)
                    {
                        // cannot exclude main and area to the left of main
                        shadow.Included = true;
                        shadow.Permissions = ShadowMonitor.PermissionsFlags.CanInclude;
                    }
                    else
                    {
                        shadow.Included = shadow.Included || shadow.HasContent;
                        shadow.Permissions = ShadowMonitor.PermissionsFlags.CanInclude | ShadowMonitor.PermissionsFlags.CanExclude;
                    }
                }
                else
                {
                    // monitor is not part of row
                    shadow.Lockout();
                }
            }
        }

        private void EnsureColumnLayout()
        {
            Monitor primary = GetPrimaryMonitor();
            if (primary == null)
            {
                // transient state during reset monitors can end up here
                return;
            }
            // WARNING: Rect does not understand rectangles that extend to negative infinity
            Rect allowed = new Rect(
                new Point(primary.DisplayRectangle.Left, double.MinValue),
                new Point(primary.DisplayRectangle.Right, double.MaxValue));
            foreach (ShadowMonitor shadow in _monitors.Values)
            {
                Rect intersection = shadow.Monitor.DisplayRectangle;
                intersection.Intersect(allowed);
                // WARNING: IntersectsWith is true even if the intersection is a touch line of 0 width, so we can't use it
                if (intersection.Width >= 1d)
                {
                    if (shadow.Monitor.Top <= primary.Monitor.Top)
                    {
                        // cannot exclude main and area above main
                        shadow.Included = true;
                        shadow.Permissions = ShadowMonitor.PermissionsFlags.CanInclude;
                    }
                    else
                    {
                        shadow.Included = shadow.Included || shadow.HasContent;
                        shadow.Permissions = ShadowMonitor.PermissionsFlags.CanInclude | ShadowMonitor.PermissionsFlags.CanExclude;
                    }
                }
                else
                {
                    // monitor is not part of column
                    shadow.Lockout();
                }
            }
        }


        private void EnsurePrimaryLayout()
        {
            foreach (ShadowMonitor shadow in _monitors.Values)
            {
                if (shadow.Monitor.IsPrimaryDisplay)
                {
                    // cannot exclude main
                    shadow.Included = true;
                    shadow.Permissions = ShadowMonitor.PermissionsFlags.CanInclude;
                }
                else
                {
                    // monitor is not allowed
                    shadow.Lockout();
                }
            }
        }

        #region Properties

        /// <summary>
        /// true if this profile uses the combined monitor setup file
        /// </summary>
        public bool GenerateCombined
        {
            get => _generateCombined;
            set
            {
                if (_generateCombined == value)
                {
                    return;
                }

                bool oldValue = _generateCombined;
                _generateCombined = value;
                OnPropertyChanged("GenerateCombined", oldValue, value, true);
            }
        }

        /// <summary>
        /// functionality supporting combined monitor setups
        /// </summary>
        internal CombinedMonitorSetup Combined { get; } = new CombinedMonitorSetup();

        internal IEnumerable<ShadowMonitor> Monitors => _monitors.Values;

        internal IEnumerable<ShadowVisual> Viewports => _viewports.Values;

        /// <summary>
        /// The desktop rectangle (in Windows coordinates) that DCS will select for rendering, based on specifying its size as the "Resolution" parameter, not persisted
        /// </summary>
        public Rect Rendered
        {
            get => _rendered;
            set
            {
                if (_rendered == value) return;
                Rect oldValue = _rendered;
                _rendered = value;
                OnPropertyChanged("Rendered", oldValue, value, false);
            }
        }

        /// <summary>
        /// the name of the combined monitor setup that needs to be selected in DCS
        /// </summary>
        public string CombinedMonitorSetupName
        {
            get => _combinedMonitorSetupName;
            set
            {
                if (_combinedMonitorSetupName != null && _combinedMonitorSetupName == value)
                {
                    return;
                }

                string oldValue = _combinedMonitorSetupName;
                _combinedMonitorSetupName = value;
                OnPropertyChanged("CombinedMonitorSetupName", oldValue, value, true);
            }
        }

        /// <summary>
        /// short name of profile currently being edited
        /// </summary>
        public string CurrentProfileName
        {
            get => _currentProfileName;
            set
            {
                if (_currentProfileName != null && _currentProfileName == value)
                {
                    return;
                }

                string oldValue = _currentProfileName;
                _currentProfileName = value;
                OnPropertyChanged("CurrentProfileName", oldValue, value, true);
            }
        }

        internal string MonitorLayoutKey { get; private set; }

        /// <summary>
        /// true if Helios IViewportProvider is the source of
        /// additional viewports, and false if external solution is used
        /// </summary>
        public bool UsingViewportProvider
        {
            get => _usingViewportProvider;
            set
            {
                if (_usingViewportProvider == value)
                {
                    return;
                }

                bool oldValue = _usingViewportProvider;
                _usingViewportProvider = value;
                OnPropertyChanged("UsingViewportProvider", oldValue, value, true);
            }
        }


        /// <summary>
        /// the currently selected method for mapping the DCS resolution to the desktop, global setting, so no Undo support
        /// </summary>
        public MonitorLayoutMode MonitorLayoutMode
        {
            get => _monitorLayoutMode;
            set
            {
                if (_monitorLayoutMode == value)
                {
                    return;
                }

                MonitorLayoutMode oldValue = _monitorLayoutMode;
                _monitorLayoutMode = value;
                ConfigManager.SettingsManager.SaveSetting(PREFERENCES_SETTINGS_GROUP, "MonitorLayoutMode", _monitorLayoutMode);
                ScheduleGeometryChange();
                OnPropertyChanged("MonitorLayoutMode", oldValue, value, false);
            }
        }

        internal MonitorSetupGenerator Renderer => _renderer;

        #endregion

        #region IExtendedDescription

        public string Description =>
            "Utility interface that writes a DCS MonitorSetup Lua file to configure screen layout for the current profile.";

        public string RemovalNarrative =>
            "Delete this interface to no longer let Helios manage the monitor setup file for this profile";

        #endregion

        #region IInstallation

        // all we have right now is the monitor setup file generator
        public InstallationResult Install(IInstallationCallbacks callbacks) => _renderer.Install(callbacks);

        #endregion

        #region IReadyCheck

        // all we have right now is the monitor setup file generator
        public IEnumerable<StatusReportItem> PerformReadyCheck() => _renderer.PerformReadyCheck();

        #endregion

        #region IShadowVisualParent

        public double Scale
        {
            get => _scale;
            set
            {
                double oldValue = _scale;
                if (Math.Abs(oldValue - value) < 0.001)
                {
                    return;
                }

                _scale = value;
                OnPropertyChanged("Scale", oldValue, value, true);
            }
        }

        public Vector GlobalOffset { get; private set; }

        #endregion

        #region IStatusReportNotify

        public void Subscribe(IStatusReportObserver observer)
        {
            _observers.Add(observer);
        }

        public void Unsubscribe(IStatusReportObserver observer)
        {
            _observers.Remove(observer);
        }

        public void InvalidateStatusReport()
        {
            if (_observers.Count < 1)
            {
                return;
            }

            // actually enumerate the report now and store it
            List<StatusReportItem> newReport = _renderer.PerformReadyCheck().ToList();

            // send newly calculated viewports data to any observers (such as combined monitor setup view model)
            UpdatedViewports?.Invoke(this, new UpdatedViewportsEventArgs(_renderer.LocalViewports));

            // publish report
            PublishStatusReport(newReport);
        }

        public void PublishStatusReport(IList<StatusReportItem> statusReport)
        {
            foreach (IStatusReportObserver observer in _observers)
            {
                observer.ReceiveStatusReport(Name,
                    Description,
                    statusReport);
            }
        }

        public string StatusName => Name;

        #endregion
    }
}
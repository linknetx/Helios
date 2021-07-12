﻿//  Copyright 2014 Craig Courtney
//    
//  Helios is free software: you can redistribute it and/or modify
//  it under the terms of the GNU General Public License as published by
//  the Free Software Foundation, either version 3 of the License, or
//  (at your option) any later version.
//
//  Helios is distributed in the hope that it will be useful,
//  but WITHOUT ANY WARRANTY; without even the implied warranty of
//  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
//  GNU General Public License for more details.
//
//  You should have received a copy of the GNU General Public License
//  along with this program.  If not, see <http://www.gnu.org/licenses/>.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Xml;
using GadrocsWorkshop.Helios.ComponentModel;
using GadrocsWorkshop.Helios.Interfaces.Capabilities;
using Microsoft.Win32;

namespace GadrocsWorkshop.Helios.Interfaces.Falcon
{
    [HeliosInterface("Helios.Falcon.Interface", "Falcon", typeof(FalconIntefaceEditor), typeof(UniqueHeliosInterfaceFactory))]
    public class FalconInterface : HeliosInterface, IReadyCheck, IStatusReportNotify, IExtendedDescription
    {
        const string falconRootKey = @"SOFTWARE\WOW6432Node\Benchmark Sims\";
        private FalconTypes _falconType;
        private string _falconPath;
        private string _currentTheater;
        private string _pilotCallsign;
        private string _keyFile;
        private string _cockpitDatFile;
        private bool _focusAssist;
        private string _falconVersion;
        private string[] _falconVersions;
        private Version _falconProfileVersion;
        private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();
        private FalconDataExporter _dataExporter;
        private FalconKeyFile _callbacks = new FalconKeyFile("");
        private bool _forceKeyFile;
        private readonly HashSet<IStatusReportObserver> _observers = new HashSet<IStatusReportObserver>();
        public FalconInterface()
            : base("Falcon")
        {
            FalconType = FalconTypes.BMS;
            _falconVersions = GetFalconVersions();
            _falconPath = GetFalconPath();
            _currentTheater = GetCurrentTheater();
            _pilotCallsign = GetpilotCallsign();
            
            _dataExporter = new BMS.BMSFalconDataExporter(this);

            HeliosAction sendAction = new HeliosAction(this, "", "callback", "send", "Press and releases a keyboard callback for falcon.", "Callback name", BindingValueUnits.Text)
            {
                ActionBindingDescription = "send %value% callback for falcon.",
                ActionInputBindingDescription = "send %value% callback",
                ValueEditorType = typeof(FalconCallbackValueEditor)
            };
            sendAction.Execute += new HeliosActionHandler(SendAction_Execute);
            Actions.Add(sendAction);

            HeliosAction pressAction = new HeliosAction(this, "", "callback", "press", "Press a keyboard callback for falcon and leave it pressed.", "Callback name", BindingValueUnits.Text)
            {
                ActionBindingDescription = "press %value% callback for falcon.",
                ActionInputBindingDescription = "press %value% callback",
                ValueEditorType = typeof(FalconCallbackValueEditor)
            };
            pressAction.Execute += new HeliosActionHandler(PressAction_Execute);
            Actions.Add(pressAction);

            HeliosAction releaseAction = new HeliosAction(this, "", "callback", "release", "Releases a previously pressed keyboard callback for falcon.", "Callback name", BindingValueUnits.Text)
            {
                ActionBindingDescription = "release %value% callback for falcon.",
                ActionInputBindingDescription = "release %value% callback",
                ValueEditorType = typeof(FalconCallbackValueEditor)
            };
            releaseAction.Execute += new HeliosActionHandler(ReleaseAction_Execute);
            Actions.Add(releaseAction);
        }

        #region Properties

        public string CurrentTheater { get { return _currentTheater; } }

        public string PilotCallsign { get { return _pilotCallsign; } }
        
        public bool ForceKeyFile
        {
            get { return _forceKeyFile; }
            set
            {
                var oldValue = _forceKeyFile;
                _forceKeyFile = value;
                OnPropertyChanged("ForceKeyFile", oldValue, value, true);
            }
        }
        public bool FocusAssist
        {
            get { return _focusAssist; }
            set
            {
                var oldValue = _focusAssist;
                _focusAssist = value;
                OnPropertyChanged("FocusAssist", oldValue, value, true);
            }
        }

        public string[] FalconVersions
        {
            get
            {
                return _falconVersions;
            }
        }

        public string FalconVersion
        {
            get
            {
                if(_falconVersion == null && _falconVersions != null)
                {
                    _falconVersion = _falconVersions[0];
                }
                return _falconVersion;
            }
            set
            {
                if(_falconVersion == null && value != null ||
                    _falconVersion != null && !_falconVersion.Equals(value))
                {
                    string oldValue = _falconVersion;
                    _falconVersion = value;
                    OnPropertyChanged("FalconVersion", oldValue, value, false);
                    FalconProfileVersion = ParseProfileVersion(_falconVersion);
                    FalconPath = GetFalconPath();
                }
            }
        }

        public Version FalconProfileVersion
        {
            get
            {
                return _falconProfileVersion;
            }
            set
            {
                if (_falconProfileVersion == null && value != null ||
                    _falconProfileVersion != null && !_falconProfileVersion.Equals(value))
                {
                    Version oldValue = _falconProfileVersion;
                    _falconProfileVersion = value;
                    OnPropertyChanged("FalconVersion", oldValue, value, false);
                }
            }
        }

        public FalconTypes FalconType
        {
            get
            {
                return _falconType;
            }
            set
            {
                if (!_falconType.Equals(value))
                {
                    FalconTypes oldValue = _falconType;
                    if (_dataExporter != null)
                    {
                        _dataExporter.RemoveExportData(this);
                    }

                    _falconType = value;
                    _falconPath = null;

                    switch (_falconType)
                    {
                        case FalconTypes.BMS:
                        default:
                            _dataExporter = new BMS.BMSFalconDataExporter(this);
                            KeyFileName = System.IO.Path.Combine(FalconPath, "User\\Config\\BMS - Full.key");
                            break;
                    }

                    OnPropertyChanged("FalconType", oldValue, value, true);
                    InvalidateStatusReport();
                }
            }
        }

        public FalconKeyFile KeyFile
        {
            get { return _callbacks; }
        }

        public string KeyFileName
        {
            get
            {
                if(_keyFile != null)
                {
                    return _keyFile;
                }
                else
                {
                    return null;
                }
            }
            set
            {
                if ((_keyFile == null && value != null)
                    || (_keyFile != null && value != null))
                {
                    string oldValue = _keyFile;
                    FalconKeyFile oldKeyFile = _callbacks;
                    _keyFile = value;
                    _callbacks = new FalconKeyFile(_keyFile);
                    OnPropertyChanged("KeyFileName", oldValue, value, true);
                    OnPropertyChanged("KeyFile", oldKeyFile, _callbacks, false);
                    InvalidateStatusReport();
                }
            }
        }


        public string CockpitDatFile
        {
            get
            {
                return _cockpitDatFile;
            }
            set
            {
                if ((_cockpitDatFile == null && value != null)
                    || (_cockpitDatFile != null && !_cockpitDatFile.Equals(value)))
                {
                    string oldValue = _cockpitDatFile;
                    _cockpitDatFile = value;
                    OnPropertyChanged("CockpitDatFile", oldValue, value, true);
                }
            }
        }

        public string FalconPath
        {
            get
            {
                return _falconPath;
            }
            set
            {
                if (_falconPath == null && value != null ||
                    _falconPath != null && !_falconPath.Equals(value))
                {
                    string oldValue = _falconPath;
                    _falconPath = value;
                    OnPropertyChanged("FalconPath", oldValue, value, false);
                }
            }
        }

        internal RadarContact[] RadarContacts => _dataExporter?.RadarContacts;
        public string[] RwrInfo => _dataExporter?.RwrInfo;

        #endregion

        public static Version ParseProfileVersion(string versionString)
        {
            Version ver = Version.Parse(System.Text.RegularExpressions.Regex.Replace(versionString, "[A-Za-z ]", ""));
            return ver;
        }

        public string[] GetFalconVersions()
        {
            string[] subkeys = null;
            if(Registry.LocalMachine.OpenSubKey(falconRootKey) != null)
            {
                subkeys = Registry.LocalMachine.OpenSubKey(falconRootKey).GetSubKeyNames();
                Array.Reverse(subkeys);
            }
            return subkeys;
        }     
        public string GetFalconPath()
        {
            RegistryKey pathKey = null;
            string pathValue = null;
            pathKey = Registry.LocalMachine.OpenSubKey(falconRootKey + FalconVersion);

            if (pathKey != null)
            {
                pathValue = (string)pathKey.GetValue("baseDir");
            }
            else
            {
                pathValue = "";
            }
            return pathValue;
        }
        public string GetCurrentTheater()
        {
            RegistryKey pathKey = null;
            string pathValue = null;
            pathKey = Registry.LocalMachine.OpenSubKey(falconRootKey + FalconVersion);

            if (pathKey != null)
            {
                pathValue = (string)pathKey.GetValue("curTheater");
            }
            else
            {
                pathValue = "";
            }
            return pathValue;
        }
        public string GetpilotCallsign()
        {
            string callsign = "";
            RegistryKey pathKey = Registry.LocalMachine.OpenSubKey(falconRootKey + FalconVersion);

            if (pathKey != null)
            {
                try
                {
                    callsign = System.Text.Encoding.UTF8.GetString((byte[])pathKey.GetValue("PilotCallsign")).Replace("\0", "");
                }
                catch { }
            }
            return callsign;
        }
        private void SetPilotOptions()
        {
            var popFile = Path.Combine(FalconPath,"User","Config",PilotCallsign + ".pop");
            var backupDir = Path.Combine(FalconPath,"User","Config","Helios");
            var backupPopFile = Path.Combine(backupDir,PilotCallsign + ".pop");

            if (File.Exists(popFile))
            {
                if (!File.Exists(backupPopFile))
                {
                    if (!Directory.Exists(backupDir))
                    {
                        _ = Directory.CreateDirectory(backupDir);
                    }
                    File.Copy(popFile, backupPopFile, true);
                    Logger.Debug("File " + Path.GetFileName(popFile) + " has been backed up to " + backupDir);
                }
                //}

                File.SetAttributes(popFile, File.GetAttributes(popFile) & ~FileAttributes.ReadOnly);

                FileStream fileStream = new FileStream(popFile, FileMode.Open, FileAccess.Read);
                byte[] bytes = new byte[fileStream.Length];
                _ = fileStream.Read(bytes, 0, bytes.Length);
                fileStream.Close();


                byte[] keyFileName = System.Text.Encoding.ASCII.GetBytes(Path.GetFileName(KeyFileName).Replace(".key", ""));
                for (int i = 0; i <= 15; i++)
                {
                    if (i >= keyFileName.Length)
                    {
                        bytes[336 + i] = 0x00;
                        continue;
                    }
                    bytes[336 + i] = keyFileName[i];
                }

                fileStream = new FileStream
                    (popFile, FileMode.Create, FileAccess.Write);
                fileStream.Write(bytes, 0, bytes.Length);
                fileStream.Close();
                Logger.Debug(popFile + " has been modified to load key file " + Path.GetFileName(KeyFileName) + " by default");
            }
            else
            {
                Logger.Error("FILE NOT FOUND! " + popFile + " Failed to force key file usage in Falcon");
            }
        }
        public BindingValue GetValue(string device, string name)
        {
            return _dataExporter?.GetValue(device, name) ?? BindingValue.Empty;
        }

        protected override void OnProfileChanged(HeliosProfile oldProfile)
        {
            base.OnProfileChanged(oldProfile);

            if (oldProfile != null)
            {
                oldProfile.ProfileStarted -= new EventHandler(Profile_ProfileStarted);
                oldProfile.ProfileTick -= new EventHandler(Profile_ProfileTick);
                oldProfile.ProfileStopped -= new EventHandler(Profile_ProfileStopped);
            }

            if (Profile != null)
            {
                Profile.ProfileStarted += new EventHandler(Profile_ProfileStarted);
                Profile.ProfileTick += new EventHandler(Profile_ProfileTick);
                Profile.ProfileStopped += new EventHandler(Profile_ProfileStopped);
            }
            InvalidateStatusReport();
        }
        void Profile_ProfileStopped(object sender, EventArgs e)
        {
            _dataExporter?.CloseData();
        }

        void Profile_ProfileTick(object sender, EventArgs e)
        {
            _dataExporter?.PollData();
        }

        void Profile_ProfileStarted(object sender, EventArgs e)
        {
            /*
             * Check to see if we need to rewrite pilot options file
             */
            if (ForceKeyFile)
            {
                if(PilotCallsign != "")
                {
                    Logger.Info("Profile has set pilot callsign " + PilotCallsign + " to use key file " + KeyFileName);
                    SetPilotOptions();
                }
                else
                {
                    Logger.Warn("Profile is set to force key file usage but the pilot callsign is not set in Falcon install");
                }
            }
            _dataExporter?.InitData();
        }

        void PressAction_Execute(object action, HeliosActionEventArgs e)
        {
            if (_callbacks.HasCallback(e.Value.StringValue))
            {
                WindowFocused(_falconType);
                _callbacks[e.Value.StringValue].Down();
            }
        }

        void ReleaseAction_Execute(object action, HeliosActionEventArgs e)
        {
            if (_callbacks.HasCallback(e.Value.StringValue))
            {
                WindowFocused(_falconType);
                _callbacks[e.Value.StringValue].Up();
            }
        }

        void SendAction_Execute(object action, HeliosActionEventArgs e)
        {
            if (_callbacks.HasCallback(e.Value.StringValue))
            {
                WindowFocused(_falconType);
                _callbacks[e.Value.StringValue].Press();
            }
        }
        
        void WindowFocused(FalconTypes type)
        {
            if(type == FalconTypes.BMS && _focusAssist)
            {
                Process[] bms = Process.GetProcessesByName("Falcon BMS");
                if(bms.Length == 1)
                {
                    IntPtr hWnd = bms[0].MainWindowHandle;
                   SetForegroundWindow(hWnd);
                }
            }
        }

        [DllImport("user32.dll")]
        static extern bool SetForegroundWindow(IntPtr hWnd);

        public override void ReadXml(XmlReader reader)
        {

            while (reader.NodeType == XmlNodeType.Element)
            {
                switch (reader.Name)
                {
                    case "FalconType":
                        FalconType = (FalconTypes)Enum.Parse(typeof(FalconTypes), reader.ReadElementString("FalconType"));
                        break;
                    case "KeyFile":
                        KeyFileName = reader.ReadElementString("KeyFile");
                        break;
                    case "CockpitDatFile":
                        CockpitDatFile = reader.ReadElementString("CockpitDatFile");
                        break;
                    case "FocusAssist":
                        FocusAssist = Convert.ToBoolean(reader.ReadElementString("FocusAssist"));
                        break;
                    case "ForceKeyFile":
                        ForceKeyFile = Convert.ToBoolean(reader.ReadElementString("ForceKeyFile"));
                        break;
                    case "FalconVersion":
                        FalconVersion = reader.ReadElementString("FalconVersion");
                        break;
                    default:
                        // ignore unsupported settings
                        string elementName = reader.Name;
                        string discard = reader.ReadElementString(reader.Name);
                        Logger.Warn($"Ignored unsupported {GetType().Name} setting '{elementName}' with value '{discard}'");
                        break;
                }
            }
        }

        public override void WriteXml(XmlWriter writer)
        {
            writer.WriteElementString("FalconType", FalconType.ToString());
            writer.WriteElementString("FalconVersion", FalconVersion.ToString());
            writer.WriteElementString("KeyFile", KeyFileName);
            //writer.WriteElementString("CockpitDatFile", CockpitDatFile);
            writer.WriteElementString("FocusAssist", FocusAssist.ToString());
            writer.WriteElementString("ForceKeyFile", ForceKeyFile.ToString());
        }

        

        #region IReadyCheck
        public IEnumerable<StatusReportItem> PerformReadyCheck()
        {
            // XXX perform integrity check.  any warnings will light caution on Control Center
            // XXX any error will block control center from starting profile unless user selects to disable ready check
            yield return new StatusReportItem
            {
                Status = $"Selected Falcon interface driver is '{FalconType}' version '{FalconVersion}'",
                Severity = StatusReportItem.SeverityCode.Info,
                Flags = StatusReportItem.StatusFlags.ConfigurationUpToDate
            };

            if (KeyFileName != null)
            {
                if (!System.IO.File.Exists(KeyFileName))
                {
                    yield return new StatusReportItem
                    {
                        Status = $"The key file configured in this profile does not exist at the path specified '{KeyFileName}'",
                        Recommendation = "Configure this interface with a valid key file",
                        Severity = StatusReportItem.SeverityCode.Error,
                    };
                }
                else
                {
                    yield return new StatusReportItem
                    {
                        Status = $"The key file configured in this profile is '{KeyFileName}'\n",
                        Severity = StatusReportItem.SeverityCode.Info,
                        Flags = StatusReportItem.StatusFlags.ConfigurationUpToDate
                    };
                }
            }
            else
            {
                yield return new StatusReportItem
                {
                    Status = $"Key file not defined",
                    Recommendation = "Please configure this interface with a valid key file if this profile is designed to interact with Falcon",
                    Severity = StatusReportItem.SeverityCode.Warning,
                    Flags = StatusReportItem.StatusFlags.ConfigurationUpToDate
                };
            }
            if (PilotCallsign.Equals(""))
            {
                yield return new StatusReportItem
                {
                    Status = $"Pilot Callsign not set in BMS",
                    Severity = StatusReportItem.SeverityCode.Error,
                    Recommendation = "Run Falcon and set your pilot callsign"
                };
            }
        }

        public HeliosBindingCollection CheckBindings(HeliosBindingCollection heliosBindings)
        {
            HeliosBindingCollection missingCallbackBindings = new HeliosBindingCollection();
            foreach (HeliosBinding binding in heliosBindings)
            {
                if (binding.Value != "" && !_callbacks.HasCallback(binding.Value) && binding.ValueSource.ToString().Equals("StaticValue"))
                {
                    missingCallbackBindings.Add(binding);

                }
            }
            return missingCallbackBindings;
        }

        public IEnumerable<StatusReportItem> ReportBindings(HeliosBindingCollection bindings)
        {
            foreach (HeliosBinding binding in bindings)
            {
                yield return new StatusReportItem
                {
                    Status = $"callback bound in the profile is not found in the key file '{binding.Value}'",
                    Recommendation = $"Add missing callbacks to your key file.",
                    Severity = StatusReportItem.SeverityCode.Error,
                    Flags = StatusReportItem.StatusFlags.DoNotDisturb | StatusReportItem.StatusFlags.Verbose
                };
            }
        }
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

        public void PublishStatusReport(IList<StatusReportItem> statusReport)
        {
            foreach (IStatusReportObserver observer in _observers)
            {
                observer.ReceiveStatusReport(Name, Description, statusReport);
            }
        }

        public void InvalidateStatusReport()
        {
            List<StatusReportItem> newReport = new List<StatusReportItem>();
            newReport.AddRange(PerformReadyCheck());
            if(_callbacks.IsParsed)
            {
                newReport.AddRange(ReportBindings(CheckBindings(InputBindings)));
            }
            PublishStatusReport(newReport);
        }
        #endregion
        
        #region IExtendedDescription
        public string Description => $"Interface to {FalconType}";
        public string RemovalNarrative => $"Delete this interface and remove all of its bindings from the Profile";

        #endregion
    }
}
